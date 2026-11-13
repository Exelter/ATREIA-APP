using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AionDPS.Linux.Ui;

/// <summary>
/// A line's bar, drawn behind its text: the class colour as a glow over the share of the width the player's figure has of the best one, a bright line along
/// its bottom and a thin edge at its end (the Windows meter's look, Client/Ui/MeterRowView.cs). In the damage-taken view a blue bar behind it shows what
/// the shields took on top.
/// </summary>
internal sealed class FillBar : Control
{
    private static readonly IBrush AbsorbBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x6A, 0x9C, 0xE0)).ToImmutable();

    private double _fill;
    private double _absorbFill;
    private IBrush? _glow;
    private IBrush? _line;
    private IBrush? _edge;

    /// <summary>Sets what the bar shows (0-100 of the width); draws again only when something changed.</summary>
    public void Set(double fillPercent, double absorbFillPercent, IBrush glow, IBrush line, IBrush edge)
    {
        if (_fill == fillPercent && _absorbFill == absorbFillPercent && ReferenceEquals(_glow, glow) && ReferenceEquals(_line, line) && ReferenceEquals(_edge, edge))
        {
            return;
        }

        _fill = fillPercent;
        _absorbFill = absorbFillPercent;
        _glow = glow;
        _line = line;
        _edge = edge;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width, height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_absorbFill > 0)
        {
            context.FillRectangle(AbsorbBrush, new Rect(0, 0, width * Math.Clamp(_absorbFill, 0, 100) / 100, height));
        }

        double filled = width * Math.Clamp(_fill, 0, 100) / 100;
        if (filled <= 0 || _glow is null)
        {
            return;
        }

        context.FillRectangle(_glow, new Rect(0, 0, filled, height));
        if (_line is not null)
        {
            context.FillRectangle(_line, new Rect(0, height - 2, filled, 2));
        }

        if (_edge is not null && filled >= 2)
        {
            context.FillRectangle(_edge, new Rect(filled - 2, 0, 2, height));
        }
    }
}
