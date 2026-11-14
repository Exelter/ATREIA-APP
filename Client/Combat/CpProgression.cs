using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The kind of slot a piece is worn in, as the progression guides group them.</summary>
public enum CpGearKind
{
    Weapon,
    Guard,
    Armor,
    Jewelry,
    Other,
}

/// <summary>A progression guide of the website: the Item Level it is written for (<see cref="To"/> not included), its page and its name.</summary>
public sealed record CpGuide(string Id, int From, int To, string Path, string Name);

/// <summary>
/// The gear a stage of the guides aims for in one kind of slot: its Item Level, how the pieces' English names start (to find the piece of the character's own
/// kind in the item data), whether it is crafted, where it comes from in the guide's words and what else the guide says about it.
/// </summary>
public sealed record CpTierGear(int ItemLevel, IReadOnlyList<string> Names, bool Crafted, string Source, string? Also = null);

/// <summary>A stage of the guides from an Item Level on: the gear it aims for per kind of slot (null: the guide names none); below 1,400 the guide leaves it to
/// the player (<see cref="Optional"/>).</summary>
public sealed record CpTier(int From, bool Optional, CpTierGear? Weapon, CpTierGear? Guard, CpTierGear? Armor, CpTierGear? Jewelry)
{
    public CpTierGear? For(CpGearKind kind) => kind switch
    {
        CpGearKind.Weapon => Weapon,
        CpGearKind.Guard => Guard,
        CpGearKind.Armor => Armor,
        CpGearKind.Jewelry => Jewelry,
        _ => null,
    };
}

/// <summary>The Combat Power of the characters of the 100 Item Level from <see cref="From"/>: how many, the lower quarter, the middle and the upper quarter.</summary>
public sealed record CpBand(int From, int Count, int Low, int Middle, int High);

/// <summary>What the characters of an Item Level band wear in a kind of slot, on average per piece: enhancement, breakthrough, and the share at IL 102 in % (of the
/// characters for the weapon and the Guard, of the pieces for armor and jewelry).</summary>
public sealed record CpWearGroup(double Enchant, double Breakthrough, double Top);

/// <summary>An Item Level band of the characters the website tracked: how many, their Combat Power and Item Level, what they wear, their Arcana cards' average
/// level, their open Daevanion nodes and their four equipped Stigmas' levels added up.</summary>
public sealed record CpWearBand(int From, int To, int Count, int Cp, int ItemLevel, CpWearGroup Weapon, CpWearGroup Guard, CpWearGroup Armor, CpWearGroup Jewelry,
    double Arcana, int Nodes, int Stigmas);

/// <summary>
/// A worn piece as the Progression tab reads it: its kind, Item Level, its tables' highest enhancement and breakthrough, whether it is Unique (a Transfer Stone
/// moves its enhancement, breakthrough and soul lines to the next Unique piece of the slot), the stage's gear for its slot, that gear's piece of the same kind,
/// and whether the stage leaves its gear to the player (<see cref="CpTier.Optional"/>).
/// </summary>
public sealed record CpProgPiece(CpPiece Piece, CpGearKind Kind, int ItemLevel, bool Unique, int MaxEnchant, int MaxExceed, CpTierGear? Target, CpItemData? TargetItem,
    bool Optional = false)
{
    /// <summary>At the stage's tier or past it (or the stage names no gear for the slot).</summary>
    public bool AtTier => Target is null || ItemLevel >= Target.ItemLevel;

    /// <summary>Worth raising: at the stage's tier, or Unique (its levels move on with a Transfer Stone). A Legendary or lower piece below the tier is not.</summary>
    public bool Keeper => AtTier || Unique;

    /// <summary>At its tables' highest enhancement.</summary>
    public bool FullyEnhanced => MaxEnchant > 0 && Piece.Enchant >= MaxEnchant;
}

/// <summary>The character in the terms of <see cref="CpWearGroup"/> for one kind of slot: the average enhancement and breakthrough of its pieces, how many are at
/// IL 102, how many it wears and the lowest Item Level among them.</summary>
public sealed record CpYouGroup(double Enchant, double Breakthrough, int Top, int Count, int ItemLevel);

/// <summary>The character against a <see cref="CpWearBand"/>: per kind of slot (null: none worn), the Arcana cards' average level (null: none), the open Daevanion
/// nodes and the four equipped Stigmas' levels added up.</summary>
public sealed record CpYou(CpYouGroup? Weapon, CpYouGroup? Guard, CpYouGroup? Armor, CpYouGroup? Jewelry, double? Arcana, int Nodes, int Stigmas);

