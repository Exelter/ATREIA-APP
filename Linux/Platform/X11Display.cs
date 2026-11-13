using System.Runtime.InteropServices;

namespace AionDPS.Linux.Platform;

/// <summary>
/// This app's own connection to the X server (libX11), for what Avalonia does not offer: the input shape of a window (<see cref="X11Shape"/>) and where the
/// mouse is on the screen (<see cref="X11Pointer"/>). Opened when first needed, used on the UI thread only. Without an X server (or without libX11) there is
/// none, and those helpers do nothing.
/// </summary>
internal static class X11Display
{
    private static IntPtr _display;
    private static bool _unavailable;

    /// <summary>The connection, or null without an X server.</summary>
    internal static IntPtr? Get()
    {
        if (_unavailable)
        {
            return null;
        }

        if (_display != IntPtr.Zero)
        {
            return _display;
        }

        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _display = IntPtr.Zero;
        }

        if (_display == IntPtr.Zero)
        {
            _unavailable = true;
            return null;
        }

        return _display;
    }

    /// <summary>Sends what was asked to the X server now.</summary>
    internal static void Flush(IntPtr display) => XFlush(display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);
}
