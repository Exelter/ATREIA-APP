using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Ui;

namespace AionDPS.Linux.Meter;

/// <summary>
/// The bars in parts and rDPS (Windows: MainWindow.UpdateRaidDps, the parts in RankRows, UpdateBarLegend; the lengths are worked out by the shared
/// Client/Ui/MeterBarParts.cs): after the own damage in the class colour the bar goes on in grey for what the player's buffs gave the others, then in green
/// for their healing on the others. The rDPS view credits the buffs to who gave them. Each part has its switch in the settings (the Windows meter's look).
/// </summary>
public sealed partial class MeterEngine
{
    private bool _barBuffs = true;
    private bool _barHeals = true;
    private DateTime _rdpsAt = DateTime.MinValue;

    /// <summary>The span the last rDPS was worked out over: the grey part of a damage bar is the buffs' rate over it.</summary>
    private double _rdpsSeconds;

    private int _barLegendSessions;
    private bool _barLegendCounted;

    /// <summary>The bars are drawn in parts: in the damage views (DPS, rDPS, All in one), when the look has them (Windows: BarPartsOn).</summary>
    public bool BarPartsOn => (_barBuffs || _barHeals) && _mode is MeterMode.Damage or MeterMode.Raid or MeterMode.All;

    /// <summary>The grey part (the buffs given) and the green part (the healing on the others), each with its own switch.</summary>
    public bool BarBuffsOn => BarPartsOn && _barBuffs;

    public bool BarHealsOn => BarPartsOn && _barHeals;

    /// <summary>The line under the lines saying what the bar's colours are: whether the bar is rDPS, and which parts are on; null when it is not shown.</summary>
    public (bool Rdps, bool Buffs, bool Heals)? Legend { get; private set; }

    /// <summary>A sample source (the pictures and the checks) instead of the game.</summary>
    private bool SampleSource => _source?.Entities is ISampleDirectory;

    /// <summary>
    /// The legend under the lines (Windows: UpdateBarLegend): while some bar has a part beside its own, the colours are said; learnt after a few days of
    /// play, it shows in the first five runs of the app it was needed in, then gives its row back.
    /// </summary>
    private void UpdateBarLegend()
    {
        bool rowsShown = !ShowStagger && !ShowGrind && !ShowBuffs && !DummyCardShown;
        bool show = BarPartsOn && rowsShown && _rows.Any(r => r.HealFillPercent - r.FillPercent >= 0.3);
        if (show && !_barLegendCounted && !SampleSource)
        {
            _barLegendCounted = true;
            MeterSettings counted = MeterSettings.Load();
            counted.BarLegendSessions++;
            counted.Save();
            _barLegendSessions = counted.BarLegendSessions;
        }

        show &= SampleSource || _barLegendSessions <= 5;
        Legend = show ? (_rows.Count > 0 && _rows[0].RdpsView, _barBuffs, _barHeals) : null;
    }

