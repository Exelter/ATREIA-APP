using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>A grind session's numbers at one moment: what came in, the time farmed, and each per hour (null until a minute was farmed).</summary>
public sealed record GrindFigures(GrindSession Session, double Farmed, double? KinahPerHour, double? ApPerHour, double? KillsPerHour, long? LootWorth, double? LootPerHour,
    long Items, bool Over)
{
    /// <summary>The figures of <paramref name="s"/> at <paramref name="now"/>, the loot priced at <paramref name="book"/> (the market of the character's server).</summary>
    public static GrindFigures Of(GrindSession s, DateTime now, MarketBook? book)
    {
        bool over = Aion2Grind.IsOver(s, now);
        double farmed = over ? s.ActiveSeconds : Aion2Grind.ActiveSecondsAt(s, now);
        long? worth = null;
        if (book is not null && s.Items.Count > 0)
        {
            worth = s.Items.Sum(kv => (CraftWorkshop.PriceOf(book, kv.Key) ?? 0) * kv.Value);
        }

        return new GrindFigures(s, farmed, s.PerHour(s.Kinah, farmed), s.PerHour(s.AbyssPoints, farmed), s.PerHour(s.Kills, farmed), worth,
            worth is long w ? s.PerHour(w, farmed) : null, s.Items.Values.Sum(), over);
    }

    /// <summary>"14:32" or "1:14:32".</summary>
    public string FarmedText => Clock(Farmed);

    public static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }
}

/// <summary>
/// The grind on screen (10 November 2026, the owner: "switch to grind on the meter while farming: how much Kinah, how much Abyss Points, and per hour"):
/// the meter's Grind view (<see cref="MeterView"/>) and the cards of the Grind page. Kinah gold, Abyss Points violet, kills and loot under them, the time
/// farmed and the map at the foot.
/// </summary>
public static class GrindView
{
    public static readonly Color KinahColour = Color.FromRgb(0xE8, 0xC4, 0x6E);
    public static readonly Color ApColour = Color.FromRgb(0xB4, 0x9C, 0xFF);
    public static readonly Color KillColour = Color.FromRgb(0xE9, 0xEE, 0xF2);
    public static readonly Color LootColour = Color.FromRgb(0x6C, 0xD4, 0x8A);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);

    /// <summary>"1.24M", "84.2K", "950".</summary>
    public static string Short(double value) => CraftWorkshop.Kinah((long)Math.Round(value));

    /// <summary>The meter's Grind view: in place of the lines. <paramref name="newSession"/> (null in the pictures) starts a new session.</summary>
    public static FrameworkElement MeterView(GrindFigures? f, Action? newSession)
    {
        var stack = new StackPanel();
        if (f is null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Kill something: the Kinah, Abyss Points and loot you farm show here, with what that makes an hour.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Muted), Margin = new Thickness(2, 2, 2, 4),
            });
            return stack;
        }

        GrindSession s = f.Session;
        var tiles = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tiles.Children.Add(Tile("KINAH / H", f.KinahPerHour is double k ? Short(k) : "…", "+" + Short(s.Kinah) + " this session", KinahColour));
        var ap = Tile("ABYSS POINTS / H", f.ApPerHour is double a ? Short(a) : "…", "+" + Short(s.AbyssPoints) + " this session", ApColour);
        Grid.SetColumn(ap, 2);
        tiles.Children.Add(ap);
        stack.Children.Add(tiles);

        stack.Children.Add(Line("Kills", $"{s.Kills:N0}" + (f.KillsPerHour is double kh ? $"  ·  {kh:N0}/h" : ""), KillColour));
        string loot = f.Items == 0 ? "nothing yet"
            : f.LootWorth is long w ? $"{f.Items:N0} items  ·  ~{Short(w)}" + (f.LootPerHour is double lh ? $"  ·  {Short(lh)}/h" : "")
            : $"{f.Items:N0} items";
        stack.Children.Add(Line("Loot", loot, LootColour, f.LootWorth is null ? null : "What the items picked up sell for at the market of your server"));

        // the foot: the time farmed, the map, and a new session
        var foot = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        if (newSession is not null)
        {
            var again = new TextBlock { Text = "New session", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(OverlaySkin.Gold), Cursor = Cursors.Hand,
                ToolTip = "Start counting again from the next kill (this session is kept on the Grind page)", VerticalAlignment = VerticalAlignment.Center };
            again.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                newSession();
            };
            DockPanel.SetDock(again, Dock.Right);
            foot.Children.Add(again);
        }

        string where = s.Zone != 0 ? "  ·  " + LootText.MapName(s.Zone) : "";
        foot.Children.Add(new TextBlock
        {
            Text = (f.Over ? "Paused  ·  " : "Farmed ") + f.FarmedText + where, FontSize = 10.5, Foreground = new SolidColorBrush(Faint), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "The time farmed: pauses of more than five minutes are left out, so the hourly numbers stay honest",
        });
        stack.Children.Add(foot);
        return stack;
    }

    private static Border Tile(string title, string value, string sub, Color colour)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, colour.R, colour.G, colour.B)) });
        var number = new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour), Margin = new Thickness(0, 0, 0, -1) };
        number.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        stack.Children.Add(number);
        stack.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = new SolidColorBrush(Muted), TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border
        {
            Child = stack, Padding = new Thickness(9, 6, 9, 7), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x14, colour.R, colour.G, colour.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, colour.R, colour.G, colour.B)),
        };
    }

    private static Grid Line(string name, string value, Color colour, string? tip = null)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 2, 1), ToolTip = tip };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Foreground = new SolidColorBrush(Muted) });
        var shown = new TextBlock { Text = value, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour), TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis };
        shown.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        Grid.SetColumn(shown, 1);
        grid.Children.Add(shown);
        return grid;
    }
}

