using Avalonia.Threading;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Combat.Sources;
using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Linux.Meter;

/// <summary>
/// The training dummy's parse (Windows: Client/Ui/DummyTestController in DummyTestOverlay.cs, without its window of its own: the meter shows the parse's
/// clock and card itself, as on Windows). Owns the shared model (<see cref="DummyTest"/>), feeds it the capture's damage and the player's skills and effects,
/// keeps its runs in the same file (dummy-test.json in the data folder). A hit on a training dummy starts a timed parse by itself (Settings: its length and
/// whether it does).
/// </summary>
public sealed class DummyRunner : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private MeterSettings _settings = MeterSettings.Load();
    private int _ticks;

    public DummyTest Model { get; } = new();

    public DummyRunner(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Model.Load(DummyStore.Load(DummyStore.DefaultPath));
        Model.Capture = CaptureRun;
        Model.Changed += () => TrySave();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    /// <summary>The meter's poll: the player's own damage starts the clock and fills the run. <paramref name="entities"/> says who the player is.</summary>
    public void Feed(CombatBatch batch, IEntityDirectory? entities)
    {
        // a hit on a training dummy starts a timed run by itself (Settings: the length, and whether it does)
        if (_settings.DummyAutoStart && batch.Damage.Count > 0 && entities is not null && _directory() is { } dir)
        {
            var known = new Dictionary<int, bool>();
            bool IsDummy(int id)
            {
                if (!known.TryGetValue(id, out bool dummy))
                {
                    known[id] = dummy = DummyTargets.IsDummy(dir.NameFor(id) ?? dir.MonsterNameFor(id));
                }

                return dummy;
            }

            Model.FeedAuto(batch.Damage, entities.IsLocalPlayer, IsDummy, TimeSpan.FromSeconds(Math.Clamp(_settings.DummyTestSeconds, 10, 3600)),
                LocalizationManager.Instance["Dummy.AutoNote"]);
        }

        if (Model.State is DummyState.Armed or DummyState.Running or DummyState.Finished && batch.Damage.Count > 0 && entities is not null)
        {
            Model.Feed(batch.Damage, entities.IsLocalPlayer);
        }
    }

    /// <summary>Starts a parse (the clock starts with the first hit; see <see cref="DummyTest.Arm"/>), its length kept for the next.</summary>
    public void Start(TimeSpan planned, string note)
    {
        Model.Arm(planned, note, DateTime.Now);
        var settings = MeterSettings.Load();
        settings.DummyTestSeconds = (int)planned.TotalSeconds;
        settings.Save();
        _settings = settings;
    }

    /// <summary>The settings changed (the automatic parse switched on or off): read again now rather than within the second.</summary>
    public void SettingsChanged() => _settings = MeterSettings.Load();

    private void TrySave()
    {
        try
        {
            DummyStore.Save(DummyStore.DefaultPath, Model.Runs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the runs stay in memory; the next change tries again
        }
    }

    /// <summary>Reads what the capture logged for the player while the run lasted (the skills used and the effects on the player).</summary>
    private void CaptureRun(DummyRun run)
    {
        if (_directory() is not { } dir)
        {
            return;
        }

        int player = run.PlayerId;
        FightExtras extras = dir.Combat.Slice(run.Start, run.End, id => dir.NameFor(id) ?? dir.MonsterNameFor(id));
        run.Uses.AddRange(extras.Uses.Where(u => u.Actor == player));
        run.Buffs.AddRange(extras.Buffs.Where(b => b.Target == player));
        foreach (var (id, name) in extras.Names)
        {
            if (id == player)
            {
                run.Names[id] = name;
            }
        }

        // the levels of the skills now: the game's own when the login or an entry was seen, else unknown (a saved file is not what the game shows)
        if (dir.SkillLevelsLive)
        {
            run.SkillLevels = dir.LocalSkills.GroupBy(s => DummyAnalysis.BaseOf(s.SkillId)).ToDictionary(g => g.Key, g => g.Max(s => s.Level));
        }

        // the targets the player hit: the analysis compares only runs fought at a training dummy
        foreach (int target in run.Events.Select(e => e.TargetObjectId).Distinct())
        {
            if ((dir.NameFor(target) ?? dir.MonsterNameFor(target)) is { } targetName)
            {
                run.Names[target] = targetName;
            }
        }
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;
        if (_ticks++ % 10 == 0)
        {
            _settings = MeterSettings.Load();
        }

        // a skill or an effect starts the clock as well as a hit does (the cast comes before the hit); for a few seconds into the run it still moves the start back
        if (Model.State == DummyState.Armed || Model.State == DummyState.Running && Model.Current is { } run && now - run.Start < TimeSpan.FromSeconds(3))
        {
            if (_directory() is { } dir && dir.LocalPlayerId is >= 0 and var player && dir.Combat.FirstActivity(player, Model.ArmedAt) is DateTime at)
            {
                Model.NoteActivity(at, player);
            }
        }

        Model.Tick(now);
    }
}
