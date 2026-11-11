using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// An overlay with its own buttons (its gear and its padlock) and the settings panel its gear opens: the overlays on <see cref="MovableOverlay"/>, the timers
/// overlay and the pet information over a monster (11 November 2026, the owner: "every overlay should have a settings button and a lock button").
/// </summary>
public interface IOverlayQuick : IOverlayPlace
{
    /// <summary>The window title: the overlay's name in the settings (<see cref="OverlayPlaces"/>).</summary>
    string Title { get; }

    bool IsLocked { get; }

    /// <summary>Its settings are open beside it: its buttons stay.</summary>
    bool QuickOpen { get; set; }

    /// <summary>How opaque its background is (0 to 1), as its settings keep it.</summary>
    double QuickOpacity { get; }

    /// <summary>Keeps another opacity in its settings and shows it at once.</summary>
    void SaveQuickOpacity(double opacity);

    void ApplyLocked(bool locked);

    /// <summary>Switched off from its settings panel: off the screen now (its switch in the settings is already off).</summary>
    void QuickHide();
}

/// <summary>
/// The gear and the padlock in an overlay's top right corner. Shown while the cursor rests on the overlay (the game shows it), always while the overlay is
/// unlocked or its settings are open. A locked overlay takes the mouse only while the cursor is on the two buttons (<see cref="Input"/>, its window then turns
/// its click-through off for that moment), so the rest of it still lets every click through to the game. The buttons keep their size whatever the overlay's.
/// </summary>
internal sealed class OverlayChrome
{
    private readonly Window _window;
    private readonly IOverlayQuick _overlay;
    private readonly Action _inputChanged;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly TextBlock _padlock;

    public OverlayChrome(Window window, IOverlayQuick overlay, Action inputChanged)
    {
        _window = window;
        _overlay = overlay;
        _inputChanged = inputChanged;
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock Add(string glyph, string tip, Action click)
        {
            var t = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Width = 24, Height = 22, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(0, 5, 0, 0), Foreground = new SolidColorBrush(Ink), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip,
            };
            t.MouseEnter += (_, _) => t.Foreground = new SolidColorBrush(Hover);
            t.MouseLeave += (_, _) => t.Foreground = new SolidColorBrush(Ink);
            // the press is the button's own: it never starts a drag of the overlay
            t.MouseLeftButtonDown += (_, e) => e.Handled = true;
            t.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            bar.Children.Add(t);
            return t;
        }

        Add("", "Settings of this overlay: size, background, place", () => OverlayQuickWindow.Open(_overlay));
        _padlock = Add("", "", () => OverlayQuick.SetLocked(_overlay, !_overlay.IsLocked));
        ShowLocked(true);
        Element = new Border
        {
            Child = bar, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Padding = new Thickness(2, 0, 2, 0),
            CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x16, 0x13, 0x1C)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xDC, 0xB8, 0x77)), Visibility = Visibility.Hidden, LayoutTransform = _scale,
        };
        Panel.SetZIndex(Element, 50);
        _timer.Tick += (_, _) => Tick();
        window.IsVisibleChanged += (_, _) =>
        {
            if (window.IsVisible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                Set(false, false);
            }
        };
        window.Closed += (_, _) => _timer.Stop();
    }

    private static readonly Color Ink = Color.FromRgb(0xE8, 0xE2, 0xD4), Hover = Color.FromRgb(0xFF, 0xD2, 0x7A);

    /// <summary>The two buttons, to lay over the overlay's content (in a Grid with it).</summary>
    public Border Element { get; }

    /// <summary>The part of the overlay the cursor shows the buttons over (its frame).</summary>
    public FrameworkElement? Frame { get; set; }

    /// <summary>False while there are no buttons (a view in the app, an overlay with no room for them).</summary>
    public Func<bool> Enabled { get; set; } = () => true;

    /// <summary>False for an overlay with a padlock of its own (the map): only the gear.</summary>
    public bool WithLock { get; set; } = true;

    /// <summary>The cursor is on the buttons: a locked overlay takes the mouse for that moment.</summary>
    public bool Input { get; private set; }

    /// <summary>The overlay's own scale: the buttons are scaled back to their size.</summary>
    public void SetScale(double scale)
    {
        double back = double.IsFinite(scale) && scale > 0 ? 1 / scale : 1;
        _scale.ScaleX = back;
        _scale.ScaleY = back;
    }

    public void ShowLocked(bool locked)
    {
        _padlock.Text = locked ? "" : "";
        _padlock.ToolTip = locked ? "Locked: click to move and size it" : "Free to move and size: click to lock it in place";
    }

    /// <summary>For the pictures: the buttons as with the mouse on the overlay.</summary>
    public void ShowForPicture()
    {
        _padlock.Visibility = WithLock ? Visibility.Visible : Visibility.Collapsed;
        Element.Visibility = Visibility.Visible;
    }

    private void Tick()
    {
        if (!Enabled() || !_window.IsVisible || Frame is null)
        {
            Set(false, false);
            return;
        }

        _padlock.Visibility = WithLock ? Visibility.Visible : Visibility.Collapsed;
        bool overWindow = Frame.IsVisible && NativeCursor.Showing() && Under(Frame);
        bool overButtons = overWindow && Element.Visibility == Visibility.Visible && Under(Element);
        Set(overWindow || !_overlay.IsLocked || _overlay.QuickOpen, overButtons);
    }

    private void Set(bool shown, bool input)
    {
        Visibility v = shown ? Visibility.Visible : Visibility.Hidden;
        if (Element.Visibility != v)
        {
            Element.Visibility = v;
        }

        if (input != Input)
        {
            Input = input;
            _inputChanged();
        }
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
}