    /// <summary>
    /// rDPS, nDPS and what each player's buffs gave the others (Windows: UpdateRaidDps), for the rDPS view and the bar's grey part: every player's direct
    /// hits of the fight against the buffs on them and on the boss (Combat/RaidDps.cs). Worked out every second and a half at most.
    /// </summary>
    private void UpdateRaidDps((DateTime From, DateTime To)? span)
    {
        bool on = _mode == MeterMode.Raid;
        foreach (PlayerRow row in _rows)
        {
            row.RdpsView = on;
        }

        bool wanted = on || BarPartsOn;
        if (!wanted || span is not (DateTime from, DateTime to) || to <= from)
        {
            return;
        }

        if (_source?.Entities is ISampleDirectory sample)
        {
            SampleRaidDps(sample, (to - from).TotalSeconds);
            return;
        }

        if (GameDirectory is not { } directory || DateTime.UtcNow - _rdpsAt < TimeSpan.FromSeconds(1.5))
        {
            return;
        }

        _rdpsAt = DateTime.UtcNow;
        try
        {
            var players = _rows.Where(r => !r.IsEnemy).ToList();
            var ids = players.Select(r => r.ObjectId).ToHashSet();
            int? bossId = _selectedTargetId is int target && !directory.IsKnownPlayer(target) ? target : null;
            var (onPlayers, onBoss) = RaidDps.BuffsIn(directory.Combat.BuffsAround(from, to), ids, bossId, from, to);

            // every player's direct hits of the fight, for rDPS hit by hit (milliseconds from the window's start, as the effects' spans): in a boss's
            // fight the hits on the boss, as its DPS counts them; damage-over-time ticks take no part in the split
            var hitsBy = new Dictionary<int, List<(long, long)>>();
            var bossTargets = new Dictionary<int, bool>();
            bool OnFight(int hitTarget) => bossId is null ? !ids.Contains(hitTarget)
                : hitTarget == bossId || (bossTargets.TryGetValue(hitTarget, out bool isBoss) ? isBoss : bossTargets[hitTarget] = directory.BossNpcIdOf(hitTarget) is not null);
            foreach (DamageEvent ev in _aggregator.Events)
            {
                if (!ev.IsHeal && !ev.IsTick && ev.Amount > 0 && ev.Timestamp >= from && ev.Timestamp <= to && ids.Contains(ev.SourceObjectId) && OnFight(ev.TargetObjectId))
                {
                    (hitsBy.TryGetValue(ev.SourceObjectId, out var l) ? l : hitsBy[ev.SourceObjectId] = new List<(long, long)>()).Add(((long)(ev.Timestamp - from).TotalMilliseconds, ev.Amount));
                }
            }

            var participants = players.Select(r => new RaidParticipant(r.ObjectId, r.Dps ?? 0,
                onPlayers.TryGetValue(r.ObjectId, out var buffs) ? buffs : new List<RaidBuff>(), hitsBy.GetValueOrDefault(r.ObjectId))).ToList();
            Dictionary<int, RaidDpsResult> results = RaidDps.Compute(participants, onBoss, (to - from).TotalSeconds);
            _rdpsSeconds = (to - from).TotalSeconds;

            // Nobody in the list buffed anyone (open world, a solo run, a group without a Chanter or Cleric): rDPS and nDPS would only repeat DPS; the rDPS
            // view then shows each player's DPS, which is their rDPS then.
            bool shared = players.Any(row => results.TryGetValue(row.ObjectId, out RaidDpsResult x) && (x.Given >= 0.5 || Math.Abs(x.Ndps - (row.Dps ?? 0)) >= 0.5));
            foreach (PlayerRow row in players)
            {
                if (!shared)
                {
                    row.Rdps = null;
                    row.Ndps = null;
                    row.Given = null;
                    continue;
                }

                bool known = results.TryGetValue(row.ObjectId, out RaidDpsResult r);
                row.Rdps = known ? r.Rdps : row.Dps;
                row.Ndps = known ? r.Ndps : row.Dps;
                row.Given = known ? r.Given : null;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // the logs changed while being read: the next refresh tries again
            _rdpsAt = DateTime.MinValue;
        }
    }

    /// <summary>The sample fight's buffs (the pictures and the checks): what each player's buffs gave the others, the others' damage lowered by what they got, so
    /// the group's rDPS adds up to its DPS as the real one does.</summary>
    private void SampleRaidDps(ISampleDirectory sample, double seconds)
    {
        var players = _rows.Where(r => !r.IsEnemy).ToList();
        double given = players.Sum(r => sample.GivenOf(r.ObjectId) ?? 0);
        double dps = players.Sum(r => r.Dps ?? 0);
        _rdpsSeconds = seconds;
        foreach (PlayerRow row in players)
        {
            double own = row.Dps ?? 0;
            double received = dps > 0 ? given * own / dps : 0;
            row.Given = sample.GivenOf(row.ObjectId) is double g && g > 0 ? g : 0;
            row.Ndps = own - received;
            row.Rdps = own - received + (row.Given ?? 0);
        }
    }
}
