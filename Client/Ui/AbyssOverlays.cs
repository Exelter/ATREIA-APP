using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using Shapes = System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>One enemy on the radar: who, how strong against the own character, and his record against it.</summary>
public sealed record RadarEnemy(int Id, string Name, string? ClassName, int? CombatPower, int? ItemLevel, double? Against, Rival? Rival, DateTime Since);

/// <summary>Whose side a player on the radar is on.</summary>
public enum RadarSide { Enemy, Party, Legion, Ally, Unknown }

/// <summary>A player on the radar's disc: metres east and south of the own character, his side, a much stronger enemy, and the name shown beside him.</summary>
public sealed record RadarDot(int Id, double East, double South, RadarSide Side, bool Strong = false, string? Label = null);

/// <summary>What the radar shows: the enemies in view, the strongest first, and a much stronger one that just came (null: none); the players around on the
/// disc, how many of each side are close, and the own kills, assists and deaths in this zone.</summary>
public sealed record RadarView(IReadOnlyList<RadarEnemy> Enemies, RadarEnemy? Warning, bool Sample = false)
{
    /// <summary>A newcomer this much stronger than the own character is announced.</summary>
    public const double WarnAbove = 0.20;

    /// <summary>Metres from the own character to the disc's edge (who is farther sits on the rim).</summary>
    public const double Range = 50;

    /// <summary>The inner ring: who is this close counts in the fight at hand.</summary>
    public const double Near = 25;

    public IReadOnlyList<RadarDot> Dots { get; init; } = Array.Empty<RadarDot>();

    /// <summary>Whether the own position is known (no dots without it).</summary>
    public bool Placed { get; init; }

    /// <summary>The own side in view: the party, the legion and the rest of the faction.</summary>
    public int Allies { get; init; }

    public int EnemiesNear { get; init; }

    public int AlliesNear { get; init; }

    public (int Kills, int Assists, int Deaths) Tally { get; init; }

    /// <summary>The own faction (null: not known yet); for the log, the players in view and how many of them had no faction.</summary>
    public string? Faction { get; init; }

    public int InView { get; init; }

    public int NoFaction { get; init; }

    private static string? FactionOfServer(int server) => server / 1000 == 1 ? "Elyos" : server / 1000 == 2 ? "Asmodian" : null;

