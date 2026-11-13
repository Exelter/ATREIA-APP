using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Combat.Sources;
using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Linux.Meter;

/// <summary>
/// The meter without its window: the Linux app's counterpart of the meter part of the Windows app's main window (Client/Ui/MainWindow.xaml.cs - PollTick,
/// ApplyBatch, RefreshRows, RankRows, FollowNewestRun, the boss line of MainWindow.BossBar.cs). The same rules, ported method by method under the same
/// names, so a change there is found here: once a second the capture is polled; what is counted (instances, boss fights, dummies and PvP; the open world
/// only with its switch) goes into the fight; the fight starts again after a quiet spell or a boss's next attempt; the lines are worked out for the mode
/// (damage, healing, damage taken) and the scope (all, the group, the corps, me), and the meter follows the newest boss fight by itself, as the Windows
/// meter over the game does. Runs on the window thread; the capture reports from its own.
/// </summary>
public sealed class MeterEngine : IDisposable
{
    public enum MeterMode { Damage, Heal, Taken }

    /// <summary>Whose lines are shown: everybody, the own group, the corps (two groups together), or the own character alone.</summary>
    public enum MeterScope { All, Group, Corps, Me }

    /// <summary>Where the player is, for the automatic choice of the scope: in an instance or arena everybody, at a training dummy the own character.</summary>
    private enum ScopeContext { World, Instance, Dummy }

    /// <summary>The most lines the meter shows (the Windows meter over the game: "cap the max users on meter to 10"); the own line takes the last place
    /// when it is further down.</summary>
    public const int MaxLines = 10;

    private readonly LiveAggregator _aggregator = new();
    private readonly List<AvoidEvent> _avoids = new();
    private readonly List<KillEvent> _kills = new();
    private readonly List<PlayerRow> _rows = new();
    private readonly Dictionary<int, PlayerRow> _rowsByObjectId = new();
    private readonly HashSet<string> _groupNames = new(StringComparer.Ordinal);
    private HashSet<int> _corpsIds = new();
    private readonly Dictionary<int, (DateTime First, DateTime Last)> _bossActivity = new();
    private readonly Dictionary<int, bool> _dummyTargets = new();

    /// <summary>The bosses (and, in a later PvP view, players) fought since the last clear: the Windows meter's Mob/Boss list.</summary>
    private readonly List<(int TargetId, string Name)> _mobBossEntries = new();

    private ICombatSource? _source;
    private SourceStatus _status = new(SourceState.Idle, "");
    private readonly object _statusGate = new();

    private int? _selectedTargetId;
    private DateTime? _selectedRunWindowStart;
    private DateTime? _selectedRunWindowEnd;
    private IReadOnlyList<DamageEvent> _shownHits = Array.Empty<DamageEvent>();
    private (DateTime First, DateTime Last)? _shownSpan;

    private bool _paused;
    private MeterMode _mode;
    private MeterScope _scope = MeterScope.Group;
    private MeterScope? _scopeManual;
    private bool _autoScope = true;
    private ScopeContext _scopeContext = ScopeContext.World;
    private bool _autoReset;
    private TimeSpan _autoResetIdle = TimeSpan.FromSeconds(10);
    private bool _trackOpenWorld;
    private bool _showDeaths = true;
    private bool _showShareBars = true;

    private DateTime _lastDummyHit = DateTime.MinValue;
    private DateTime _lastBossFight = DateTime.MinValue;
    private DateTime _lastPvpFight = DateTime.MinValue;
    private DateTime _lastOwnCombat = DateTime.MinValue;
    private DateTime _lastOwnBossFight = DateTime.MinValue;

    /// <summary>How long after the last hit on or by a boss the open world stays counted.</summary>
    private static readonly TimeSpan BossFightHold = TimeSpan.FromSeconds(30);

    /// <summary>A pause this long between hits on a boss starts a new stretch of it.</summary>
    private static readonly TimeSpan BossQuietBeforeUpload = TimeSpan.FromSeconds(20);

    /// <summary>Gap between hits past which two hits on the same target are two runs (the Windows meter's RunClusterGapSeconds).</summary>
    private const double RunClusterGapSeconds = 120;

    /// <summary>The lines changed (after a tick, a clear, a mode or scope change).</summary>
    public event Action? Changed;

    /// <summary>A note for the status line (the open world not counted, ...): raised on the window thread.</summary>
    public event Action<string>? Note;

    private DateTime _notCountedSaid = DateTime.MinValue;

