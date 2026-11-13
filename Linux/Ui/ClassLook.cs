using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The class colours and emblems of the meter's lines, as the Windows app draws them (Client/Ui/ClassBrushes.cs: the same colours, keep them in step): the
/// bar a soft glow of the class colour fading along it, a bright line along its bottom, the name in the class colour lifted towards white.
/// </summary>
internal static class ClassLook
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.Ordinal)
    {
        ["Cleric"] = "#d9c777",
        ["Chanter"] = "#dfa04e",
        ["Templar"] = "#4ba4bd",
        ["Gladiator"] = "#7ad0de",
        ["Brawler"] = "#e2735c",
        ["Assassin"] = "#9ad694",
        ["Ranger"] = "#60b593",
        ["Sorcerer"] = "#aa7fe1",
        ["Spiritmaster"] = "#b75abc",
    };

    private const string Unknown = "#9fb3c8";

    private static readonly Dictionary<string, IBrush> Cache = new();
    private static readonly Dictionary<string, Bitmap?> Icons = new();

    public static Color ColorOf(string? className) => Color.Parse(className is not null && Colors.TryGetValue(className, out string? hex) ? hex : Unknown);

    private static IBrush Cached(string kind, string? className, Func<Color, IBrush> make)
    {
        string key = kind + "|" + (className is not null && Colors.ContainsKey(className) ? className : "");
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out IBrush? brush))
            {
                brush = make(ColorOf(className)).ToImmutable();
                Cache[key] = brush;
            }

            return brush;
        }
    }

    /// <summary>A player's bar: the class colour as a soft glow, stronger at the start (30 %) and fading along the bar (9 %).</summary>
    public static IBrush Glow(string? className) => Cached("glow", className, c => Horizontal(Color.FromArgb(0x4D, c.R, c.G, c.B), Color.FromArgb(0x17, c.R, c.G, c.B)));

    /// <summary>The bright line along the bottom of a player's bar: from nothing to the full class colour at its end.</summary>
    public static IBrush Line(string? className) => Cached("line", className, c => Horizontal(Color.FromArgb(0x00, c.R, c.G, c.B), Color.FromArgb(0xF0, c.R, c.G, c.B)));

    /// <summary>The thin edge of a player's bar: the class colour, nearly solid.</summary>
    public static IBrush Edge(string? className) => Cached("edge", className, c => new SolidColorBrush(Color.FromArgb(0xC8, c.R, c.G, c.B)));

    /// <summary>The translucent class colour (the bar of the plain look).</summary>
    public static IBrush Bar(string? className) => Cached("bar", className, c => new SolidColorBrush(Color.FromArgb(0x59, c.R, c.G, c.B)));

    /// <summary>A player's name: the class colour, lifted towards white so the deep shades read on the dark panel.</summary>
    public static IBrush Text(string? className) => Cached("text", className, c => new SolidColorBrush(Color.FromRgb(Lift(c.R), Lift(c.G), Lift(c.B))));

    private static byte Lift(byte v) => (byte)(v + (255 - v) * 0.35);

    private static LinearGradientBrush Horizontal(Color from, Color to) => new()
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
    };

    /// <summary>The class's emblem from the image pack (classes/icons/&lt;Class&gt;.png), null while there is none.</summary>
    public static Bitmap? Icon(string? className)
    {
        if (className is null || !Colors.ContainsKey(className))
        {
            return null;
        }

        lock (Icons)
        {
            if (Icons.TryGetValue(className, out Bitmap? known))
            {
                return known;
            }

            Bitmap? icon = Pictures.Load(Aion2.GameArt.PathOf("classes", "icons", className + ".png"));
            if (icon is not null)
            {
                Icons[className] = icon; // a missing one is asked again (the image pack may still be coming)
            }

            return icon;
        }
    }

    /// <summary>The image pack changed: the emblems are read again.</summary>
    public static void Forget()
    {
        lock (Icons)
        {
            Icons.Clear();
        }
    }
}

/// <summary>Pictures read from disk once and kept (null when missing or unreadable).</summary>
internal static class Pictures
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? Load(string? path)
    {
        if (path is null)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out Bitmap? known))
            {
                return known;
            }
        }

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = new Bitmap(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            bitmap = null;
        }

        if (bitmap is not null)
        {
            lock (Cache)
            {
                Cache[path] = bitmap;
            }
        }

        return bitmap;
    }

    public static void Forget()
    {
        lock (Cache)
        {
            Cache.Clear();
        }
    }
}
