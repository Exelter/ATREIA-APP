using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AionDPS.Linux.Meter;
using AionDPS.Ui;

namespace AionDPS.Linux.Ui;

/// <summary>
/// One player's line in the Windows meter's usual look (Client/Ui/MeterRowView.cs with the standard design of Client/Ui/MeterLook.cs: the glow bar, the class
/// icon, the combat power after the name, then the rate, the share and the total): over the bar of the class colour (<see cref="FillBar"/>), the "+" figures of
/// the bar's parts at the end of the name side - grey for what the buffs gave the others, green for the healing on the others, per second. In All in one the
/// three figures are the damage, the healing (green) and the damage taken (red).
/// </summary>
internal sealed class RowView : Grid
{
    /// <summary>The line's height and the gap under it (Windows: MeterLook.RowHeight 27 and Gap 1 of the standard design).</summary>
    public const double LineHeight = 27, Gap = 1;

    private static readonly IBrush GivenText = new SolidColorBrush(Color.Parse("#B9C1CB")).ToImmutable();
    private static readonly IBrush HealText = new SolidColorBrush(Color.Parse("#6FDF9C")).ToImmutable();

    private readonly FillBar _bar = new();
    private readonly Image _icon = new() { Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = MeterView.Text(13, FontWeight.SemiBold, Palette.OverlayText);
    private readonly TextBlock _power = MeterView.Text(10.5, FontWeight.Normal, Palette.TextMuted);
    private readonly TextBlock _given = MeterView.Text(10.5, FontWeight.SemiBold, GivenText);
    private readonly TextBlock _healed = MeterView.Text(10.5, FontWeight.SemiBold, HealText);
    private readonly TextBlock _rate = MeterView.Text(13.5, FontWeight.Bold, Palette.OverlayText);
    private readonly TextBlock _share = MeterView.Text(11.5, FontWeight.Normal, Palette.TextSubtle);
    private readonly TextBlock _total = MeterView.Text(12, FontWeight.Normal, Palette.TextSubtle);

    public RowView()
    {
        Height = LineHeight;
        Margin = new Thickness(0, 0, 0, Gap);
        Children.Add(_bar);

        var line = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        line.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        line.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "Rate" });
        line.ColumnDefinitions.Add(new ColumnDefinition(42, GridUnitType.Pixel));
        line.ColumnDefinitions.Add(new ColumnDefinition(48, GridUnitType.Pixel));

        // the name side: the name and its combat power, the "+" figures of the bar's parts at its end (they take their room from the name, not the meter's width)
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _power.Margin = new Thickness(7, 1, 0, 0);
        name.Children.Add(_name);
        name.Children.Add(_power);
        var plus = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _given.Margin = new Thickness(6, 0, 0, 0);
        _healed.Margin = new Thickness(6, 0, 0, 0);
        plus.Children.Add(_given);
        plus.Children.Add(_healed);
        var nameSide = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        DockPanel.SetDock(plus, Dock.Right);
        nameSide.Children.Add(plus);
        nameSide.Children.Add(name);

        _rate.TextAlignment = TextAlignment.Right;
        _rate.HorizontalAlignment = HorizontalAlignment.Right;
        _rate.Margin = new Thickness(8, 0, 0, 0);
        _share.TextAlignment = TextAlignment.Right;
        _share.HorizontalAlignment = HorizontalAlignment.Right;
        _total.TextAlignment = TextAlignment.Right;
        _total.HorizontalAlignment = HorizontalAlignment.Right;

        Place(line, _icon, 0);
        Place(line, nameSide, 1);
        Place(line, _rate, 2);
        Place(line, _share, 3);
        Place(line, _total, 4);
        Children.Add(line);
    }

    public void Show(PlayerRow row, MeterEngine.MeterMode mode)
    {
        _bar.Set(row.FillPercent, row.GivenFillPercent, row.HealFillPercent, ClassLook.Glow(row.ClassName), ClassLook.Line(row.ClassName), ClassLook.Edge(row.ClassName), row.IsEnemy);
        Bitmap? icon = ClassLook.Icon(row.ClassName);
        _icon.Source = icon;
        _icon.Opacity = icon is null ? 0 : 1;
        _name.Text = row.NameDisplay;
        _power.Text = row.CombatPowerText;
        _power.IsVisible = row.CombatPowerText.Length > 0;
        _given.Text = row.BarGivenText;
        _given.IsVisible = row.BarGivenText.Length > 0;
        _healed.Text = row.BarHealText;
        _healed.IsVisible = row.BarHealText.Length > 0;
        _rate.Text = row.RateOrDamageCompact;
        _share.Text = row.ShareOrHealCompact;
        _total.Text = row.TotalOrTakenCompact;
        bool all = mode == MeterEngine.MeterMode.All;
        _share.Foreground = all ? Palette.Heal : Palette.TextSubtle;
        _total.Foreground = all ? Palette.DangerSoft : Palette.TextSubtle;
    }

    private static void Place(Grid grid, Control child, int column)
    {
        SetColumn(child, column);
        grid.Children.Add(child);
    }
}

/// <summary>One line of the buff view (Windows: OverlayBuffRows): the effect's icon, its name (red when harmful), a thin bar and the share of the fight it was on.</summary>
internal sealed class BuffRowView : Grid
{
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#5CC27E")).ToImmutable();
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E06A5E")).ToImmutable();
    private static readonly IBrush Faint = new SolidColorBrush(Color.Parse("#14FFFFFF")).ToImmutable();

    private readonly Image _icon = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _name = MeterView.Text(12.5, FontWeight.Normal, Palette.OverlayText);
    private readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _percent = MeterView.Text(11.5, FontWeight.Normal, Palette.TextMuted);

    public BuffRowView()
    {
        Height = 24;
        ColumnDefinitions = new ColumnDefinitions("24,*,80,40");
        var frame = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = Faint, ClipToBounds = true, Child = _icon,
        };
        Children.Add(frame);
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        SetColumn(_name, 1);
        Children.Add(_name);
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Faint, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Child = _fill };
        SetColumn(track, 2);
        Children.Add(track);
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        SetColumn(_percent, 3);
        Children.Add(_percent);
    }

    public void Show(BuffLine line)
    {
        _icon.Source = Pictures.Load(line.IconPath);
        _name.Text = line.Name;
        _name.Foreground = line.Harmful ? Bad : Palette.OverlayText;
        _fill.Background = line.Harmful ? Bad : Good;
        _fill.Width = 68 * Math.Clamp(line.Percent / 100, 0, 1);
        _percent.Text = $"{line.Percent:0}%";
    }
}