/// <summary>A made-up grind for the pictures: 34 minutes farmed in Chaotic Middle Reshanta, two earlier sessions, a market for the loot.</summary>
internal static class GrindSample
{
    public static GrindSession Current(DateTime now) => new()
    {
        Id = "picture", Character = "Lumiel", Start = now.AddMinutes(-41), End = now.AddSeconds(-12), ActiveSeconds = 34 * 60 + 8, Kinah = 712_400, AbyssPoints = 18_640,
        Kills = 214, ZoneKills = new() { [22] = 214 },
        Items = new() { [511351001] = 14, [511351002] = 5, [511360003] = 9, [510116011] = 22, [511900001] = 2, [610620013] = 36 },
    };

    public static IReadOnlyList<GrindSession> Earlier(DateTime now) => new[]
    {
        new GrindSession
        {
            Id = "a", Character = "Lumiel", Start = now.AddDays(-1).AddHours(-2), End = now.AddDays(-1).AddHours(-1).AddMinutes(-8), ActiveSeconds = 47 * 60, Kinah = 905_200,
            AbyssPoints = 24_100, Kills = 301, ZoneKills = new() { [22] = 301 }, Items = new() { [511351001] = 19, [510116011] = 30 },
        },
        new GrindSession
        {
            Id = "b", Character = "Arkhel", Start = now.AddDays(-2).AddHours(-5), End = now.AddDays(-2).AddHours(-4), ActiveSeconds = 58 * 60, Kinah = 1_480_000, Kills = 466,
            ZoneKills = new() { [103] = 466 }, Items = new() { [511360003] = 31, [610620013] = 120 },
        },
    };

    public static MarketBook Book()
    {
        var prices = new Dictionary<int, MarketPrice>();
        foreach ((int item, long price) in new (int, long)[] { (511351001, 4_200), (511351002, 18_500), (511360003, 6_100), (510116011, 1_350), (511900001, 96_000), (610620013, 640) })
        {
            prices[item] = new MarketPrice(price, price, 8, price);
        }

        return new MarketBook(1301, "Picture", DateTime.UtcNow.AddMinutes(-4), prices);
    }
}
