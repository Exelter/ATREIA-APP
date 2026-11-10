using System.Windows;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The see-through look of the windows over the game (10 November 2026, the owner: "the window should be transparent, same with the other windows - it's an
/// in-game overlay"; and the light line along the top looked "weird and ugly"): the panels of the meter's launcher, the player window, the overlay menu and
/// the overlay editor take the meter's own panel colour at the overlays' opacity (Overlays page; never below half, the pages are dense), the meter's faint
/// edge instead of a light one, and the app's pages inside them their cards and controls as light tints of that glass instead of solid greys.
/// Every part still takes the mouse over the game: a see-through colour is not click-through (only a fully clear one is).
/// </summary>
internal static class InGameGlass
{
    /// <summary>The lowest opacity a window of pages gets, whatever the overlays' setting says.</summary>
    public const double Floor = 0.5;

    /// <summary>The window's background: the meter's panel colour (Meter look → Panel) at the overlays' opacity, at least <see cref="Floor"/>.</summary>
    public static Brush Background(MeterSettings s)
    {
        double opacity = Math.Clamp(double.IsFinite(s.OverlayOpacity) ? s.OverlayOpacity : 0.6, Floor, 1.0);
        Color themed = Application.Current?.TryFindResource("Color.OverlayBg") is Color c ? c : Color.FromRgb(0x14, 0x12, 0x18);
        Color back = MeterLook.PanelOf(s.OverlayLook) switch
        {
            // the meter's "Clear" panel is a light glass over the game rows; a window of pages needs more under its text
            "Clear" => Color.FromArgb((byte)Math.Round(opacity * 255), 0x0C, 0x0C, 0x10),
            "Graphite" => Color.FromArgb((byte)Math.Round(opacity * 255), 0x1F, 0x20, 0x24),
            "Black" => Color.FromArgb((byte)Math.Round(opacity * 255), 0x11, 0x11, 0x13),
            _ => Color.FromArgb((byte)Math.Round(opacity * 255), themed.R, themed.G, themed.B),
        };
        return Frozen(new SolidColorBrush(back));
    }

    /// <summary>The faint edge the meter has (no light line around the window).</summary>
    public static Brush Edge(MeterSettings s) => Frozen(new SolidColorBrush(MeterLook.PanelOf(s.OverlayLook) switch
    {
        "Clear" => Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        "Graphite" => Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
        "Black" => Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF),
        _ => Color.FromArgb(0x33, 0xDC, 0xB8, 0x77),
    }));

    /// <summary>
    /// The app's pages in a window over the game: their solid page, card and control colours (the theme's, looked up as resources) become tints of the
    /// glass under them, set on <paramref name="root"/> so they count for everything inside it and nowhere else.
    /// </summary>
    public static void Apply(FrameworkElement root)
    {
        ResourceDictionary r = root.Resources;
        r["Brush.Window"] = Brushes.Transparent;
        r["Brush.TitleBar"] = Tint(0x33, 0x00);
        r["Brush.Panel"] = Tint(0x10, 0xFF);
        r["Brush.RowAlt"] = Tint(0x08, 0xFF);
        r["Brush.Control"] = Tint(0x14, 0xFF);
        r["Brush.ControlRaised"] = Tint(0x1C, 0xFF);
        r["Brush.ControlHover"] = Tint(0x22, 0xFF);
        r["Brush.ControlHoverStrong"] = Tint(0x30, 0xFF);
        r["Brush.Border"] = Tint(0x22, 0xFF);
        r["Brush.ScrollThumb"] = Tint(0x38, 0xFF);
        r["Brush.CheckBorder"] = Tint(0x40, 0xFF);
        r["Brush.CheckBorderHover"] = Tint(0x60, 0xFF);
    }

    private static Brush Tint(byte alpha, byte level) => Frozen(new SolidColorBrush(Color.FromArgb(alpha, level, level, level)));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
