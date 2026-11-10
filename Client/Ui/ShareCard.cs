using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>One player on a fight's share card.</summary>
public sealed record ShareLine(string Name, string? ClassName, double Dps, long Total, double Share, bool You);

/// <summary>
/// Share cards (10 November 2026, the owner picked: "one click turns a boss kill, a grind session or a parse into a clean image, ready to paste into Discord"):
/// a card of its own design - not a screenshot of a window - copied to the clipboard as a picture (Ctrl+V in Discord) and kept in Pictures\Atreia App as well.
/// A fight's card lists the players by damage with their DPS and share; a grind's card has the hourly numbers in big. At twice the pixels, so it stays sharp.
/// </summary>
public static class ShareCard
{
    public const double Width = 640;
    private static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);
    private static readonly Color Ink = Color.FromRgb(0xE9, 0xEE, 0xF2);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);

    /// <summary>The players of a saved fight by damage (the report's own events: what each player did in the fight's window).</summary>
    public static List<ShareLine> LinesOf(ReportInput input, string? you)
    {
        double seconds = Math.Max(1, (input.End - input.Start).TotalSeconds);
        var ids = input.Players.ToDictionary(p => p.Id);
        var totals = input.Events.Where(e => !e.IsHeal && ids.ContainsKey(e.SourceObjectId) && !ids.ContainsKey(e.TargetObjectId) && e.Timestamp >= input.Start && e.Timestamp <= input.End)
            .GroupBy(e => e.SourceObjectId).Select(g => (Id: g.Key, Total: g.Sum(e => e.Amount))).Where(t => t.Total > 0).ToList();
        long all = totals.Sum(t => t.Total);
        return totals.OrderByDescending(t => t.Total).Select(t =>
        {
            ReportPlayer p = ids[t.Id];
            return new ShareLine(p.Name, p.ClassName, t.Total / seconds, t.Total, all > 0 ? (double)t.Total / all : 0, p.IsSelf || p.Name == you);
        }).ToList();
    }

    /// <summary>A fight's card: <paramref name="kicker"/> ("BOSS KILL", "WIPE", "FIGHT") over the title, the place and day, the length and the group's DPS, the players.</summary>
    public static FrameworkElement Fight(string kicker, string title, string? sub, TimeSpan length, IReadOnlyList<ShareLine> lines, Color? accent = null)
    {
        Color tone = accent ?? Gold;
        var stack = new StackPanel();
        stack.Children.Add(Head(kicker, title, sub, tone));
        var stats = new UniformGrid3();
        stats.Add(Stat("TIME", $"{(int)length.TotalMinutes}:{length.Seconds:00}", Ink));
        stats.Add(Stat("GROUP DPS", GrindView.Short(lines.Sum(l => l.Dps)), tone));
        stats.Add(Stat("PLAYERS", lines.Count.ToString(CultureInfo.CurrentCulture), Ink));
        stack.Children.Add(stats.Panel);
        stack.Children.Add(Rule());
        double top = lines.Count > 0 ? lines.Max(l => l.Total) : 1;
        int rank = 0;
        foreach (ShareLine line in lines.Take(10))
        {
            rank++;
            stack.Children.Add(PlayerLine(rank, line, top, tone));
        }

        if (lines.Count > 10)
        {
            stack.Children.Add(Text($"+ {lines.Count - 10} more", 12, Faint, margin: new Thickness(34, 4, 0, 0)));
        }

        return Frame(stack, tone);
    }

    /// <summary>A grind session's card: the map and the time farmed, Kinah, Abyss Points, kills and loot an hour in big, what came in under them.</summary>
    public static FrameworkElement Grind(GrindFigures f)
    {
        GrindSession s = f.Session;
        var stack = new StackPanel();
        string where = s.Zone != 0 ? LootText.MapName(s.Zone) : "Grind session";
        string sub = (s.Character.Length > 0 ? s.Character + "  ·  " : "") + s.Start.ToString("ddd d MMM", CultureInfo.CurrentCulture) + "  ·  farmed " + f.FarmedText;
        stack.Children.Add(Head("GRIND", where, sub, GrindView.KinahColour));
        var stats = new UniformGrid3(4);
        stats.Add(Stat("KINAH / HOUR", f.KinahPerHour is double k ? GrindView.Short(k) : "–", GrindView.KinahColour, "+" + GrindView.Short(s.Kinah)));
        stats.Add(Stat("ABYSS POINTS / HOUR", f.ApPerHour is double a ? GrindView.Short(a) : "–", GrindView.ApColour, "+" + GrindView.Short(s.AbyssPoints)));
        stats.Add(Stat("KILLS / HOUR", f.KillsPerHour is double kh ? kh.ToString("N0", CultureInfo.CurrentCulture) : "–", Ink, s.Kills.ToString("N0", CultureInfo.CurrentCulture) + " killed"));
        stats.Add(Stat("LOOT / HOUR", f.LootPerHour is double lh ? GrindView.Short(lh) : "–", GrindView.LootColour, f.LootWorth is long w ? "~" + GrindView.Short(w) : $"{f.Items:N0} items"));
        stack.Children.Add(stats.Panel);
        if (s.Items.Count > 0)
        {
            stack.Children.Add(Rule());
            var icons = new WrapPanel();
            foreach ((int item, long count) in s.Items.OrderByDescending(kv => kv.Value).Take(8))
            {
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 6) };
                Border icon = CraftText.IconTile(item, 26);
                icon.Margin = new Thickness(0, 0, 6, 0);
                cell.Children.Add(icon);
                cell.Children.Add(Text("×" + count.ToString("N0", CultureInfo.CurrentCulture), 12.5, Ink, bold: true));
                icons.Children.Add(cell);
            }

            stack.Children.Add(icons);
        }

        return Frame(stack, GrindView.KinahColour);
    }

    /// <summary>
    /// Copies the card to the clipboard as a picture and keeps it in Pictures\Atreia App. Returns the file kept (null when none could be written); false
    /// when the clipboard was busy.
    /// </summary>
    public static bool Copy(FrameworkElement card, string name, out string? saved)
    {
        BitmapSource bitmap = Render(card);
        saved = null;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Atreia App");
            Directory.CreateDirectory(folder);
            string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
            string path = Path.Combine(folder, $"{safe} {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path))
            {
                encoder.Save(file);
            }

            saved = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // the picture still goes to the clipboard
        }

        try
        {
            Clipboard.SetImage(bitmap);
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            return false; // the clipboard is busy
        }
    }

    /// <summary>The card as a picture at twice its size.</summary>
    public static BitmapSource Render(FrameworkElement card)
    {
        card.Measure(new Size(Width, double.PositiveInfinity));
        card.Arrange(new Rect(0, 0, Width, card.DesiredSize.Height));
        card.UpdateLayout();
        int w = (int)Math.Ceiling(Width), h = (int)Math.Ceiling(card.DesiredSize.Height);
        var bitmap = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }

    // ------------------------------------------------------------------ parts

    private static FrameworkElement Head(string kicker, string title, string? sub, Color tone)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var top = new DockPanel();
        var brand = Text("ATREIA APP", 11, Faint, bold: true);
        DockPanel.SetDock(brand, Dock.Right);
        top.Children.Add(brand);
        top.Children.Add(Text(kicker, 11, tone, bold: true));
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Ink), TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0) });
        if (sub is { Length: > 0 })
        {
            stack.Children.Add(Text(sub, 13, Muted));
        }

        return stack;
    }

    private static FrameworkElement Stat(string title, string value, Color colour, string? sub = null)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        stack.Children.Add(Text(title, 10.5, Faint, bold: true));
        var number = new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour) };
        number.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        stack.Children.Add(number);
        if (sub is not null)
        {
            stack.Children.Add(Text(sub, 12, Muted));
        }

        return stack;
    }

    private static FrameworkElement PlayerLine(int rank, ShareLine line, double top, Color tone)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.Children.Add(Text(rank.ToString(CultureInfo.CurrentCulture), 13, rank == 1 ? tone : Faint, bold: true));
        var icon = new Image { Source = MainWindow.ClassIcon(line.ClassName), Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var name = new StackPanel();
        var title = new TextBlock { Text = line.Name + (line.You ? "  (you)" : ""), FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = line.ClassName is { } c ? ClassBrushes.Text(c) : new SolidColorBrush(Ink) };
        name.Children.Add(title);
        double fill = top > 0 ? Math.Clamp(line.Total / top, 0, 1) : 0;
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 4, 16, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fill, GridUnitType.Star) });
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = line.ClassName is { } cl ? ClassBrushes.Solid(cl) : new SolidColorBrush(tone) });
        name.Children.Add(new Border { Child = bar, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), ClipToBounds = true,
            Margin = new Thickness(0, 4, 16, 0) });
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        var dps = Text(GrindView.Short(line.Dps), 15, Ink, bold: true);
        dps.HorizontalAlignment = HorizontalAlignment.Right;
        dps.ToolTip = "DPS";
        Grid.SetColumn(dps, 3);
        grid.Children.Add(dps);
        var share = Text(line.Share.ToString("P0", CultureInfo.CurrentCulture), 12.5, Muted);
        share.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(share, 4);
        grid.Children.Add(share);
        if (line.You)
        {
            return new Border { Child = grid, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(-8, 0, -8, 0), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x16, tone.R, tone.G, tone.B)) };
        }

        return grid;
    }

    private static Border Rule() => new() { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 16, 0, 12) };

    private static Border Frame(UIElement content, Color tone)
    {
        var glow = new RadialGradientBrush(Color.FromArgb(0x38, tone.R, tone.G, tone.B), Color.FromArgb(0, tone.R, tone.G, tone.B))
        {
            Center = new Point(0.15, 0), GradientOrigin = new Point(0.15, 0), RadiusX = 0.9, RadiusY = 0.7,
        };
        var inner = new Border { Background = glow, Padding = new Thickness(28, 24, 28, 22), Child = content };
        var outer = new Border
        {
            Width = Width, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), ClipToBounds = true, Child = inner,
            Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1D, 0x24), Color.FromRgb(0x10, 0x11, 0x16), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, tone.R, tone.G, tone.B)),
        };
        TextOptions.SetTextFormattingMode(outer, TextFormattingMode.Ideal);
        return outer;
    }

    private static TextBlock Text(string text, double size, Color colour, bool bold = false, Thickness? margin = null) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = margin ?? new Thickness(0),
    };

    /// <summary>A row of equal columns.</summary>
    private sealed class UniformGrid3
    {
        public UniformGrid3(int columns = 3) => Panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };

        public System.Windows.Controls.Primitives.UniformGrid Panel { get; }

        public void Add(UIElement e) => Panel.Children.Add(e);
    }
}