    /// <summary>
    /// The players in view now. An enemy is a player of the other faction, or one who traded hits with the own character (11 November 2026: in the Abyss most
    /// came with no server, and those whose "player appeared" frame was missed had no faction at all). The own faction is that of the own server, else the
    /// other side of a player who traded hits with it. A player's place comes from the movement frames; he is on the disc while it is under 20 s old.
    /// </summary>
    public static RadarView Of(Aion2EntityDirectory dir, int? ownPower, DateTime now, Func<int, string?, (int? Power, int? Items)?> looked, (int, int, int) tally = default)
    {
        IReadOnlyList<(int Id, DateTime Since)> inView = dir.PlayersInView();
        string? mine = dir.LocalPlayerId >= 0 ? dir.FactionOf(dir.LocalPlayerId) : null;
        mine ??= dir.LocalCharacter is { ServerId: > 0 } me ? FactionOfServer(me.ServerId) : null;
        if (mine is null)
        {
            foreach ((int id, _) in inView)
            {
                if (dir.IsHostile(id) && dir.FactionOf(id) is { } other)
                {
                    mine = other == "Elyos" ? "Asmodian" : "Elyos";
                    break;
                }
            }
        }

        IReadOnlySet<string> party = dir.PartyNames;
        var legion = new HashSet<string>(dir.LegionRoster().Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        (float X, float Y, DateTime At)? self = dir.BestPosition;
        DateTime clock = DateTime.UtcNow;
        var list = new List<RadarEnemy>();
        var dots = new List<RadarDot>();
        int allies = 0, enemiesNear = 0, alliesNear = 0, players = 0, noFaction = 0;
        foreach ((int id, DateTime since) in inView)
        {
            if (id == dir.LocalPlayerId)
            {
                continue;
            }

            players++;
            string? theirs = dir.FactionOf(id);
            noFaction += theirs is null ? 1 : 0;
            string name = dir.NameFor(id) ?? "Unknown player";
            // a member of the own group is never an enemy, whatever is read of his faction (11 November 2026: one came up as an enemy in a dungeon)
            bool enemy = !party.Contains(name) && (dir.IsHostile(id) || (theirs is not null && mine is not null && theirs != mine));
            RadarSide side = enemy ? RadarSide.Enemy : party.Contains(name) ? RadarSide.Party : legion.Contains(name) ? RadarSide.Legion
                : theirs is not null && theirs == mine ? RadarSide.Ally : RadarSide.Unknown;
            RadarEnemy? e = null;
            if (enemy)
            {
                Aion2SeenProfile? profile = dir.SeenProfileOf(id);
                int server = dir.ServerIdOf(id) ?? 0;
                int? power = profile?.CombatPower is int cp and > 0 and not 397228 ? cp : null;
                int? items = profile?.ItemLevel is > 0 ? profile.ItemLevel : null;
                if ((power is null || items is null) && looked(id, name) is { } got)
                {
                    power ??= got.Power;
                    items ??= got.Items;
                }

                string? className = profile?.ClassId is int classId ? Aion2SkillNames.ClassFromCode(4 * classId + 1) : dir.ClassOf(id);
                double? against = power is int p && ownPower is int o && o > 0 ? (double)p / o - 1 : null;
                e = new RadarEnemy(id, name, className, power, items, against, Aion2Pvp.RivalOf(name, server), since);
                list.Add(e);
            }
            else if (side != RadarSide.Unknown)
            {
                allies++;
            }

            if (self is { } s && dir.PositionOf(id) is { } at && clock - at.At <= TimeSpan.FromSeconds(20))
            {
                double east = (at.X - s.X) / 100.0, south = (at.Y - s.Y) / 100.0;
                dots.Add(new RadarDot(id, east, south, side, e?.Against >= WarnAbove));
                if (Math.Sqrt(east * east + south * south) <= Near)
                {
                    enemiesNear += enemy ? 1 : 0;
                    alliesNear += !enemy && side != RadarSide.Unknown ? 1 : 0;
                }
            }
        }

        list = list.OrderByDescending(e => e.CombatPower.HasValue).ThenByDescending(e => e.CombatPower ?? 0).ThenBy(e => e.Name, StringComparer.CurrentCulture).ToList();
        RadarEnemy? warning = list.Where(e => e.Against >= WarnAbove && now - e.Since <= TimeSpan.FromSeconds(12)).OrderByDescending(e => e.CombatPower).FirstOrDefault();
        return new RadarView(list, warning)
        {
            Dots = Labelled(dots, list), Placed = self is not null, Allies = allies, EnemiesNear = enemiesNear, AlliesNear = alliesNear, Tally = tally, Faction = mine,
            InView = players, NoFaction = noFaction,
        };
    }

    // the strongest enemy on the disc carries his name
    private static IReadOnlyList<RadarDot> Labelled(List<RadarDot> dots, IReadOnlyList<RadarEnemy> enemies)
    {
        foreach (RadarEnemy e in enemies)
        {
            int i = dots.FindIndex(d => d.Id == e.Id);
            if (i >= 0)
            {
                dots[i] = dots[i] with { Label = e.Name };
                break;
            }
        }

        return dots;
    }

    /// <summary>What the radar shows while it is placed with no enemy around (and the pictures).</summary>
    public static RadarView SampleView(DateTime now)
    {
        var kraeger = new RadarEnemy(1, "Kraeger", "Templar", 231_400, 3_120, 0.12, new Rival { Name = "Kraeger", Met = 4, Kills = 2, Deaths = 2, KilledBy = 1 }, now);
        var enemies = new List<RadarEnemy>
        {
            kraeger,
            new(2, "Vesh", "Assassin", 210_700, 3_044, 0.02, null, now),
            new(3, "Morrigane", "Sorcerer", 190_100, 2_967, -0.08, new Rival { Name = "Morrigane", Met = 1, Kills = 1 }, now),
            new(4, "Toskar", "Gladiator", 163_200, 2_880, -0.21, null, now),
            new(5, "Ilyre", "Cleric", null, null, null, null, now),
        };
        string[] more = { "Draven", "Sylka", "Orrin", "Quessa", "Thalor", "Brisk", "Neveth" };
        for (int i = 0; i < more.Length; i++)
        {
            enemies.Add(new RadarEnemy(6 + i, more[i], null, null, null, null, null, now));
        }

        // a fixed scatter: the own side gathered south-west of you, the enemies pushing in from the north-east
        var rnd = new Random(7);
        var dots = new List<RadarDot>
        {
            new(1, 14, -17, RadarSide.Enemy, Strong: true, Label: "Kraeger"), new(2, 18, -9, RadarSide.Enemy), new(3, 22, -21, RadarSide.Enemy),
            new(4, 8, -23, RadarSide.Enemy), new(5, 26, -13, RadarSide.Enemy),
            new(100, -3, 2, RadarSide.Party), new(101, 2.5, -3.5, RadarSide.Party), new(102, -5, -2.5, RadarSide.Party), new(103, 3.5, 3, RadarSide.Party),
        };
        double[][] far = { new[] { 31.0, -28 }, new[] { 36.0, -19 }, new[] { 28.0, -37 }, new[] { 41.0, -30 }, new[] { 34.0, -42 }, new[] { 62.0, -45 }, new[] { 70.0, -12 } };
        for (int i = 0; i < far.Length; i++)
        {
            dots.Add(new RadarDot(6 + i, far[i][0], far[i][1], RadarSide.Enemy));
        }

        for (int i = 0; i < 5; i++)
        {
            dots.Add(new RadarDot(200 + i, -9 + rnd.NextDouble() * 8, 4 + rnd.NextDouble() * 8, RadarSide.Legion));
        }

        for (int i = 0; i < 26; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, d = Math.Sqrt(rnd.NextDouble()) * 15;
            dots.Add(new RadarDot(300 + i, -13 + Math.Cos(a) * d, 11 + Math.Sin(a) * d, RadarSide.Ally));
        }

        return new RadarView(enemies, kraeger, Sample: true)
        {
            Dots = dots, Placed = true, Allies = 35, EnemiesNear = 4, AlliesNear = 21, Tally = (3, 7, 1), Faction = "Asmodian", InView = 47,
        };
    }
}

/// <summary>
/// The Abyss radar (10 November 2026, the owner picked: "a list of every enemy player near you, strongest first, with class and CP; it warns you when someone
/// much stronger shows up"; 11 November 2026, the owner: "the radar should be something like this", a round war map). Every player in view on a disc around
/// the own character, north up, 50 m to the edge (who is farther sits on the rim): the enemies red with a haze where they gather, the party blue, the
/// legion violet, the rest of the own faction green; the strongest enemy named. Under it who has the numbers within 25 m, the own kills, assists and deaths
/// in this zone, and the three strongest enemies with their class, Combat Power against the own and the record against each (<see cref="Aion2Pvp"/>). A
/// newcomer 20 % stronger than the own character shows in red at the top for a few seconds. The players come and go with the game's own frames (a player
/// appeared, 0x4536; a player left, 0x4736); their places with the movement frames. Over the game only; with no enemy in view it is not there.
/// </summary>
public sealed class EnemyRadarWindow : MovableOverlay
{
    private const int Strongest = 3;
    private const double DiscSize = 232;
    private static readonly Color Enemy = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color PartyBlue = Color.FromRgb(0x6F, 0xA8, 0xFF);
    private static readonly Color LegionViolet = Color.FromRgb(0xB4, 0x8C, 0xFF);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private readonly ContentControl _content = new() { MinWidth = 262, Focusable = false };
    private string _shown = "";

