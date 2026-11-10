using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>A death in a wipe: who, how far into the try, and the blow that did it.</summary>
public sealed record WipeDeath(string Name, string? ClassName, TimeSpan At, string? Killer, string? Skill, long Amount, long Taken);

/// <summary>
/// A boss try that ended without the kill (10 November 2026, the owner picked: "wipe review - after a wipe: who died first, what killed them, which mechanic went
/// wrong, and how it compared with your best try"): the deaths in order with the blow that did each (the hits of their last seconds, as the death recap works them
/// out), the skill that took down the most of the group, the health the boss was left at against the best try of the last hours.
/// </summary>
public sealed record WipeReview(string Boss, DateTime Start, TimeSpan Length, int? Try, double? HpLeft, double? Best, IReadOnlyList<WipeDeath> Deaths, string? Mechanic,
    int MechanicDeaths, int Players)
{
    /// <summary>The review of one try from its deaths and its hits.</summary>
    public static WipeReview Build(string boss, DateTime start, DateTime end, int? tryNumber, double? hpLeft, double? best, IReadOnlyList<DeathRecord> deaths,
        IReadOnlyList<DamageEvent> events, Func<int, string> nameOf, Func<int, string?> classOf, Func<int, bool> isPlayer, int players)
    {
        var list = deaths.OrderBy(d => d.At).Select(d =>
        {
            DeathRecap r = Aion2Pvp.Build(d.Entity, d.At, events, nameOf, classOf, isPlayer);
            return new WipeDeath(r.VictimName, classOf(d.Entity), d.At - start, r.Killer?.Name, r.KillingSkill, r.KillingAmount, r.Taken);
        }).ToList();
        var mechanic = list.Where(x => x.Skill is not null).GroupBy(x => x.Skill!).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(x => x.At)).FirstOrDefault();
        return new WipeReview(boss, start, end - start, tryNumber, hpLeft, best, list, mechanic?.Key, mechanic?.Count() ?? 0, players);
    }

    /// <summary>"2:14".</summary>
    public static string Clock(TimeSpan t) => t < TimeSpan.Zero ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>The review on the meter, over the lines (<paramref name="close"/>: the ✕, null in the pictures).</summary>
    public static FrameworkElement MeterView(WipeReview w, Action? close)
    {
        Color red = Color.FromRgb(0xFF, 0x7A, 0x6E);
        Color muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
        Color faint = Color.FromRgb(0x6F, 0x87, 0x97);
        var stack = new StackPanel();

        // the head: WIPE · TRY 4 · 31% LEFT, the best try on the right, the ✕
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        if (close is not null)
        {
            var x = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(muted), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Close the review" };
            x.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                close();
            };
            DockPanel.SetDock(x, Dock.Right);
            head.Children.Add(x);
        }

        if (w.Best is double best && w.HpLeft is double left)
        {
            bool closer = left < best - 0.0005;
            var compare = new TextBlock
            {
                Text = closer ? "your best yet" : $"best {best:P0}", FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(closer ? OverlaySkin.Good : OverlaySkin.Gold),
            };
            DockPanel.SetDock(compare, Dock.Right);
            head.Children.Add(compare);
        }

        string title = "WIPE" + (w.Try is int n ? $"  ·  TRY {n}" : "") + (w.HpLeft is double hp ? $"  ·  {hp:P0} LEFT" : "");
        head.Children.Add(new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(red), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);

        // the deaths in order: 1. Lunessa  2:14  Searing Breath 12.4K
        int i = 0;
        foreach (WipeDeath d in w.Deaths.Take(6))
        {
            i++;
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.Children.Add(new TextBlock { Text = i.ToString(CultureInfo.CurrentCulture), FontSize = 11, Foreground = new SolidColorBrush(faint) });
            var name = new TextBlock { Text = d.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = d.ClassName is { } c ? ClassBrushes.Text(c) : Brushes.White };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var at = new TextBlock { Text = Clock(d.At), FontSize = 11, Margin = new Thickness(8, 0, 8, 0), Foreground = new SolidColorBrush(muted) };
            at.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            Grid.SetColumn(at, 2);
            grid.Children.Add(at);
            var blow = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Right,
                ToolTip = d.Killer is { } k ? $"{k}: {d.Skill} {d.Amount:N0} (took {d.Taken:N0} in the last seconds)" : null };
            blow.Inlines.Add(new System.Windows.Documents.Run(d.Skill ?? "no hit seen") { Foreground = new SolidColorBrush(d.Skill == w.Mechanic && w.MechanicDeaths > 1 ? red : muted) });
            if (d.Amount > 0)
            {
                blow.Inlines.Add(new System.Windows.Documents.Run("  " + d.Amount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            }

            Grid.SetColumn(blow, 3);
            grid.Children.Add(blow);
            stack.Children.Add(grid);
        }

        if (w.Deaths.Count > 6)
        {
            stack.Children.Add(new TextBlock { Text = $"+ {w.Deaths.Count - 6} more", FontSize = 10.5, Foreground = new SolidColorBrush(faint) });
        }

        // what went wrong, in one line
        string summary = w.Mechanic is { } m && w.MechanicDeaths > 1 ? $"{m} took down {w.MechanicDeaths} of {Math.Max(w.Players, w.Deaths.Count)}"
            : w.Deaths.Count > 0 ? $"First down {Clock(w.Deaths[0].At)} into the try" : "No deaths seen";
        summary += $"  ·  the try lasted {Clock(w.Length)}";
        stack.Children.Add(new TextBlock { Text = summary, FontSize = 10.5, Foreground = new SolidColorBrush(faint), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = stack, Padding = new Thickness(9, 6, 9, 7), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x14, red.R, red.G, red.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, red.R, red.G, red.B)),
        };
    }

    /// <summary>A made-up wipe for the pictures.</summary>
    public static WipeReview Sample(DateTime now) => new("Ultimate Berk", now.AddMinutes(-4), TimeSpan.FromSeconds(171), 4, 0.31, 0.18, new[]
    {
        new WipeDeath("Draegor", "Cleric", TimeSpan.FromSeconds(134), "Ultimate Berk", "Searing Breath", 12_400, 18_900),
        new WipeDeath("Seraphyne", "Sorcerer", TimeSpan.FromSeconds(139), "Ultimate Berk", "Searing Breath", 11_800, 15_200),
        new WipeDeath("Nyxelle", "Ranger", TimeSpan.FromSeconds(151), "Ultimate Berk", "Tail Swipe", 9_800, 24_600),
        new WipeDeath("Lumiael", "Spiritmaster", TimeSpan.FromSeconds(168), "Ultimate Berk", "Searing Breath", 15_100, 21_000),
    }, "Searing Breath", 3, 4);
}
