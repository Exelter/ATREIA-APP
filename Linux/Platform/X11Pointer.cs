using System.Runtime.InteropServices;
using Avalonia;

namespace AionDPS.Linux.Platform;

/// <summary>
/// Where the mouse is on the screen, in pixels, asked of the X server. The meter moves itself by its head with it (<see cref="Ui.MeterWindow"/>, as the
/// Windows meter's DragMove): the mouse positions Avalonia gives are relative to the window, which moves under them, and the window manager's own move
/// (_NET_WM_MOVERESIZE) needs a window manager and kept the next click from the meter.
/// </summary>
internal static class X11Pointer
{
    private static bool _unavailable;

    /// <summary>The mouse's place on the screen; false without an X server, or when the mouse is on another screen of it.</summary>
    internal static bool TryGet(out PixelPoint at)
    {
        at = default;
        if (_unavailable || X11Display.Get() is not IntPtr display)
        {
            return false;
        }

        try
        {
            if (XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out int x, out int y, out _, out _, out _) == 0)
            {
                return false;
            }

            at = new PixelPoint(x, y);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            return false;
        }
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX,
        out int windowY, out uint mask);
}