    public EnemyRadarWindow() : base(OverlayPlaces.EnemyRadar, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 330, SystemParameters.WorkArea.Top + 260,
        LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = _content;
        OverlaySkin.SolidBackground(this, OverlayPlaces.EnemyRadar);
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.EnemyRadarWidth, s.EnemyRadarHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.EnemyRadarWidth = width;
        s.EnemyRadarHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.EnemyRadarLeft, s.EnemyRadarTop, s.EnemyRadarScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.EnemyRadarLeft = left;
        s.EnemyRadarTop = top;
        s.EnemyRadarScale = scale;
        s.Save();
    }

    public new bool HasContent => _content.Content is not null;

    public void Render(RadarView? view)
    {
        string key = view is null ? "" : string.Join("|", view.Sample, view.Warning?.Id, view.Placed, view.Allies, view.EnemiesNear, view.AlliesNear, view.Tally,
            string.Join(",", view.Enemies.Take(Strongest + 1).Select(e => $"{e.Id}:{e.CombatPower}:{e.ClassName}:{e.Rival?.Kills}:{e.Rival?.Deaths}")), view.Enemies.Count,
            string.Join(",", view.Dots.Select(d => $"{d.Id}:{Math.Round(d.East)}:{Math.Round(d.South)}:{(int)d.Side}:{d.Label}")));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _content.Content = view is null || view.Enemies.Count == 0 ? null : View(view);
    }

    /// <summary>The radar (the pictures draw it too).</summary>
    public static FrameworkElement View(RadarView view)
    {
        var stack = new StackPanel();
        string note = view.Enemies.Count.ToString(CultureInfo.CurrentCulture) + (view.Enemies.Count == 1 ? " enemy" : " enemies")
            + (view.Allies > 0 ? "  ·  " + view.Allies.ToString(CultureInfo.CurrentCulture) + (view.Allies == 1 ? " ally" : " allies") : "");
        stack.Children.Add(OverlaySkin.Heading(view.Sample ? "Abyss radar · sample" : "Abyss radar", note, Enemy, Enemy));
        if (view.Warning is { } w)
        {
            stack.Children.Add(Warning(w));
        }

        stack.Children.Add(Disc(view));
        stack.Children.Add(Legend());
        stack.Children.Add(Balance(view));
        stack.Children.Add(Score(view.Tally));
        if (view.Enemies.Count > 0)
        {
            stack.Children.Add(OverlaySkin.Divider(7, 3));
            bool band = false;
            foreach (RadarEnemy e in view.Enemies.Take(Strongest))
            {
                stack.Children.Add(Row(e, band));
                band = !band;
            }

            if (view.Enemies.Count > Strongest)
            {
                stack.Children.Add(new TextBlock { Text = $"+ {view.Enemies.Count - Strongest} more", FontSize = 10.5, Foreground = new SolidColorBrush(Faint), Margin = new Thickness(6, 2, 0, 0) });
            }
        }

        return OverlaySkin.Panel(stack, new Thickness(11, 8, 11, 9));
    }

