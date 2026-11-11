using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>
/// Full size for the app's own windows (11 November 2026, a player: "there's no fullscreen" - the windows had only minimise and close). The windows draw their
/// own title bar (no system frame), and such a window, maximised, would cover the taskbar and reach past the screen's edges: the system is asked for the
/// size of a maximised window (WM_GETMINMAXINFO) and told the work area of the monitor the window is on.
/// </summary>
internal static class MaximizeFit
{
    /// <summary>The maximise button's lines: one square; while maximised, the two of "restore".</summary>
    public const string MaximizeGlyph = "M1.5,1.5 L10.5,1.5 L10.5,10.5 L1.5,10.5 Z";

    public const string RestoreGlyph = "M3.5,3.5 L3.5,1.5 L10.5,1.5 L10.5,8.5 L8.5,8.5 M1.5,3.5 L8.5,3.5 L8.5,10.5 L1.5,10.5 Z";

    /// <summary>Makes the window's maximised size the work area of its monitor (call before the window is shown).</summary>
    public static void Attach(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook(window);
            return;
        }

        window.SourceInitialized += (_, _) => Hook(window);
    }

    /// <summary>Maximised ⇄ normal.</summary>
    public static void Toggle(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static void Hook(Window window)
    {
        if (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is { } source)
        {
            source.AddHook(WndProc);
        }
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        try
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                mmi.MaxSize.X = info.Work.Right - info.Work.Left;
                mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                Marshal.StructureToPtr(mmi, lParam, false);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or EntryPointNotFoundException or DllNotFoundException)
        {
            // the system's own maximised size then
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved;
        public Point32 MaxSize;
        public Point32 MaxPosition;
        public Point32 MinTrackSize;
        public Point32 MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
