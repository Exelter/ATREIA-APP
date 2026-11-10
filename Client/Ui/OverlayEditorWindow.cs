using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// The overlay editor over the game (10 November 2026, the owner, with Questlog's "Overlay controls" as the example: "a simple key to open an overlay editor
/// that allows everyone to add stuff to their screen based on what we have, turn it off and on, with a preview of what it will look like"). The window is
/// the frame - see-through like the meter, dragged by its head, closed with ✕, Esc or Done - and takes the keyboard only to read a new hotkey
/// (<see cref="ReadKeys"/>). What it holds is built by <see cref="MainWindow"/> (MainWindow.OverlayEditor.cs). Its place is kept.
/// </summary>
public sealed class OverlayEditorWindow : Window
{
    private readonly Border _frame;
    private readonly Border _host = new();
    private Action<HotkeyBinding?>? _reading;

    /// <summary>How wide the editor is (two tiles side by side with their keys).</summary>
    public const double EditorWidth = 400;

    public OverlayEditorWindow(MeterSettings settings)
    {
        Title = "Overlay editor · " + Brand.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = ThemeManager.UiFont;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // clicked without the game losing the keyboard (the overlays that show only while the game is in front stay up); taken only to read a new hotkey
        ShowActivated = false;
        SourceInitialized += (_, _) => NoActivate(true);
        AltTabHidden.Apply(this);
        _frame = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = _host, Width = EditorWidth,
            BorderBrush = InGameGlass.Edge(settings), Background = InGameGlass.Background(settings),
        };
        InGameGlass.Apply(_frame);
        Content = _frame;
        Loaded += (_, _) => OverlayMotion.Appear(_frame);
        PreviewKeyDown += OnKey;
        LocationChanged += (_, _) =>
        {
            if (IsLoaded && WindowPlacement.CanSave && double.IsFinite(Left) && double.IsFinite(Top))
            {
                MeterSettings s = MeterSettings.Load();
                s.OverlayEditorLeft = Left;
                s.OverlayEditorTop = Top;
                s.Save();
            }
        };
    }

    /// <summary>What the editor shows (built anew when something in it changed).</summary>
    public FrameworkElement? Body
    {
        get => _host.Child as FrameworkElement;
        set => _host.Child = value;
    }

    /// <summary>The glass again at the overlays' opacity (the editor's own slider moved it).</summary>
    public void Restyle(MeterSettings settings)
    {
        _frame.Background = InGameGlass.Background(settings);
        _frame.BorderBrush = InGameGlass.Edge(settings);
    }

    /// <summary>The frame as a picture would show it (the pictures of the build).</summary>
    internal Border Frame => _frame;

    /// <summary>Where it opens: where it was left, else at the right of <paramref name="area"/> near its top, like the editor it follows.</summary>
    public void Place(Rect area)
    {
        MeterSettings s = MeterSettings.Load();
        UpdateLayout();
        double w = _frame.ActualWidth > 0 ? _frame.ActualWidth : EditorWidth, h = _frame.ActualHeight > 0 ? _frame.ActualHeight : 560;
        Rect place = s.OverlayEditorLeft is double left && s.OverlayEditorTop is double top
            ? WindowPlacement.Resolve(new Rect(left, top, w, h), out _)
            : new Rect(area.Right - w - 24, area.Top + 72, w, h);
        Rect room = WindowPlacement.WorkAreaOf(place);
        Left = Math.Clamp(place.X, room.Left, Math.Max(room.Left, room.Right - w));
        Top = Math.Clamp(place.Y, room.Top, Math.Max(room.Top, room.Bottom - Math.Min(h, room.Height)));
    }

    /// <summary>To another screen (the editor's screen buttons): at the right of <paramref name="area"/> near its top, kept there.</summary>
    public void PlaceOn(Rect area)
    {
        double w = _frame.ActualWidth > 0 ? _frame.ActualWidth : EditorWidth, h = _frame.ActualHeight > 0 ? _frame.ActualHeight : 560;
        Left = area.Right - w - 24;
        Top = Math.Clamp(area.Top + 72, area.Top, Math.Max(area.Top, area.Bottom - Math.Min(h, area.Height)));
    }

    /// <summary>The head: dragged to move the editor.</summary>
    public void DragFrom(UIElement head)
    {
        head.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed || e.Handled)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // let go before the move began
            }
        };
    }

    /// <summary>
    /// Reads the next keys pressed as a hotkey: <paramref name="done"/> hears the binding, an empty one for Backspace or Delete (no hotkey), or null for Esc.
    /// The window takes the keyboard for it (the game gets it back with the editor's close or a click in the game).
    /// </summary>
    public void ReadKeys(Action<HotkeyBinding?> done)
    {
        _reading = answer =>
        {
            NoActivate(true);
            done(answer);
        };
        NoActivate(false);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void NoActivate(bool on)
    {
        if (new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero)
        {
            return;
        }

        using var native = new NativeOverlay(this);
        native.SetNoActivate(on);
    }

    public bool Reading => _reading is not null;

    /// <summary>Stops reading keys without an answer (another key cap was clicked, the editor closes).</summary>
    public void StopReading()
    {
        Action<HotkeyBinding?>? reading = _reading;
        _reading = null;
        reading?.Invoke(null);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_reading is null)
        {
            if (key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }

            return;
        }

        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return; // a modifier alone: waiting for the key
        }

        Action<HotkeyBinding?> done = _reading;
        _reading = null;
        if (key == Key.Escape)
        {
            done(null);
            return;
        }

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            done(default);
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        var binding = new HotkeyBinding(mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift), win, key);
        // a typing key alone would be taken from every program: Parse refuses it, and the reading goes on
        if (HotkeyBinding.Parse(binding.ToString()).IsEmpty)
        {
            _reading = done;
            return;
        }

        done(binding);
    }

    /// <summary>
    /// The faint grid over the screen while the editor is open (as in the editor it follows): drawn on a click-through window, so the overlays under it are
    /// dragged as before.
    /// </summary>
    public static Window Grid(Rect area)
    {
        var line = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
        line.Freeze();
        var tile = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 48, 48), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(null, new Pen(line, 1), new RectangleGeometry(new Rect(0, 0, 48, 48))),
        };
        tile.Freeze();
        var grid = new Window
        {
            Title = "Atreia overlay grid", WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize, IsHitTestVisible = false, Focusable = false, Left = area.Left, Top = area.Top, Width = area.Width,
            Height = area.Height, Content = new Rectangle { Fill = tile, IsHitTestVisible = false },
        };
        AltTabHidden.Apply(grid);
        grid.SourceInitialized += (_, _) =>
        {
            using var native = new NativeOverlay(grid);
            native.SetClickThrough(true);
            native.SetNoActivate(true);
        };
        return grid;
    }
}
