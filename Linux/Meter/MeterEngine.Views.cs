using System.Globalization;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Ui;
using AionDPS.Upload;

namespace AionDPS.Linux.Meter;

/// <summary>One of the own counters at the meter's foot (Windows: OverlayTicket in MainWindow.MeterBar.cs): its icon (a path under the game's pictures), the
/// figure, the bonus on top ("+2,000"), a little more (the time to the next refill) and its tip; <paramref name="Warn"/> when the next refill would be lost.</summary>
public sealed record FootTicket(string Icon, string Value, string? Bonus, string? Extra, string Tip, bool Warn = false);

/// <summary>One line of the meter's buff view: the effect, its icon, its uptime over the fight shown, harmful or not.</summary>
public sealed record BuffLine(string Name, string? IconPath, double Percent, bool Harmful);

/// <summary>
/// What the meter shows besides the lines, as the Windows meter over the game does (MainWindow.MeterBar.cs, MainWindow.Stagger.cs, MainWindow.Grind.cs,
/// UpdateCompactOverlay, UpdateOverlayBuffs, UpdateOverlayDummy, UpdateOverlayBossCallout): the fold between fights, the measure picked in the pill's menu,
/// the views in place of the lines (the buffs on you, the boss's stagger checks, the grind), the training dummy's parse, the boss's next casts and the own
/// counters at the foot.
/// </summary>
public sealed partial class MeterEngine
{
    private MeterSettings _settings = new();
    private bool _meterGrind;
    private bool _foldIdle = true;
    private bool _showTickets = true;
    private bool _bossLineOn = true;
    private bool _bossTimersOn = true;

    /// <summary>A boss fight or a training dummy was on screen since the meter last started from zero: the meter stays open on it (Windows: _overlayHadFight).</summary>
    private bool _hadFight;

    /// <summary>The fight on screen is a boss fight (Windows: _overlayBossShown).</summary>
    private bool _bossShown;

    private DummyRunner? _dummy;
    private bool _dummyWasActive;

    /// <summary>The buff view is on: the effects on the own character in place of the lines.</summary>
    public bool ShowBuffs { get; private set; }

    /// <summary>The Stagger view is on (the scope switch's "Stagger").</summary>
    public bool ShowStagger { get; private set; }

    /// <summary>The Grind view is on (the scope switch's "Grind"); kept from one start to the next.</summary>
    public bool ShowGrind { get; private set; }

    /// <summary>A measure was picked while a dummy parse is on: the lines show instead of its card, until the next parse.</summary>
    public bool DummyCardHidden { get; private set; }

    /// <summary>The training dummy's parse (null for the pictures and the checks).</summary>
    public DummyRunner? Dummy => _dummy;

    /// <summary>A parse waits for its first hit, runs or just ended: its clock and name are in the head and its card in place of the lines.</summary>
    public bool DummyActive => DummyHeader(DateTime.Now) is not null;

    /// <summary>The dummy's card stands in for the lines.</summary>
    public bool DummyCardShown => DummyActive && !DummyCardHidden;

    /// <summary>"Grind: every fight" in the pill's menu: the meter counts and opens for every fight, trash and the open world too.</summary>
    public bool CountsEverything => _meterGrind;

    /// <summary>The boss line over the lines (Settings: MeterBossLine).</summary>
    public bool BossLineOn => _bossLineOn;

    /// <summary>The settings the meter goes by.</summary>
    public MeterSettings Settings => _settings;

    // ---- the fold ----

    /// <summary>
    /// Whether the meter is open (Windows: UpdateOverlayFold): in a boss fight with lines, PvP, at a training dummy or its parse, in the Grind, buff or Stagger
    /// view, and all the time with the fold switched off; once a boss fight or a dummy was on screen it stays open until the meter starts from zero (a
    /// reset, a new map). Folded, the meter is its bar and the counters at its foot (5 November 2026, the owner: "shows nothing before it goes in a boss
    /// fight or dummy, only my character's info").
    /// </summary>
    public bool Open(DateTime now)
    {
        bool header = DummyHeader(now) is not null;
        bool atDummy = now - _lastDummyHit < TimeSpan.FromSeconds(20);
        bool lines = _rows.Count > 0;
        bool pvp = now - _lastPvpFight < BossFightHold;
        if (((_bossShown || pvp) && lines) || header || atDummy)
        {
            _hadFight = true;
        }

        return !_foldIdle || header || atDummy || (_hadFight && lines) || _meterGrind || ShowBuffs || ShowStagger || ShowGrind || (_bossShown && lines);
    }

