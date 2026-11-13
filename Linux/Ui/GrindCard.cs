using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The meter's Grind view (the Windows meter's, Client/Ui/GrindView.cs MeterView): in place of the lines, the grind going on - Kinah and Abyss Points an hour
/// in big, what came in this session under them, the kills and the loot with its worth at the market - and a new session at the foot.
/// </summary>
internal static class GrindCard
{
    private static readonly Color KinahColour = Color.FromRgb(0xE8, 0xC4, 0x6E);
    private static readonly Color ApColour = Color.FromRgb(0xB4, 0x9C, 0xFF);
    private static readonly Color KillColour = Color.FromRgb(0xE9, 0xEE, 0xF2);
    private static readonly Color LootColour = Color.FromRgb(0x6C, 0xD4, 0x8A);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);

    private static IBrush Brush(Color c, byte alpha = 0xFF) => new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)).ToImmutable();

    /// <summary>"1.24M", "84.2K", "950".</summary>
    private static string Short(double value) => CraftWorkshop.Kinah((long)Math.Round(value));

    /// <summary>The view for <paramref name="f"/> (null: nothing farmed yet); <paramref name="newSession"/> (null in the pictures) starts a new session.</summary>
    public static Control Build(GrindFigures? f, Action? newSession)
    {
        var stack = new StackPanel();
        if (f is null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Kill something: the Kinah, Abyss Points and loot you farm show here, with what that makes an hour.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(Muted), Margin = new Thickness(2, 2, 2, 4), FontFamily = Palette.UiFont,
            });
            return stack;
        }

        GrindSession s = f.Session;
        var tiles = new Grid { Margin = new Thickness(0, 0, 0, 6), ColumnDefinitions = new ColumnDefinitions("*,6,*") };
        tiles.Children.Add(Tile("KINAH / H", f.KinahPerHour is double k ? Short(k) : "…", "+" + Short(s.Kinah) + " this session", KinahColour));
        Control ap = Tile("ABYSS POINTS / H", f.ApPerHour is double a ? Short(a) : "…", "+" + Short(s.AbyssPoints) + " this session", ApColour);
        Grid.SetColumn(ap, 2);
        tiles.Children.Add(ap);
        stack.Children.Add(tiles);

        stack.Children.Add(Line("Kills", $"{s.Kills:N0}" + (f.KillsPerHour is double kh ? $"  ·  {kh:N0}/h" : ""), KillColour));
        string loot = f.Items == 0 ? "nothing yet"
            : f.LootWorth is long w ? $"{f.Items:N0} items  ·  ~{Short(w)}" + (f.LootPerHour is double lh ? $"  ·  {Short(lh)}/h" : "")
            : $"{f.Items:N0} items";
        stack.Children.Add(Line("Loot", loot, LootColour, f.LootWorth is null ? null : "What the items picked up sell for at the market of your server"));

        // the foot: a new session, and when the clock stopped (the time farmed and the map are in the meter's head already)
        var foot = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        if (newSession is not null)
        {
            var again = new TextBlock
            {
                Text = "New session", FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = Brush(Gold), Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, FontFamily = Palette.UiFont,
            };
            ToolTip.SetTip(again, "Start counting again from the next kill (this session is kept)");
            again.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                newSession();
            };
            DockPanel.SetDock(again, Dock.Right);
            foot.Children.Add(again);
        }

        if (f.Over)
        {
            var ended = new TextBlock
            {
                Text = "Ended: the next kill starts a new session", FontSize = 10.5, Foreground = Brush(Faint), TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, FontFamily = Palette.UiFont,
            };
            ToolTip.SetTip(ended, "Half an hour without a kill ends a session");
            foot.Children.Add(ended);
        }

        if (foot.Children.Count > 0)
        {
            stack.Children.Add(foot);
        }

        return stack;
    }

    private static Control Tile(string title, string value, string sub, Color colour)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 9.5, FontWeight = FontWeight.SemiBold, Foreground = Brush(colour, 0xC0), FontFamily = Palette.UiFont });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brush(colour), Margin = new Thickness(0, 0, 0, -1), FontFamily = Palette.UiFont });
        stack.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = Brush(Muted), TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = Palette.UiFont });
        return new Border
        {
            Child = stack, Padding = new Thickness(9, 6, 9, 7), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = Brush(colour, 0x14), BorderBrush = Brush(colour, 0x40),
        };
    }

    private static Control Line(string name, string value, Color colour, string? tip = null)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 2, 1), ColumnDefinitions = new ColumnDefinitions("44,*") };
        if (tip is not null)
        {
            ToolTip.SetTip(grid, tip);
        }

        grid.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Foreground = Brush(Muted), FontFamily = Palette.UiFont });
        var shown = new TextBlock
        {
            Text = value, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Brush(colour), TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = Palette.UiFont,
        };
        Grid.SetColumn(shown, 1);
        grid.Children.Add(shown);
        return grid;
    }
}
