using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// What an overlay's own buttons need of it (11 November 2026, the owner: "every overlay should have a settings button and a lock button to allow users to change
/// the size etc or settings without needing to open the app"): its name, its lock in the settings, its switch, and its page in the app. Named by the window title.
/// </summary>
public static class OverlayQuick
{
    public sealed record Entry(Func<string> Name, Func<MeterSettings, bool> Locked, Action<MeterSettings, bool> SetLocked, Action<MeterSettings>? Hide, string Page);

    private static string L(string key) => LocalizationManager.Instance[key];

    private static readonly Dictionary<string, Entry> Entries = new()
    {
        [OverlayPlaces.Assist] = new(() => L("Shell.OvCooldowns"), s => s.AssistOverlayLocked, (s, v) => s.AssistOverlayLocked = v, s => s.ShowAssistOverlay = false, "assist." + AssistWindow.CooldownsTab),
        [OverlayPlaces.Effects] = new(() => L("Shell.OvEffects"), s => s.AssistOverlayLocked, (s, v) => s.AssistOverlayLocked = v, s => s.ShowEffectsOverlay = false, "assist." + AssistWindow.BuffsTab),
        [OverlayPlaces.Network] = new(() => L("Settings.Nav.Network"), s => s.NetworkOverlayLocked, (s, v) => s.NetworkOverlayLocked = v, s => s.ShowNetworkOverlay = false, "network"),
        [OverlayPlaces.Dummy] = new(() => L("Shell.Dummy"), s => s.DummyOverlayLocked, (s, v) => s.DummyOverlayLocked = v, s => s.ShowDummyOverlay = false, "dummy"),
        [OverlayPlaces.WorldBoss] = new(() => L("Settings.Nav.Boss"), s => s.BossOverlayLocked, (s, v) => s.BossOverlayLocked = v, s => s.ShowBossOverlay = false, "boss"),
        [OverlayPlaces.Hotkeys] = new(() => L("Shell.OvHotkeys"), s => s.HotkeyOverlayLocked ?? s.PetFarmLocked, (s, v) => s.HotkeyOverlayLocked = v, s => s.ShowHotkeyOverlay = false, "overlays"),
        [OverlayPlaces.Timetable] = new(() => L("Timers.Title"), s => s.TimetableLocked, (s, v) => s.TimetableLocked = v, s => s.ShowTimetable = false, "timetable"),
        [OverlayPlaces.PetMap] = new(() => L("Settings.Nav.PetMap"), s => s.PetFarmLocked, (s, v) => s.PetFarmLocked = v, s => s.ShowPetMap = false, "petmap"),
        [OverlayPlaces.PetList] = new(() => L("Shell.LayoutPetList"), s => s.PetListLocked ?? s.PetFarmLocked, (s, v) => s.PetListLocked = v, s => s.ShowPetMap = false, "petmap"),
        [OverlayPlaces.PetInfo] = new(() => L("Shell.OvPetFarm"), s => s.PetInfoLocked, (s, v) => s.PetInfoLocked = v, s => s.ShowPetFarm = false, "petmap.pets"),
        [OverlayPlaces.Loot] = new(() => L("Main.MenuTools.Loot"), s => s.LootOverlayLocked, (s, v) => s.LootOverlayLocked = v, s => s.ShowLootOverlay = false, "loot"),
        [OverlayPlaces.Gather] = new(() => L("Settings.Nav.Gather"), s => s.GatherOverlayLocked, (s, v) => s.GatherOverlayLocked = v, s => s.ShowGatherOverlay = false, "petmap.gather"),
        [OverlayPlaces.Season] = new(() => L("Checklist.Season"), s => s.SeasonOverlayLocked, (s, v) => s.SeasonOverlayLocked = v, s => s.ShowSeasonOverlay = false, "checklist"),
        [OverlayPlaces.Craft] = new(() => L("Main.MenuTools.Craft"), s => s.CraftOverlayLocked, (s, v) => s.CraftOverlayLocked = v, s => s.ShowCraftOverlay = false, "craft"),
        [OverlayPlaces.BossCast] = new(() => L("Settings.Nav.Combat"), s => s.CombatCastOverlayLocked ?? s.CombatOverlayLocked, (s, v) => s.CombatCastOverlayLocked = v, s => s.ShowCombatOverlay = false, "combat"),
        [OverlayPlaces.BossHp] = new(() => L("Settings.Nav.Combat"), s => s.CombatHpOverlayLocked ?? s.CombatOverlayLocked, (s, v) => s.CombatHpOverlayLocked = v, s => s.ShowCombatOverlay = false, "combat"),
        [OverlayPlaces.TargetOfTarget] = new(() => L("Settings.Nav.Combat"), s => s.CombatOverlayLocked, (s, v) => s.CombatOverlayLocked = v, s => s.ShowCombatOverlay = false, "combat"),
        [OverlayPlaces.Fps] = new(() => "FPS counter", s => s.FpsOverlayLocked, (s, v) => s.FpsOverlayLocked = v, s => s.ShowFpsOverlay = false, "overlays"),
        [OverlayPlaces.EnemyCard] = new(() => "Enemy card", s => s.EnemyCardLocked, (s, v) => s.EnemyCardLocked = v, s => s.ShowEnemyCard = false, "overlays"),
        [OverlayPlaces.EnemyRadar] = new(() => "Abyss radar", s => s.EnemyRadarLocked, (s, v) => s.EnemyRadarLocked = v, s => s.ShowEnemyRadar = false, "overlays"),
        [OverlayPlaces.DeathRecap] = new(() => "Death recap", s => s.DeathRecapLocked, (s, v) => s.DeathRecapLocked = v, s => s.ShowDeathRecap = false, "overlays"),
    };

