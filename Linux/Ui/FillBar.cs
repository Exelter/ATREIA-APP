using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AionDPS.Linux.Ui;

/// <summary>
/// A line's bar, drawn behind its text, as the Windows meter's usual look draws it (Client/Ui/MeterRowView.cs, the "glow" bar, and MeterBarPartsView): a faint
/// track over the whole line, the class colour as a glow over the share of the width the player has of the best, a fine bright line at its foot; after it the
/// bar's parts - grey for what the player's buffs gave the others, green for their healing on the others - each with its own fine line; and the class colour
/// as a thin edge at the line's left end.
/// </summary>
internal sealed class FillBar : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)).ToImmutable();

    // the parts (Windows: MeterBarParts.GivenColor, HealColor) as strong as the glow's class part, their lines in the full colour
    private static readonly IBrush Given = new SolidColorBrush(Color.FromArgb(0x46, 0xB4, 0xBD, 0xC8)).ToImmutable();
    private static readonly IBrush Heal = new SolidColorBrush(Color.FromArgb(0x46, 0x52, 0xD2, 0x86)).ToImmutable();
    private static readonly IBrush GivenLine = new SolidColorBrush(Color.FromArgb(0xC8, 0xB4, 0xBD, 0xC8)).ToImmutable();
    private static readonly IBrush HealLine = new SolidColorBrush(Color.FromArgb(0xD0, 0x52, 0xD2, 0x86)).ToImmutable();

    // an enemy's line in PvP (Windows: Brush.OverlayEnemy, Brush.DangerSoft)
    private static readonly IBrush EnemyFill = new SolidColorBrush(Color.Parse("#992E1719")).ToImmutable();
    private static readonly IBrush EnemyLine = new SolidColorBrush(Color.Parse("#F09A8E")).ToImmutable();

    private (double Own, double Given, double Heal, IBrush? Glow, IBrush? Line, IBrush? Edge) _shown;

    /// <summary>Sets what the bar shows (0-100 of the width: the own part's end, the grey part's, the green part's); draws again only when something changed.</summary>
    public void Set(double own, double givenEnd, double healEnd, IBrush glow, IBrush line, IBrush edge, bool enemy)
    {
        IBrush fill = enemy ? EnemyFill : glow;
        IBrush bottom = enemy ? EnemyLine : line;
        if (_shown.Own == own && _shown.Given == givenEnd && _shown.Heal == healEnd && ReferenceEquals(_shown.Glow, fill) && ReferenceEquals(_shown.Line, bottom)
            && ReferenceEquals(_shown.Edge, edge))
        {
            return;
        }

        _shown = (own, givenEnd, healEnd, fill, bottom, edge);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width, height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        context.FillRectangle(Track, new Rect(0, 0, width, height));
        double X(double percent) => width * Math.Clamp(double.IsFinite(percent) ? percent : 0, 0, 100) / 100;
        double x0 = X(_shown.Own), x1 = Math.Max(x0, X(_shown.Given)), x2 = Math.Max(x1, X(_shown.Heal));
        if (x0 > 0 && _shown.Glow is not null)
        {
            context.FillRectangle(_shown.Glow, new Rect(0, 0, x0, height));
            if (_shown.Line is not null)
            {
                context.FillRectangle(_shown.Line, new Rect(0, height - 1, x0, 1));
            }
        }

        if (x1 - x0 >= 0.5)
        {
            context.FillRectangle(Given, new Rect(x0, 0, x1 - x0, height));
            context.FillRectangle(GivenLine, new Rect(x0, height - 1, x1 - x0, 1));
        }

        if (x2 - x1 >= 0.5)
        {
            context.FillRectangle(Heal, new Rect(x1, 0, x2 - x1, height));
            context.FillRectangle(HealLine, new Rect(x1, height - 1, x2 - x1, 1));
        }

        if (_shown.Edge is not null)
        {
            context.FillRectangle(_shown.Edge, new Rect(0, 0, Math.Min(3, width), height));
        }
    }
}