    public MeterMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            RefreshRows();
        }
    }

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>The open world is counted too (off: only instances, boss fights, dummies and PvP, as on Windows).</summary>
    public bool TrackOpenWorld
    {
        get => _trackOpenWorld;
        set => _trackOpenWorld = value;
    }

    /// <summary>Whose lines are shown now: the pick for this place, else the automatic one (instance: all, dummy: you), else the saved choice.</summary>
    public MeterScope ShownScope => _scopeManual ?? (!_autoScope ? _scope : _scopeContext switch
    {
        ScopeContext.Instance => MeterScope.All,
        ScopeContext.Dummy => MeterScope.Me,
        _ => _scope,
    });

    /// <summary>The capture's last word on its state.</summary>
    public SourceStatus Status
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    /// <summary>The game's server has been seen: the meter is reading the game.</summary>
    public bool Capturing => (_source as Aion2PacketCombatSource)?.ServerFingerprint is not null || _source is not Aion2PacketCombatSource && _source is not null;

    /// <summary>The source the meter reads (the capture, or a sample).</summary>
    public ICombatSource? Source => _source;

    private Aion2EntityDirectory? GameDirectory => _source?.Entities as Aion2EntityDirectory;

    /// <summary>The lines in the order shown, at most <see cref="MaxLines"/> (the own line kept), each with its rank and share among all of them.</summary>
    public IReadOnlyList<PlayerRow> ShownRows { get; private set; } = Array.Empty<PlayerRow>();

    /// <summary>Every line of the fight, best first.</summary>
    public IReadOnlyList<PlayerRow> AllRows { get; private set; } = Array.Empty<PlayerRow>();

    /// <summary>What the lines are of: the boss (and its run) followed, or every target.</summary>
    public string TargetText => _selectedTargetId is int id ? ResolveDisplayName(id) : LocalizationManager.Instance["Main.FilterAllTargets"];

    /// <summary>The fight's time on screen, first to last hit shown ("1:51"); empty without a fight.</summary>
    public string TimeText => _shownSpan is (DateTime first, DateTime last) && last > first ? (last - first).ToString(@"m\:ss") : "";

    /// <summary>The group's rate in big: the sum of the lines' rates (DPS, HPS or DTPS by the mode).</summary>
    public double TotalRate => _rows.Where(r => !r.IsEnemy).Sum(r => r.Dps ?? 0);

    public string Unit => _mode switch { MeterMode.Heal => "HPS", MeterMode.Taken => "DTPS", _ => "DPS" };

    /// <summary>The own character as the game named it, and its combat power ("196.5K"), for the meter's head.</summary>
    public (string? Name, string Power) Own
    {
        get
        {
            if (GameDirectory is not { } dir)
            {
                int me = _source?.Entities.LocalPlayerId ?? -1;
                int? samplePower = (_source?.Entities as ISampleDirectory)?.PowerOf(me);
                return (_source?.Entities.NameFor(me), samplePower is int sp and > 0 ? PlayerRow.Compact(sp) + " CP" : "");
            }

            string? name = dir.LocalCharacter?.Name ?? (dir.LocalPlayerId >= 0 ? dir.NameFor(dir.LocalPlayerId) : null);
            int? power = dir.LocalPlayerId >= 0 ? dir.SeenProfileOf(dir.LocalPlayerId)?.CombatPower : null;
            if (power is not > 0)
            {
                power = OwnCombatPower(dir);
            }

            return (name, power is int cp and > 0 ? PlayerRow.Compact(cp) + " CP" : "");
        }
    }

    /// <summary>The own character's class ("Gladiator"), null while unknown: its emblem goes in the meter's head.</summary>
    public string? OwnClass
    {
        get
        {
            int me = GameDirectory is { } dir ? (dir.LocalPlayerId >= 0 ? dir.LocalPlayerId : dir.InferLocalPlayer() ?? -1) : _source?.Entities.LocalPlayerId ?? -1;
            if (me < 0)
            {
                return null;
            }

            string name = ResolveClassName(me);
            return name == "?" ? null : name;
        }
    }

    /// <summary>
    /// Starts reading: the game's traffic through libpcap (<see cref="Aion2PacketCombatSource"/>, as the Windows app's StartCapture), or the source
    /// given (a sample for the pictures and checks).
    /// </summary>
    public void Start(MeterSettings settings, ICombatSource? source = null)
    {
        ApplySettings(settings);
        Aion2.Capture.DebugRecording.Enabled = settings.DebugRecording;
        Aion2.Capture.LoginCapture.Enabled = settings.ShareLoginWithLegion;
        _source?.Dispose();
        if (source is null)
        {
            var live = new Aion2PacketCombatSource(Aion2Protocol.Load(), settings.CaptureAdapterId, null, Aion2CharacterStore.DefaultPath);
            if (live.Entities is Aion2EntityDirectory entities)
            {
                // the characters of the login's list stay known on this PC; they also name the local player mid-session (Windows: StartCapture)
                entities.SetKnownCharacters(Aion2RememberedCharacters.Load().Select(r => (r.Name, r.ClassCode, r.ServerId)));
                entities.AccountCharactersNoted += characters => Aion2RememberedCharacters.Note(characters);
                // a map change announces the own character again under another combat id: one player, one line (marshalled: the capture's thread)
                entities.LocalIdChanged += (oldId, newId) => Avalonia.Threading.Dispatcher.UIThread.Post(() => MergeIds(oldId, newId));
                entities.IdentityMoved += (oldId, newId) => Avalonia.Threading.Dispatcher.UIThread.Post(() => MergeIds(oldId, newId));
            }

            source = live;
        }

        _source = source;
        source.StatusChanged += status =>
        {
            lock (_statusGate)
            {
                _status = status;
            }
        };
        source.Start();
    }

    /// <summary>The settings the meter goes by (Windows: RefreshCharacterSettings).</summary>
    public void ApplySettings(MeterSettings settings)
    {
        _showShareBars = settings.ShowShareBars;
        _showDeaths = settings.ShowDeaths;
        _autoReset = settings.AutoResetEnabled;
        _autoResetIdle = TimeSpan.FromSeconds(Math.Clamp(settings.AutoResetSeconds, 1, 600));
        _scope = Enum.TryParse(settings.ViewScope, true, out MeterScope scope) ? scope : MeterScope.Group;
        _autoScope = settings.AutoScope;
        _trackOpenWorld = settings.TrackOpenWorld;
        _mode = settings.LastMode switch { "Heal" => MeterMode.Heal, "Taken" => MeterMode.Taken, _ => MeterMode.Damage };
    }

    private void MergeIds(int oldId, int newId)
    {
        _aggregator.MergeIds(oldId, newId);
        if (_selectedTargetId == oldId)
        {
            _selectedTargetId = newId;
        }
    }

    /// <summary>The meter's tick, once a second (Windows: PollTick, the parts of the meter).</summary>
    public void Tick()
    {
        CombatBatch captured = _source?.Poll(_paused) ?? CombatBatch.Empty;
        NoteOwnCombat(captured.Damage);
        if (_source is Aion2PacketCombatSource packets && GameDirectory is { } lootDirectory)
        {
            // the deaths the stream told of: the loot tool's kills (counted or not)
            IReadOnlyList<(int Entity, DateTime At)> deaths = packets.DrainDeaths();
            IReadOnlyList<KillEvent> kills = deaths.Count == 0 ? captured.Kills
                : captured.Kills.Concat(deaths.Select(d => new KillEvent(d.At, null, d.Entity, lootDirectory.IsPlayer(d.Entity)))).ToList();
            Aion2Loot.NoteCombat(captured.Damage, kills, lootDirectory.IsLocalPlayer, lootDirectory.NpcIdOf);
        }

        CombatBatch batch = Counted(captured);
        UpdateScopeContext();

        // Hits a summon dealt before its owner was known go to the owner once found, instead of staying on a "Player #id" line.
        if (GameDirectory is { } resolvedIn)
        {
            var resolved = resolvedIn.DrainResolvedOwners();
            foreach ((int summon, int owner) in resolved)
            {
                _aggregator.Reattribute(summon, owner);
            }

            if (resolved.Count > 0 && batch.Damage.Count > 0)
            {
                var ownerOf = new Dictionary<int, int>();
                foreach ((int summon, int owner) in resolved)
                {
                    ownerOf[summon] = owner;
                }

                batch = batch with
                {
                    Damage = batch.Damage.Select(ev => ownerOf.TryGetValue(ev.SourceObjectId, out int owner) ? ev with { SourceObjectId = owner } : ev).ToList(),
                };
            }
        }

        if (batch.ZoneChanges.Count > 0)
        {
            // A map change cuts the stay in two: what happened before it still belongs to the old fight, which is then cleared before the rest comes in.
            IReadOnlyList<DamageEvent> damage = batch.Damage;
            int from = 0;
            foreach (ZoneChange zone in batch.ZoneChanges)
            {
                int to = from;
                while (to < damage.Count && damage[to].Timestamp < zone.Timestamp)
                {
                    to++;
                }

                ApplyBatch(new CombatBatch(damage.Skip(from).Take(to - from).ToList(), Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>()));
                Clear();
                from = to;
            }

            ApplyBatch(new CombatBatch(damage.Skip(from).ToList(), batch.Avoids, batch.Kills));
        }
        else
        {
            ApplyBatch(batch);
        }

        Changed?.Invoke();
    }

    /// <summary>What the meter does with one batch: kills and avoids, boss tracking, the automatic reset between fights, then the events (Windows: ApplyBatch).</summary>
    private void ApplyBatch(CombatBatch batch)
    {
        _avoids.AddRange(batch.Avoids);
        _kills.AddRange(batch.Kills);
        IReadOnlyList<DamageEvent> events = batch.Damage;
        NoteBossActivity(events);

        DateTime? keepFrom = null;
        bool fresh = _autoReset && StartsNewFight(events);
        if (!fresh && NextBossAttempt(events, afterWipeOnly: !_autoReset) is DateTime attempt)
        {
            fresh = true;
            keepFrom = attempt;
        }

        if (fresh)
        {
            // the next attempt's first hits can be on record already (they may come just before the game says the boss is in combat again): kept
            var kept = keepFrom is DateTime from ? _aggregator.Events.Where(ev => ev.Timestamp >= from).ToList() : null;
            var keptAvoids = keepFrom is DateTime avoidFrom ? _avoids.Where(a => a.Timestamp >= avoidFrom).ToList() : null;
            var keptKills = keepFrom is DateTime killFrom ? _kills.Where(k => k.Timestamp >= killFrom).ToList() : null;
            Clear();
            if (kept is { Count: > 0 })
            {
                _aggregator.IngestEvents(kept);
            }

            _avoids.AddRange(keptAvoids ?? new List<AvoidEvent>());
            _kills.AddRange(keptKills ?? new List<KillEvent>());
        }

        if (events.Count > 0 || batch.Avoids.Count > 0 || batch.Kills.Count > 0)
        {
            if (events.Count > 0)
            {
                _aggregator.IngestEvents(events);
            }

            RefreshMobBossFilterItems();
            // nobody picks a target on the meter over the game: it follows the newest fight
            if (events.Count > 0)
            {
                FollowNewestRun();
            }

            RefreshRows();
        }
    }

    /// <summary>Starts from zero (the Clear button, a map change, a new fight).</summary>
    public void Clear()
    {
        _aggregator.Clear();
        _groupNames.Clear();
        _avoids.Clear();
        _kills.Clear();
        _rows.Clear();
        _rowsByObjectId.Clear();
        _selectedTargetId = null;
        _selectedRunWindowStart = null;
        _selectedRunWindowEnd = null;
        _mobBossEntries.Clear();
        _shownHits = Array.Empty<DamageEvent>();
        _shownSpan = null;
        ShownRows = Array.Empty<PlayerRow>();
        AllRows = Array.Empty<PlayerRow>();
        Changed?.Invoke();
    }

    /// <summary>The scope switch: in an instance or at a dummy the click holds for this place only; in the open world it is the saved choice.</summary>
    public void SetScope(MeterScope scope)
    {
        if (_autoScope && _scopeContext != ScopeContext.World)
        {
            _scopeManual = scope;
        }
        else
        {
            _scope = scope;
            MeterSettings settings = MeterSettings.Load();
            settings.ViewScope = scope.ToString();
            settings.Save();
        }

        RefreshRows();
        Changed?.Invoke();
    }

    // ---- what is counted: dungeons, arenas, boss fights and training dummies; the open world only when its switch is on ----

    private bool CountingNow()
    {
        if (_trackOpenWorld || _source is not Aion2PacketCombatSource source || source.CurrentZoneId <= 0)
        {
            return true;
        }

        DateTime now = DateTime.Now;
        return InInstanceNow() || now - _lastBossFight < BossFightHold || now - _lastPvpFight < BossFightHold || now - _lastDummyHit < TimeSpan.FromSeconds(20);
    }

    /// <summary>A poll's events as the meter counts them: in the open world without a boss or a dummy (and the switch off) none, the zone changes kept.</summary>
    private CombatBatch Counted(CombatBatch batch)
    {
        if (CountingNow() || (batch.Damage.Count == 0 && batch.Avoids.Count == 0 && batch.Kills.Count == 0))
        {
            return batch;
        }

        if (DateTime.Now - _notCountedSaid > TimeSpan.FromMinutes(2) && batch.Damage.Count > 0)
        {
            _notCountedSaid = DateTime.Now;
            Note?.Invoke(LocalizationManager.Instance["Main.OpenWorld.NotCounted"]);
        }

        return batch with { Damage = Array.Empty<DamageEvent>(), Avoids = Array.Empty<AvoidEvent>(), Kills = Array.Empty<KillEvent>() };
    }

    private bool InInstanceNow() =>
        _source is Aion2PacketCombatSource source && source.CurrentZoneId is > 0 and var zone && (Aion2Zones.IsInstance(zone) || Aion2Zones.IsArena(zone));

    private void UpdateScopeContext()
    {
        bool atDummy = DateTime.Now - _lastDummyHit < TimeSpan.FromSeconds(20);
        ScopeContext context = atDummy ? ScopeContext.Dummy : InInstanceNow() ? ScopeContext.Instance : ScopeContext.World;
        if (context == _scopeContext)
        {
            return;
        }

        _scopeContext = context;
        _scopeManual = null;
        RefreshRows();
    }

    /// <summary>Notes a poll's own fighting (Windows: NoteOwnCombat): dummies, boss fights nearby, PvP. True when the own character fought.</summary>
    private bool NoteOwnCombat(IReadOnlyList<DamageEvent> events)
    {
        if (events.Count == 0 || GameDirectory is not { } directory)
        {
            return false;
        }

        int local = directory.LocalPlayerId >= 0 ? directory.LocalPlayerId : directory.InferLocalPlayer() ?? -1;
        if (local >= 0)
        {
            foreach (DamageEvent ev in events)
            {
                if (!ev.IsHeal && ev.SourceObjectId == local && IsDummyTarget(directory, ev.TargetObjectId))
                {
                    _lastDummyHit = DateTime.Now;
                    break;
                }
            }
        }

        foreach (DamageEvent ev in events)
        {
            if (!ev.IsHeal && (directory.BossNpcIdOf(ev.TargetObjectId) is not null || directory.BossNpcIdOf(ev.SourceObjectId) is not null))
            {
                _lastBossFight = DateTime.Now;
                break;
            }
        }

        if (local >= 0)
        {
            foreach (DamageEvent ev in events)
            {
                if (!ev.IsHeal && ((ev.SourceObjectId == local && directory.BossNpcIdOf(ev.TargetObjectId) is not null)
                    || (ev.TargetObjectId == local && directory.BossNpcIdOf(ev.SourceObjectId) is not null)))
                {
                    _lastOwnBossFight = DateTime.Now;
                    break;
                }
            }
        }

        if (directory.LocalPlayerId is int me and >= 0)
        {
            foreach (DamageEvent ev in events)
            {
                if (!ev.IsHeal && ev.SourceObjectId != ev.TargetObjectId
                    && ((ev.SourceObjectId == me && directory.IsKnownPlayer(ev.TargetObjectId)) || (ev.TargetObjectId == me && directory.IsKnownPlayer(ev.SourceObjectId))))
                {
                    _lastPvpFight = DateTime.Now;
                    break;
                }
            }
        }

        local = directory.LocalPlayerId;
        foreach (DamageEvent ev in events)
        {
            if (ev.IsHeal)
            {
                continue;
            }

            if (local >= 0 ? ev.SourceObjectId == local || ev.TargetObjectId == local
                : directory.IsKnownPlayer(ev.SourceObjectId) || (!directory.IsKnownMonster(ev.SourceObjectId) && !directory.IsKnownPlayer(ev.TargetObjectId)))
            {
                _lastOwnCombat = DateTime.Now;
                return true;
            }
        }

        return false;
    }

    /// <summary>The own character fought in the last <paramref name="within"/> (the meter folds away between fights by it).</summary>
    public bool FoughtWithin(TimeSpan within) => DateTime.Now - _lastOwnCombat < within || DateTime.Now - _lastOwnBossFight < within;

    private bool IsDummyTarget(Aion2EntityDirectory directory, int targetId)
    {
        if (_dummyTargets.TryGetValue(targetId, out bool known))
        {
            return known;
        }

        if (_dummyTargets.Count > 2000)
        {
            _dummyTargets.Clear();
        }

        string? name = directory.NameFor(targetId) ?? directory.MonsterNameFor(targetId);
        bool dummy = DummyTargets.IsDummy(name);
        if (name is not null)
        {
            _dummyTargets[targetId] = dummy;
        }

        return dummy;
    }

    // ---- the fight's start and the boss's attempts ----

    /// <summary>Whether this batch's first damage comes the idle time or more after the last damage on record, and no boss fight is unfinished.</summary>
    private bool StartsNewFight(IReadOnlyList<DamageEvent> batch)
    {
        DateTime? first = null;
        foreach (DamageEvent ev in batch)
        {
            if (!ev.IsHeal && (first is null || ev.Timestamp < first))
            {
                first = ev.Timestamp;
            }
        }

        if (first is not DateTime start)
        {
            return false;
        }

        IReadOnlyList<DamageEvent> recorded = _aggregator.Events;
        DateTime? last = null;
        for (int i = recorded.Count - 1; i >= 0; i--)
        {
            if (!recorded[i].IsHeal)
            {
                last = recorded[i].Timestamp;
                break;
            }
        }

        return last is DateTime end && start - end >= _autoResetIdle && !BossFightUnfinished(now: start);
    }

    private DateTime? NextBossAttempt(IReadOnlyList<DamageEvent> batch, bool afterWipeOnly) =>
        GameDirectory is { } directory ? BossFight.NextAttemptStart(batch, _aggregator.Events, directory, afterWipeOnly) : null;

    private bool BossFightUnfinished(int? except = null, DateTime? now = null) =>
        GameDirectory is { } directory && BossFight.Unfinished(directory, _aggregator.Events, except, now);

    private void NoteBossActivity(IReadOnlyList<DamageEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        foreach (DamageEvent e in events)
        {
            if (e.IsHeal || !IsKnownBoss(e.TargetObjectId))
            {
                continue;
            }

            _bossActivity[e.TargetObjectId] = _bossActivity.TryGetValue(e.TargetObjectId, out var a)
                && e.Timestamp - a.Last < BossQuietBeforeUpload + BossQuietBeforeUpload
                ? (a.First, e.Timestamp > a.Last ? e.Timestamp : a.Last)
                : (e.Timestamp, e.Timestamp);
        }
    }

    // ---- the targets fought, and which one the meter follows ----

    /// <summary>A boss of the catalog (Windows: IsKnownBoss); a sample source says it itself.</summary>
    private bool IsKnownBoss(int targetId) =>
        GameDirectory is { } directory ? directory.BossNpcIdOf(targetId) is not null : _source?.Entities is ISampleDirectory sample && sample.IsBoss(targetId);

    /// <summary>Adds the newly fought bosses to the list of targets (never removes - only a clear does).</summary>
    private void RefreshMobBossFilterItems()
    {
        var knownIds = _mobBossEntries.Select(entry => entry.TargetId).ToHashSet();
        foreach (int targetId in _aggregator.Events.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct().ToList())
        {
            if (knownIds.Contains(targetId) || IsPlayerName(targetId) || !IsKnownBoss(targetId))
            {
                continue;
            }

            _mobBossEntries.Add((targetId, ResolveDisplayName(targetId)));
        }
    }

    /// <summary>One entry of the list: a target's whole history, or one numbered run of it (a boss farmed again, or tried again after a wipe).</summary>
    private readonly record struct MobBossTag(int TargetId, DateTime? WindowStart, DateTime? WindowEnd);

    /// <summary>The runs of one target (Windows: MobBossRowsFor): split where the hits pause longer than the gap, or where the boss's next attempt began.</summary>
    private IEnumerable<(MobBossTag Tag, string Name, long Damage)> MobBossRowsFor(int targetId, string name)
    {
        var hits = _aggregator.Events.Where(ev => !ev.IsHeal && ev.TargetObjectId == targetId).OrderBy(ev => ev.Timestamp).ToList();
        if (hits.Count == 0)
        {
            yield return (new MobBossTag(targetId, null, null), name, 0);
            yield break;
        }

        Aion2EntityDirectory? directoryOfRuns = GameDirectory;
        DateTime runsNow = hits[^1].Timestamp > DateTime.Now ? hits[^1].Timestamp : DateTime.Now;
        IReadOnlyList<DateTime> wipes = directoryOfRuns is null ? Array.Empty<DateTime>()
            : directoryOfRuns.HitPoints.ResetsOf(targetId).Concat(directoryOfRuns.FailedEndsOf(targetId, runsNow)).ToList();
        IReadOnlyList<DateTime> resets = directoryOfRuns is null || directoryOfRuns.BossNpcIdOf(targetId) is null
            ? wipes
            : BossFight.AttemptStarts(directoryOfRuns, targetId, hits, runsNow);
        var clusters = new List<List<DamageEvent>>();
        var wiped = new List<bool>();
        foreach (DamageEvent hit in hits)
        {
            if (clusters.Count > 0)
            {
                DateTime previous = clusters[^1][^1].Timestamp;
                bool reset = resets.Any(at => at > previous && at <= hit.Timestamp);
                if (reset || (hit.Timestamp - previous).TotalSeconds > RunClusterGapSeconds)
                {
                    wiped[^1] = reset;
                    clusters.Add(new List<DamageEvent> { hit });
                    wiped.Add(false);
                    continue;
                }

                clusters[^1].Add(hit);
            }
            else
            {
                clusters.Add(new List<DamageEvent> { hit });
                wiped.Add(false);
            }
        }

        DateTime lastStart = clusters[^1][0].Timestamp;
        wiped[^1] = wipes.Any(at => at > lastStart);
        if (clusters.Count == 1 && !wiped[0])
        {
            yield return (new MobBossTag(targetId, null, null), name, clusters[0].Sum(e => e.Amount));
            yield break;
        }

        for (int i = 0; i < clusters.Count; i++)
        {
            List<DamageEvent> cluster = clusters[i];
            yield return (new MobBossTag(targetId, cluster[0].Timestamp, cluster[^1].Timestamp), $"{name} #{i + 1}{(wiped[i] ? " \U0001F480" : "")}", cluster.Sum(e => e.Amount));
        }
    }

    /// <summary>Keeps the meter on the newest fight of the list (Windows: FollowNewestRun): the run whose latest hit is the most recent, a boss staying on
    /// screen while its adds die around it.</summary>
    private void FollowNewestRun()
    {
        var entries = _mobBossEntries.SelectMany(entry => MobBossRowsFor(entry.TargetId, entry.Name)).Select(row => row.Tag).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var byTarget = new Dictionary<int, List<int>>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (!byTarget.TryGetValue(entries[i].TargetId, out List<int>? slots))
            {
                byTarget[entries[i].TargetId] = slots = new List<int>();
            }

            slots.Add(i);
        }

        var lastHits = new DateTime[entries.Count];
        int onlySource = ShownScope == MeterScope.Me && GameDirectory is { } directory
            ? (directory.LocalPlayerId >= 0 ? directory.LocalPlayerId : directory.InferLocalPlayer() ?? -1) : -1;
        foreach (DamageEvent ev in _aggregator.Events)
        {
            if (ev.IsHeal || (onlySource >= 0 && ev.SourceObjectId != onlySource) || !byTarget.TryGetValue(ev.TargetObjectId, out List<int>? slots))
            {
                continue;
            }

            foreach (int slot in slots)
            {
                MobBossTag tag = entries[slot];
                if (ev.Timestamp > lastHits[slot] && (tag.WindowStart is not DateTime from || (ev.Timestamp >= from && ev.Timestamp <= tag.WindowEnd)))
                {
                    lastHits[slot] = ev.Timestamp;
                }
            }
        }

        int newest = -1;
        DateTime newestHit = DateTime.MinValue;
        for (int i = 0; i < entries.Count; i++)
        {
            if (lastHits[i] > newestHit)
            {
                newestHit = lastHits[i];
                newest = i;
            }
        }

        if (newest >= 0)
        {
            MobBossTag picked = entries[newest];
            _selectedTargetId = picked.TargetId;
            _selectedRunWindowStart = picked.WindowStart;
            _selectedRunWindowEnd = picked.WindowEnd;
        }
    }

    // ---- the lines ----

    /// <summary>A player: an object the stream showed casting a class skill or whose "appeared" frame was read; a sample source says it itself.</summary>
    private bool IsPlayerName(int id) =>
        GameDirectory is { } entities ? entities.IsKnownPlayer(id) : _source?.Entities is ISampleDirectory sample && sample.IsPlayer(id);

    private string ResolveDisplayName(int objectId) =>
        _source?.Entities.NameFor(objectId) ?? GameDirectory?.MonsterNameFor(objectId) ?? $"0x{objectId:X8}";

    private string ResolveClassName(int sourceId) =>
        GameDirectory?.ClassOf(sourceId) ?? (_source?.Entities as ISampleDirectory)?.ClassOf(sourceId) ?? "?";

    private void ApplyIdentity(PlayerRow row, int sourceId)
    {
        row.Name = ResolveDisplayName(sourceId);
        row.ClassName = ResolveClassName(sourceId);
    }

    private bool IsInScope(int sourceId)
    {
        Aion2EntityDirectory? directory = GameDirectory;
        bool local = directory is null ? _source?.Entities.IsLocalPlayer(sourceId) == true : directory.IsLocalPlayer(sourceId) || directory.InferLocalPlayer() == sourceId;
        if (ShownScope == MeterScope.All || local)
        {
            return true;
        }

        if (ShownScope == MeterScope.Me)
        {
            return false;
        }

        bool groupKnown = RosterKnown(directory);
        if (ShownScope == MeterScope.Group && !groupKnown)
        {
            return _corpsIds.Contains(sourceId);
        }

        return IsGroupMember(sourceId, directory) || (ShownScope == MeterScope.Corps && _corpsIds.Contains(sourceId));
    }

    private bool RosterKnown(Aion2EntityDirectory? directory)
    {
        if (directory is null)
        {
            return _source?.Entities is ISampleDirectory sample && sample.Party.Count > 0;
        }

        string? own = directory.LocalCharacter?.Name;
        return _groupNames.Any(n => n != own) || directory.PartyNames.Any(n => n != own);
    }

    private bool IsGroupMember(int id, Aion2EntityDirectory? directory)
    {
        string name = ResolveDisplayName(id);
        if (directory is null)
        {
            return _source?.Entities.IsLocalPlayer(id) == true || (_source?.Entities is ISampleDirectory sample && sample.Party.Contains(name));
        }

        if (directory.IsLocalPlayer(id) || directory.InferLocalPlayer() == id)
        {
            return true;
        }

        return _groupNames.Contains(name) || directory.PartyNames.Contains(name) || name == directory.LocalCharacter?.Name;
    }

    /// <summary>The players who hit a monster the group also hit (the corps until its own roster is read).</summary>
    private HashSet<int> ComputeCorps(IReadOnlyList<DamageEvent> shown)
    {
        var corps = new HashSet<int>();
        Aion2EntityDirectory? directory = GameDirectory;
        var groupTargets = shown.Where(ev => IsGroupMember(ev.SourceObjectId, directory)).Select(ev => ev.TargetObjectId).ToHashSet();
        foreach (DamageEvent ev in shown)
        {
            if (groupTargets.Contains(ev.TargetObjectId) && IsPlayerName(ev.SourceObjectId))
            {
                corps.Add(ev.SourceObjectId);
            }
        }

        return corps;
    }

    private List<DamageEvent> RestrictToEngagedTargets(List<DamageEvent> damageEvents) =>
        _source is null ? damageEvents : EngagedTargets.Filter(damageEvents, _source.Entities.LocalPlayerId);

    private List<DamageEvent> ShownFightHits(IEnumerable<DamageEvent> damage, int targetId) =>
        BossFight.ShownHits(damage, targetId, _selectedRunWindowStart, _selectedRunWindowEnd, IsKnownBoss);

    /// <summary>Works the lines out again (Windows: RefreshRows, the views the meter over the game shows: damage, healing, damage taken).</summary>
    public void RefreshRows()
    {
        Aion2EntityDirectory? directory = GameDirectory;
        var damageOnly = _aggregator.Events.Where(ev => !ev.IsHeal);
        bool scoped = ShownScope != MeterScope.All;
        List<DamageEvent> filtered = _selectedTargetId is int targetId
            ? ShownFightHits(damageOnly, targetId)
            : scoped ? RestrictToEngagedTargets(damageOnly.ToList()) : damageOnly.ToList();
        if (directory is not null)
        {
            foreach (string member in directory.PartyNames)
            {
                _groupNames.Add(member);
            }
        }

        _corpsIds = scoped ? ComputeCorps(filtered) : new HashSet<int>();
        (DateTime, DateTime)? filteredSpan = filtered.Count == 0 ? null : (filtered.Min(ev => ev.Timestamp), filtered.Max(ev => ev.Timestamp));
        _shownHits = filtered;
        _shownSpan = filteredSpan;

        if (_mode == MeterMode.Heal)
        {
            RefreshHealRows(filteredSpan);
            RankRows();
            return;
        }

        if (_mode == MeterMode.Taken)
        {
            RefreshTakenRows(filteredSpan);
            RankRows();
            return;
        }

        var healSourceIds = filteredSpan is (DateTime spanStart, DateTime spanEnd)
            ? _aggregator.Events.Where(ev => ev.IsHeal && ev.Timestamp >= spanStart && ev.Timestamp <= spanEnd && IsPlayerName(ev.SourceObjectId)).Select(ev => ev.SourceObjectId)
            : Enumerable.Empty<int>();
        var sourceIds = filtered.Select(ev => ev.SourceObjectId).Distinct().Union(healSourceIds).Where(IsPlayerName).ToList();
        sourceIds = sourceIds.Where(IsInScope).ToList();

        // A passer-by the meter knows by no name, with one single hit seen, is no line: in the open world every stranger fighting within sight would add a
        // "Player #id" line. The own character and the group stay whatever they did.
        if (directory is not null)
        {
            Dictionary<int, int> hitsBySource = filtered.GroupBy(ev => ev.SourceObjectId).ToDictionary(g => g.Key, g => g.Count());
            sourceIds = sourceIds.Where(id => id == directory.LocalPlayerId
                || hitsBySource.GetValueOrDefault(id) > 1
                || directory.NameFor(id) is not string stranger
                || !stranger.StartsWith("Player #", StringComparison.Ordinal)).ToList();
        }

        KeepRows(sourceIds);

        // One pass over the events instead of one per player.
        var damageById = new Dictionary<int, long>();
        var estimatedById = new Dictionary<int, long>();
        var hitsById = new Dictionary<int, HitStats>();
        foreach (DamageEvent hit in filtered)
        {
            damageById[hit.SourceObjectId] = damageById.GetValueOrDefault(hit.SourceObjectId) + hit.Amount;
            System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(hitsById, hit.SourceObjectId, out _).Add(hit);
            if (hit.Estimated)
            {
                estimatedById[hit.SourceObjectId] = estimatedById.GetValueOrDefault(hit.SourceObjectId) + hit.Amount;
            }
        }

        // the rate: a picked target's over the time of the hits on it; every target's over each player's own hits (the scoped view), or over the fight's
        // window for everyone (the view of all) - the Windows meter's two paths (its index takes the second, the scoped rescan the first)
        Dictionary<int, List<DamageEvent>>? allBySource = null;
        if (_selectedTargetId is null && scoped)
        {
            allBySource = new Dictionary<int, List<DamageEvent>>();
            foreach (DamageEvent hit in _aggregator.Events)
            {
                if (!allBySource.TryGetValue(hit.SourceObjectId, out var list))
                {
                    allBySource[hit.SourceObjectId] = list = new List<DamageEvent>();
                }

                list.Add(hit);
            }
        }

        double targetSeconds = _selectedTargetId is int spanTarget ? BossFight.SecondsOnTarget(filtered, spanTarget) : 0;
        foreach (int sourceId in sourceIds)
        {
            PlayerRow row = _rowsByObjectId[sourceId];
            ApplyIdentity(row, sourceId);
            row.Damage = damageById.GetValueOrDefault(sourceId);
            row.IsEstimated = estimatedById.GetValueOrDefault(sourceId) * 2 > row.Damage;
            row.Hits = hitsById.GetValueOrDefault(sourceId);
            row.Dps = _selectedTargetId is not null
                ? (targetSeconds > 0 ? damageById.GetValueOrDefault(sourceId) / targetSeconds : null)
                : allBySource is not null
                    ? DpsCalculator.AllDpsWallClock(allBySource.GetValueOrDefault(sourceId) ?? new List<DamageEvent>(), sourceId)
                    : SharedRate(row.Damage, filteredSpan);
            row.AllMode = false;
            row.AllCompact = false;
            row.Healing = 0;
            row.HealOthers = 0;
            row.HealOthersRate = null;
            row.Absorbed = 0;
            row.DamageTaken = 0;
            ShowDeaths(row, sourceId, filteredSpan);
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(sourceId) ?? "";
        }

        RankRows();
    }

    /// <summary>Keeps one line per id: drops the lines of ids no longer shown, adds the new ones.</summary>
    private void KeepRows(IReadOnlyCollection<int> ids)
    {
        var keep = ids as HashSet<int> ?? ids.ToHashSet();
        foreach (int staleId in _rowsByObjectId.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _rows.Remove(_rowsByObjectId[staleId]);
            _rowsByObjectId.Remove(staleId);
        }

        foreach (int id in ids)
        {
            if (!_rowsByObjectId.ContainsKey(id))
            {
                var added = new PlayerRow(id);
                _rowsByObjectId[id] = added;
                _rows.Add(added);
            }
        }
    }

    private void RefreshHealRows((DateTime Start, DateTime End)? span)
    {
        Aion2EntityDirectory? directory = GameDirectory;
        List<DamageEvent> heals = HealsInSpan(span);
        double? spanSeconds = span is (DateTime a, DateTime b) && b > a ? (b - a).TotalSeconds : null;
        var sourceIds = heals.Select(ev => ev.SourceObjectId).Distinct().Where(IsInScope).ToList();
        KeepRows(sourceIds);
        foreach (int sourceId in sourceIds)
        {
            PlayerRow row = _rowsByObjectId[sourceId];
            ApplyIdentity(row, sourceId);
            var mine = heals.Where(ev => ev.SourceObjectId == sourceId).ToList();
            row.IsEstimated = false;
            row.Damage = mine.Sum(ev => ev.Amount);
            double seconds = spanSeconds ?? (mine.Max(ev => ev.Timestamp) - mine.Min(ev => ev.Timestamp)).TotalSeconds;
            row.Dps = seconds > 0 ? row.Damage / seconds : null;
            row.Hits = default;
            row.DamageTaken = 0;
            row.Absorbed = 0;
            row.AllMode = false;
            row.AllCompact = false;
            row.Healing = 0;
            row.HealOthers = 0;
            row.HealOthersRate = null;
            ShowDeaths(row, sourceId, span);
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(sourceId) ?? "";
        }
    }

    /// <summary>Taken mode: one line per player with the damage monsters dealt them over the fight on screen, per second of it, and their deaths there.</summary>
    private void RefreshTakenRows((DateTime Start, DateTime End)? span)
    {
        Aion2EntityDirectory? directory = GameDirectory;
        List<DamageEvent> hits = HostileHitsTaken(span);
        var absorbs = _aggregator.Shields
            .Where(ev => ShieldSkills.IsAbsorb(ev) && IsPlayerName(ev.TargetObjectId) && !IsPlayerName(ev.SourceObjectId)
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();
        double? spanSeconds = span is (DateTime a, DateTime b) && b > a ? (b - a).TotalSeconds : null;
        var targetIds = hits.Select(ev => ev.TargetObjectId).Distinct().Where(IsInScope).ToList();
        KeepRows(targetIds);
        foreach (int targetId in targetIds)
        {
            PlayerRow row = _rowsByObjectId[targetId];
            ApplyIdentity(row, targetId);
            var mine = hits.Where(ev => ev.TargetObjectId == targetId).ToList();
            row.IsEstimated = false;
            row.Damage = mine.Sum(ev => ev.Amount);
            row.Absorbed = absorbs.Where(ev => ev.TargetObjectId == targetId).Sum(ev => ev.Amount);
            double seconds = spanSeconds ?? (mine.Max(ev => ev.Timestamp) - mine.Min(ev => ev.Timestamp)).TotalSeconds;
            row.Dps = seconds > 0 ? row.Damage / seconds : null;
            row.Hits = default;
            var readings = directory is null || span is not (DateTime from, DateTime to)
                ? new List<(DateTime, long)>()
                : directory.HitPoints.SamplesAround(targetId, from, to + Deaths.BlowWindow).Select(s => (s.At, s.Hp)).ToList();
            var credits = directory is null || span is not (DateTime creditFrom, DateTime creditTo)
                ? null
                : directory.HitPoints.KillCreditsBetween(targetId, creditFrom, creditTo + Deaths.BlowWindow);
            List<Death> deaths = Deaths.Find(targetId, readings, mine, credits);
            row.Deaths = deaths.Count;
            row.DeadFor = deaths.Count > 0 && deaths[^1].RevivedAt is null && span is (DateTime _, DateTime deadUntil) ? deaths[^1].DeadFor(deadUntil) : null;
            row.DamageTaken = 0;
            row.AllMode = false;
            row.AllCompact = false;
            row.Healing = 0;
            row.ShowShareBar = _showShareBars;
            row.ShowDamageTaken = false;
            row.Faction = directory?.FactionOf(targetId) ?? "";
        }
    }

    /// <summary>Deaths after a player's name: how often their hit points reached 0 in the fight shown, and while they are down how long.</summary>
    private void ShowDeaths(PlayerRow row, int playerId, (DateTime Start, DateTime End)? span)
    {
        if (!_showDeaths || span is not (DateTime from, DateTime to) || GameDirectory is not { } directory)
        {
            row.Deaths = 0;
            row.DeadFor = null;
            return;
        }

        var readings = directory.HitPoints.SamplesAround(playerId, from, to + Deaths.BlowWindow).Select(s => (s.At, s.Hp)).ToList();
        List<Death> deaths = Deaths.Find(playerId, readings, Array.Empty<DamageEvent>());
        row.Deaths = deaths.Count;
        row.DeadFor = deaths.Count > 0 && deaths[^1].RevivedAt is null ? deaths[^1].DeadFor(to) : null;
    }

    /// <summary>A player's DPS over the window the meter shows - the fight, from its first hit to its last - the same for everyone.</summary>
    private static double? SharedRate(long damage, (DateTime From, DateTime To)? span)
    {
        if (span is not (DateTime from, DateTime to))
        {
            return null;
        }

        double seconds = (to - from).TotalSeconds;
        return seconds > 0 ? damage / seconds : null;
    }

    /// <summary>The heals by players inside a span, without those on summoned spirits.</summary>
    private List<DamageEvent> HealsInSpan((DateTime Start, DateTime End)? span)
    {
        Aion2EntityDirectory? directory = GameDirectory;
        return _aggregator.Events
            .Where(ev => ev.IsHeal && IsPlayerName(ev.SourceObjectId)
                && directory?.SummonOwnerOf(ev.TargetObjectId) is null
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();
    }

    /// <summary>The hostile hits players took inside a span: from a monster, not from a player.</summary>
    private List<DamageEvent> HostileHitsTaken((DateTime Start, DateTime End)? span) =>
        _aggregator.Events
            .Where(ev => !ev.IsHeal && IsPlayerName(ev.TargetObjectId) && !IsPlayerName(ev.SourceObjectId)
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();

    /// <summary>Ranks, shares and bar lengths among the lines on screen, then the lines the meter shows (the best ten, the own one kept).</summary>
    private void RankRows()
    {
        long shownTotal = _rows.Sum(r => r.Damage);
        bool taken = _mode == MeterMode.Taken;
        long shownMax = _rows.Count > 0 ? _rows.Max(r => r.Damage + (taken ? r.Absorbed : 0)) : 0;
        int rank = 0;
        List<PlayerRow> ordered = _rows.OrderByDescending(r => r.Damage).ThenBy(r => r.ObjectId).ToList();
        foreach (PlayerRow row in ordered)
        {
            row.Rank = ++rank;
            row.SharePercent = shownTotal > 0 ? 100.0 * row.Damage / shownTotal : 0;
            row.FillPercent = shownMax > 0 ? 100.0 * row.Damage / shownMax : 0;
            row.AbsorbedFillPercent = taken && shownMax > 0 && row.Absorbed > 0 ? 100.0 * (row.Damage + row.Absorbed) / shownMax : 0;
        }

        // the combat power after each name, from what the game showed of the player
        if (GameDirectory is { } powers)
        {
            foreach (PlayerRow row in ordered)
            {
                int? power = powers.SeenProfileOf(row.ObjectId)?.CombatPower;
                if (power is not > 0 && row.ObjectId >= 0 && row.ObjectId == powers.LocalPlayerId)
                {
                    power = OwnCombatPower(powers);
                }

                row.CombatPowerText = power is int cp and > 0 and not 397228 ? PlayerRow.Compact(cp) : "";
            }
        }
        else if (_source?.Entities is ISampleDirectory sample)
        {
            foreach (PlayerRow row in ordered)
            {
                row.CombatPowerText = sample.PowerOf(row.ObjectId) is int cp and > 0 ? PlayerRow.Compact(cp) : "";
            }
        }

        AllRows = ordered;
        List<PlayerRow> shown = ordered.Take(MaxLines).ToList();
        if (ordered.Count > MaxLines && _source?.Entities is { } entities && !shown.Any(r => entities.IsLocalPlayer(r.ObjectId))
            && ordered.FirstOrDefault(r => entities.IsLocalPlayer(r.ObjectId)) is { } me)
        {
            shown[^1] = me;
        }

        ShownRows = shown;
    }

    /// <summary>The own character's combat power (Windows: MainWindow.OwnCombatPower): the game's live number, else the legion list's, else the CP checks'.</summary>
    internal static int? OwnCombatPower(Aion2EntityDirectory directory)
    {
        if (directory.LocalCharacter is not { } me || me.Name.Length == 0)
        {
            return null;
        }

        if (directory.OwnLiveCombatPower is int live)
        {
            return live;
        }

        if (Aion2Moved.Find(directory.LegionRoster(), m => m.Name, m => m.ServerId, me.Name, me.ServerId) is { CombatPower: > 0 } listed)
        {
            return listed.CombatPower;
        }

        if (CpChecks.State(me.Name, me.ServerId)?.Points is { Count: > 0 } points && points[^1].Cp > 0)
        {
            return points[^1].Cp;
        }

        return CpChecks.OfficialOf(me.Name, me.ServerId)?.Cp is int page and > 0 ? page : null;
    }

    // ---- the boss line ----

    /// <summary>The boss being fought now: its name, health, when it dies and its enrage (Windows: MainWindow.BossBar.cs, CurrentBoss); null when none.</summary>
    public BossLine? CurrentBoss()
    {
        if (_source?.Entities is ISampleDirectory sample)
        {
            return sample.Boss;
        }

        if (GameDirectory is not { } dir || CombatOverlayData.BossHp(dir, DateTime.Now) is not { } boss)
        {
            return null;
        }

        int? npc = dir.BossNpcIdOf(boss.Id) ?? dir.NpcIdOf(boss.Id);
        string name = npc is int id ? Aion2Npcs.NameOf(id, LocalizationManager.Instance.Language) ?? "" : "";
        TimeSpan? rage = boss.Since is DateTime since && Aion2BossRage.Known(dir.BossNpcIdOf(boss.Id)) ? TimeSpan.FromSeconds(boss.RageSeconds) - (DateTime.Now - since) : null;
        TimeSpan? kill = KillTime(dir, boss.Id);
        double groupDps = _rows.Where(r => !r.IsEnemy).Sum(r => r.Dps ?? 0);
        if (kill is null && groupDps > 0 && boss.Hp > 0 && boss.Hp / groupDps <= 3600)
        {
            kill = TimeSpan.FromSeconds(boss.Hp / groupDps);
        }

        return new BossLine(name, boss.Hp, boss.Max, kill, rage);
    }

    /// <summary>How far back the health readings are taken for the pace.</summary>
    private static readonly TimeSpan BossPaceOver = TimeSpan.FromSeconds(15);

    /// <summary>When the boss dies at the pace its health fell over the last 15 seconds (its own readings); null while it does not fall.</summary>
    internal static TimeSpan? KillTime(Aion2EntityDirectory dir, int id)
    {
        if (dir.HitPoints.Latest(id) is not { Hp: > 0 } last)
        {
            return null;
        }

        var samples = dir.HitPoints.SamplesAround(id, last.At - BossPaceOver, last.At);
        return samples.Count >= 2 ? KillTime(samples[0].Hp, samples[0].At, last.Hp, last.At) : null;
    }

    /// <summary>The time to go from <paramref name="nowHp"/> to 0 at the pace from the earlier reading to the latest; null without a fall over at least 3 s
    /// (Windows: MainWindow.KillTime).</summary>
    internal static TimeSpan? KillTime(long thenHp, DateTime then, long nowHp, DateTime now)
    {
        double seconds = (now - then).TotalSeconds, lost = thenHp - nowHp;
        if (seconds < 3 || lost <= 0 || nowHp <= 0)
        {
            return nowHp <= 0 ? TimeSpan.Zero : null;
        }

        double left = nowHp / (lost / seconds);
        return left > 3600 ? null : TimeSpan.FromSeconds(left);
    }

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }
}

/// <summary>The boss line: what is left of the boss, its whole, its name, when it dies and its enrage.</summary>
public sealed record BossLine(string Name, long Hp, long Max, TimeSpan? Kill, TimeSpan? Rage);

/// <summary>A source's directory that says itself who is a player, a boss, a class and the group - the sample fights of the pictures and the checks
/// (the capture's directory, <see cref="Aion2EntityDirectory"/>, knows that from the game).</summary>
public interface ISampleDirectory : IEntityDirectory
{
    bool IsPlayer(int id);

    bool IsBoss(int id);

    string? ClassOf(int id);

    int? PowerOf(int id);

    IReadOnlySet<string> Party { get; }

    BossLine? Boss { get; }
}
