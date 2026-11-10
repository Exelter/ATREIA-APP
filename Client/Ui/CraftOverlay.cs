using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>A recipe of the plan as the overlay shows it.</summary>
public sealed record CraftOverlayRecipe(int Product, string Name, Color Color, int Count, int Percent);

/// <summary>A material the plan still lacks: what is there of what is needed, what buying the rest costs, and where what is there lies when not in the bag.</summary>
public sealed record CraftOverlayMaterial(int Item, string Name, Color Color, long Have, long Need, long? Buy, string? Elsewhere);

/// <summary>What the crafting overlay shows: how far the plan is, what is left to buy, the recipes, the materials still missing (the most first).</summary>
public sealed record CraftOverlayView(int Percent, long? ToBuy, int Unpriced, IReadOnlyList<CraftOverlayRecipe> Recipes, int MoreRecipes, IReadOnlyList<CraftOverlayMaterial> Missing,
    int MoreMissing, bool Sample)
{
    public static readonly CraftOverlayView None = new(0, null, 0, Array.Empty<CraftOverlayRecipe>(), 0, Array.Empty<CraftOverlayMaterial>(), 0, false);

    public bool IsEmpty => Recipes.Count == 0;

    public bool Ready => Recipes.Count > 0 && Missing.Count == 0;
}

/// <summary>
/// The crafting overlay (10 November 2026, the owner: "something magnificent, same with the in game overlay"): in the meter's look, a heading with how far the
/// plan is, what is left to buy at the market (aion2.exchange, the character's server), the recipes with a bar each, and the materials still missing, each with
/// what is there of what is needed, what buying the rest costs, and where what is there lies when it is not in the bag (the storage, the server storage, another
/// character). Placed, scaled and locked like the other overlays (<see cref="MovableOverlay"/>); <see cref="CraftOverlayController"/> shows it only while the game
/// is in front (locked or not: never on the desktop).
/// </summary>
public sealed class CraftOverlayWindow : MovableOverlay
{
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private readonly StackPanel _rows = new() { MinWidth = 290, MinHeight = 30 };
    private string _shown = "";

    public CraftOverlayWindow() : base(OverlayPlaces.Craft, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 60, 420, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = OverlaySkin.Panel(_rows, new Thickness(12, 9, 12, 10));
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.CraftOverlayWidth, s.CraftOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.CraftOverlayWidth = width;
        s.CraftOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.CraftOverlayLeft, s.CraftOverlayTop, s.CraftOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.CraftOverlayLeft = left;
        s.CraftOverlayTop = top;
        s.CraftOverlayScale = scale;
        s.Save();
    }

    public new bool HasContent => _rows.Children.Count > 0;

    public void Render(CraftOverlayView view)
    {
        string key = string.Join("|", view.Percent, view.ToBuy, view.Unpriced, view.MoreRecipes, view.MoreMissing, view.Sample,
            string.Join(",", view.Recipes.Select(r => r.Product + "x" + r.Count + "=" + r.Percent)), string.Join(",", view.Missing.Select(m => $"{m.Item}:{m.Have}/{m.Need}:{m.Buy}:{m.Elsewhere}")));
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

        // the heading: how far, and the state in its colour
        string note = view.Ready ? "READY ✓" : view.Percent + "% READY";
        _rows.Children.Add(OverlaySkin.Heading(view.Sample ? "Crafting · sample" : "Crafting", note, view.Ready ? OverlaySkin.Good : OverlaySkin.Gold, view.Ready ? OverlaySkin.Good : OverlaySkin.Gold));
        _rows.Children.Add(Bar(view.Percent, new Thickness(0, -1, 0, 8), 4));

        // the recipes
        foreach (CraftOverlayRecipe r in view.Recipes)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border icon = CraftText.IconTile(r.Product, 22);
            icon.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            title.Inlines.Add(new System.Windows.Documents.Run(r.Name) { Foreground = new SolidColorBrush(r.Color), FontWeight = FontWeights.SemiBold });
            title.Inlines.Add(new System.Windows.Documents.Run($"  ×{r.Count:N0}") { Foreground = new SolidColorBrush(Muted) });
            names.Children.Add(title);
            names.Children.Add(Bar(r.Percent, new Thickness(0, 3, 0, 0), 3));
            Grid.SetColumn(names, 1);
            grid.Children.Add(names);
            var pct = new TextBlock { Text = r.Percent + "%", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 34, TextAlignment = TextAlignment.Right, Foreground = new SolidColorBrush(r.Percent >= 100 ? OverlaySkin.Good : OverlaySkin.Gold) };
            Grid.SetColumn(pct, 2);
            grid.Children.Add(pct);
            _rows.Children.Add(grid);
        }

