using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>A grind session's numbers at one moment: what came in, the time farmed, and each per hour (null until a minute was farmed).</summary>
public sealed record GrindFigures(GrindSession Session, double Farmed, double? KinahPerHour, double? ApPerHour, double? KillsPerHour, long? LootWorth, double? LootPerHour,
    long Items, bool Over)
{
    /// <summary>The figures of <paramref name="s"/> at <paramref name="now"/>, the loot priced at <paramref name="book"/> (the market of the character's server).</summary>
    public static GrindFigures Of(GrindSession s, DateTime now, MarketBook? book)
    {
        bool over = Aion2Grind.IsOver(s, now);
        double farmed = over ? s.ActiveSeconds : Aion2Grind.ActiveSecondsAt(s, now);
        long? worth = null;
        if (book is not null && s.Items.Count > 0)
        {
            worth = s.Items.Sum(kv => (CraftWorkshop.PriceOf(book, kv.Key) ?? 0) * kv.Value);
        }

        return new GrindFigures(s, farmed, s.PerHour(s.Kinah, farmed), s.PerHour(s.AbyssPoints, farmed), s.PerHour(s.Kills, farmed), worth,
            worth is long w ? s.PerHour(w, farmed) : null, s.Items.Values.Sum(), over);
    }

    /// <summary>"14:32" or "1:14:32".</summary>
    public string FarmedText => Clock(Farmed);

    public static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }
}

/// <summary>A made-up grind for the pictures: 34 minutes farmed in Chaotic Middle Reshanta, two earlier sessions, a market for the loot.</summary>
internal static class GrindSample
{
    public static GrindSession Current(DateTime now) => new()
    {
        Id = "picture", Character = "Lumiel", Start = now.AddMinutes(-41), End = now.AddSeconds(-12), ActiveSeconds = 34 * 60 + 8, Kinah = 712_400, AbyssPoints = 18_640,
        Kills = 214, ZoneKills = new() { [22] = 214 },
        Items = new() { [511351001] = 14, [511351002] = 5, [511360003] = 9, [510116011] = 22, [511900001] = 2, [610620013] = 36 },
    };

    public static IReadOnlyList<GrindSession> Earlier(DateTime now) => new[]
    {
        new GrindSession
        {
            Id = "a", Character = "Lumiel", Start = now.AddDays(-1).AddHours(-2), End = now.AddDays(-1).AddHours(-1).AddMinutes(-8), ActiveSeconds = 47 * 60, Kinah = 905_200,
            AbyssPoints = 24_100, Kills = 301, ZoneKills = new() { [22] = 301 }, Items = new() { [511351001] = 19, [510116011] = 30 },
        },
        new GrindSession
        {
            Id = "b", Character = "Arkhel", Start = now.AddDays(-2).AddHours(-5), End = now.AddDays(-2).AddHours(-4), ActiveSeconds = 58 * 60, Kinah = 1_480_000, Kills = 466,
            ZoneKills = new() { [103] = 466 }, Items = new() { [511360003] = 31, [610620013] = 120 },
        },
    };

    /// <summary>Four monsters of two maps with their drops and the Kinah after their kills.</summary>
    public static IReadOnlyList<LootMob> Mobs() => new[]
    {
        new LootMob { NpcId = 2090168, Kills = 120, Zones = new() { 22 }, Items = new() { new LootMobItem { ItemId = 511351001, Count = 30, Kills = 28 }, new LootMobItem { ItemId = 511900001, Count = 3, Kills = 3 } } },
        new LootMob { NpcId = 2000053, Kills = 94, Zones = new() { 22 }, Items = new() { new LootMobItem { ItemId = 510116011, Count = 40, Kills = 31 }, new LootMobItem { ItemId = 511351002, Count = 6, Kills = 6 } } },
        new LootMob { NpcId = 2100217, Kills = 260, Zones = new() { 103 }, Items = new() { new LootMobItem { ItemId = 610620013, Count = 210, Kills = 150 }, new LootMobItem { ItemId = 511360003, Count = 22, Kills = 22 } } },
        new LootMob { NpcId = 2100066, Kills = 310, Zones = new() { 103 }, Items = new() { new LootMobItem { ItemId = 610620013, Count = 95, Kills = 80 } } },
    };

    public static IReadOnlyList<MobYield> Yields() => new[]
    {
        new MobYield { NpcId = 2090168, Kills = 120, Kinah = 410_000, AbyssPoints = 11_400 },
        new MobYield { NpcId = 2000053, Kills = 94, Kinah = 352_000, AbyssPoints = 7_900 },
        new MobYield { NpcId = 2100217, Kills = 260, Kinah = 1_010_000 },
        new MobYield { NpcId = 2100066, Kills = 310, Kinah = 690_000 },
    };

    public static MarketBook Book()
    {
        var prices = new Dictionary<int, MarketPrice>();
        foreach ((int item, long price) in new (int, long)[] { (511351001, 4_200), (511351002, 18_500), (511360003, 6_100), (510116011, 1_350), (511900001, 96_000), (610620013, 640) })
        {
            prices[item] = new MarketPrice(price, price, 8, price);
        }

        return new MarketBook(1301, "Picture", DateTime.UtcNow.AddMinutes(-4), prices);
    }
}
