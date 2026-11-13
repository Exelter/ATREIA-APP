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
/// The meter over the game: a window without a frame, see-through around its rounded panel, on top of the game, dragged by its head and sized by its
/// corner. Locked, it lets the clicks through to the game except on its lock button (<see cref="X11Shape"/>). Ticks the engine once a second and says on
/// its status line what the capture has to say - first of all whether this app may read the network at all (<see cref="CaptureRights"/>).
/// </summary>
internal sealed class MeterWindow : Window
{
    private readonly MeterEngine _engine;
    private readonly MeterView _view;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly WindowSpot _spot = WindowSpot.Load();
    private CaptureRights.Rights _rights = CaptureRights.Rights.Granted;
    private string? _note;
    private DateTime _noteUntil = DateTime.MinValue;
    private string? _images;
    private string? _newVersion;
    private string? _action;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(3) };

    public MeterWindow(MeterEngine engine)
    {
        _engine = engine;
        Title = Brand.AppName;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = _spot.Topmost;
        CanResize = true;
        ShowInTaskbar = true;
        MinWidth = 300;
        MinHeight = 140;
        Width = _spot.Width;
        Height = _spot.Height;
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

        _view = new MeterView(engine);
        _view.HeaderPressed += e =>
        {
            if (!_spot.Locked)
            {
                BeginMoveDrag(e);
            }
        };
        _view.LockClicked += () => SetLocked(!_spot.Locked);
        _view.CloseClicked += Close;
        _view.StatusActionClicked += OnStatusAction;
        _view.MenuChosen += OnMenu;

        // the corner to size the meter by
        var grip = new Border
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
        };
        grip.PointerPressed += (_, e) =>
        {
            if (!_spot.Locked && e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                BeginResizeDrag(WindowEdge.SouthEast, e);
                e.Handled = true;
            }
        };
        var root = new Panel();
        root.Children.Add(_view);
        root.Children.Add(grip);
        Content = root;

        _engine.Changed += () => _view.Update();
        _engine.Note += text => Say(text, TimeSpan.FromSeconds(20));
        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) =>
        {
            _view.ShowLocked(_spot.Locked);
            ApplyClickThrough();
            ShowStatus();
            _timer.Start();
            _ = CheckForUpdateAsync();
            _updateTimer.Start();
        };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        LayoutUpdated += (_, _) =>
        {
            if (_spot.Locked)
            {
                ApplyClickThrough();
            }
        };
        Closing += (_, _) => Remember();
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

        _engine.Start(settings);
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

    /// <summary>The status line: what keeps the meter from working first, then what the capture says, the image pack's download and the notes.</summary>
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
            case "openworld":
                _engine.TrackOpenWorld = !_engine.TrackOpenWorld;
                settings.TrackOpenWorld = _engine.TrackOpenWorld;
                settings.Save();
                Say(_engine.TrackOpenWorld ? "The open world is counted now." : "The open world is not counted now (dungeons, boss fights and dummies are).", TimeSpan.FromSeconds(8));
                break;
            case "topmost":
                Topmost = !Topmost;
                _spot.Topmost = Topmost;
                _spot.Save();
                Say(Topmost ? "The meter stays on top of other windows." : "The meter no longer stays on top.", TimeSpan.FromSeconds(8));
                break;
            case "capturecheck":
                Say(CaptureCheck.Summary(), TimeSpan.FromSeconds(45));
                break;
            case "datafolder":
                OpenFolder(MeterSettings.DataDirectory);
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

    private void SetLocked(bool locked)
    {
        _spot.Locked = locked;
        _spot.Save();
        _view.ShowLocked(locked);
        ApplyClickThrough();
        Say(locked ? "Locked: the clicks go through to the game. The lock button unlocks it." : "Unlocked: drag the meter by its head, size it by its corner.", TimeSpan.FromSeconds(6));
    }

    private string? _shapeApplied;
    private bool _shapeFailSaid;

    /// <summary>Locked: only the lock button takes the mouse; unlocked: the whole window.</summary>
    private void ApplyClickThrough()
    {
        if (TryGetPlatformHandle() is not { } handle || handle.HandleDescriptor != "XID")
        {
            return;
        }

        if (!_spot.Locked)
        {
            if (_shapeApplied is not null)
            {
                X11Shape.ClearInputRegion(handle.Handle);
                _shapeApplied = null;
                LinuxLog.Write("input region: the whole window again");
            }

            return;
        }

        // the lock button, and the status line's button while it is there (the command to copy, the new version): the rest lets the clicks through
        var parts = new List<(int X, int Y, int Width, int Height)>();
        foreach (Control button in new[] { _view.LockButton, _view.StatusButton })
        {
            if (button.IsEffectivelyVisible && button.TranslatePoint(new Point(0, 0), this) is Point at)
            {
                double scale = RenderScaling;
                parts.Add(((int)Math.Floor(at.X * scale) - 2, (int)Math.Floor(at.Y * scale) - 2, (int)Math.Ceiling(button.Bounds.Width * scale) + 4, (int)Math.Ceiling(button.Bounds.Height * scale) + 4));
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
            var lockPart = parts[0];
            LinuxLog.Write($"input region: the lock button alone ({lockPart.X},{lockPart.Y} {lockPart.Width}x{lockPart.Height} px){(parts.Count > 1 ? " and the status line's button" : "")}: the clicks go through the rest");
        }
        else if (!_shapeFailSaid)
        {
            _shapeFailSaid = true;
            LinuxLog.Write("input region: no X server to ask (a Wayland-only window): the locked meter still takes its clicks");
        }
    }

    private void Remember()
    {
        _spot.X = Position.X;
        _spot.Y = Position.Y;
        _spot.Width = Bounds.Width > 0 ? Bounds.Width : Width;
        _spot.Height = Bounds.Height > 0 ? Bounds.Height : Height;
        _spot.Topmost = Topmost;
        _spot.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _updateTimer.Stop();
        _engine.Dispose();
        base.OnClosed(e);
    }
}
