using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// What the progression overlay shows: the character (its name and its class colour), its Item Level, the guide its Item Level is in with how far into it the
/// character is and the next stage, its Combat Power with the characters of the same Item Level, the next moves (<see cref="CpProgression.Moves"/>) and the
/// Road to 3,000+ done so far (none below 2,100).
/// </summary>
public sealed record ProgressionOverlayView(string Name, Color Accent, int? ItemLevel, string? Guide, double GuideShare, string GuideNote, string? NextStage, double Cp,
    CpBand? Band, IReadOnlyList<CpMove> Moves, int RoadDone, int RoadTotal, bool Sample)
{
    public static readonly ProgressionOverlayView None = new("", OverlaySkin.Gold, null, null, 0, "", null, 0, null, Array.Empty<CpMove>(), 0, 0, false);

    public bool IsEmpty => Name.Length == 0;

    /// <summary>Everything the overlay draws, as one text (it draws again only when this changes).</summary>
    public string Key => string.Join("|", Name, Accent, ItemLevel, Guide, GuideShare.ToString("0.000", CultureInfo.InvariantCulture), GuideNote, NextStage, Math.Round(Cp),
        Band?.From, RoadDone, RoadTotal, Sample, LocalizationManager.Instance.Language,
        string.Join(";", Moves.Select(m => string.Join("/", m.ItemId, CpCalcPage.MoveText(m), CpCalcPage.MoveFigures(m)))));
}

/// <summary>
/// The progression overlay (14 November 2026, the owner: "make a sexy overlay for it that guides players"): the Progression page in the game, in the meter's
/// look. A heading in the class colour with the character; a card washed in that colour with the Item Level large, the Combat Power and how it stands against the
/// players of the same Item Level (green above their middle half, gold in it, red below), the guide's bar and the next stage; then "do this next": three moves,
/// each numbered in a gold ring with its piece, what to do and where, and the Item Level and Combat Power it adds; and the Road to 3,000+ so far. Placed,
/// scaled and locked like the other overlays (<see cref="MovableOverlay"/>; scaled as a whole: its text wraps at its own width);
/// <see cref="ProgressionOverlayController"/> shows it only while the game is in front, and steps it aside in a dungeon.
/// </summary>
public sealed class ProgressionOverlayWindow : MovableOverlay
{
    /// <summary>The width the overlay lays its text out to (scaled as a whole, see the class).</summary>
    public const double ContentWidth = 336;

    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private readonly StackPanel _rows = new() { Width = ContentWidth };
    private string _shown = "";

    public ProgressionOverlayWindow() : base(OverlayPlaces.Progression, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 60, 160, LoadSize, SaveSize,
        OverlayEdgeMode.Uniform)
    {
        Body = OverlaySkin.Panel(_rows, new Thickness(12, 10, 12, 11));
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.ProgressionOverlayWidth, s.ProgressionOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.ProgressionOverlayWidth = width;
        s.ProgressionOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.ProgressionOverlayLeft, s.ProgressionOverlayTop, s.ProgressionOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.ProgressionOverlayLeft = left;
        s.ProgressionOverlayTop = top;
        s.ProgressionOverlayScale = scale;
        s.Save();
    }

    public new bool HasContent => _rows.Children.Count > 0;

    private static string Loc(string key) => LocalizationManager.Instance[key];

