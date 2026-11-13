namespace AionDPS.Ui;

/// <summary>
/// The row's WPF colours: the class brushes of the bars and names (<see cref="ClassBrushes"/>). Kept apart from PlayerRow.cs, which has no WPF in it,
/// so the Linux app (Linux/) builds the same row with its own brushes.
/// </summary>
public sealed partial class PlayerRow
{
    /// <summary>The class colour, translucent, for that bar. Same colours as the website's meter.</summary>
    public System.Windows.Media.Brush ClassBrush => ClassBrushes.For(ClassName);

    /// <summary>The edge of the bar and the name's colour in the in-game meter.</summary>
    public System.Windows.Media.Brush ClassEdgeBrush => ClassBrushes.Edge(ClassName);

    public System.Windows.Media.Brush ClassTextBrush => ClassBrushes.Text(ClassName);

    /// <summary>The bar's glow and the bright line along its bottom (the new look of both views).</summary>
    public System.Windows.Media.Brush ClassGlowBrush => ClassBrushes.Glow(ClassName);

    public System.Windows.Media.Brush ClassLineBrush => ClassBrushes.Line(ClassName);

    /// <summary>The bar colour setting changed: bind the bar to the new brush.</summary>
    public void RefreshClassBrush()
    {
        OnPropertyChanged(nameof(ClassBrush));
        OnPropertyChanged(nameof(ClassEdgeBrush));
        OnPropertyChanged(nameof(ClassTextBrush));
        OnPropertyChanged(nameof(ClassGlowBrush));
        OnPropertyChanged(nameof(ClassLineBrush));
    }
}

/// <summary>One party buff on a player over the fight shown: its icon, the share of the fight it was on, and the colour of the class that gave it.</summary>
public sealed record SupportBadge(string Name, System.Windows.Media.ImageSource? Icon, int Percent, System.Windows.Media.Brush Edge, string Tip)
{
    public string PercentText => Percent + "%";
}