        if (view.MoreRecipes > 0)
        {
            _rows.Children.Add(Small($"+ {view.MoreRecipes} more recipe{(view.MoreRecipes == 1 ? "" : "s")}", new Thickness(30, 0, 0, 2)));
        }

        if (view.Ready)
        {
            _rows.Children.Add(OverlaySkin.Divider(5, 6));
            _rows.Children.Add(new TextBlock { Text = "Every material is there: off to the workbench.", FontSize = 11.5, Foreground = new SolidColorBrush(OverlaySkin.Good), TextWrapping = TextWrapping.Wrap });
            return;
        }

        // what is still missing
        _rows.Children.Add(OverlaySkin.Divider(5, 4));
        var missingHead = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        missingHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        missingHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        missingHead.Children.Add(new TextBlock { Text = "STILL NEEDED", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Faint) });
        string buy = view.ToBuy is long total ? "TO BUY " + CraftWorkshop.Kinah(total) + (view.Unpriced > 0 ? $" +{view.Unpriced}?" : "") : "";
        var buyText = new TextBlock { Text = buy, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(OverlaySkin.Gold),
            ToolTip = "What buying every missing material costs at the market" };
        Grid.SetColumn(buyText, 1);
        missingHead.Children.Add(buyText);
        _rows.Children.Add(missingHead);
        bool band = false;
        foreach (CraftOverlayMaterial m in view.Missing)
        {
            _rows.Children.Add(MaterialRow(m, band));
            band = !band;
        }

        if (view.MoreMissing > 0)
        {
            _rows.Children.Add(Small($"+ {view.MoreMissing} more material{(view.MoreMissing == 1 ? "" : "s")}", new Thickness(7, 2, 0, 0)));
        }
    }

    private static Border MaterialRow(CraftOverlayMaterial m, bool band)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Border icon = CraftText.IconTile(m.Item, 18);
        icon.Margin = new Thickness(0, 0, 7, 0);
        grid.Children.Add(icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = m.Name, FontSize = 11.5, Foreground = new SolidColorBrush(m.Color), TextTrimming = TextTrimming.CharacterEllipsis });
        if (m.Elsewhere is { Length: > 0 } where)
        {
            names.Children.Add(new TextBlock { Text = where, FontSize = 10, Foreground = new SolidColorBrush(Faint), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var count = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextAlignment = TextAlignment.Right };
        count.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        count.Inlines.Add(new System.Windows.Documents.Run(m.Have.ToString("N0")) { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(m.Have > 0 ? OverlaySkin.Warn : OverlaySkin.Bad) });
        count.Inlines.Add(new System.Windows.Documents.Run(" / " + m.Need.ToString("N0")) { Foreground = new SolidColorBrush(Muted) });
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);
        var price = new TextBlock { Text = m.Buy is long b ? CraftWorkshop.Kinah(b) : "", FontSize = 11, MinWidth = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0), Foreground = new SolidColorBrush(OverlaySkin.Gold), ToolTip = "What buying the rest costs at the market" };
        Grid.SetColumn(price, 3);
        grid.Children.Add(price);
        return new Border
        {
            Child = grid, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(5), Margin = new Thickness(-6, 0, -6, 0),
            Background = band ? new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
    }

    private static Border Bar(int percent, Thickness margin, double height)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Margin = margin, ClipToBounds = true, Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(percent >= 100 ? OverlaySkin.Good : OverlaySkin.Gold) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(percent, 0, 100), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - Math.Clamp(percent, 0, 100), GridUnitType.Star) });
        grid.Children.Add(fill);
        track.Child = grid;
        return track;
    }

    private static TextBlock Small(string text, Thickness margin) => new() { Text = text, FontSize = 10.5, Foreground = new SolidColorBrush(Faint), Margin = margin };
}

/// <summary>Runs the crafting overlay: twice a second it reads the settings, the plan and the stock, and shows the overlay only while the game is in front (or while it is unlocked so it can be placed).</summary>
public sealed class CraftOverlayController : IDisposable
{
    private const int RecipeLines = 4;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private CraftOverlayWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private CraftOverlayView _view = CraftOverlayView.None;
    private DateTime _viewAt = DateTime.MinValue;
    private bool _viewLocked;

    public CraftOverlayController(Func<Aion2EntityDirectory?> directory)
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

