using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>
/// The Characters page in the look of Journey and Legion (6 November 2026, the owner: "design the other pages better now that we have an idea of what we
/// want"): a title over the page, the characters as cards in their class colours, the roster's totals as a board of figures and every character as a card
/// of its counters (no table to scroll sideways), each character's page under a banner in its class colour, the sections under a gold stroke.
/// </summary>
public sealed partial class ChecklistWindow
{
    /// <summary>The page's title and what it is, over the characters and the page.</summary>
    private static UIElement PageHead()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var head = new StackPanel { Margin = new Thickness(4, 2, 4, 14) };
        TextBlock title = Label(loc["Checklist.PageTitle"], "Brush.Text", 26);
        ThemeManager.UseTitleFont(title);
        head.Children.Add(title);
        TextBlock lede = Label(loc["Checklist.PageLede"], "Brush.TextMuted", 13);
        lede.TextWrapping = TextWrapping.Wrap;
        lede.Margin = new Thickness(0, 4, 0, 0);
        head.Children.Add(lede);
        return head;
    }

    /// <summary>A section's title: a short gold stroke, then the text.</summary>
    private static FrameworkElement SectionTitle(string text, double size = 15, string brush = "Brush.Text")
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var stroke = new Border { Width = 3, Height = size, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        stroke.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        line.Children.Add(stroke);
        line.Children.Add(Label(text, brush, size, bold: true));
        return line;
    }

    /// <summary>A small round "i" whose tool tip holds what used to be a paragraph on the page.</summary>
    private static UIElement InfoMark(string tip)
    {
        var mark = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Cursor = Cursors.Help, ToolTip = new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } };
        mark.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        TextBlock i = Label("i", "Brush.TextMuted", 10.5, bold: true);
        i.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Child = i;
        ToolTipService.SetInitialShowDelay(mark, 100);
        ToolTipService.SetShowDuration(mark, 60_000);
        return mark;
    }

    private static Color Resource(string key, Color fallback) => Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    private static SolidColorBrush Tint(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    /// <summary>One figure of a board: a coloured dot and a caption in capitals, the figure large, a note small under it.</summary>
    private sealed record Score(string Caption, UIElement Value, UIElement? Note, string Dot, string? Tip = null);

    /// <summary>
    /// A board of figures with lines between the cells: all of them in one line where the page is wide enough, else in lines of three (the roster's resets,
    /// energy, claims, whom to play next, the daily tasks): kept low, so the characters under it are in sight.
    /// </summary>
    private static UIElement Scoreboard(IReadOnlyList<Score> cells)
    {
        var grid = new Grid();
        var borders = new List<Border>();
        foreach (Score cell in cells)
        {
            var stack = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, cell.Dot);
            top.Children.Add(dot);
            TextBlock caption = Label(cell.Caption.ToUpper(CultureInfo.CurrentCulture), "Brush.TextMuted", 9.5, bold: true);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            top.Children.Add(caption);
            stack.Children.Add(top);
            if (cell.Value is FrameworkElement value)
            {
                value.Margin = new Thickness(0, 4, 0, 0);
            }

            stack.Children.Add(cell.Value);
            if (cell.Note is not null)
            {
                stack.Children.Add(cell.Note);
            }

            var border = new Border { Child = stack, Padding = new Thickness(14, 10, 12, 10), Background = Brushes.Transparent, ToolTip = cell.Tip };
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            borders.Add(border);
            grid.Children.Add(border);
        }

        int placed = 0;
        void Place(int columns)
        {
            if (placed == columns)
            {
                return;
            }

            placed = columns;
            int rows = (borders.Count + columns - 1) / columns;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (int c = 0; c < columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < borders.Count; i++)
            {
                int column = i % columns, row = i / columns;
                Grid.SetColumn(borders[i], column);
                Grid.SetRow(borders[i], row);
                borders[i].BorderThickness = new Thickness(0, 0, column < columns - 1 && i < borders.Count - 1 ? 1 : 0, row < rows - 1 ? 1 : 0);
            }
        }

        Place(Math.Min(3, Math.Max(1, borders.Count)));
        grid.SizeChanged += (_, e) => Place(e.NewSize.Width >= 140 * borders.Count ? borders.Count : Math.Min(3, borders.Count));
        var panel = new Border { Child = grid, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return panel;
    }

    /// <summary>A figure of the board: large, in a brush of the theme.</summary>
    private static TextBlock Big(string text, string brush = "Brush.Text")
    {
        TextBlock big = Label(text, brush, 19, bold: true);
        big.TextTrimming = TextTrimming.CharacterEllipsis;
        return big;
    }

    /// <summary>The small line under a figure.</summary>
    private static TextBlock Small(string text, string brush = "Brush.TextSubtle")
    {
        TextBlock small = Label(text, brush, 11.5);
        small.TextTrimming = TextTrimming.CharacterEllipsis;
        small.Margin = new Thickness(0, 1, 0, 0);
        return small;
    }

    /// <summary>
    /// The banner over a character's page: a wash of its class colour from the left with its emblem large and faint at the right, the name in the title face and
    /// class colour with its legion, class, level and server under it; on the right whether it is online or when the meter saw it last.
    /// </summary>
    private static UIElement Hero(TicketCharacter c)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        bool online = Aion2Tickets.CurrentName == c.Name;
        ImageSource? icon = className is not null ? new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null;

        var host = new Grid { ClipToBounds = true };
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x44, accent.R, accent.G, accent.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0.7));
        host.Children.Add(new Border { Background = wash });
        if (icon is not null)
        {
            host.Children.Add(new Image { Source = icon, Width = 150, Height = 150, Opacity = 0.10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -30, 150, -30), IsHitTestVisible = false });
        }

        var grid = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (icon is not null)
        {
            var ring = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), BorderThickness = new Thickness(2), BorderBrush = Tint(accent, 0xC0), Background = Tint(accent, 0x1A),
                Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Child = new Image { Source = icon, Width = 36, Height = 36 } };
            grid.Children.Add(ring);
        }

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock name = Label(c.Name, "Brush.Text", 28);
        ThemeManager.UseTitleFont(name);
        if (className is not null)
        {
            name.Foreground = ClassBrushes.Text(className);
        }

        nameLine.Children.Add(name);
        if (!string.IsNullOrEmpty(c.Legion))
        {
            TextBlock legion = Label("<" + c.Legion + ">", "Brush.TextMuted", 13);
            legion.Margin = new Thickness(10, 0, 0, 4);
            legion.VerticalAlignment = VerticalAlignment.Bottom;
            nameLine.Children.Add(legion);
        }

        who.Children.Add(nameLine);
        var parts = new List<string>();
        if (className is not null)
        {
            parts.Add(ClassCatalog.DisplayName(className, language));
        }

        if (Strength(c, loc) is { } strength)
        {
            parts.Add(strength);
        }

        if (c.ServerId > 0)
        {
            parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId));
        }

        if (parts.Count > 0)
        {
            TextBlock info = Label(string.Join("  ·  ", parts), "Brush.TextMuted", 13);
            info.Margin = new Thickness(0, 2, 0, 0);
            who.Children.Add(info);
        }

        Grid.SetColumn(who, 1);
        grid.Children.Add(who);

        var state = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        if (online)
        {
            var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 11, 4), HorizontalAlignment = HorizontalAlignment.Right };
            pill.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            pill.Child = Label("●  " + loc["Checklist.OnlineNow"], "Brush.Heal", 12, bold: true);
            state.Children.Add(pill);
        }
        else
        {
            if (c.LastOnlineMs > 0)
            {
                TextBlock last = Label(string.Format(CultureInfo.CurrentCulture, loc["Checklist.LastOnline"], DateTimeOffset.FromUnixTimeMilliseconds(c.LastOnlineMs).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)), "Brush.Text", 12);
                last.HorizontalAlignment = HorizontalAlignment.Right;
                state.Children.Add(last);
            }

            TextBlock asOf = Label(loc["Checklist.RelogOld"], "Brush.TextSubtle", 11);
            asOf.TextWrapping = TextWrapping.Wrap;
            asOf.TextAlignment = TextAlignment.Right;
            asOf.MaxWidth = 260;
            asOf.HorizontalAlignment = HorizontalAlignment.Right;
            state.Children.Add(asOf);
        }

        Grid.SetColumn(state, 2);
        grid.Children.Add(state);
        if (Aion2Tickets.CurrentName is null)
        {
            // the counters come with the login: a meter started after the login has not read the character's yet
            TextBlock hint = Label(loc["Checklist.NeedLogin"], "Brush.Accent", 12);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(hint, 1);
            Grid.SetColumnSpan(hint, 3);
            grid.Children.Add(hint);
        }

        host.Children.Add(grid);
        var card = new Border { Child = host, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12), BorderBrush = Tint(accent, 0x60) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 11, 11);
        return card;
    }
}
