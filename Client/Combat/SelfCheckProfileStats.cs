using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>
/// The combat stats of the player window (9 November 2026, the owner: "if someone is using the app then give exact numbers, and if someone isn't don't give
/// fake numbers"): the stat table follows the game's whole lists (a buff's stat that ended goes, a transformation's list is not the character's own), only the
/// stats of the character in the file go with it to the website, and the window shows rows only when the website says they are the game's own numbers.
/// </summary>
public static class SelfCheckProfileStats
{
    // the ids the character window is made of, as a recorded stat frame of 9 November 2026 had them (the window showed Attack 3,254, Defense 17,314, HP 31,402)
    private static Dictionary<int, int> Recorded() => new()
    {
        [1] = 54, [2] = 48, [4] = 20, [5] = 42, [6] = 14, [7] = 51, [19] = 344, [31] = 782, [33] = 625, [317] = 1805, [425] = 1411, [52] = 2164, [307] = 10195, [426] = 4010,
        [104] = 418, [318] = 355, [427] = 780, [116] = 95, [312] = 12, [428] = 840, [128] = 1032, [429] = 1080, [133] = 217, [430] = 480, [193] = 20862, [196] = 5052,
        [199] = 3255, [197] = 310, [442] = 1507, [159] = 3240, [222] = 2000,
    };

    public static bool Run()
    {
        Console.WriteLine("[selftest] Combat stats for the player window:");
        bool ok = true;

        // the stat table: a whole list replaces it, a list without the attributes (a transformation) is not the character's own
        var directory = new Aion2EntityDirectory();
        bool before = directory.OwnStats is null;
        directory.SetLocalStats(Recorded());
        var buffed = Recorded();
        buffed.Remove(222); // the buff ended: the next list hasn't its stat
        buffed[193] = 20900;
        directory.SetLocalStats(buffed);
        bool replaced = directory.OwnStats is { } own && !own.Values.ContainsKey(222) && own.Values[193] == 20900;
        directory.SetLocalStats(new Dictionary<int, int> { [317] = 3150, [193] = 28050, [307] = 15000 });
        bool transformed = directory.OwnStats is null && directory.LocalStats is { } merged && merged[193] == 28050;
        directory.SetLocalStats(Recorded());
        bool back = directory.OwnStats is { } again && again.Values[193] == 20862 && again.Values[317] == 1805;
        var noIntelligence = Recorded(); // an attribute at 0 is not sent
        noIntelligence.Remove(3);
        directory.SetLocalStats(noIntelligence);
        bool zeroLeftOut = directory.OwnStats is not null;
        bool table = before && replaced && transformed && back && zeroLeftOut;
        Console.WriteLine($"  -> a whole list replaces the table (an ended buff's stat goes), a transformation's list is not the character's own until the next whole one: {table}");
        ok &= table;

        // what goes to the website: the window's ids and the known ones, in order, with the time; nothing without HP or attack
        var saved = new Aion2SavedCharacter { Name = "Ashveil", ServerId = 2001, ClassCode = 6, Level = 45, SavedAt = DateTime.UtcNow };
        DateTime at = new(2026, 11, 9, 19, 25, 47, DateTimeKind.Utc);
        ProfileCharacter? withStats = ProfileShare.Build(saved, (Recorded(), at));
        bool sent = withStats is { Stats: { } lines } && lines.All(l => l.Length == 2 && ProfileShare.StatIds.Contains(l[0])) && lines.Any(l => l[0] == 193 && l[1] == 20862)
            && !lines.Any(l => l[0] is 159 or 222 or 7) && lines.Select(l => l[0]).SequenceEqual(lines.Select(l => l[0]).OrderBy(i => i))
            && withStats.StatsAt == new DateTimeOffset(at).ToUnixTimeMilliseconds();
        bool alone = ProfileShare.Build(saved) is null && ProfileShare.StatLines(new Dictionary<int, int> { [1] = 50, [317] = 900 }) is null;
        string json = JsonSerializer.Serialize(withStats, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        bool shape = json.Contains("\"stats\":[[1,54],", StringComparison.Ordinal) && json.Contains("\"statsAt\":", StringComparison.Ordinal);
        Console.WriteLine($"  -> the window's stat ids go, in order, with when the game sent them; nothing else of the list, nothing without HP and attack: {sent && alone && shape}");
        ok &= sent && alone && shape;

        // the window: rows only when the website says they are the game's own numbers (a website still summing the page gives none)
        var who = new FoundPlayer("Ashveil", "Spiritmaster", 45, "Vaizel", 2001, "eu", null, null, null, null, null, null, null, null);
        PlayerPage Read(string sheet) => PlayerClient.Read(JsonDocument.Parse("{\"ok\":true,\"character\":{\"name\":\"Ashveil\"},\"sheet\":" + sheet + "}").RootElement, who);
        PlayerPage exact = Read("{\"exact\":true,\"at\":1794252347445,\"offense\":[{\"name\":\"Attack\",\"value\":\"3,254\"}],\"defense\":[{\"name\":\"HP\",\"value\":\"31,402\"}],\"other\":[],\"from\":[],\"missing\":[],\"effects\":{}}");
        PlayerPage summed = Read("{\"offense\":[{\"name\":\"Attack\",\"value\":\"2,833\"}],\"defense\":[],\"other\":[],\"from\":[\"gear\"],\"missing\":[],\"effects\":{\"Might\":[\"Attack Increase +5.4%\"]}}");
        bool window = exact.Sheet is { Exact: true, At: not null } e && e.Offense.Single().Value == "3,254" && e.Defense.Single().Name == "HP"
            && summed.Sheet is { Exact: false, Offense.Count: 0, Defense.Count: 0 } s && s.Effects["Might"].Count == 1;
        Console.WriteLine($"  -> the window shows the game's numbers with their time, and none that weren't sent by the character's own app: {window}");
        ok &= window;
        return ok;
    }
}
