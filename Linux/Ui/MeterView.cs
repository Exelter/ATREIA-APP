using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Linux.Meter;
using AionDPS.Linux.Platform;
using AionDPS.Ui;
using AionDPS.Upload;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The meter's panel, laid out as the Windows app's meter over the game (Client/Ui/MainWindow.xaml's compact overlay, filled by MainWindow.MeterBar.cs,
/// UpdateCompactOverlay and the views' own code). The bar, always there: the own class, name and Combat Power, the fight's clock, the pill of what is
/// measured, the reset, the lock and the menu. Under it the body, which opens in a boss fight, at a training dummy, in PvP and in the Grind, Stagger and buff
/// views, folded away between fights (<see cref="MeterEngine.Open"/>): what is fought and the group's rate in big beside the scope switch, the boss's next
/// casts, the dummy's card, the boss line, the Stagger or the Grind view, the players' lines with the bar's parts and their legend, or the buffs on you. Then
/// the Linux app's own line for what keeps the meter from working, and the foot: whether the game is read, the own counters, the ping and the corner grip.
/// </summary>
internal sealed class MeterView : Border
{
    private readonly MeterEngine _engine;

    // ---- the bar ----
    private readonly Panel _logo = new() { Width = 14, Height = 14 };
    private readonly Image _ownIcon = new() { Width = 24, Height = 24 };
    private readonly TextBlock _ownName = Text(12.5, FontWeight.SemiBold, Palette.OverlayText);
    private readonly TextBlock _ownPower = Text(10, FontWeight.Normal, Palette.TextMuted);
    private readonly Rectangle _divider = new() { Width = 1, Height = 18, Fill = new SolidColorBrush(Color.Parse("#22FFFFFF")), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _time = Text(13, FontWeight.SemiBold, Palette.OverlayText);
    private readonly TextBlock _modeText = Text(11, FontWeight.SemiBold, Palette.ScopeOnText);
    private readonly Button _modeButton;
    private readonly Button _lockButton;
    private readonly ShapePath _lockIcon = new() { Stroke = Palette.TextSubtle, StrokeThickness = 1.4, Fill = Palette.TextSubtle, Width = 11, Height = 12, Stretch = Stretch.Uniform };
    private readonly Button _menuButton;
    private readonly Button _resetButton;
    private readonly Grid _head;

    // ---- the body ----
    private readonly StackPanel _body = new();
    private readonly TextBlock _target = Text(11.5, FontWeight.SemiBold, Palette.TextMuted);
    private readonly Grid _totalLine = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 0, 10, 5) };
    private readonly TextBlock _rate = Text(24, FontWeight.SemiBold, Palette.Accent);
    private readonly TextBlock _unit = Text(12.5, FontWeight.SemiBold, Palette.TextMuted);
    private readonly Border _scopeBox;
    private readonly StackPanel _scopes = new() { Orientation = Orientation.Horizontal };
    private readonly Border _callout = new()
    {
        Margin = new Thickness(10, 0, 10, 5), Padding = new Thickness(8, 3, 8, 4), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.Parse("#12DCB877")), BorderBrush = new SolidColorBrush(Color.Parse("#30DCB877")), IsVisible = false,
    };
    private readonly TextBlock _calloutText = Text(11.5, FontWeight.Normal, Palette.OverlayText);
    private readonly Border _dummyCard = new()
    {
        Margin = new Thickness(10, 0, 10, 6), Padding = new Thickness(11, 8, 11, 9), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.Parse("#0CFFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#1CFFFFFF")), IsVisible = false,
    };
    private readonly TextBlock _dummyRun = Text(11, FontWeight.SemiBold, Palette.TextMuted);
    private readonly TextBlock _dummyClock = Text(12, FontWeight.Normal, Palette.TextMuted);
    private readonly TextBlock _dummyDps = Text(30, FontWeight.SemiBold, Palette.OverlayText);
    private readonly Grid _dummyBar = new() { Height = 3, Margin = new Thickness(0, 3, 0, 7), ColumnDefinitions = new ColumnDefinitions("*,*"), IsVisible = false };
    private readonly Grid _dummyStats = new() { ColumnDefinitions = new ColumnDefinitions("*,*") };
    private readonly Grid _bossLine = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(10, 0, 10, 6), IsVisible = false };
    private readonly TextBlock _bossNumbers = Text(12, FontWeight.SemiBold, Palette.OverlayText);
    private readonly TextBlock _bossName = Text(11, FontWeight.Normal, Palette.OverlayMuted);
    private readonly TextBlock _bossKill = Text(12, FontWeight.SemiBold, Palette.KillGreen);
    private readonly TextBlock _bossRage = Text(12, FontWeight.SemiBold, Palette.OverlayMuted);
    private readonly Border _staggerCard = new()
    {
        Margin = new Thickness(10, 0, 10, 6), Padding = new Thickness(9, 6, 9, 7), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.Parse("#0E6F9FD8")), BorderBrush = new SolidColorBrush(Color.Parse("#336F9FD8")), IsVisible = false,
    };
    private readonly Border _grindCard = new() { Margin = new Thickness(10, 0, 10, 6), IsVisible = false };
    private readonly Panel _rowsArea = new() { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _lines = new() { Margin = new Thickness(0, 0, 0, 2) };
    private readonly LayoutTransformControl _linesFit = new();
    private readonly WrapPanel _legend = new() { Margin = new Thickness(10, 1, 10, 4), IsVisible = false, IsHitTestVisible = false };
    private readonly StackPanel _buffRows = new() { Margin = new Thickness(10, 0, 10, 4), IsVisible = false };
    private readonly List<RowView> _rowViews = new();
    private readonly List<BuffRowView> _buffViews = new();

    // ---- the Linux app's own line, and the foot ----
    private readonly Grid _statusRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 0, 10, 4), IsVisible = false };
    private readonly TextBlock _status = Text(11, FontWeight.Normal, Palette.TextMuted);
    private readonly Button _statusAction;
    private readonly Rectangle _stateDot = new() { Width = 6, Height = 6, RadiusX = 1, RadiusY = 1, Fill = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
    private readonly TextBlock _stateText = Text(10.5, FontWeight.Normal, Palette.TextMuted);
    private readonly WrapPanel _tickets = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ping = Text(10.5, FontWeight.Normal, Palette.TextMuted);

    private string _ticketsKey = "";
    private string _staggerKey = "";
    private string _grindKey = "";
    private string _legendKey = "";
    private bool _statusWarns;
    private double _rowsCap;

    /// <summary>The header was pressed where no button is: the window is dragged by it.</summary>
    public event Action<PointerPressedEventArgs>? HeaderPressed;

    public event Action? LockClicked;

    public event Action? StatusActionClicked;

    /// <summary>A window-wide item of the menu ("topmost", "openworld", "pause", "capturecheck", "datafolder", "quit"): the window does it.</summary>
    public event Action<string>? MenuChosen;

    /// <summary>The corner grip at the foot: dragging it makes the meter wider or longer (with Ctrl: bigger, text and all), as on Windows.</summary>
    public Control Grip { get; }

    public MeterView(MeterEngine engine)
    {
        _engine = engine;
        Background = Palette.GlassBg;
        BorderBrush = Palette.GlassEdge;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;

        // ---- the bar: you, the clock, what is measured and the buttons; its free part moves the meter ----
        _logo.Children.Add(new ShapePath { Data = Geometry.Parse("M7,0.6 L13.4,7 L7,13.4 L0.6,7 Z"), Stroke = Palette.Accent, StrokeThickness = 1.2 });
        _logo.Children.Add(new ShapePath { Data = Geometry.Parse("M7,4 L10,7 L7,10 L4,7 Z"), Fill = Palette.Accent });
        ToolTip.SetTip(_logo, Brand.AppName);
        var emblem = new Panel { Width = 24, Height = 24, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        _logo.HorizontalAlignment = HorizontalAlignment.Center;
        _logo.VerticalAlignment = VerticalAlignment.Center;
        emblem.Children.Add(_logo);
        emblem.Children.Add(_ownIcon);
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _ownName.MaxWidth = 100;
        _ownName.TextTrimming = TextTrimming.CharacterEllipsis;
        _ownPower.Margin = new Thickness(0, -1, 0, 0);
        nameStack.Children.Add(_ownName);
        nameStack.Children.Add(_ownPower);
        var you = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        you.Children.Add(emblem);
        you.Children.Add(nameStack);
        _time.Margin = new Thickness(0, 0, 10, 0);

        var pill = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pill.Children.Add(_modeText);
        pill.Children.Add(new ShapePath
        {
            Data = Geometry.Parse("M0,0 L3.5,3.5 L7,0"), Stroke = new SolidColorBrush(Color.Parse("#B3E6C88F")), StrokeThickness = 1.3,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        _modeButton = new Button
        {
            Content = pill, Height = 22, MinHeight = 0, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(9, 0, 7, 0), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#40DCB877")), Background = new SolidColorBrush(Color.Parse("#14DCB877")),
            VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(_modeButton, "What the meter shows: DPS, rDPS, healing, damage taken, buffs, a dummy parse, Grind");
        var modeMenu = NewMenu();
        modeMenu.Opening += (_, _) => FillModeMenu(modeMenu);
        modeMenu.Opened += (_, _) => NoteItems(modeMenu);
        modeMenu.Closed += (_, _) => ItemsGone(modeMenu);
        _modeButton.Flyout = modeMenu;

        Button reset = _resetButton = IconButton(new ShapePath { Data = Geometry.Parse("M 10.2,4.2 A 4.6,4.6 0 1 0 10.9,8 M 10.6,0.9 L 10.4,4.5 L 6.9,4.2"), Stroke = Palette.TextSubtle, StrokeThickness = 1.4, Width = 11, Height = 11, Stretch = Stretch.Uniform },
            LocalizationManager.Instance["Main.Overlay.Reset"]);
        reset.Click += (_, _) =>
        {
            _engine.Clear();
            Did("clear");
        };
        _lockButton = IconButton(_lockIcon, "Lock the meter in place");
        _lockButton.Click += (_, _) => LockClicked?.Invoke();
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2.5, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            dots.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = Palette.TextSubtle });
        }

        _menuButton = IconButton(dots, "More: pause, the open world, always on top, the capture check, the data folder, quit");
        var menu = NewMenu();
        menu.Opening += (_, _) => FillMenu(menu);
        menu.Opened += (_, _) => NoteItems(menu);
        menu.Closed += (_, _) => ItemsGone(menu);
        _menuButton.Flyout = menu;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(_modeButton);
        buttons.Children.Add(reset);
        buttons.Children.Add(_lockButton);
        buttons.Children.Add(_menuButton);

        var head = _head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(8, 5, 6, 5), MinHeight = 30, Background = Brushes.Transparent };
        Place(head, you, 0);
        Place(head, _divider, 1);
        Place(head, _time, 2);
        Place(head, buttons, 4);
        head.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(head).Properties.IsLeftButtonPressed)
            {
                HeaderPressed?.Invoke(e);
            }
        };

        // ---- the body: what is fought, the rate in big, the scope switch ----
        _body.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(Color.Parse("#16FFFFFF")), Margin = new Thickness(0, 0, 0, 5) });
        _target.Margin = new Thickness(12, 0, 10, 0);
        _target.TextTrimming = TextTrimming.CharacterEllipsis;
        _body.Children.Add(_target);
        var rateLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        _rate.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.Margin = new Thickness(6, 0, 0, 4);
        rateLine.Children.Add(_rate);
        rateLine.Children.Add(_unit);
        foreach ((string tag, string label, string? tip) in new[]
        {
            ("Me", "Me", "Only you (picked by itself at a training dummy)"),
            ("Group", LocalizationManager.Instance["Main.Scope.Group"], (string?)null),
            ("Stagger", "Stagger", "The boss's stagger checks: who took how much off its stagger bar, with which skills (hover them)"),
            ("Grind", "Grind", "Grind: the Kinah, Abyss Points, kills and loot you farm, and what that makes an hour"),
            ("Corps", LocalizationManager.Instance["Main.Scope.Corps"], null),
            ("All", LocalizationManager.Instance["Main.Scope.All"], "Everybody (picked by itself in dungeons and raids)"),
        })
        {
            var word = Text(11, FontWeight.SemiBold, Palette.TextMuted);
            word.Text = label;
            var segment = new Border { Child = word, Padding = new Thickness(7, 0, 7, 0), CornerRadius = new CornerRadius(4), Tag = tag, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            if (tip is not null)
            {
                ToolTip.SetTip(segment, tip);
            }

            segment.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _engine.ScopeClicked(tag);
                Did("scope switch " + tag);
            };
            _scopes.Children.Add(segment);
        }

        _scopeBox = new Border
        {
            Child = _scopes, Background = new SolidColorBrush(Color.Parse("#0AFFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#1FFFFFFF")), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center,
        };
        Place(_totalLine, rateLine, 0);
        Place(_totalLine, _scopeBox, 1);
        _body.Children.Add(_totalLine);

        // the boss's next casts
        _calloutText.TextTrimming = TextTrimming.CharacterEllipsis;
        _callout.Child = _calloutText;
        _body.Children.Add(_callout);

        // the dummy's card: the run, the own DPS in big, its clock running out and every kind of hit
        var dummy = new StackPanel();
        _dummyRun.TextTrimming = TextTrimming.CharacterEllipsis;
        dummy.Children.Add(_dummyRun);
        var dpsLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3, 0, 0) };
        var dpsFigure = new StackPanel { Orientation = Orientation.Horizontal };
        _dummyDps.VerticalAlignment = VerticalAlignment.Bottom;
        dpsFigure.Children.Add(_dummyDps);
        TextBlock dpsWord = Text(12, FontWeight.SemiBold, Palette.TextMuted);
        dpsWord.Text = "DPS";
        dpsWord.Margin = new Thickness(6, 0, 0, 6);
        dpsWord.VerticalAlignment = VerticalAlignment.Bottom;
        dpsFigure.Children.Add(dpsWord);
        Place(dpsLine, dpsFigure, 0);
        _dummyClock.VerticalAlignment = VerticalAlignment.Bottom;
        _dummyClock.Margin = new Thickness(0, 0, 0, 5);
        Place(dpsLine, _dummyClock, 1);
        dummy.Children.Add(dpsLine);
        _dummyBar.Children.Add(new Border { Background = Palette.Accent, CornerRadius = new CornerRadius(2) });
        var dummyTrack = new Border { Background = new SolidColorBrush(Color.Parse("#14FFFFFF")), CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(dummyTrack, 1);
        _dummyBar.Children.Add(dummyTrack);
        dummy.Children.Add(_dummyBar);
        dummy.Children.Add(_dummyStats);
        _dummyCard.Child = dummy;
        _body.Children.Add(_dummyCard);

        // the boss line: what is left of the boss and its whole, its name, when it dies and its enrage
        _bossName.Margin = new Thickness(8, 0, 0, 0);
        _bossName.TextTrimming = TextTrimming.CharacterEllipsis;
        _bossKill.Margin = new Thickness(10, 0, 0, 0);
        _bossRage.Margin = new Thickness(10, 0, 0, 0);
        ToolTip.SetTip(_bossKill, LocalizationManager.Instance["Overlay.BossBar.DiesTip"]);
        Place(_bossLine, _bossNumbers, 0);
        Place(_bossLine, _bossName, 1);
        Place(_bossLine, _bossKill, 2);
        Place(_bossLine, _bossRage, 3);
        _body.Children.Add(_bossLine);

        _body.Children.Add(_staggerCard);
        _body.Children.Add(_grindCard);

        // the lines (their rate column one width for all), the legend of the bar's colours, the buffs on you
        Grid.SetIsSharedSizeScope(_lines, true);
        _linesFit.Child = _lines;
        _rowsArea.Children.Add(_linesFit);
        _body.Children.Add(_rowsArea);
        _body.Children.Add(_legend);
        _body.Children.Add(_buffRows);

        // ---- the Linux app's own line: what keeps the meter from working, the notes, an update ----
        _status.TextWrapping = TextWrapping.Wrap;
        _statusAction = new Button
        {
            Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(8, 0, 0, 0), Background = Palette.Control, BorderBrush = Palette.Border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, FontSize = 11,
        };
        _statusAction.Click += (_, _) => StatusActionClicked?.Invoke();
        Place(_statusRow, _status, 0);
        Place(_statusRow, _statusAction, 1);

        // ---- the foot: whether the game is read, the own counters, the ping, the grip ----
        var foot = new DockPanel { Margin = new Thickness(10, 1, 4, 4), LastChildFill = true };
        var grip = new Panel { Width = 12, Height = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner) };
        grip.Children.Add(new ShapePath { Data = Geometry.Parse("M11,1 L1,11 M11,5 L5,11 M11,9 L9,11"), Stroke = Palette.TextMuted, StrokeThickness = 1 });
        ToolTip.SetTip(grip, LocalizationManager.Instance["Main.Overlay.ResizeTip"]);
        Grip = grip;
        DockPanel.SetDock(grip, Dock.Right);
        foot.Children.Add(grip);
        _ping.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_ping, Dock.Right);
        foot.Children.Add(_ping);
        DockPanel.SetDock(_stateDot, Dock.Left);
        foot.Children.Add(_stateDot);
        _stateText.Margin = new Thickness(0, 0, 8, 0);
        _stateText.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(_stateText, Dock.Left);
        foot.Children.Add(_stateText);
        foot.Children.Add(_tickets);

        var root = new StackPanel();
        root.Children.Add(head);
        root.Children.Add(_body);
        root.Children.Add(_statusRow);
        root.Children.Add(foot);
        Child = root;

        ShowLocked(false);
        Update();
    }

    /// <summary>The lock button's place in the window: what keeps taking clicks while the meter lets the rest through.</summary>
    public Control LockButton => _lockButton;

    /// <summary>The status line's button ("Copy the command", "Get it"): it takes clicks while it is there, locked or not.</summary>
    public Control StatusButton => _statusAction;

    /// <summary>What keeps taking clicks while the clicks go through the meter: the bar's buttons and the status line's.</summary>
    public IEnumerable<Control> ClickableParts => new Control[] { _modeButton, _resetButton, _lockButton, _menuButton, _statusAction };

    /// <summary>The height the lines take now (the grip's start).</summary>
    public double RowsHeight => _rowsArea.Bounds.Height;

    /// <summary>The most the lines may take, in device-independent units (the grip's height; 0: as many as there are, up to ten).</summary>
    public double RowsCap
    {
        get => _rowsCap;
        set
        {
            _rowsCap = value;
            FitRowsArea();
        }
    }

    /// <summary>Shows the lock as closed or open (Windows: ApplyOverlayLock): locked, the head no longer moves the meter and the grip is gone.</summary>
    public void ShowLocked(bool locked)
    {
        _lockIcon.Data = Geometry.Parse(locked
            ? "M 2,6 L 10,6 L 10,12 L 2,12 Z M 3.6,6 L 3.6,4 A 2.4,2.4 0 0 1 8.4,4 L 8.4,6"
            : "M 2,6 L 10,6 L 10,12 L 2,12 Z M 3.6,6 L 3.6,4 A 2.4,2.4 0 0 1 8.4,4 L 8.4,4.6");
        _lockIcon.Fill = locked ? Palette.Accent : Palette.TextSubtle;
        _lockIcon.Stroke = locked ? Palette.Accent : Palette.TextSubtle;
        ToolTip.SetTip(_lockButton, locked ? "Locked in place - click to move or size the meter again" : "Lock the meter in place (the clicks then go through it to the game)");
        Grip.IsVisible = !locked;
    }

    /// <summary>The Linux app's own line (what keeps the meter from working, a note, an update), with a button beside it (null: none).</summary>
    public void ShowStatus(string text, bool warning, string? action = null)
    {
        _status.Text = text;
        _status.Foreground = warning ? Palette.Warning : Palette.TextMuted;
        _statusAction.Content = action;
        _statusAction.IsVisible = action is not null;
        _statusRow.IsVisible = text.Length > 0 || action is not null;
        _statusWarns = warning;
    }

    /// <summary>Fills the panel from the engine (once a second, and after each click).</summary>
    public void Update()
    {
        DateTime now = DateTime.Now;
        var loc = LocalizationManager.Instance;
        bool open = _engine.Open(now);

        // the bar: you, the clock, what is measured
        (string? name, string power) = _engine.Own;
        string? ownClass = _engine.OwnClass;
        Bitmap? classIcon = ClassLook.Icon(ownClass);
        _ownIcon.Source = classIcon;
        _ownIcon.IsVisible = classIcon is not null;
        _logo.IsVisible = classIcon is null;
        _ownName.Text = name is { Length: > 0 } ? name : Brand.AppName;
        _ownName.Foreground = ownClass is { Length: > 0 } ? ClassLook.Text(ownClass) : Palette.OverlayText;
        _ownPower.Text = power;
        _ownPower.IsVisible = power.Length > 0;
        (string target, string time, bool accent) = _engine.Header(now);
        _time.Text = time;
        _time.Foreground = accent ? Palette.Accent : Palette.OverlayText;
        bool timed = open && time.Length > 0;
        _time.IsVisible = timed;
        _divider.IsVisible = timed;
        _modeText.Text = _engine.ModeShort;

        // the body
        _body.IsVisible = open;
        bool card = _engine.DummyCardShown;
        _target.Text = target;
        _target.IsVisible = !card && target.Length > 0;
        _totalLine.IsVisible = !card && _engine.Settings.MeterShowTotal;
        (string figure, string unit) = _engine.Total(now);
        _rate.Text = figure;
        _unit.Text = unit;
        _rate.IsVisible = !_engine.ShowGrind; // the Grind's tiles carry the numbers
        _unit.IsVisible = !_engine.ShowGrind;
        _scopeBox.IsVisible = !_engine.ShowBuffs;
        foreach (Border segment in _scopes.Children.OfType<Border>())
        {
            string tag = segment.Tag as string ?? "";
            bool selected = _engine.ShowStagger || _engine.ShowGrind ? tag == (_engine.ShowGrind ? "Grind" : "Stagger") : tag == _engine.ShownScope.ToString();
            segment.Background = selected ? Palette.ScopeOnBg : Brushes.Transparent;
            if (segment.Child is TextBlock word)
            {
                word.Foreground = selected ? Palette.ScopeOnText : Palette.TextMuted;
            }
        }

        UpdateCallout(loc);
        UpdateDummyCard(now);
        UpdateBossLine(loc);
        UpdateStagger(now);
        UpdateGrind(now);
        _rowsArea.IsVisible = !card && !_engine.ShowStagger && !_engine.ShowGrind;
        _lines.IsVisible = !_engine.ShowBuffs;
        UpdateLines();
        UpdateLegend();
        UpdateBuffRows();

        // the foot
        bool capturing = _engine.Capturing;
        _stateDot.Fill = _engine.Paused ? Brushes.Orange : capturing ? Palette.Live : Brushes.Gray;
        _stateText.Text = _engine.Paused ? "Paused" : capturing ? "" : "Waiting for the game";
        _stateText.IsVisible = _stateText.Text.Length > 0;
        double? ping = capturing && _engine.Source is Aion2.Aion2PacketCombatSource ? Aion2.Capture.NetworkStats.Snapshot().PingMs : null;
        _ping.Text = ping is double ms and >= 1 ? $"{ms:0} ms" : "";
        UpdateTickets(now);
        NoteWhere();
    }

    /// <summary>What the meter shows now, in a few words (linux.log's "action" lines).</summary>
    public string State =>
        $"{_engine.ModeShort} · {(_engine.ShowStagger || _engine.ShowGrind || _engine.ShowBuffs ? "view" : "scope " + _engine.ShownScope)} · {_engine.ShownRows.Count} lines"
        + $" · {(_body.IsVisible ? "open" : "folded")}{(_engine.Paused ? " · paused" : "")}{(_engine.TrackOpenWorld ? " · open world" : "")}";

    /// <summary>Notes a click and what the meter shows after it.</summary>
    private void Did(string what) => LinuxLog.Write($"action: {what} -> {State}");

    /// <summary>
    /// A menu filled anew each time it opens (its Opening). Its item list is made here, before its first opening: Avalonia (11.3) makes the menu's
    /// presenter on the first opening before Opening fills the items, and a presenter made while the menu had no list of its own yet keeps an empty one -
    /// the menu opened empty (the workflow's button test found it).
    /// </summary>
    private static MenuFlyout NewMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Clear();
        return menu;
    }

    /// <summary>For the workflow's test (ATREIA_UI_TEST=1): where the bar's buttons, the scope switch, the grip and a free spot of the head are.</summary>
    private void NoteWhere()
    {
        if (!UiTest.Enabled)
        {
            return;
        }

        UiTest.Where("pill", _modeButton);
        UiTest.Where("reset", _resetButton);
        UiTest.Where("lock", _lockButton);
        UiTest.Where("menu", _menuButton);
        UiTest.Where("grip", Grip);
        UiTest.WherePoint("head", _head, new Point(_head.Bounds.Width * 0.55, _head.Bounds.Height / 2));
        foreach (Border segment in _scopes.Children.OfType<Border>())
        {
            UiTest.Where("scope:" + segment.Tag, segment);
        }
    }

    /// <summary>For the workflow's test: where the items of the menu just opened are, for as long as it is open (its popup is laid out and placed a moment
    /// after it opens).</summary>
    private static void NoteItems(MenuFlyout menu)
    {
        if (!UiTest.Enabled)
        {
            return;
        }

        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (!menu.IsOpen)
            {
                timer.Stop();
                return;
            }

            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                if (item.IsEffectivelyVisible && item.Bounds.Width > 0)
                {
                    UiTest.Where("item:" + item.Header, item);
                }
            }
        };
        timer.Start();
    }

    /// <summary>For the workflow's test: the menu closed, its items are gone.</summary>
    private static void ItemsGone(MenuFlyout menu)
    {
        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            UiTest.Gone("item:" + item.Header);
        }
    }

    /// <summary>A few times a second (the window's quick timer): the boss's next casts and the dummy's countdown move on their own.</summary>
    public void UpdateQuick()
    {
        DateTime now = DateTime.Now;
        _engine.UpdateCallout(now);
        UpdateCallout(LocalizationManager.Instance);
        if (_engine.DummyHeader(now) is { } dummy && _body.IsVisible)
        {
            _time.Text = dummy.Clock;
            UpdateDummyCard(now);
        }
    }

    // ---- the parts of the body ----

    private void UpdateCallout(LocalizationManager loc)
    {
        if (!_body.IsVisible || _engine.Callout is not { } frame)
        {
            _callout.IsVisible = false;
            return;
        }

        var inlines = new InlineCollection();
        bool first = true;
        void Gap()
        {
            if (!first)
            {
                inlines.Add(new Run("   ·   ") { Foreground = Palette.OverlayMuted });
            }

            first = false;
        }

        foreach (BossCallout c in frame.Next.Take(2))
        {
            Gap();
            inlines.Add(new Run(c.Name) { FontWeight = FontWeight.SemiBold });
            inlines.Add(new Run("  " + (c.Casting ? loc["Overlay.BossNow"] : string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossIn"], Math.Ceiling(c.In))))
            {
                Foreground = c.Casting || c.In <= 5 ? Palette.OverlayRed : c.In <= 15 ? Palette.OverlayAmber : Palette.OverlayMuted,
                FontWeight = FontWeight.SemiBold,
            });
        }

        if (frame.Rage is TimeSpan rage)
        {
            Gap();
            inlines.Add(new Run(rage > TimeSpan.Zero ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.Enrage"], $"{(int)rage.TotalMinutes}:{rage.Seconds:00}") : loc["Overlay.Enraged"])
            {
                Foreground = rage <= TimeSpan.FromSeconds(20) ? Palette.OverlayRed : rage <= TimeSpan.FromSeconds(60) ? Palette.OverlayAmber : Palette.OverlayMuted,
                FontWeight = FontWeight.SemiBold,
            });
        }

        _calloutText.Inlines = inlines;
        ToolTip.SetTip(_callout, frame.Fights > 0
            ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossTimersTip"], frame.Boss, frame.Fights) + (frame.Rage is null ? "" : loc["Overlay.BossTimersTipRage"])
            : string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossTimersRageOnly"], frame.Boss));
        _callout.IsVisible = true;
    }

    private void UpdateDummyCard(DateTime now)
    {
        if (!_engine.DummyCardShown || _engine.DummyCard(now) is not { } card)
        {
            _dummyCard.IsVisible = false;
            return;
        }

        _dummyRun.Text = card.Run;
        _dummyClock.Text = card.Clock;
        _dummyDps.Text = PlayerRow.Compact((long)Math.Round(card.Dps));
        double? fraction = _engine.DummyHeader(now)?.Fraction;
        _dummyBar.IsVisible = fraction is not null;
        if (fraction is double f)
        {
            _dummyBar.ColumnDefinitions[0].Width = new GridLength(Math.Max(f, 0.0001), GridUnitType.Star);
            _dummyBar.ColumnDefinitions[1].Width = new GridLength(Math.Max(1 - f, 0.0001), GridUnitType.Star);
        }

        // the figures two to a line: the label on the left, the figure on the right
        int lines = (card.Stats.Count + 1) / 2;
        if (_dummyStats.RowDefinitions.Count != lines || _dummyStats.Children.Count != card.Stats.Count * 2)
        {
            _dummyStats.RowDefinitions.Clear();
            _dummyStats.Children.Clear();
            for (int r = 0; r < lines; r++)
            {
                _dummyStats.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            for (int i = 0; i < card.Stats.Count; i++)
            {
                var label = Text(11.5, FontWeight.Normal, Palette.TextMuted);
                var value = Text(11.5, FontWeight.SemiBold, Palette.OverlayText);
                label.HorizontalAlignment = HorizontalAlignment.Left;
                value.HorizontalAlignment = HorizontalAlignment.Right;
                var cell = new Grid { Margin = new Thickness(i % 2 == 0 ? 0 : 14, 1, 0, 1) };
                cell.Children.Add(label);
                cell.Children.Add(value);
                Grid.SetRow(cell, i / 2);
                Grid.SetColumn(cell, i % 2);
                _dummyStats.Children.Add(cell);
            }
        }

        for (int i = 0; i < card.Stats.Count; i++)
        {
            var cell = (Grid)_dummyStats.Children[i];
            ((TextBlock)cell.Children[0]).Text = card.Stats[i].Label;
            ((TextBlock)cell.Children[1]).Text = card.Stats[i].Value;
        }

        _dummyCard.IsVisible = true;
    }

    private void UpdateBossLine(LocalizationManager loc)
    {
        if (!_engine.BossLineOn || _engine.DummyCardShown || _engine.CurrentBoss() is not { } boss)
        {
            _bossLine.IsVisible = false;
            return;
        }

        // the head names the boss already: the line says how much of it is left instead of naming it twice (Windows: ShowBossBar)
        bool named = boss.Name.Length > 0 && _target.Text is { Length: > 0 } header && header.Contains(boss.Name, StringComparison.OrdinalIgnoreCase);
        string share = boss.Max > 0 ? Percent(Math.Clamp((double)boss.Hp / boss.Max, 0, 1)) : "";
        _bossNumbers.Text = boss.Max > 0 ? $"{PlayerRow.Compact(boss.Hp)} / {PlayerRow.Compact(boss.Max)}" + (named ? "" : " · " + share) : PlayerRow.Compact(boss.Hp);
        _bossName.Text = named && boss.Max > 0 ? share + " left" : boss.Name.Length > 0 ? boss.Name : loc["Overlay.BossBar.Boss"];
        bool rageFirst = boss.Kill is TimeSpan k && boss.Rage is TimeSpan r && r > TimeSpan.Zero && k > r;
        _bossKill.Text = boss.Kill is TimeSpan dies ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Kill"], Clock(dies)) : loc["Overlay.BossLine.NoKill"];
        _bossKill.Foreground = boss.Kill is null ? Palette.OverlayMuted : rageFirst ? Palette.OverlayRed : Palette.KillGreen;
        if (boss.Rage is TimeSpan left)
        {
            _bossRage.Text = left > TimeSpan.Zero ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Enrage"], Clock(left)) : loc["Overlay.Enraged"];
            _bossRage.Foreground = left <= TimeSpan.FromSeconds(20) ? Palette.OverlayRed : left <= TimeSpan.FromSeconds(60) ? Palette.OverlayAmber : Palette.OverlayMuted;
            _bossRage.IsVisible = true;
        }
        else
        {
            _bossRage.IsVisible = false;
        }

        _bossLine.IsVisible = true;
    }

    private void UpdateStagger(DateTime now)
    {
        if (!_engine.ShowStagger)
        {
            _staggerCard.IsVisible = false;
            _staggerKey = "";
            return;
        }

        now = _engine.SampleStaggerAt ?? now;
        var (checks, shown, people, next) = _engine.Stagger(now);

        // built again only when something on it changed (a tooltip being read stays open); a running check's time moves every time
        string key = string.Join("|", shown?.Start.Ticks, shown?.Outcome, shown?.Left, shown?.Outcome == StaggerOutcome.Running ? Math.Round(shown.Seconds(now), 1) : 0, next,
            string.Join(",", checks.Select(c => c.Start.Ticks + ":" + c.Outcome)),
            shown is null ? "" : string.Join(",", shown.Credit.Players.Select(p => p.Player + ":" + p.Amount + ":" + p.Skills.Count)), string.Join(",", people.Select(p => p.Id)));
        if (key != _staggerKey || !_staggerCard.IsVisible)
        {
            _staggerKey = key;
            _staggerCard.Child = StaggerView.MeterView(checks, shown, now, people, _engine.StaggerPersonOf, next, c =>
            {
                _engine.PickStagger(c);
                _staggerKey = "";
                Update();
            });
        }

        _staggerCard.IsVisible = true;
    }

    private void UpdateGrind(DateTime now)
    {
        if (!_engine.ShowGrind)
        {
            _grindCard.IsVisible = false;
            _grindKey = "";
            return;
        }

        GrindFigures? f = _engine.GrindNow(now);
        string key = f is null ? "none" : string.Join("|", f.Session.Id, f.Session.Kinah, f.Session.AbyssPoints, f.Session.Kills, f.Items, f.LootWorth, f.FarmedText, f.Over,
            f.KinahPerHour is double k ? Math.Round(k) : -1, f.ApPerHour is double a ? Math.Round(a) : -1, f.KillsPerHour is double kh ? Math.Round(kh) : -1,
            f.LootPerHour is double lh ? Math.Round(lh) : -1);
        if (key != _grindKey || !_grindCard.IsVisible)
        {
            _grindKey = key;
            _grindCard.Child = GrindCard.Build(f, _engine.SampleGrind is null ? _engine.NewGrindSession : null);
        }

        _grindCard.IsVisible = true;
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
                _rowViews[i].Show(rows[i], _engine.Mode);
            }
        }

        FitLines();
        FitRowsArea();
    }

    /// <summary>The lines on a meter narrower than they need are drawn smaller, all by the same amount so the columns stay in line (Windows: FitMeterLines): the
    /// name gives up its room first, only then the text gets smaller, never below 70 %.</summary>
    private void FitLines()
    {
        const double needed = 20 + 18 + 7 + 80 + 58 + 42 + 48; // the margins, the icon, some of the name and the three figures (MeterLook.LineWidthNeeded)
        double available = (double.IsNaN(Width) ? Bounds.Width : Width) - 2;
        double scale = available > 0 && needed > available ? Math.Max(0.7, available / needed) : 1;
        _linesFit.LayoutTransform = scale < 0.995 ? new ScaleTransform(scale, scale) : null;
    }

    /// <summary>The lines take as much room as there are lines, up to ten (or less when the meter was dragged shorter), and give it back as they go (Windows: FitRowsArea).</summary>
    private void FitRowsArea()
    {
        double ten = 10 * (RowView.LineHeight + RowView.Gap) + 2;
        _rowsArea.MaxHeight = _rowsCap > 0 ? Math.Min(_rowsCap, ten) : ten;
    }

    private void UpdateLegend()
    {
        if (_engine.Legend is not { } legend || !_body.IsVisible)
        {
            _legend.IsVisible = false;
            return;
        }

        string key = $"{legend.Rdps}|{legend.Buffs}|{legend.Heals}";
        if (key != _legendKey)
        {
            _legendKey = key;
            _legend.Children.Clear();
            void Item(IBrush swatch, string text)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
                item.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Background = swatch });
                TextBlock words = Text(10.5, FontWeight.Normal, Palette.TextMuted);
                words.Text = text;
                item.Children.Add(words);
                _legend.Children.Add(item);
            }

            // the own part is the class colour: three of them in the swatch
            Color gladiator = ClassLook.ColorOf("Gladiator"), sorcerer = ClassLook.ColorOf("Sorcerer"), ranger = ClassLook.ColorOf("Ranger");
            var classes = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(gladiator, 0.0), new GradientStop(gladiator, 0.33), new GradientStop(sorcerer, 0.34),
                    new GradientStop(sorcerer, 0.66), new GradientStop(ranger, 0.67), new GradientStop(ranger, 1.0),
                },
            };
            Item(classes, "own damage");
            if (legend.Buffs)
            {
                Item(new SolidColorBrush(Color.FromArgb(0xCC, 0xB4, 0xBD, 0xC8)), "buffs for others");
            }

            if (legend.Heals)
            {
                Item(new SolidColorBrush(Color.FromArgb(0xD9, 0x52, 0xD2, 0x86)), "healing on others");
            }

            string bar = legend.Rdps ? "bar = rDPS" : "bar = damage" + (legend.Buffs ? " + buffs" : "");
            TextBlock whole = Text(10.5, FontWeight.Normal, Palette.TextSubtle);
            whole.Text = bar + (legend.Heals ? " + healing" : "");
            _legend.Children.Add(whole);
        }

        _legend.IsVisible = true;
    }

    private void UpdateBuffRows()
    {
        _buffRows.IsVisible = _engine.ShowBuffs;
        if (!_engine.ShowBuffs)
        {
            return;
        }

        IReadOnlyList<BuffLine> lines = _engine.BuffLines;
        while (_buffViews.Count < lines.Count)
        {
            var view = new BuffRowView();
            _buffViews.Add(view);
            _buffRows.Children.Add(view);
        }

        for (int i = 0; i < _buffViews.Count; i++)
        {
            bool shown = i < lines.Count;
            _buffViews[i].IsVisible = shown;
            if (shown)
            {
                _buffViews[i].Show(lines[i]);
            }
        }
    }

    private void UpdateTickets(DateTime now)
    {
        IReadOnlyList<FootTicket> tickets = _engine.Tickets(now);
        string key = string.Join("|", tickets.Select(t => $"{t.Icon}:{t.Value}:{t.Bonus}:{t.Extra}:{t.Warn}:{t.Tip}"));
        if (key == _ticketsKey)
        {
            return;
        }

        _ticketsKey = key;
        _tickets.Children.Clear();
        foreach (FootTicket ticket in tickets)
        {
            var glyph = new Image { Source = Pictures.Load(Aion2.GameArt.PathOf(ticket.Icon)), Width = 17, Height = 17, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
            var text = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, FontFamily = Palette.UiFont };
            var inlines = new InlineCollection { new Run(ticket.Value) { FontWeight = FontWeight.SemiBold, Foreground = ticket.Warn ? Palette.Warning : Palette.OverlayText } };
            if (ticket.Bonus is { } bonus)
            {
                inlines.Add(new Run(" " + bonus) { Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xB6, 0xFF)), FontSize = 10 });
            }

            if (ticket.Extra is { } extra)
            {
                inlines.Add(new Run(" " + extra) { FontSize = 10, Foreground = Palette.TextMuted });
            }

            text.Inlines = inlines;

            // the counters wrap to a second line on a narrow meter
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 11, 1), Background = Brushes.Transparent };
            ToolTip.SetTip(item, ticket.Tip);
            item.Children.Add(glyph);
            item.Children.Add(text);
            _tickets.Children.Add(item);
        }
    }

    // ---- the menus ----

    /// <summary>The pill's menu (Windows: OverlayModeItems): what is measured, the training dummy's parse, Grind.</summary>
    private void FillModeMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        MeterEngine engine = _engine;
        bool card = engine.DummyCardShown;
        bool other = !card && !engine.ShowBuffs && !engine.ShowStagger && !engine.ShowGrind;
        MeterSettings settings = MeterSettings.Load();
        DummyState state = engine.Dummy?.Model.State ?? DummyState.Idle;
        int seconds = Math.Clamp(settings.DummyTestSeconds, 10, 3600);
        string length = seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s";
        void Add(string header, Action act, bool on = false, bool enabled = true)
        {
            var item = new MenuItem { Header = header, Icon = new TextBlock { Text = on ? "✓" : "", FontSize = 12, Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Center }, IsEnabled = enabled };
            item.Click += (_, _) =>
            {
                act();
                Update();
                Did("menu " + header);
            };
            menu.Items.Add(item);
        }

        Add("DPS", () => engine.PickMode(MeterEngine.MeterMode.Damage), other && engine.Mode == MeterEngine.MeterMode.Damage);
        Add("rDPS (buffs credited)", () => engine.PickMode(MeterEngine.MeterMode.Raid), other && engine.Mode == MeterEngine.MeterMode.Raid);
        Add("Healing", () => engine.PickMode(MeterEngine.MeterMode.Heal), other && engine.Mode == MeterEngine.MeterMode.Heal);
        Add("Tanked (damage taken)", () => engine.PickMode(MeterEngine.MeterMode.Taken), other && engine.Mode == MeterEngine.MeterMode.Taken);
        Add("Buffs on you", engine.PickBuffs, !card && engine.ShowBuffs);
        Add("All in one", () => engine.PickMode(MeterEngine.MeterMode.All), other && engine.Mode == MeterEngine.MeterMode.All);
        Add("Grind: Kinah, Abyss Points and loot per hour", () => engine.SetGrindView(true), !card && engine.ShowGrind);
        menu.Items.Add(new Separator());
        if (engine.DummyActive)
        {
            Add("Dummy: the parse's card", engine.ShowDummyCard, card);
        }

        Add($"Dummy: a {length} parse by itself when I hit a dummy", () => engine.ChangeSetting(s => s.DummyAutoStart = !s.DummyAutoStart), settings.DummyAutoStart);
        if (state is DummyState.Armed or DummyState.Running)
        {
            Add(state == DummyState.Running ? "Dummy: end the parse now (it is saved)" : "Dummy: take the waiting parse back", () => engine.Dummy?.Model.Stop(DateTime.Now));
        }
        else
        {
            Add($"Dummy: start a {length} parse now (the clock starts with my first hit)", () => engine.Dummy?.Start(TimeSpan.FromSeconds(seconds), ""), false, engine.Dummy is not null);
        }

        menu.Items.Add(new Separator());
        Add("Grind: every fight, trash and the open world too", () => engine.ChangeSetting(s => s.MeterGrind = !s.MeterGrind), settings.MeterGrind);
    }

    /// <summary>The "…" menu: the Linux app's own items (Windows: OverlaySettingsItems has its other windows, which the Linux app does not have yet).</summary>
    private void FillMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        void Add(string header, string action, bool on = false)
        {
            var item = new MenuItem { Header = header, Icon = new TextBlock { Text = on ? "✓" : "", FontSize = 12, Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Center } };
            item.Click += (_, _) =>
            {
                MenuChosen?.Invoke(action);
                Did("menu " + header);
            };
            menu.Items.Add(item);
        }

        Add(_engine.Paused ? "Go on counting" : "Pause (nothing is counted until you go on)", "pause");
        Add("Count the open world too", "openworld", _engine.TrackOpenWorld);
        Add("Always on top", "topmost", MeterWindowState.Topmost);
        Add("Let the clicks through to the game (but on these buttons)", "clickthrough", MeterSettings.Load().CompactClickThrough);
        menu.Items.Add(new Separator());
        Add("Capture check (what the app can see)", "capturecheck");
        Add("Open the data folder", "datafolder");
        menu.Items.Add(new Separator());
        Add("Quit Atreia App", "quit");
    }

    private static Button IconButton(Control icon, string tip)
    {
        var button = new Button
        {
            Content = icon, Width = 23, Height = 22, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    internal static TextBlock Text(double size, FontWeight weight, IBrush brush) => new()
    {
        FontFamily = Palette.UiFont,
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

/// <summary>The window's state the menu shows (set by the window).</summary>
internal static class MeterWindowState
{
    public static bool Topmost { get; set; } = true;
}