    // ---- what is measured ----

    /// <summary>The pill's word (Windows: OverlayModeShort): the view or the measure, "Dummy" while its card shows, "· Grind" while every fight is counted.</summary>
    public string ModeShort
    {
        get
        {
            string measured = ShowGrind ? "Grind" : ShowStagger ? "Stagger" : ShowBuffs ? "Buffs" : _mode switch
            {
                MeterMode.All => "All", MeterMode.Taken => "Tank", MeterMode.Heal => "Heal", MeterMode.Raid => "rDPS", _ => "DPS",
            };
            return DummyCardShown ? "Dummy" : _meterGrind ? measured + " · Grind" : measured;
        }
    }

    /// <summary>A measure picked in the pill's menu (Windows: OverlayModeItems' Pick): the lines in that mode, the views in place of the lines closed; during a
    /// dummy parse the lines instead of its card. Kept for the next start.</summary>
    public void PickMode(MeterMode mode)
    {
        ShowBuffs = false;
        if (ShowStagger)
        {
            CloseStagger();
        }

        if (ShowGrind)
        {
            SetGrindView(false, refresh: false);
        }

        DummyCardHidden = DummyActive;
        _mode = mode;
        if (!SampleSource)
        {
            MeterSettings settings = MeterSettings.Load();
            settings.LastMode = mode.ToString();
            settings.Save();
        }

        RefreshRows();
        Changed?.Invoke();
    }

    /// <summary>"Buffs on you" in the pill's menu: the effects on the own character over the fight shown, in place of the lines.</summary>
    public void PickBuffs()
    {
        if (ShowStagger)
        {
            CloseStagger();
        }

        if (ShowGrind)
        {
            SetGrindView(false, refresh: false);
        }

        ShowBuffs = true;
        _buffsAt = DateTime.MinValue;
        DummyCardHidden = DummyActive;
        RefreshRows();
        UpdateBuffs();
        Changed?.Invoke();
    }

    /// <summary>"Dummy: the parse's card" in the pill's menu: the card back in place of the lines.</summary>
    public void ShowDummyCard()
    {
        DummyCardHidden = false;
        RefreshRows();
        Changed?.Invoke();
    }

    /// <summary>A click on the scope switch (Windows: OnOverlayScopeClicked): "Stagger" and "Grind" are no scopes but views in place of the lines, a second
    /// click brings the lines back; a scope closes them and picks whose lines show.</summary>
    public void ScopeClicked(string tag)
    {
        if (tag == "Stagger")
        {
            SetStaggerView(!ShowStagger);
        }
        else if (tag == "Grind")
        {
            SetGrindView(!ShowGrind);
        }
        else if (Enum.TryParse(tag, out MeterScope scope))
        {
            if (ShowStagger)
            {
                CloseStagger();
            }

            if (ShowGrind)
            {
                SetGrindView(false, refresh: false);
            }

            SetScope(scope);
        }
    }

    /// <summary>The Grind view on or off (Windows: SetGrindView); the choice is kept from one start to the next.</summary>
    public void SetGrindView(bool on, bool save = true, bool refresh = true)
    {
        ShowGrind = on;
        _grindAt = 0;
        if (on)
        {
            ShowBuffs = false;
            DummyCardHidden = DummyActive;
            if (ShowStagger)
            {
                CloseStagger();
            }
        }

        if (save && !SampleSource)
        {
            MeterSettings s = MeterSettings.Load();
            if (s.MeterGrindView != on)
            {
                s.MeterGrindView = on;
                s.Save();
            }
        }

        if (refresh)
        {
            RefreshRows();
            Changed?.Invoke();
        }
    }

    /// <summary>The Stagger view on or off (Windows: SetStaggerView).</summary>
    public void SetStaggerView(bool on)
    {
        ShowStagger = on;
        _staggerPicked = null;
        if (on)
        {
            ShowBuffs = false;
            DummyCardHidden = DummyActive;
            if (ShowGrind)
            {
                SetGrindView(false, refresh: false);
            }
        }

        RefreshRows();
        Changed?.Invoke();
    }

    private void CloseStagger()
    {
        ShowStagger = false;
        _staggerPicked = null;
    }