    private static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Loc(key), args);

    private static string Thousands(double v) => Math.Round(v).ToString("N0", CultureInfo.CurrentCulture);

    private static SolidColorBrush Solid(Color c) => new(c);

    private static SolidColorBrush Tint(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    public void Render(ProgressionOverlayView view)
    {
        string key = view.Key;
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        if (view.IsEmpty)
        {
            return;
        }

        // the heading: a dot in the class colour, the title, the character on the right
        string title = Loc("Main.MenuTools.Progression") + (view.Sample ? " · " + Loc("Progression.Overlay.Sample") : "");
        _rows.Children.Add(OverlaySkin.Heading(title, view.Name, Muted, view.Accent));
        _rows.Children.Add(StageCard(view));

        // what to do next
        var head = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(new TextBlock { Text = Loc("CpCalc.Prog.Moves").ToUpper(CultureInfo.CurrentCulture), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Solid(Faint) });
        _rows.Children.Add(head);
        if (view.Moves.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = Loc("Progression.Overlay.None"), FontSize = 11.5, Foreground = Solid(OverlaySkin.Good), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2),
            });
        }

        for (int k = 0; k < view.Moves.Count; k++)
        {
            _rows.Children.Add(MoveRow(view.Moves[k], k + 1, k % 2 == 1));
        }

        // the Road to 3,000+ so far
        if (view.RoadTotal > 0)
        {
            _rows.Children.Add(OverlaySkin.Divider(6, 6));
            var road = new Grid();
            road.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            road.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            road.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            road.Children.Add(new TextBlock
            {
                Text = Loc("CpCalc.Prog.Road").ToUpper(CultureInfo.CurrentCulture), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Solid(Faint), VerticalAlignment = VerticalAlignment.Center,
            });
            bool done = view.RoadDone >= view.RoadTotal;
            Border bar = Bar((double)view.RoadDone / view.RoadTotal, done ? OverlaySkin.Good : OverlaySkin.Gold, 4);
            bar.Margin = new Thickness(10, 0, 10, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 1);
            road.Children.Add(bar);
            var count = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            count.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            count.Inlines.Add(new Run(view.RoadDone.ToString(CultureInfo.CurrentCulture)) { Foreground = Solid(done ? OverlaySkin.Good : OverlaySkin.Gold) });
            count.Inlines.Add(new Run(" / " + view.RoadTotal.ToString(CultureInfo.CurrentCulture)) { Foreground = Solid(Muted) });
            Grid.SetColumn(count, 2);
            road.Children.Add(count);
            _rows.Children.Add(road);
        }
    }

    /// <summary>
    /// The stage: on a wash of the class colour, the Item Level large on the left; the Combat Power on the right with how it stands against the players of the
    /// same Item Level; under them the guide with a bar of how far into it the character is, and what the next stage brings.
    /// </summary>
    private static Border StageCard(ProgressionOverlayView v)
    {
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var level = new TextBlock { FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = Solid(OverlaySkin.Gold), VerticalAlignment = VerticalAlignment.Center };
        level.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        level.Inlines.Add(new Run(v.ItemLevel is int il ? Thousands(il) : "—"));
        level.Inlines.Add(new Run(" IL") { FontSize = 12, Foreground = Tint(OverlaySkin.Gold, 0xB0) });
        top.Children.Add(level);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var power = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        power.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        power.Inlines.Add(new Run(Thousands(v.Cp)));
        power.Inlines.Add(new Run(" CP") { Foreground = Solid(Muted), FontWeight = FontWeights.Normal });
        power.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        right.Children.Add(power);
        if (v.Band is { } band)
        {
            double share = (v.Cp - band.Middle) / Math.Max(1, band.Middle) * 100;
            Color tone = v.Cp < band.Low ? OverlaySkin.Bad : v.Cp > band.High ? OverlaySkin.Good : OverlaySkin.Gold;
            string pace = (share >= 0 ? "▲ " : "▼ ") + Math.Abs(share).ToString("0.#", CultureInfo.CurrentCulture) + " %";
            right.Children.Add(new Border
            {
                Background = Tint(tone, 0x24), BorderBrush = Tint(tone, 0x70), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock { Text = F("Progression.Overlay.Pace", pace), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Solid(tone) },
            });
        }

        Grid.SetColumn(right, 1);
        top.Children.Add(right);
        stack.Children.Add(top);
        if (v.Guide is { } guide)
        {
            var line = new Grid { Margin = new Thickness(0, 7, 0, 3) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = guide, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
            line.Children.Add(name);
            bool past = v.GuideShare >= 1;
            var note = new TextBlock { Text = v.GuideNote, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Solid(past ? OverlaySkin.Good : Muted), Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(note, 1);
            line.Children.Add(note);
            stack.Children.Add(line);
            stack.Children.Add(Bar(v.GuideShare, past ? OverlaySkin.Good : OverlaySkin.Gold, 5));
        }

        if (v.NextStage is { Length: > 0 } next)
        {
            stack.Children.Add(new TextBlock { Text = next, FontSize = 10.5, Foreground = Solid(Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        }

        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x3A, v.Accent.R, v.Accent.G, v.Accent.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, v.Accent.R, v.Accent.G, v.Accent.B), 1));
        return new Border
        {
            Child = stack, Background = wash, BorderBrush = Tint(v.Accent, 0x5A), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 9), Margin = new Thickness(0, 0, 0, 8),
        };
    }

    /// <summary>A move: its number in a gold ring (the first filled), its piece, what to do and where, the Item Level and the Combat Power it adds on the right.</summary>
    private static Border MoveRow(CpMove m, int number, bool band)
    {
        var (title, detail) = CpCalcPage.MoveText(m);
        var (itemLevel, cp) = CpCalcPage.MoveFigures(m);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.2), BorderBrush = Solid(OverlaySkin.Gold),
            Background = Tint(OverlaySkin.Gold, number == 1 ? (byte)0x44 : (byte)0x12), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = number.ToString(CultureInfo.CurrentCulture), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Solid(OverlaySkin.Gold),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        FrameworkElement icon = CpCalcPage.MoveIcon(m, 26);
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var what = new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        what.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        words.Children.Add(what);
        if (detail.Length > 0)
        {
            // two lines at most, the rest cut with an ellipsis (the piece's name and where it comes from matter more than a cut-off line)
            words.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 10, Foreground = Solid(Faint), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 13, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 26, Margin = new Thickness(0, 1, 0, 0),
            });
        }

        Grid.SetColumn(words, 2);
        grid.Children.Add(words);
        var figures = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var (text, color) in new[] { (itemLevel, OverlaySkin.Gold), (cp, OverlaySkin.Good) })
        {
            if (text.Length > 0)
            {
                var figure = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Solid(color), HorizontalAlignment = HorizontalAlignment.Right };
                figure.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
                figures.Children.Add(figure);
            }
        }

        Grid.SetColumn(figures, 3);
        grid.Children.Add(figures);
        return new Border
        {
            Child = grid, Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(6), Margin = new Thickness(-6, 0, -6, 1),
            Background = band ? new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
    }

    /// <summary>A bar of how far along, filled from the left with a soft shine.</summary>
    private static Border Bar(double share, Color color, double height)
    {
        double part = Math.Clamp(share, 0, 1);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, color.R, color.G, color.B), 0));
        fill.GradientStops.Add(new GradientStop(color, 1));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, part), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - part), GridUnitType.Star) });
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = fill });
        return new Border
        {
            Height = height, CornerRadius = new CornerRadius(height / 2), ClipToBounds = true, Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), Child = grid,
        };
    }
}

