using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The boss line on the meter (5 November 2026, the owner: "remove the boss bar and instead add it over the meter, and knowing the boss's HP we calculate how
/// long it has to die compared to the DPS", then: "simple, not a huge bar, just a text like 50m/120m and time to enrage / time to kill"): between the meter's
/// head and its lines, one line - what is left of the boss and its whole, its name, when it dies at the pace of the last 15 seconds of its health readings
/// (everybody's damage; the group's DPS until there is a pace) and its enrage: green when it dies first, red when the enrage comes first. The boss is the one the combat overlay followed (the target,
/// else the boss last hit, else the boss casting); it has its own switch in Meter look (MeterBossLine), no longer the combat overlay's health bar switch, which
/// a player turning the old big bar off had turned off with it.
/// </summary>
public partial class MainWindow
{
    private TextBlock? _bossName;
    private TextBlock? _bossNumbers;
    private TextBlock? _bossKill;
    private TextBlock? _bossRage;
    private DockPanel? _bossSecond;
    private TextBlock? _bossPace;
    private TextBlock? _bossTries;

    /// <summary>How far back the health readings are taken for the pace.</summary>
    private static readonly TimeSpan BossPaceOver = TimeSpan.FromSeconds(15);

    private void UpdateOverlayBossBar()
    {
        if (Headless)
        {
            return; // the pictures fill it themselves
        }

        if (!_gateSettings.MeterBossLine || CurrentBoss() is not { } boss)
        {
            OverlayBossBar.Visibility = Visibility.Collapsed;
            return;
        }

        ShowBossBar(boss.Name, boss.Hp, boss.Max, boss.Kill, boss.Rage, PaceNow(boss.Npc), TriesNow(boss.Npc, boss.Hp, boss.Max));
    }

    // ---- the pace against the own best, and the tries (10 November 2026, the owner: "live pace against your best: during a boss fight, whether you're ahead of
    // or behind your personal best on that boss"; "wipe progress: across tries on the same boss, how far each pull got") ----

    /// <summary>The own best DPS per boss (killed fights of the character played, from the history), worked out again when the history changed.</summary>
    private readonly Dictionary<(int Npc, string Who), double?> _bests = new();
    private int _bestsVersion = -1;

    private (int Npc, int Version) _triesFor = (-1, -1);
    private DateTime _triesAt = DateTime.MinValue;
    private List<History.FightLogEntry> _tries = new();

    /// <summary>How far back the tries on a boss count as one evening of them (a kill ends them too).</summary>
    private static readonly TimeSpan TriesWithin = TimeSpan.FromHours(3);

    /// <summary>The own DPS now against the own best on this boss: (now, best), or null without a best or a DPS yet.</summary>
    private (double Now, double Best)? PaceNow(int? npc)
    {
        if (npc is not int id || _source?.Entities is not Aion2.Aion2EntityDirectory dir || dir.LocalPlayerId < 0 || dir.NameFor(dir.LocalPlayerId) is not { Length: > 0 } me)
        {
            return null;
        }

        double? mine = _rows.FirstOrDefault(r => r.ObjectId == dir.LocalPlayerId)?.Dps;
        if (mine is not > 0 || BestOn(id, me) is not double best || best <= 0)
        {
            return null;
        }

        return (mine.Value, best);
    }

    private double? BestOn(int npc, string who)
    {
        if (_bestsVersion != History.FightLog.Version)
        {
            _bests.Clear();
            _bestsVersion = History.FightLog.Version;
        }

        if (!_bests.TryGetValue((npc, who), out double? best))
        {
            best = History.FightLog.Entries().Where(e => e.Kind == History.FightLogKind.Boss && e.Killed == true && e.BossNpcId == npc && e.You == who && e.YourDps > 0)
                .Select(e => (double?)e.YourDps).DefaultIfEmpty(null).Max();
            _bests[(npc, who)] = best;
        }

        return best;
    }

    /// <summary>
    /// The tries on this boss: the saved ones of the last hours since its last kill (each with the health it was left at), and the one going on now.
    /// (number of this try, the lowest health any try left it at, the last try's) as fractions; null on the first try.
    /// </summary>
    private (int Try, double Best, double Last)? TriesNow(int? npc, long hp, long max)
    {
        if (npc is not int id || max <= 0)
        {
            return null;
        }

        // the saved tries are read again only when the history changed (or a minute went by: an old one leaves the window)
        DateTime since = DateTime.Now - TriesWithin;
        if (_triesFor != (id, History.FightLog.Version) || DateTime.Now - _triesAt > TimeSpan.FromMinutes(1))
        {
            _triesFor = (id, History.FightLog.Version);
            _triesAt = DateTime.Now;
            _tries = History.FightLog.Entries().Where(e => e.Kind == History.FightLogKind.Boss && e.BossNpcId == id && e.Start >= since)
                .OrderByDescending(e => e.Start).TakeWhile(e => e.Killed != true).Where(e => e.BossHpLeft is double).ToList();
        }

        List<History.FightLogEntry> before = _tries;
        if (before.Count == 0)
        {
            return null;
        }

        double now = Math.Clamp((double)hp / max, 0, 1);
        double best = Math.Min(now, before.Min(e => e.BossHpLeft!.Value));
        return (before.Count + 1, best, before[0].BossHpLeft!.Value);
    }

    /// <summary>The lowest health of a boss during one try, as a fraction of its whole (its own readings); null without readings.</summary>
    internal static double? HpLeftOf(Aion2.Aion2EntityDirectory dir, int id, DateTime from, DateTime to)
    {
        long? max = dir.HitPoints.MaxOf(id) ?? dir.HitPoints.SpawnMaxOf(id) ?? dir.HitPoints.HighestSeen(id);
        var samples = dir.HitPoints.SamplesAround(id, from, to);
        if (max is not > 0 || samples.Count == 0)
        {
            return null;
        }

        return Math.Clamp((double)samples.Min(x => x.Hp) / max.Value, 0, 1);
    }

    /// <summary>The boss being fought now with its name, health, when it dies and its enrage (the meter's line and the stream's boss widget); null when none.</summary>
    private (string Name, long Hp, long Max, TimeSpan? Kill, TimeSpan? Rage, int? Npc)? CurrentBoss()
    {
        if (_pvpOnly || _historyMode || _source?.Entities is not Aion2.Aion2EntityDirectory dir || CombatOverlayData.BossHp(dir, DateTime.Now) is not { } boss)
        {
            return null;
        }

        // A boss the app's list does not have yet (a new raid's: the game's hit points make it one) is named from the game's monster names; its enrage is not
        // shown rather than guessed (the usual five minutes would be wrong for most new bosses).
        int? npc = dir.BossNpcIdOf(boss.Id) ?? dir.NpcIdOf(boss.Id);
        string name = npc is int id ? Aion2.Protocol.Aion2Npcs.NameOf(id, LocalizationManager.Instance.Language) ?? "" : "";
        TimeSpan? rage = boss.Since is DateTime since && Aion2.Protocol.Aion2BossRage.Known(dir.BossNpcIdOf(boss.Id)) ? TimeSpan.FromSeconds(boss.RageSeconds) - (DateTime.Now - since) : null;
        // when it dies: at the pace its health fell (everybody's damage), else at the group's DPS on the meter
        TimeSpan? kill = KillTime(dir, boss.Id);
        double groupDps = _rows.Where(r => !r.IsEnemy).Sum(r => r.Dps ?? 0);
        if (kill is null && groupDps > 0 && boss.Hp > 0 && boss.Hp / groupDps <= 3600)
        {
            kill = TimeSpan.FromSeconds(boss.Hp / groupDps);
        }

        return (name, boss.Hp, boss.Max, kill, rage, npc);
    }

    /// <summary>When the boss dies at the pace its health fell over the last <see cref="BossPaceOver"/> (its own readings: everybody's damage, heals of it
    /// counted against); null while it does not fall (a pause, a phase, too few readings).</summary>
    internal static TimeSpan? KillTime(Aion2.Aion2EntityDirectory dir, int id)
    {
        if (dir.HitPoints.Latest(id) is not { Hp: > 0 } last)
        {
            return null;
        }

        var samples = dir.HitPoints.SamplesAround(id, last.At - BossPaceOver, last.At);
        return samples.Count >= 2 ? KillTime(samples[0].Hp, samples[0].At, last.Hp, last.At) : null;
    }

    /// <summary>The time to go from <paramref name="nowHp"/> to 0 at the pace from the earlier reading to the latest; null without a fall over at least 3 s.</summary>
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

    /// <summary>Fills the line (also the pictures of the meter): the boss, then the pace against the own best and the tries when there are any.</summary>
    internal void ShowBossBar(string name, long hp, long max, TimeSpan? kill, TimeSpan? rage, (double Now, double Best)? pace = null, (int Try, double Best, double Last)? tries = null)
    {
        if (_bossNumbers is null)
        {
            BuildBossBar();
        }

        LocalizationManager loc = LocalizationManager.Instance;
        _bossNumbers!.Text = max > 0 ? $"{PlayerRow.Compact(hp)} / {PlayerRow.Compact(max)}" : PlayerRow.Compact(hp);
        // the header names the boss already: the line says how much of it is left instead of naming it twice
        bool named = name.Length > 0 && OverlayTargetText.Text is { Length: > 0 } header && header.Contains(name, StringComparison.OrdinalIgnoreCase);
        _bossName!.Text = named && max > 0 ? Percent(Math.Clamp((double)hp / max, 0, 1)) + " left" : name.Length > 0 ? name : loc["Overlay.BossBar.Boss"];
        bool rageFirst = kill is TimeSpan k && rage is TimeSpan r && r > TimeSpan.Zero && k > r;
        _bossKill!.Text = kill is TimeSpan dies ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Kill"], Clock(dies)) : loc["Overlay.BossLine.NoKill"];
        _bossKill.Foreground = kill is null ? OverlayPictures.Muted : rageFirst ? OverlayPictures.Red : new SolidColorBrush(Color.FromRgb(0x6C, 0xD4, 0x8A));
        _bossKill.ToolTip = loc["Overlay.BossBar.DiesTip"];
        if (rage is TimeSpan left)
        {
            _bossRage!.Text = left > TimeSpan.Zero ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Enrage"], Clock(left)) : loc["Overlay.Enraged"];
            _bossRage.Foreground = left <= HpBarView.RageSoonBefore ? OverlayPictures.Red : left <= HpBarView.RageWarnBefore ? OverlayPictures.Amber : OverlayPictures.Muted;
            _bossRage.Visibility = Visibility.Visible;
        }
        else
        {
            _bossRage!.Visibility = Visibility.Collapsed;
        }

        if (pace is { } p)
        {
            double change = (p.Now - p.Best) / p.Best;
            bool ahead = change >= 0;
            _bossPace!.Text = (ahead ? "▲ " : "▼ ") + Math.Abs(change * 100).ToString(Math.Abs(change) < 0.1 ? "0.#" : "0", CultureInfo.CurrentCulture) + "% vs your best "
                + PlayerRow.Compact((long)p.Best);
            _bossPace.Foreground = ahead ? new SolidColorBrush(Color.FromRgb(0x6C, 0xD4, 0x8A)) : Math.Abs(change) < 0.05 ? OverlayPictures.Amber : OverlayPictures.Red;
            _bossPace.ToolTip = "Your DPS now against your best kill of this boss on this character (History)";
            _bossPace.Visibility = Visibility.Visible;
        }
        else
        {
            _bossPace!.Visibility = Visibility.Collapsed;
        }

        if (tries is { } t)
        {
            _bossTries!.Text = $"Try {t.Try} · best {Percent(t.Best)} · last {Percent(t.Last)}";
            _bossTries.ToolTip = "The boss's health at the lowest any try got it, and where the try before this one left it (the tries of the last hours since its last kill)";
            _bossTries.Visibility = Visibility.Visible;
        }
        else
        {
            _bossTries!.Visibility = Visibility.Collapsed;
        }

        _bossSecond!.Visibility = pace is null && tries is null ? Visibility.Collapsed : Visibility.Visible;
        OverlayBossBar.Visibility = Visibility.Visible;

        static string Percent(double fraction) => (fraction * 100).ToString(fraction < 0.1 ? "0.#" : "0", CultureInfo.CurrentCulture) + "%";

        static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>
    /// One line of text, no bar (5 November 2026, the owner: "simple, not a huge bar, just a text like 50m/120m and time to enrage / time to kill"): what is left
    /// of the boss and its whole, its name, then when it dies and its enrage on the right.
    /// </summary>
    private void BuildBossBar()
    {
        var line = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _bossKill = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) };
        _bossKill.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(12));
        right.Children.Add(_bossKill);
        _bossRage = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) };
        _bossRage.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(12));
        right.Children.Add(_bossRage);
        DockPanel.SetDock(right, Dock.Right);
        line.Children.Add(right);
        _bossNumbers = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _bossNumbers.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        _bossNumbers.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(12));
        DockPanel.SetDock(_bossNumbers, Dock.Left);
        line.Children.Add(_bossNumbers);
        _bossName = new TextBlock { Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = OverlayPictures.Muted };
        _bossName.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        line.Children.Add(_bossName);

        // under it: the pace against the own best on the left, the tries on the right
        _bossSecond = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
        _bossTries = new TextBlock { Foreground = OverlayPictures.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        _bossTries.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        DockPanel.SetDock(_bossTries, Dock.Right);
        _bossSecond.Children.Add(_bossTries);
        _bossPace = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _bossPace.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        _bossSecond.Children.Add(_bossPace);
        var both = new StackPanel();
        both.Children.Add(line);
        both.Children.Add(_bossSecond);
        OverlayBossBar.Child = both;
    }
}