    /// <summary>A switch of the pill's menu kept in the settings (the automatic dummy parse, "Grind: every fight"): changed, saved and applied.</summary>
    public void ChangeSetting(Action<MeterSettings> change)
    {
        MeterSettings settings = MeterSettings.Load();
        change(settings);
        settings.Save();
        _settings = settings;
        _meterGrind = settings.MeterGrind;
        _dummy?.SettingsChanged();
        _notCountedSaid = DateTime.MinValue;
        RefreshRows();
        Changed?.Invoke();
    }

    // ---- the head: what is shown and since when ----

    /// <summary>The head's line of what is fought and the fight's time, as the Windows meter writes them (UpdateCompactOverlay, UpdateOverlayDummy, the Grind view):
    /// a dummy parse's name and its countdown (in gold), the Grind's map and the time farmed, else the target and the fight's time.</summary>
    public (string Target, string Time, bool Accent) Header(DateTime now)
    {
        if (DummyHeader(now) is { } dummy)
        {
            return (dummy.Label, dummy.Clock, true);
        }

        if (ShowGrind)
        {
            GrindSession? session = SampleGrind ?? Aion2Grind.Current;
            GrindFigures? f = GrindNow(now);
            return (session is { Zone: not 0 } s ? "Grind · " + MapName(s.Zone) : "Grind", f?.FarmedText ?? "", false);
        }

        return (TargetText, TimeText, false);
    }

    /// <summary>A map's name (Windows: LootText.MapName).</summary>
    private static string MapName(int zone)
    {
        var loc = LocalizationManager.Instance;
        return zone > 0 ? Aion2Zones.NameOf(zone, loc.Language) ?? loc["Loot.Map"] + " " + zone : loc["Loot.UnknownMap"];
    }

    /// <summary>The big figure and its unit beside the scope switch (Windows: UpdateCompactOverlay, the views' own): the group's rate; the own name and "buffs" in the
    /// buff view; what came off the stagger bar in the Stagger view.</summary>
    public (string Figure, string Unit) Total(DateTime now)
    {
        if (ShowBuffs)
        {
            return (OwnShortName ?? "You", "buffs");
        }

        if (ShowStagger)
        {
            StaggerCheck? shown = StaggerShown(now);
            return shown is null ? ("–", "Stagger") : (shown.Taken.ToString("N0", CultureInfo.CurrentCulture), "of " + shown.Max.ToString("N0", CultureInfo.CurrentCulture) + " off");
        }

        if (ShowGrind)
        {
            return (GrindNow(now)?.KinahPerHour is double perHour ? CraftWorkshop.Kinah((long)Math.Round(perHour)) : "–", "Kinah/h");
        }

        return (PlayerRow.Compact((long)Math.Round(TotalRate)), Unit);
    }

    /// <summary>The own character's name once the meter knows it (Windows: CompactOverlayOwnName).</summary>
    private string? OwnShortName =>
        GameDirectory is not { } d ? _source?.Entities.NameFor(_source.Entities.LocalPlayerId)
        : d.LocalPlayerId >= 0 && d.NameFor(d.LocalPlayerId) is { } n && !n.StartsWith("Player #", StringComparison.Ordinal) ? n
        : d.LearnedLocalName ?? d.ConfiguredLocalName;

    // ---- the training dummy ----

    /// <summary>The dummy parse in the head (Windows: UpdateOverlayDummy): its clock, its name and how much of its time is left; null while none waits, runs or just ended.</summary>
    public (string Clock, string Label, double? Fraction)? DummyHeader(DateTime now)
    {
        if (_dummy?.Model is not { } model)
        {
            return SampleDummy is { } sample ? (sample.Clock, "Dummy parse", sample.Fraction) : null;
        }

        switch (model.State)
        {
            case DummyState.Armed:
                TimeSpan planned = model.Planned;
                return ($"{(int)planned.TotalMinutes}:{planned.Seconds:00}", "Dummy parse · hit to start", null);
            case DummyState.Running when model.Current is not null:
                TimeSpan left = model.Remaining(now);
                return ($"{(int)left.TotalMinutes}:{left.Seconds:00}", "Dummy parse", model.Planned.TotalSeconds > 0 ? left.TotalSeconds / model.Planned.TotalSeconds : 0);
            case DummyState.Finished when model.Current is { } done:
                return ("0:00", $"Parse {done.Number} saved", null);
            default:
                return null;
        }
    }

