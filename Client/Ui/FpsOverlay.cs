using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace AionDPS.Ui;

/// <summary>
/// The game's frames per second (10 November 2026, the owner: "add an FPS counter that's movable"), counted from Windows' own frame timing: the DXGI provider's
/// Present events (ETW), the way PresentMon and the Xbox Game Bar count them. Nothing touches the game: Windows tells of every frame the game hands to the screen,
/// and the frames of the game's process are counted. Windows lets a program listen only when it runs as administrator or its user is in the "Performance Log
/// Users" group (the Game Bar asks for the same); without that the overlay says so and offers to add the user to the group once.
/// </summary>
internal static class FpsMeter
{
    public enum State
    {
        Off,
        Starting,
        Running,
        NoAccess,
        Failed,
    }

    /// <summary>Microsoft-Windows-DXGI: Present_Start (42) and PresentMultiplaneOverlay_Start (55) are a frame each.</summary>
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const string SessionName = "AtreiaApp-FPS";
    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static readonly Queue<double> Frames = new(); // the game's frames, ms on the session's clock
    private static TraceEventSession? _session;
    private static Thread? _thread;
    private static HashSet<int> _game = new();
    private static DateTime _gameAt = DateTime.MinValue;

    /// <summary>When the newest of the game's frames was handed to the counter (by this PC's clock), and when the session was last asked to hand them over.</summary>
    private static DateTime _lastFrameArrived = DateTime.MinValue;
    private static DateTime _flushedAt = DateTime.MinValue;

    /// <summary>
    /// How long without a frame of the game before the counter says it waits for the game. Windows hands the frames over in batches (each second, or when a
    /// batch is full), so the newest one can be a second or two old when it comes.
    /// </summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(3);

    public static State Status { get; private set; } = State.Off;

    public static string? Error { get; private set; }

    public static void Start()
    {
        lock (Gate)
        {
            if (_thread is not null)
            {
                return;
            }

            Status = State.Starting;
            Error = null;
            _thread = new Thread(Run) { IsBackground = true, Name = "FPS counter" };
            _thread.Start();
        }
    }

    public static void Stop()
    {
        TraceEventSession? session;
        lock (Gate)
        {
            session = _session;
            _session = null;
            _thread = null;
            Frames.Clear();
            Status = State.Off;
        }

        try
        {
            session?.Dispose(); // ends Process() on the thread
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // already gone
        }
    }

