using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>Where some of a material lies: the crafting character's bag or storage, the server storage, or another character (its name).</summary>
public enum CraftPlaceKind
{
    Bag,
    Storage,
    Server,
    Character,
}

/// <summary>Some of a material in one place.</summary>
public readonly record struct CraftPlace(CraftPlaceKind Kind, string? Character, long Count)
{
    /// <summary>"Bag 3", "Storage 10", "Server 4", "Fighter 12".</summary>
    public string Label => (Kind switch
    {
        CraftPlaceKind.Bag => "Bag",
        CraftPlaceKind.Storage => "Storage",
        CraftPlaceKind.Server => "Server",
        _ => Character ?? "?",
    }) + " " + Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>
/// Everything the crafting character can use (10 November 2026, the owner: "the crafting calculator does not detect materials from server storage, character
/// storage and other characters' storage to tell you what to craft"): its bag and its storage (bound or not), the server storage it shares, and what its other
/// characters on the same server and account hold unbound - each place by name. The character logged in now counts from this login's stock; one seen before
/// (the app started after the login, or crafting for another character) from what its last login left (<see cref="Aion2Inventories"/>), so nothing reads as
/// missing only because the app came up late. The places switched off on the page don't count.
/// </summary>
public sealed class CraftStock
{
    public string Character { get; private init; } = "";

    public int ServerId { get; private init; }

    /// <summary>Counted from the login going on now (else from what the last one left).</summary>
    public bool Live { get; private init; }

    /// <summary>When the character's holdings were read (the login now, or the last one).</summary>
    public DateTime? SeenAt { get; private init; }

    /// <summary>The other characters whose unbound materials count.</summary>
    public IReadOnlyList<string> Others { get; private init; } = Array.Empty<string>();

    /// <summary>What the character can use, per item.</summary>
    public Dictionary<int, long> Usable { get; } = new();

    /// <summary>Where each item lies, the most first.</summary>
    public Dictionary<int, List<CraftPlace>> Places { get; } = new();

    public long Have(int item) => Usable.GetValueOrDefault(item);

    public IReadOnlyList<CraftPlace> PlacesOf(int item) => Places.TryGetValue(item, out List<CraftPlace>? list) ? list : Array.Empty<CraftPlace>();

    /// <summary>Whether anything at all is known of the character's holdings.</summary>
    public bool Known => SeenAt is not null;

    /// <summary>The stock of <paramref name="me"/> (the character logged in when <paramref name="loggedIn"/>) with the places the settings count.</summary>
    public static CraftStock For(string me, bool loggedIn, MeterSettings s, int serverId)
    {
        CharacterHoldings? saved = Aion2Inventories.Characters().FirstOrDefault(c => c.Name == me);
        bool live = loggedIn && Aion2Stock.HasData;
        IReadOnlyList<CharacterHoldings> mates = s.CraftCountOtherCharacters && me.Length > 0 ? Aion2Inventories.Mates(me) : Array.Empty<CharacterHoldings>();
        int savedServer = saved?.ServerId ?? 0;
        var stock = new CraftStock
        {
            Character = me, ServerId = savedServer > 0 ? savedServer : serverId, Live = live, SeenAt = live ? Aion2Stock.SnapshotAt : saved?.SavedAt,
            Others = mates.Select(m => m.Name).ToList(),
        };

        void Add(CraftPlaceKind kind, string? who, IReadOnlyDictionary<int, long> items, bool unboundOnly)
        {
            foreach ((int item, long count) in items)
            {
                if (count <= 0 || Aion2Inventories.IsCurrency(item) || (unboundOnly && Aion2Inventories.IsBound(item)))
                {
                    continue;
                }

                stock.Usable[item] = stock.Usable.GetValueOrDefault(item) + count;
                if (!stock.Places.TryGetValue(item, out List<CraftPlace>? list))
                {
                    stock.Places[item] = list = new List<CraftPlace>();
                }

                list.Add(new CraftPlace(kind, who, count));
            }
        }

        if (live)
        {
            if (s.CraftCountInventory)
            {
                Add(CraftPlaceKind.Bag, null, Aion2Stock.Totals(StockSources.Inventory), false);
            }

            if (s.CraftCountStorage)
            {
                Add(CraftPlaceKind.Storage, null, Aion2Stock.Totals(StockSources.CharacterStorage), false);
            }

            if (s.CraftCountAccountStorage)
            {
                Add(CraftPlaceKind.Server, null, Aion2Stock.Totals(StockSources.AccountStorage), false);
            }
        }
        else
        {
            if (saved is not null && s.CraftCountInventory)
            {
                Add(CraftPlaceKind.Bag, null, saved.Bag, false);
            }

            if (saved is not null && s.CraftCountStorage)
            {
                Add(CraftPlaceKind.Storage, null, saved.Storage, false);
            }

            if (s.CraftCountAccountStorage && Aion2Inventories.AccountOf(me) is { } server)
            {
                Add(CraftPlaceKind.Server, null, server.Items, false);
            }
        }

        foreach (CharacterHoldings mate in mates)
        {
            var both = new Dictionary<int, long>(mate.Bag);
            foreach ((int item, long count) in mate.Storage)
            {
                both[item] = both.GetValueOrDefault(item) + count;
            }

            Add(CraftPlaceKind.Character, mate.Name, both, true);
        }

        stock.SortPlaces();
        return stock;
    }

    /// <summary>A made-up stock, for the pictures of the tool (render-craft): each item in the places given.</summary>
    public static CraftStock Made(string character, int serverId, IReadOnlyList<string> others, IEnumerable<(int Item, CraftPlace Place)> places)
    {
        var stock = new CraftStock { Character = character, ServerId = serverId, Live = true, SeenAt = DateTime.Now, Others = others };
        foreach ((int item, CraftPlace place) in places)
        {
            if (place.Count <= 0)
            {
                continue;
            }

            stock.Usable[item] = stock.Usable.GetValueOrDefault(item) + place.Count;
            if (!stock.Places.TryGetValue(item, out List<CraftPlace>? list))
            {
                stock.Places[item] = list = new List<CraftPlace>();
            }

            list.Add(place);
        }

        stock.SortPlaces();
        return stock;
    }

    private void SortPlaces()
    {
        foreach (List<CraftPlace> list in Places.Values)
        {
            list.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : b.Count.CompareTo(a.Count));
        }
    }
}