/// <summary>
/// A task of the Road to 3,000+: done or not, and what is left - levels (cards, Stigma levels, sockets, Item Level: <see cref="Left"/>), the Item Level that adds
/// and the Combat Power its steps add on their own. <see cref="Missing"/>: slots of the task the character wears nothing in.
/// </summary>
public sealed record CpRoadTask(string Id, string Group, bool Done, int Left = 0, int ItemLevel = 0, double Cp = 0, int Missing = 0);

/// <summary>
/// The website's progression guides (atreia.app/guides: 0-700, 700-1,400, 1,400-2,100, 2,100+ and the Road to 3,000+) on the character the app reads, for the CP
/// calculator's Progression tab (14 November 2026, the owner: "add to the app as another tab to the cp calc"): the guides for its Item Level, the dungeon gear each
/// stage aims for and the character's pieces against it (a Unique piece's levels move on with a Transfer Stone, a Legendary or lower piece's don't), its Combat
/// Power against the characters of the same Item Level, what the players of its Item Level wear, and the Road to 3,000+ checklist with what each task still adds.
/// The characters' figures are the website's, read between 14 October and 14 November 2026 (Taiwan's servers left out); the Combat Power of a step is the
/// calculator's own (the item tables and the model), what the step adds on its own.
/// </summary>
public static class CpProgression
{
    /// <summary>The Item Level of the season's best gear (Global Season 1 tops out at Unique IL 102).</summary>
    public const int TopItemLevel = 102;

    /// <summary>Where the Road to 3,000+ leads, and the Item Level its checklist shows from (the 2,100+ guide's stage).</summary>
    public const int RoadGoal = 3000, RoadFrom = 2100;

    /// <summary>The highest level of a Stigma the guides count to (four at Lv. 20: 80 in all).</summary>
    public const int StigmaTop = 20;

    /// <summary>The Arcana cards worn (Chalice, Parchment, Compass, Bell, Mirror).</summary>
    public const int ArcanaCards = 5;

    public static readonly IReadOnlyList<CpGuide> Guides = new[]
    {
        new CpGuide("0", 0, 700, "/guides/progression-0-700", "0–700 IL"),
        new CpGuide("700", 700, 1400, "/guides/progression-700-1400", "700–1,400 IL"),
        new CpGuide("1400", 1400, 2100, "/guides/progression-1400-2100", "1,400–2,100 IL"),
        new CpGuide("2100", 2100, 3200, "/guides/progression-2100-plus", "2,100+ IL"),
        new CpGuide("3000", 2500, int.MaxValue, "/guides/progression-3000-plus", "Road to 3,000+"),
    };

    /// <summary>The guides written for an Item Level (two from 2,500 to 3,199: the 2,100+ guide and the Road to 3,000+).</summary>
    public static IReadOnlyList<CpGuide> GuidesFor(int itemLevel) => Guides.Where(g => itemLevel >= g.From && itemLevel < g.To).ToList();

    // the crafted IL 102 pieces: "Splendent Wise Dragon Lord" is the Elyos name, "Splendent Ebony Dragon Lord" the Asmodian one
    private static readonly string[] Splendent102 = { "Splendent Ebony", "Splendent Wise" };

    /// <summary>The crafted IL 102 piece of a kind (the Road to 3,000+'s "IL 102" tasks).</summary>
    public static readonly CpTierGear TopGear = new(TopItemLevel, Splendent102, true, "crafted Splendent Wise / Ebony Dragon Lord");