/// <summary>
/// Runs the progression overlay: once a second it reads the settings and shows the overlay only while the game is in front (locked or not: never on the
/// desktop), not in a dungeon unless the player wants it there. It counts the character again (as the Progression page does, <see cref="CpCalcPage.Evaluate"/>)
/// when its gear, Item Level or the rest of it changed; the game's stat frame moves in a fight, so a change of it alone counts once a minute at most.
/// </summary>
public sealed class ProgressionOverlayController : IDisposable
{
    /// <summary>How many moves the overlay lists.</summary>
    public const int MovesShown = 3;

    private static readonly TimeSpan StatsEvery = TimeSpan.FromMinutes(1);
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private ProgressionOverlayWindow? _overlay;
    private ProgressionOverlayView _view = ProgressionOverlayView.None;
    private string _counted = "?", _stats = "?";
    private DateTime _countedAt = DateTime.MinValue;

    public ProgressionOverlayController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
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
        MeterSettings settings = MeterSettings.Load();
        if (!settings.ShowProgressionOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = settings.ProgressionOverlayLocked;
        _overlay ??= new ProgressionOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if (!GameWindow.OverlayMayShow() || (settings.ProgressionOverlayHideInDungeons && MainWindow.InDungeonNow))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        // counted again when the character, its gear, its Item Level, the rest of it, the model or the language changed; the stats alone once a minute
        Aion2EntityDirectory? directory = _directory();
        string counted = string.Join("|", directory?.LocalCharacter?.Name, CpCalcPage.GearSignature(directory?.LocalEquipment), CpCalcPage.RestSignature(directory),
            directory?.OwnLiveItemLevel, Aion2CpData.Model.FromWebsite, LocalizationManager.Instance.Language, CpChecks.Version, locked, PrivacyConsent.Restricted);
        string stats = CpCalcPage.FrameSignature(directory?.LocalStats);
        if (counted != _counted || (stats != _stats && DateTime.UtcNow - _countedAt >= StatsEvery))
        {
            _counted = counted;
            _stats = stats;
            _countedAt = DateTime.UtcNow;
            _view = ViewOf(directory, sample: !locked);
        }

        _overlay.Render(_view);
        _overlay.ShowOverlay(!locked || _overlay.HasContent);
    }