    private static void Run()
    {
        try
        {
            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            lock (Gate)
            {
                _session = session;
            }

            session.EnableProvider(Dxgi, TraceEventLevel.Informational);
            session.Source.AllEvents += OnEvent;
            Status = State.Running;
            session.Source.Process(); // until the session is disposed
        }
        catch (UnauthorizedAccessException)
        {
            Status = State.NoAccess;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // access denied comes as 0x80070005 from some Windows versions
            bool denied = ex.HResult == unchecked((int)0x80070005) || ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 };
            Status = denied ? State.NoAccess : State.Failed;
            Error = ex.Message;
        }
        finally
        {
            lock (Gate)
            {
                _thread = null;
            }
        }
    }

    private static void OnEvent(TraceEvent e)
    {
        if (e.ProviderGuid != Dxgi || (int)e.ID is not (42 or 55))
        {
            return;
        }

        if (DateTime.UtcNow - _gameAt > TimeSpan.FromSeconds(5))
        {
            _gameAt = DateTime.UtcNow;
            var pids = new HashSet<int>();
            foreach (Process p in Process.GetProcessesByName(Aion2.Capture.GameConnections.ProcessName))
            {
                pids.Add(p.Id);
                p.Dispose();
            }

            _game = pids;
        }

        if (!_game.Contains(e.ProcessID))
        {
            return;
        }

        double at = e.TimeStampRelativeMSec;
        lock (Gate)
        {
            _lastFrameArrived = DateTime.UtcNow;
            Frames.Enqueue(at);
            while (Frames.Count > 0 && at - Frames.Peek() > Kept.TotalMilliseconds)
            {
                Frames.Dequeue();
            }
        }
    }

    /// <summary>
    /// The frames per second over the last second and the 1 % low over the last ten (the rate of the slowest frames: 1000 / the 99th percentile frame time).
    /// Nulls while the game hands over no frames (not running, minimized).
    /// </summary>
    public static (double? Fps, double? Low) Read()
    {
        double[] frames;
        lock (Gate)
        {
            frames = Frames.ToArray();
        }

        if (frames.Length < 3)
        {
            return (null, null);
        }

        double last = frames[^1];
        // Windows stops telling of frames when the game is minimized, and the queue then only holds old ones. Told by when the newest one arrived, not by
        // comparing its time stamp with the clock (10 November 2026, a tester: "the FPS counter keeps interchanging" between the rate and "waiting for the
        // game"): the frames come in batches, each up to a second or more behind, and the session's start time is not exactly on the frames' clock, so on
        // some PCs the newest frame looked older than 1.5 s every other moment while the game ran at 180 frames a second
        DateTime arrived;
        lock (Gate)
        {
            arrived = _lastFrameArrived;
        }

        if (DateTime.UtcNow - arrived > Quiet)
        {
            return (null, null);
        }

        AskForFrames();

        // the frames of the last second: (frames - 1) spans over the time from the first to the last of them
        double from = last - 1000;
        double[] second = frames.Where(f => f > from).ToArray();
        double? fps = second.Length >= 2 && second[^1] > second[0] ? (second.Length - 1) * 1000.0 / (second[^1] - second[0]) : null;
        var gaps = new List<double>(frames.Length);
        for (int i = 1; i < frames.Length; i++)
        {
            double gap = frames[i] - frames[i - 1];
            if (gap is > 0 and < 2000)
            {
                gaps.Add(gap);
            }
        }

        double? low = null;
        if (gaps.Count >= 20)
        {
            gaps.Sort();
            double slow = gaps[(int)Math.Min(gaps.Count - 1, Math.Ceiling(gaps.Count * 0.99) - 1)];
            low = slow > 0 ? 1000.0 / slow : null;
        }

        return (fps, low);
    }

    /// <summary>Asks Windows for the frames it holds (at most twice a second), so the rate shown is no more than a moment behind the game.</summary>
    private static void AskForFrames()
    {
        if (DateTime.UtcNow - _flushedAt < TimeSpan.FromMilliseconds(500) || _session is not { } session)
        {
            return;
        }

        _flushedAt = DateTime.UtcNow;
        try
        {
            session.Flush();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ObjectDisposedException
            or System.IO.IOException or ArgumentException)
        {
            // the session is ending: the next reading says so
        }
    }

    /// <summary>
    /// Adds the user to "Performance Log Users" (the group's well-known id, so it works in every language of Windows), as administrator: Windows asks first. The
    /// user has to sign out and in again (or restart) before Windows lets the counter listen.
    /// </summary>
    public static bool AskForAccess()
    {
        string member = (Environment.UserDomainName + "\\" + Environment.UserName).Replace("'", "''", StringComparison.Ordinal);
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"Add-LocalGroupMember -SID 'S-1-5-32-559' -Member '{member}' -ErrorAction SilentlyContinue\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            return p is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false; // the Windows question was answered no
        }
    }
}

/// <summary>The FPS counter over the game: a small pill with the frames per second, coloured by how smooth it is, and the 1 % low under it.</summary>
internal sealed class FpsOverlayWindow : MovableOverlay
{
    private readonly StackPanel _content = new() { MinWidth = 86 };
    private readonly System.Windows.Threading.DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private Border? _allow;
    private string _shown = "";

