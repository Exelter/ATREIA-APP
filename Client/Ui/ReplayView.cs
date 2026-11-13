using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The fight from above: the group as class-coloured dots, the bosses as large ones with the way they face, the areas of the skills a boss is winding up in their own shape (a circle,
/// a ring, a cone, a line, a cross or the whole room; amber while it winds up, filling up to the moment it lands, red with a flash as it lands), the reach of the objects a boss leaves on
/// the floor while they stay (violet), the tiles of a board (Eternal Ludra's "bingo": amber filling up until each lights up, then red on its beat; the tiles that never light up
/// turn green once the last ones have, as does a square the board leaves out), waves spreading from where they set out, the boss's windup as a ring filling around it with the
/// skill's name, a red ring on whoever was hit, the dead crossed out, and the last few seconds of each walk as a trail. Positions are the game's own readings (it sends them with movements and skills, not on a regular beat), drawn straight between two of them. North is up, as on the
/// game's map (10 November 2026: the map was drawn upside down before). Drag to move the map, the wheel zooms, a double click fits it again.
/// </summary>
public sealed class ReplayView : FrameworkElement
{
    private const double TrailSeconds = 6, FlashSeconds = 0.6, LandSeconds = 0.45, TileBeatSeconds = 0.5;

    private ReplayData? _data;
    private IReadOnlyList<ReplayBoard> _boards = Array.Empty<ReplayBoard>();
    private HashSet<int> _tileActors = new();
    private double _time;
    private bool _showNames = true;
    private double _scale = 0.02; // pixels per centimetre
    private Point _origin; // the world point at the middle of the view
    private bool _fitted, _panning;
    private Point _panStart, _panOrigin;

    public ReplayView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public void Show(ReplayData data)
    {
        _data = data;
        _boards = ReplayBoards.Of(data.Areas);
        _tileActors = data.Areas.Where(a => a.Shape is { Shape: "square" }).Select(a => a.Actor).ToHashSet();
        _fitted = false;
        InvalidateVisual();
    }

    public double Time
    {
        get => _time;
        set
        {
            _time = value;
            InvalidateVisual();
        }
    }

    public bool ShowNames
    {
        get => _showNames;
        set
        {
            _showNames = value;
            InvalidateVisual();
        }
    }

    /// <summary>Fits the map to everything that was drawn, with a margin.</summary>
    public void Fit()
    {
        _fitted = false;
        InvalidateVisual();
    }

    private void FitNow()
    {
        if (_data is null || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        foreach (ReplayEntity e in _data.Entities)
        {
            // the middle 96 % of each walk: one wrong reading must not shrink the whole map
            var px = e.Track.X.OrderBy(v => v).ToArray();
            var py = e.Track.Y.OrderBy(v => v).ToArray();
            xs.Add(px[(int)(px.Length * 0.02)]);
            xs.Add(px[Math.Min(px.Length - 1, (int)(px.Length * 0.98))]);
            ys.Add(py[(int)(py.Length * 0.02)]);
            ys.Add(py[Math.Min(py.Length - 1, (int)(py.Length * 0.98))]);
        }

        foreach (ReplayArea a in _data.Areas)
        {
            if (a.Shape is { Shape: "wave" } or { Room: true })
            {
                // a wave may run across the whole room, and the room is everywhere: only where it set out counts
                xs.Add(a.X);
                ys.Add(a.Y);
                continue;
            }

            xs.Add(a.X - a.Radius);
            xs.Add(a.X + a.Radius);
            ys.Add(a.Y - a.Radius);
            ys.Add(a.Y + a.Radius);
        }

        if (xs.Count == 0)
        {
            return;
        }

        double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
        double w = Math.Max(maxX - minX, 800), h = Math.Max(maxY - minY, 800);
        _scale = Math.Min((ActualWidth - 60) / w, (ActualHeight - 60) / h);
        _origin = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        _fitted = true;
    }

    // the game's y grows to the south, as on its map (Aion2/Protocol/Aion2Maps.cs): down the screen
    private Point ToScreen(double x, double y) => new(ActualWidth / 2 + (x - _origin.X) * _scale, ActualHeight / 2 + (y - _origin.Y) * _scale);

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        var back = (Brush)(TryFindResource("Brush.Panel") ?? Brushes.Black);
        dc.DrawRectangle(back, null, new Rect(0, 0, width, height));
        if (_data is null || !_data.HasData)
        {
            return;
        }

        if (!_fitted)
        {
            FitNow();
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var muted = (Brush)(TryFindResource("Brush.TextMuted") ?? Brushes.Gray);
        var text = (Brush)(TryFindResource("Brush.Text") ?? Brushes.White);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0x9D, 0xB3, 0xC2)), 1);

        // a grid of ten metres, to show the distances
        double stepCm = 1000;
        double left = _origin.X - width / 2 / _scale, right = _origin.X + width / 2 / _scale, bottom = _origin.Y - height / 2 / _scale, top = _origin.Y + height / 2 / _scale;
        if (_scale * stepCm >= 14)
        {
            for (double gx = Math.Floor(left / stepCm) * stepCm; gx <= right; gx += stepCm)
            {
                dc.DrawLine(gridPen, ToScreen(gx, bottom), ToScreen(gx, top));
            }

            for (double gy = Math.Floor(bottom / stepCm) * stepCm; gy <= top; gy += stepCm)
            {
                dc.DrawLine(gridPen, ToScreen(left, gy), ToScreen(right, gy));
            }
        }

        // the areas: the reach of the objects on the floor first (under everything), then the windups
        foreach (ReplayArea area in _data.Areas.Where(a => a.Zone))
        {
            if (_time < area.Start || _time > area.End)
            {
                continue;
            }

            if (area.Shape is { Shape: "square" })
            {
                continue; // a tile: with its board, below
            }

            double fade = Math.Clamp(area.End - _time, 0, 1);

            // an object on the floor: where it is now (it may move), violet while it stays, fading in its last second
            var actor = _data.Entities.FirstOrDefault(e => e.Id == area.Actor);
            Point c = actor?.At(_data.Start, _time) is { } place ? ToScreen(place.X, place.Y) : ToScreen(area.X, area.Y);
            double r = area.Radius * _scale;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(0x38 * fade + 0x10), 0xA8, 0x6B, 0xFF)), new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xC0 * fade + 0x20), 0xB8, 0x86, 0xFF)), 1.5), c, r, r);
        }

        // the boards: their tiles, and the squares they leave out, where nothing lights up (green while the board is on)
        foreach (ReplayBoard board in _boards)
        {
            if (_time < board.Start || _time > board.End)
            {
                continue;
            }

            foreach (ReplayArea tile in board.Tiles)
            {
                if (_time >= tile.Start && _time <= tile.End)
                {
                    DrawTile(dc, tile, board, Math.Clamp(tile.End - _time, 0, 1));
                }
            }

            double side = board.SizeCm * _scale, fade = Math.Clamp(board.End - _time, 0, 1);
            var safePen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xE0 * fade), 0x5B, 0xE0, 0x8A)), 2) { DashStyle = DashStyles.Dash };
            foreach (var (gx, gy) in board.Gaps)
            {
                Point m = ToScreen(gx, gy);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(0x40 * fade), 0x5B, 0xE0, 0x8A)), safePen, new Rect(m.X - side / 2 + 2, m.Y - side / 2 + 2, Math.Max(1, side - 4), Math.Max(1, side - 4)));
            }
        }

        foreach (ReplayArea area in _data.Areas.Where(a => !a.Zone))
        {
            if (_time < area.Start || _time > area.End + LandSeconds)
            {
                continue;
            }

            if (area.Shape is { Shape: "wave" })
            {
                DrawWave(dc, area);
                continue;
            }

            bool landing = _time > area.End;
            double progress = Math.Clamp((_time - area.Start) / Math.Max(0.05, area.End - area.Start), 0, 1);
            Color fill = landing ? Color.FromArgb((byte)(0x80 * (1 - (_time - area.End) / LandSeconds)), 0xFF, 0x4D, 0x4D) : Color.FromArgb((byte)(0x16 + 0x40 * progress), 0xFF, 0xBA, 0x42);
            var pen = new Pen(new SolidColorBrush(landing ? Color.FromArgb(0xC0, 0xFF, 0x4D, 0x4D) : Color.FromArgb(0xC0, 0xFF, 0xBA, 0x42)), 1.5);
            if (!area.Sized)
            {
                pen.DashStyle = DashStyles.Dash; // nobody has measured this one: its size is a guess
            }

            if (area.Shape is { Room: true })
            {
                // the whole room: the edge of the map lights up
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(fill.A / 3), fill.R, fill.G, fill.B)), new Pen(pen.Brush, 6), new Rect(3, 3, Math.Max(0, width - 6), Math.Max(0, height - 6)));
                continue;
            }

            dc.DrawGeometry(new SolidColorBrush(fill), pen, AreaGeometry(area, 1));
            if (!landing && progress > 0.02)
            {
                // how far the windup is: the area fills up to the moment it lands
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(0x30 * progress), 0xFF, 0xBA, 0x42)), new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xBA, 0x42)), 1),
                    AreaGeometry(area, progress));
            }
            else if (landing)
            {
                // the hit: a white flash running just past its edge
                double k = Math.Clamp((_time - area.End) / LandSeconds, 0, 1);
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xD0 * (1 - k)), 0xFF, 0xFF, 0xFF)), 1 + 2.5 * (1 - k)), AreaGeometry(area, 1 + 0.08 * k));
            }
        }

        // trails, then the entities
        foreach (ReplayEntity e in _data.Entities.OrderBy(e => e.Role is ReplayRole.Add or ReplayRole.Mechanic ? 0 : e.Role == ReplayRole.Boss ? 2 : 1))
        {
            if (e.Role == ReplayRole.Mechanic)
            {
                // an object on the floor: a small violet diamond while it does something (its reach or a windup is on); a tile of a board is its square
                if (!_tileActors.Contains(e.Id) && _data.Areas.Any(a => a.Actor == e.Id && a.Start <= _time && _time <= a.End + LandSeconds) && e.At(_data.Start, _time) is { } spot)
                {
                    Point m = ToScreen(spot.X, spot.Y);
                    var diamond = new StreamGeometry();
                    using (StreamGeometryContext g = diamond.Open())
                    {
                        g.BeginFigure(new Point(m.X, m.Y - 5), true, true);
                        g.LineTo(new Point(m.X + 5, m.Y), true, false);
                        g.LineTo(new Point(m.X, m.Y + 5), true, false);
                        g.LineTo(new Point(m.X - 5, m.Y), true, false);
                    }

                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0xFF)), new Pen(Brushes.White, 1), diamond);
                }

                continue;
            }

            if (e.Role == ReplayRole.Add)
            {
                // another monster of the fight: a small marker where it is, no trail, areas or health
                if (e.At(_data.Start, _time) is { } addAt)
                {
                    Point a = ToScreen(addAt.X, addAt.Y);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x8C, 0x5A, 0x4A)), new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), 1), a, 4, 4);
                    if (_showNames)
                    {
                        var addLabel = new FormattedText(e.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 9, muted, dpi);
                        dc.DrawText(addLabel, new Point(a.X - addLabel.Width / 2, a.Y + 6));
                    }
                }

                continue;
            }

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 1);
            Point? previous = null;
            for (double t = Math.Max(0, _time - TrailSeconds); t <= _time; t += 0.5)
            {
                if (e.At(_data.Start, t) is { } p)
                {
                    Point s = ToScreen(p.X, p.Y);
                    if (previous is Point q)
                    {
                        dc.DrawLine(pen, q, s);
                    }

                    previous = s;
                }
            }

            if (e.At(_data.Start, _time) is not { } now)
            {
                continue;
            }

            Point at = ToScreen(now.X, now.Y);
            ReplayDeath? death = _data.Deaths.LastOrDefault(d => d.Entity == e.Id && d.At <= _time);
            bool dead = death is not null && e.Role == ReplayRole.Player && (death.Until is null || _time < death.Until);
            if (e.Role == ReplayRole.Boss)
            {
                double rb = Math.Max(9, 160 * _scale);
                ReplayArea? casting = _data.Areas.LastOrDefault(a => a.Actor == e.Id && !a.Zone && a.Start <= _time && _time <= a.End);
                if (casting is not null)
                {
                    // the boss winds up: a glow around it
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xBA, 0x42)), null, at, rb + 9, rb + 9);
                }

                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xC0, 0x45, 0x2E)), new Pen(Brushes.White, 1.5), at, rb, rb);
                ReplayArea? facing = _data.Areas.LastOrDefault(a => a.Actor == e.Id && a.Start <= _time && a.Start >= _time - 6);
                if (facing is not null)
                {
                    double ang = facing.Facing * Math.PI / 180;
                    dc.DrawLine(new Pen(Brushes.White, 2), at, new Point(at.X + Math.Cos(ang) * rb * 1.8, at.Y + Math.Sin(ang) * rb * 1.8));
                }

                if (casting is not null)
                {
                    // its windup as a ring filling up around it, the skill's name above
                    double share = Math.Clamp((_time - casting.Start) / Math.Max(0.05, casting.End - casting.Start), 0, 1);
                    var amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xBA, 0x42)), 3), at, rb + 5, rb + 5);
                    dc.DrawGeometry(null, new Pen(amber, 3), Arc(at, rb + 5, share));
                    if (!string.IsNullOrWhiteSpace(casting.SkillName))
                    {
                        var cast = new FormattedText(casting.SkillName, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 10, amber, dpi);
                        dc.DrawText(cast, new Point(at.X - cast.Width / 2, at.Y - rb - 9 - cast.Height));
                    }
                }

                var label = new FormattedText(e.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 11, text, dpi);
                dc.DrawText(label, new Point(at.X - label.Width / 2, at.Y + rb + 2));
            }
            else
            {
                Brush colour = dead ? Brushes.DimGray : ClassBrushes.Solid(e.ClassName);
                // the own character between two readings of his place: a dashed ring says the spot is an estimate
                bool estimated = e.EstimatedAt(_data.Start, _time);
                dc.DrawEllipse(colour, new Pen(Brushes.Black, 1), at, 6, 6);
                if (estimated)
                {
                    dc.DrawEllipse(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash }, at, 10, 10);
                }
                if (dead)
                {
                    var cross = new Pen(Brushes.OrangeRed, 2);
                    dc.DrawLine(cross, new Point(at.X - 6, at.Y - 6), new Point(at.X + 6, at.Y + 6));
                    dc.DrawLine(cross, new Point(at.X - 6, at.Y + 6), new Point(at.X + 6, at.Y - 6));
                    // how long they have been dead
                    var span = new FormattedText("☠ " + TimeSpan.FromSeconds(Math.Max(0, _time - death!.At)).ToString(@"m\:ss"), System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 10, Brushes.OrangeRed, dpi);
                    dc.DrawText(span, new Point(at.X - span.Width / 2, at.Y - 22));
                }

                // the hit it just took: a red ring
                double flash = _data.Hits.Where(h => h.Victim == e.Id && h.At <= _time && _time - h.At <= FlashSeconds).Select(h => 1 - (_time - h.At) / FlashSeconds).DefaultIfEmpty(0).Max();
                if (flash > 0)
                {
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * flash), 0xFF, 0x3B, 0x3B)), 3), at, 6 + 10 * (1 - flash), 6 + 10 * (1 - flash));
                }

                if (_showNames)
                {
                    var label = new FormattedText(e.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 10, dead ? muted : text, dpi);
                    dc.DrawText(label, new Point(at.X - label.Width / 2, at.Y + 8));
                }
            }
        }

        // the time and what the boss is doing
        var status = new List<string> { TimeSpan.FromSeconds(_time).ToString(@"m\:ss") };
        ReplayHp? hp = _data.Hp.FirstOrDefault();
        if (hp is not null)
        {
            int i = Array.FindLastIndex(hp.Seconds, s => s <= _time);
            if (i >= 0)
            {
                status.Add($"{hp.Fraction[i] * 100:0}%");
            }
        }

        // what lands now, else a board's tiles that are lit
        ReplayArea? current = _data.Areas.LastOrDefault(a => !a.Zone && a.Start <= _time && a.End + LandSeconds >= _time)
            ?? _data.Areas.LastOrDefault(a => a.Zone && a.LitAt is double lit && lit <= _time && _time <= a.End);
        if (current is not null && !string.IsNullOrWhiteSpace(current.SkillName))
        {
            status.Add(current.SkillName);
        }

        if (_data.Staggers.Any(s => s.Start <= _time && s.End >= _time))
        {
            status.Add(LocalizationManager.Instance["Replay.Staggered"]);
        }

        if (_data.Entities.Any(e => e.EstimatedAt(_data.Start, _time)))
        {
            status.Add(LocalizationManager.Instance["Replay.OwnEstimated"]);
        }

        var header = new FormattedText(string.Join("  ·  ", status), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 13, text, dpi);
        dc.DrawText(header, new Point(10, 8));
        double bar = 10 * _scale * 100; // ten metres
        if (bar >= 20)
        {
            var scalePen = new Pen(muted, 2);
            dc.DrawLine(scalePen, new Point(10, height - 14), new Point(10 + bar, height - 14));
            var meters = new FormattedText("10 m", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiTypeface(), 10, muted, dpi);
            dc.DrawText(meters, new Point(14 + bar, height - 22));
        }
    }

    /// <summary>
    /// A tile of a board: amber from when it appeared, filling up to the moment it lights up; then red, flashing on the beat it hurts at. A tile that never lights up is an
    /// amber outline while the board's other tiles are still to light up, then green. Violet when the moments are not known (a fight kept before 2.0.15). <paramref name="fade"/>:
    /// 1 until its last second.
    /// </summary>
    private void DrawTile(DrawingContext dc, ReplayArea area, ReplayBoard board, double fade)
    {
        Point mid = ToScreen(area.X, area.Y);
        double side = Math.Max(2, (area.Shape?.SizeCm ?? board.SizeCm) * _scale - 2);
        var rect = new Rect(mid.X - side / 2, mid.Y - side / 2, side, side);
        if (area.LitAt is double lit && _time >= lit)
        {
            double beat = 1 - (_time - lit) % TileBeatSeconds / TileBeatSeconds;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)((0x48 + 0x58 * beat) * fade), 0xFF, 0x3D, 0x5A)),
                new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xE8 * fade), 0xFF, 0x9A, 0xA8)), 1.5), rect);
            return;
        }

        if (area.LitAt is double soon)
        {
            double progress = Math.Clamp((_time - area.Start) / Math.Max(0.05, soon - area.Start), 0, 1), inner = side * progress;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xBA, 0x42)), new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xBA, 0x42)), 1), rect);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(0x18 + 0x48 * progress), 0xFF, 0xBA, 0x42)), null, new Rect(mid.X - inner / 2, mid.Y - inner / 2, inner, inner));
            return;
        }

        if (board.Known)
        {
            if (board.LastLit is double last && _time >= last)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(0x30 * fade), 0x5B, 0xE0, 0x8A)), new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xB0 * fade), 0x5B, 0xE0, 0x8A)), 1), rect);
            }
            else
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xBA, 0x42)), new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xBA, 0x42)), 1), rect);
            }

            return;
        }

        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(0x30 * fade + 0x10), 0xA8, 0x6B, 0xFF)), new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xC0 * fade + 0x20), 0xB8, 0x86, 0xFF)), 1), rect);
    }

    /// <summary>A wave: a band spreading from where it set out at its speed, ahead of the way it faced (all round when the half angle is 180), as far as it reaches (dotted).</summary>
    private void DrawWave(DrawingContext dc, ReplayArea area)
    {
        var shape = area.Shape!;
        double speed = shape.SpeedCmS is > 0.0 ? shape.SpeedCmS.Value : 1000, width = shape.WidthCm is > 0.0 ? shape.WidthCm.Value : 300, reach = area.Radius;
        double half = shape.HalfAngle ?? 180, front = (_time - area.Start) * speed;
        if (front <= 0 || front - width >= reach)
        {
            return;
        }

        double outer = Math.Min(front, reach), inner = Math.Max(0, front - width), fade = 1 - 0.55 * Math.Clamp(front / Math.Max(1, reach), 0, 1);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x6A, 0x3D)), 1) { DashStyle = DashStyles.Dot }, Sector(area, reach, 0, half));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(0xA8 * fade), 0xFF, 0x5A, 0x3D)), new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xF0 * fade), 0xFF, 0xD8, 0xB0)), 1.5),
            Sector(area, outer, inner, half));
    }

    /// <summary>The band between two distances from an area's spot (centimetres), within <paramref name="halfDeg"/> of its facing: a ring all round, else a slice of one.</summary>
    private Geometry Sector(ReplayArea area, double outerCm, double innerCm, double halfDeg)
    {
        Point c = ToScreen(area.X, area.Y);
        double ro = Math.Max(0.5, outerCm * _scale), ri = innerCm * _scale;
        if (halfDeg >= 179.5)
        {
            Geometry disc = new EllipseGeometry(c, ro, ro);
            return ri > 0.5 ? new CombinedGeometry(GeometryCombineMode.Exclude, disc, new EllipseGeometry(c, ri, ri)) : disc;
        }

        double th = area.Facing * Math.PI / 180, half = halfDeg * Math.PI / 180;
        bool large = half * 2 > Math.PI;
        Point P(double r, double a) => new(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(P(ri, th - half), true, true);
            ctx.LineTo(P(ro, th - half), true, false);
            ctx.ArcTo(P(ro, th + half), new Size(ro, ro), 0, large, SweepDirection.Clockwise, true, false);
            if (ri > 0.5)
            {
                ctx.LineTo(P(ri, th + half), true, false);
                ctx.ArcTo(P(ri, th - half), new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true, false);
            }
        }

        return g;
    }

    /// <summary><paramref name="share"/> of a circle around <paramref name="c"/>, from the top, clockwise.</summary>
    private static Geometry Arc(Point c, double r, double share)
    {
        if (share >= 0.999)
        {
            return new EllipseGeometry(c, r, r);
        }

        double a0 = -Math.PI / 2, a1 = a0 + Math.Max(0.001, share) * 2 * Math.PI;
        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r), false, false);
            ctx.ArcTo(new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r), new Size(r, r), 0, share > 0.5, SweepDirection.Clockwise, true, false);
        }

        return g;
    }

    /// <summary>
    /// The shape of an area on the screen, <paramref name="share"/> of its size (the windup's progress grows inside it): a circle around the spot the windup aims at, a ring,
    /// a cone around the facing, a line from just behind the spot ahead along the facing, a cross of two such bars. Sizes measured on recorded fights, else the size the meter learnt, else a guess.
    /// </summary>
    private Geometry AreaGeometry(ReplayArea area, double share)
    {
        Point c = ToScreen(area.X, area.Y);
        double th = area.Facing * Math.PI / 180;
        var (dx, dy) = (Math.Cos(th), Math.Sin(th)); // on the screen as in the world: y grows down
        var shape = area.Shape;
        double r = area.Radius * _scale * share;
        Point At(double along, double across) => new(c.X + (dx * along - dy * across) * _scale, c.Y + (dy * along + dx * across) * _scale);
        Geometry Bar(double from, double to, double half, bool turned)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                Point P(double a, double b) => turned ? At(-b, a) : At(a, b);
                ctx.BeginFigure(P(from, -half), true, true);
                ctx.LineTo(P(to, -half), true, false);
                ctx.LineTo(P(to, half), true, false);
                ctx.LineTo(P(from, half), true, false);
            }

            return g;
        }

        switch (shape?.Shape)
        {
            case "ring":
            {
                double inner = (shape!.InnerCm ?? area.Radius * 0.5) * _scale * share;
                return new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r, r), new EllipseGeometry(c, inner, inner));
            }

            case "cone":
            {
                double half = (shape!.HalfAngle ?? Aion2.Protocol.Aion2MechanicAreas.GuessHalfAngle) * Math.PI / 180;
                if (half >= Math.PI - 0.01)
                {
                    return new EllipseGeometry(c, r, r);
                }

                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(c, true, true);
                    ctx.LineTo(new Point(c.X + Math.Cos(th - half) * r, c.Y + Math.Sin(th - half) * r), true, false);
                    ctx.ArcTo(new Point(c.X + Math.Cos(th + half) * r, c.Y + Math.Sin(th + half) * r), new Size(r, r), 0, half * 2 > Math.PI, SweepDirection.Clockwise, true, false);
                }

                return g;
            }

            case "line":
            {
                double length = (shape!.LengthCm ?? Math.Max(area.Radius * 2, 900)) * share, half = shape!.WidthCm ?? Aion2.Protocol.Aion2MechanicAreas.GuessWidthCm;
                return Bar(-(shape!.BehindCm ?? 0) * share, length, half, false);
            }

            case "cross":
            {
                double length = (shape!.LengthCm ?? Math.Max(area.Radius * 2, 900)) * share, half = shape!.WidthCm ?? Aion2.Protocol.Aion2MechanicAreas.GuessWidthCm;
                return new CombinedGeometry(GeometryCombineMode.Union, Bar(-length, length, half, false), Bar(-length, length, half, true));
            }

            default:
                return new EllipseGeometry(c, r, r);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point mouse = e.GetPosition(this);
        double worldX = _origin.X + (mouse.X - ActualWidth / 2) / _scale, worldY = _origin.Y + (mouse.Y - ActualHeight / 2) / _scale;
        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.002, 1.0);
        _origin = new Point(worldX - (mouse.X - ActualWidth / 2) / _scale, worldY - (mouse.Y - ActualHeight / 2) / _scale);
        _fitted = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            Fit();
            return;
        }

        _panning = true;
        _panStart = e.GetPosition(this);
        _panOrigin = _origin;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            Point now = e.GetPosition(this);
            _origin = new Point(_panOrigin.X - (now.X - _panStart.X) / _scale, _panOrigin.Y - (now.Y - _panStart.Y) / _scale);
            _fitted = true;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _panning = false;
        ReleaseMouseCapture();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _fitted = false;
    }
}