    private static Border Warning(RadarEnemy w)
    {
        var warn = new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 5), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x30, Enemy.R, Enemy.G, Enemy.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, Enemy.R, Enemy.G, Enemy.B)),
        };
        var text = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Inlines.Add(new System.Windows.Documents.Run("STRONGER ENEMY  ") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Enemy) });
        text.Inlines.Add(new System.Windows.Documents.Run(w.Name) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        text.Inlines.Add(new System.Windows.Documents.Run($"  {PlayerRow.Compact(w.CombatPower ?? 0)}  ▲ {w.Against:P0}") { Foreground = new SolidColorBrush(Enemy) });
        warn.Child = text;
        return warn;
    }

    private static SolidColorBrush Paint(Color c, byte alpha = 0xFF) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    private static void Put(Canvas canvas, UIElement e, double left, double top)
    {
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        canvas.Children.Add(e);
    }

    private static void Dot(Canvas canvas, double x, double y, double radius, Brush? fill, Brush? stroke = null, double thickness = 0) =>
        Put(canvas, new Shapes.Ellipse { Width = 2 * radius, Height = 2 * radius, Fill = fill, Stroke = stroke, StrokeThickness = thickness }, x - radius, y - radius);

    private static int Order(RadarSide side) => side switch { RadarSide.Unknown => 0, RadarSide.Ally => 1, RadarSide.Legion => 2, RadarSide.Party => 3, _ => 4 };

    // the disc: north up, east right, the own character in the middle
    private static FrameworkElement Disc(RadarView view)
    {
        double size = DiscSize, r = size / 2, c = r, near = r * RadarView.Near / RadarView.Range;
        var canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 7) };
        var ground = new RadialGradientBrush(Color.FromRgb(0x24, 0x2D, 0x3B), Color.FromRgb(0x0F, 0x13, 0x1A)) { GradientOrigin = new Point(0.5, 0.45) };
        Dot(canvas, c, c, r, ground, Paint(OverlaySkin.Gold, 0x55), 1);
        Dot(canvas, c, c, near, null, Paint(Colors.White, 0x38), 1);
        ((Shapes.Ellipse)canvas.Children[canvas.Children.Count - 1]).StrokeDashArray = new DoubleCollection { 3, 4 };
        canvas.Children.Add(new Shapes.Line { X1 = c, Y1 = 14, X2 = c, Y2 = size - 8, Stroke = Paint(Colors.White, 0x16), StrokeThickness = 1 });
        canvas.Children.Add(new Shapes.Line { X1 = 8, Y1 = c, X2 = size - 8, Y2 = c, Stroke = Paint(Colors.White, 0x16), StrokeThickness = 1 });
        Put(canvas, new TextBlock { Text = "N", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Paint(OverlaySkin.Gold) }, c - 3.5, 1);
        Put(canvas, new TextBlock { Text = RadarView.Near.ToString("0", CultureInfo.CurrentCulture) + " m", FontSize = 9, Foreground = Paint(Faint) }, c + near * 0.72 + 2, c - near * 0.72 - 12);
        if (!view.Placed)
        {
            var wait = new TextBlock { Text = "Your place comes with\nyour next skill", FontSize = 10.5, Foreground = Paint(Muted), TextAlignment = TextAlignment.Center, Width = 140 };
            Put(canvas, wait, c - 70, c + 12);
        }

        (double X, double Y, bool Outside) Spot(RadarDot d)
        {
            double dist = Math.Sqrt(d.East * d.East + d.South * d.South);
            bool outside = dist > RadarView.Range;
            double k = outside ? (r - 6) / dist : (r - 3) / RadarView.Range;
            return (c + d.East * k, c + d.South * k, outside);
        }

        // a red haze where the enemies gather
        foreach (RadarDot d in view.Dots.Where(d => d.Side == RadarSide.Enemy))
        {
            (double x, double y, bool outside) = Spot(d);
            if (!outside)
            {
                var haze = new RadialGradientBrush(Color.FromArgb(0x48, Enemy.R, Enemy.G, Enemy.B), Color.FromArgb(0x00, Enemy.R, Enemy.G, Enemy.B));
                Dot(canvas, x, y, 13, haze);
            }
        }

        foreach (RadarDot d in view.Dots.OrderBy(d => Order(d.Side)))
        {
            (double x, double y, bool outside) = Spot(d);
            Color colour = d.Side switch
            {
                RadarSide.Enemy => Enemy, RadarSide.Party => PartyBlue, RadarSide.Legion => LegionViolet, RadarSide.Ally => OverlaySkin.Good, _ => Color.FromRgb(0x8A, 0x96, 0xA3),
            };
            if (outside)
            {
                Dot(canvas, x, y, 2.8, null, Paint(colour, 0xC0), 1.3);
                continue;
            }

            switch (d.Side)
            {
                case RadarSide.Enemy when d.Strong:
                    Dot(canvas, x, y, 8, null, Paint(Enemy, 0x90), 1);
                    Dot(canvas, x, y, 4.8, Paint(Enemy), Brushes.White, 1.2);
                    break;
                case RadarSide.Enemy:
                    Dot(canvas, x, y, 3.6, Paint(Enemy), Paint(Color.FromRgb(0x2A, 0x0E, 0x10)), 0.8);
                    break;
                case RadarSide.Party:
                    Dot(canvas, x, y, 4, Paint(PartyBlue), Paint(Colors.White, 0xD0), 1);
                    break;
                case RadarSide.Legion:
                    Dot(canvas, x, y, 3.3, Paint(LegionViolet));
                    break;
                case RadarSide.Ally:
                    Dot(canvas, x, y, 2.7, Paint(OverlaySkin.Good, 0xD0));
                    break;
                default:
                    Dot(canvas, x, y, 2.5, Paint(colour, 0x99));
                    break;
            }

            if (d.Label is { } label)
            {
                double w = label.Length * 5.9 + 10;
                var tag = new Border
                {
                    Background = Paint(Color.FromRgb(0x10, 0x13, 0x19), 0xD8), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1.5),
                    Child = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Paint(Enemy) },
                };
                bool right = x + 9 + w < size;
                Put(canvas, tag, right ? x + 9 : x - 9 - w, y - 9);
            }
        }

        // you: gold, in the middle
        Dot(canvas, c, c, 10, Paint(OverlaySkin.Gold, 0x26));
        Dot(canvas, c, c, 5, Paint(OverlaySkin.Gold), Paint(Color.FromRgb(0x10, 0x14, 0x1B)), 2);
        return canvas;
    }

    private static FrameworkElement Legend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7) };
        foreach ((string text, Color colour) in new[] { ("Party", PartyBlue), ("Legion", LegionViolet), ("Allies", OverlaySkin.Good), ("Enemies", Enemy) })
        {
            row.Children.Add(new Shapes.Ellipse { Width = 7, Height = 7, Fill = Paint(colour), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(row.Children.Count == 0 ? 0 : 10, 0, 4, 0) });
            row.Children.Add(new TextBlock { Text = text, FontSize = 10.5, Foreground = Paint(Muted), VerticalAlignment = VerticalAlignment.Center });
        }

        return row;
    }

    // who has the numbers within the inner ring (the own character counts for its side)
    private static FrameworkElement Balance(RadarView view)
    {
        int us = view.AlliesNear + 1, them = view.EnemiesNear;
        (string verdict, Color tone) = !view.Placed ? ("Distances come with your place", Muted)
            : them == 0 ? ("No enemy within " + RadarView.Near.ToString("0", CultureInfo.CurrentCulture) + " m", Muted)
            : us >= them * 1.5 ? ("You have the numbers", OverlaySkin.Good)
            : them >= us * 1.5 ? ("Outnumbered", Enemy)
            : ("Even fight", OverlaySkin.Warn);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = verdict, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Paint(tone) });
        if (view.Placed && them > 0)
        {
            var count = new TextBlock { FontSize = 11, Foreground = Paint(Muted), VerticalAlignment = VerticalAlignment.Center };
            count.Inlines.Add(new System.Windows.Documents.Run(us.ToString(CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = Paint(OverlaySkin.Good) });
            count.Inlines.Add(new System.Windows.Documents.Run(" v "));
            count.Inlines.Add(new System.Windows.Documents.Run(them.ToString(CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = Paint(Enemy) });
            count.Inlines.Add(new System.Windows.Documents.Run("  within " + RadarView.Near.ToString("0", CultureInfo.CurrentCulture) + " m"));
            Grid.SetColumn(count, 1);
            grid.Children.Add(count);
        }

        var panel = new StackPanel();
        panel.Children.Add(grid);
        if (view.Placed && them > 0)
        {
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 1, 0, 8) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(us, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(them, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = Paint(OverlaySkin.Good), CornerRadius = new CornerRadius(2, 0, 0, 2) });
            var red = new Border { Background = Paint(Enemy), CornerRadius = new CornerRadius(0, 2, 2, 0) };
            Grid.SetColumn(red, 2);
            bar.Children.Add(red);
            panel.Children.Add(bar);
        }
        else
        {
            panel.Margin = new Thickness(0, 0, 0, 5);
        }

        return panel;
    }

    // the own kills, assists and deaths in this zone
    private static FrameworkElement Score((int Kills, int Assists, int Deaths) tally)
    {
        var grid = new Grid { ToolTip = "Since you came into this zone: a kill is your last blow, an assist your hits on someone another player finished" };
        (int value, string label, Color colour)[] cells = { (tally.Kills, "KILLS", OverlaySkin.Good), (tally.Assists, "ASSISTS", OverlaySkin.Gold), (tally.Deaths, "DEATHS", Enemy) };
        for (int i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new Border
            {
                Background = Paint(Colors.White, 0x08), CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 4, 0, 4), Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0),
            };
            var inner = new StackPanel();
            var value = new TextBlock { Text = cells[i].value.ToString(CultureInfo.CurrentCulture), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Paint(cells[i].colour),
                HorizontalAlignment = HorizontalAlignment.Center };
            value.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            inner.Children.Add(value);
            inner.Children.Add(new TextBlock { Text = cells[i].label, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Paint(Faint), HorizontalAlignment = HorizontalAlignment.Center });
            cell.Child = inner;
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static Border Row(RadarEnemy e, bool band)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Source = MainWindow.ClassIcon(e.ClassName), Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBlock { Text = e.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = e.ClassName is null ? Brushes.White : ClassBrushes.Text(e.ClassName) };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var power = new TextBlock { Text = e.CombatPower is int cp ? PlayerRow.Compact(cp) : "…", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0),
            Foreground = new SolidColorBrush(OverlaySkin.Gold), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right,
            ToolTip = e.ItemLevel is int il ? $"Item level {il:N0}" : null };
        power.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        Grid.SetColumn(power, 2);
        grid.Children.Add(power);
        if (e.Against is double a)
        {
            bool stronger = a > 0.005, weaker = a < -0.005;
            var vs = new TextBlock { Text = stronger ? $"▲{a:P0}" : weaker ? $"▼{-a:P0}" : "=", FontSize = 10.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(stronger ? Enemy : weaker ? OverlaySkin.Good : Muted) };
            Grid.SetColumn(vs, 3);
            grid.Children.Add(vs);
        }

        if (e.Rival is { } r && r.Met > 0)
        {
            var record = new TextBlock { Text = $"{r.Kills}–{r.Deaths}", FontSize = 10.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(r.Kills > r.Deaths ? OverlaySkin.Good : r.Kills < r.Deaths ? Enemy : Muted),
                ToolTip = $"Met {r.Met}×: you killed {r.Kills.ToString(CultureInfo.CurrentCulture)}, died {r.Deaths.ToString(CultureInfo.CurrentCulture)}" };
            Grid.SetColumn(record, 4);
            grid.Children.Add(record);
        }

        return new Border
        {
            Child = grid, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(5), Margin = new Thickness(-6, 0, -6, 0),
            Background = band ? new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
    }
}

