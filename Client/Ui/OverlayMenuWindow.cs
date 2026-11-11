using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The overlay menu over the game: a small Atreia button at the top of the screen; a click opens the menu under it with every overlay's switch and its
/// hotkey, and the buttons to move or hide the overlays, change the hotkeys and open the app (built by <see cref="MainWindow"/>, fresh at each opening).
/// Placed, scaled and locked like every overlay (<see cref="MovableOverlay"/>: "move the overlays" unlocks it with the others). Locked it lets every click
/// through but while the mouse rests on the button and the game shows the cursor, and neither the button nor the menu ever takes the keyboard from the game.
/// The menu closes with the button, its ✕, a click anywhere else, or when the game leaves the front.
/// </summary>
public sealed class OverlayMenuWindow : MovableOverlay
{
    private static readonly Color Gold = Color.FromRgb(0xD9, 0xB3, 0x6A);
    private readonly Border _pill;
    private readonly TextBlock _keys;
    private readonly Func<Action, FrameworkElement> _build;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private Window? _menu;
    private FrameworkElement? _menuBody;
    private bool _over;

    public OverlayMenuWindow(Func<Action, FrameworkElement> build)
        : base(OverlayPlaces.Menu, new Border(), Load, Save, SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 - 48, SystemParameters.WorkArea.Top + 2,
            () => (null, null), (_, _) => { }, OverlayEdgeMode.Uniform)
    {
        ShowChrome = false; // the Atreia button is itself the way to the overlays
        _build = build;
        (_pill, _keys) = Pill();
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        column.Children.Add(_keys);
        column.Children.Add(_pill);
        Body = new Border { Child = column }; // no background of its own: the overlays' opacity setting (which sets the body's) leaves the button as it is drawn
        // a click opens or closes the menu (10 November 2026, the owner: "moving the overlay button is really buggy/laggy, that should not be changeable the
        // way it is right now; just a settings button like Questlog's ... smooth to move"): it is moved, and sized at its corner, in the overlay editor, by
        // Windows' own move like every overlay
        _pill.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsLocked)
            {
                return; // being placed (the overlay editor): the frame drags it
            }

