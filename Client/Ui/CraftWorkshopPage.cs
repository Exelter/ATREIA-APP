using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The crafting page (10 November 2026, the owner: "the crafting calculator is outdated: it does not detect materials from server storage, character storage
/// and other characters' storage to tell you what to craft, and the design is very dated - I want something magnificent; pull prices from aion2.exchange").
/// Three tabs under one head: the plan (the recipes marked, each with what is there and where - the bag, the storage, the server storage, another character -
/// what is missing and what buying it costs, and what the crafts are worth), what the character can craft now from everything it holds (the most profitable
/// first), and every recipe to search and add. The head says whose stock counts (a click switches each place), how far the plan is, what is left to buy and
/// what it all is worth, at the prices aion2.exchange has for the character's server. The same page shows in the app and in the meter's panel over the game.
/// </summary>
public sealed class CraftWorkshopPage : StackPanel
{
    private static readonly Color Gold = Color.FromRgb(0xDC, 0xB8, 0x77);
    private static readonly Color Good = Color.FromRgb(0x6C, 0xD4, 0x8A);
    private static readonly Color Bad = Color.FromRgb(0xFF, 0x7A, 0x6E);
    private static readonly string[] Categories = { "Blacksmithing", "Tailoring", "Jewelcrafting", "Alchemy", "Cooking" };

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly StackPanel _head = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 14) };
    private readonly StackPanel _body = new();
    private readonly Border _searchRow = new() { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 2, 10, 2), Margin = new Thickness(0, 0, 0, 12),
        Width = 520, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _searchHint = new() { Text = "Search a recipe or a material", FontSize = 13.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBox _search = new() { FontSize = 13.5, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 6, 0, 6) };
    private string _tab;
    private string _category = "";
    private string? _crafter;
    private string _shown = "";
    private bool _showAll;

    public CraftWorkshopPage(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        MeterSettings s = MeterSettings.Load();
        _tab = s.CraftTab is "plan" or "now" or "recipes" ? s.CraftTab : "plan";
        _showAll = s.CraftShowAll;
        MaxWidth = 1200; // as the Legion page: the width of the window, centred past 1200
        SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
        Children.Add(_head);
        Children.Add(_tabs);
        Children.Add(_searchRow);
        Children.Add(_body);

        // the search field stays where it is while the list under it is drawn again, so typing keeps the focus
        _search.SetResourceReference(TextBox.ForegroundProperty, "Brush.Text");
        _search.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
        _searchHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _searchRow.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        _searchRow.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var field = new DockPanel();
        TextBlock glass = Glyph("\uE721", 13, "Brush.TextMuted", new Thickness(0, 0, 10, 0));
        DockPanel.SetDock(glass, Dock.Left);
        field.Children.Add(glass);
        var layer = new Grid();
        layer.Children.Add(_searchHint);
        layer.Children.Add(_search);
        field.Children.Add(layer);
        _searchRow.Child = field;
        _search.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _shown = "";
            Refresh();
        };
        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _refresh.Start();
            CraftPlan.Changed -= OnPlanChanged;
            CraftPlan.Changed += OnPlanChanged;
            _shown = "";
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _refresh.Stop();
            CraftPlan.Changed -= OnPlanChanged;
        };
        Refresh();
    }

    /// <summary>For the pictures of the tool (render-craft): the stock and the market shown instead of the live ones.</summary>
    public static (CraftStock Stock, MarketBook? Book)? Picture { get; set; }

    /// <summary>Shows one of the tabs ("plan", "now", "recipes") without keeping it as the one to open with.</summary>
    public void ShowTab(string tab)
    {
        _tab = tab;
        _shown = "";
        Refresh();
    }

    public void Stop()
    {
        _refresh.Stop();
        CraftPlan.Changed -= OnPlanChanged;
    }

    private void OnPlanChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        _shown = "";
        Refresh();
    }));

    // ------------------------------------------------------------------ what is shown, drawn again only when it changed

    private string Crafter(Aion2EntityDirectory? dir)
    {
        string me = CraftText.Character(dir);
        if (_crafter is { Length: > 0 } picked && Aion2Inventories.Characters().Any(c => c.Name == picked))
        {
            return picked;
        }

        return me.Length > 0 ? me : Aion2Inventories.Characters().FirstOrDefault()?.Name ?? "";
    }

    private void Refresh()
    {
        Aion2EntityDirectory? dir = _directory();
        MeterSettings s = MeterSettings.Load();
        string crafter = Picture?.Stock.Character ?? Crafter(dir);
        bool loggedIn = crafter.Length > 0 && crafter == CraftText.Character(dir);
        int ownServer = loggedIn && dir?.LocalCharacter is { ServerId: > 0 } local ? local.ServerId : Aion2Inventories.Characters().FirstOrDefault(c => c.Name == crafter)?.ServerId ?? 0;
        CraftStock stock = Picture?.Stock ?? CraftStock.For(crafter, loggedIn, s, ownServer);
        MarketBook? book = Picture is { } picture ? picture.Book : stock.ServerId > 0 ? ExchangePrices.For(stock.ServerId) : null;
        bool elyos = CraftText.IsElyos(dir);
        List<CraftEntry> entries = CraftPlan.For(crafter);
        string key = string.Join("|", crafter, loggedIn, _tab, _category, _search.Text, _showAll, ExchangePrices.Version, stock.SeenAt?.Ticks, s.CraftCountInventory, s.CraftCountStorage,
            s.CraftCountAccountStorage, s.CraftCountAlts, s.CraftResolve, LocalizationManager.Instance.Language,
            string.Join(",", entries.Select(e => e.Recipe + "x" + e.Count + (e.AccountWide ? "a" : "c"))),
            string.Join(",", stock.Usable.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        List<CraftPlanLine> plan = CraftWorkshop.Plan(entries, stock, s.CraftResolve, elyos, book);
        DrawHead(stock, book, plan, s, dir);
        DrawTabs(entries.Count);
        _searchRow.Visibility = _tab == "recipes" ? Visibility.Visible : Visibility.Collapsed;
        _body.Children.Clear();
        switch (_tab)
        {
            case "now":
                DrawCanCraft(stock, book, elyos, entries);
                break;
            case "recipes":
                DrawRecipes(stock, book, elyos, entries);
                break;
            default:
                DrawPlan(plan, stock, book);
                break;
        }
    }

    // ------------------------------------------------------------------ the head: whose stock, the places, the numbers of the plan

    private void DrawHead(CraftStock stock, MarketBook? book, List<CraftPlanLine> plan, MeterSettings s, Aion2EntityDirectory? dir)
    {
        _head.Children.Clear();
        var hero = Card(new Thickness(22, 18, 22, 18));
        var stack = new StackPanel();
        hero.Child = stack;

        // who crafts, and the places that count
        var who = new DockPanel { LastChildFill = false };
        var whoLeft = new StackPanel { Orientation = Orientation.Horizontal };
        whoLeft.Children.Add(Text("Crafting as", 12.5, "Brush.TextMuted", margin: new Thickness(0, 0, 8, 0)));
        whoLeft.Children.Add(CrafterChip(stock.Character, dir));
        if (stock.SeenAt is DateTime seen)
        {
            whoLeft.Children.Add(Text((stock.Live ? "· items as of this login " : "· items as of its last login ") + Local(seen).ToString("ddd HH:mm", CultureInfo.CurrentCulture), 12, "Brush.TextMuted",
                margin: new Thickness(10, 0, 0, 0)));
        }
        else
        {
            whoLeft.Children.Add(Text("· log in with the app running and its items are read", 12, "Brush.TextMuted", margin: new Thickness(10, 0, 0, 0)));
        }

        who.Children.Add(whoLeft);
        var prices = Text(PriceNote(stock.ServerId, book), 12, "Brush.TextMuted");
        prices.ToolTip = "Market prices of the character's server: the average sale, else the cheapest listing. Read again every half hour.";
        DockPanel.SetDock(prices, Dock.Right);
        who.Children.Add(prices);
        stack.Children.Add(who);

        var places = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        places.Children.Add(SourceChip("\uE7BF", "Bag", s.CraftCountInventory, "What the character carries", v => Change(x => x.CraftCountInventory = v)));
        places.Children.Add(SourceChip("\uE8F1", "Storage", s.CraftCountStorage, "The character's own storage", v => Change(x => x.CraftCountStorage = v)));
        places.Children.Add(SourceChip("\uE821", "Server storage", s.CraftCountAccountStorage, "The storage every character of the account on this server shares",
            v => Change(x => x.CraftCountAccountStorage = v)));
        string others = stock.Others.Count > 0 ? "Other characters: " + string.Join(", ", stock.Others) : "Other characters";
        places.Children.Add(SourceChip("\uE716", others, s.CraftCountAlts,
            "What your other characters on this server hold unbound (as their last login left it): they can send it through the server storage. Bound items stay with their owner.",
            v => Change(x => x.CraftCountAlts = v)));
        places.Children.Add(SourceChip("\uE8B3", "Break down in-between materials", s.CraftResolve,
            "An ingot, a dust or a thread the plan needs is made from what you have: the list shows the ore and the raw materials", v => Change(x => x.CraftResolve = v)));
        stack.Children.Add(places);

        // the plan in four numbers
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 16, 0, 0) };
        long need = plan.Sum(p => p.Result.Materials.Sum(m => m.Needed)), have = plan.Sum(p => p.Result.Materials.Sum(m => Math.Min(m.Have, m.Needed)));
        int percent = need == 0 ? (plan.Count > 0 ? 100 : 0) : (int)(100 * have / need);
        tiles.Children.Add(Tile("READY", plan.Count == 0 ? "–" : percent + "%", plan.Count == 0 ? "no recipe in the plan yet" : $"{plan.Count} recipe{(plan.Count == 1 ? "" : "s")} in the plan",
            percent >= 100 ? Good : Gold, plan.Count == 0 ? null : percent));
        long? toBuy = book is null ? null : plan.Sum(p => p.ToBuy ?? 0);
        int unpriced = plan.Sum(p => p.Unpriced);
        tiles.Children.Add(Tile("TO BUY", toBuy is long b ? (b == 0 && unpriced == 0 ? "nothing" : CraftWorkshop.Kinah(b)) : "…",
            toBuy is null ? "prices are loading" : unpriced > 0 ? $"+ {unpriced} material{(unpriced == 1 ? "" : "s")} without a price" : "Kinah, at the market's prices", Gold, null));
        long? worth = plan.All(p => p.Worth is not null) && plan.Count > 0 ? plan.Sum(p => p.Worth!.Value) : null;
        tiles.Children.Add(Tile("WORTH WHEN CRAFTED", worth is long w ? CraftWorkshop.Kinah(w) : "–", worth is null ? (plan.Count == 0 ? "add a recipe" : "a product has no price yet") : "what the products sell for",
            Color.FromRgb(0xE9, 0xEE, 0xF2), null));
        long? profit = plan.Count > 0 && plan.All(p => p.Profit is not null) ? plan.Sum(p => p.Profit!.Value) : null;
        tiles.Children.Add(Tile("PROFIT AT MARKET", profit is long pr ? (pr >= 0 ? "+" : "") + CraftWorkshop.Kinah(pr) : "–", "products against all their materials at the market",
            profit is long q && q < 0 ? Bad : Good, null));
        stack.Children.Add(tiles);
        _head.Children.Add(hero);

        void Change(Action<MeterSettings> set)
        {
            MeterSettings now = MeterSettings.Load();
            set(now);
            now.Save();
            _shown = "";
            Refresh();
        }
    }

    private static string PriceNote(int server, MarketBook? book)
    {
        if (server <= 0)
        {
            return "Market prices: the character's server is not known yet";
        }

        if (book is null)
        {
            return ExchangePrices.IsLoading(server) ? "Loading market prices…" : "Market prices can't be read right now";
        }

        string where = Aion2.Protocol.Aion2Servers.IsKnown(server) ? Aion2.Protocol.Aion2Servers.NameOf(server) : book.Name;
        return $"Market prices · {where}" + (book.Captured is DateTime at ? " · " + Ago(at) : "");
    }

    private static string Ago(DateTime at)
    {
        TimeSpan span = DateTime.UtcNow - (at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : at);
        return span.TotalMinutes < 1 ? "just now" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago" : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago" : $"{(int)span.TotalDays} d ago";
    }

    /// <summary>The crafting character: a click lists the characters whose items are known, to plan for one of them.</summary>
    private FrameworkElement CrafterChip(string name, Aion2EntityDirectory? dir)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Text(name.Length > 0 ? name : "no character yet", 13, "Brush.Text", bold: true));
        line.Children.Add(Glyph("\uE70D", 9, "Brush.TextMuted", new Thickness(7, 2, 0, 0)));
        var chip = new Border { Child = line, Padding = new Thickness(10, 3, 9, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            ToolTip = "Plan for another of your characters (its items as its last login left them)" };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        chip.MouseLeftButtonUp += (_, _) =>
        {
            var menu = new ContextMenu();
            string me = CraftText.Character(dir);
            foreach (CharacterHoldings c in Aion2Inventories.Characters())
            {
                var item = new MenuItem { Header = c.Name + (c.Name == me ? "  (logged in)" : "") };
                string pick = c.Name;
                item.Click += (_, _) =>
                {
                    _crafter = pick == me ? null : pick;
                    _shown = "";
                    Refresh();
                };
                menu.Items.Add(item);
            }

            if (menu.Items.Count > 0)
            {
                menu.PlacementTarget = chip;
                menu.IsOpen = true;
            }
        };
        return chip;
    }

    /// <summary>A place that counts or not: a click switches it.</summary>
    private static FrameworkElement SourceChip(string glyph, string text, bool on, string tip, Action<bool> set)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var mark = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = on ? new SolidColorBrush(Gold) : Brushes.Transparent, BorderBrush = on ? new SolidColorBrush(Gold) : new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
            Child = on ? new TextBlock { Text = "\uE73E", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null };
        line.Children.Add(mark);
        line.Children.Add(Glyph(glyph, 12, on ? "Brush.Text" : "Brush.TextMuted", new Thickness(0, 1, 6, 0)));
        line.Children.Add(Text(text, 12.5, on ? "Brush.Text" : "Brush.TextMuted"));
        var chip = new Border { Child = line, Padding = new Thickness(9, 5, 12, 6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, ToolTip = tip };
        chip.SetResourceReference(Border.BackgroundProperty, on ? "Brush.Control" : "Brush.Panel");
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        chip.MouseLeftButtonUp += (_, _) => set(!on);
        return chip;
    }

    private static FrameworkElement Tile(string title, string value, string sub, Color colour, int? percent)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        stack.Children.Add(Text(title, 10.5, "Brush.TextMuted", bold: true));
        var number = new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour), Margin = new Thickness(0, 2, 0, 0) };
        number.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        stack.Children.Add(number);
        if (percent is int p)
        {
            stack.Children.Add(Bar(p, 150, 5, new Thickness(0, 4, 0, 4)));
        }

        TextBlock subText = Text(sub, 11.5, "Brush.TextMuted");
        subText.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(subText);
        return stack;
    }

    // ------------------------------------------------------------------ the tabs

    private void DrawTabs(int planned)
    {
        _tabs.Children.Clear();
        _tabs.Children.Add(TabPill("plan", "\uE7C1", "Your plan" + (planned > 0 ? $"  {planned}" : "")));
        _tabs.Children.Add(TabPill("now", "\uE945", "What you can craft"));
        _tabs.Children.Add(TabPill("recipes", "\uE721", "All recipes"));
    }

    private FrameworkElement TabPill(string id, string glyph, string text)
    {
        bool on = _tab == id;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 8, 0), Foreground = on ? new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05)) : new SolidColorBrush(Gold) });
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            label.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05));
        }
        else
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        }

        line.Children.Add(label);
        var pill = new Border { Child = line, Padding = new Thickness(14, 7, 16, 8), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Cursor = on ? Cursors.Arrow : Cursors.Hand };
        if (on)
        {
            pill.Background = new SolidColorBrush(Gold);
            pill.BorderBrush = new SolidColorBrush(Gold);
        }
        else
        {
            pill.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            pill.MouseLeftButtonUp += (_, _) =>
            {
                _tab = id;
                MeterSettings s = MeterSettings.Load();
                s.CraftTab = id;
                s.Save();
                _shown = "";
                Refresh();
            };
        }

        return pill;
    }

    // ------------------------------------------------------------------ the plan

    private void DrawPlan(List<CraftPlanLine> plan, CraftStock stock, MarketBook? book)
    {
        if (plan.Count == 0)
        {
            _body.Children.Add(Empty("\uE7C1", "Nothing planned yet",
                "Find what you want to make under All recipes, or let What you can craft show you what your items already cover, and add it with one click. "
                + "The plan then counts everything you hold - the bag, the storage, the server storage and your other characters - and what is left to buy."));
            return;
        }

        var toggle = Link(_showAll ? "Only what's missing" : "Show every material", () =>
        {
            _showAll = !_showAll;
            MeterSettings s = MeterSettings.Load();
            s.CraftShowAll = _showAll;
            s.Save();
            _shown = "";
            Refresh();
        });
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.Margin = new Thickness(0, -6, 4, 8);
        _body.Children.Add(toggle);
        foreach (CraftPlanLine line in plan)
        {
            _body.Children.Add(PlanCard(line, stock, book));
        }
    }

    private FrameworkElement PlanCard(CraftPlanLine line, CraftStock stock, MarketBook? book)
    {
        CraftResult r = line.Result;
        CraftEntry entry = r.Entry;
        var card = Card(new Thickness(18, 16, 18, 14));
        card.Margin = new Thickness(0, 0, 0, 12);
        var stack = new StackPanel();
        card.Child = stack;

        // the recipe: its product, how many, how far
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Border icon = CraftText.IconTile(r.Recipe.Product, 48);
        icon.CornerRadius = new CornerRadius(8);
        icon.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(icon);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = CraftText.RecipeName(r.Recipe), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CraftText.GradeColor(r.Recipe.Product)),
            TextTrimming = TextTrimming.CharacterEllipsis });
        string makes = r.Recipe.Count > 1 ? $" · makes {r.Recipe.Count} each" : "";
        titles.Children.Add(Text($"{KindOf(r.Recipe)}{makes}", 12, "Brush.TextMuted", margin: new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        var stepper = Stepper(entry);
        Grid.SetColumn(stepper, 2);
        head.Children.Add(stepper);
        var progress = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), MinWidth = 130 };
        var pct = new TextBlock { Text = r.Percent + "%", FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(r.Percent >= 100 ? Good : Gold) };
        progress.Children.Add(pct);
        progress.Children.Add(Bar(r.Percent, 130, 5, new Thickness(0, 3, 0, 0)));
        Grid.SetColumn(progress, 3);
        head.Children.Add(progress);
        stack.Children.Add(head);

        // the money: worth, materials, profit, to buy
        var money = new WrapPanel { Margin = new Thickness(62, 10, 0, 0) };
        if (line.Worth is long worth)
        {
            money.Children.Add(Pill("Worth " + CraftWorkshop.Kinah(worth), Color.FromRgb(0xE9, 0xEE, 0xF2), "What the products sell for at the market"));
        }

        if (line.MaterialsWorth is long mats)
        {
            money.Children.Add(Pill("Materials " + CraftWorkshop.Kinah(mats), Color.FromRgb(0xB8, 0xC0, 0xC8), "What all its materials sell for at the market (what you hold is worth that too)"));
        }

        if (line.Profit is long profit)
        {
            money.Children.Add(Pill((profit >= 0 ? "Profit +" : "Loss ") + CraftWorkshop.Kinah(Math.Abs(profit)), profit >= 0 ? Good : Bad, "The products against all their materials at the market"));
        }

        if (line.ToBuy is long buy && buy > 0)
        {
            money.Children.Add(Pill("To buy " + CraftWorkshop.Kinah(buy) + (line.Unpriced > 0 ? $" + {line.Unpriced} unpriced" : ""), Gold, "What buying the missing materials costs at the market"));
        }
        else if (r.Materials.All(m => m.Missing == 0))
        {
            money.Children.Add(Pill("Everything is there", Good, "Every material is in the places that count"));
        }

        if (book is null && stock.ServerId > 0)
        {
            money.Children.Add(Pill("Prices loading…", Color.FromRgb(0x9A, 0xA4, 0xAE), null));
        }

        money.Children.Add(ScopeChip(entry, stock.Character));
        stack.Children.Add(money);

        if (r.Intermediates.Count > 0)
        {
            var first = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(62, 8, 0, 0) };
            first.Children.Add(Glyph("\uE8B3", 12, "Brush.Accent", new Thickness(0, 1, 7, 0)));
            first.Children.Add(Text("Craft first: " + string.Join(" · ", r.Intermediates.Select(i =>
                $"{i.Crafts * (Aion2CraftData.ProducerOf(i.Item, r.Recipe.Elyos)?.Count ?? 1):N0}× {CraftText.ItemName(i.Item)}")), 12.5, "Brush.TextSubtle"));
            stack.Children.Add(first);
        }

        // the materials
        var lines = r.Materials.Where(m => _showAll || m.Missing > 0).OrderByDescending(m => m.Missing > 0).ThenByDescending(m => m.Missing)
            .ThenBy(m => CraftText.ItemName(m.Item), StringComparer.CurrentCultureIgnoreCase).ToList();
        if (lines.Count > 0)
        {
            var table = new Grid { Margin = new Thickness(62, 12, 0, 0) };
            foreach (GridLength w in new[] { new GridLength(2, GridUnitType.Star), new GridLength(64), new GridLength(64), new GridLength(1.3, GridUnitType.Star), GridLength.Auto })
            {
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            }

            int row = 0;
            void Cell(UIElement element, int column)
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                table.Children.Add(element);
            }

            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(Text("MATERIAL", 10.5, "Brush.TextMuted", bold: true), 0);
            Cell(Right(Text("NEED", 10.5, "Brush.TextMuted", bold: true)), 1);
            Cell(Right(Text("HAVE", 10.5, "Brush.TextMuted", bold: true)), 2);
            Cell(Text("WHERE", 10.5, "Brush.TextMuted", bold: true, margin: new Thickness(16, 0, 0, 0)), 3);
            Cell(Right(Text("BUY", 10.5, "Brush.TextMuted", bold: true)), 4);
            foreach (CraftLine m in lines)
            {
                row++;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var band = new Border { Margin = new Thickness(-8, 0, -8, 0), CornerRadius = new CornerRadius(6) };
                if (row % 2 == 1)
                {
                    band.SetResourceReference(Border.BackgroundProperty, "Brush.RowAlt");
                }

                Grid.SetColumnSpan(band, 5);
                Cell(band, 0);
                var name = new DockPanel { Margin = new Thickness(0, 5, 0, 5) }; // a dock, not a row: a long name ends in "…" at the column's edge
                Border mi = CraftText.IconTile(m.Item, 26);
                mi.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(mi, Dock.Left);
                name.Children.Add(mi);
                name.Children.Add(new TextBlock { Text = CraftText.ItemName(m.Item), FontSize = 13, Foreground = new SolidColorBrush(CraftText.GradeColor(m.Item)), VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = CraftText.ItemName(m.Item) });
                Cell(name, 0);
                Cell(Right(Text(m.Needed.ToString("N0", CultureInfo.CurrentCulture), 13, "Brush.Text")), 1);
                var haveText = Right(Text(m.Have.ToString("N0", CultureInfo.CurrentCulture), 13, "Brush.Text", bold: true));
                ((TextBlock)haveText).Foreground = new SolidColorBrush(m.Missing == 0 ? Good : m.Have > 0 ? Gold : Color.FromRgb(0x9A, 0xA4, 0xAE));
                Cell(haveText, 2);
                var where = new WrapPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                foreach (CraftPlace place in stock.PlacesOf(m.Item).Take(4))
                {
                    where.Children.Add(PlaceChip(place));
                }

                Cell(where, 3);
                string buyText = m.Missing == 0 ? "✓" : CraftWorkshop.PriceOf(book, m.Item) is long price ? $"{m.Missing:N0} × {CraftWorkshop.Kinah(price)} = {CraftWorkshop.Kinah(price * m.Missing)}"
                    : book is null ? "…" : "no price";
                var buyBlock = (TextBlock)Right(Text(buyText, 12.5, m.Missing == 0 ? "Brush.Heal" : "Brush.TextSubtle"));
                if (m.Missing > 0 && CraftWorkshop.PriceOf(book, m.Item) is not null)
                {
                    buyBlock.Foreground = new SolidColorBrush(Gold);
                }

                buyBlock.Margin = new Thickness(14, 0, 0, 0);
                Cell(buyBlock, 4);
            }

            stack.Children.Add(table);
        }

        return card;
    }

    /// <summary>− 3 + : how many times the recipe is crafted; × takes it out of the plan.</summary>
    private FrameworkElement Stepper(CraftEntry entry)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(SmallButton("\uE738", "One less", () =>
        {
            if (entry.Count > 1)
            {
                entry.Count--;
                CraftPlan.Touch();
            }
        }));
        var count = new TextBlock { Text = entry.Count.ToString("N0", CultureInfo.CurrentCulture) + "×", FontSize = 15, FontWeight = FontWeights.SemiBold, MinWidth = 44, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(count);
        line.Children.Add(SmallButton("\uE710", "One more (Shift: ten more)", () =>
        {
            entry.Count += Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            CraftPlan.Touch();
        }));
        line.Children.Add(SmallButton("\uE711", "Take it out of the plan", () => CraftPlan.Remove(entry), new Thickness(10, 0, 0, 0)));
        return line;
    }

    /// <summary>For every character, or for this one only.</summary>
    private static FrameworkElement ScopeChip(CraftEntry entry, string character)
    {
        string text = entry.AccountWide ? "All characters" : "Only " + (entry.Character.Length > 0 ? entry.Character : character);
        var chip = Pill(text, Color.FromRgb(0x9A, 0xA4, 0xAE), "Click: " + (entry.AccountWide ? "only for " + character : "for every character") + " (each character's own items count)");
        chip.Cursor = Cursors.Hand;
        chip.MouseLeftButtonUp += (_, _) =>
        {
            entry.AccountWide = !entry.AccountWide;
            entry.Character = entry.AccountWide ? "" : character;
            CraftPlan.Touch();
        };
        return chip;
    }

    // ------------------------------------------------------------------ what can be crafted now

    private void DrawCanCraft(CraftStock stock, MarketBook? book, bool elyos, List<CraftEntry> entries)
    {
        if (!stock.Known)
        {
            _body.Children.Add(Empty("\uE7BF", "Your items aren't known yet",
                "Log in with the app running: the game sends the bag, the storage and the server storage then. Each of your characters' items are kept from its last login."));
            return;
        }

        _body.Children.Add(CategoryChips());
        List<CraftChance> chances = CraftWorkshop.CanCraft(stock, elyos, book).Where(c => _category.Length == 0 || c.Recipe.Category == _category)
            .Where(c => !Aion2Recipes.HasData || Aion2Recipes.IsKnown(c.Recipe)).ToList();
        if (chances.Count == 0)
        {
            _body.Children.Add(Empty("\uE945", "Nothing to craft from what you hold" + (_category.Length > 0 ? " in " + CategoryName(_category) : ""),
                "Gather or buy a few materials, or switch on more places at the top (the server storage, your other characters)."));
            return;
        }

        _body.Children.Add(Text($"{chances.Count} recipe{(chances.Count == 1 ? "" : "s")} your items cover, the most profitable at the market first", 12.5, "Brush.TextMuted", margin: new Thickness(2, 0, 0, 10)));
        var wrap = new WrapPanel();
        foreach (CraftChance c in chances.Take(60))
        {
            bool planned = entries.Any(e => e.Recipe == c.Recipe.Id || Aion2CraftData.ForFaction(e.Recipe, elyos)?.Id == c.Recipe.Id);
            wrap.Children.Add(ChanceCard(c, planned, stock.Character));
        }

        _body.Children.Add(wrap);
    }

    private FrameworkElement ChanceCard(CraftChance c, bool planned, string character)
    {
        var card = Card(new Thickness(14, 12, 14, 12));
        card.Width = 356;
        card.Margin = new Thickness(0, 0, 12, 12);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border icon = CraftText.IconTile(c.Recipe.Product, 44);
        icon.CornerRadius = new CornerRadius(8);
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(icon);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = CraftText.RecipeName(c.Recipe), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CraftText.GradeColor(c.Recipe.Product)),
            TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(Text(KindOf(c.Recipe), 11.5, "Brush.TextMuted", margin: new Thickness(0, 1, 0, 6)));
        var numbers = new WrapPanel();
        numbers.Children.Add(Pill($"Can craft {c.Times:N0}×", Good, "As often as everything you hold covers it (materials in between made from it too)"));
        if (c.ProfitEach is long p)
        {
            numbers.Children.Add(Pill((p >= 0 ? "+" : "−") + CraftWorkshop.Kinah(Math.Abs(p)) + " each", p >= 0 ? Good : Bad,
                $"Sells for {CraftWorkshop.Kinah(c.WorthEach ?? 0)}, its materials for {CraftWorkshop.Kinah(c.CostEach ?? 0)} at the market"));
        }
        else if (c.WorthEach is long w)
        {
            numbers.Children.Add(Pill("Worth " + CraftWorkshop.Kinah(w), Color.FromRgb(0xE9, 0xEE, 0xF2), "What one sells for at the market"));
        }

        stack.Children.Add(numbers);
        var add = planned ? Pill("✓ In your plan", Gold, null) : ActionButton("\uE710", "Add to plan", () => CraftPlan.Add(c.Recipe.Id, character));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(add);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        card.Child = grid;
        return card;
    }

    // ------------------------------------------------------------------ every recipe

    private void DrawRecipes(CraftStock stock, MarketBook? book, bool elyos, List<CraftEntry> entries)
    {
        _body.Children.Add(CategoryChips());

        string query = _search.Text.Trim();
        IEnumerable<CraftRecipe> found = Aion2CraftData.Visible(elyos).Where(r => _category.Length == 0 || r.Category == _category);
        if (query.Length >= 2)
        {
            found = found.Where(r => CraftText.RecipeName(r).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || r.Components.Any(c => CraftText.ItemName(c.Item).Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        }

        var list = found.OrderBy(r => r.Category).ThenByDescending(r => r.Level).ThenBy(r => CraftText.RecipeName(r), StringComparer.CurrentCultureIgnoreCase).ToList();
        if (list.Count == 0)
        {
            _body.Children.Add(Empty("\uE721", "No recipe matches", "Try another word: a recipe's name or one of its materials."));
            return;
        }

        var card = Card(new Thickness(8, 6, 8, 6));
        var rows = new StackPanel();
        card.Child = rows;
        int i = 0;
        foreach (CraftRecipe r in list.Take(80))
        {
            bool planned = entries.Any(e => e.Recipe == r.Id || Aion2CraftData.ForFaction(e.Recipe, elyos)?.Id == r.Id);
            int ready = CraftCalc.Compute(new[] { new CraftEntry { Recipe = r.Id, Count = 1 } }, stock.Usable, true, elyos) is [{ } res] ? res.Percent : 0;
            rows.Children.Add(RecipeRow(r, planned, ready, book, i++ % 2 == 1, stock.Character));
        }

        _body.Children.Add(card);
        if (list.Count > 80)
        {
            _body.Children.Add(Text($"{list.Count - 80} more: type a few letters of the name to find one", 12, "Brush.TextMuted", margin: new Thickness(4, 8, 0, 0)));
        }
    }

    private FrameworkElement RecipeRow(CraftRecipe r, bool planned, int ready, MarketBook? book, bool band, string character)
    {
        var grid = new Grid { Margin = new Thickness(0) };
        foreach (GridLength w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(110), new GridLength(120), new GridLength(130) })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }

        Border icon = CraftText.IconTile(r.Product, 32);
        icon.CornerRadius = new CornerRadius(6);
        icon.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = CraftText.RecipeName(r), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CraftText.GradeColor(r.Product)),
            TextTrimming = TextTrimming.CharacterEllipsis });
        string learned = Aion2Recipes.HasData && !Aion2Recipes.IsKnown(r) ? " · not learned yet" : "";
        names.Children.Add(Text($"{KindOf(r)}{(r.Count > 1 ? $" · makes {r.Count}" : "")}{learned}", 11.5, "Brush.TextMuted"));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var price = Right(Text(CraftWorkshop.PriceOf(book, r.Product) is long p ? CraftWorkshop.Kinah(p * r.Count) : book is null ? "…" : "no price", 13, "Brush.TextSubtle"));
        ((FrameworkElement)price).ToolTip = "What one craft's products sell for at the market";
        Grid.SetColumn(price, 2);
        grid.Children.Add(price);
        var readyStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), ToolTip = "How much of one craft's materials you hold" };
        var readyText = Text(ready + "% of one", 11.5, "Brush.TextMuted");
        readyText.HorizontalAlignment = HorizontalAlignment.Right;
        readyStack.Children.Add(readyText);
        readyStack.Children.Add(Bar(ready, 100, 4, new Thickness(0, 3, 0, 0), HorizontalAlignment.Right));
        Grid.SetColumn(readyStack, 3);
        grid.Children.Add(readyStack);
        FrameworkElement add = planned ? Pill("✓ In your plan", Gold, null) : ActionButton("\uE710", "Add to plan", () => CraftPlan.Add(r.Id, character));
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(add, 4);
        grid.Children.Add(add);
        var row = new Border { Child = grid, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8) };
        if (band)
        {
            row.SetResourceReference(Border.BackgroundProperty, "Brush.RowAlt");
        }

        return row;
    }

    private FrameworkElement CategoryChips()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (string category in new[] { "" }.Concat(Categories))
        {
            bool on = _category == category;
            var label = Text(category.Length == 0 ? "All" : CategoryName(category), 12.5, on ? "Brush.Text" : "Brush.TextMuted", bold: on);
            var chip = new Border { Child = label, Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand };
            if (on)
            {
                chip.Background = new SolidColorBrush(Color.FromArgb(0x2A, Gold.R, Gold.G, Gold.B));
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, Gold.R, Gold.G, Gold.B));
            }
            else
            {
                chip.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            }

            string pick = category;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _category = pick;
                _shown = "";
                Refresh();
            };
            wrap.Children.Add(chip);
        }

        return wrap;
    }

    // ------------------------------------------------------------------ small parts

    /// <summary>"Alchemy · Lv 55"; the level left out where the recipe has none (0).</summary>
    private static string KindOf(CraftRecipe recipe) => CategoryName(recipe.Category) + (recipe.Level > 0 ? $" · Lv {recipe.Level}" : "");

    private static string CategoryName(string category)
    {
        string key = "Craft.Cat." + category;
        string text = LocalizationManager.Instance[key];
        return text == key ? category : text;
    }

    private static DateTime Local(DateTime t) => t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t;

    private static Border Card(Thickness padding)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = padding };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static FrameworkElement Empty(string glyph, string title, string text)
    {
        var card = Card(new Thickness(28, 26, 28, 26));
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 640 };
        stack.Children.Add(Glyph(glyph, 26, "Brush.Accent", new Thickness(0, 0, 0, 10), HorizontalAlignment.Left));
        stack.Children.Add(Text(title, 17, "Brush.Text", bold: true));
        TextBlock body = Text(text, 13, "Brush.TextMuted", margin: new Thickness(0, 6, 0, 0));
        body.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(body);
        card.Child = stack;
        return card;
    }

    private static TextBlock Text(string text, double size, string brush, bool bold = false, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static UIElement Right(TextBlock text)
    {
        text.HorizontalAlignment = HorizontalAlignment.Right;
        text.TextAlignment = TextAlignment.Right;
        return text;
    }

    private static TextBlock Glyph(string glyph, double size, string brush, Thickness margin, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var t = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = align };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static Border Pill(string text, Color colour, string? tip)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour) };
        return new Border
        {
            Child = label, Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ToolTip = tip,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, colour.R, colour.G, colour.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, colour.R, colour.G, colour.B)),
        };
    }

    /// <summary>"Bag 3", "Server 4", "Fighter 12": where some of a material lies.</summary>
    private static Border PlaceChip(CraftPlace place)
    {
        Color colour = place.Kind switch
        {
            CraftPlaceKind.Bag => Color.FromRgb(0xE9, 0xEE, 0xF2),
            CraftPlaceKind.Storage => Color.FromRgb(0x9F, 0xC8, 0xF0),
            CraftPlaceKind.Server => Gold,
            _ => Good,
        };
        string tip = place.Kind switch
        {
            CraftPlaceKind.Bag => "In the bag",
            CraftPlaceKind.Storage => "In the character's storage",
            CraftPlaceKind.Server => "In the server storage, which all your characters on this server share",
            _ => $"On {place.Character}, unbound: it can be sent over through the server storage",
        };
        var chip = Pill(place.Label, colour, tip);
        ((TextBlock)chip.Child).FontSize = 11.5;
        ((TextBlock)chip.Child).FontWeight = FontWeights.Normal;
        chip.Padding = new Thickness(7, 1, 8, 2);
        chip.Margin = new Thickness(0, 2, 5, 2);
        return chip;
    }

    private static Border Bar(int percent, double width, double height, Thickness margin, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var track = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(height / 2), Margin = margin, HorizontalAlignment = align, ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)) };
        track.Child = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(height / 2), Width = width * Math.Clamp(percent, 0, 100) / 100.0,
            Background = new SolidColorBrush(percent >= 100 ? Good : Gold) };
        return track;
    }

    private static Border SmallButton(string glyph, string tip, Action click, Thickness? margin = null)
    {
        var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip, Margin = margin ?? new Thickness(0),
            Child = Glyph(glyph, 11, "Brush.Text", new Thickness(0), HorizontalAlignment.Center) };
        b.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        b.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Brush.ControlHover");
        b.MouseLeave += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    private static Border ActionButton(string glyph, string text, Action click)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(0, 1, 7, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05)), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x05)), VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Child = line, Padding = new Thickness(11, 5, 13, 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = new SolidColorBrush(Gold) };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xC6, 0x86));
        b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Gold);
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    private static TextBlock Link(string text, Action click)
    {
        var t = new TextBlock { Text = text, FontSize = 12.5, Cursor = Cursors.Hand };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        t.MouseLeftButtonUp += (_, _) => click();
        return t;
    }
}