    /// <summary>
    /// The dungeon gear of each stage in the guides' words (the website's guides/gear-plan.js): IL 54 from 700 (the 700-1,400 guide leaves it to the player:
    /// quick Item Level now, or the entries saved for 1,400), IL 70 from 1,400, IL 86 from 2,100, and from 2,500 the crafted IL 102 weapon, Guard and jewelry
    /// first while most top players keep Nuakum armor much longer.
    /// </summary>
    public static readonly IReadOnlyList<CpTier> Tiers = new[]
    {
        new CpTier(700, true,
            new CpTierGear(54, new[] { "Rupture" }, false, "Rupture weapon from Krao Cave"),
            null,
            new CpTierGear(54, new[] { "Bakarma" }, false, "Bakarma armor from Draupnir"),
            new CpTierGear(54, new[] { "Berk" }, false, "Berk accessories from Krao Cave")),
        new CpTier(1400, false,
            new CpTierGear(70, new[] { "Aulamus" }, false, "Aulamus weapon from Urugugu Canyon"),
            new CpTierGear(70, new[] { "Vakron" }, false, "Vakron Guard from Vakron Sky Island"),
            new CpTierGear(70, new[] { "Vakron" }, false, "Vakron armor from Vakron Sky Island"),
            new CpTierGear(70, new[] { "Aulamus" }, false, "Aulamus accessories from Urugugu Canyon")),
        new CpTier(2100, false,
            new CpTierGear(86, new[] { "Enraged Kromede" }, false, "Enraged Kromede weapon from Fire Temple",
                "or keep an Aulamus weapon with really good soul lines until you craft the Dragon Lord weapon"),
            new CpTierGear(86, new[] { "Nuakum" }, false, "Nuakum Guard from Ferocious Horn Den"),
            new CpTierGear(86, new[] { "Nuakum" }, false, "Nuakum armor from Ferocious Horn Den"),
            new CpTierGear(86, new[] { "Enraged Kromede" }, false, "Enraged Kromede jewelry from Fire Temple")),
        new CpTier(2500, false,
            new CpTierGear(TopItemLevel, Splendent102, true, "crafted Splendent Wise / Ebony Dragon Lord weapon"),
            new CpTierGear(TopItemLevel, Splendent102, true, "crafted Splendent Wise / Ebony Dragon Lord Guard"),
            new CpTierGear(86, new[] { "Nuakum" }, false, "Nuakum armor from Ferocious Horn Den",
                "the crafted IL 102 armor comes after the weapon, Guard and jewelry"),
            new CpTierGear(TopItemLevel, Splendent102, true, "crafted Splendent Wise / Ebony Dragon Lord jewelry", "IL 102 jewelry can only be crafted")),
    };

    /// <summary>The stage of an Item Level (null below 700, where the guides name no dungeon gear, or without an Item Level).</summary>
    public static CpTier? TierFor(int? itemLevel) => itemLevel is int il ? Tiers.LastOrDefault(t => il >= t.From) : null;

    /// <summary>The kind of slot of a piece by its item data.</summary>
    public static CpGearKind KindOf(CpItemData? data) =>
        data is null ? CpGearKind.Other
        : data.Category == "Guarder" ? CpGearKind.Guard
        : data.Group.StartsWith("Weapon", StringComparison.Ordinal) ? CpGearKind.Weapon
        : data.Group.StartsWith("Armor", StringComparison.Ordinal) ? CpGearKind.Armor
        : data.Category is "Necklace" or "Earring" or "Ring" ? CpGearKind.Jewelry
        : CpGearKind.Other;

    /// <summary>Unique: the grade on the character window's scale (4), else (an item the names table lacks) its tables' group ("Weapon_Unique_86").</summary>
    public static bool IsUnique(int itemId, CpItemData data)
    {
        int grade = Aion2ItemNames.GradeOf(itemId);
        return grade > 0 ? grade == 4 : data.Group.Contains("_Unique_", StringComparison.Ordinal);
    }

    /// <summary>The worn pieces with what the stage of the Item Level aims for in their slots (weapon and Guard, armor, jewelry, the rest; by slot within).</summary>
    public static IReadOnlyList<CpProgPiece> Read(IReadOnlyList<CpPiece> pieces, int? itemLevel, char race, CpTotals totals, CpModel? model = null)
    {
        CpTier? tier = TierFor(itemLevel);
        var list = new List<CpProgPiece>();
        foreach (CpPiece p in pieces)
        {
            if (p.Data is not { } data)
            {
                continue;
            }

            CpGearKind kind = KindOf(data);
            CpTierGear? target = tier?.For(kind);
            CpItemData? item = target is not null && data.Level < target.ItemLevel ? TargetItem(data, target, race, p.Enchant, p.Exceed, totals, model) : null;
            list.Add(new CpProgPiece(p, kind, data.Level, IsUnique(p.ItemId, data), Aion2CpData.MaxEnchant(data.Group), Aion2CpData.MaxExceed(data.Group), target, item,
                tier?.Optional ?? false));
        }

        return list.OrderBy(x => x.Kind).ThenBy(x => x.Piece.Slot).ToList();
    }

    /// <summary>
    /// The piece of the stage's gear of the same kind (a Greatsword for a Greatsword, a Helmet for a Helmet) the character's race can wear (any race when its
    /// race is not known), the one with most Combat Power at the given levels (its own highest at most); null when the data has none.
    /// </summary>
    public static CpItemData? TargetItem(CpItemData mine, CpTierGear gear, char race, int enchant, int exceed, CpTotals totals, CpModel? model = null) =>
        Aion2CpData.Items
            .Where(x => x.Category == mine.Category && x.Level == gear.ItemLevel && Named(x.Id, gear) && Fits(x.Id, race, gear.Crafted))
            .OrderByDescending(x => AtLevels(x, enchant, exceed, totals, model))
            .ThenBy(x => x.Id)
            .FirstOrDefault();