    public static Entry? Of(string title) => Entries.GetValueOrDefault(title);

    /// <summary>A setting changed from an overlay's buttons: the app puts it on everywhere (MainWindow).</summary>
    public static event Action<MeterSettings>? Changed;

    /// <summary>"All settings": the app opens the overlay's page (MainWindow).</summary>
    public static event Action<string>? PageAsked;

    internal static void RaiseChanged(MeterSettings s) => Changed?.Invoke(s);

    internal static void AskPage(string page) => PageAsked?.Invoke(page);

    /// <summary>Locks or unlocks the overlay (its padlock, or the switch of its settings).</summary>
    internal static void SetLocked(IOverlayQuick overlay, bool locked)
    {
        if (Of(overlay.Title) is not { } entry)
        {
            overlay.ApplyLocked(locked);
            return;
        }

        MeterSettings s = MeterSettings.Load();
        entry.SetLocked(s, locked);
        s.Save();
        overlay.ApplyLocked(locked);
        RaiseChanged(s);
    }
}

/// <summary>
/// An overlay's settings at the overlay (its gear): how big it is, how see-through its background is, locked or free to move and size, back at its first place,
/// switched off, or all its settings in the app. Opens beside the overlay and closes when the game (or anything else) is clicked.
/// </summary>
public sealed class OverlayQuickWindow : Window
{
    private static OverlayQuickWindow? _open;
    private readonly IOverlayQuick _overlay;
    private readonly System.Windows.Threading.DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _dirtyScale;

    public static void Open(IOverlayQuick overlay)
    {
        _open?.Close();
        _open = new OverlayQuickWindow(overlay);
        _open.Closed += (_, _) => { overlay.QuickOpen = false; _open = null; };
        overlay.QuickOpen = true;
        _open.Show();
        _open.Activate();
    }

    /// <summary>For the pictures: the panel of an overlay, never shown as a window.</summary>
    internal static FrameworkElement PictureOf(IOverlayQuick overlay)
    {
        var window = new OverlayQuickWindow(overlay);
        var panel = (FrameworkElement)window.Content;
        window.Content = null;
        return panel;
    }

