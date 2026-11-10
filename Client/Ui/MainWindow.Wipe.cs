using System.Windows;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The wipe review on the meter (10 November 2026): when a boss try ends without the kill and players died in it, the review comes up over the lines - the
/// deaths in order with what did each, the skill that took down most of the group, and the health the boss was left at against the best try of the last hours.
/// It goes at the next pull, after a few minutes, or with its ✕.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan WipeShown = TimeSpan.FromMinutes(4);
    private WipeReview? _wipe;
    private DateTime _wipeAt;

    /// <summary>The review of one try (on the history's thread: the directory and the fight log lock for themselves).</summary>
    private static WipeReview BuildWipeReview(Aion2.Aion2EntityDirectory directory, BossAttempt attempt, int? npc, IReadOnlyList<DeathRecord> deaths,
        IReadOnlyList<DamageEvent> window, Func<int, string> nameOf, Func<int, string?> classOf, IReadOnlySet<int> players, int playerCount)
    {
        double? left = HpLeftOf(directory, attempt.TargetId, attempt.Start, attempt.End);
        // the tries on this boss of the last hours since its last kill: this one is among them (saved just before)
        DateTime since = DateTime.Now - TriesWithin;
        List<FightLogEntry> tries = npc is int id
            ? FightLog.Entries().Where(e => e.Kind == FightLogKind.Boss && e.BossNpcId == id && e.Start >= since).OrderByDescending(e => e.Start).TakeWhile(e => e.Killed != true).ToList()
            : new List<FightLogEntry>();
        var others = tries.Where(e => Math.Abs((e.Start - attempt.Start).TotalSeconds) > 1 && e.BossHpLeft is double).Select(e => e.BossHpLeft!.Value).ToList();
        double? best = others.Count > 0 ? others.Min() : null;
        int tryNumber = Math.Max(1, tries.Count(e => e.Start <= attempt.Start.AddSeconds(1)));
        return WipeReview.Build(attempt.Name, attempt.Start, attempt.End, tryNumber, left, best, deaths, window, nameOf, classOf, players.Contains, playerCount);
    }

    /// <summary>The review on the meter (the pictures call it too).</summary>
    internal void ShowWipeReview(WipeReview review)
    {
        _wipe = review;
        _wipeAt = DateTime.Now;
        OverlayWipeCard.Child = WipeReview.MeterView(review, Headless ? null : CloseWipeReview);
        OverlayWipeCard.Visibility = Visibility.Visible;
        UpdateOverlayFold();
    }

    private void CloseWipeReview()
    {
        _wipe = null;
        OverlayWipeCard.Child = null;
        OverlayWipeCard.Visibility = Visibility.Collapsed;
        UpdateOverlayFold();
    }

    /// <summary>A few times a second: the review goes at the next pull (the own hits on a boss again) or after a few minutes.</summary>
    private void UpdateOverlayWipe()
    {
        if (_wipe is null || Headless)
        {
            return;
        }

        if (DateTime.Now - _wipeAt > WipeShown || _lastOwnBossFight > _wipeAt.AddSeconds(5))
        {
            CloseWipeReview();
        }
    }
}
