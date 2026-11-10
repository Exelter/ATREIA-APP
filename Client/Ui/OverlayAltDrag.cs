using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// Every overlay moves with the mouse while Alt is held (10 November 2026, the owner: "make every overlay movable with your mouse, so people can move stuff
/// around"). In the game Alt brings the cursor up; with Alt held, the overlay under the cursor glows gold and takes the mouse, and a drag moves it where it is
/// let go, kept like a move in the layout editor. Let go of Alt and it lets every click through to the game again. "Move overlays" (its hotkey) still unlocks
/// them all at once, with the handles to size them. Asked of Windows by a short timer: a locked overlay is click-through and hears no mouse of its own.
/// </summary>
internal static class OverlayAltDrag
{
    private const int GwlExStyle = -20, WsExTransparent = 0x20, WsExNoActivate = 0x08000000, VkMenu = 0x12;

    private sealed record Entry(WeakReference<Window> Window, Func<bool> Allowed, Action? Reapply);

    private static readonly List<Entry> Entries = new();
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
    private static readonly Effect Glow = Frozen(new DropShadowEffect { Color = Color.FromRgb(0xDC, 0xB8, 0x77), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.95 });

    private static Entry? _grabbed;
    private static int _savedStyle;
    private static Effect? _savedEffect;
    private static bool _savedHit = true;
    private static bool _dragging, _buttonWas;
    private static (int X, int Y) _start;
    private static (double Left, double Top) _from;
    private static double _dpi = 1;
    private static DateTime _settingsAt = DateTime.MinValue;
    private static bool _enabled = true;

    /// <summary>Shown when a drag ended: a note of where it went is not needed, but the layout editor and the docking hear of the move through <see cref="IOverlayPlace"/>.</summary>
    public static event Action<Window>? Moved;

    /// <summary>
    /// Lets <paramref name="window"/> be moved with Alt held. <paramref name="allowed"/>: whether it may be moved now (the meter locked in place may not).
    /// <paramref name="reapply"/>: how the window sets its own click-through again once Alt is let go (else its window style is put back as it was).
    /// </summary>
    public static void Register(Window window, Func<bool>? allowed = null, Action? reapply = null)
    {
        Entries.Add(new Entry(new WeakReference<Window>(window), allowed ?? (() => true), reapply));
        if (!Timer.IsEnabled)
        {
            Timer.Tick += (_, _) => Tick();
            Timer.Start();
        }
    }

    private static void Tick()
    {
        if (DateTime.UtcNow - _settingsAt > TimeSpan.FromSeconds(2))
        {
            _settingsAt = DateTime.UtcNow;
            _enabled = MeterSettings.Load().AltDragOverlays;
        }

        bool button = NativeCursor.LeftButtonHeld();
        if (_dragging)
        {
            if (_grabbed is not null && _grabbed.Window.TryGetTarget(out Window? moving) && button && NativeCursor.TryGet(out int mx, out int my))
            {
                moving.Left = _from.Left + (mx - _start.X) / _dpi;
                moving.Top = _from.Top + (my - _start.Y) / _dpi;
                return;
            }

            _dragging = false;
            if (_grabbed is not null && _grabbed.Window.TryGetTarget(out Window? moved))
            {
                if (moved is IOverlayPlace place)
                {
                    place.PlaceAt(moved.Left, moved.Top, save: true);
                }

                Moved?.Invoke(moved);
            }
        }

        bool alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        if (!_enabled || !alt || !NativeCursor.Showing())
        {
            Release();
            _buttonWas = button;
            return;
        }

        Entry? under = button && _grabbed is not null ? _grabbed : EntryUnderCursor();
        if (!ReferenceEquals(under, _grabbed))
        {
            Release();
            if (under is not null)
            {
                Grab(under);
            }
        }

        // a press on the glowing overlay starts the drag (the press itself went to the overlay, not to the game)
        if (button && !_buttonWas && _grabbed is not null && _grabbed.Window.TryGetTarget(out Window? target) && NativeCursor.TryGet(out int x, out int y))
        {
            _dragging = true;
            _start = (x, y);
            _from = (target.Left, target.Top);
            _dpi = PresentationSource.FromVisual(target)?.CompositionTarget?.TransformToDevice.M11 is double d and > 0 ? d : 1;
        }

        _buttonWas = button;
    }

    /// <summary>The overlay under the cursor: of those shown and allowed to move, the smallest that holds the cursor (a small one on a bigger one wins).</summary>
    private static Entry? EntryUnderCursor()
    {
        if (!NativeCursor.TryGet(out int x, out int y))
        {
            return null;
        }

        Entry? best = null;
        long bestArea = long.MaxValue;
        Entries.RemoveAll(e => !e.Window.TryGetTarget(out _));
        foreach (Entry entry in Entries)
        {
            if (!entry.Window.TryGetTarget(out Window? window) || !window.IsVisible || window is IOverlayPlace { PlaceIsView: true } || !entry.Allowed())
            {
                continue;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out Rect32 r) || x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom)
            {
                continue;
            }

            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area < bestArea)
            {
                best = entry;
                bestArea = area;
            }
        }

        return best;
    }

    private static void Grab(Entry entry)
    {
        if (!entry.Window.TryGetTarget(out Window? window))
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _grabbed = entry;
        _savedStyle = GetWindowLong(handle, GwlExStyle);
        // takes the mouse (no longer click-through) but never the keyboard: the game stays the active window
        SetWindowLong(handle, GwlExStyle, (_savedStyle & ~WsExTransparent) | WsExNoActivate);
        if (window.Content is UIElement content)
        {
            _savedEffect = content.Effect;
            _savedHit = content.IsHitTestVisible;
            content.Effect = Glow;
            content.IsHitTestVisible = false; // its buttons don't react to the press that moves it
        }
    }

    private static void Release()
    {
        if (_dragging || _grabbed is null)
        {
            return;
        }

        Entry entry = _grabbed;
        _grabbed = null;
        if (!entry.Window.TryGetTarget(out Window? window))
        {
            return;
        }

        if (window.Content is UIElement content)
        {
            content.Effect = _savedEffect;
            content.IsHitTestVisible = _savedHit;
        }

        _savedEffect = null;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (entry.Reapply is { } reapply)
        {
            reapply();
        }
        else if (handle != IntPtr.Zero)
        {
            int now = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, (now & ~(WsExTransparent | WsExNoActivate)) | (_savedStyle & (WsExTransparent | WsExNoActivate)));
        }
    }

    private static Effect Frozen(Effect effect)
    {
        effect.Freeze();
        return effect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