/// <summary>The strip under the map: the whole fight with its big attacks (amber), its boards (violet), the hits on the group (red), deaths (x), staggers (shaded) and the boss's
/// health as a line; click or drag on it to go to that moment.</summary>
public sealed class ReplayTimeline : FrameworkElement
{
    private ReplayData? _data;
    private IReadOnlyList<ReplayBoard> _boards = Array.Empty<ReplayBoard>();
    private double _time;
    private bool _dragging;

    public event Action<double>? Seek;

    public ReplayTimeline()
    {
        Cursor = Cursors.Hand;
        Height = 54;
    }

    public void Show(ReplayData data)
    {
        _data = data;
        _boards = ReplayBoards.Of(data.Areas);
        InvalidateVisual();
    }

    public double Time
    {
        set
        {
            _time = value;
            InvalidateVisual();
        }
    }

    private double X(double seconds) => 8 + seconds / Math.Max(1, _data?.DurationSeconds ?? 1) * Math.Max(1, ActualWidth - 16);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_data is null)
        {
            return;
        }

        var border = (Brush)(TryFindResource("Brush.Border") ?? Brushes.DimGray);
        dc.DrawLine(new Pen(border, 1), new Point(8, h - 10), new Point(w - 8, h - 10));
        foreach (StaggerPhase s in _data.Staggers)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0xB0, 0x6B, 0xFF)), null, new Rect(X(s.Start), 2, Math.Max(2, X(s.End) - X(s.Start)), h - 12));
        }

        if (_data.Hp.FirstOrDefault() is { } hp)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x5B, 0xD0, 0x7A)), 1.2);
            for (int i = 1; i < hp.Seconds.Length; i++)
            {
                dc.DrawLine(pen, new Point(X(hp.Seconds[i - 1]), h - 12 - (h - 24) * hp.Fraction[i - 1]), new Point(X(hp.Seconds[i]), h - 12 - (h - 24) * hp.Fraction[i]));
            }
        }

        // the boards (a violet bar for as long as each is on), under the attacks
        foreach (ReplayBoard b in _boards)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x70, 0xB8, 0x86, 0xFF)), null, new Rect(X(b.Start), 4, Math.Max(2, X(b.End) - X(b.Start)), 12));
        }

        var amber = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xBA, 0x42)), 1);
        foreach (ReplayArea a in _data.Areas.Where(a => !a.Zone))
        {
            dc.DrawLine(amber, new Point(X(a.Start), 4), new Point(X(a.Start), 16));
        }

        var red = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x4D, 0x4D)), 1);
        foreach (ReplayHit hit in _data.Hits)
        {
            dc.DrawLine(red, new Point(X(hit.At), 18), new Point(X(hit.At), 26));
        }

        var cross = new Pen(Brushes.OrangeRed, 1.5);
        foreach (ReplayDeath d in _data.Deaths)
        {
            double x = X(d.At);
            dc.DrawLine(cross, new Point(x - 4, 30), new Point(x + 4, 38));
            dc.DrawLine(cross, new Point(x - 4, 38), new Point(x + 4, 30));
            // the time dead as a bar to the revive (to the end when there was none)
            double until = X(d.Until ?? _data.DurationSeconds);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x45, 0x00)), 3), new Point(x, 41), new Point(Math.Max(x + 2, until), 41));
        }

        var accent = (Brush)(TryFindResource("Brush.Accent") ?? Brushes.Orange);
        dc.DrawLine(new Pen(accent, 2), new Point(X(_time), 0), new Point(X(_time), h - 4));
    }

    private void SeekTo(Point p)
    {
        if (_data is null)
        {
            return;
        }

        Seek?.Invoke(Math.Clamp((p.X - 8) / Math.Max(1, ActualWidth - 16), 0, 1) * _data.DurationSeconds);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        SeekTo(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            SeekTo(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }
}