/// <summary>A recipe of the plan worked out: what it needs, what it is worth and costs at the market, and what is left to buy.</summary>
public sealed record CraftPlanLine(CraftResult Result, long? Worth, long? MaterialsWorth, long? ToBuy, int Unpriced)
{
    public long? Profit => Worth is long w && MaterialsWorth is long m ? w - m : null;
}

/// <summary>A recipe the character can craft now from what it holds: how many times, and what one craft is worth and costs at the market.</summary>
public sealed record CraftChance(CraftRecipe Recipe, int Times, long? WorthEach, long? CostEach)
{
    public long? ProfitEach => WorthEach is long w && CostEach is long c ? w - c : null;
}

/// <summary>
/// The plan against the stock and the market (prices from aion2.exchange for the character's server, see <see cref="ExchangePrices"/>), and what the stock
/// lets the character craft now.
/// </summary>
public static class CraftWorkshop
{
    /// <summary>A market price (what one is worth: the average sale, else the cheapest listing ...), or null without one.</summary>
    public static long? PriceOf(MarketBook? book, int item) => book is not null && book.Items.TryGetValue(item, out MarketPrice? p) && p.Value is > 0 ? p.Value : null;

    /// <summary>The plan worked out against <paramref name="stock"/>: one line per recipe, in the plan's order, with the market's numbers.</summary>
    public static List<CraftPlanLine> Plan(IEnumerable<CraftEntry> entries, CraftStock stock, bool resolve, bool elyos, MarketBook? book)
    {
        var lines = new List<CraftPlanLine>();
        foreach (CraftResult r in CraftCalc.Compute(entries, stock.Usable, resolve, elyos))
        {
            long? worth = PriceOf(book, r.Recipe.Product) is long p ? p * r.Recipe.Count * r.Count : null;
            long materials = 0, buy = 0;
            int unpriced = 0;
            bool allPriced = true;
            foreach (CraftLine m in r.Materials)
            {
                if (PriceOf(book, m.Item) is long price)
                {
                    materials += price * m.Needed;
                    buy += price * m.Missing;
                }
                else
                {
                    allPriced = false;
                    if (m.Missing > 0)
                    {
                        unpriced++;
                    }
                }
            }

            lines.Add(new CraftPlanLine(r, worth, allPriced ? materials : null, book is null ? null : buy, unpriced));
        }

        return lines;
    }