    /// <summary>What the overlay shows for the character logged in (also used to draw the picture of the tool, with a made-up stock and market).</summary>
    public static CraftOverlayView ViewOf(CraftStock stock, MarketBook? book, IReadOnlyList<CraftPlanLine> plan, MeterSettings settings, bool sample)
    {
        if (plan.Count == 0)
        {
            return sample ? Sample() : CraftOverlayView.None;
        }

        List<CraftResult> results = plan.Select(p => p.Result).ToList();
        List<CraftLine> all = CraftCalc.Total(results);
        long need = all.Sum(l => l.Needed), have = all.Sum(l => Math.Min(l.Have, l.Needed));
        int percent = need == 0 ? 100 : (int)(100 * have / need);
        List<CraftLine> missing = all.Where(l => l.Missing > 0).ToList();
        int max = Math.Clamp(settings.CraftOverlayLines, 3, 30);
        var lines = missing.Take(max).Select(l => new CraftOverlayMaterial(l.Item, CraftText.ItemName(l.Item), CraftText.GradeColor(l.Item), l.Have, l.Needed,
            CraftWorkshop.PriceOf(book, l.Item) is long p ? p * l.Missing : null, Elsewhere(stock, l.Item))).ToList();
        var recipes = results.Take(RecipeLines).Select(r => new CraftOverlayRecipe(r.Recipe.Product, CraftText.RecipeName(r.Recipe), CraftText.GradeColor(r.Recipe.Product), r.Entry.Count, r.Percent)).ToList();
        long? toBuy = book is null ? null : plan.Sum(p => p.ToBuy ?? 0);
        return new CraftOverlayView(percent, toBuy, plan.Sum(p => p.Unpriced), recipes, Math.Max(0, results.Count - RecipeLines), lines, Math.Max(0, missing.Count - max), false);
    }

    /// <summary>Where what is there lies when it is not all in the bag: "Storage 10 · Lumi 12".</summary>
    private static string? Elsewhere(CraftStock stock, int item)
    {
        IReadOnlyList<CraftPlace> places = stock.PlacesOf(item);
        if (places.Count == 0 || places.All(p => p.Kind == CraftPlaceKind.Bag))
        {
            return null;
        }

        return string.Join(" · ", places.Take(3).Select(p => p.Label));
    }

    /// <summary>What the overlay shows while it is placed with nothing planned.</summary>
    private static CraftOverlayView Sample()
    {
        var loc = LocalizationManager.Instance;
        Dictionary<int, Color> grades = CharacterWindow.GradeColors;
        return new CraftOverlayView(62, 184_000, 0,
            new[] { new CraftOverlayRecipe(0, "Your recipe", grades[3], 10, 62) }, 0,
            new[]
            {
                new CraftOverlayMaterial(0, loc["Craft.Sample.1"], grades[1], 3, 50, 94_000, "Storage 3"),
                new CraftOverlayMaterial(0, loc["Craft.Sample.2"], grades[2], 0, 20, 62_000, null),
                new CraftOverlayMaterial(0, loc["Craft.Sample.3"], grades[3], 2, 6, 28_000, "Server 2"),
            }, 0, true);
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.ShowCraftOverlay)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.CraftOverlayLocked;
        _overlay ??= new CraftOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        // the stock, the plan and the market worked out once a second (the window asks twice)
        if ((DateTime.UtcNow - _viewAt).TotalSeconds >= 1 || _viewLocked != locked)
        {
            _viewAt = DateTime.UtcNow;
            _viewLocked = locked;
            Aion2EntityDirectory? dir = _directory();
            string me = CraftText.Character(dir);
            int server = dir?.LocalCharacter is { ServerId: > 0 } local ? local.ServerId : 0;
            CraftStock stock = CraftStock.For(me, me.Length > 0, _settings, server);
            MarketBook? book = stock.ServerId > 0 ? ExchangePrices.For(stock.ServerId) : null;
            bool elyos = CraftText.IsElyos(dir);
            List<CraftPlanLine> plan = CraftWorkshop.Plan(CraftPlan.For(me), stock, _settings.CraftResolve, elyos, book);
            _view = ViewOf(stock, book, plan, _settings, sample: !locked);
        }

        _overlay.Render(_view);
        if (!GameWindow.OverlayMayShow() || (locked && !_overlay.HasContent))
        {
            _overlay.ShowOverlay(false);
            return;
        }

        _overlay.ShowOverlay(true);
    }
}
