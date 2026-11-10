using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>One page of an in-game panel: its id (as the app's pages have them), its name and its glyph.</summary>
public sealed record GamePanelTab(string Id, string Label, string Glyph);

/// <summary>
/// An in-game panel (10 November 2026, the owner: "every part of the app usable from the meter, so users don't have to go from one tab to another"): one of the
/// app's pages over the game, in the meter's look, the way the player window shows a character. Opened from the meter's launcher. A group of pages that belong
/// together (Characters, Main stats, CP calculator, Journey) are the panel's tabs. Dragged by its header, sized at its edges or the grip, closed with Esc or ×;
/// its place, size and tab are kept per panel. "Open in the app" shows the same page in the app's window.
/// </summary>
public sealed class GamePanelWindow : Window
{
    private static readonly Dictionary<string, GamePanelWindow> Opened = new(StringComparer.Ordinal);
    private static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);

    private readonly string _group;
    private readonly IReadOnlyList<GamePanelTab> _tabs;
    private readonly Func<string, ToolPage?> _build;
    private readonly Action<string>? _openInApp;
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly Border _host = new() { ClipToBounds = true };
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private ToolPage? _page;
    private string _tab;

    private GamePanelWindow(string group, string title, string glyph, IReadOnlyList<GamePanelTab> tabs, string tab, Func<string, ToolPage?> build, Action<string>? openInApp, Rect place)
    {
        _group = group;
        _tabs = tabs;
        _tab = tab;
        _build = build;
        _openInApp = openInApp;
        Title = title + " · " + Brand.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        MinWidth = 520;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = place.X;
        Top = place.Y;
        Width = place.Width;
        Height = place.Height;
        FontFamily = ThemeManager.UiFont;
        // the edges size the window (the frame is drawn by the panel itself)
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(7), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        AltTabHidden.Apply(this);
        Content = Frame(title, glyph);
        ShowTab(tab);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Persist();
        };
        SizeChanged += (_, _) => Changed();
        LocationChanged += (_, _) => Changed();
        Closing += (_, _) =>
        {
            _save.Stop();
            Persist();
        };
        Closed += (_, _) =>
        {
            Opened.Remove(_group);
            _page?.Cleanup?.Invoke();
            _page = null;
        };
    }

    /// <summary>
    /// Shows the panel <paramref name="group"/> on the page <paramref name="tab"/> (null: the page it was left on); a panel already open comes up on that page.
    /// The first time it opens beside <paramref name="near"/> (the meter), as wide as the page wants and the screen allows.
    /// </summary>
    public static void Open(string group, string title, string glyph, IReadOnlyList<GamePanelTab> tabs, string? tab, Func<string, ToolPage?> build, Action<string>? openInApp,
        Rect? near)
    {
        if (tabs.Count == 0)
        {
            return;
        }

        if (Opened.TryGetValue(group, out GamePanelWindow? shown))
        {
            if (tab is not null && tab != shown._tab && shown._tabs.Any(t => t.Id == tab))
            {
                shown.ShowTab(tab);
            }

            shown.Activate();
            return;
        }

        MeterSettings settings = MeterSettings.Load();
        settings.GamePanels.TryGetValue(group, out GamePanelPlace? kept);
        string first = tab is not null && tabs.Any(t => t.Id == tab) ? tab
            : kept?.Tab is { } last && tabs.Any(t => t.Id == last) ? last : tabs[0].Id;
        Rect place;
        if (kept is { Width: > 0, Height: > 0 })
        {
            place = WindowPlacement.Resolve(new Rect(kept.Left, kept.Top, kept.Width, kept.Height), out _);
        }
        else
        {
            Rect anchor = near ?? WindowPlacement.Primary();
            Rect area = WindowPlacement.WorkAreaOf(anchor);
            double width = Math.Clamp(Math.Min(ToolsWindow.DefaultWidth(first), 1180), 560, Math.Max(560, area.Width * 0.9));
            double height = Math.Max(380, Math.Min(760, area.Height * 0.82));
            // beside the meter, on the side with room, at the meter's height
            double left = anchor.Right + 12 + width <= area.Right ? anchor.Right + 12 : Math.Max(area.Left + 12, anchor.Left - width - 12);
            double top = Math.Clamp(anchor.Top, area.Top + 12, Math.Max(area.Top + 12, area.Bottom - height - 12));
            place = new Rect(left, top, width, height);
        }

        Rect room = WindowPlacement.WorkAreaOf(place);
        place = new Rect(place.X, place.Y, Math.Min(place.Width, room.Width), Math.Min(place.Height, room.Height));
        var window = new GamePanelWindow(group, title, glyph, tabs, first, build, openInApp, place);
        Opened[group] = window;
        window.Show();
        window.Activate();
    }

    /// <summary>A panel drawn for the pictures (never shown): its frame with the page <paramref name="tab"/> in it.</summary>
    internal static FrameworkElement Picture(string title, string glyph, IReadOnlyList<GamePanelTab> tabs, string tab, Func<string, ToolPage?> build, bool openInApp)
    {
        var window = new GamePanelWindow("picture", title, glyph, tabs, tab, build, openInApp ? _ => { } : null, new Rect(0, 0, 900, 700));
        var frame = (FrameworkElement)window.Content;
        window.Content = null;
        return frame;
    }

    /// <summary>Whether a panel is open (the launcher marks its tile).</summary>
    public static bool IsOpen(string group) => Opened.ContainsKey(group);

    /// <summary>Every panel closed (the meter is ending).</summary>
    public static void CloseAll()
    {
        foreach (GamePanelWindow window in Opened.Values.ToList())
        {
            window.Close();
        }
    }

    // ------------------------------------------------------------------ the frame

    private Border Frame(string title, string glyph)
    {
        var header = new Grid { Margin = new Thickness(14, 10, 8, 6), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // let go before the move began
                }
            }
        };
        var mark = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x24, Gold.R, Gold.G, Gold.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, Gold.R, Gold.G, Gold.B)),
            Child = Glyph(glyph, 13, new SolidColorBrush(Gold)),
        };
        header.Children.Add(mark);
        var name = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        Grid.SetColumn(name, 1);
        header.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_openInApp is not null)
        {
            buttons.Children.Add(IconButton("", "Open this page in the app's window", () => _openInApp(_tab)));
        }

        buttons.Children.Add(IconButton("", "Close (Esc)", Close));
        Grid.SetColumn(buttons, 2);
        header.Children.Add(buttons);

        var top = new StackPanel();
        top.Children.Add(header);
        if (_tabs.Count > 1)
        {
            _tabStrip.Margin = new Thickness(14, 0, 14, 8);
            top.Children.Add(_tabStrip);
        }

        top.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)) });
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(_host);

        // the grip in the corner: the window's edges size it as well
        var grip = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Margin = new Thickness(0, 0, 5, 4), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(45), Foreground = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
        };
        WindowChrome.SetIsHitTestVisibleInChrome(grip, false);
        var layers = new Grid();
        layers.Children.Add(dock);
        layers.Children.Add(grip);
        return new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0), Child = layers,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            // never see-through: every part takes the mouse over the game
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x14, 0x12, 0x18)),
        };
    }

    private void DrawTabs()
    {
        _tabStrip.Children.Clear();
        foreach (GamePanelTab tab in _tabs)
        {
            bool on = tab.Id == _tab;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Glyph(tab.Glyph, 11, on ? new SolidColorBrush(Gold) : Muted()));
            ((TextBlock)line.Children[0]).Margin = new Thickness(0, 1, 7, 0);
            var text = new TextBlock { Text = tab.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.OverlayText" : "Brush.TextMuted");
            line.Children.Add(text);
            var rest = new SolidColorBrush(on ? Color.FromArgb(0x2A, Gold.R, Gold.G, Gold.B) : Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
            var pill = new Border
            {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(11, 4, 11, 5), Margin = new Thickness(0, 0, 5, 0), BorderThickness = new Thickness(1), Child = line,
                BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x99, Gold.R, Gold.G, Gold.B) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Background = rest,
                Cursor = on ? Cursors.Arrow : Cursors.Hand,
            };
            if (!on)
            {
                string id = tab.Id;
                pill.MouseEnter += (_, _) => pill.Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
                pill.MouseLeave += (_, _) => pill.Background = rest;
                pill.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    ShowTab(id);
                };
            }

            _tabStrip.Children.Add(pill);
        }
    }

    /// <summary>The panel on one of its pages: the one shown goes (its timers stop), the other is built.</summary>
    private void ShowTab(string id)
    {
        _page?.Cleanup?.Invoke();
        _page = null;
        _host.Child = null;
        _tab = id;
        DrawTabs();
        ToolPage? page = _build(id);
        if (page?.Content is FrameworkElement content)
        {
            if (content.Parent is Panel parent)
            {
                parent.Children.Remove(content);
            }
            else if (content.Parent is Decorator decorator)
            {
                decorator.Child = null;
            }

            _page = page;
            _host.Child = content;
        }
        else
        {
            var none = new TextBlock { Text = "This page can't be shown here right now. Open it in the app (top right).", Margin = new Thickness(18), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _host.Child = none;
        }

        Changed();
    }

    // ------------------------------------------------------------------ keeping the place

    private void Changed()
    {
        _save.Stop();
        _save.Start();
    }

    private void Persist()
    {
        if (!WindowPlacement.CanSave || !IsLoaded)
        {
            return;
        }

        MeterSettings s = MeterSettings.Load();
        s.GamePanels[_group] = new GamePanelPlace
        {
            Left = Left, Top = Top, Width = ActualWidth > 0 ? ActualWidth : Width, Height = ActualHeight > 0 ? ActualHeight : Height, Tab = _tab,
        };
        s.Save();
    }

    // ------------------------------------------------------------------ small parts

    private static TextBlock Glyph(string glyph, double size, Brush brush) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = brush,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Brush Muted() => Application.Current?.TryFindResource("Brush.TextMuted") as Brush ?? new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4));

    private static Border IconButton(string glyph, string tip, Action click)
    {
        var rest = Brushes.Transparent;
        var b = new Border
        {
            Width = 28, Height = 26, CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 0, 0, 0), Background = rest, Cursor = Cursors.Hand, ToolTip = tip,
            Child = Glyph(glyph, 11.5, Muted()),
        };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) => b.Background = rest;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // no drag of the window from a button
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }
}