            e.Handled = true;
            if (_menu is null)
            {
                OpenMenu();
            }
            else
            {
                CloseMenu();
            }
        };
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                ShowKeys();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                CloseMenu();
                SetOver(false);
            }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            CloseMenu();
        };
        ShowKeys();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.OverlayMenuLeft, s.OverlayMenuTop, s.OverlayMenuScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.OverlayMenuLeft = left;
        s.OverlayMenuTop = top;
        s.OverlayMenuScale = scale;
        s.Save();
    }

    /// <summary>The keys of the overlay editor on the button; nothing when there are none.</summary>
    private void ShowKeys()
    {
        HotkeyBinding binding = HotkeyBinding.Parse(MeterSettings.Load().HotkeyMoveOverlays);
        _keys.Text = binding.IsEmpty ? "" : binding.ToString();
        _keys.Visibility = binding.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        _pill.ToolTip = "Overlay settings: click for the overlay menu (every overlay on or off, the hotkeys, the app)"
            + (binding.IsEmpty ? ". The overlay editor moves and sizes this button." : $". {binding} opens the overlay editor, where this button is moved and sized too.");
    }

    /// <summary>A hotkey changed (the overlay editor): the keys on the button and in an open menu again.</summary>
    public void ShowKeysAgain()
    {
        ShowKeys();
        RefreshMenu();
    }

    /// <summary>
    /// The button (10 November 2026, the owner: "just add a settings button like Questlog instead of the icon, and properly scale it"): a small dark square with
    /// the settings sliders in gold, the overlay editor's keys small above it. Drawn in shapes and text only, so it stays sharp at any size it is given.
    /// </summary>
    private static (Border Pill, TextBlock Keys) Pill()
    {
        var keys = new TextBlock
        {
            FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xE9, 0xCF)), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        };
        var pill = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x14, 0x15, 0x1A)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, Gold.R, Gold.G, Gold.B)),
            Child = new TextBlock
            {
                Text = "\uE9E9", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = new SolidColorBrush(Gold),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        TextOptions.SetTextFormattingMode(pill, TextFormattingMode.Ideal); // scaled up or down, the glyph is drawn anew rather than its pixels stretched
        return (pill, keys);
    }

    /// <summary>The mouse rests on the button (the game showing the cursor): it takes the clicks for that moment and lights up.</summary>
    private void SetOver(bool over)
    {
        if (over == _over)
        {
            return;
        }

        _over = over;
        TakeMouseWhileOver(over);
        Paint();
    }

    private void Paint()
    {
        bool lit = _over || _menu is not null;
        _pill.Background = new SolidColorBrush(lit ? Color.FromArgb(0xEE, 0x1E, 0x1F, 0x26) : Color.FromArgb(0xD0, 0x14, 0x15, 0x1A));
        _pill.BorderBrush = new SolidColorBrush(Color.FromArgb(lit ? (byte)0xFF : (byte)0x80, Gold.R, Gold.G, Gold.B));
    }

    private void Tick()
    {
        bool showing = NativeCursor.Showing();
        SetOver(IsLocked && showing && Under(_pill));
        if (_menu is null)
        {
            return;
        }

        // a click anywhere but on the menu or the button closes it (the menu never has the focus, so it hears of no other click by itself)
        if (!GameWindow.OverlayMayShow() || (GameWindow.MouseButtonDown() && !Under(_pill) && (_menuBody is null || !Under(_menuBody))))
        {
            CloseMenu();
        }
    }

    /// <summary>Whether the mouse is over <paramref name="element"/> (asked of Windows: a locked overlay gets no mouse events of its own).</summary>
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

    /// <summary>Opens the menu under the button (above it when the button sits low on the screen), at the button's scale.</summary>
    public void OpenMenu()
    {
        CloseMenu();
        _menuBody = _build(CloseMenu);
        // at the button's size, within reason: a button made big for a 4K screen should not open a menu twice the screen's height
        double scale = Math.Clamp(PlaceScale, 0.85, 1.35);
        var scaled = new Border { Child = _menuBody, LayoutTransform = new ScaleTransform(scale, scale) };
        _menu = new Window
        {
            Title = OverlayPlaces.Menu + " (open)", WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
            ShowInTaskbar = false, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Focusable = false,
            Content = scaled, Left = -32000, Top = -32000, Opacity = 0,
        };
        AltTabHidden.Apply(_menu);
        _menu.SourceInitialized += (_, _) =>
        {
            using var native = new NativeOverlay(_menu);
            native.SetNoActivate(true); // its switches are clicked without the game losing the keyboard
        };
        _menu.Show();
        _menu.UpdateLayout();
        Place(_menu);
        _menu.Opacity = 1;
        Paint();
    }

    private void Place(Window menu)
    {
        double w = menu.ActualWidth, h = menu.ActualHeight;
        var button = new Rect(Left, Top, ActualWidth, ActualHeight);
        Rect area = WindowPlacement.WorkAreaOf(button);
        double left = button.Left + (button.Width - w) / 2;
        double top = button.Bottom + 2;
        if (top + h > area.Bottom && button.Top - h - 2 >= area.Top)
        {
            top = button.Top - h - 2; // low on the screen: the menu opens upwards
        }

        menu.Left = Math.Max(area.Left + 4, Math.Min(left, area.Right - w - 4));
        menu.Top = Math.Max(area.Top, top);
    }

    public void CloseMenu()
    {
        if (_menu is null)
        {
            return;
        }

        Window menu = _menu;
        _menu = null;
        _menuBody = null;
        menu.Close();
        Paint();
    }

    /// <summary>The menu's content was changed (a switch flipped elsewhere, a hotkey): built anew in place.</summary>
    public void RefreshMenu()
    {
        if (_menu is not null)
        {
            OpenMenu();
        }
    }

    public bool MenuOpen => _menu is not null;

    /// <summary>Also the open menu goes back to the front of the topmost band (a full-screen game can come before it).</summary>
    public void RaiseAll()
    {
        RaiseToFront();
        if (_menu is { IsVisible: true } menu)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(menu).Handle);
        }
    }
}