    private static bool Named(int itemId, CpTierGear gear) =>
        Aion2ItemNames.NameOf(itemId, "en") is { } name && gear.Names.Any(n => name.StartsWith(n, StringComparison.Ordinal));

    // a crafted tier takes crafted pieces only; a race's own piece only for that race (D Asmodian, L Elyos, A both), any when the character's race isn't known
    private static bool Fits(int itemId, char race, bool crafted)
    {
        CpPieceCost? cost = Aion2CpCosts.Piece(itemId);
        if (crafted && cost is not { Crafted: true })
        {
            return false;
        }

        char of = cost?.Race ?? 'A';
        return of == 'A' || race == 'A' || of == race;
    }

    /// <summary>The Combat Power of a piece's own stats (base, enhancement, breakthrough; soul lines and stones aside) at given levels, its own highest at most.</summary>
    public static double AtLevels(CpItemData data, int enchant, int exceed, CpTotals totals, CpModel? model = null) =>
        CpCalc.ItemCp(data, Math.Min(enchant, Aion2CpData.MaxEnchant(data.Group)), Math.Min(exceed, Aion2CpData.MaxExceed(data.Group)), totals, model);

    /// <summary>
    /// What the stage's piece gives over the worn one at the worn one's levels: what a Transfer Stone makes of a Unique piece (enhancement, breakthrough and soul
    /// lines move; the soul lines aside here), and a new piece once it is enhanced as far. Null without a stage's piece.
    /// </summary>
    public static double? TargetCp(CpProgPiece p, CpTotals totals, CpModel? model = null) =>
        p.TargetItem is { } t && p.Piece.Data is { } data
            ? AtLevels(t, p.Piece.Enchant, p.Piece.Exceed, totals, model) - CpCalc.ItemCp(data, p.Piece.Enchant, p.Piece.Exceed, totals, model)
            : null;

    /// <summary>
    /// The Item Level the stage's piece adds: a Unique piece with a Transfer Stone keeps its enhancement and breakthrough (<c>Now</c> = <c>Later</c>), a new
    /// piece starts at +0 (<c>Now</c>) and adds the rest once it is enhanced as far (<c>Later</c>); manastones and the Theostone aside, they don't move.
    /// </summary>
    public static (int Now, int Later) TargetItemLevel(CpProgPiece p)
    {
        if (p.Target is not { } target)
        {
            return (0, 0);
        }

        int later = target.ItemLevel - p.ItemLevel;
        return p.Unique ? (later, later) : (later - p.Piece.Enchant - 5 * p.Piece.Exceed, later);
    }

    // the slots the guides count (weapon, Guard, the seven armor pieces, necklace, two earrings, two rings): the item data's kind, the slot's name and how many
    private static readonly (string Category, string Slot, int Count)[] MainSlots =
    {
        ("", "MainHand", 1), ("Guarder", "SubHand", 1), ("Helmet", "Helmet", 1), ("Shoulder", "Shoulder", 1), ("Torso", "Torso", 1), ("Pants", "Pants", 1),
        ("Gloves", "Gloves", 1), ("Boots", "Boots", 1), ("Cape", "Cape", 1), ("Necklace", "Necklace", 1), ("Earring", "Earring", 2), ("Ring", "Ring", 2),
    };

    /// <summary>The slots of the weapon, Guard, armor and jewelry the character wears nothing in, by the slot's name (MainHand, Earring ...), once per piece missing.</summary>
    public static IReadOnlyList<string> Missing(IReadOnlyList<CpProgPiece> pieces)
    {
        var missing = new List<string>();
        foreach (var (category, slot, count) in MainSlots)
        {
            int worn = category.Length == 0 ? pieces.Count(p => p.Kind == CpGearKind.Weapon) : pieces.Count(p => p.Piece.Data?.Category == category);
            for (int k = worn; k < count; k++)
            {
                missing.Add(slot);
            }
        }

        return missing;
    }

    // ------------------------------------------------------------------ Combat Power at each Item Level

