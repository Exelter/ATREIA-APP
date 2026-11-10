using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The overlay editor (10 November 2026, the owner: "make it a simple key to open an overlay editor like this, that Questlog has, and allows everyone to add
/// stuff to their screen based on what we have; they turn it off/on, it shows a preview of what it will look like"; and the hotkeys "should open the menu
/// to edit them when they click keys"). One key ("Overlay editor", Ctrl+Alt+V unless changed; the overlay menu's first button too) opens it over the game:
/// every overlay as a tile to switch it on or off, its hotkey on it - a click on the key reads a new one - and, for all of them, size and opacity, the
/// Atreia button and Alt + drag. While it is open every overlay is unlocked where it sits on the faint grid, showing what it will look like, to be dragged
/// and sized; Done (or ✕, or the key again) locks them all in place.
/// </summary>
public partial class MainWindow
{
    private OverlayEditorWindow? _overlayEditor;
    private Window? _overlayGrid;
    private HotkeyAction? _readingKeysFor;
    private readonly DispatcherTimer _editorOpacitySave = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private double _editorOpacity = double.NaN;

    /// <summary>The overlays of the editor that have a hotkey of their own (by the overlay's id).</summary>
    private static HotkeyAction? EditorHotkeyOf(string id) => id switch
    {
        "meter" => HotkeyAction.ToggleHideUi,
        "combat" => HotkeyAction.ToggleCombatOverlay,
        "cooldowns" => HotkeyAction.ToggleAssistOverlay,
        "timetable" => HotkeyAction.ToggleTimetable,
        "boss" => HotkeyAction.ToggleWorldBossOverlay,
        "petmap" => HotkeyAction.TogglePetMap,
        "loot" => HotkeyAction.ToggleLootOverlay,
        "craft" => HotkeyAction.ToggleCraftOverlay,
        "network" => HotkeyAction.ToggleNetworkOverlay,
        "hotkeys" => HotkeyAction.ToggleHotkeyOverlay,
        _ => null,
    };

    /// <summary>The editor's key, the overlay menu's first button and the Atreia button's keys: open it, or close it when it is open.</summary>
    private void ToggleOverlayEditor()
    {
        if (_overlayEditor is not null)
        {
            CloseOverlayEditor();
        }
        else
        {
            OpenOverlayEditor();
        }
    }

    /// <summary>Opens the editor (or brings it up), reading a new key for <paramref name="readKeysFor"/> when given (a key clicked in the overlay menu).</summary>
    private void OpenOverlayEditor(HotkeyAction? readKeysFor = null)
    {
        if (Headless)
        {
            return;
        }

        _overlayMenu?.CloseMenu();
        if (_overlayEditor is null)
        {
            MeterSettings s = MeterSettings.Load();
            // every overlay unlocked while the editor is open: each one shows where it sits and how it looks, to be dragged and sized there
            if (!(OverlaysMovable(s) && !_meterOverlayLocked))
            {
                SetOverlaysMovable(true);
                s = MeterSettings.Load();
                s.MeterOverlayLocked = _meterOverlayLocked = false;
                s.Save();
                ApplyOverlayLock();
            }

            Rect area = EditorArea();
            _editorArea = area;
            _overlayGrid = OverlayEditorWindow.Grid(area);
            _overlayGrid.Show();
            var editor = new OverlayEditorWindow(s);
            editor.Closed += (_, _) => OverlayEditorClosed(editor);
            _overlayEditor = editor;
            FillOverlayEditor();
            editor.Show();
            editor.Place(area);
            if (!_editorOpacitySaveHooked)
            {
                _editorOpacitySaveHooked = true;
                _editorOpacitySave.Tick += (_, _) => SaveEditorOpacity();
            }
        }

        if (readKeysFor is { } action)
        {
            ReadEditorKeys(action);
        }
    }

    private bool _editorOpacitySaveHooked;

    /// <summary>Done, ✕ or the key again: the editor goes, every overlay is locked in place (clicks go to the game again).</summary>
    private void CloseOverlayEditor()
    {
        _overlayEditor?.Close();
    }

    private void OverlayEditorClosed(OverlayEditorWindow editor)
    {
        if (!ReferenceEquals(_overlayEditor, editor))
        {
            return;
        }

        _overlayEditor = null;
        if (_readingKeysFor is not null)
        {
            _readingKeysFor = null;
            ApplyHotkeys(MeterSettings.Load()); // the hotkeys were let go while a new one was read
        }

        if (_editorOpacitySave.IsEnabled)
        {
            _editorOpacitySave.Stop();
            SaveEditorOpacity();
        }

        _overlayGrid?.Close();
        _overlayGrid = null;
        SetOverlaysMovable(false);
        MeterSettings s = MeterSettings.Load();
        s.MeterOverlayLocked = _meterOverlayLocked = true;
        s.Save();
        ApplyOverlayLock();
        CubeToast.Note(string.Format(CultureInfo.CurrentCulture, "Overlays locked in place: clicks go to the game. {0} opens the editor again.",
            HotkeyBinding.Parse(s.HotkeyMoveOverlays) is { IsEmpty: false } keys ? keys.ToString() : "The Atreia button"));
    }

    /// <summary>The editor and its grid step aside while neither the game nor this app is in front (never over a browser or the desktop).</summary>
    private void GateOverlayEditor()
    {
        if (_overlayEditor is not { } editor)
        {
            return;
        }

        bool show = GameWindow.ForegroundIsGameOrSelf(ignoreMenu: true);
        if (show && !editor.IsVisible)
        {
            _overlayGrid?.Show();
            editor.Show();
        }
        else if (!show && editor.IsVisible && !editor.Reading)
        {
            _overlayGrid?.Hide();
            editor.Hide();
        }
    }

    /// <summary>The screen the editor and its grid go on: the one with the meter (the game's), else the main one.</summary>
    private Rect EditorArea()
    {
        Rect anchor = _meterOverlay is { IsVisible: true } m ? new Rect(m.Left, m.Top, Math.Max(1, m.ActualWidth), Math.Max(1, m.ActualHeight))
            : _overlayMenu is { IsVisible: true } b ? new Rect(b.Left, b.Top, 1, 1) : WindowPlacement.Primary();
        return WindowPlacement.WorkAreaOf(anchor);
    }

    /// <summary>The editor's content built anew (a switch flipped, a key changed).</summary>
    private void FillOverlayEditor()
    {
        if (_overlayEditor is not { } editor)
        {
            return;
        }

        editor.Body = BuildOverlayEditor(CloseOverlayEditor, MeterSettings.Load(), editor);
    }

    /// <summary>
    /// What the editor holds: its head, the overlays as tiles two by two (lit while on; the key on a tile reads a new one), then what counts for all of them,
    /// and the buttons. <paramref name="editor"/> is null in the pictures.
    /// </summary>
    internal FrameworkElement BuildOverlayEditor(Action close, MeterSettings s, OverlayEditorWindow? editor)
    {
        var root = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };

        // the head: the mark, the name, ✕ (dragged to move the editor)
        var head = new DockPanel { LastChildFill = true, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 4) };
        var x = MenuIconButton("", "Close: the overlays lock in place", close);
        DockPanel.SetDock(x, Dock.Right);
        head.Children.Add(x);
        var mark = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1),
            Background = MenuBrush(0x24, MenuGold.R, MenuGold.G, MenuGold.B), BorderBrush = MenuBrush(0x55, MenuGold.R, MenuGold.G, MenuGold.B),
            Child = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(MenuGold),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(mark, Dock.Left);
        head.Children.Add(mark);
        head.Children.Add(new TextBlock { Text = "Overlay editor", FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = MenuBrush(0xFF, 0xF1, 0xF4, 0xF7), VerticalAlignment = VerticalAlignment.Center });
        editor?.DragFrom(head);
        root.Children.Add(head);
        root.Children.Add(new TextBlock
        {
            Text = "Click an overlay to switch it on or off. While this is open, each one shows where it sits and what it looks like: drag it, size it at its "
                + "corners. Click a key to change it. Done locks them in place.",
            FontSize = 11.5, Foreground = MenuBrush(0xFF, 0x9A, 0xA4, 0xAE), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10),
        });

        // the overlays, two by two, in the overlay menu's groups
        root.Children.Add(EditorHeading("Overlays"));
        var tiles = new UniformGrid2();
        foreach (OverlayDef def in OverlayDefs().Where(d => d.Id != "menu").OrderBy(d => Array.IndexOf(MenuGroups, MenuGroupOf(d.Id))))
        {
            tiles.Add(EditorTile(def, s));
        }

        root.Children.Add(tiles.Panel);

        // what counts for all of them
        root.Children.Add(EditorHeading("All overlays", 14));
        IReadOnlyList<EditorScreen> screens = editor is null ? SampleScreens : EditorScreens();
        if (screens.Count > 1)
        {
            // which screen (10 November 2026, the owner: "allow people to select screen 1 or screen 2, so if they want the overlays on screen 2, where the game
            // isn't, they can")
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var screenLabel = EditorLabel("Screen");
            screenLabel.Width = 70;
            DockPanel.SetDock(screenLabel, Dock.Left);
            line.Children.Add(screenLabel);
            var chips = new WrapPanel();
            foreach (EditorScreen screen in screens)
            {
                bool here = editor is null ? screen.Number == 1 : SameArea(screen.Area, _editorArea);
                chips.Children.Add(ScreenChip(screen, here, () => MoveOverlaysToScreen(screen)));
            }

            line.Children.Add(chips);
            root.Children.Add(line);
        }

        var size = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var sizeButtons = new StackPanel { Orientation = Orientation.Horizontal };
        sizeButtons.Children.Add(EditorButton("", "Smaller", () => ScaleAllOverlays(0.9), false));
        sizeButtons.Children.Add(EditorButton("", "Bigger", () => ScaleAllOverlays(1.1), false));
        DockPanel.SetDock(sizeButtons, Dock.Right);
        size.Children.Add(sizeButtons);
        size.Children.Add(EditorLabel("Size"));
        root.Children.Add(size);

        double opacity = Math.Round(Math.Clamp(double.IsFinite(s.OverlayOpacity) ? s.OverlayOpacity : 0.6, 0.2, 1.0) * 100);
        var opacityLine = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var percent = new TextBlock { Text = $"{opacity:0} %", Width = 44, TextAlignment = TextAlignment.Right, FontSize = 11.5, Foreground = MenuBrush(0xFF, 0xD8, 0xDE, 0xE4), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(percent, Dock.Right);
        opacityLine.Children.Add(percent);
        var label = EditorLabel("Opacity");
        label.Width = 70;
        DockPanel.SetDock(label, Dock.Left);
        opacityLine.Children.Add(label);
        var slider = new GoldSlider(20, 100, 5, opacity);
        slider.Changed += value =>
        {
            percent.Text = $"{value:0} %";
            _editorOpacity = value / 100.0;
            _editorOpacitySave.Stop();
            _editorOpacitySave.Start(); // saved once the slider rests
        };
        opacityLine.Children.Add(slider);
        root.Children.Add(opacityLine);

        root.Children.Add(EditorSwitch("The Atreia button over the game", "The small button that opens the overlay menu (drag it to move it)", s.ShowOverlayMenu, on =>
        {
            MeterSettings now = MeterSettings.Load();
            now.ShowOverlayMenu = on;
            now.Save();
            _gateSettings = now;
        }));
        root.Children.Add(EditorSwitch("Alt + drag moves overlays", "Off by default: Alt is the game's cursor key, so with this on an overlay comes along when you click in the game with Alt held",
            s.AltDragOverlays, on =>
            {
                MeterSettings now = MeterSettings.Load();
                now.AltDragOverlays = on;
                now.Save();
            }));
        root.Children.Add(EditorSwitch("Step aside in the game's windows", "The overlays hide while the inventory, the map or another game window is open", s.HideDuringMenus, on =>
        {
            MeterSettings now = MeterSettings.Load();
            now.HideDuringMenus = on;
            now.Save();
        }));

        // the buttons: the hotkeys and the overlays in the app, Done
        root.Children.Add(new Border { Height = 1, Background = MenuBrush(0x1C, 0xFF, 0xFF, 0xFF), Margin = new Thickness(0, 10, 0, 10) });
        var foot = new DockPanel { LastChildFill = false };
        var done = EditorButton("", "Done", close, true);
        DockPanel.SetDock(done, Dock.Right);
        foot.Children.Add(done);
        foot.Children.Add(EditorButton("", "Hotkeys", () =>
        {
            close();
            BringAppForward();
            OpenSettingsPage("");
        }, false));
        foot.Children.Add(EditorButton("", "Open in the app", () =>
        {
            close();
            BringAppForward();
            ShowShellPage("overlays");
        }, false));
        root.Children.Add(foot);
        return root;
    }

    private static TextBlock EditorHeading(string text, double top = 0) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, top, 0, 7),
        Foreground = MenuBrush(0xC8, MenuGold.R, MenuGold.G, MenuGold.B),
    };

    private static TextBlock EditorLabel(string text) => new() { Text = text, FontSize = 12, Foreground = MenuBrush(0xFF, 0xD8, 0xDE, 0xE4), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// One overlay of the editor: its picture and name on a tile, lit gold while on (a click switches it), and its hotkey on the right - a click on the key
    /// reads a new one (Backspace: none, Esc: as it was).
    /// </summary>
    private FrameworkElement EditorTile(OverlayDef def, MeterSettings s)
    {
        bool on = def.Get(s);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = new TextBlock
        {
            Text = MenuGlyphOf(def.Id), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Width = 20, VerticalAlignment = VerticalAlignment.Center,
            Foreground = on ? new SolidColorBrush(MenuGold) : MenuBrush(0xFF, 0x7A, 0x82, 0x8C),
        };
        grid.Children.Add(glyph);
        var name = new TextBlock
        {
            Text = EditorNameOf(def), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = on ? MenuBrush(0xFF, 0xF4, 0xF6, 0xF8) : MenuBrush(0xFF, 0xA8, 0xB0, 0xB8), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (EditorHotkeyOf(def.Id) is { } action)
        {
            HotkeyTable.Entry entry = HotkeyTable.Of(action);
            HotkeyBinding binding = HotkeyBinding.Parse(entry.Get(s));
            bool reading = _readingKeysFor == action;
            bool broken = HotkeyProblems.ContainsKey(action);
            var cap = new Border
            {
                CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                BorderBrush = reading ? new SolidColorBrush(MenuGold) : broken ? MenuBrush(0xFF, 0x8A, 0x3A, 0x34) : MenuBrush(0x30, 0xFF, 0xFF, 0xFF),
                Background = reading ? MenuBrush(0x30, MenuGold.R, MenuGold.G, MenuGold.B) : MenuBrush(0x12, 0xFF, 0xFF, 0xFF),
                ToolTip = reading ? "Press the new keys - Backspace: no key, Esc: keep it as it was"
                    : (binding.IsEmpty ? "No key yet: click to give it one" : "Click to change the key") + (broken ? " (this one does nothing now: another program or hotkey has it)" : ""),
                Child = new TextBlock
                {
                    Text = reading ? "Press keys…" : binding.IsEmpty ? "+ key" : binding.ToString(), FontSize = 9.5,
                    Foreground = reading ? MenuBrush(0xFF, 0xF4, 0xE9, 0xCF) : broken ? MenuBrush(0xFF, 0xFF, 0x7A, 0x6E) : binding.IsEmpty ? MenuBrush(0xFF, 0x7A, 0x82, 0x8C) : MenuBrush(0xFF, 0xC9, 0xAA, 0x70),
                },
            };
            cap.MouseLeftButtonDown += (_, e) => e.Handled = true;
            cap.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ReadEditorKeys(action);
            };
            Grid.SetColumn(cap, 2);
            grid.Children.Add(cap);
        }

        var rest = on ? MenuBrush(0x26, MenuGold.R, MenuGold.G, MenuGold.B) : MenuBrush(0x0C, 0xFF, 0xFF, 0xFF);
        var tile = new Border
        {
            Child = grid, Padding = new Thickness(9, 6, 6, 6), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, MinHeight = 32,
            Background = rest, BorderBrush = on ? MenuBrush(0xA0, MenuGold.R, MenuGold.G, MenuGold.B) : MenuBrush(0x1C, 0xFF, 0xFF, 0xFF),
            ToolTip = def.What,
        };
        tile.MouseEnter += (_, _) => tile.Background = on ? MenuBrush(0x36, MenuGold.R, MenuGold.G, MenuGold.B) : MenuBrush(0x1A, 0xFF, 0xFF, 0xFF);
        tile.MouseLeave += (_, _) => tile.Background = rest;
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SetOverlayFromMenu(def, !on);
            FillOverlayEditor();
        };
        return tile;
    }

    /// <summary>The screen the editor and its grid are on (where its screen buttons put the overlays).</summary>
    private Rect _editorArea;

    /// <summary>The screen the game was last seen on (by its number less one), to mark it among the editor's screens.</summary>
    private int _editorGameScreen = -1;

    /// <summary>A monitor as the editor offers it: its number (Windows' order), its work area, its size in pixels, whether it is the main one and the game's.</summary>
    private sealed record EditorScreen(int Number, Rect Area, string Size, bool Main, bool Game);

    /// <summary>Two screens for the pictures (the build's machine has one).</summary>
    private static readonly EditorScreen[] SampleScreens =
    {
        new(1, new Rect(0, 0, 2560, 1400), "2560 × 1440", true, true),
        new(2, new Rect(2560, 0, 1920, 1040), "1920 × 1080", false, false),
    };

    private IReadOnlyList<EditorScreen> EditorScreens()
    {
        System.Windows.Forms.Screen[] all = System.Windows.Forms.Screen.AllScreens;
        IReadOnlyList<Rect> areas = WindowPlacement.WorkAreas(); // in the same order, in the app's units
        if (all.Length != areas.Count)
        {
            return Array.Empty<EditorScreen>();
        }

        if (GameWindow.ForegroundClientArea(ignoreMenu: true) is { } game)
        {
            var middle = new System.Drawing.Point(game.X + game.Width / 2, game.Y + game.Height / 2);
            int at = Array.FindIndex(all, screen => screen.Bounds.Contains(middle));
            if (at >= 0)
            {
                _editorGameScreen = at;
            }
        }

        return all.Select((screen, i) => new EditorScreen(i + 1, areas[i], $"{screen.Bounds.Width} × {screen.Bounds.Height}", screen.Primary, i == _editorGameScreen)).ToList();
    }

    private static bool SameArea(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) < 2 && Math.Abs(a.Top - b.Top) < 2 && Math.Abs(a.Width - b.Width) < 2 && Math.Abs(a.Height - b.Height) < 2;

    /// <summary>A screen of the editor: its number with the game's or the main one's mark, its size under it; lit where the editor is.</summary>
    private static FrameworkElement ScreenChip(EditorScreen screen, bool here, Action click)
    {
        var words = new StackPanel();
        words.Children.Add(new TextBlock
        {
            Text = $"Screen {screen.Number}" + (screen.Game ? " · game" : screen.Main ? " · main" : ""), FontSize = 11.5, FontWeight = here ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = here ? MenuBrush(0xFF, 0xF4, 0xF6, 0xF8) : MenuBrush(0xFF, 0xB8, 0xC0, 0xC8),
        });
        words.Children.Add(new TextBlock { Text = screen.Size, FontSize = 9.5, Foreground = MenuBrush(0xFF, 0x8A, 0x93, 0x9D), Margin = new Thickness(0, 1, 0, 0) });
        Brush rest = here ? MenuBrush(0x26, MenuGold.R, MenuGold.G, MenuGold.B) : MenuBrush(0x0C, 0xFF, 0xFF, 0xFF);
        var chip = new Border
        {
            Child = words, Padding = new Thickness(9, 4, 10, 5), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = rest, BorderBrush = here ? MenuBrush(0xA0, MenuGold.R, MenuGold.G, MenuGold.B) : MenuBrush(0x1C, 0xFF, 0xFF, 0xFF), Cursor = here ? Cursors.Arrow : Cursors.Hand,
            ToolTip = here ? "The overlays are being placed on this screen" : $"Every overlay to screen {screen.Number}, in the same spots relative to its corners (the game can stay on "
                + "another screen; the overlays still show only while the game is in front). The editor goes along.",
        };
        if (!here)
        {
            chip.MouseEnter += (_, _) => chip.Background = MenuBrush(0x1A, 0xFF, 0xFF, 0xFF);
            chip.MouseLeave += (_, _) => chip.Background = rest;
            chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
        }

        return chip;
    }

    /// <summary>
    /// Every overlay to <paramref name="screen"/>: each open one moved to the same spot relative to the corners of its new screen (one at the right edge stays at
    /// the right edge), the saved places of the others too, so they come up there when switched on; the editor and its grid go along. The Atreia button
    /// stays over the game.
    /// </summary>
    private void MoveOverlaysToScreen(EditorScreen screen)
    {
        Rect target = screen.Area;
        Point? Into(Rect bounds)
        {
            Rect from = WindowPlacement.WorkAreaOf(bounds);
            return SameArea(from, target) ? null : MapInto(bounds, from, target);
        }

        foreach (Window window in Application.Current.Windows.Cast<Window>().ToList())
        {
            if (window is IOverlayPlace place && !place.PlaceIsView && window is not OverlayMenuWindow && Into(place.PlaceBounds) is { } to)
            {
                place.PlaceAt(to.X, to.Y, save: true);
            }
        }

        OverlayPlaces.MoveSaved(Into);
        _editorArea = target;
        if (_overlayGrid is { } grid)
        {
            (grid.Left, grid.Top, grid.Width, grid.Height) = (target.Left, target.Top, target.Width, target.Height);
        }

        _overlayEditor?.PlaceOn(target);
        FillOverlayEditor();
        CubeToast.Note($"Overlays on screen {screen.Number}: they show there while the game is in front.", 4);
    }

    /// <summary>The same spot relative to the corners of another screen: the share of the free room to the left and above is kept.</summary>
    private static Point MapInto(Rect bounds, Rect from, Rect to)
    {
        double fx = from.Width - bounds.Width > 1 ? Math.Clamp((bounds.Left - from.Left) / (from.Width - bounds.Width), 0, 1) : 0;
        double fy = from.Height - bounds.Height > 1 ? Math.Clamp((bounds.Top - from.Top) / (from.Height - bounds.Height), 0, 1) : 0;
        return new Point(to.Left + fx * Math.Max(0, to.Width - bounds.Width), to.Top + fy * Math.Max(0, to.Height - bounds.Height));
    }

    /// <summary>An overlay's name on its tile: short in English (a tile is half the editor wide, with its key beside the name), else the app's own.</summary>
    private static string EditorNameOf(OverlayDef def) => LocalizationManager.Instance.Language != "en" ? def.Name : def.Id switch
    {
        "combat" => "Boss fight",
        "cooldowns" => "Skill assist",
        "effects" => "Buffs on you",
        "boss" => "World bosses",
        "petmap" => "Map",
        "craft" => "Crafting",
        "network" => "Network",
        "season" => "Season missions",
        "hotkeys" => "Hotkey sheet",
        _ => def.Name,
    };

    /// <summary>
    /// A slider in the overlays' look: a thin track, gold up to the value, a round gold knob; dragged or clicked, in steps. <see cref="Changed"/> hears every new
    /// value. The editor never takes the keyboard, so there are no keys for it.
    /// </summary>
    private sealed class GoldSlider : Grid
    {
        private readonly double _min, _max, _step;
        private readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(MenuGold) };
        private readonly Border _knob = new()
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(MenuGold), BorderBrush = MenuBrush(0xFF, 0x1A, 0x12, 0x05),
        };
        private double _value;

        public event Action<double>? Changed;

        public GoldSlider(double min, double max, double step, double value)
        {
            (_min, _max, _step) = (min, max, step);
            _value = Math.Clamp(value, min, max);
            Height = 20;
            Margin = new Thickness(0, 0, 4, 0);
            Background = Brushes.Transparent; // the whole strip takes the mouse
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = MenuBrush(0x26, 0xFF, 0xFF, 0xFF), VerticalAlignment = VerticalAlignment.Center });
            _fill.VerticalAlignment = VerticalAlignment.Center;
            _knob.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(_fill);
            Children.Add(_knob);
            SizeChanged += (_, _) => Lay();
            MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                CaptureMouse();
                Follow(e.GetPosition(this).X);
            };
            MouseMove += (_, e) =>
            {
                if (IsMouseCaptured)
                {
                    Follow(e.GetPosition(this).X);
                }
            };
            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ReleaseMouseCapture();
            };
        }

        private void Follow(double x)
        {
            double usable = Math.Max(1, ActualWidth - _knob.Width);
            double raw = _min + Math.Clamp((x - _knob.Width / 2) / usable, 0, 1) * (_max - _min);
            double value = Math.Clamp(Math.Round(raw / _step) * _step, _min, _max);
            if (Math.Abs(value - _value) < 1e-9)
            {
                return;
            }

            _value = value;
            Lay();
            Changed?.Invoke(value);
        }

        private void Lay()
        {
            double usable = Math.Max(0, ActualWidth - _knob.Width);
            double at = (_value - _min) / Math.Max(1e-9, _max - _min) * usable;
            _knob.Margin = new Thickness(at, 0, 0, 0);
            _fill.Width = at + _knob.Width / 2;
        }
    }

    /// <summary>
    /// A key of the editor clicked (or of the overlay menu): the hotkeys let go (so the keys pressed reach the editor, also ones taken already) and the next
    /// keys read; a combination another of the meter's hotkeys has moves here (that one has none then), and every hotkey is registered again.
    /// </summary>
    private void ReadEditorKeys(HotkeyAction action)
    {
        if (_overlayEditor is not { } editor)
        {
            return;
        }

        if (editor.Reading)
        {
            editor.StopReading();
        }

        _readingKeysFor = action;
        _hotkeys?.Apply(Array.Empty<KeyValuePair<HotkeyAction, HotkeyBinding>>());
        FillOverlayEditor();
        editor.ReadKeys(binding =>
        {
            _readingKeysFor = null;
            MeterSettings s = MeterSettings.Load();
            if (binding is { } chosen)
            {
                string text = chosen.ToString();
                if (!chosen.IsEmpty)
                {
                    foreach (HotkeyTable.Entry other in HotkeyTable.All.Where(e => e.Action != action && HotkeyBinding.Parse(e.Get(s)) == chosen))
                    {
                        other.Set(s, "");
                        CubeToast.Note($"{chosen}: {LocalizationManager.Instance[HotkeyTable.Of(action).NameKey]} now ({LocalizationManager.Instance[other.NameKey]} has no key)", 5);
                    }
                }

                HotkeyTable.Of(action).Set(s, text);
                s.Save();
            }

            ApplyHotkeys(s);
            _settingsWindow?.SyncOverlaySwitches(s);
            _overlayMenu?.ShowKeysAgain();
            FillOverlayEditor();
        });
    }

    /// <summary>The opacity slider rested: the meter and every overlay at the new opacity, the editor's own glass too.</summary>
    private void SaveEditorOpacity()
    {
        _editorOpacitySave.Stop();
        if (!double.IsFinite(_editorOpacity))
        {
            return;
        }

        MeterSettings s = MeterSettings.Load();
        s.OverlayOpacity = _editorOpacity;
        s.TimetableOpacity = null; // follows the meter's
        foreach (string title in s.OverlayOpacities.Keys.ToList())
        {
            s.OverlayOpacities[title] = _editorOpacity;
        }

        s.Save();
        _editorOpacity = double.NaN;
        ApplySettingsEverywhere(s);
        _overlayEditor?.Restyle(s);
    }

    /// <summary>A switch of the editor: the words on the left, a gold switch on the right; the whole line flips it.</summary>
    private static FrameworkElement EditorSwitch(string text, string tip, bool on, Action<bool> changed)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(EditorLabel(text));
        var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);
        var row = new Border { Child = grid, Padding = new Thickness(0, 4, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };

        void Paint()
        {
            track.Background = on ? new SolidColorBrush(MenuGold) : MenuBrush(0x18, 0xFF, 0xFF, 0xFF);
            track.BorderBrush = on ? new SolidColorBrush(MenuGold) : MenuBrush(0x30, 0xFF, 0xFF, 0xFF);
            knob.Background = on ? MenuBrush(0xFF, 0x1A, 0x12, 0x05) : MenuBrush(0xFF, 0x8A, 0x93, 0x9D);
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        Paint();
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            on = !on;
            Paint();
            changed(on);
        };
        return row;
    }

    /// <summary>A button of the editor: an icon and a word; the main one (Done) filled gold.</summary>
    private static FrameworkElement EditorButton(string glyph, string text, Action click, bool main)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0),
            Foreground = main ? MenuBrush(0xFF, 0x1A, 0x12, 0x05) : new SolidColorBrush(MenuGold),
        });
        line.Children.Add(new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = main ? MenuBrush(0xFF, 0x1A, 0x12, 0x05) : MenuBrush(0xFF, 0xE9, 0xEE, 0xF2),
        });
        Brush rest = main ? new SolidColorBrush(MenuGold) : MenuBrush(0x10, 0xFF, 0xFF, 0xFF);
        var b = new Border
        {
            Child = line, Padding = new Thickness(11, 5, 12, 6), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Background = rest, BorderBrush = main ? new SolidColorBrush(MenuGold) : MenuBrush(0x22, 0xFF, 0xFF, 0xFF),
        };
        b.MouseEnter += (_, _) => b.Background = main ? MenuBrush(0xFF, 0xE8, 0xC6, 0x86) : MenuBrush(0x20, 0xFF, 0xFF, 0xFF);
        b.MouseLeave += (_, _) => b.Background = rest;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // no drag of the editor from a button
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    /// <summary>The editor as a picture (the pictures of the build: render-tool overlay-editor): its frame with what it holds, for the settings given.</summary>
    internal FrameworkElement OverlayEditorPicture(MeterSettings s)
    {
        var frame = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Width = OverlayEditorWindow.EditorWidth,
            BorderBrush = InGameGlass.Edge(s), Background = InGameGlass.Background(s), Child = BuildOverlayEditor(() => { }, s, null),
        };
        InGameGlass.Apply(frame);
        return frame;
    }
}