    /// <summary>What the overlay shows for the character logged in (also used to draw its picture); a sample while it is placed with no character read yet.</summary>
    public static ProgressionOverlayView ViewOf(Aion2EntityDirectory? directory, bool sample)
    {
        Aion2CharacterInfo? me = directory?.LocalCharacter;
        IReadOnlyList<Aion2EquippedItem>? gear = directory?.LocalEquipment;
        CpModel model = Aion2CpData.Model;
        if (me is not { Name.Length: > 0 } || gear is not { Count: > 0 } || CpCalcPage.Evaluate(directory, gear, me.Name, me.ServerId, model) is not { } v)
        {
            return sample ? Sample() : ProgressionOverlayView.None;
        }

        int? il = CpCalcPage.ItemLevelFor(directory, me.Name, me.ServerId);
        char race = CpWhole.RaceOf(gear);
        IReadOnlyList<CpProgPiece> pieces = CpProgression.Read(v.Pieces, il, race, v.Totals, model);
        var steps = v.Whole.Steps.Where(s => s.Key != CpWhole.RuneKey).ToList();
        IReadOnlyList<CpMove> moves = CpProgression.Moves(pieces, steps, v.Whole.Arcana, il, race, v.Totals, model, MovesShown);
        string cls = Aion2SkillNames.ClassFromCode(me.ClassCode) ?? "";
        Color accent = cls.Length > 0 ? ClassBrushes.ClassColor(cls) : OverlaySkin.Gold;
        (string? guide, double share, string note) = GuideOf(il);
        int done = 0, total = 0;
        if (il >= CpProgression.RoadFrom)
        {
            IReadOnlyList<CpRoadTask> road = CpProgression.Road(pieces, il, v.Whole.Arcana, v.Whole.Stigmas, race, v.Totals, model);
            done = road.Count(t => t.Done);
            total = road.Count;
        }

        return new ProgressionOverlayView(me.Name, accent, il, guide, share, note, NextStageOf(il), v.GameCp ?? v.Whole.Total, il is int level ? CpProgression.BandOf(level) : null,
            moves, done, total, false);
    }

    /// <summary>The guide the Item Level is furthest into (the Road to 3,000+ from 2,500), how far into it (0 to 1) and how much is left ("266 to go", "reached").</summary>
    private static (string? Guide, double Share, string Note) GuideOf(int? itemLevel)
    {
        if (itemLevel is not int il || CpProgression.GuidesFor(il).LastOrDefault() is not { } guide)
        {
            return (null, 0, "");
        }

        int goal = guide.To == int.MaxValue ? CpProgression.RoadGoal : guide.To;
        double share = goal > guide.From ? (double)(il - guide.From) / (goal - guide.From) : 1;
        string note = il >= goal ? LocalizationManager.Instance["Progression.Overlay.Reached"]
            : string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Progression.Overlay.ToGo"], (goal - il).ToString("N0", CultureInfo.CurrentCulture));
        return (guide.Name, Math.Clamp(share, 0, 1), note);
    }

    /// <summary>What the next stage brings and from what Item Level ("Next stage at 2,100 IL: IL 86 dungeon gear"); null past the last stage.</summary>
    private static string? NextStageOf(int? itemLevel)
    {
        if (itemLevel is not int il || CpProgression.Tiers.FirstOrDefault(t => t.From > il) is not { } next)
        {
            return null;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        CpTierGear? gear = next.Weapon ?? next.Armor ?? next.Jewelry;
        string what = gear is null ? ""
            : gear.Crafted ? string.Format(CultureInfo.CurrentCulture, loc["Progression.Overlay.NextCrafted"], gear.ItemLevel)
            : string.Format(CultureInfo.CurrentCulture, loc["Progression.Overlay.NextDungeon"], gear.ItemLevel);
        return string.Format(CultureInfo.CurrentCulture, loc["Progression.Overlay.Next"], next.From.ToString("N0", CultureInfo.CurrentCulture),
            (next.From - il).ToString("N0", CultureInfo.CurrentCulture), what);
    }

    /// <summary>What the overlay shows while it is placed with no character read yet: a character at 2,734 Item Level and three moves.</summary>
    private static ProgressionOverlayView Sample()
    {
        const int il = 2734;
        (string? guide, double share, string note) = GuideOf(il);
        CpTier stage = CpProgression.TierFor(il)!;
        var moves = new[]
        {
            new CpMove(CpMoveKind.Equip, 102, 3480, Slot: "SubHand", Target: stage.Guard),
            new CpMove(CpMoveKind.Stones, 12, null, Count: 3),
            new CpMove(CpMoveKind.Arcana, 4, null, Count: 4),
        };
        return new ProgressionOverlayView(LocalizationManager.Instance["Progression.Overlay.You"], OverlaySkin.Gold, il, guide, share, note, NextStageOf(il), 152_310,
            CpProgression.BandOf(il), moves, 9, 17, true);
    }
}
