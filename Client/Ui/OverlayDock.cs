namespace AionDPS.Ui;

/// <summary>Where an overlay hangs on another one: the side of the parent it touches and how far along that side it starts.</summary>
public sealed class OverlayDock
{
    public string Parent { get; set; } = "";

    /// <summary>Left, Right, Top or Bottom: the side of the parent the overlay touches.</summary>
    public string Side { get; set; } = "Bottom";

    /// <summary>Distance of the overlay's start from the parent's start along the shared side (in screen units).</summary>
    public double Offset { get; set; }
}