/// <summary>
/// Runs the radar: twice a second it reads who is in view, and shows the radar while the game is in front and an enemy is near (a sample while it is placed).
/// A game window the screen reader thinks open does not hide it (11 November 2026: it took the Abyss's own bars for one, for two minutes of a fight).
/// What it saw and why it did not show goes to pvp-log.txt.
/// </summary>
public sealed class EnemyRadarController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Func<int, string?, (int? Power, int? Items)?> _looked;
    private EnemyRadarWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private int? _ownPower;
    private DateTime _ownPowerAt = DateTime.MinValue;
    private int _zone = -1;
    private (int Kills, int Assists, int Deaths) _zoneStart;

    public EnemyRadarController(Func<Aion2EntityDirectory?> directory, Func<int, string?, (int? Power, int? Items)?> looked)
    {
        _directory = directory;
        _looked = looked;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            if (_overlay is not null)
            {
                OverlaySkin.SolidBackground(_overlay, OverlayPlaces.EnemyRadar);
            }
        }

        if (!_settings.ShowEnemyRadar)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.EnemyRadarLocked;
        Aion2EntityDirectory? dir = _directory();
        if (dir is not null && DateTime.UtcNow - _ownPowerAt > TimeSpan.FromSeconds(10))
        {
            _ownPowerAt = DateTime.UtcNow;
            _ownPower = MainWindow.OwnCombatPower(dir);
        }

        // the kills, assists and deaths since the zone began
        var tally = Aion2Pvp.Tally;
        if (dir is not null && dir.ZoneEpoch != _zone)
        {
            _zone = dir.ZoneEpoch;
            _zoneStart = tally;
        }

        // never in a dungeon or raid (only its sample while it is being placed)
        bool pve = dir is not null && Aion2.Protocol.Aion2Zones.IsPveHere(dir.ZoneId, dir.LiveMobs);
        if (pve)
        {
            PvpLog.Note("radar", "pve", $"hidden: in a dungeon or raid (zone {dir!.ZoneId}{(Aion2.Protocol.Aion2Zones.IsPveInstance(dir.ZoneId) ? "" : ", told by the monsters around")})");
        }

        RadarView? view = dir is null || pve ? null
            : RadarView.Of(dir, _ownPower, DateTime.Now, _looked, (tally.Kills - _zoneStart.Kills, tally.Assists - _zoneStart.Assists, tally.Deaths - _zoneStart.Deaths));
        bool mayShow = GameWindow.OverlayMayShow(ignoreMenu: true);
        if (view is not null)
        {
            string state = view.Enemies.Count == 0 ? (view.InView > 0 && view.Faction is null ? "no enemies: own faction unknown" : "no enemies in view")
                : mayShow ? "shown" : "hidden: the game is not the window in front";
            PvpLog.Note("radar", state, $"{state} ({view.InView} players in view, {view.NoFaction} without a faction, {view.Enemies.Count} enemies, {view.Allies} allies, "
                + $"{view.Dots.Count} placed, own faction {view.Faction ?? "unknown"}, own place {(view.Placed ? "known" : "unknown")}, zone {dir!.ZoneId})");
        }

        if ((view is null || view.Enemies.Count == 0) && !locked)
        {
            view = RadarView.SampleView(DateTime.Now);
        }

        if ((view is null || view.Enemies.Count == 0) && _overlay is null)
        {
            return;
        }

        if (_overlay is null)
        {
            _overlay = new EnemyRadarWindow();
        }

        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(view);
        _overlay.ShowOverlay(view is { Enemies.Count: > 0 } && mayShow);
    }
}

