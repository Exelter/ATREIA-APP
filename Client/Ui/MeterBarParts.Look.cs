using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The bar parts' colours and their legend (WPF); which part is how long is worked out in MeterBarParts.cs, which has no WPF in it.</summary>
public static partial class MeterBarParts
{
    /// <summary>The grey of the buffs given and the green of the healing (the bar parts, their "+" figures, the legend).</summary>
    public static readonly Color GivenColor = Color.FromRgb(0xB4, 0xBD, 0xC8);

    public static readonly Color HealColor = Color.FromRgb(0x52, 0xD2, 0x86);

    public static readonly Brush GivenText = Frozen(Color.FromRgb(0xB9, 0xC1, 0xCB));

    public static readonly Brush HealText = Frozen(Color.FromRgb(0x6F, 0xDF, 0x9C));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>The line under the meter's lines that says what the colours are: own damage, buffs for others, healing on others, and what the whole bar is.</summary>
    public static UIElement Legend(MeterLook look, bool rdpsView)
    {
        var line = new WrapPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        TextElementFont(line, look);
        void Item(Brush swatch, string text)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            item.Children.Add(new Border
            {
                Width = 9, Height = 9, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                Background = swatch,
            });
            var words = new TextBlock { Text = text, FontSize = look.TinySize, VerticalAlignment = VerticalAlignment.Center };
            words.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            item.Children.Add(words);
            line.Children.Add(item);
        }

        // the own part is the class colour: three of them in the swatch
        var classes = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        classes.GradientStops.Add(new GradientStop(ClassBrushes.ClassColor("Gladiator"), 0.0));
        classes.GradientStops.Add(new GradientStop(ClassBrushes.ClassColor("Gladiator"), 0.33));
        classes.GradientStops.Add(new GradientStop(ClassBrushes.ClassColor("Sorcerer"), 0.34));
        classes.GradientStops.Add(new GradientStop(ClassBrushes.ClassColor("Sorcerer"), 0.66));
        classes.GradientStops.Add(new GradientStop(ClassBrushes.ClassColor("Ranger"), 0.67));
        classes.GradientStops.Add(new GradientStop(ClassBrushes.ClassColor("Ranger"), 1.0));
        Item(classes, "own damage");
        // only the parts that are switched on (11 November 2026)
        if (look.BarBuffs)
        {
            Item(new SolidColorBrush(GivenColor) { Opacity = 0.8 }, "buffs for others");
        }

        if (look.BarHeals)
        {
            Item(new SolidColorBrush(HealColor) { Opacity = 0.85 }, "healing on others");
        }

        string bar = rdpsView ? "bar = rDPS" : "bar = damage" + (look.BarBuffs ? " + buffs" : "");
        var whole = new TextBlock { Text = bar + (look.BarHeals ? " + healing" : ""), FontSize = look.TinySize, VerticalAlignment = VerticalAlignment.Center };
        whole.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        line.Children.Add(whole);
        return line;
    }

    private static void TextElementFont(FrameworkElement element, MeterLook look) => System.Windows.Documents.TextElement.SetFontFamily(element, look.FontFamily);
}