    public FpsOverlayWindow() : base(OverlayPlaces.Fps, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 24, SystemParameters.WorkArea.Top + 24, LoadSize,
        SaveSize, OverlayEdgeMode.Uniform)
    {
        Body = OverlaySkin.Panel(_content, new Thickness(10, 6, 12, 7));
        // the button that asks Windows for the access takes the mouse while the cursor rests on it (a locked overlay lets every click through otherwise)
        _hover.Tick += (_, _) => TakeMouseWhileOver(_allow is { IsVisible: true } button && IsLocked && NativeCursor.Showing() && Under(button));
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _hover.Start();
            }
            else
            {
                _hover.Stop();
            }
        };
        Closed += (_, _) => _hover.Stop();
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.FpsOverlayWidth, s.FpsOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.FpsOverlayWidth = width;
        s.FpsOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.FpsOverlayLeft, s.FpsOverlayTop, s.FpsOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.FpsOverlayLeft = left;
        s.FpsOverlayTop = top;
        s.FpsOverlayScale = scale;
        s.Save();
    }

    private static bool Under(FrameworkElement element)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null || !NativeCursor.TryGet(out int x, out int y))
        {
            return false;
        }

        try
        {
            Point p = element.PointFromScreen(new Point(x, y));
            return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The counter: the frames per second big, coloured (green from 60, amber from 30, red under), "FPS" beside it, the 1 % low under it.</summary>
    public void Render(double? fps, double? low, FpsMeter.State state)
    {
        string key = $"{fps:0}|{low:0}|{state}";
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _content.Children.Clear();
        _allow = null;
        if (state == FpsMeter.State.NoAccess)
        {
            var head = new TextBlock { Text = "FPS", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(OverlaySkin.Gold) };
            _content.Children.Add(head);
            var why = new TextBlock { Text = "Windows has to allow the counter once.", FontSize = 10.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 180, Margin = new Thickness(0, 2, 0, 5) };
            why.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _content.Children.Add(why);
            var text = new TextBlock { Text = "Allow", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x16, 0x10)) };
            _allow = new Border
            {
                Child = text, Padding = new Thickness(10, 2, 10, 3), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(OverlaySkin.Gold), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Adds you to Windows' \"Performance Log Users\" group (Windows asks first), as the Xbox Game Bar does for its FPS counter. Sign out and in again afterwards.",
            };
            _allow.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                bool asked = FpsMeter.AskForAccess();
                _content.Children.Remove(_allow);
                var then = new TextBlock { Text = asked ? "Sign out of Windows and in again, then it counts." : "Windows said no: the counter stays off.", FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 180, Foreground = new SolidColorBrush(asked ? OverlaySkin.Good : OverlaySkin.Warn) };
                _content.Children.Add(then);
                _shown = "asked";
            };
            _content.Children.Add(_allow);
            return;
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        Color colour = fps is not double f ? Color.FromRgb(0x9A, 0x9A, 0xA4) : f >= 60 ? OverlaySkin.Good : f >= 30 ? OverlaySkin.Warn : OverlaySkin.Bad;
        var number = new TextBlock
        {
            Text = fps is double v ? Math.Round(v).ToString("0", CultureInfo.CurrentCulture) : state == FpsMeter.State.Failed ? "!" : "–", FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Bottom,
            ToolTip = state == FpsMeter.State.Failed ? "Windows' frame timing can't be read: " + FpsMeter.Error : null,
        };
        number.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        line.Children.Add(number);
        line.Children.Add(new TextBlock { Text = "FPS", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(OverlaySkin.Gold), Margin = new Thickness(5, 0, 0, 4),
            VerticalAlignment = VerticalAlignment.Bottom });
        _content.Children.Add(line);
        var sub = new TextBlock
        {
            Text = low is double l ? "1% low " + Math.Round(l).ToString("0", CultureInfo.CurrentCulture) : fps is null ? (state == FpsMeter.State.Running ? "waiting for the game" : "starting…") : "1% low …",
            FontSize = 10, Margin = new Thickness(0, -2, 0, 0),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _content.Children.Add(sub);
    }
}

/// <summary>Runs the FPS counter: once every half second it reads the settings and the count, and shows the overlay only while the game is in front (or while
/// the overlays are unlocked so it can be placed). Windows' frame timing is listened to only while the counter is on.</summary>
internal sealed class FpsOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private FpsOverlayWindow? _overlay;
    private DateTime _settingsAt = DateTime.MinValue;
    private MeterSettings? _settings;

    public FpsOverlayController()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        FpsMeter.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if (_settings is null || DateTime.UtcNow - _settingsAt > TimeSpan.FromSeconds(1))
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.ShowFpsOverlay)
        {
            _overlay?.ShowOverlay(false);
            if (FpsMeter.Status != FpsMeter.State.Off)
            {
                FpsMeter.Stop();
            }

            return;
        }

        if (FpsMeter.Status == FpsMeter.State.Off)
        {
            FpsMeter.Start();
        }

        bool locked = _settings.FpsOverlayLocked;
        _overlay ??= new FpsOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if (!GameWindow.OverlayMayShow())
        {
            _overlay.ShowOverlay(false);
            return;
        }

        (double? fps, double? low) = FpsMeter.Read();
        // while it is placed and the game hands over no frames, a sample fills it in its real size
        _overlay.Render(fps ?? (!locked ? 144 : null), low ?? (!locked && fps is null ? 118 : null), FpsMeter.Status);
        _overlay.ShowOverlay(true);
    }
}