/// <summary>
/// The death recap (10 November 2026, the owner picked: "when you die, a card shows who hit you, with which skills, how much, and over how many seconds"):
/// the own character's death as <see cref="Aion2Pvp"/> worked it out - the killing blow, every attacker with his share and skills, the heals that came, and the
/// seconds from the first hit of the burst. It comes up at the death and goes after half a minute. Over the game only.
/// </summary>
public sealed class DeathRecapWindow : MovableOverlay
{
    private static readonly Color Red = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private readonly ContentControl _content = new() { MinWidth = 300, Focusable = false };
    private string _shown = "";

    public DeathRecapWindow() : base(OverlayPlaces.DeathRecap, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 - 170,
        SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.55, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = _content;
        OverlaySkin.SolidBackground(this, OverlayPlaces.DeathRecap);
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.DeathRecapWidth, s.DeathRecapHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.DeathRecapWidth = width;
        s.DeathRecapHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.DeathRecapLeft, s.DeathRecapTop, s.DeathRecapScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.DeathRecapLeft = left;
        s.DeathRecapTop = top;
        s.DeathRecapScale = scale;
        s.Save();
    }

    public new bool HasContent => _content.Content is not null;

    public void Render(DeathRecap? recap, bool sample)
    {
        string key = recap is null ? "" : sample ? "sample" : recap.At.Ticks + "|" + recap.Victim; // the sample is made anew each tick: drawn once
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _content.Content = recap is null ? null : View(recap, sample);
    }

