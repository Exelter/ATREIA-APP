using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// The look the overlays share with the meter (10 November 2026, the owner: "re-design the overlays to be way nicer, way more smooth, similar to the meter"): the
/// theme's overlay background (Brush.OverlayBg, as the meter and the timers have it) behind a soft edge, round corners, a small gold heading in capitals with
/// what matters on its right, and rows of muted names and clear values. Before this the overlays had four looks of their own (a blue box, a blue gradient …).
/// </summary>
internal static class OverlaySkin
{
    public static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);
    public static readonly Color Good = Color.FromRgb(0x5B, 0xD0, 0x7A);
    public static readonly Color Warn = Color.FromRgb(0xFF, 0xC8, 0x6B);
    public static readonly Color Bad = Color.FromRgb(0xFF, 0x6B, 0x6B);

    private static readonly Brush Edge = Frozen(new LinearGradientBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 90));
    private static readonly Brush Rule = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Fallback = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x17, 0x18, 0x1D)));

    /// <summary>The soft edge of the overlays' frames.</summary>
    public static Brush SoftEdge => Edge;

    /// <summary>An overlay's frame: the theme's overlay background, a soft edge, round corners.</summary>
    public static Border Panel(UIElement child, Thickness padding)
    {
        var border = new Border { BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = padding, Child = child, Background = Fallback };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        return border;
    }

    /// <summary>
    /// An overlay's heading: a status dot when given, the title in small gold capitals, and on the right a short note (a total, a state) in its colour.
    /// </summary>
    public static Grid Heading(string title, string? note = null, Color? noteColor = null, Color? dot = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (dot is Color d)
        {
            grid.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(d), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        var name = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Gold), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (note is { Length: > 0 })
        {
            var right = new TextBlock { Text = note, FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (noteColor is Color c)
            {
                right.Foreground = new SolidColorBrush(c);
            }
            else
            {
                right.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            }

            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }

        return grid;
    }

    /// <summary>A thin line between parts of an overlay.</summary>
    public static Border Divider(double top = 2, double bottom = 6) => new() { Height = 1, Background = Rule, Margin = new Thickness(0, top, 0, bottom) };

    /// <summary>A line of an overlay: the name muted on the left, the value clear on the right (in its colour when given), every other line on a faint band.</summary>
    public static Border Row(string name, string value, Color? valueColor, bool band, double nameWidth = double.NaN)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(nameWidth) ? GridLength.Auto : new GridLength(nameWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock { Text = name, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        grid.Children.Add(label);
        var shown = new TextBlock { Text = value, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis };
        shown.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        if (valueColor is Color c)
        {
            shown.Foreground = new SolidColorBrush(c);
        }
        else
        {
            shown.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        }

        Grid.SetColumn(shown, 1);
        grid.Children.Add(shown);
        return new Border
        {
            Child = grid, Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(5),
            Background = band ? new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
    }

    /// <summary>A key or a key combination as keycaps: "Ctrl+Alt+H" as three small raised boxes.</summary>
    public static StackPanel Keys(string keys, Color? color = null)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        string[] parts = keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || keys.Contains(' ', StringComparison.Ordinal) && parts.Length == 1)
        {
            var plain = new TextBlock { Text = keys, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
            plain.Foreground = new SolidColorBrush(color ?? Gold);
            line.Children.Add(plain);
            return line;
        }

        Color ink = color ?? Gold;
        foreach (string whole in parts)
        {
            // the long key names short, as keyboards print them
            string part = whole switch { "PageUp" => "PgUp", "PageDown" => "PgDn", "Escape" => "Esc", "Delete" => "Del", "Insert" => "Ins", _ => whole };
            line.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1, 1, 1, 2), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(3, 0, 0, 0), MinWidth = 18,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ink.R, ink.G, ink.B)), Background = new SolidColorBrush(Color.FromArgb(0x16, ink.R, ink.G, ink.B)),
                Child = new TextBlock { Text = part, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), HorizontalAlignment = HorizontalAlignment.Center },
            });
        }

        return line;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
