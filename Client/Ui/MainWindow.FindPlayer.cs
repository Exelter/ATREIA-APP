using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The meter's own buttons and menus (5 November 2026, the owner, with pictures of another meter): Tools (the buff and cooldown timers, the pet tracker and
/// its list, sound alerts, clearing the fight, the history), the short "…" (where everything is, the Ko-fi page, exit), and Find a player:
/// a name and a region, the characters the website's search finds, then a card of the one picked (class, server, Combat Power, Item Level, the best parse
/// of a public fight) with its full page in Character Lookup one click away. The full page stays on the website, the one place it is kept up to date.
/// With the box empty it lists the other players of the own group (the game's party roster), each a click away from its card (10 November 2026).
/// </summary>
public partial class MainWindow
{
    private const string KofiPage = "https://ko-fi.com/atreia";

    // ------------------------------------------------------------------ Tools and "…"

    private void OnOverlayToolsClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenOverlayMenu((FrameworkElement)sender, OverlayToolsItems());
    }

    /// <summary>The Tools menu (also drawn by render-overlay … tools).</summary>
    internal IList<Control> OverlayToolsItems()
    {
        MeterSettings s = MeterSettings.Load();
        return new Control[]
        {
            OverlayItem("Buff and cooldown timers…", OpenAssistFromMeter, glyph: "\uE945"),
            OverlayItem("Pet tracker map", TogglePetMap, s.ShowPetMap, !PrivacyConsent.Restricted),
            OverlayItem("Pet tracker list", TogglePetList, s.ShowPetMap && s.PetMapShowList, s.ShowPetMap && !PrivacyConsent.Restricted),
            OverlayItem("Sound alerts", () => ChangeOverlaySetting(x => x.MuteSounds = !x.MuteSounds), !s.MuteSounds),
            new Separator(),
            OverlayItem("Clear current fight", ResetMeter, glyph: "\uE7A7"),
            OverlayItem("Fight history", OpenHistory, glyph: "\uE81C"),
            OverlayItem("Everything from the meter…", ShowLauncher, glyph: "\uE8A9"),
            // Find a player: the search button on the meter and "…" (6 November 2026, the owner: three were too many)
        };
    }

    /// <summary>The lines of the meter's own menu "…" (also drawn by render-overlay … menu): where the rest is, nothing more (5 November 2026, the owner:
    /// no huge menu, "just redirect"). The meter's switches are on its Meter look page, the training dummy and Grind in the pill's menu.</summary>
    internal IList<Control> OverlaySettingsItems() => new Control[]
    {
        OverlayItem("Fight details", () =>
        {
            ShowClient();
            _ = OpenFightReportAsync();
        }, glyph: "\uE9D2"),
        OverlayItem("History", OpenHistory, glyph: "\uE81C"),
        OverlayItem("Find a player", ShowFindPlayer, glyph: "\uE721"),
        // over the game, in a panel of the meter's look (10 November 2026): no trip to the app's window
        OverlayItem("Characters", () => OpenGamePanel("characters", "characters"), glyph: "\uE716"),
        OverlayItem("Everything from the meter…", ShowLauncher, glyph: "\uE8A9"),
        OverlayItem("Settings", () =>
        {
            ShowClient();
            if (_shellBuilt)
            {
                ShowShellPage("meterlook");
            }
            else
            {
                OnSettingsClicked(this, new RoutedEventArgs());
            }
        }, glyph: "\uE713"),
        OverlayItem("What's new", () => OnAnnouncementsClicked(this, new RoutedEventArgs()), glyph: "\uE789"),
        OverlayItem("Support us on Ko-fi ♥", () => OpenInBrowser(KofiPage), glyph: "\uEB51"),
        new Separator(),
        OverlayItem("Hide the meter", HideMeterOverlay, glyph: "\uED1A"),
        OverlayItem("Exit Atreia App", Close, glyph: "\uE7E8"),
    };

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open: nothing else to do
        }
    }

    /// <summary>The Skill Assist window (buffs on you, the cooldowns, their places and sizes, every class's skills) from the meter: on its own, without bringing
    /// the meter window up over the game.</summary>
    private void OpenAssistFromMeter()
    {
        if (_assistWindow is not null)
        {
            _assistWindow.Activate();
            return;
        }

        _assistWindow = new AssistWindow(() => _liveSource?.Entities as Aion2EntityDirectory);
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _assistWindow.Owner = this;
        }

        _assistWindow.Closed += (_, _) => _assistWindow = null;
        _assistWindow.Show();
        _assistWindow.Activate();
    }

    /// <summary>The meter's reset (the button, Tools → Clear current fight): the fight on screen goes to the history, a dummy parse starts over.</summary>
    private void ResetMeter()
    {
        _dummy?.Model.Reset();
        _dummyCardHidden = false;
        ClearDamageData();
        RefreshRows();
    }

    private void OnOverlayPetMapClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        TogglePetMap();
        UpdatePetMapButton(MeterSettings.Load());
    }

    /// <summary>The pet tracker's list beside its map, on or off (the map's own switch stays).</summary>
    private void TogglePetList()
    {
        var settings = MeterSettings.Load();
        settings.PetMapShowList = !settings.PetMapShowList;
        settings.Save();
        _petMapController?.SettingsChanged();
        ApplyPetFarm(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The bar's pet tracker button lit while the map is on.</summary>
    private void UpdatePetMapButton(MeterSettings settings)
    {
        OverlayPetMapGlyph.SetResourceReference(TextBlock.ForegroundProperty, settings.ShowPetMap ? "Brush.Accent" : "Brush.TextSubtle");
        OverlayPetMapButton.Visibility = PrivacyConsent.Restricted ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ Find a player

    private string _findRegion = "";
    private int _findAsked;

    /// <summary>The one server the search keeps (0: every server of the region) and the faction tab of the server picker.</summary>
    private int _findServer;
    private bool _findServersAsmodian;

    /// <summary>The region's block in the game's server numbers (1 NA East, 2 NA West, 3 Europe, 4 LATAM, 5 Asia); 0 for all regions.</summary>
    private static int BlockOf(string region) => region switch { "nae" => 1, "naw" => 2, "eu" => 3, "la" => 4, "as" => 5, _ => 0 };

    private void OnOverlayFindClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (OverlayFind.Visibility == Visibility.Visible)
        {
            CloseFindPlayer();
        }
        else
        {
            ShowFindPlayer();
        }
    }

    private void OnOverlayFindCloseClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFindPlayer();
    }

    /// <summary>Opens the search under the bar, the region the own server's, the box ready for typing (the meter's window takes the keyboard for it).</summary>
    private void ShowFindPlayer()
    {
        if (OverlayApps.Visibility == Visibility.Visible)
        {
            CloseLauncher(); // the two share the place under the bar
        }

        InitFindRegion();
        _findAuto = false; // opened by the player: it stays until closed
        OverlayFind.Visibility = Visibility.Visible;
        ShowGroupLists(); // the group's players and those fighting with it, a click each, until a name is searched
        StartGroupWatch();
        _overlayPeekUntil = DateTime.Now + TimeSpan.FromMinutes(2); // the meter stays up while the search is used
        if (_meterOverlay is { IsVisible: true } overlay && !overlay.IsActive)
        {
            overlay.Activate();
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            OverlayFindBox.Focus();
            Keyboard.Focus(OverlayFindBox);
            OverlayFindBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>The search's region at its first opening: the own server's.</summary>
    private void InitFindRegion()
    {
        if (_findRegion.Length > 0)
        {
            return;
        }

        int server = (_liveSource?.Entities as Aion2EntityDirectory)?.LocalCharacter?.ServerId ?? 0;
        _findRegion = server > 0 ? FindPlayerClient.RegionOf(server) : "all";
        _findServersAsmodian = server / 1000 == 2;
        OverlayFindRegionText.Text = FindPlayerClient.Regions.First(r => r.Code == _findRegion).Name;
        ShowFindServer();
    }

    private void CloseFindPlayer()
    {
        _findAuto = false;
        _groupWatch?.Stop();
        _findAsked++; // an answer still on its way is dropped
        OverlayFind.Visibility = Visibility.Collapsed;
        OverlayFindServers.Visibility = Visibility.Collapsed;
        OverlayFindResults.Children.Clear();
        OverlayFindCard.Visibility = Visibility.Collapsed;
        OverlayFindStatus.Visibility = Visibility.Collapsed;
    }

    private void OnOverlayFindTextChanged(object sender, TextChangedEventArgs e)
    {
        OverlayFindHint.Visibility = OverlayFindBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (OverlayFindBox.Text.Length == 0 && OverlayFind.Visibility == Visibility.Visible)
        {
            _findAsked++; // a search still on its way no longer replaces the group
            ShowGroupLists(); // the box emptied: the group again
        }
    }

    private void OnOverlayFindKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = RunFindAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseFindPlayer();
        }
    }

    private void OnOverlayFindRegionClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenOverlayMenu((FrameworkElement)sender, FindPlayerClient.Regions.Select(r => (Control)OverlayItem(r.Name, () =>
        {
            _findRegion = r.Code;
            OverlayFindRegionText.Text = r.Name;
            _findServer = 0; // the servers are the region's
            OverlayFindServers.Visibility = Visibility.Collapsed;
            ShowFindServer();
            if (OverlayFindBox.Text.Trim().Length >= 2)
            {
                _ = RunFindAsync();
            }
        }, r.Code == _findRegion)).ToList());
    }

    /// <summary>The server pill: "All servers" or the one picked; dimmed while all regions are searched (a server belongs to one region).</summary>
    private void ShowFindServer()
    {
        OverlayFindServerText.Text = _findServer > 0 ? Aion2Servers.NameOf(_findServer).Split(" - ")[^1] : "All servers";
        OverlayFindServer.Opacity = BlockOf(_findRegion) > 0 ? 1 : 0.45;
    }

    private void OnOverlayFindServerClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (BlockOf(_findRegion) == 0)
        {
            ShowFindStatus("Pick a region first to search one of its servers. All regions searches every server.");
            return;
        }

        if (OverlayFindServers.Visibility == Visibility.Visible)
        {
            OverlayFindServers.Visibility = Visibility.Collapsed;
            return;
        }

        FillServerPicker();
        OverlayFindServers.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The server picker as on the website: Elyos and Asmodians tabs, "All servers", then the region's servers A to Z. A pick narrows the results to that server
    /// (the search itself is the region's) and searches again when a name is in the box.
    /// </summary>
    private void FillServerPicker()
    {
        var servers = Aion2Servers.LiveServers(BlockOf(_findRegion));
        var stack = new StackPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        foreach (bool asmo in new[] { false, true })
        {
            bool on = asmo == _findServersAsmodian;
            var tab = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x99, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(on ? Color.FromArgb(0x26, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)) };
            var text = new TextBlock { Text = $"{(asmo ? "Asmodians" : "Elyos")}  {servers.Count(x => x.Asmodian == asmo)}", FontSize = 11.5,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.OverlayText" : "Brush.TextMuted");
            tab.Child = text;
            bool which = asmo;
            tab.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                _findServersAsmodian = which;
                FillServerPicker();
            };
            tabs.Children.Add(tab);
        }

        stack.Children.Add(tabs);
        var chips = new WrapPanel();
        chips.Children.Add(ServerChip("All servers", 0));
        foreach (var server in servers.Where(x => x.Asmodian == _findServersAsmodian))
        {
            chips.Children.Add(ServerChip(server.Name, server.Id));
        }

        stack.Children.Add(chips);
        OverlayFindServers.Child = stack;
    }

    /// <summary>The picker open on a region (the pictures of the meter).</summary>
    internal void OpenServerPicker(string region, int server, bool asmodian)
    {
        _findRegion = region;
        _findServer = server;
        _findServersAsmodian = asmodian;
        OverlayFindRegionText.Text = FindPlayerClient.Regions.First(r => r.Code == region).Name;
        ShowFindServer();
        FillServerPicker();
        OverlayFindServers.Visibility = Visibility.Visible;
    }

    private Border ServerChip(string name, int id)
    {
        bool on = id == _findServer;
        var chip = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(on ? Color.FromArgb(0x33, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)) };
        var text = new TextBlock { Text = name, FontSize = 11.5 };
        text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Accent" : "Brush.OverlayText");
        chip.Child = text;
        chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(on ? Color.FromArgb(0x33, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        chip.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            _findServer = id;
            OverlayFindServers.Visibility = Visibility.Collapsed;
            ShowFindServer();
            if (OverlayFindBox.Text.Trim().Length >= 2)
            {
                _ = RunFindAsync();
            }
        };
        return chip;
    }

    private async Task RunFindAsync()
    {
        string name = OverlayFindBox.Text.Trim();
        if (name.Length < 2)
        {
            ShowFindStatus("Enter at least 2 letters of the name.");
            return;
        }

        int asked = ++_findAsked;
        OverlayFindResults.Children.Clear();
        OverlayFindCard.Visibility = Visibility.Collapsed;
        ShowFindStatus("Searching…");
        FindAnswer answer = await FindPlayerClient.SearchAsync(name, _findRegion.Length > 0 ? _findRegion : "all");
        if (asked != _findAsked)
        {
            return;
        }

        if (answer.Error is { } error)
        {
            ShowFindStatus(error);
            return;
        }

        IReadOnlyList<FoundPlayer> players = _findServer > 0 ? answer.Players.Where(p => p.ServerId == _findServer).ToList() : answer.Players;
        if (players.Count == 0)
        {
            ShowFindStatus(_findServer > 0
                ? $"No character called \"{name}\" on {OverlayFindServerText.Text}" + (answer.Players.Count > 0 ? $" ({answer.Players.Count} on other servers: pick All servers)." : ".")
                : $"No character called \"{name}\" in {OverlayFindRegionText.Text}.");
            return;
        }

        ShowFindResults(players);
        if (players.Count == 1)
        {
            PickFindResult(players[0]); // the one character: its card straight away
        }
    }

    private void ShowFindStatus(string? text)
    {
        OverlayFindStatus.Text = text ?? "";
        OverlayFindStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The characters found, one line each: class, name, level, server and region, Combat Power; a click shows its card (and only it).</summary>
    internal void ShowFindResults(IReadOnlyList<FoundPlayer> players)
    {
        _findPlayers = players;
        _findShown = null;
        OverlayFindCard.Visibility = Visibility.Collapsed;
        ShowFindStatus(players.Count > 1 ? $"{players.Count} characters. Pick one:" : null);
        OverlayFindResults.Children.Clear();
        foreach (FoundPlayer player in players.Take(12))
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Source = player.ClassName is { } cls ? new ClassIconConverter().Convert(cls, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null };
            grid.Children.Add(icon);
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock { Text = player.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            nameText.Foreground = player.ClassName is { } c ? ClassBrushes.Text(c) : (Brush)FindResource("Brush.OverlayText");
            who.Children.Add(nameText);
            var line = new TextBlock { Text = (player.Level is int lv ? $"Lv {lv}  ·  " : "") + FindPlayerClient.Line(player), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            who.Children.Add(line);
            Grid.SetColumn(who, 1);
            grid.Children.Add(who);
            if (player.CombatPower is int cp)
            {
                var power = new TextBlock { Text = FindPlayerClient.Number(cp), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 2, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)) };
                Grid.SetColumn(power, 2);
                grid.Children.Add(power);
            }

            // never fully transparent: in the see-through look Windows gives clicks on such pixels to the game (5 November 2026: the rows could not be clicked)
            var rest = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
            var row = new Border { Child = grid, Padding = new Thickness(7, 4, 7, 4), CornerRadius = new CornerRadius(6), Background = rest, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 2) };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            row.MouseLeave += (_, _) => row.Background = rest;
            FoundPlayer picked = player;
            row.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                PickFindResult(picked);
            };
            OverlayFindResults.Children.Add(row);
        }
    }

    private FoundPlayer? _findShown;
    private IReadOnlyList<FoundPlayer> _findPlayers = Array.Empty<FoundPlayer>();
    private PlayerWindow? _playerWindow;

    /// <summary>
    /// A character picked from the results (6 November 2026, the owner: "the other ones disappear and just the one I clicked appears"): the list goes, its card
    /// stays, with a way back to all of them when there were more.
    /// </summary>
    internal void PickFindResult(FoundPlayer player)
    {
        OverlayFindResults.Children.Clear();
        ShowFindStatus(null);
        if (_findPlayers.Count > 1)
        {
            var text = new TextBlock { Text = $"‹  All {_findPlayers.Count} results", FontSize = 11.5 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            var rest = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); // never see-through: clicks would go to the game
            var back = new Border { Child = text, Padding = new Thickness(8, 4, 10, 4), CornerRadius = new CornerRadius(6), Background = rest, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Back to the other characters of the name" };
            back.MouseEnter += (_, _) =>
            {
                back.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
                text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
            };
            back.MouseLeave += (_, _) =>
            {
                back.Background = rest;
                text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            };
            back.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                ShowFindResults(_findPlayers);
            };
            OverlayFindResults.Children.Add(back);
        }

        ShowFindCard(player);
    }

    /// <summary>
    /// The character in the player window over the game (6 November 2026, the owner: "a tab in game, not a redirect to the website, so people don't have to
    /// alt-tab"): one window, beside the meter; picking another character shows that one in it.
    /// </summary>
    private void OpenPlayerWindow(FoundPlayer player)
    {
        if (Headless)
        {
            return;
        }

        if (_playerWindow is { IsLoaded: true } open)
        {
            _ = open.ShowPlayerAsync(player);
            open.Activate();
            return;
        }

        var window = new PlayerWindow(player) { WindowStartupLocation = WindowStartupLocation.Manual };
        Rect area = SystemParameters.WorkArea;
        const double width = 562;
        if (_meterOverlay is { IsVisible: true } overlay)
        {
            double right = overlay.Left + overlay.ActualWidth + 10;
            window.Left = right + width <= area.Right ? right : Math.Max(area.Left, overlay.Left - width - 10);
            window.Top = Math.Clamp(overlay.Top, area.Top, Math.Max(area.Top, area.Bottom - 420));
        }
        else
        {
            window.Left = area.Left + (area.Width - width) / 2;
            window.Top = area.Top + area.Height * 0.12;
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_playerWindow, window))
            {
                _playerWindow = null;
            }
        };
        _playerWindow = window;
        window.Show();
        window.Activate();
    }

    /// <summary>The card of one character: its class, name, class · server · region, Combat Power, Item Level and best parse, and its player window.
    /// A character the website has no figures of (not a player of the meter) gets them from its official page a moment later.</summary>
    internal void ShowFindCard(FoundPlayer player, bool asked = false)
    {
        _findShown = player;
        bool reading = false;
        if (!asked && !Headless && (player.CombatPower is null || player.ItemLevel is null))
        {
            reading = true; // "…" until the official page answered
            _ = FillFigures(player);
        }

        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var frame = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 11, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = new Image { Width = 30, Height = 30, Source = player.ClassName is { } cls ? new ClassIconConverter().Convert(cls, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null } };
        head.Children.Add(frame);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = player.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Foreground = player.ClassName is { } c ? ClassBrushes.Text(c) : (Brush)FindResource("Brush.OverlayText");
        who.Children.Add(name);
        var line = new TextBlock { Text = FindPlayerClient.Line(player), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        who.Children.Add(line);
        Grid.SetColumn(who, 1);
        head.Children.Add(who);
        stack.Children.Add(head);

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
        string waiting = reading ? "…" : "–";
        tiles.Children.Add(Tile("Combat power", player.CombatPower is int cp ? FindPlayerClient.Number(cp) : waiting, Color.FromRgb(0xE8, 0xC3, 0x5A), null));
        tiles.Children.Add(Tile("Item level", player.ItemLevel is int il ? FindPlayerClient.Number(il) : waiting, null, null));
        tiles.Children.Add(Tile("Best parse", player.BestPercent is int pct ? pct.ToString(CultureInfo.CurrentCulture) + "%" : "–", player.BestPercent is int p ? ParseColour(p) : null,
            player.BestPercent is not null
                ? $"{player.BestBoss}: {(player.BestDps is double dps ? PlayerRow.Compact((long)Math.Round(dps)) + " DPS, " : "")}rank {player.BestRank} of {player.BestOf} {player.ClassName ?? "players"} - the best public parse uploaded with the meter"
                : "No parse: this character's fights were never uploaded with the meter (or are not public)"));
        stack.Children.Add(tiles);

        var open = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(0, 7, 0, 7), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            IsEnabled = player.ServerId > 0 };
        var label = new TextBlock { Text = "Open player page", FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        open.Child = label;
        open.ToolTip = "Everything about the character in a window over the game: gear, stats, skills, parses, and Genus Insight and pets when they play with Atreia App";
        open.MouseEnter += (_, _) => open.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        open.MouseLeave += (_, _) => open.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        open.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            OpenPlayerWindow(player);
        };
        stack.Children.Add(open);

        OverlayFindCard.Child = new Border
        {
            Child = stack, Padding = new Thickness(12, 11, 12, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
        };
        OverlayFindCard.Visibility = Visibility.Visible;
    }

    /// <summary>The colours parse percentiles are known by: grey, green, blue, purple, orange, pink at 99, gold at 100.</summary>
    internal static Color ParseColour(int pct) => pct switch
    {
        >= 100 => Color.FromRgb(0xE5, 0xCC, 0x80),
        >= 99 => Color.FromRgb(0xE2, 0x68, 0xA8),
        >= 95 => Color.FromRgb(0xFF, 0x80, 0x00),
        >= 75 => Color.FromRgb(0xA3, 0x35, 0xEE),
        >= 50 => Color.FromRgb(0x00, 0x70, 0xFF),
        >= 25 => Color.FromRgb(0x1E, 0xFF, 0x00),
        _ => Color.FromRgb(0x9D, 0x9D, 0x9D),
    };

    /// <summary>Reads the Combat Power and Item Level the website has none of from the character's official page, then shows the card again with them.</summary>
    private async Task FillFigures(FoundPlayer player)
    {
        (int? cp, int? il) = await FindPlayerClient.FiguresAsync(player);
        if (!ReferenceEquals(_findShown, player) || OverlayFind.Visibility != Visibility.Visible)
        {
            return; // another character was picked, or the search closed
        }

        ShowFindCard(player with { CombatPower = player.CombatPower ?? cp, ItemLevel = player.ItemLevel ?? il }, asked: true);
    }

    /// <summary>A player of the own group, as the game's party roster lists them: the name, the class and the server when the roster gave them.</summary>
    internal readonly record struct GroupMate(string Name, string? ClassName, int? ServerId);

    /// <summary>The other players of the own group (the party roster the meter's Group view reads too), in the roster's order; empty out of a group.</summary>
    private IReadOnlyList<GroupMate> GroupMates()
    {
        if (_liveSource?.Entities is not Aion2EntityDirectory directory)
        {
            return Array.Empty<GroupMate>();
        }

        string? own = directory.LocalCharacter?.Name;
        return directory.PartyNames.Where(n => n != own)
            .Select(n => new GroupMate(n, directory.PartyClassOfName(n), directory.ServerIdOfName(n)))
            .ToList();
    }

    /// <summary>How far back the fight is read for the players fighting with the group.</summary>
    private static readonly TimeSpan AlongsideWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The players fighting with the group who are not in it (10 November 2026, the owner: Ludra and other corps fights): the game's roster names the own party
    /// only, so the other party of a corps is read from the fight, as the meter's Corps view reads it - the named players who hit a monster the group hit, or
    /// healed one of them, over the last 10 minutes of the fight. Most damage first. In a raid that is the other party; in the open world it can be a stranger
    /// at the same boss. Empty out of a group, and while the history shows a stored fight.
    /// </summary>
    private IReadOnlyList<GroupMate> AlongsideMates()
    {
        if (_historyMode || _liveSource?.Entities is not Aion2EntityDirectory directory || !RosterKnown(directory))
        {
            return Array.Empty<GroupMate>();
        }

        IReadOnlyList<DamageEvent> events = _aggregator.Events;
        if (events.Count == 0)
        {
            return Array.Empty<GroupMate>();
        }

        // the last 10 minutes; the list is in the order the hits came in, so reading back stops a little behind the cut
        DateTime cut = events[^1].Timestamp - AlongsideWindow;
        var recent = new List<DamageEvent>();
        for (int i = events.Count - 1; i >= 0; i--)
        {
            DamageEvent ev = events[i];
            if (ev.Timestamp < cut - TimeSpan.FromSeconds(30))
            {
                break;
            }

            if (ev.Timestamp >= cut && ev.SourceObjectId != ev.TargetObjectId)
            {
                recent.Add(ev);
            }
        }

        var memberOf = new Dictionary<int, bool>();
        bool Member(int id) => memberOf.TryGetValue(id, out bool known) ? known : memberOf[id] = IsGroupMember(id, directory);
        var groupTargets = recent.Where(ev => !ev.IsHeal && Member(ev.SourceObjectId)).Select(ev => ev.TargetObjectId).ToHashSet();
        var damage = new Dictionary<int, long>();
        foreach (DamageEvent ev in recent)
        {
            bool alongside = ev.IsHeal ? Member(ev.TargetObjectId) : groupTargets.Contains(ev.TargetObjectId);
            if (alongside && !Member(ev.SourceObjectId) && directory.IsKnownPlayer(ev.SourceObjectId))
            {
                damage[ev.SourceObjectId] = damage.GetValueOrDefault(ev.SourceObjectId) + (ev.IsHeal ? 0 : ev.Amount);
            }
        }

        IReadOnlySet<string> party = directory.PartyNames;
        string? own = directory.LocalCharacter?.Name;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mates = new List<GroupMate>();
        foreach ((int id, _) in damage.OrderByDescending(d => d.Value))
        {
            if (directory.NameFor(id) is not { Length: >= 2 } name || name == own || party.Contains(name) || !seen.Add(name))
            {
                continue;
            }

            mates.Add(new GroupMate(name, directory.ClassOf(id), directory.ServerIdOf(id) ?? directory.ServerIdOfName(name)));
            if (mates.Count == 16)
            {
                break;
            }
        }

        return mates;
    }

    /// <summary>The empty box's lists: the group, then the players fighting with it.</summary>
    private void ShowGroupLists()
    {
        IReadOnlyList<GroupMate> mates = GroupMates(), alongside = AlongsideMates();
        _groupListsShown = ListsKey(mates, alongside);
        ShowGroupMates(mates, alongside);
    }

    // What the empty box's lists show (names, class and server, in order): drawn again only when this changes.
    private string _groupListsShown = "";
    private System.Windows.Threading.DispatcherTimer? _groupWatch;

    private static string ListsKey(IReadOnlyList<GroupMate> mates, IReadOnlyList<GroupMate> alongside) =>
        string.Join("|", mates.Select(m => $"{m.Name}/{m.ClassName}/{m.ServerId}")) + "#" + string.Join("|", alongside.Select(m => $"{m.Name}/{m.ClassName}/{m.ServerId}"));

    /// <summary>
    /// The lists follow the group while they are on show (10 November 2026, the owner: "it does not update when new people join the lobby or leave"): every
    /// 2 s the roster and the fight are read again and the lists drawn anew when someone joined, left, or a class or server came in. Not while a name is searched or
    /// a card is open, and not once the search is closed.
    /// </summary>
    private void StartGroupWatch()
    {
        if (Headless)
        {
            return;
        }

        if (_groupWatch is null)
        {
            _groupWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _groupWatch.Tick += (_, _) =>
            {
                if (OverlayFind.Visibility != Visibility.Visible)
                {
                    _groupWatch.Stop();
                    return;
                }

                if (OverlayFindBox.Text.Length > 0 || _findShown is not null || _findPlayers.Count > 0)
                {
                    return; // a search or a card on show: the lists come back when the box is emptied
                }

                IReadOnlyList<GroupMate> mates = GroupMates(), alongside = AlongsideMates();
                string key = ListsKey(mates, alongside);
                if (key != _groupListsShown)
                {
                    _groupListsShown = key;
                    ShowGroupMates(mates, alongside);
                }
            };
        }

        _groupWatch.Start();
    }

    /// <summary>
    /// Find a player with an empty box (10 November 2026, the owner: "pops up people in your party … and you can click to check them"): the other players of the
    /// group, one click each to their card, with no name to type, then the players fighting with the group (see <see cref="AlongsideMates"/>). Nothing out of a group.
    /// </summary>
    internal void ShowGroupMates(IReadOnlyList<GroupMate> mates, IReadOnlyList<GroupMate>? alongside = null)
    {
        OverlayFindResults.Children.Clear();
        OverlayFindCard.Visibility = Visibility.Collapsed;
        _findShown = null;
        _findPlayers = Array.Empty<FoundPlayer>();
        alongside ??= Array.Empty<GroupMate>();
        ShowFindStatus(mates.Count > 0 ? "In your group:" : alongside.Count > 0 ? "Fighting with you:" : null);
        foreach (GroupMate mate in mates)
        {
            OverlayFindResults.Children.Add(GroupMateRow(mate));
        }

        if (mates.Count > 0 && alongside.Count > 0)
        {
            var heading = new TextBlock { Text = "Fighting with you:", FontSize = 11, Margin = new Thickness(2, 8, 0, 4),
                ToolTip = "Players who fought the same monsters as your group, or healed it: in a raid, the other party" };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            OverlayFindResults.Children.Add(heading);
        }

        foreach (GroupMate mate in alongside)
        {
            OverlayFindResults.Children.Add(GroupMateRow(mate));
        }
    }

    /// <summary>One player of the empty box's lists: class icon, name, class · server, their Combat Power once looked up, and a click to their card.</summary>
    private Border GroupMateRow(GroupMate mate)
    {
        FoundPlayer? known = LookedUp(mate);
        if (mate.ClassName is null && known?.ClassName is { } lookedUpClass)
        {
            mate = mate with { ClassName = lookedUpClass };
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Source = mate.ClassName is { } cls ? new ClassIconConverter().Convert(cls, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null });
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock { Text = mate.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        nameText.Foreground = mate.ClassName is { } c ? ClassBrushes.Text(c) : (Brush)FindResource("Brush.OverlayText");
        who.Children.Add(nameText);
        var parts = new List<string>();
        if (mate.ClassName is { Length: > 0 } className)
        {
            parts.Add(className);
        }

        if (mate.ServerId is int server && Aion2Servers.IsKnown(server))
        {
            parts.Add(Aion2Servers.NameOf(server));
        }

        if (parts.Count > 0)
        {
            var line = new TextBlock { Text = string.Join("  ·  ", parts), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            who.Children.Add(line);
        }

        Grid.SetColumn(who, 1);
        grid.Children.Add(who);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (known?.CombatPower is int cp)
        {
            right.Children.Add(new TextBlock
            {
                Text = FindPlayerClient.Number(cp), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)), ToolTip = "Combat Power" + (known.ItemLevel is int il ? $" · Item Level {FindPlayerClient.Number(il)}" : ""),
            });
        }

        var go = new TextBlock { Text = "›", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        go.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        right.Children.Add(go);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        // never fully transparent: in the see-through look Windows gives clicks on such pixels to the game
        var rest = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
        var row = new Border { Child = grid, Padding = new Thickness(7, 4, 7, 4), CornerRadius = new CornerRadius(6), Background = rest, Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 2), ToolTip = $"{mate.Name}'s card" };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        row.MouseLeave += (_, _) => row.Background = rest;
        GroupMate picked = mate;
        row.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            if (LookedUp(picked) is { } found)
            {
                // looked up already (the group shown by itself): its card at once
                _findAuto = false;
                OverlayFindBox.Text = picked.Name;
                ShowFindResults(new[] { found });
                PickFindResult(found);
                return;
            }

            _ = FindGroupMateAsync(picked);
        };
        return row;
    }

    /// <summary>A group member clicked: the website's search for the name in the region of the member's server, kept to that server, then the card of the one found
    /// (several of the name on an unknown server: the list to pick from, as a typed search shows it).</summary>
    private async Task FindGroupMateAsync(GroupMate mate)
    {
        int asked = ++_findAsked;
        OverlayFindBox.Text = mate.Name;
        OverlayFindResults.Children.Clear();
        OverlayFindCard.Visibility = Visibility.Collapsed;
        ShowFindStatus("Searching…");
        int server = mate.ServerId is int s && Aion2Servers.IsKnown(s) ? s : 0;
        string region = server > 0 ? FindPlayerClient.RegionOf(server) : _findRegion.Length > 0 ? _findRegion : "all";
        FindAnswer answer = await FindPlayerClient.SearchAsync(mate.Name, region);
        if (asked != _findAsked)
        {
            return;
        }

        if (answer.Error is { } error)
        {
            ShowFindStatus(error);
            return;
        }

        IReadOnlyList<FoundPlayer> players = answer.Players.Where(p => p.Name == mate.Name && (server == 0 || p.ServerId == server)).ToList();
        if (players.Count == 0)
        {
            players = answer.Players.Where(p => server == 0 || p.ServerId == server).ToList(); // the website writes the name otherwise (capitals, accents)
        }

        if (players.Count == 0)
        {
            ShowFindStatus(server > 0
                ? $"The website can't find {mate.Name} on {Aion2Servers.NameOf(server)} yet."
                : $"The website can't find {mate.Name} yet.");
            return;
        }

        ShowFindResults(players);
        if (players.Count == 1)
        {
            PickFindResult(players[0]);
        }
    }

    // ------------------------------------------------------------------ the group by itself

    /// <summary>The roster names other players (the meter stays up then, with "stay up in dungeons and while in a group").</summary>
    private bool _groupSeen;

    /// <summary>The search was opened by <see cref="AutoShowGroup"/>: it folds away when a boss fight starts.</summary>
    private bool _findAuto;

    private string _autoGroupKey = "";
    private ScopeContext _autoGroupContext = ScopeContext.World;
    private DateTime _autoGroupAt = DateTime.MinValue;

    /// <summary>The party members looked up by themselves (by "name|server"): what the website's search found, and when.</summary>
    private readonly Dictionary<string, (FoundPlayer Player, DateTime At)> _mateLookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mateLooking = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan MateLookupKept = TimeSpan.FromMinutes(15);

    private static string MateKey(GroupMate mate) => mate.Name + "|" + (mate.ServerId ?? 0).ToString(CultureInfo.InvariantCulture);

    /// <summary>Party members as if looked up already (the pictures).</summary>
    internal void SeedMateLookups(IEnumerable<FoundPlayer> found)
    {
        foreach (FoundPlayer p in found)
        {
            _mateLookups[p.Name + "|" + p.ServerId.ToString(CultureInfo.InvariantCulture)] = (p, DateTime.UtcNow);
        }
    }

    private FoundPlayer? LookedUp(GroupMate mate) =>
        _mateLookups.TryGetValue(MateKey(mate), out var found) && DateTime.UtcNow - found.At < MateLookupKept ? found.Player : null;

    /// <summary>
    /// The group by itself (10 November 2026, the owner: "make the meter auto search and show the party members once in any party, Ludra, exploration,
    /// Conquest"): once a second, while the meter is up, the roster is read; a new group (someone joined, another party) or going into a dungeon with one opens
    /// Find a player on the group - never taking the keyboard from the game - and looks the party members up in the background, so their Combat Power shows on
    /// their rows and a click shows their card at once. Not in a boss fight; folded away when one starts. With "Show my group" off it only notes the group.
    /// </summary>
    private void AutoShowGroup(DateTime now)
    {
        // a boss fight starting folds the group away: the fight is on the meter then
        if (_findAuto && OverlayFind.Visibility == Visibility.Visible && now - _lastOwnBossFight < TimeSpan.FromSeconds(3) && OverlayFindBox.Text.Length == 0 && _findShown is null)
        {
            CloseFindPlayer();
        }

        if (now - _autoGroupAt < TimeSpan.FromSeconds(1) || Headless)
        {
            return;
        }

        _autoGroupAt = now;
        IReadOnlyList<GroupMate> mates = GroupMates();
        _groupSeen = mates.Count > 0;
        bool entered = _scopeContext == ScopeContext.Instance && _autoGroupContext != ScopeContext.Instance;
        _autoGroupContext = _scopeContext;
        if (mates.Count == 0)
        {
            _autoGroupKey = "";
            // out of the group (left the room, the room closed): the search it opened goes, the meter as it was (10 November 2026, the owner: "when leaving
            // a group, the meter should not still show them, instead return to original state"); one the player is using stays
            if (_findAuto && OverlayFind.Visibility == Visibility.Visible && OverlayFindBox.Text.Length == 0 && _findShown is null)
            {
                CloseFindPlayer();
            }

            return;
        }

        // opened also while the meter steps aside for a game window (the group finder is one): the list on show is what brings the meter up over it
        if (!_meterAutoGroup || !MeterOverlayActive)
        {
            return;
        }

        string key = string.Join("|", mates.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        if (key == _autoGroupKey && !entered)
        {
            return;
        }

        _autoGroupKey = key;
        _ = LookUpMatesAsync(mates);
        bool fighting = now - _lastOwnBossFight < TimeSpan.FromSeconds(10);
        bool busy = OverlayApps.Visibility == Visibility.Visible || (OverlayFind.Visibility == Visibility.Visible && (OverlayFindBox.Text.Length > 0 || _findShown is not null));
        if (fighting || busy)
        {
            return;
        }

        InitFindRegion();
        OverlayFind.Visibility = Visibility.Visible;
        ShowGroupLists();
        StartGroupWatch();
        _findAuto = true;
    }

    /// <summary>The party members the search hasn't found lately, one after the other (the website's searches are limited: at most ten a minute).</summary>
    private async Task LookUpMatesAsync(IReadOnlyList<GroupMate> mates)
    {
        foreach (GroupMate mate in mates.Take(7))
        {
            string key = MateKey(mate);
            if (LookedUp(mate) is not null || !_mateLooking.Add(key))
            {
                continue;
            }

            try
            {
                int server = mate.ServerId is int s && Aion2Servers.IsKnown(s) ? s : 0;
                string region = server > 0 ? FindPlayerClient.RegionOf(server) : _findRegion.Length > 0 ? _findRegion : "all";
                FindAnswer answer = await FindPlayerClient.SearchAsync(mate.Name, region);
                FoundPlayer? found = answer.Players.FirstOrDefault(p => p.Name == mate.Name && (server == 0 || p.ServerId == server))
                    ?? answer.Players.FirstOrDefault(p => string.Equals(p.Name, mate.Name, StringComparison.OrdinalIgnoreCase) && (server == 0 || p.ServerId == server));
                if (found is not null)
                {
                    _mateLookups[key] = (found, DateTime.UtcNow);
                    if (OverlayFind.Visibility == Visibility.Visible && OverlayFindBox.Text.Length == 0 && _findShown is null && _findPlayers.Count == 0)
                    {
                        ShowGroupLists(); // the row gets its Combat Power
                    }
                }
            }
            finally
            {
                _mateLooking.Remove(key);
            }

            await Task.Delay(400);
        }
    }

    private Border Tile(string title, string value, Color? colour, string? tip)
    {
        var stack = new StackPanel();
        var head = new TextBlock { Text = title, FontSize = 10.5 };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(head);
        var figure = new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        figure.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        if (colour is { } c)
        {
            figure.Foreground = new SolidColorBrush(c);
        }
        else
        {
            figure.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        }

        stack.Children.Add(figure);
        return new Border
        {
            Child = stack, Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 0, 5, 0), CornerRadius = new CornerRadius(7), ToolTip = tip,
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
    }
}