    /// <summary>The recap card (the pictures draw it too).</summary>
    public static FrameworkElement View(DeathRecap recap, bool sample = false)
    {
        var stack = new StackPanel();
        string seconds = recap.Seconds.ToString(recap.Seconds < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " s";
        stack.Children.Add(OverlaySkin.Heading(sample ? "You died · sample" : "You died", "in " + seconds, Red, Red));

        // the killing blow
        if (recap.Killer is { } killer)
        {
            var blow = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            blow.Inlines.Add(new System.Windows.Documents.Run("Killed by ") { Foreground = new SolidColorBrush(Muted) });
            blow.Inlines.Add(new System.Windows.Documents.Run(killer.Name) { FontWeight = FontWeights.SemiBold, Foreground = killer.ClassName is { } c ? ClassBrushes.Text(c) : Brushes.White });
            if (recap.KillingSkill is { } skill)
            {
                blow.Inlines.Add(new System.Windows.Documents.Run("  ·  " + skill + "  ") { Foreground = new SolidColorBrush(Muted) });
                blow.Inlines.Add(new System.Windows.Documents.Run(recap.KillingAmount.ToString("N0", CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Red) });
            }

            stack.Children.Add(blow);
        }

        string totals = $"{recap.Taken.ToString("N0", CultureInfo.CurrentCulture)} taken" + (recap.Healed > 0 ? $"  ·  {recap.Healed.ToString("N0", CultureInfo.CurrentCulture)} healed" : "");
        stack.Children.Add(new TextBlock { Text = totals, FontSize = 11, Foreground = new SolidColorBrush(Faint), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(OverlaySkin.Divider(2, 5));

        // who: share bar, amount, his skills under it
        foreach (RecapSource s in recap.Sources.Take(5))
        {
            double share = recap.Taken > 0 ? (double)s.Amount / recap.Taken : 0;
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            if (s.Player)
            {
                grid.Children.Add(new Image { Source = MainWindow.ClassIcon(s.ClassName), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            var name = new TextBlock { Text = s.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = s.ClassName is { } cn ? ClassBrushes.Text(cn) : new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF2)) };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var amount = new TextBlock { Text = s.Amount.ToString("N0", CultureInfo.CurrentCulture), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0),
                Foreground = Brushes.White };
            amount.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            Grid.SetColumn(amount, 2);
            grid.Children.Add(amount);
            var pct = new TextBlock { Text = share.ToString("P0", CultureInfo.CurrentCulture), FontSize = 11, TextAlignment = TextAlignment.Right, Foreground = new SolidColorBrush(Muted),
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pct, 3);
            grid.Children.Add(pct);
            stack.Children.Add(grid);
            stack.Children.Add(ShareBar(share, s.Player ? Red : Color.FromRgb(0xC8, 0x9B, 0x5A)));
            // his two heaviest skills on the card; all of them on hover
            static string SkillText(RecapSkill k) => $"{k.Name} ×{k.Hits} {k.Amount.ToString("N0", CultureInfo.CurrentCulture)}";
            string skills = string.Join("  ·  ", s.Skills.Take(2).Select(SkillText));
            if (skills.Length > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = skills, FontSize = 10.5, Foreground = new SolidColorBrush(Faint), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 3),
                    ToolTip = s.Skills.Count > 2 ? string.Join("\n", s.Skills.Select(SkillText)) : null,
                });
            }
        }

        if (recap.Sources.Count > 5)
        {
            stack.Children.Add(new TextBlock { Text = $"+ {recap.Sources.Count - 5} more", FontSize = 10.5, Foreground = new SolidColorBrush(Faint) });
        }

        if (recap.Sources.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "No hits were seen before it (a fall, or the meter came up after).", FontSize = 11.5, Foreground = new SolidColorBrush(Muted),
                TextWrapping = TextWrapping.Wrap });
        }

        return OverlaySkin.Panel(stack, new Thickness(12, 9, 12, 10));
    }

    private static Border ShareBar(double share, Color colour)
    {
        var grid = new Grid();
        double pct = Math.Clamp(share * 100, 0, 100);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(colour) });
        return new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2, 0, 0), ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Child = grid };
    }

    /// <summary>What the card shows while it is placed.</summary>
    public static DeathRecap Sample(DateTime now) => new(now, 0, "You", 4.2, 41_930, 3_100,
        new RecapSource(1, "Kraeger", "Templar", true, 28_410, 6, new[] { new RecapSkill("Shield Bash", 0, 3, 18_200, 1), new RecapSkill("Punishing Blow", 0, 2, 7_100, 0), new RecapSkill("Attack", 0, 1, 3_110, 0) }),
        "Shield Bash", 8_214,
        new[]
        {
            new RecapSource(1, "Kraeger", "Templar", true, 28_410, 6, new[] { new RecapSkill("Shield Bash", 0, 3, 18_200, 1), new RecapSkill("Punishing Blow", 0, 2, 7_100, 0), new RecapSkill("Attack", 0, 1, 3_110, 0) }),
            new RecapSource(2, "Morrigane", "Sorcerer", true, 9_120, 3, new[] { new RecapSkill("Flame Bolt", 0, 2, 6_800, 1), new RecapSkill("Frost Shard", 0, 1, 2_320, 0) }),
            new RecapSource(3, "Balaur Warrior", null, false, 4_400, 2, new[] { new RecapSkill("Cleave", 0, 2, 4_400, 0) }),
        });
}

