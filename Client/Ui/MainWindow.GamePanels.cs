using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The meter as the one place for everything (10 November 2026, the owner: "I want the meter to be an all around tool, so users don't have to go from one tab to
/// another"): its launcher, under the bar like Find a player, opens the app's pages over the game in panels of the meter's look (GamePanelWindow): Characters,
/// CP calculator, Journey, Legion, Items, Gathering, Season missions, Timers, Map and History. The tools with an overlay (items, gathering, season missions,
/// timers, the map) switch it on or off right on their tile; the tile itself opens their page, with the numbers behind the overlay.
/// </summary>
public partial class MainWindow
{
    /// <summary>A panel: its pages (the app's page ids) under one title.</summary>
    private sealed record PanelGroup(string Id, string Title, string Glyph, string[] Tabs);

    /// <summary>A tile of the launcher: the panel and page it opens, and the overlay it switches when it has one.</summary>
    private sealed record LauncherTile(string Label, string Sub, string Glyph, string Group, string Tab, Func<MeterSettings, bool>? Overlay = null,
        Action<MeterSettings, bool>? SetOverlay = null, string? OverlayName = null);

    private PanelGroup[] PanelGroups()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return new PanelGroup[]
        {
            new("characters", loc["Shell.Characters"], "", new[] { "characters", "attributes", "cpcalc", "journey" }),
            new("season", loc["Checklist.Season"], "", new[] { "season" }),
            new("legion", loc["Main.MenuTools.Legion"], "", new[] { "legion" }),
            new("items", loc["Shell.HubItems"], "", new[] { "loot", "craft" }),
            new("gather", loc["Settings.Nav.Gather"], "", new[] { "petmap.gather" }),
            new("timers", loc["Timers.Title"], "", new[] { "timetable", "boss" }),
            new("map", loc["Shell.HubMap"], "", new[] { "petmap", "petmap.pets", "cubes" }),
            new("history", loc["Shell.History"], "", new[] { "history" }),
        };
    }

    private LauncherTile[] LauncherTiles()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return new LauncherTile[]
        {
            new(loc["Shell.Characters"], "Odyle, dailies, weeklies", "", "characters", "characters"),
            new(loc["Main.MenuTools.CpCalc"], "Your CP and the cheapest upgrades", "", "characters", "cpcalc"),
            new(loc["Main.MenuTools.Journey"], "Your play over time", "", "characters", "journey"),
            new(loc["Main.MenuTools.Legion"], "Members, power, who's online", "", "legion", "legion"),
            new(loc["Shell.HubItems"], "Loot overlay · drops, rates, cubes", "", "items", "loot", s => s.ShowLootOverlay, (s, v) => s.ShowLootOverlay = v, "the loot overlay"),
            new(loc["Settings.Nav.Gather"], "Tally overlay · what you gathered", "", "gather", "petmap.gather", s => s.ShowGatherOverlay, (s, v) => s.ShowGatherOverlay = v,
                "the gathering overlay"),
            new(loc["Checklist.Season"], "Overlay · pick its missions", "", "season", "season", s => s.ShowSeasonOverlay, (s, v) => s.ShowSeasonOverlay = v,
                "the season missions overlay"),
            new(loc["Timers.Title"], "Overlay · events, world bosses", "", "timers", "timetable", s => s.ShowTimetable, (s, v) => s.ShowTimetable = v, "the timers overlay"),
            new(loc["Shell.HubMap"], "Map overlay · pets, cubes", "", "map", "petmap", s => s.ShowPetMap, (s, v) => s.ShowPetMap = v, "the map over the game"),
            new(loc["Shell.History"], "Every fight, your bests", "", "history", "history"),
        };
    }

    /// <summary>Opens a panel over the game on one of its pages (null: where it was left), beside the meter.</summary>
    internal void OpenGamePanel(string groupId, string? tab = null)
    {
        if (Headless || PanelGroups().FirstOrDefault(g => g.Id == groupId) is not { } group)
        {
            return;
        }

        Dictionary<string, ShellItem> pages = ShellPages();
        LocalizationManager loc = LocalizationManager.Instance;
        var tabs = group.Tabs.Where(Allowed).Select(id => id switch
        {
            "season" => new GamePanelTab(id, loc["Checklist.Season"], ""),
            _ => new GamePanelTab(id, pages.TryGetValue(id, out ShellItem? item) ? item.Label : id, pages.TryGetValue(id, out ShellItem? glyphed) ? glyphed.Glyph : ""),
        }).ToList();
        Rect? near = _meterOverlay is { IsVisible: true } overlay ? new Rect(overlay.Left, overlay.Top, overlay.ActualWidth, overlay.ActualHeight) : null;
        GamePanelWindow.Open(group.Id, group.Title, group.Glyph, tabs, tab, BuildPanelPage, id =>
        {
            BringAppForward();
            ShowShellPage(id == "season" ? "characters" : id);
        }, near);

        bool Allowed(string id) => id is "season" or "history" or "characters" || pages.ContainsKey(id);
    }

    /// <summary>A panel as it looks over the game, for the pictures: the group's frame and tabs with the page <paramref name="tab"/>.</summary>
    internal FrameworkElement GamePanelPicture(string groupId, string tab)
    {
        PanelGroup group = PanelGroups().First(g => g.Id == groupId);
        Dictionary<string, ShellItem> pages = ShellPages();
        LocalizationManager loc = LocalizationManager.Instance;
        var tabs = group.Tabs.Select(id => id == "season" ? new GamePanelTab(id, loc["Checklist.Season"], "\uE734")
            : new GamePanelTab(id, pages.TryGetValue(id, out ShellItem? item) ? item.Label : id, pages.TryGetValue(id, out ShellItem? glyphed) ? glyphed.Glyph : "\uE8B7")).ToList();
        return GamePanelWindow.Picture(group.Title, group.Glyph, tabs, tab, BuildPanelPage, openInApp: true);
    }

    /// <summary>A page for a panel: the app's own page, built as the app's window builds it.</summary>
    private ToolPage? BuildPanelPage(string id)
    {
        switch (id)
        {
            case "characters":
                return BuildToolPage("checklist", inPopOut: true);
            case "history":
            {
                var history = new HistoryWindow();
                return new ToolPage { Content = ThemedChrome.TakeContent(history) ?? new Grid(), Cleanup = history.Close };
            }

            case "season":
            {
                // the season missions of the character played, where the overlay's missions are picked (☆ Overlay on a mission)
                var checklist = new ChecklistWindow();
                if ((_liveSource?.Entities as Aion2EntityDirectory)?.LocalCharacter?.Name is { Length: > 0 } me)
                {
                    checklist.ShowCharacter(me, ChecklistWindow.Section.Season);
                }

                return new ToolPage { Content = ThemedChrome.TakeContent(checklist) ?? new Grid(), Cleanup = checklist.Close };
            }

            default:
                return BuildToolPage(id, inPopOut: true);
        }
    }

    // ------------------------------------------------------------------ the launcher under the bar

    private void OnOverlayAppsClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (OverlayApps.Visibility == Visibility.Visible)
        {
            CloseLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    /// <summary>The launcher under the bar (Find a player goes; the two share the place).</summary>
    internal void ShowLauncher()
    {
        if (OverlayFind.Visibility == Visibility.Visible)
        {
            CloseFindPlayer();
        }

        DrawLauncher();
        OverlayApps.Visibility = Visibility.Visible;
        OverlayAppsButton.Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xDC, 0xB8, 0x77));
        _overlayPeekUntil = DateTime.Now + TimeSpan.FromMinutes(2); // the meter stays up while it is used
        if (!Headless && _meterOverlay is { IsVisible: true } overlay && !overlay.IsActive)
        {
            overlay.Activate();
        }
    }

    private void CloseLauncher()
    {
        OverlayApps.Visibility = Visibility.Collapsed;
        OverlayApps.Children.Clear();
        OverlayAppsButton.ClearValue(Border.BackgroundProperty);
    }

    private void DrawLauncher()
    {
        OverlayApps.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 6) };
        var close = new Border { Style = (Style)FindResource("OverlayIconButton"), ToolTip = "Close", Child = new TextBlock { Text = "", Style = (Style)FindResource("OverlayIconGlyph"), FontSize = 10 } };
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseLauncher();
        };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var title = new TextBlock { Text = "EVERYTHING FROM THE METER", FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        head.Children.Add(title);
        OverlayApps.Children.Add(head);

        Dictionary<string, ShellItem> pages = ShellPages();
        MeterSettings settings = MeterSettings.Load();
        var grid = new UniformGrid { Columns = 2 };
        foreach (LauncherTile tile in LauncherTiles())
        {
            if (tile.Tab is not ("season" or "history" or "characters") && !pages.ContainsKey(tile.Tab))
            {
                continue; // a tool the restricted mode leaves out
            }

            grid.Children.Add(TileView(tile, settings));
        }

        OverlayApps.Children.Add(grid);
        var hint = new TextBlock { Text = "A tile opens its page over the game; the switch puts its overlay on or off.", FontSize = 10.5, Margin = new Thickness(2, 5, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        OverlayApps.Children.Add(hint);
    }

    private Border TileView(LauncherTile tile, MeterSettings settings)
    {
        var gold = Color.FromRgb(0xDC, 0xB8, 0x77);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool open = GamePanelWindow.IsOpen(tile.Group);
        var mark = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(open ? (byte)0x3A : (byte)0x1E, gold.R, gold.G, gold.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(open ? (byte)0x99 : (byte)0x40, gold.R, gold.G, gold.B)),
            Child = new TextBlock
            {
                Text = tile.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(gold),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(mark);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = tile.Label, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        text.Children.Add(name);
        var sub = new TextBlock { Text = tile.Sub, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (tile.Overlay is { } get && tile.SetOverlay is { } set)
        {
            FrameworkElement toggle = OverlaySwitch(get(settings), on =>
            {
                MeterSettings s = MeterSettings.Load();
                set(s, on);
                s.Save();
                ApplySettingsEverywhere(s);
            }, $"Turn {tile.OverlayName} on or off");
            toggle.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);
        }

        // never fully transparent: in the see-through look Windows gives clicks on such pixels to the game
        var rest = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
        var card = new Border
        {
            Child = grid, Padding = new Thickness(7, 6, 8, 6), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(8), Background = rest, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), ToolTip = $"Open {tile.Label} over the game",
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, gold.R, gold.G, gold.B));
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = rest;
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenGamePanel(tile.Group, tile.Tab);
            CloseLauncher();
        };
        return card;
    }

    /// <summary>A small switch in the meter's look: gold with the knob right when on. A click turns it over at once and tells <paramref name="changed"/>.</summary>
    internal static FrameworkElement OverlaySwitch(bool on, Action<bool> changed, string tip)
    {
        var gold = Color.FromRgb(0xDC, 0xB8, 0x77);
        var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        var track = new Border
        {
            Width = 30, Height = 17, CornerRadius = new CornerRadius(8.5), BorderThickness = new Thickness(1), Child = knob, Cursor = Cursors.Hand, ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void Paint()
        {
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            knob.Background = new SolidColorBrush(on ? Color.FromRgb(0x1A, 0x16, 0x10) : Color.FromRgb(0xB8, 0xB8, 0xC0));
            track.Background = new SolidColorBrush(on ? gold : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            track.BorderBrush = new SolidColorBrush(on ? gold : Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
        }

        Paint();
        track.MouseLeftButtonDown += (_, e) => e.Handled = true;
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true; // the tile under it doesn't open its page
            on = !on;
            Paint();
            changed(on);
        };
        return track;
    }
}