    /// <summary>The dummy card (Windows: FillDummyCard): which run and how far it is, the own DPS in big, the clock against the planned length and every kind of hit.</summary>
    public (string Run, string Clock, double Dps, IReadOnlyList<(string Label, string Value)> Stats)? DummyCard(DateTime now)
    {
        if (SampleDummy is { } sample)
        {
            return (sample.Run, sample.CardClock, sample.Dps, DummyStatsOf(sample.Hits));
        }

        if (_dummy?.Model is not { } model || DummyHeader(now) is null)
        {
            return null;
        }

        DummyRun? run = model.Current;
        int number = run?.Number ?? model.Runs.Count + 1;
        TimeSpan planned = model.Planned;
        TimeSpan ran = model.State switch
        {
            DummyState.Running when run is not null => now - run.Start,
            DummyState.Finished when run is not null => run.Length,
            _ => TimeSpan.Zero,
        };
        ran = ran < TimeSpan.Zero ? TimeSpan.Zero : ran > planned && planned > TimeSpan.Zero ? planned : ran;
        string state = model.State switch
        {
            DummyState.Armed => "hit the dummy to start",
            DummyState.Running => "running",
            DummyState.Finished => "saved",
            _ => "",
        };
        IReadOnlyList<DamageEvent> events = run?.Events ?? (IReadOnlyList<DamageEvent>)Array.Empty<DamageEvent>();
        long total = events.Sum(e => e.Amount);
        double dps = ran.TotalSeconds >= 1 ? total / ran.TotalSeconds : 0;
        return ($"Run {number}  ·  {state}", $"{(int)ran.TotalMinutes}:{ran.Seconds:00} / {(int)planned.TotalMinutes}:{planned.Seconds:00}", dps, DummyStatsOf(events));
    }

    /// <summary>The parse's figures beside its DPS (Windows: DummyStatsOf): the damage, the hits, the share of crits, perfect, double, back and front hits, the biggest hit.</summary>
    public static IReadOnlyList<(string Label, string Value)> DummyStatsOf(IEnumerable<DamageEvent> events)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var kinds = new HitStats();
        long total = 0, biggest = 0;
        foreach (DamageEvent e in events)
        {
            if (e.IsHeal)
            {
                continue;
            }

            kinds.Add(e);
            total += e.Amount;
            if (!e.IsTick)
            {
                biggest = Math.Max(biggest, e.Amount / Math.Max(1, e.HitCount));
            }
        }