    private OverlayQuickWindow(IOverlayQuick overlay)
    {
        _overlay = overlay;
        OverlayQuick.Entry? entry = OverlayQuick.Of(overlay.Title);
        Title = "Overlay settings";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        AltTabHidden.Apply(this);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var stack = new StackPanel { Width = 268 };
        // the head: the overlay's name and a close button
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = Glyph("", 11, "Close (Esc)");
        close.MouseLeftButtonUp += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        var name = new TextBlock { Text = entry?.Name() ?? overlay.Title.Replace("Aion DPS ", ""), FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        head.Children.Add(name);
        stack.Children.Add(head);

        // its size
        stack.Children.Add(SliderRow("Size", 50, 300, Math.Round(overlay.PlaceScale * 100), v => $"{v:0} %", v =>
        {
            overlay.PlaceScaleTo(v / 100, save: false);
            _dirtyScale = true;
            Later();
        }));

        // how see-through its background is
        stack.Children.Add(SliderRow("Background", 20, 100, Math.Round(overlay.QuickOpacity * 100), v => $"{v:0} %", v => overlay.SaveQuickOpacity(v / 100)));

        // locked or free
        var lockRow = new CheckBox
        {
            Content = new TextBlock { Text = "Locked in place (clicks go through to the game)", TextWrapping = TextWrapping.Wrap }, IsChecked = overlay.IsLocked,
            Margin = new Thickness(0, 6, 0, 2), FontSize = 12.5,
        };
        lockRow.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        lockRow.Checked += (_, _) => OverlayQuick.SetLocked(overlay, true);
        lockRow.Unchecked += (_, _) => OverlayQuick.SetLocked(overlay, false);
        stack.Children.Add(lockRow);
        var hint = new TextBlock
        {
            Text = "Unlocked, drag it anywhere and pull its edges to change its size. Locked, Alt + drag still moves it.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 0, 0, 10),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(hint);

        // the buttons
        // the buttons: back to its place and turn it off side by side, all its settings in the app across the whole width under them
        var buttons = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border reset = ActionButton("\uE72C", "Reset place", () => OverlayPlaces.Reset(overlay.Title));
        reset.Margin = new Thickness(0, 0, 3, 6);
        buttons.Children.Add(reset);
        if (entry?.Hide is { } hide)
        {
            Border off = ActionButton("\uED1A", "Turn off", () =>
            {
                MeterSettings s = MeterSettings.Load();
                hide(s);
                s.Save();
                overlay.QuickHide();
                OverlayQuick.RaiseChanged(s);
                Close();
            });
            off.Margin = new Thickness(3, 0, 0, 6);
            Grid.SetColumn(off, 1);
            buttons.Children.Add(off);
        }
        else
        {
            Grid.SetColumnSpan(reset, 2);
            reset.Margin = new Thickness(0, 0, 0, 6);
        }

        stack.Children.Add(buttons);
        Border all = ActionButton("\uE713", "All settings in the app", () =>
        {
            OverlayQuick.AskPage(entry?.Page ?? "overlays");
            Close();
        });
        all.Margin = new Thickness(0);
        stack.Children.Add(all);

        var panel = new Border { Child = stack, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(10) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black };
        Content = panel;

        _save.Tick += (_, _) => Flush();
        Deactivated += (_, _) => { if (IsLoaded) Close(); };
        Closing += (_, _) => Flush();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Loaded += (_, _) => Place();
    }

    // beside the overlay's top right corner, on its screen
    private void Place()
    {
        Rect at = _overlay.PlaceBounds;
        double w = ActualWidth, h = ActualHeight;
        Rect area = WindowPlacement.WorkAreaOf(at);
        double left = at.Right + 4 + w <= area.Right ? at.Right + 4 : at.Left - w - 4;
        if (left < area.Left)
        {
            left = Math.Max(area.Left, at.Right - w);
        }

        Left = left;
        Top = Math.Max(area.Top, Math.Min(at.Top, area.Bottom - h));
    }

    private void Later()
    {
        _save.Stop();
        _save.Start();
    }

    private void Flush()
    {
        _save.Stop();
        if (_dirtyScale)
        {
            _dirtyScale = false;
            _overlay.PlaceScaleTo(_overlay.PlaceScale, save: true);
        }
    }

    private static FrameworkElement SliderRow(string label, double min, double max, double value, Func<double, string> shown, Action<double> changed)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        row.Children.Add(text);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = 5, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        var number = new TextBlock { Text = shown(slider.Value), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(number, 2);
        row.Children.Add(number);
        slider.ValueChanged += (_, e) =>
        {
            number.Text = shown(e.NewValue);
            changed(e.NewValue);
        };
        return row;
    }

    private static TextBlock Glyph(string glyph, double size, string tip)
    {
        var t = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Cursor = Cursors.Hand, ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 4, 2, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return t;
    }

    private static Border ActionButton(string glyph, string text, System.Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var g = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        g.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        row.Children.Add(g);
        var t = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        row.Children.Add(t);
        var b = new Border { Child = row, Padding = new Thickness(10, 6, 11, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        b.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        b.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHover");
        b.MouseLeave += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }
}