/// <summary>
/// The grey and green parts of a meter line's bar, drawn right after the class-coloured part (<see cref="MeterBarParts"/>) in the shape of the look's bar:
/// the whole line, the thin thread at its foot, the round capsule or the frame.
/// </summary>
public sealed class MeterBarPartsView : FrameworkElement
{
    public static readonly DependencyProperty OwnProperty = DependencyProperty.Register(nameof(Own), typeof(double), typeof(MeterBarPartsView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GivenEndProperty = DependencyProperty.Register(nameof(GivenEnd), typeof(double), typeof(MeterBarPartsView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HealEndProperty = DependencyProperty.Register(nameof(HealEnd), typeof(double), typeof(MeterBarPartsView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Where the class-coloured part ends, 0-100 of the line (<see cref="PlayerRow.FillPercent"/>).</summary>
    public double Own
    {
        get => (double)GetValue(OwnProperty);
        set => SetValue(OwnProperty, value);
    }

    /// <summary>Where the grey part ends (<see cref="PlayerRow.GivenFillPercent"/>).</summary>
    public double GivenEnd
    {
        get => (double)GetValue(GivenEndProperty);
        set => SetValue(GivenEndProperty, value);
    }

    /// <summary>Where the green part ends (<see cref="PlayerRow.HealFillPercent"/>).</summary>
    public double HealEnd
    {
        get => (double)GetValue(HealEndProperty);
        set => SetValue(HealEndProperty, value);
    }

    /// <summary>The look's bar style (<see cref="MeterLook.Bars"/>), or "round" for the window's rows.</summary>
    public string Shape { get; set; } = "glow";

    private static readonly Dictionary<string, (Brush Given, Brush Heal, Brush? GivenLine, Brush? HealLine)> Cache = new();

    public MeterBarPartsView()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    private (Brush Given, Brush Heal, Brush? GivenLine, Brush? HealLine) BrushesFor()
    {
        if (Cache.TryGetValue(Shape, out var known))
        {
            return known;
        }

        // as strong as the class part is in that style, so the parts read as one bar
        byte alpha = Shape switch { "solid" => 0x92, "table" => 0x78, "thread" => 0xD8, "frame" => 0x50, "glow" => 0x46, _ => 0x56 };
        Brush Fill(Color c, byte a)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }

        bool lined = Shape is "glow" or "frame" || !MeterLook.Bars.Contains(Shape);
        var made = (Fill(MeterBarParts.GivenColor, alpha), Fill(MeterBarParts.HealColor, alpha),
            lined ? Fill(MeterBarParts.GivenColor, 0xC8) : null, lined ? Fill(MeterBarParts.HealColor, 0xD0) : null);
        Cache[Shape] = made;
        return made;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // the bar's own margins and corners in each style (MeterRowView.BarTemplate)
        // "round": the main window's rows and the chips (Shared.xaml's GlowBarTemplate, round ends)
        (double side, double edge, double radius) = Shape switch { "capsule" => (3.0, 2.0, 6.0), "frame" => (2.0, 1.0, 3.0), "round" => (0.0, 0.0, 9.0), _ => (0.0, 0.0, 0.0) };
        double inner = width - 2 * side;
        double X(double percent) => side + inner * Math.Clamp(double.IsFinite(percent) ? percent : 0, 0, 100) / 100.0;
        double x0 = X(Own), x1 = Math.Max(x0, X(GivenEnd)), x2 = Math.Max(x1, X(HealEnd));
        if (x2 - x0 < 0.5)
        {
            return;
        }

        double top = Shape == "thread" ? height - 2 : edge, tall = Shape == "thread" ? 2 : height - 2 * edge;
        if (tall <= 0)
        {
            return;
        }

        var (given, heal, givenLine, healLine) = BrushesFor();
        if (radius > 0)
        {
            // the round end goes on the last part, as the class part has it when it stands alone
            dc.PushClip(new RectangleGeometry(new Rect(side, top, x2 - side, tall), radius, radius));
        }

        if (x1 > x0)
        {
            dc.DrawRectangle(given, null, new Rect(x0, top, x1 - x0, tall));
        }

        if (x2 > x1)
        {
            dc.DrawRectangle(heal, null, new Rect(x1, top, x2 - x1, tall));
        }

        // the glow, flat and frame bars have a fine line in the full colour at their foot: the parts too
        if (givenLine is not null && x1 > x0)
        {
            dc.DrawRectangle(givenLine, null, new Rect(x0, top + tall - 1, x1 - x0, 1));
        }

        if (healLine is not null && x2 > x1)
        {
            dc.DrawRectangle(healLine, null, new Rect(x1, top + tall - 1, x2 - x1, 1));
        }

        if (radius > 0)
        {
            dc.Pop();
        }
    }
}

/// <summary>A "+" figure of the bar parts is only there when it has a value: an empty one takes no room.</summary>
public sealed class FilledToVisible : IValueConverter
{
    public static readonly FilledToVisible Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