    /// <summary>
    /// Every recipe of the faction the stock covers at least once (the materials of the ones in between counted, an ingot or a dust made from what lies
    /// there), how many times, with the market's numbers: the most profitable first, then the ones without prices by level.
    /// </summary>
    public static List<CraftChance> CanCraft(CraftStock stock, bool elyos, MarketBook? book)
    {
        var chances = new List<CraftChance>();
        foreach (CraftRecipe recipe in Aion2CraftData.Visible(elyos))
        {
            // a quick look first: every direct material there, or made from what is (a material with a recipe of its own)
            if (recipe.Components.Any(c => stock.Have(c.Item) < c.Count && Aion2CraftData.ProducerOf(c.Item, elyos) is null))
            {
                continue;
            }

            int times = Times(recipe, stock, elyos);
            if (times <= 0)
            {
                continue;
            }

            long? worth = PriceOf(book, recipe.Product) is long p ? p * recipe.Count : null;
            long? cost = CostOfOne(recipe, stock, elyos, book);
            chances.Add(new CraftChance(recipe, times, worth, cost));
        }

        // one line per product and profession: an ingot made one at a time and ten at a time is the same choice (the one that makes the most of it stays)
        chances = chances.GroupBy(c => (c.Recipe.Product, c.Recipe.Category)).Select(g => g.OrderByDescending(c => (long)c.Times * c.Recipe.Count).ThenByDescending(c => c.Recipe.Count).First()).ToList();
        return chances.OrderByDescending(c => c.ProfitEach.HasValue).ThenByDescending(c => c.ProfitEach ?? 0).ThenByDescending(c => c.WorthEach ?? 0)
            .ThenByDescending(c => c.Recipe.Level).ToList();
    }

    /// <summary>How many times the stock covers the recipe (materials in between made from it too), up to 999.</summary>
    private static int Times(CraftRecipe recipe, CraftStock stock, bool elyos)
    {
        bool Covers(int n) => CraftCalc.Compute(new[] { new CraftEntry { Recipe = recipe.Id, Count = n } }, stock.Usable, true, elyos) is [{ } r] && r.Materials.All(m => m.Missing == 0);
        if (!Covers(1))
        {
            return 0;
        }

        int low = 1, high = 2;
        while (high <= 999 && Covers(high))
        {
            low = high;
            high *= 2;
        }

        high = Math.Min(high, 1000);
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (Covers(mid))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>What the raw materials of one craft are worth at the market; null when one of them has no price.</summary>
    private static long? CostOfOne(CraftRecipe recipe, CraftStock stock, bool elyos, MarketBook? book)
    {
        if (book is null || CraftCalc.Compute(new[] { new CraftEntry { Recipe = recipe.Id, Count = 1 } }, new Dictionary<int, long>(), true, elyos) is not [{ } r])
        {
            return null;
        }

        long sum = 0;
        foreach (CraftLine m in r.Materials)
        {
            if (PriceOf(book, m.Item) is not long price)
            {
                return null;
            }

            sum += price * m.Needed;
        }

        return sum;
    }

    /// <summary>"1.24M", "84K", "950" - Kinah the short way.</summary>
    public static string Kinah(long value)
    {
        long a = Math.Abs(value);
        string text = a >= 1_000_000_000 ? (a / 1_000_000_000.0).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + "B"
            : a >= 1_000_000 ? (a / 1_000_000.0).ToString(a >= 100_000_000 ? "0" : "0.##", System.Globalization.CultureInfo.CurrentCulture) + "M"
            : a >= 10_000 ? (a / 1000.0).ToString(a >= 100_000 ? "0" : "0.#", System.Globalization.CultureInfo.CurrentCulture) + "K"
            : a.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        return (value < 0 ? "−" : "") + text;
    }
}