    // [class ("" all), from, characters, lower quarter, middle, upper quarter] per 100 Item Level, the classes by the website's names; from the characters the
    // website read between 14 October and 14 November 2026 (Taiwan's servers left out), a band and class only with 25 characters or more
    private static readonly (string Class, int From, int Count, int Low, int Middle, int High)[] BandRows =
    {
        ("", 2000, 63, 96213, 103713, 108526), ("", 2100, 109, 98689, 105542, 115535), ("", 2200, 149, 108393, 118823, 128707), ("", 2300, 230, 121006, 129294, 134125),
        ("", 2400, 324, 127694, 132108, 138001), ("", 2500, 555, 130922, 136005, 142250), ("", 2600, 1442, 137753, 144293, 151782), ("", 2700, 1798, 150033, 157640, 165611),
        ("", 2800, 2184, 166461, 174167, 182172), ("", 2900, 2093, 181197, 188980, 197457), ("", 3000, 2112, 195631, 202298, 208093), ("", 3100, 1008, 206472, 213810, 223376),
        ("", 3200, 454, 220180, 228627, 235233), ("", 3300, 220, 230256, 241114, 248949), ("", 3400, 136, 237924, 249511, 256217), ("", 3500, 156, 246047, 256152, 264336),
        ("", 3600, 84, 266116, 271469, 279406), ("Assassin", 2300, 33, 121136, 127288, 131445), ("Assassin", 2400, 33, 125982, 127959, 134330), ("Assassin", 2500, 47, 132370, 137998, 140894),
        ("Assassin", 2600, 98, 139812, 146923, 153327), ("Assassin", 2700, 122, 149811, 159697, 166247), ("Assassin", 2800, 163, 167141, 177669, 185027), ("Assassin", 2900, 153, 182038, 189147, 197878),
        ("Assassin", 3000, 168, 197285, 202633, 207691), ("Assassin", 3100, 60, 206000, 214491, 224885), ("Assassin", 3200, 38, 224646, 230021, 236322), ("Chanter", 2200, 25, 108214, 118257, 127604),
        ("Chanter", 2300, 31, 125640, 130017, 135280), ("Chanter", 2400, 51, 129226, 132933, 138830), ("Chanter", 2500, 87, 129690, 133916, 140621), ("Chanter", 2600, 240, 137128, 143301, 150555),
        ("Chanter", 2700, 305, 149637, 158041, 164753), ("Chanter", 2800, 354, 166343, 174159, 181651), ("Chanter", 2900, 347, 180934, 187084, 194704), ("Chanter", 3000, 316, 195361, 201992, 208727),
        ("Chanter", 3100, 155, 207375, 213914, 221935), ("Chanter", 3200, 90, 216626, 226630, 232195), ("Chanter", 3300, 29, 227560, 233344, 248265), ("Cleric", 2300, 26, 115328, 126653, 130923),
        ("Cleric", 2500, 50, 127885, 131579, 137790), ("Cleric", 2600, 192, 133300, 139221, 146518), ("Cleric", 2700, 269, 144925, 152758, 160712), ("Cleric", 2800, 325, 160894, 169300, 178219),
        ("Cleric", 2900, 291, 176982, 185252, 192104), ("Cleric", 3000, 360, 189953, 199248, 205077), ("Cleric", 3100, 161, 203197, 210169, 217939), ("Cleric", 3200, 45, 214896, 228533, 234718),
        ("Cleric", 3300, 29, 230256, 238869, 246008), ("Gladiator", 2300, 46, 118894, 129688, 134153), ("Gladiator", 2400, 54, 129069, 133668, 140375), ("Gladiator", 2500, 92, 131310, 136021, 142250),
        ("Gladiator", 2600, 226, 139108, 146375, 151898), ("Gladiator", 2700, 291, 151057, 158647, 166396), ("Gladiator", 2800, 367, 166192, 173361, 180240), ("Gladiator", 2900, 368, 180445, 188074, 196385),
        ("Gladiator", 3000, 351, 196100, 201993, 207196), ("Gladiator", 3100, 166, 204932, 212885, 221462), ("Gladiator", 3200, 89, 218249, 226451, 235065), ("Gladiator", 3300, 33, 230525, 239665, 246397),
        ("Gladiator", 3500, 35, 243308, 256322, 267119), ("Gladiator", 3600, 25, 266186, 272316, 282343), ("Ranger", 2300, 30, 130186, 133430, 138141), ("Ranger", 2400, 62, 129524, 132529, 140701),
        ("Ranger", 2500, 93, 134327, 140172, 146860), ("Ranger", 2600, 212, 141603, 148109, 156042), ("Ranger", 2700, 257, 153314, 161112, 167662), ("Ranger", 2800, 344, 170817, 178991, 186229),
        ("Ranger", 2900, 346, 185310, 194152, 201375), ("Ranger", 3000, 311, 200036, 205694, 211809), ("Ranger", 3100, 178, 210092, 218359, 227707), ("Ranger", 3200, 82, 225012, 231909, 237687),
        ("Ranger", 3300, 54, 236982, 245895, 252750), ("Ranger", 3400, 30, 240075, 249636, 255543), ("Ranger", 3500, 26, 251054, 257199, 262117), ("Sorcerer", 2300, 27, 116717, 130111, 135484),
        ("Sorcerer", 2400, 30, 127086, 133632, 137742), ("Sorcerer", 2500, 70, 130538, 135344, 142429), ("Sorcerer", 2600, 164, 137622, 145949, 152060), ("Sorcerer", 2700, 192, 151038, 157234, 165014),
        ("Sorcerer", 2800, 216, 167954, 174003, 181942), ("Sorcerer", 2900, 212, 183729, 190616, 199306), ("Sorcerer", 3000, 190, 198944, 204432, 211529), ("Sorcerer", 3100, 102, 209205, 217257, 225989),
        ("Sorcerer", 3200, 37, 222509, 231908, 238062), ("Sorcerer", 3300, 27, 230372, 239618, 246043), ("Spiritmaster", 2300, 26, 126226, 128612, 131292), ("Spiritmaster", 2400, 40, 127091, 129895, 134023),
        ("Spiritmaster", 2500, 74, 131390, 136360, 141754), ("Spiritmaster", 2600, 158, 139287, 144982, 153296), ("Spiritmaster", 2700, 152, 152822, 160828, 167898), ("Spiritmaster", 2800, 195, 168968, 176016, 182540),
        ("Spiritmaster", 2900, 167, 183670, 191072, 199731), ("Spiritmaster", 3000, 175, 198344, 203885, 210523), ("Spiritmaster", 3100, 66, 210443, 218956, 226373), ("Spiritmaster", 3200, 27, 221438, 229892, 239514),
        ("Templar", 2400, 31, 126089, 131423, 136696), ("Templar", 2500, 42, 129573, 136189, 139704), ("Templar", 2600, 152, 136726, 142986, 150247), ("Templar", 2700, 210, 148031, 154986, 162881),
        ("Templar", 2800, 220, 163837, 172293, 181039), ("Templar", 2900, 209, 178046, 184546, 193792), ("Templar", 3000, 241, 192683, 200356, 205966), ("Templar", 3100, 120, 203383, 209047, 217272),
        ("Templar", 3200, 46, 217018, 222221, 231400),
    };

