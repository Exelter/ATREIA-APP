using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Ui;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The meter's Stagger view (the Windows meter's, Client/Ui/StaggerCardView.cs: MeterView, Card in short, Chips, Rows): in place of the lines, the boss's latest
/// stagger check - its mechanic and how it went, a thin bar of what is left, every player of the group with the exact amount they took off it (0 for one who
/// took nothing off) and the skills that did it, each with its hits on hover - and the boss's earlier checks as chips to click.
/// </summary>
internal static class StaggerView
{
    private static readonly Color Blue = Color.FromRgb(0x6F, 0x9F, 0xD8);
    private static readonly Color GreenColor = Color.FromRgb(0x5B, 0xD0, 0x7A), RedColor = Color.FromRgb(0xFF, 0x5A, 0x5A), AmberColor = Color.FromRgb(0xFF, 0xBA, 0x42);
    private static readonly Color MutedColor = Color.FromRgb(0x9D, 0xB3, 0xC2);

    private static IBrush Brush(Color c, byte alpha = 0xFF) => new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)).ToImmutable();

    /// <summary>The check's title: its mechanic's name, else "Stagger bar" for a bar that is always up.</summary>
    private static string Title(StaggerCheck check) => check.Mechanic != 0 ? Aion2SkillNames.DisplayOf(check.Mechanic) : "Stagger bar";

    /// <summary>How the check went, in a few words, and its colour.</summary>
    private static (string Text, Color Color) Status(StaggerCheck check, DateTime now)
    {
        string S(double s) => s.ToString(s < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " s";
        return check.Outcome switch
        {
            StaggerOutcome.Broken => ("✓ Broken in " + S(check.Seconds(now)) + (check.Limit is double lim ? " of " + S(lim) : ""), GreenColor),
            StaggerOutcome.Failed => ("✗ Not broken: " + check.Left.ToString("N0", CultureInfo.CurrentCulture) + " of " + check.Max.ToString("N0", CultureInfo.CurrentCulture) + " left", RedColor),
            StaggerOutcome.Ended => ("Ended: " + check.Left.ToString("N0", CultureInfo.CurrentCulture) + " of " + check.Max.ToString("N0", CultureInfo.CurrentCulture) + " left", MutedColor),
            _ => check.TimeLeft(now) is double left
                ? (S(left) + " left · " + check.Left.ToString("N0", CultureInfo.CurrentCulture) + " to go", left <= 3 ? RedColor : AmberColor)
                : (check.Left.ToString("N0", CultureInfo.CurrentCulture) + " of " + check.Max.ToString("N0", CultureInfo.CurrentCulture) + " to go", AmberColor),
        };
    }

    private static TextBlock Text(string text, double size, IBrush? brush = null, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = brush ?? Palette.OverlayText,
        FontFamily = Palette.UiFont,
    };

    /// <summary>The view: the shown check in short with the group's lines, the earlier checks as chips under it; with no check yet a line says what will show.</summary>
    public static Control MeterView(IReadOnlyList<StaggerCheck> checks, StaggerCheck? shown, DateTime now, IReadOnlyList<StaggerPerson> people,
        Func<int, StaggerPerson>? nameOf, string? next, Action<StaggerCheck>? pick)
    {
        if (shown is null)
        {
            var empty = new StackPanel();
            TextBlock line = Text("No stagger check yet. When a boss's stagger bar comes up, this shows who takes how much off it and with which skills.", 11, Brush(MutedColor));
            line.TextWrapping = TextWrapping.Wrap;
            empty.Children.Add(line);
            if (next is not null)
            {
                TextBlock n = Text(next, 10.5, Brush(AmberColor), true);
                n.Margin = new Thickness(0, 3, 0, 0);
                empty.Children.Add(n);
            }

            return empty;
        }

        var root = new StackPanel();
        root.Children.Add(Card(shown, now, people, nameOf, next));
        if (checks.Count > 1)
        {
            WrapPanel chips = Chips(checks, shown, now, pick);
            chips.Margin = new Thickness(0, 4, 0, 0);
            root.Children.Add(chips);
        }

        return root;
    }

    /// <summary>A check in short (Windows: Card with compact): what started it and how it went, a thin bar of what is left, the players, the next check expected.</summary>
    private static Control Card(StaggerCheck check, DateTime now, IReadOnlyList<StaggerPerson> people, Func<int, StaggerPerson>? nameOf, string? next)
    {
        var root = new StackPanel();
        var head = new DockPanel();
        (string status, Color statusColor) = Status(check, now);
        TextBlock statusText = Text(status, 11.5, Brush(statusColor), bold: true);
        statusText.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(statusText, Dock.Right);
        head.Children.Add(statusText);
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5, FontFamily = Palette.UiFont };
        title.Inlines = new InlineCollection
        {
            new Run("◆ STAGGER  ") { Foreground = Brush(Blue), FontWeight = FontWeight.SemiBold, FontSize = 11.5 * 0.85 },
            new Run(Title(check)) { FontWeight = FontWeight.SemiBold, Foreground = Palette.OverlayText },
        };
        ToolTip.SetTip(title, check.Mechanic != 0
            ? $"The boss's cast \"{Title(check)}\" started the check: its stagger bar ({check.Max:N0}) has to break before the cast ends ({check.Limit:0.#} s)."
            : $"The boss's stagger bar ({check.Max:N0}): break it to stagger the boss.");
        head.Children.Add(title);
        root.Children.Add(head);

        // a thin bar of what is left
        var slim = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 4) };
        double fill = check.Max > 0 ? Math.Clamp((double)check.Left / check.Max, 0, 1) : 0;
        slim.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fill, 0.0001), GridUnitType.Star));
        slim.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fill, 0.0001), GridUnitType.Star));
        slim.Children.Add(new Border { Background = Brush(Blue, 0xD0), CornerRadius = new CornerRadius(2) });
        var track = new Border { Background = Brush(Colors.White, 0x14), CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(track, 1);
        slim.Children.Add(track);
        root.Children.Add(slim);

        root.Children.Add(Rows(check.Credit, check.Used, people, nameOf, "seconds into the check", "in this check"));
        if (next is not null)
        {
            TextBlock n = Text(next, 10.5, Brush(AmberColor), bold: true);
            n.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(n);
        }

        return root;
    }

    /// <summary>The boss's checks as chips, oldest first: ✓ broken (its time), ✗ not (what was left), the shown one marked; a click picks one.</summary>
    private static WrapPanel Chips(IReadOnlyList<StaggerCheck> checks, StaggerCheck? shown, DateTime now, Action<StaggerCheck>? pick)
    {
        var chips = new WrapPanel();
        Border Chip(string label, Color color, bool on, string tip, Action? click)
        {
            var chip = new Border
            {
                Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(0, 0, 4, 2), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                BorderBrush = Brush(color, (byte)(on ? 0xC0 : 0x50)), Background = Brush(color, (byte)(on ? 0x30 : 0x10)),
                Child = Text(label, 10, Brush(color), on), Cursor = click is not null ? new Cursor(StandardCursorType.Hand) : null,
            };
            ToolTip.SetTip(chip, tip);
            if (click is not null)
            {
                chip.PointerReleased += (_, e) =>
                {
                    e.Handled = true;
                    click();
                };
            }

            return chip;
        }

        TextBlock title = Text("Checks", 10, Brush(MutedColor), true);
        title.Margin = new Thickness(0, 0, 6, 2);
        chips.Children.Add(title);
        int n = 0;
        foreach (StaggerCheck c in checks)
        {
            n++;
            (string status, Color color) = Status(c, now);
            string mark = c.Outcome switch
            {
                StaggerOutcome.Broken => "✓ " + c.Seconds(now).ToString("0.0", CultureInfo.CurrentCulture) + " s",
                StaggerOutcome.Failed => "✗ " + c.Left.ToString("N0", CultureInfo.CurrentCulture) + " left",
                StaggerOutcome.Ended => "– " + c.Left.ToString("N0", CultureInfo.CurrentCulture) + " left",
                _ => "● now",
            };
            StaggerCheck picked = c;
            chips.Children.Add(Chip(n.ToString(CultureInfo.CurrentCulture) + "  " + mark, color, shown is not null && c.Start == shown.Start,
                $"Check {n}: {Title(c)} at {c.Start.ToString("HH:mm:ss", CultureInfo.CurrentCulture)} - {status}", pick is null ? null : () => pick(picked)));
        }

        return chips;
    }

    /// <summary>The players' lines of a check: the most first, every player of <paramref name="people"/> who took nothing off with 0, each with the skills that did it
    /// (hover them) and, faded, those they hit with that took nothing off.</summary>
    private static StackPanel Rows(StaggerReport credit, IReadOnlyList<StaggerUse> used, IReadOnlyList<StaggerPerson> people, Func<int, StaggerPerson>? nameOf, string when, string where)
    {
        var root = new StackPanel();
        var lines = credit.Players.ToDictionary(p => p.Player);
        var shown = new List<StaggerPerson>();
        foreach (StaggerLine line in credit.Players)
        {
            shown.Add(people.FirstOrDefault(p => p.Id == line.Player) ?? nameOf?.Invoke(line.Player) ?? new StaggerPerson(line.Player, "#" + line.Player, ""));
        }

        foreach (StaggerPerson p in people.Where(p => !lines.ContainsKey(p.Id)).OrderBy(p => p.Name, StringComparer.CurrentCulture))
        {
            shown.Add(p);
        }

        int total = credit.Total;
        int best = credit.Players.Select(p => p.Amount).DefaultIfEmpty(0).Max();
        foreach (StaggerPerson p in shown)
        {
            root.Children.Add(PlayerLine(p, lines.GetValueOrDefault(p.Id), credit, used, total, best, when, where));
        }

        int unknown = credit.Hits.Where(h => h.Player == 0).Sum(h => h.Amount);
        if (unknown > 0)
        {
            TextBlock u = Text($"+{unknown:N0} with no hit of the group just before it (a hit the meter didn't see)", 10, Brush(MutedColor));
            u.Margin = new Thickness(0, 2, 0, 0);
            u.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(u);
        }

        return root;
    }

    private static Control PlayerLine(StaggerPerson p, StaggerLine? line, StaggerReport credit, IReadOnlyList<StaggerUse> used, int total, int best, string when, string where)
    {
        int amount = line?.Amount ?? 0;
        Color cls = ClassLook.ColorOf(p.ClassName);
        var grid = new Grid { MinHeight = 18, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(46, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(40, GridUnitType.Pixel));

        // a bar behind, as long as the player's part against the most anyone took off
        var bar = new Grid();
        double part = best > 0 ? (double)amount / best : 0;
        bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(part, 0.0001), GridUnitType.Star));
        bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - part, 0.0001), GridUnitType.Star));
        bar.Children.Add(new Border { Background = Brush(cls, 0x30), CornerRadius = new CornerRadius(3) });
        Grid.SetColumnSpan(bar, 4);
        grid.Children.Add(bar);

        TextBlock name = Text(p.Name, 11.5, Brush(amount > 0 ? cls : MutedColor), true);
        name.Margin = new Thickness(5, 0, 6, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(name, PlayerTip(p, line, used, total, where));
        ToolTip.SetShowDelay(name, 150);
        grid.Children.Add(name);

        // the skills that took it off, as icons: hovering one tells what it did; then, faded, those they hit with that took nothing off
        var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        IReadOnlyList<StaggerSkill> staggered = line?.Skills ?? (IReadOnlyList<StaggerSkill>)Array.Empty<StaggerSkill>();
        const double size = 14;
        foreach (StaggerSkill s in staggered.Take(5))
        {
            icons.Children.Add(SkillIcon(s, p, credit, size, when));
        }

        var did = staggered.Select(s => s.SkillId).ToHashSet();
        foreach (StaggerUse u in used.Where(u => u.Player == p.Id && !did.Contains(u.SkillId) && u.SkillId != 0).OrderByDescending(u => u.Hits).Take(amount > 0 ? 2 : 4))
        {
            icons.Children.Add(UsedIcon(u, p, size, where));
        }

        Grid.SetColumn(icons, 1);
        grid.Children.Add(icons);

        TextBlock value = Text(amount.ToString("N0", CultureInfo.CurrentCulture), 12, amount > 0 ? null : Brush(RedColor), true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTip.SetTip(value, amount > 0 ? $"{p.Name} took {amount:N0} off the bar {where}" : $"{p.Name} took nothing off the bar {where}");
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        TextBlock share = Text(total > 0 && amount > 0 ? (100.0 * amount / total).ToString("0", CultureInfo.CurrentCulture) + "%" : "–", 11, Brush(MutedColor));
        share.HorizontalAlignment = HorizontalAlignment.Right;
        share.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(share, 3);
        grid.Children.Add(share);
        return grid;
    }

    private static Control SkillIcon(StaggerSkill s, StaggerPerson p, StaggerReport credit, double size, string when)
    {
        Bitmap? picture = Pictures.Load((s.SkillId != 0 ? Aion2SkillIcons.PathFor(s.SkillId) : null) ?? Aion2SkillIcons.PathForName(s.Name, p.ClassName));
        Control inner = picture is not null
            ? new Image { Source = picture, Stretch = Stretch.UniformToFill }
            : Centered(Text(s.Name.Length > 0 ? s.Name[..1] : "?", size * 0.55, Brush(Blue), true));
        var icon = new Border
        {
            Width = size, Height = size, Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(3), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = Brush(Blue, 0xA0), Background = Brush(Blue, 0x22), Child = inner,
        };
        ToolTip.SetTip(icon, SkillTip(s, p, credit, when));
        ToolTip.SetShowDelay(icon, 100);
        return icon;
    }

    /// <summary>A skill the player hit the boss with in the check that took nothing off the bar: faded, its hits on hover.</summary>
    private static Control UsedIcon(StaggerUse u, StaggerPerson p, double size, string where)
    {
        string name = Aion2SkillNames.DisplayOf(u.SkillId);
        Bitmap? picture = Pictures.Load(Aion2SkillIcons.PathFor(u.SkillId) ?? Aion2SkillIcons.PathForName(Aion2SkillNames.NameOf(u.SkillId), p.ClassName));
        Control inner = picture is not null
            ? new Image { Source = picture, Stretch = Stretch.UniformToFill }
            : Centered(Text(name.Length > 0 ? name[..1] : "?", size * 0.55, Brush(MutedColor)));
        var tip = new StackPanel { MaxWidth = 320 };
        tip.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        tip.Children.Add(new TextBlock { Text = $"{p.Name} hit the boss with it {u.Hits} time{(u.Hits == 1 ? "" : "s")} {where}: none of those hits took anything off the stagger bar.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var icon = new Border
        {
            Width = size, Height = size, Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(3), ClipToBounds = true, BorderThickness = new Thickness(1),
            BorderBrush = Brush(MutedColor, 0x50), Child = inner, Opacity = 0.45,
        };
        ToolTip.SetTip(icon, tip);
        ToolTip.SetShowDelay(icon, 100);
        return icon;
    }

    private static Control Centered(TextBlock text)
    {
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    /// <summary>A skill's part: its name, its hits that took something off and what each did (half as much again with a stagger buff), and when they came.</summary>
    internal static Control SkillTip(StaggerSkill s, StaggerPerson p, StaggerReport credit, string when)
    {
        var hits = credit.Hits.Where(h => h.Player == p.Id && h.SkillId == s.SkillId).OrderBy(h => h.At).ToList();
        var panel = new StackPanel { MaxWidth = 340 };
        string title = s.Name.Length > 0
            ? Aion2SkillNames.DisplayOf(s.SkillId) is { Length: > 0 } shown && shown != s.SkillId.ToString(CultureInfo.InvariantCulture) ? shown : s.Name
            : "Attack";
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        panel.Children.Add(new TextBlock { Text = $"{p.Name}: {s.Amount:N0} off the bar in {s.Hits} hit{(s.Hits == 1 ? "" : "s")}", FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
        int boosted = hits.Count(h => h.Boosted);
        var amounts = hits.GroupBy(h => h.Amount).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} × {g.Key}").ToList();
        if (amounts.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Each hit: " + string.Join(", ", amounts) + (boosted > 0 ? $"  ({boosted} with a stagger buff: half as much again)" : ""), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        }

        if (hits.Count > 0)
        {
            string times = string.Join(", ", hits.Take(16).Select(h => h.At.ToString("0.0", CultureInfo.CurrentCulture) + " s"));
            panel.Children.Add(new TextBlock { Text = $"When ({when}): " + times + (hits.Count > 16 ? " …" : ""), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }

        if (hits.Any(h => !h.Exact))
        {
            panel.Children.Add(new TextBlock { Text = "Some of it went to the nearest likely hit (the amount matched no skill exactly).", FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }

        return panel;
    }

    /// <summary>A player's part: the amount, the share, the skills with what each took off.</summary>
    private static Control PlayerTip(StaggerPerson p, StaggerLine? line, IReadOnlyList<StaggerUse> used, int total, string where)
    {
        var panel = new StackPanel { MaxWidth = 340 };
        panel.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        var others = used.Where(u => u.Player == p.Id && u.SkillId != 0 && line?.Skills.All(s => s.SkillId != u.SkillId) != false).OrderByDescending(u => u.Hits).ToList();
        string Others() => string.Join(", ", others.Take(8).Select(u => $"{Aion2SkillNames.DisplayOf(u.SkillId)} ×{u.Hits}"));
        if (line is null || line.Amount <= 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = others.Count > 0
                    ? $"Took nothing off the boss's stagger bar {where}. Hit the boss with: " + Others() + " (none of it lowered the bar)."
                    : $"Took nothing off the boss's stagger bar {where}, and the meter saw no hit of theirs on the boss.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });
            return panel;
        }

        panel.Children.Add(new TextBlock { Text = $"{line.Amount:N0} off the bar ({(total > 0 ? 100.0 * line.Amount / total : 0):0} % of what came off){(line.Breaks > 0 ? " · finished the break" : "")}", FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
        foreach (StaggerSkill s in line.Skills)
        {
            panel.Children.Add(new TextBlock { Text = $"{s.Amount,6:N0}   {s.Name} ({s.Hits} hit{(s.Hits == 1 ? "" : "s")})", FontSize = 11.5, FontFamily = new FontFamily("monospace") });
        }

        if (others.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Also hit with, taking nothing off: " + Others(), FontSize = 11.5, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }

        return panel;
    }
}
