using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AionDPS.Linux.Meter;
using AionDPS.Ui;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The meter's panel (the Windows app's meter over the game, Client/Ui/MainWindow.xaml's compact overlay): the own character and the fight's time on top
/// with the buttons, what is fought and the group's rate in big beside the scope switch, the boss line, the players' lines with their class colours, and a
/// status line under them for what the capture has to say. Fills itself from a <see cref="MeterEngine"/> (<see cref="Update"/>).
/// </summary>
internal sealed class MeterView : Border
{
    private readonly MeterEngine _engine;

    private readonly Image _ownIcon = new() { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ownName = Text(15, FontWeight.SemiBold, Theme.OverlayText);
    private readonly TextBlock _ownPower = Text(11, FontWeight.Normal, Theme.TextMuted);
    private readonly TextBlock _time = Text(16, FontWeight.SemiBold, Theme.OverlayText);
    private readonly TextBlock _modeText = Text(12, FontWeight.SemiBold, Theme.OverlayText);
    private readonly Button _modeButton;
    private readonly Button _pauseButton;
    private readonly ShapePath _pauseIcon = new() { Fill = Theme.TextSubtle, Width = 12, Height = 12, Stretch = Stretch.Uniform };
    private readonly Button _lockButton;
    private readonly ShapePath _lockIcon = new() { Stroke = Theme.TextSubtle, StrokeThickness = 1.4, Fill = Theme.TextSubtle, Width = 12, Height = 13, Stretch = Stretch.Uniform };

    private readonly TextBlock _target = Text(12, FontWeight.Normal, Theme.TextMuted);
    private readonly TextBlock _rate = Text(26, FontWeight.SemiBold, Theme.Accent);
    private readonly TextBlock _unit = Text(13, FontWeight.Normal, Theme.TextMuted);
    private readonly StackPanel _scopes = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };

    private readonly Grid _bossLine = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(12, 2, 12, 4), IsVisible = false };
    private readonly TextBlock _bossNumbers = Text(13, FontWeight.SemiBold, Theme.OverlayText);
    private readonly TextBlock _bossName = Text(12, FontWeight.Normal, Theme.TextMuted);
    private readonly TextBlock _bossKill = Text(12, FontWeight.SemiBold, Theme.Green);
    private readonly TextBlock _bossRage = Text(12, FontWeight.SemiBold, Theme.TextMuted);

    private readonly StackPanel _lines = new() { Margin = new Thickness(0, 4, 0, 2) };
    private readonly TextBlock _empty = Text(12, FontWeight.Normal, Theme.TextMuted);
    private readonly TextBlock _status = Text(11, FontWeight.Normal, Theme.TextMuted);
    private readonly Button _statusAction;
    private readonly List<RowView> _rowViews = new();

    private string? _statusActionText;

    /// <summary>The header was pressed where no button is: the window is dragged by it.</summary>
    public event Action<PointerPressedEventArgs>? HeaderPressed;

    public event Action? LockClicked;

    public event Action? CloseClicked;

    public event Action? StatusActionClicked;

    /// <summary>The settings asked from the menu ("Always on top", the open world ...): the window applies them.</summary>
    public event Action<string>? MenuChosen;

    public MeterView(MeterEngine engine)
    {
        _engine = engine;
        Background = Theme.OverlayBg;
        BorderBrush = Theme.Border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;

        // ---- head: the own character, the fight's time, the buttons ----
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(_ownName);
        nameStack.Children.Add(_ownPower);
        _time.Margin = new Thickness(12, 0, 0, 0);
        _time.VerticalAlignment = VerticalAlignment.Center;

        _modeButton = new Button
        {
            Content = _modeText,
            Padding = new Thickness(10, 2, 10, 3),
            Margin = new Thickness(0, 0, 6, 0),
            Background = Theme.Control,
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(_modeButton, "What the lines show: damage, healing or damage taken");
        var modeMenu = new MenuFlyout();
        foreach ((MeterEngine.MeterMode mode, string label) in new[] { (MeterEngine.MeterMode.Damage, "Damage (DPS)"), (MeterEngine.MeterMode.Heal, "Healing (HPS)"), (MeterEngine.MeterMode.Taken, "Damage taken (DTPS)") })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => SetMode(mode);
            modeMenu.Items.Add(item);
        }

        _modeButton.Flyout = modeMenu;

        Button clear = IconButton(new ShapePath { Data = Geometry.Parse("M 10.2,4.2 A 4.6,4.6 0 1 0 10.9,8 M 10.6,0.9 L 10.4,4.5 L 6.9,4.2"), Stroke = Theme.TextSubtle, StrokeThickness = 1.4, Width = 12, Height = 12, Stretch = Stretch.Uniform }, "Clear: start the meter from zero");
        clear.Click += (_, _) => _engine.Clear();
        _pauseButton = IconButton(_pauseIcon, "Pause: nothing is counted until you go on");
        _pauseButton.Click += (_, _) =>
        {
            _engine.Paused = !_engine.Paused;
            Update();
        };
        _lockButton = IconButton(_lockIcon, "Lock: the meter lets the clicks through to the game (this button stays)");
        _lockButton.Click += (_, _) => LockClicked?.Invoke();
        Button close = IconButton(new ShapePath { Data = Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"), Stroke = Theme.TextSubtle, StrokeThickness = 1.5, Width = 10, Height = 10, Stretch = Stretch.Uniform }, "Close Atreia App");
        close.Click += (_, _) => CloseClicked?.Invoke();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(_modeButton);
        buttons.Children.Add(clear);
        buttons.Children.Add(_pauseButton);
        buttons.Children.Add(_lockButton);
        buttons.Children.Add(close);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(12, 8, 8, 6), Background = Brushes.Transparent };
        Place(head, _ownIcon, 0);
        Place(head, nameStack, 1);
        Place(head, _time, 2);
        Place(head, buttons, 4);
        head.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(head).Properties.IsLeftButtonPressed)
            {
                HeaderPressed?.Invoke(e);
            }
        };

        // ---- what is fought, the rate in big, the scope switch ----
        var rateLine = new StackPanel { Orientation = Orientation.Horizontal };
        _rate.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.Margin = new Thickness(6, 0, 0, 5);
        rateLine.Children.Add(_rate);
        rateLine.Children.Add(_unit);
        var fought = new StackPanel();
        _target.TextTrimming = TextTrimming.CharacterEllipsis;
        fought.Children.Add(_target);
        fought.Children.Add(rateLine);
        foreach (MeterEngine.MeterScope scope in new[] { MeterEngine.MeterScope.Me, MeterEngine.MeterScope.Group, MeterEngine.MeterScope.Corps, MeterEngine.MeterScope.All })
        {
            var label = Text(12, FontWeight.Normal, Theme.TextMuted);
            label.Text = scope.ToString();
            var pill = new Border { Child = label, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(5), Tag = scope, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            pill.PointerPressed += (_, e) =>
            {
                _engine.SetScope(scope);
                e.Handled = true;
            };
            _scopes.Children.Add(pill);
        }

        var scopeBox = new Border { Child = _scopes, Background = Theme.Control, CornerRadius = new CornerRadius(7), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Bottom };
        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 2, 12, 2) };
        Place(middle, fought, 0);
        Place(middle, scopeBox, 1);

        // ---- the boss line ----
        _bossName.Margin = new Thickness(8, 0, 0, 0);
        _bossName.TextTrimming = TextTrimming.CharacterEllipsis;
        _bossRage.Margin = new Thickness(10, 0, 0, 0);
        Place(_bossLine, _bossNumbers, 0);
        Place(_bossLine, _bossName, 1);
        Place(_bossLine, _bossKill, 2);
        Place(_bossLine, _bossRage, 3);

        // ---- the status line ----
        _status.TextWrapping = TextWrapping.Wrap;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _statusAction = new Button
        {
            Padding = new Thickness(8, 2, 8, 3),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Theme.Control,
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            FontSize = 11,
        };
        _statusAction.Click += (_, _) => StatusActionClicked?.Invoke();
        var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 2, 12, 8) };
        Place(statusRow, _status, 0);
        Place(statusRow, _statusAction, 1);

        _empty.Margin = new Thickness(12, 10, 12, 10);
        _empty.TextWrapping = TextWrapping.Wrap;

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        var rule = new Border { Height = 1, Background = Theme.Separator, Margin = new Thickness(10, 0, 10, 4) };
        DockPanel.SetDock(rule, Dock.Top);
        DockPanel.SetDock(middle, Dock.Top);
        DockPanel.SetDock(_bossLine, Dock.Top);
        DockPanel.SetDock(statusRow, Dock.Bottom);
        dock.Children.Add(head);
        dock.Children.Add(rule);
        dock.Children.Add(middle);
        dock.Children.Add(_bossLine);
        dock.Children.Add(statusRow);
        var body = new StackPanel();
        body.Children.Add(_lines);
        body.Children.Add(_empty);
        dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden });
        Child = dock;

        ContextMenu = BuildMenu();
        Update();
    }

    /// <summary>The lock button's place in the window (device-independent units): what keeps taking clicks while the meter lets the rest through.</summary>
    public Control LockButton => _lockButton;

    /// <summary>Shows the lock as closed or open.</summary>
    public void ShowLocked(bool locked)
    {
        // a padlock, its shackle closed or swung open
        _lockIcon.Data = Geometry.Parse(locked
            ? "M 2,6 L 10,6 L 10,12 L 2,12 Z M 3.6,6 L 3.6,4 A 2.4,2.4 0 0 1 8.4,4 L 8.4,6"
            : "M 2,6 L 10,6 L 10,12 L 2,12 Z M 3.6,6 L 3.6,4 A 2.4,2.4 0 0 1 8.4,4 L 8.4,4.6");
        _lockIcon.Fill = locked ? Theme.Accent : Theme.TextSubtle;
        _lockIcon.Stroke = locked ? Theme.Accent : Theme.TextSubtle;
        ToolTip.SetTip(_lockButton, locked ? "Unlock: the meter takes the mouse again" : "Lock: the meter lets the clicks through to the game (this button stays)");
    }

    /// <summary>A line for the status row, with a button beside it (null: none).</summary>
    public void ShowStatus(string text, bool warning, string? action = null)
    {
        _status.Text = text;
        _status.Foreground = warning ? Theme.Warning : Theme.TextMuted;
        _status.IsVisible = text.Length > 0;
        _statusActionText = action;
        _statusAction.Content = action;
        _statusAction.IsVisible = action is not null;
    }

    /// <summary>Fills the panel from the engine (once a second, and after each click).</summary>
    public void Update()
    {
        var loc = LocalizationManager.Instance;
        (string? name, string power) = _engine.Own;
        _ownName.Text = name ?? "Atreia App";
        _ownPower.Text = power;
        _ownPower.IsVisible = power.Length > 0;
        string? ownClass = _engine.AllRows.FirstOrDefault(r => _engine.Source?.Entities.IsLocalPlayer(r.ObjectId) == true)?.ClassName;
        Bitmap? ownIcon = ClassLook.Icon(ownClass) ?? Pictures.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "app", "a2h-emblem.png"));
        _ownIcon.Source = ownIcon;
        _ownIcon.IsVisible = ownIcon is not null;
        _time.Text = _engine.TimeText;
        _modeText.Text = _engine.Unit;

        _pauseIcon.Data = Geometry.Parse(_engine.Paused ? "M 2,1 L 11,6.5 L 2,12 Z" : "M 2,1 L 5,1 L 5,12 L 2,12 Z M 7,1 L 10,1 L 10,12 L 7,12 Z");
        _pauseIcon.Fill = _engine.Paused ? Theme.Warning : Theme.TextSubtle;
        ToolTip.SetTip(_pauseButton, _engine.Paused ? "Go on counting" : "Pause: nothing is counted until you go on");

        _target.Text = _engine.TargetText;
        _rate.Text = PlayerRow.Compact((long)Math.Round(_engine.TotalRate));
        _unit.Text = _engine.Unit;
        foreach (Border pill in _scopes.Children.OfType<Border>())
        {
            bool on = pill.Tag is MeterEngine.MeterScope s && s == _engine.ShownScope;
            pill.Background = on ? Theme.ScopeOn : Brushes.Transparent;
            if (pill.Child is TextBlock label)
            {
                label.Foreground = on ? Theme.Accent : Theme.TextMuted;
            }
        }

        UpdateBossLine(loc);
        UpdateLines();
    }

    private void UpdateBossLine(LocalizationManager loc)
    {
        if (_engine.CurrentBoss() is not { } boss)
        {
            _bossLine.IsVisible = false;
            return;
        }

        // the head names the boss already: the line says how much of it is left instead of naming it twice (Windows: ShowBossBar)
        bool named = boss.Name.Length > 0 && _engine.TargetText.Contains(boss.Name, StringComparison.OrdinalIgnoreCase);
        string share = boss.Max > 0 ? Percent(Math.Clamp((double)boss.Hp / boss.Max, 0, 1)) : "";
        _bossNumbers.Text = boss.Max > 0 ? $"{PlayerRow.Compact(boss.Hp)} / {PlayerRow.Compact(boss.Max)}" + (named ? "" : " · " + share) : PlayerRow.Compact(boss.Hp);
        _bossName.Text = named && boss.Max > 0 ? share + " left" : boss.Name.Length > 0 ? boss.Name : loc["Overlay.BossBar.Boss"];
        bool rageFirst = boss.Kill is TimeSpan k && boss.Rage is TimeSpan r && r > TimeSpan.Zero && k > r;
        _bossKill.Text = boss.Kill is TimeSpan dies ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Kill"], Clock(dies)) : loc["Overlay.BossLine.NoKill"];
        _bossKill.Foreground = boss.Kill is null ? Theme.TextMuted : rageFirst ? Theme.Danger : Theme.Green;
        if (boss.Rage is TimeSpan left)
        {
            _bossRage.Text = left > TimeSpan.Zero ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Enrage"], Clock(left)) : loc["Overlay.Enraged"];
            _bossRage.Foreground = left <= TimeSpan.FromSeconds(20) ? Theme.Danger : left <= TimeSpan.FromSeconds(60) ? Theme.Amber : Theme.TextMuted;
            _bossRage.IsVisible = true;
        }
        else
        {
            _bossRage.IsVisible = false;
        }

        _bossLine.IsVisible = true;
    }

    private void UpdateLines()
    {
        IReadOnlyList<PlayerRow> rows = _engine.ShownRows;
        while (_rowViews.Count < rows.Count)
        {
            var view = new RowView();
            _rowViews.Add(view);
            _lines.Children.Add(view);
        }

        for (int i = 0; i < _rowViews.Count; i++)
        {
            bool shown = i < rows.Count;
            _rowViews[i].IsVisible = shown;
            if (shown)
            {
                _rowViews[i].Show(rows[i], _engine.Mode, _engine.Source?.Entities.IsLocalPlayer(rows[i].ObjectId) == true);
            }
        }

        _empty.IsVisible = rows.Count == 0;
        _empty.Text = _engine.Paused ? "Paused - nothing is counted until you go on."
            : _engine.Capturing ? "No fight yet: the meter counts dungeons, arenas, boss fights and training dummies (the open world too when it is switched on)."
            : "Waiting for the game.";
    }

    private void SetMode(MeterEngine.MeterMode mode)
    {
        _engine.Mode = mode;
        MeterSettings settings = MeterSettings.Load();
        settings.LastMode = mode.ToString();
        settings.Save();
        Update();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }

        Add("Damage (DPS)", () => SetMode(MeterEngine.MeterMode.Damage));
        Add("Healing (HPS)", () => SetMode(MeterEngine.MeterMode.Heal));
        Add("Damage taken (DTPS)", () => SetMode(MeterEngine.MeterMode.Taken));
        menu.Items.Add(new Separator());
        Add("Count the open world too (on/off)", () => MenuChosen?.Invoke("openworld"));
        Add("Always on top (on/off)", () => MenuChosen?.Invoke("topmost"));
        Add("Clear", () => _engine.Clear());
        menu.Items.Add(new Separator());
        Add("Capture check (what the app can see)", () => MenuChosen?.Invoke("capturecheck"));
        Add("Open the data folder", () => MenuChosen?.Invoke("datafolder"));
        Add("Quit", () => CloseClicked?.Invoke());
        return menu;
    }

    private static Button IconButton(Control icon, string tip)
    {
        var button = new Button
        {
            Content = icon,
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    internal static TextBlock Text(double size, FontWeight weight, IBrush brush) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void Place(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static string Percent(double fraction) => (fraction * 100).ToString(fraction < 0.1 ? "0.#" : "0", CultureInfo.CurrentCulture) + "%";

    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

/// <summary>One player's line: the class emblem, the name in the class colour with the combat power after it, the rate, the share and the total, over the
/// bar of the class colour (the Windows meter's line, Client/Ui/MeterRowView.cs).</summary>
internal sealed class RowView : Grid
{
    private readonly FillBar _bar = new();
    private readonly Image _icon = new() { Width = 18, Height = 18, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = MeterView.Text(13, FontWeight.SemiBold, Theme.OverlayText);
    private readonly TextBlock _power = MeterView.Text(11, FontWeight.Normal, Theme.TextMuted);
    private readonly TextBlock _rate = MeterView.Text(14, FontWeight.Bold, Theme.OverlayText);
    private readonly TextBlock _share = MeterView.Text(12, FontWeight.Normal, Theme.TextMuted);
    private readonly TextBlock _total = MeterView.Text(12, FontWeight.Normal, Theme.TextSubtle);

    public RowView()
    {
        Height = 28;
        Margin = new Thickness(0, 1, 0, 1);
        Children.Add(_bar);
        var text = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _power.Margin = new Thickness(7, 1, 0, 0);
        name.Children.Add(_name);
        name.Children.Add(_power);
        _rate.TextAlignment = TextAlignment.Right;
        _rate.Width = 62;
        _share.TextAlignment = TextAlignment.Right;
        _share.Width = 52;
        _total.TextAlignment = TextAlignment.Right;
        _total.Width = 64;
        _total.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(_rate, 2);
        Grid.SetColumn(_share, 3);
        Grid.SetColumn(_total, 4);
        text.Children.Add(_icon);
        text.Children.Add(name);
        text.Children.Add(_rate);
        text.Children.Add(_share);
        text.Children.Add(_total);
        Children.Add(text);
    }

    public void Show(PlayerRow row, MeterEngine.MeterMode mode, bool own)
    {
        _bar.Set(row.FillPercent, row.AbsorbedFillPercent, ClassLook.Glow(row.ClassName), ClassLook.Line(row.ClassName), ClassLook.Edge(row.ClassName));
        Bitmap? icon = ClassLook.Icon(row.ClassName);
        _icon.Source = icon;
        _icon.Opacity = icon is null ? 0 : 1;
        _name.Text = row.NameDisplay;
        _name.Foreground = ClassLook.Text(row.ClassName);
        _power.Text = row.CombatPowerText;
        _power.IsVisible = row.CombatPowerText.Length > 0;
        _rate.Text = row.DpsCompact;
        _rate.Foreground = mode == MeterEngine.MeterMode.Heal ? Theme.Heal : Theme.OverlayText;
        _share.Text = row.ShareDisplay;
        _total.Text = row.DamageCompact;
    }
}