    /// <summary>The website's name of a class (the meter's Elementalist is its Spiritmaster).</summary>
    public static string SiteClass(string? className) => className == "Elementalist" ? "Spiritmaster" : className ?? "";

    /// <summary>The Combat Power of the characters at an Item Level, all classes (no class) or one class; null when too few were read.</summary>
    public static CpBand? BandOf(int itemLevel, string? className = null)
    {
        int from = itemLevel / 100 * 100;
        string cls = className is null ? "" : SiteClass(className);
        foreach (var r in BandRows)
        {
            if (r.Class == cls && r.From == from)
            {
                return new CpBand(r.From, r.Count, r.Low, r.Middle, r.High);
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ what the players of an Item Level wear

    /// <summary>
    /// What the characters the website tracked between 14 October and 14 November 2026 wear, by Item Level band (13,658 at level 45, Taiwan's servers left out;
    /// the Road to 3,000+'s table): enhancement and breakthrough per piece, IL 102 the crafted Splendent Wise / Ebony Dragon Lord pieces and Ludra's.
    /// </summary>
    public static readonly IReadOnlyList<CpWearBand> Wear = new CpWearBand[]
    {
        new(2500, 2800, 3795, 149539, 2693, new(14.6, 2.2, 4), new(13.5, 1.2, 0), new(10.4, 0.0, 0), new(11.1, 0.1, 0), 2.3, 286, 60),
        new(2800, 3000, 4277, 181505, 2897, new(15.0, 3.2, 19), new(14.9, 2.6, 3), new(11.6, 0.2, 0), new(13.2, 0.9, 1), 3.7, 347, 70),
        new(3000, 3200, 3120, 205338, 3067, new(15.0, 4.0, 44), new(15.0, 3.5, 16), new(13.3, 1.0, 0), new(14.6, 2.3, 7), 4.3, 375, 76),
        new(3200, 3400, 674, 231290, 3267, new(15.0, 4.8, 82), new(15.0, 4.7, 65), new(14.7, 2.5, 5), new(15.0, 3.9, 40), 4.7, 387, 80),
        new(3400, int.MaxValue, 391, 256711, 3535, new(15.0, 5.0, 97), new(15.0, 5.0, 97), new(15.0, 4.6, 71), new(15.0, 4.9, 93), 4.8, 391, 80),
    };

    /// <summary>The band of an Item Level (from 2,500; null below or without one).</summary>
    public static CpWearBand? WearOf(int? itemLevel) => itemLevel is int il ? Wear.FirstOrDefault(w => il >= w.From && il < w.To) : null;

    /// <summary>The grade of an Arcana card: the digit before the set's four (810130001: 3). 4 is Unique.</summary>
    public static int ArcanaGrade(int itemId) => itemId / 10_000 % 10;

    /// <summary>The highest level of an Arcana card of a grade: one more than the grade (the experience table, assets/aion2/items/arcana.json: grade 1 two
    /// levels ... a Unique card five).</summary>
    public static int ArcanaTop(int grade) => Math.Max(0, grade) + 1;

    /// <summary>The character in the terms of the bands.</summary>
    public static CpYou You(IReadOnlyList<CpProgPiece> pieces, IReadOnlyList<CpArcanaRow> arcana, IReadOnlyList<CpStigmaRow> stigmas, IReadOnlyList<CpBoardRow> boards)
    {
        CpYouGroup? Group(CpGearKind kind)
        {
            var of = pieces.Where(p => p.Kind == kind).ToList();
            return of.Count == 0 ? null
                : new CpYouGroup(of.Average(p => (double)p.Piece.Enchant), of.Average(p => (double)p.Piece.Exceed), of.Count(p => p.ItemLevel >= TopItemLevel), of.Count,
                    of.Min(p => p.ItemLevel));
        }

        double? cards = arcana.Count > 0 ? arcana.Average(a => (double)a.Level) : (double?)null;
        int stigmaLevels = stigmas.Where(s => s.Equipped).OrderByDescending(s => s.Level).Take(4).Sum(s => s.Level);
        return new CpYou(Group(CpGearKind.Weapon), Group(CpGearKind.Guard), Group(CpGearKind.Armor), Group(CpGearKind.Jewelry), cards, boards.Sum(b => b.Open), stigmaLevels);
    }

    // ------------------------------------------------------------------ the Road to 3,000+

    /// <summary>
    /// The Road to 3,000+ checklist (the website's guide, in its order) ticked from the character: the weapon (+15, breakthrough 3 and 5, the IL 102 one), the
    /// Guard, the jewelry, every manastone socket, the Arcana cards, the Stigmas, the armor last, and 3,000 Item Level. Each with what is left and what that adds.
    /// </summary>
    public static IReadOnlyList<CpRoadTask> Road(IReadOnlyList<CpProgPiece> pieces, int? itemLevel, IReadOnlyList<CpArcanaRow> arcana, IReadOnlyList<CpStigmaRow> stigmas,
        char race, CpTotals totals, CpModel? model = null)
    {
        var weapon = pieces.Where(p => p.Kind == CpGearKind.Weapon).Take(1).ToList();
        var guard = pieces.Where(p => p.Kind == CpGearKind.Guard).Take(1).ToList();
        var jewelry = pieces.Where(p => p.Kind == CpGearKind.Jewelry).ToList();
        var armor = pieces.Where(p => p.Kind == CpGearKind.Armor).ToList();
        var tasks = new List<CpRoadTask>
        {
            Enhanced("wpn15", "Weapon", weapon, 1, totals, model),
            Broken("wpnbt3", "Weapon", weapon, 1, 3, totals, model),
            Broken("wpnbt5", "Weapon", weapon, 1, 5, totals, model),
            Enhanced("grd15", "Weapon", guard, 1, totals, model),
            Broken("grdbt5", "Weapon", guard, 1, 5, totals, model),
            Top("wpn102", "Weapon", weapon, 1, race, totals, model),
            Top("grd102", "Weapon", guard, 1, race, totals, model),
            Enhanced("jew15", "Jewelry", jewelry, 5, totals, model),
            Broken("jewbt3", "Jewelry", jewelry, 5, 3, totals, model),
            Top("jew102", "Jewelry", jewelry, 5, race, totals, model),
        };

        int empty = pieces.Sum(p => p.Piece.EmptyStones);
        tasks.Add(new CpRoadTask("stones", "Build", empty == 0, empty, 4 * empty));
        int unique = arcana.Count(a => ArcanaGrade(a.ItemId) >= 4);
        tasks.Add(new CpRoadTask("arc5", "Build", unique >= ArcanaCards, Math.Max(0, ArcanaCards - unique)));
        int arcanaLeft = arcana.Sum(a => Math.Max(0, ArcanaTop(ArcanaGrade(a.ItemId)) - a.Level));
        tasks.Add(new CpRoadTask("arcmax", "Build", arcana.Count >= ArcanaCards && arcanaLeft == 0, arcanaLeft, arcanaLeft, Missing: Math.Max(0, ArcanaCards - arcana.Count)));
        var worn = stigmas.Where(s => s.Equipped).OrderByDescending(s => s.Level).Take(4).ToList();
        int stigmaLeft = worn.Sum(s => Math.Max(0, StigmaTop - s.Level)) + StigmaTop * Math.Max(0, 4 - worn.Count);
        tasks.Add(new CpRoadTask("stig20", "Build", stigmaLeft == 0, stigmaLeft));
        tasks.Add(Enhanced("arm15", "Armor", armor, 7, totals, model));
        tasks.Add(Broken("armbt", "Armor", armor, 7, 2, totals, model));
        int toGoal = itemLevel is int il ? Math.Max(0, RoadGoal - il) : RoadGoal;
        tasks.Add(new CpRoadTask("il3000", "Build", itemLevel >= RoadGoal, toGoal, toGoal));
        return tasks;
    }

    // every piece (as many as the slots take) at its tables' highest enhancement: the levels, Item Level and Combat Power left
    private static CpRoadTask Enhanced(string id, string group, IReadOnlyList<CpProgPiece> of, int slots, CpTotals totals, CpModel? model)
    {
        int left = 0;
        double cp = 0;
        foreach (CpProgPiece p in of)
        {
            if (p.MaxEnchant > p.Piece.Enchant && p.Piece.Data is { } data)
            {
                left += p.MaxEnchant - p.Piece.Enchant;
                cp += CpCalc.ItemCp(data, p.MaxEnchant, p.Piece.Exceed, totals, model) - CpCalc.ItemCp(data, p.Piece.Enchant, p.Piece.Exceed, totals, model);
            }
        }

        bool done = of.Count >= slots && of.All(p => p.FullyEnhanced);
        return new CpRoadTask(id, group, done, left, left, cp, Math.Max(0, slots - of.Count));
    }

    // every piece at a breakthrough level (its tables' highest at most): the levels, Item Level (5 a level) and Combat Power left, counted at full enhancement
    private static CpRoadTask Broken(string id, string group, IReadOnlyList<CpProgPiece> of, int slots, int level, CpTotals totals, CpModel? model)
    {
        int left = 0;
        double cp = 0;
        foreach (CpProgPiece p in of)
        {
            int to = Math.Min(level, p.MaxExceed);
            if (p.MaxEnchant > 0 && p.Piece.Exceed < to && p.Piece.Data is { } data)
            {
                left += to - p.Piece.Exceed;
                cp += CpCalc.ItemCp(data, p.MaxEnchant, to, totals, model) - CpCalc.ItemCp(data, p.MaxEnchant, p.Piece.Exceed, totals, model);
            }
        }

        bool done = of.Count >= slots && of.All(p => p.Piece.Exceed >= level);
        return new CpRoadTask(id, group, done, left, 5 * left, cp, Math.Max(0, slots - of.Count));
    }

    // every piece at IL 102: how many are not, the Item Level the crafted pieces add and what they give at the worn pieces' levels (a Transfer Stone moves them)
    private static CpRoadTask Top(string id, string group, IReadOnlyList<CpProgPiece> of, int slots, char race, CpTotals totals, CpModel? model)
    {
        int left = 0, itemLevel = 0;
        double cp = 0;
        foreach (CpProgPiece p in of.Where(p => p.ItemLevel < TopItemLevel))
        {
            left++;
            itemLevel += TopItemLevel - p.ItemLevel;
            if (p.Piece.Data is { } data && TargetItem(data, TopGear, race, p.Piece.Enchant, p.Piece.Exceed, totals, model) is { } top)
            {
                cp += AtLevels(top, p.Piece.Enchant, p.Piece.Exceed, totals, model) - CpCalc.ItemCp(data, p.Piece.Enchant, p.Piece.Exceed, totals, model);
            }
        }

        return new CpRoadTask(id, group, of.Count >= slots && left == 0, left, itemLevel, cp, Math.Max(0, slots - of.Count));
    }
}
