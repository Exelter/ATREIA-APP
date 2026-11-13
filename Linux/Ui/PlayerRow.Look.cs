using Avalonia.Media;
using AionDPS.Linux.Ui;

namespace AionDPS.Ui;

/// <summary>
/// The Linux side of the shared player row (Client/Ui/PlayerRow.cs): its class colours as Avalonia brushes, where the Windows app's PlayerRow.Look.cs has
/// WPF's. Same names, so the row is the same class on both.
/// </summary>
public sealed partial class PlayerRow
{
    public IBrush ClassBrush => ClassLook.Bar(ClassName);

    public IBrush ClassEdgeBrush => ClassLook.Edge(ClassName);

    public IBrush ClassTextBrush => ClassLook.Text(ClassName);

    public IBrush ClassGlowBrush => ClassLook.Glow(ClassName);

    public IBrush ClassLineBrush => ClassLook.Line(ClassName);

    /// <summary>The bar colours changed: the views read the brushes again.</summary>
    public void RefreshClassBrush()
    {
        OnPropertyChanged(nameof(ClassBrush));
        OnPropertyChanged(nameof(ClassEdgeBrush));
        OnPropertyChanged(nameof(ClassTextBrush));
        OnPropertyChanged(nameof(ClassGlowBrush));
        OnPropertyChanged(nameof(ClassLineBrush));
    }
}

/// <summary>One party buff on a player over the fight shown (the Windows app's has its WPF picture and brush): the picture's file and the class colour.</summary>
public sealed record SupportBadge(string Name, string? IconPath, int Percent, IBrush Edge, string Tip)
{
    public string PercentText => Percent + "%";
}
