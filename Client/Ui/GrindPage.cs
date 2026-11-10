using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The Grind tool (10 November 2026, the owner: "we have the loot tab, change it to grind instead, and then have the loot tab separately in the grind"). Two tabs:
/// Grind - the session going on in big (Kinah, Abyss Points, kills and loot an hour, what came in, the time farmed, the map), the loot it brought with its
/// worth, and the earlier sessions to compare a map or a character against another - and Loot, the loot log as it was (<see cref="LootPage"/>).
/// </summary>
public sealed class GrindPage : StackPanel
{
    private static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);
    private static readonly Color Good = Color.FromRgb(0x6C, 0xD4, 0x8A);

    private readonly LootPage _loot;
    private readonly Func<int?> _server;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
    private readonly StackPanel _grind = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _tab = "grind";
    private string _shown = "";

    /// <summary>For the pictures of the tool: the session going on, the earlier ones and the market, in place of the live ones.</summary>
    public static (GrindSession? Current, IReadOnlyList<GrindSession> Earlier, MarketBook? Book, DateTime Now)? Picture { get; set; }

    public GrindPage(LootPage loot, Func<int?> server)
    {
        _loot = loot;
        _server = server;
        MaxWidth = 1200;
        SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
        Children.Add(_tabs);
        Children.Add(_grind);
        Children.Add(_loot);
        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _refresh.Start();
            _shown = "";
            Refresh();
        };
        Unloaded += (_, _) => _refresh.Stop();
        Refresh();
    }

    /// <summary>Shows "grind" or "loot".</summary>
    public void ShowTab(string tab)
    {
        _tab = tab == "loot" ? "loot" : "grind";
        _shown = "";
        Refresh();
    }

    public void Stop()
    {
        _refresh.Stop();
        _loot.Stop();
    }

    private void Refresh()
    {
        DrawTabs();
        _grind.Visibility = _tab == "grind" ? Visibility.Visible : Visibility.Collapsed;
        _loot.Visibility = _tab == "loot" ? Visibility.Visible : Visibility.Collapsed;
        if (_tab != "grind")
        {
            return;
        }

        DateTime now = Picture?.Now ?? DateTime.Now;
        GrindSession? current = Picture is { } p ? p.Current : Aion2Grind.Current;
        IReadOnlyList<GrindSession> earlier = Picture?.Earlier ?? Aion2Grind.Sessions;
        MarketBook? book = Picture is { } pic ? pic.Book : _server() is int server ? ExchangePrices.For(server) : null;
        GrindFigures? f = current is null ? null : GrindFigures.Of(current, now, book);
        string key = string.Join("|", f?.FarmedText, current?.Id, current?.Kinah, current?.AbyssPoints, current?.Kills, f?.Items, f?.LootWorth, f?.Over, ExchangePrices.Version,
            string.Join(",", earlier.Select(e => e.Id)));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _grind.Children.Clear();
        _grind.Children.Add(Hero(f, now));
        if (f is not null && f.Session.Items.Count > 0)
        {
            _grind.Children.Add(LootCard(f.Session, book));
        }

        _grind.Children.Add(Earlier(earlier, book));
    }

    // ------------------------------------------------------------------ the tabs

    private void DrawTabs()
    {
        string key = _tab;
        if (_tabs.Tag as string == key)
        {
            return;
        }

        _tabs.Tag = key;
        _tabs.Children.Clear();
        _tabs.Children.Add(TabPill("grind", "\uE9D2", "Grind"));
        _tabs.Children.Add(TabPill("loot", "\uE7B8", LocalizationManager.Instance["Main.MenuTools.Loot"]));
    }

    private FrameworkElement TabPill(string id, string glyph, string text)
    {
        bool on = _tab == id;
        Color ink = Color.FromRgb(0x1A, 0x12, 0x05);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 8, 0), Foreground = new SolidColorBrush(on ? ink : Gold) });
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            label.Foreground = new SolidColorBrush(ink);
        }
        else
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        }

        line.Children.Add(label);
        var pill = new Border { Child = line, Padding = new Thickness(14, 7, 16, 8), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Cursor = on ? Cursors.Arrow : Cursors.Hand };
        if (on)
        {
            pill.Background = new SolidColorBrush(Gold);
            pill.BorderBrush = new SolidColorBrush(Gold);
        }
        else
        {
            pill.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            pill.MouseLeftButtonUp += (_, _) => ShowTab(id);
        }

        return pill;
    }

    // ------------------------------------------------------------------ the session going on

    private FrameworkElement Hero(GrindFigures? f, DateTime now)
    {
        var card = Card(new Thickness(22, 18, 22, 18));
        var stack = new StackPanel();
        card.Child = stack;
        if (f is null)
        {
            stack.Children.Add(Text("No grind yet", 17, "Brush.Text", bold: true));
            TextBlock body = Text("Go kill a few monsters: the Kinah and Abyss Points you farm, the kills and the loot show here, each with what it makes an hour. "
                + "The meter shows the same with its Grind switch.", 13, "Brush.TextMuted", margin: new Thickness(0, 6, 0, 0));
            body.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(body);
            return card;
        }

        GrindSession s = f.Session;
        var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(StatePill(f.Over));
        left.Children.Add(Text(s.Zone != 0 ? LootText.MapName(s.Zone) : "This session", 17, "Brush.Text", bold: true, margin: new Thickness(10, 0, 0, 0)));
        string who = (s.Character.Length > 0 ? "  ·  " + s.Character : "") + "  ·  since " + s.Start.ToString("HH:mm", CultureInfo.CurrentCulture) + "  ·  farmed " + f.FarmedText;
        left.Children.Add(Text(who, 12.5, "Brush.TextMuted", margin: new Thickness(4, 2, 0, 0)));
        head.Children.Add(left);
        if (Picture is null)
        {
            Border again = ActionButton("\uE72C", "New session", () =>
            {
                Aion2Grind.NewSession();
                _shown = "";
                Refresh();
            });
            again.ToolTip = "Start counting again from the next kill: this session goes to the earlier ones";
            DockPanel.SetDock(again, Dock.Right);
            head.Children.Add(again);
        }

        stack.Children.Add(head);
        var tiles = new UniformGrid { Columns = 4 };
        tiles.Children.Add(Tile("KINAH / HOUR", f.KinahPerHour is double k ? GrindView.Short(k) : "…", "+" + GrindView.Short(s.Kinah) + " this session", GrindView.KinahColour));
        tiles.Children.Add(Tile("ABYSS POINTS / HOUR", f.ApPerHour is double a ? GrindView.Short(a) : "…", "+" + GrindView.Short(s.AbyssPoints) + " this session", GrindView.ApColour));
        tiles.Children.Add(Tile("KILLS / HOUR", f.KillsPerHour is double kh ? kh.ToString("N0", CultureInfo.CurrentCulture) : "…", s.Kills.ToString("N0", CultureInfo.CurrentCulture) + " killed",
            GrindView.KillColour));
        tiles.Children.Add(Tile("LOOT / HOUR", f.LootPerHour is double lh ? GrindView.Short(lh) : f.Items == 0 ? "–" : "…",
            f.LootWorth is long w ? $"{f.Items:N0} items, ~{GrindView.Short(w)} at the market" : $"{f.Items:N0} items", GrindView.LootColour));
        stack.Children.Add(tiles);
        TextBlock note = Text(f.KinahPerHour is null ? "The hourly numbers come after the first minute farmed." :
            "Per hour is this session's average over the time farmed: a big kill lifts it at once, and it settles again as the minutes go by. Pauses of more than five minutes don't count.",
            11.5, "Brush.TextMuted", margin: new Thickness(0, 12, 0, 0));
        note.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(note);
        return card;
    }

    private static FrameworkElement StatePill(bool over)
    {
        Color colour = over ? Color.FromRgb(0x9A, 0xA4, 0xAE) : Good;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(colour), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new TextBlock { Text = over ? "Paused" : "Farming", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour) });
        return new Border
        {
            Child = line, Padding = new Thickness(8, 2, 10, 3), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x18, colour.R, colour.G, colour.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, colour.R, colour.G, colour.B)),
        };
    }

    private static FrameworkElement Tile(string title, string value, string sub, Color colour)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        stack.Children.Add(Text(title, 10.5, "Brush.TextMuted", bold: true));
        var number = new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour), Margin = new Thickness(0, 2, 0, 0) };
        number.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        stack.Children.Add(number);
        TextBlock subText = Text(sub, 11.5, "Brush.TextMuted");
        subText.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(subText);
        return stack;
    }

    // ------------------------------------------------------------------ the loot of the session

    private static FrameworkElement LootCard(GrindSession s, MarketBook? book)
    {
        var card = Card(new Thickness(18, 14, 18, 12));
        card.Margin = new Thickness(0, 12, 0, 0);
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(Text("LOOT THIS SESSION", 10.5, "Brush.TextMuted", bold: true, margin: new Thickness(0, 0, 0, 8)));
        var wrap = new WrapPanel();
        foreach ((int item, long count) in s.Items.OrderByDescending(kv => (CraftWorkshop.PriceOf(book, kv.Key) ?? 0) * kv.Value).ThenByDescending(kv => kv.Value).Take(16))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Width = 260, Margin = new Thickness(0, 0, 10, 6) };
            Border icon = CraftText.IconTile(item, 28);
            icon.Margin = new Thickness(0, 0, 9, 0);
            line.Children.Add(icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 220 };
            names.Children.Add(new TextBlock { Text = CraftText.ItemName(item), FontSize = 12.5, Foreground = new SolidColorBrush(CraftText.GradeColor(item)), TextTrimming = TextTrimming.CharacterEllipsis });
            string worth = CraftWorkshop.PriceOf(book, item) is long price ? "  ·  ~" + CraftWorkshop.Kinah(price * count) : "";
            names.Children.Add(Text($"×{count:N0}{worth}", 11.5, "Brush.TextMuted"));
            line.Children.Add(names);
            wrap.Children.Add(line);
        }

        stack.Children.Add(wrap);
        if (s.Items.Count > 16)
        {
            stack.Children.Add(Text($"+ {s.Items.Count - 16} more kinds: the Loot tab has them all", 11.5, "Brush.TextMuted"));
        }

        return card;
    }

    // ------------------------------------------------------------------ the earlier sessions

    private FrameworkElement Earlier(IReadOnlyList<GrindSession> sessions, MarketBook? book)
    {
        var card = Card(new Thickness(8, 12, 8, 8));
        card.Margin = new Thickness(0, 12, 0, 0);
        var stack = new StackPanel();
        card.Child = stack;
        var head = new DockPanel { Margin = new Thickness(10, 0, 10, 8) };
        if (sessions.Count > 0 && Picture is null)
        {
            var forget = new TextBlock { Text = "Forget all", FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            forget.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            forget.MouseLeftButtonUp += (_, _) =>
            {
                if (ThemedMessageBox.Show(Window.GetWindow(this), "Forget every earlier grind session? The loot log stays.", "Grind", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes)
                {
                    Aion2Grind.Forget(null);
                    _shown = "";
                    Refresh();
                }
            };
            DockPanel.SetDock(forget, Dock.Right);
            head.Children.Add(forget);
        }

        head.Children.Add(Text("EARLIER SESSIONS", 10.5, "Brush.TextMuted", bold: true));
        stack.Children.Add(head);
        if (sessions.Count == 0)
        {
            stack.Children.Add(Text("A session ends after half an hour without a kill, or with New session; it shows here to compare a map or a character with another.", 12.5,
                "Brush.TextMuted", margin: new Thickness(10, 0, 10, 8)));
            return card;
        }

        // the session's name takes what is left; the numbers as wide as they and their titles are (never cut)
        var grid = new Grid();
        int columns = 7;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int c = 1; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        int row = 0;
        void Cell(UIElement e, int column)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            grid.Children.Add(e);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] titles = { "SESSION", "FARMED", "KINAH / H", "AP / H", "KILLS / H", "LOOT / H", "KINAH" };
        for (int i = 0; i < titles.Length; i++)
        {
            TextBlock t = Text(titles[i], 10, "Brush.TextMuted", bold: true, margin: new Thickness(10, 0, 10, 4));
            t.TextTrimming = i > 0 ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            if (i > 0)
            {
                t.TextAlignment = TextAlignment.Right;
                t.HorizontalAlignment = HorizontalAlignment.Right;
            }

            Cell(t, i);
        }

        foreach (GrindSession s in sessions.Take(30))
        {
            row++;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            GrindFigures f = GrindFigures.Of(s, s.End, book);
            if (row % 2 == 1)
            {
                var band = new Border { CornerRadius = new CornerRadius(8) };
                band.SetResourceReference(Border.BackgroundProperty, "Brush.RowAlt");
                Grid.SetColumnSpan(band, columns + 1);
                Cell(band, 0);
            }

            var name = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
            name.Children.Add(Text(s.Zone != 0 ? LootText.MapName(s.Zone) : "Unknown map", 13, "Brush.Text", bold: true));
            name.Children.Add(Text(s.Start.ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture) + (s.Character.Length > 0 ? "  ·  " + s.Character : ""), 11.5, "Brush.TextMuted"));
            Cell(name, 0);
            Cell(Number(f.FarmedText, null), 1);
            Cell(Number(f.KinahPerHour is double k ? GrindView.Short(k) : "–", GrindView.KinahColour), 2);
            Cell(Number(s.AbyssPoints > 0 && f.ApPerHour is double a ? GrindView.Short(a) : "–", GrindView.ApColour), 3);
            Cell(Number(f.KillsPerHour is double kh ? kh.ToString("N0", CultureInfo.CurrentCulture) : "–", null), 4);
            Cell(Number(f.LootPerHour is double lh ? GrindView.Short(lh) : "–", GrindView.LootColour), 5);
            Cell(Number("+" + GrindView.Short(s.Kinah), null), 6);
            if (Picture is null)
            {
                string id = s.Id;
                var remove = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Forget this session" };
                remove.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                remove.MouseLeftButtonUp += (_, _) =>
                {
                    Aion2Grind.Forget(id);
                    _shown = "";
                    Refresh();
                };
                Cell(remove, columns);
            }
        }

        stack.Children.Add(grid);
        return card;
    }

    private static TextBlock Number(string text, Color? colour)
    {
        var t = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        t.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        if (colour is Color c)
        {
            t.Foreground = new SolidColorBrush(c);
        }
        else
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        }

        return t;
    }

    // ------------------------------------------------------------------ small parts

    private static Border Card(Thickness padding)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = padding };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock Text(string text, double size, string brush, bool bold = false, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static Border ActionButton(string glyph, string text, Action click)
    {
        Color ink = Color.FromRgb(0x1A, 0x12, 0x05);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(0, 1, 7, 0),
            Foreground = new SolidColorBrush(ink), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Child = line, Padding = new Thickness(11, 5, 13, 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = new SolidColorBrush(Gold),
            VerticalAlignment = VerticalAlignment.Center };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }
}
