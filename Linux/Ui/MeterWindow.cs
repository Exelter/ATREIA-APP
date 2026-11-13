using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AionDPS.Aion2.Capture;
using AionDPS.Combat.Sources;
using AionDPS.Linux.Meter;
using AionDPS.Linux.Platform;
using AionDPS.Ui;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The meter over the game, as the Windows meter overlay behaves (Client/Ui/MeterOverlayWindow.cs and the meter's parts of MainWindow): a window without a
/// frame, see-through around its rounded panel, on top of the game, moved by its head. Its height follows what it shows (the bar alone between fights, the
/// lines as they come, up to ten); its corner grip makes it wider or longer, with Ctrl bigger, text and all, and a double-click gives it its own size back.
/// The lock keeps it in place. "Let the clicks through" (the menu) passes every click to the game but on the bar's buttons (<see cref="X11Shape"/>).
/// Ticks the engine once a second; its own line says what keeps the meter from working - first of all whether this app may read the network
/// (<see cref="CaptureRights"/>).
/// </summary>
internal sealed class MeterWindow : Window
{
    private const double MinScale = 0.7, MaxScale = 2.0, MinWidth_ = 240, MaxWidth_ = 1400, MinRows = 40, MaxRows = 1400;

    /// <summary>The bar's own width (the character, the clock, the pill and the buttons): the meter's width until the player drags it (Windows: OverlayBarWidth).</summary>
    private const double BarWidth = 470;

    private readonly MeterEngine _engine;
    private readonly MeterView _view;
    private readonly LayoutTransformControl _scaled = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _quickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(3) };
    private readonly DispatcherTimer _flushTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly WindowSpot _spot = WindowSpot.Load();
    private CaptureRights.Rights _rights = CaptureRights.Rights.Granted;
    private string? _note;
    private DateTime _noteUntil = DateTime.MinValue;
    private string? _images;
    private string? _newVersion;
    private string? _action;
    private bool _clickThrough;

    /// <summary>The width the player dragged the meter to (before its scale), 0 for the bar's own; the most the lines may take (0: up to ten); the scale.</summary>
    private double _userWidth, _rowsCap, _scale = 1;

    public MeterWindow(MeterEngine engine)
    {
        _engine = engine;
        Title = Brand.AppName;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = _spot.Topmost;
        MeterWindowState.Topmost = Topmost;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = true;
        FontFamily = Palette.UiFont;
        WindowStartupLocation = _spot.X is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
        if (_spot.X is int x && _spot.Y is int y)
        {
            Position = new PixelPoint(x, y);
        }

        string emblem = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "app", "a2h-emblem.png");
        if (System.IO.File.Exists(emblem))
        {
            Icon = new WindowIcon(emblem);
        }

        MeterSettings settings = MeterSettings.Load();
        _userWidth = double.IsFinite(settings.OverlayWidth) && settings.OverlayWidth > 0 ? Math.Clamp(settings.OverlayWidth, MinWidth_, MaxWidth_) : 0;
        _rowsCap = double.IsFinite(settings.OverlayRowsHeight) && settings.OverlayRowsHeight > 0 ? Math.Clamp(settings.OverlayRowsHeight, MinRows, MaxRows) : 0;
        _scale = Math.Clamp(double.IsFinite(settings.OverlayScale) ? settings.OverlayScale : 1, MinScale, MaxScale);
        _clickThrough = settings.CompactClickThrough;

        _view = new MeterView(engine) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _view.HeaderPressed += BeginMove;
        PointerMoved += (_, _) => Move();
        PointerReleased += (_, e) => EndMove(e.Pointer);
        PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this))
            {
                EndMove(null);
            }
        };
        _view.LockClicked += () => SetLocked(!_spot.Locked);
        _view.StatusActionClicked += OnStatusAction;
        _view.MenuChosen += OnMenu;
        HookGrip(_view.Grip);
        _scaled.Child = _view;
        Content = _scaled;
        ApplySize();

        _engine.Changed += () => _view.Update();
        _engine.Note += text => Say(text, TimeSpan.FromSeconds(20));
        _timer.Tick += (_, _) => Tick();
        _quickTimer.Tick += (_, _) => _view.UpdateQuick();
        _flushTimer.Tick += (_, _) => FlushStores();
        Opened += (_, _) =>
        {
            _view.ShowLocked(_spot.Locked);
            ApplyClickThrough();
            ShowStatus();
            _timer.Start();
            _quickTimer.Start();
            _flushTimer.Start();
            _ = CheckForUpdateAsync();
            _updateTimer.Start();
        };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        LayoutUpdated += (_, _) =>
        {
            if (_clickThrough)
            {
                ApplyClickThrough();
            }
        };
        Closing += (_, _) =>
        {
            Remember();
            LinuxLog.Write($"action: quit at {Position.X},{Position.Y}");
        };
    }

    /// <summary>Starts reading the game, when this app may: without the right to read the network the capture is not started, and the status line says how
    /// to give it.</summary>
    public void StartCapture(MeterSettings settings)
    {
        _rights = CaptureRights.Check();
        LinuxLog.Write($"{Brand.AppName} {Update.AppVersion.Text} starts; capture rights: {_rights}; {CaptureCheck.Summary()}");
        if (_rights != CaptureRights.Rights.Granted)
        {
            _engine.ApplySettings(settings);
            ShowStatus();
            return;
        }

        try
        {
            _engine.Start(settings);
        }
        catch (Exception ex)
        {
            // the capture could not start (a device libpcap refuses ...): said on the status line, the app stays
            CrashLog.Write("the capture did not start", ex);
            LinuxLog.Write("the capture did not start: " + ex.GetBaseException().Message);
            Say("The capture could not start: " + ex.GetBaseException().Message, TimeSpan.FromDays(1));
        }

        ShowStatus();
    }

    /// <summary>The image pack's download, for the status line (null when none runs).</summary>
    public void ShowImages(string? text)
    {
        _images = text;
        ShowStatus();
    }

    private void Tick()
    {
        try
        {
            _engine.Tick();
        }
        catch (Exception ex)
        {
            // one bad tick must not end the meter: noted, the next one tries again
            CrashLog.Write("meter tick", ex);
        }

        ShowStatus();
    }

    private void Say(string text, TimeSpan forHow)
    {
        _note = text;
        _noteUntil = DateTime.Now + forHow;
        ShowStatus();
    }

    /// <summary>The status line: what keeps the meter from working first, then the notes, the image pack's download and a newer version.</summary>
    private void ShowStatus()
    {
        (string text, bool warning, string? action) = StatusLine.For(_rights, _engine, _note is not null && DateTime.Now < _noteUntil ? _note : null, _images, _newVersion);
        _action = action;
        _view.ShowStatus(text, warning, action);
    }

    /// <summary>Asks atreia.app whether a newer Linux app is out (at the start and every three hours).</summary>
    private async Task CheckForUpdateAsync()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string? newer = await LinuxUpdates.NewerAsync(cancel.Token);
        if (newer != _newVersion)
        {
            _newVersion = newer;
            LinuxLog.Write(newer is null ? "update check: this is the newest" : $"update check: {newer} is out");
            ShowStatus();
        }
    }

    private async void OnStatusAction()
    {
        if (_action == StatusLine.CopyCommand && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(CaptureRights.SetcapCommand);
            Say("The command is copied: paste it into a terminal, then start Atreia App again.", TimeSpan.FromSeconds(30));
        }
        else if (_action == StatusLine.GetUpdate)
        {
            LinuxUpdates.OpenDownload();
        }
    }

    private void OnMenu(string what)
    {
        MeterSettings settings = MeterSettings.Load();
        switch (what)
        {
            case "pause":
                _engine.Paused = !_engine.Paused;
                _view.Update();
                break;
            case "openworld":
                _engine.TrackOpenWorld = !_engine.TrackOpenWorld;
                settings.TrackOpenWorld = _engine.TrackOpenWorld;
                settings.Save();
                Say(_engine.TrackOpenWorld ? "The open world is counted now." : "The open world is not counted now (dungeons, boss fights, PvP and dummies are).", TimeSpan.FromSeconds(8));
                break;
            case "topmost":
                Topmost = !Topmost;
                MeterWindowState.Topmost = Topmost;
                _spot.Topmost = Topmost;
                _spot.Save();
                Say(Topmost ? "The meter stays on top of other windows." : "The meter no longer stays on top.", TimeSpan.FromSeconds(8));
                break;
            case "clickthrough":
                _clickThrough = !_clickThrough;
                settings.CompactClickThrough = _clickThrough;
                settings.Save();
                ApplyClickThrough();
                Say(_clickThrough ? "The clicks go through the meter to the game now, but on its buttons." : "The meter takes its clicks again.", TimeSpan.FromSeconds(8));
                break;
            case "capturecheck":
                Say(CaptureCheck.Summary(), TimeSpan.FromSeconds(45));
                break;
            case "datafolder":
                OpenFolder(MeterSettings.DataDirectory);
                break;
            case "quit":
                Close();
                break;
        }
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{folder}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            // no file manager to hand it to
        }
    }

    /// <summary>The lock (Windows: OnOverlayLockClicked): locked, the head no longer moves the meter and the grip is gone; its buttons still work.</summary>
    private void SetLocked(bool locked)
    {
        _spot.Locked = locked;
        _spot.Save();
        _view.ShowLocked(locked);
        _view.Update();
        LinuxLog.Write($"action: {(locked ? "locked in place" : "unlocked")} -> {_view.State}");
    }

    // ---- moved by its head (Windows: DragMove) ----

    private PixelPoint _moveFromPointer, _moveFromWindow, _moveTo;
    private bool _moving;

    /// <summary>A press on the head: the meter follows the mouse until it is let go, by the mouse's place on the screen (<see cref="X11Pointer"/>) - the
    /// window moves under the mouse, so its own positions would lag. Without an X server to ask, the window manager moves it. Locked, it stays.</summary>
    private void BeginMove(PointerPressedEventArgs e)
    {
        if (_spot.Locked)
        {
            return;
        }

        if (!X11Pointer.TryGet(out PixelPoint pointer))
        {
            BeginMoveDrag(e);
            return;
        }

        _moving = true;
        _moveFromPointer = pointer;
        _moveFromWindow = _moveTo = Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void Move()
    {
        if (!_moving || !X11Pointer.TryGet(out PixelPoint pointer))
        {
            return;
        }

        var to = new PixelPoint(_moveFromWindow.X + pointer.X - _moveFromPointer.X, _moveFromWindow.Y + pointer.Y - _moveFromPointer.Y);
        if (to != _moveTo)
        {
            _moveTo = to;
            Position = to;
        }
    }

    /// <summary>Let go (or the mouse taken by something else): where the meter stands is kept.</summary>
    private void EndMove(IPointer? pointer)
    {
        if (!_moving)
        {
            return;
        }

        if (pointer is not null)
        {
            Move();
        }

        _moving = false;
        pointer?.Capture(null);
        if (_moveTo != _moveFromWindow)
        {
            Remember(_moveTo);
            LinuxLog.Write($"action: moved to {_moveTo.X},{_moveTo.Y}");
        }
    }

    // ---- the size: the player's width, the lines' room, the scale (Windows: SetCompactOverlaySize, OnCompactOverlayResize) ----

    private Point _gripFrom;
    private double _gripWidth, _gripRows, _gripScale;
    private bool _gripping;

    private double ViewWidth => _userWidth > 0 ? _userWidth : BarWidth;

    private void ApplySize()
    {
        _view.Width = Math.Clamp(ViewWidth, MinWidth_, MaxWidth_);
        _view.RowsCap = _rowsCap;
        _scaled.LayoutTransform = Math.Abs(_scale - 1) < 0.001 ? null : new ScaleTransform(_scale, _scale);
    }

    private Point PointerOnScreen(PointerEventArgs e)
    {
        PixelPoint device = this.PointToScreen(e.GetPosition(this));
        return new Point(device.X / RenderScaling, device.Y / RenderScaling);
    }

    private void HookGrip(Control grip)
    {
        grip.PointerPressed += (_, e) =>
        {
            if (_spot.Locked || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                return;
            }

            e.Handled = true;
            if (e.ClickCount == 2)
            {
                // the meter's own width and height again (its scale stays)
                _userWidth = 0;
                _rowsCap = 0;
                ApplySize();
                SaveSize();
                return;
            }

            _gripping = true;
            _gripFrom = PointerOnScreen(e);
            _gripWidth = _view.Width;
            _gripRows = _view.RowsHeight;
            _gripScale = _scale;
            e.Pointer.Capture(grip);
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!_gripping)
            {
                return;
            }

            Vector moved = PointerOnScreen(e) - _gripFrom;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _scale = Math.Clamp((_gripWidth * _gripScale + Math.Max(moved.X, moved.Y)) / _gripWidth, MinScale, MaxScale);
            }
            else
            {
                _userWidth = Math.Clamp(_gripWidth + moved.X / _scale, MinWidth_, MaxWidth_);
                if (Math.Abs(moved.Y) >= 2)
                {
                    _rowsCap = Math.Clamp(_gripRows + moved.Y / _scale, MinRows, MaxRows);
                }
            }

            ApplySize();
        };
        grip.PointerReleased += (_, e) =>
        {
            if (!_gripping)
            {
                return;
            }

            _gripping = false;
            e.Pointer.Capture(null);
            SaveSize();
        };
        grip.PointerCaptureLost += (_, _) => _gripping = false;
    }

    private void SaveSize()
    {
        MeterSettings settings = MeterSettings.Load();
        settings.OverlayScale = _scale;
        settings.OverlayWidth = _userWidth;
        settings.OverlayExtraWidth = 0;
        settings.OverlayRowsHeight = _rowsCap;
        settings.Save();
        LinuxLog.Write($"size: width {(_userWidth > 0 ? _userWidth.ToString("0") : "the bar's")}, lines {(_rowsCap > 0 ? _rowsCap.ToString("0") + " high at most" : "up to ten")}, scale {_scale:0.00}");
    }

    // ---- the clicks through to the game ----

    private string? _shapeApplied;
    private bool _shapeFailSaid;

    /// <summary>"Let the clicks through": only the bar's buttons (and the status line's while it is there) take the mouse; off: the whole window.</summary>
    private void ApplyClickThrough()
    {
        if (TryGetPlatformHandle() is not { } handle || handle.HandleDescriptor != "XID")
        {
            return;
        }

        if (!_clickThrough)
        {
            if (_shapeApplied is not null)
            {
                X11Shape.ClearInputRegion(handle.Handle);
                _shapeApplied = null;
                LinuxLog.Write("input region: the whole window again");
            }

            return;
        }

        var parts = new List<(int X, int Y, int Width, int Height)>();
        foreach (Control button in _view.ClickableParts)
        {
            if (button.IsEffectivelyVisible && button.TranslatePoint(new Point(0, 0), this) is Point at)
            {
                double scale = RenderScaling;
                Size size = button.Bounds.Size * _scale;
                parts.Add(((int)Math.Floor(at.X * scale) - 2, (int)Math.Floor(at.Y * scale) - 2, (int)Math.Ceiling(size.Width * scale) + 4, (int)Math.Ceiling(size.Height * scale) + 4));
            }
        }

        if (parts.Count == 0)
        {
            return;
        }

        string shape = string.Join(" ", parts);
        if (_shapeApplied == shape)
        {
            return;
        }

        if (X11Shape.SetInputRegion(handle.Handle, parts))
        {
            _shapeApplied = shape;
            LinuxLog.Write($"input region: the bar's buttons alone ({parts.Count} parts): the clicks go through the rest");
        }
        else if (!_shapeFailSaid)
        {
            _shapeFailSaid = true;
            LinuxLog.Write("input region: no X server to ask (a Wayland-only window): the meter still takes its clicks");
        }
    }

    // ---- the stores the meter fills (the counters, the grind, the loot) ----

    /// <summary>Every 15 seconds and when the meter closes: what the stores hold, on disk (Windows: FlushStores).</summary>
    private static void FlushStores()
    {
        foreach ((string name, Action flush) in new (string, Action)[]
        {
            ("tickets", () => Aion2.Aion2Tickets.Flush(false)),
            ("loot", () => Aion2.Aion2Loot.Flush()),
            ("grind", () => Aion2.Aion2Grind.Flush()),
        })
        {
            try
            {
                flush();
            }
            catch (Exception ex)
            {
                CrashLog.Write("the " + name + " could not be saved", ex);
            }
        }
    }

    /// <summary>Keeps where the meter stands (where it was just moved to: the window's own place may come a moment later) and whether it is on top.</summary>
    private void Remember(PixelPoint? at = null)
    {
        PixelPoint spot = at ?? Position;
        _spot.X = spot.X;
        _spot.Y = spot.Y;
        _spot.Topmost = Topmost;
        _spot.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _quickTimer.Stop();
        _updateTimer.Stop();
        _flushTimer.Stop();
        FlushStores();
        _engine.Dispose();
        base.OnClosed(e);
    }
}
