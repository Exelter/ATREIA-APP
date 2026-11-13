using System.Runtime.InteropServices;

namespace AionDPS.Linux.Platform;

/// <summary>
/// Clicks through the meter to the game (the Windows app's click-through overlays, there done with WS_EX_TRANSPARENT). On X11 - and in a Wayland session for
/// the X11 windows the game and this app are there too (Xwayland) - a window has an input shape: the part of it that takes the mouse. A locked meter keeps only
/// its lock button in it, so the rest of the clicks go to the game below; unlocked, the whole window takes them again. Uses the SHAPE extension of the X
/// server through a connection of its own (libXext); without an X server (a Wayland-only window) nothing happens and the meter just takes its clicks.
/// </summary>
internal static class X11Shape
{
    private const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XRectangle
    {
        public short X;
        public short Y;
        public ushort Width;
        public ushort Height;
    }

    private static IntPtr _display;
    private static bool _unavailable;

    /// <summary>Only these parts of the window take the mouse (window coordinates in pixels); an empty list: none.</summary>
    internal static bool SetInputRegion(IntPtr xid, IReadOnlyList<(int X, int Y, int Width, int Height)> parts)
    {
        if (Display() is not IntPtr display || xid == IntPtr.Zero)
        {
            return false;
        }

        var rects = parts.Select(p => new XRectangle
        {
            X = (short)Math.Clamp(p.X, short.MinValue, short.MaxValue),
            Y = (short)Math.Clamp(p.Y, short.MinValue, short.MaxValue),
            Width = (ushort)Math.Clamp(p.Width, 0, ushort.MaxValue),
            Height = (ushort)Math.Clamp(p.Height, 0, ushort.MaxValue),
        }).ToArray();
        try
        {
            XShapeCombineRectangles(display, xid, ShapeInput, 0, 0, rects, rects.Length, ShapeSet, Unsorted);
            XFlush(display);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            return false;
        }
    }

    /// <summary>The whole window takes the mouse again.</summary>
    internal static void ClearInputRegion(IntPtr xid)
    {
        if (Display() is not IntPtr display || xid == IntPtr.Zero)
        {
            return;
        }

        try
        {
            XShapeCombineMask(display, xid, ShapeInput, 0, 0, IntPtr.Zero, ShapeSet);
            XFlush(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
        }
    }

    private static IntPtr? Display()
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

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int destKind, int xOff, int yOff, XRectangle[] rectangles, int count, int op, int ordering);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineMask(IntPtr display, IntPtr window, int destKind, int xOff, int yOff, IntPtr pixmap, int op);
}