/// <summary>Shows the death recap when the own character dies, for half a minute (a sample while it is placed).</summary>
public sealed class DeathRecapController : IDisposable
{
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(30);
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DeathRecapWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DeathRecap? _recap;
    private DateTime _diedAt = DateTime.MinValue;
    private readonly Func<Aion2EntityDirectory?> _directory;

    public DeathRecapController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Aion2Pvp.Died += OnDied;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void OnDied(DeathRecap recap)
    {
        // a death in a dungeon or raid gets no recap (the PvP overlays stay out of them)
        if (_directory() is { } dir && Aion2.Protocol.Aion2Zones.IsPveHere(dir.ZoneId, dir.LiveMobs))
        {
            PvpLog.Write($"death: {recap.VictimName} died in a dungeon or raid (zone {dir.ZoneId}): no recap");
            return;
        }

        _recap = recap;
        _diedAt = DateTime.UtcNow;
        PvpLog.Write($"death: {recap.VictimName} died, killed by {recap.Killer?.Name ?? "no one seen"}, {recap.Sources.Count} attackers, {recap.Taken:N0} taken in {recap.Seconds:0.0} s");
        Tick();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        Aion2Pvp.Died -= OnDied;
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            if (_overlay is not null)
            {
                OverlaySkin.SolidBackground(_overlay, OverlayPlaces.DeathRecap);
            }
        }

        if (!_settings.ShowDeathRecap)
        {
            if (_recap is not null && DateTime.UtcNow - _diedAt < Shown)
            {
                PvpLog.Note("recap", "off " + _diedAt.Ticks, "not shown: the death recap is switched off");
            }

            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.DeathRecapLocked;
        DeathRecap? recap = _recap is not null && DateTime.UtcNow - _diedAt < Shown ? _recap : null;
        bool sample = false;
        if (recap is null && !locked)
        {
            recap = DeathRecapWindow.Sample(DateTime.Now);
            sample = true;
        }

        if (recap is null && _overlay is null)
        {
            return;
        }

        _overlay ??= new DeathRecapWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(recap, sample);
        // the game shows its own death window: the recap stays over it (the meter's game-window rule does not hold for it)
        bool mayShow = GameWindow.OverlayMayShow(ignoreMenu: true);
        if (!sample && recap is not null)
        {
            string state = mayShow ? "shown" : "hidden: the game is not the window in front";
            PvpLog.Note("recap", state + " " + _diedAt.Ticks, state);
        }

        _overlay.ShowOverlay(recap is not null && mayShow);
    }
}