        static string Percent(double? share) => share is double v ? v.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "–";
        return new (string, string)[]
        {
            (loc["Main.Overlay.ColTotal"], PlayerRow.Compact(total)), (loc["Look.ColHits"], kinds.Direct > 0 ? kinds.Direct.ToString("N0", CultureInfo.CurrentCulture) : "–"),
            (loc["Look.ColCrit"], Percent(kinds.CritPercent)), (loc["Look.ColPerfect"], Percent(kinds.PerfectPercent)),
            (loc["Look.ColDouble"], Percent(kinds.DoublePercent)), (loc["Look.ColBack"], Percent(kinds.BackPercent)),
            (loc["Look.ColFront"], Percent(kinds.FrontPercent)), ("Max hit", biggest > 0 ? PlayerRow.Compact(biggest) : "–"),
        };
    }

    /// <summary>Once a second: a parse that starts shows its card; one that ends gives the fight's own time and name back (Windows: UpdateOverlayDummy).</summary>
    private void NoteDummy()
    {
        bool active = DummyActive;
        if (active == _dummyWasActive)
        {
            return;
        }

        _dummyWasActive = active;
        DummyCardHidden = false; // a new parse shows its card
        RefreshRows();
    }

    // ---- the buffs on you ----

    private DateTime _buffsAt = DateTime.MinValue;

    /// <summary>The buff view's lines (Windows: UpdateOverlayBuffs).</summary>
    public IReadOnlyList<BuffLine> BuffLines { get; private set; } = Array.Empty<BuffLine>();

    /// <summary>The effects on the own character over the fight shown (the last minute when nothing is shown), with their uptime, the helpful ones first.
    /// Worked out every second and a half at most.</summary>
    private void UpdateBuffs()
    {
        if (SampleBuffs is { } sample)
        {
            BuffLines = sample;
            return;
        }

        if (!ShowBuffs || DateTime.UtcNow - _buffsAt < TimeSpan.FromSeconds(1.5) || GameDirectory is not { } directory)
        {
            return;
        }

        _buffsAt = DateTime.UtcNow;
        int you = directory.LocalPlayerId >= 0 ? directory.LocalPlayerId : directory.InferLocalPlayer() ?? -1;
        IReadOnlyList<DamageEvent> shownHits = _shownHits;
        DateTime to = shownHits.Count > 0 ? shownHits.Max(h => h.Timestamp) : DateTime.Now;
        DateTime from = shownHits.Count > 0 ? shownHits.Min(h => h.Timestamp) : to.AddMinutes(-1);
        if (to - from < TimeSpan.FromSeconds(5))
        {
            from = to.AddSeconds(-5);
        }

        if (you < 0)
        {
            BuffLines = Array.Empty<BuffLine>();
            return;
        }

        var players = _rows.Select(r => r.ObjectId).ToHashSet();
        players.Add(you);
        BuffLines = FightReport.EffectsOn(directory.Combat.BuffsAround(from, to), you, players, from, to).Take(14)
            .Select(effect => new BuffLine(effect.Name,
                Aion2SkillIcons.PathFor(effect.SkillId) ?? Aion2SkillIcons.PathForName(effect.Name, null) ?? Aion2Artwork.EffectIconPath(effect.SkillId),
                effect.UptimePercent, effect.Harmful))
            .ToList();
    }

    // ---- the boss's next casts ----

    private (int Entity, DateTime? Since, DateTime? First, long At) _calloutStart = (-1, null, null, 0);

    /// <summary>The boss timers line (Windows: UpdateOverlayBossCallout, Upload/BossTimers.cs): the boss's next two casts as everyone's uploads learnt them and the
    /// time to its rage where the game data knows it; null without a boss or anything known.</summary>
    public BossCalloutFrame? Callout { get; private set; }

    /// <summary>A few times a second (the window's quick timer): the boss's next casts again.</summary>
    public void UpdateCallout(DateTime now)
    {
        if (SampleCallout is { } sample)
        {
            Callout = sample;
            return;
        }

        // the site is asked for the timers only with the privacy policy accepted; without, the line shows the enrage from the game's own data
        BossCalloutFrame? frame = _bossTimersOn && GameDirectory is { } dir
            ? BossTimers.Now(dir, now, LocalizationManager.Instance.Language, id => BossFirstHit(id, dir), PrivacyConsent.IsAccepted(_settings)) : null;
        Callout = frame is null || (frame.Next.Count == 0 && frame.Rage is null) ? null : frame;
        _calloutFrame = frame;
    }

    /// <summary>The frame as worked out, shown or not (the Stagger view's "next check").</summary>
    private BossCalloutFrame? _calloutFrame;

    /// <summary>When the boss was first hit in this pull (the fight's start as the uploads count it), null before (Windows: BossFirstHit).</summary>
    private DateTime? BossFirstHit(int entity, Aion2EntityDirectory dir)
    {
        DateTime? since = dir.EngagedSince(entity);
        long tick = Environment.TickCount64;
        if (_calloutStart.Entity == entity && _calloutStart.Since == since && (_calloutStart.First is not null || tick - _calloutStart.At < 1000))
        {
            return _calloutStart.First;
        }

        DateTime from = (since ?? DateTime.MinValue).AddSeconds(since is null ? 0 : -60);
        DateTime? first = null;
        foreach (DamageEvent ev in _aggregator.Events)
        {
            if (!ev.IsHeal && ev.TargetObjectId == entity && ev.Timestamp >= from && (first is null || ev.Timestamp < first))
            {
                first = ev.Timestamp;
            }
        }

        _calloutStart = (entity, since, first, tick);
        return first;
    }

    // ---- the Stagger view ----

    /// <summary>How long an ended check stays on the meter.</summary>
    private static readonly TimeSpan StaggerKeep = TimeSpan.FromMinutes(5);

    /// <summary>An earlier check clicked on the chips: shown until a new check starts.</summary>
    private DateTime? _staggerPicked;

    /// <summary>The checks that ended, kept as they were: the meter's events can be reset after a wipe, the view still shows who did what.</summary>
    private readonly Dictionary<(int Boss, DateTime Start), StaggerCheck> _staggerDone = new();

    /// <summary>A check picked on the chips.</summary>
    public void PickStagger(StaggerCheck check) => _staggerPicked = check.Start;

    /// <summary>The Stagger view's content (Windows: UpdateOverlayStagger): the boss's checks, the one shown, the group and the next check expected.</summary>
    public (IReadOnlyList<StaggerCheck> Checks, StaggerCheck? Shown, IReadOnlyList<StaggerPerson> People, string? Next) Stagger(DateTime now)
    {
        if (SampleStagger is { } sample)
        {
            return sample;
        }

        List<StaggerCheck> checks = GameDirectory is { } dir ? CurrentStaggerChecks(dir, now) : new List<StaggerCheck>();
        checks.RemoveAll(c => c.End is DateTime end && now - end > StaggerKeep && c.Outcome != StaggerOutcome.Running);
        StaggerCheck? latest = StaggerChecks.Latest(checks);
        if (latest?.Outcome == StaggerOutcome.Running)
        {
            _staggerPicked = null; // a new check: shown at once
        }

        StaggerCheck? shown = _staggerPicked is DateTime picked ? checks.FirstOrDefault(c => c.Start == picked) ?? latest : latest;
        string? next = shown is null || shown.Outcome != StaggerOutcome.Running ? NextStaggerCheck() : null;
        return (checks, shown, StaggerPeople(), next);
    }

    private StaggerCheck? StaggerShown(DateTime now) => Stagger(now).Shown;

    /// <summary>The checks of the boss whose stagger bar the game sent last (in the last ten minutes), oldest first; empty without one.</summary>
    private List<StaggerCheck> CurrentStaggerChecks(Aion2EntityDirectory dir, DateTime now)
    {
        if (dir.Combat.LastStagger() is not (int boss, DateTime at) || now - at > TimeSpan.FromMinutes(10) || dir.BossNpcIdOf(boss) is null)
        {
            return new List<StaggerCheck>();
        }

        DateTime since = now.AddMinutes(-10);
        List<StaggerEvent> readings = dir.Combat.StaggersSince(boss, since);
        if (readings.Count == 0)
        {
            return new List<StaggerCheck>();
        }

        var windups = dir.Combat.WindupsSince(boss, readings[0].At.AddSeconds(-5));
        List<DamageEvent> hits = HitsOn(boss, readings[0].At.AddSeconds(-2));
        List<StaggerCheck> checks = StaggerChecks.Find(boss, readings, windups, hits, now);

        // an ended check as it was when it ended (the meter's events may have been reset since)
        for (int i = 0; i < checks.Count; i++)
        {
            var id = (boss, checks[i].Start);
            if (checks[i].Outcome == StaggerOutcome.Running)
            {
                continue;
            }

            if (_staggerDone.TryGetValue(id, out StaggerCheck? kept) && kept.Credit.Total >= checks[i].Credit.Total)
            {
                checks[i] = kept;
            }
            else
            {
                _staggerDone[id] = checks[i];
            }
        }

        foreach (var old in _staggerDone.Where(kv => kv.Value.End is DateTime e && now - e > StaggerKeep + TimeSpan.FromMinutes(5)).Select(kv => kv.Key).ToList())
        {
            _staggerDone.Remove(old);
        }

        return checks;
    }

    /// <summary>The hits on <paramref name="target"/> since <paramref name="since"/>, in the order they came (the meter's events, read from the newest back).</summary>
    private List<DamageEvent> HitsOn(int target, DateTime since)
    {
        IReadOnlyList<DamageEvent> events = _aggregator.Events;
        var found = new List<DamageEvent>();
        int older = 0;
        for (int i = events.Count - 1; i >= 0; i--)
        {
            DamageEvent e = events[i];
            if (e.Timestamp < since)
            {
                if (++older > 300)
                {
                    break; // the events come in time order but for a few late ones
                }

                continue;
            }

            older = 0;
            if (e.TargetObjectId == target && !e.IsHeal)
            {
                found.Add(e);
            }
        }

        found.Reverse();
        return found;
    }

    /// <summary>The group as the meter lists it: its players (not the enemies), each with 0 when they took nothing off.</summary>
    private IReadOnlyList<StaggerPerson> StaggerPeople() =>
        _rows.Where(r => !r.IsEnemy && r.ObjectId >= 0).GroupBy(r => r.ObjectId).Select(g => g.First())
            .Select(r => new StaggerPerson(r.ObjectId, r.Name, r.ClassName ?? "")).Take(12).ToList();

    /// <summary>A player who took some off the bar and is not in the list.</summary>
    public StaggerPerson StaggerPersonOf(int id) => new(id, ResolveDisplayName(id), GameDirectory?.ClassOf(id) ?? "");

    /// <summary>The next check the boss timers expect: a cast known to start one, coming up (null: none).</summary>
    private string? NextStaggerCheck() =>
        _calloutFrame?.Next.FirstOrDefault(n => n.SkillId != 0 && StaggerChecks.IsMechanic(n.SkillId)) is { } c
            ? c.Casting ? $"Next stagger check: {c.Name} now" : $"Next stagger check: {c.Name} in {Math.Ceiling(c.In).ToString(CultureInfo.CurrentCulture)} s"
            : null;

    // ---- the Grind view ----

    private long _grindAt;
    private GrindFigures? _grindFigures;

    /// <summary>The grind going on at <paramref name="now"/> (Windows: UpdateOverlayGrind): its numbers once a second, the loot priced at the market of the own server.</summary>
    public GrindFigures? GrindNow(DateTime now)
    {
        if (SampleGrind is { } sample)
        {
            return GrindFigures.Of(sample, now, SampleGrindBook);
        }

        long tick = Environment.TickCount64;
        if (tick - _grindAt < 1000 && _grindAt != 0)
        {
            return _grindFigures;
        }

        _grindAt = tick;
        MarketBook? book = GameDirectory?.LocalCharacter is { ServerId: > 0 } own ? ExchangePrices.For(own.ServerId) : null;
        _grindFigures = Aion2Grind.Current is { } session ? GrindFigures.Of(session, now, book) : null;
        return _grindFigures;
    }

    /// <summary>"New session" on the Grind view: counting starts again from the next kill (the session goes on the Grind page).</summary>
    public void NewGrindSession()
    {
        Aion2Grind.NewSession();
        _grindAt = 0;
        Changed?.Invoke();
    }

    // ---- the counters at the foot ----

    // the game's own icons (Windows: MainWindow.MeterBar.cs): the items' pictures and the Dimensional Invasion's event icon
    private const string OdyleIcon = "aion2/items/more/Icon_Item_Odenergy_A_001.png";
    private const string ShugoIcon = "aion2/items/more/Icon_Currency_Treasure_Key_Legend_004.png";
    private const string InvasionIcon = "aion2/schedule/icons/invasion.png";
    private const string NightmareIcon = "aion2/items/more/Icon_Ticket_Entrance_BossChallenge_001.png";
    private const string DungeonIcon = "aion2/items/more/Icon_Ticket_Entrance_DailyDungeon_001.png";
    private const string AscensionIcon = "aion2/items/more/Icon_Ticket_Entrance_Awaken_001.png";

    /// <summary>The counters at the foot after the Odyle Energy: Shugo Festival, Dimensional Invasion, Nightmare, the daily dungeon's weekly entries and
    /// Ascension Trial.</summary>
    private static readonly (int Id, string Icon)[] FootTickets =
    {
        (1, ShugoIcon), (8, InvasionIcon), (4, NightmareIcon), (101, DungeonIcon), (80000001, AscensionIcon),
    };

    private DateTime _ticketsAt = DateTime.MinValue;
    private IReadOnlyList<FootTicket> _tickets = Array.Empty<FootTicket>();

    /// <summary>Every five seconds: the character played's counters at the meter's foot, as the game sent them (worked out from the refills since where the game sent
    /// nothing new, "~"). Nothing while no character is known or the counters are switched off (Windows: UpdateOverlayTickets).</summary>
    public IReadOnlyList<FootTicket> Tickets(DateTime now)
    {
        if (SampleTickets is { } sample)
        {
            return sample;
        }

        if (DateTime.UtcNow - _ticketsAt < TimeSpan.FromSeconds(5))
        {
            return _tickets;
        }

        _ticketsAt = DateTime.UtcNow;
        string? who = Aion2Tickets.CurrentName ?? GameDirectory?.LocalCharacter?.Name;
        _tickets = _showTickets ? TicketsOf(who, now) : Array.Empty<FootTicket>();
        return _tickets;
    }

    /// <summary>The counters of one character for the foot (empty when it is not known; Windows: TicketsOf).</summary>
    internal static IReadOnlyList<FootTicket> TicketsOf(string? characterName, DateTime now)
    {
        if (characterName is null || Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == characterName) is not { } character)
        {
            return Array.Empty<FootTicket>();
        }

        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        DateTime utcNow = now.ToUniversalTime();
        bool premium = Aion2Tickets.HasPremium(character, utcNow) != false;
        DateTime seenAt = character.SavedAt.ToLocalTime();
        var list = new List<FootTicket>();
        if (Aion2Tickets.OdyleNow(character, utcNow, online: true) is { } odyle)
        {
            string? next = odyle.NextTick is DateTime tick && tick > utcNow ? Clock(tick - utcNow) : null;
            string tip = $"{Aion2Tickets.Find(Aion2Tickets.OdyleId)?.NameIn(language) ?? "Odyle Energy"}: {odyle.Count} / {odyle.Cap}"
                + (odyle.Bonus > 0 ? $"  ·  +{odyle.Bonus.ToString("N0", CultureInfo.CurrentCulture)} charged" : "")
                + (next is not null ? $"  ·  +{odyle.PerTick} in {next}" : odyle.Full ? "  ·  full" : "");
            list.Add(new FootTicket(OdyleIcon, odyle.Count.ToString("N0", CultureInfo.CurrentCulture),
                odyle.Bonus > 0 ? "+" + odyle.Bonus.ToString("N0", CultureInfo.CurrentCulture) : null, next, tip, odyle.Full));
        }

        foreach ((int id, string icon) in FootTickets)
        {
            if (Aion2Tickets.Find(id) is not { } def || Aion2Tickets.IsLocked(character, id) || !character.Tickets.TryGetValue(id, out TicketValue value))
            {
                continue;
            }

            int count = Aion2Tickets.Expected(def, value.Count, seenAt, now, premium);
            int cap = premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;
            int lost = Aion2Tickets.LostAtNextRefill(def, count, premium);
            string tip = $"{def.ShortIn(language)}: {count} / {Math.Max(cap, count)}" + (value.Bonus > 0 ? $"  (+{value.Bonus})" : "")
                + (lost > 0 ? "  ·  " + string.Format(CultureInfo.CurrentCulture, loc[lost == 1 ? "Checklist.WarnLostOne" : "Checklist.WarnLostMany"], lost) : "");
            list.Add(new FootTicket(icon, (count != value.Count ? "~" : "") + count.ToString(CultureInfo.CurrentCulture),
                value.Bonus > 0 ? "+" + value.Bonus.ToString(CultureInfo.CurrentCulture) : null, null, tip, lost > 0));
        }

        return list;
    }

    /// <summary>"1:35" (hours and minutes) or "12m" under an hour.</summary>
    private static string Clock(TimeSpan left) => left.TotalHours >= 1
        ? $"{(int)left.TotalHours}:{left.Minutes:00}"
        : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m";

    /// <summary>The counters the pictures show (Windows: SampleTickets): a character with charged energy, a Shugo bonus key and a full Nightmare.</summary>
    public static IReadOnlyList<FootTicket> SampleTicketList() => new[]
    {
        new FootTicket(OdyleIcon, "840", "+2,000", "1:35", "Odyle Energy: 840 / 840"),
        new FootTicket(ShugoIcon, "20", "+3", null, "Shugo Festival: 20 / 21"),
        new FootTicket(InvasionIcon, "7", null, null, "Dimensional Invasion: 7 / 7"),
        new FootTicket(NightmareIcon, "14", null, null, "Nightmare: 14 / 14", Warn: true),
        new FootTicket(DungeonIcon, "6", "+6", null, "Daily Dungeon: 6 / 14"),
        new FootTicket(AscensionIcon, "3", null, null, "Ascension Trial: 3 / 3"),
    };

    // ---- the pictures' stand-ins (Render.cs): what the game would send, made up ----

    internal IReadOnlyList<FootTicket>? SampleTickets { get; set; }

    internal IReadOnlyList<BuffLine>? SampleBuffs { get; set; }

    internal GrindSession? SampleGrind { get; set; }

    internal MarketBook? SampleGrindBook { get; set; }

    internal (IReadOnlyList<StaggerCheck> Checks, StaggerCheck? Shown, IReadOnlyList<StaggerPerson> People, string? Next)? SampleStagger { get; set; }

    internal BossCalloutFrame? SampleCallout { get; set; }

    /// <summary>The moment the sample's stagger checks are drawn at (their running check's time left).</summary>
    internal DateTime? SampleStaggerAt { get; set; }

    internal (string Clock, double? Fraction, string Run, string CardClock, double Dps, IReadOnlyList<DamageEvent> Hits)? SampleDummy { get; set; }

    /// <summary>Opens a view for the pictures without saving anything.</summary>
    internal void ShowForPicture(string view)
    {
        ShowStagger = view == "stagger";
        ShowGrind = view == "grind";
        ShowBuffs = view == "buffs";
        DummyCardHidden = false;
        RefreshRows();
        UpdateBuffs();
    }
}
