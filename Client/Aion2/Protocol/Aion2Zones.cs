using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>What a zone id of the game's zone frame (0x2136) is: an open map or an instance, and its name as the game client shows it
/// (assets/aion2/zones/zones.json). Ids the file does not list are told apart by their range alone (6xxxxx are instances) and have no name.</summary>
public static class Aion2Zones
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "zones", "zones.json");
    private static Dictionary<int, (bool Instance, Dictionary<string, string> Names)>? _zones;

    /// <summary>Whether the zone is an instance (a dungeon you enter and leave) and not an open map.</summary>
    public static bool IsInstance(int zoneId) =>
        Table().TryGetValue(zoneId, out var zone) ? zone.Instance : zoneId is >= 600000 and < 700000;

    /// <summary>The 10 against 10 arena (zone 62, "Odyle Shard Cradle", seen on 2026-10-22). The game evens everybody out there: the combat power is the
    /// same fixed number for all (397228) and every skill and stigma is at level 20, so what a client reads of a player there is not his real state.
    /// Only this zone is verified.</summary>
    public static bool IsEqualizedArena(int zoneId) => zoneId == 62;

    /// <summary>The arenas (60 Fire Temple Arena, 61 Impetusium Arena, 62 the 10 against 10 arena): the players met there are uploaded once the fight is over.</summary>
    public static bool IsArena(int zoneId) => zoneId is 60 or 61 or 62;

    /// <summary>
    /// A dungeon or raid (an instance that is not an arena): no place for the PvP overlays (the enemy card, the Abyss radar, the death recap; 11 November
    /// 2026, the owner: "these should not open in raids" - the radar came up in a raid).
    /// </summary>
    public static bool IsPveInstance(int zoneId) => zoneId > 0 && IsInstance(zoneId) && !IsArena(zoneId);

    /// <summary>
    /// No place for the PvP overlays: a dungeon or raid by the zone frame, or by the monsters around when the zone frame says nothing of one. That frame
    /// can be missed (11 November 2026, the owner: the radar came up in Exploration, Conquest and Ludra): the meter started inside (the zone stays
    /// unknown, 0; a Fire Temple run after an update had the radar up at its boss), or the frame was lost while the game moved to the instance's server
    /// (the zone of the open map stays). A known open map or an unknown zone counts as a dungeon when <see cref="MobsSayInstance"/>. An arena never.
    /// </summary>
    public static bool IsPveHere(int zoneId, Func<IReadOnlyList<(int NpcId, float X, float Y)>> mobs)
    {
        if (IsPveInstance(zoneId))
        {
            return true;
        }

        if (IsArena(zoneId) || (zoneId > 0 && IsInstance(zoneId)))
        {
            return false;
        }

        // the monsters' answer is kept a moment for the same monsters: three overlays ask several times a second
        IReadOnlyList<(int NpcId, float X, float Y)> around = mobs();
        long seen = around.Count * 1_000_003L + around.Sum(m => (long)m.NpcId);
        lock (Gate)
        {
            if (_mobsAnswer is { } kept && kept.Zone == zoneId && kept.Seen == seen && DateTime.UtcNow - kept.At < TimeSpan.FromSeconds(2))
            {
                return kept.Instance;
            }
        }

        bool instance = MobsSayInstance(around);
        lock (Gate)
        {
            _mobsAnswer = (zoneId, seen, DateTime.UtcNow, instance);
        }

        return instance;
    }

    private static readonly object Gate = new();
    private static (int Zone, long Seen, DateTime At, bool Instance)? _mobsAnswer;

    /// <summary>
    /// The monsters around tell a dungeon or raid: a boss of one is among them (the boss table's place for it is no open map, the Abyss or Impetusium,
    /// where players fight each other), or at least three of them stand at spawn points of their kind in an instance's map and none at one of an open map.
    /// </summary>
    public static bool MobsSayInstance(IReadOnlyList<(int NpcId, float X, float Y)> mobs)
    {
        if (mobs.Count == 0)
        {
            return false;
        }

        if (mobs.Any(m => IsInstanceBoss(m.NpcId)))
        {
            return true;
        }

        return Aion2Maps.InstanceVotes(mobs) >= 3 && Aion2Maps.Votes(mobs).Count == 0;
    }

    /// <summary>A boss of a dungeon or raid (Conquest included): its place in the boss table is no open map, not the Abyss and not Impetusium.</summary>
    public static bool IsInstanceBoss(int npcId)
    {
        if (Aion2BossCatalog.Find(npcId) is not { Instance: { Length: > 0 } place })
        {
            return false;
        }

        string name = place.Replace(" (Conquest)", "", StringComparison.Ordinal).Trim();
        return !OpenMapNames().Contains(name) && !name.Equals("Abyss", StringComparison.OrdinalIgnoreCase)
               && !name.Contains("Impetusium", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string>? _openNames;

    /// <summary>The English names of the open maps in the zone file (Verteron, Altgard, the Reshanta levels ...).</summary>
    private static HashSet<string> OpenMapNames() => _openNames ??= new HashSet<string>(
        Table().Values.Where(z => !z.Instance).Select(z => z.Names.GetValueOrDefault("en")).OfType<string>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The zone's name in a language, else English; null when the id is not in the file.</summary>
    public static string? NameOf(int zoneId, string language) =>
        Table().TryGetValue(zoneId, out var zone) ? zone.Names.GetValueOrDefault(language) ?? zone.Names.GetValueOrDefault("en") : null;

    private static Dictionary<int, (bool Instance, Dictionary<string, string> Names)> Table() => _zones ??= Load();

    private static Dictionary<int, (bool, Dictionary<string, string>)> Load()
    {
        var table = new Dictionary<int, (bool, Dictionary<string, string>)>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonElement zone in doc.RootElement.GetProperty("zones").EnumerateArray())
                {
                    var names = new Dictionary<string, string>();
                    foreach (JsonProperty name in zone.GetProperty("names").EnumerateObject())
                    {
                        if (name.Value.GetString() is { Length: > 0 } text)
                        {
                            names[name.Name] = text;
                        }
                    }

                    table[zone.GetProperty("id").GetInt32()] = (zone.GetProperty("kind").GetString() == "instance", names);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No zone names then: the session list shows numbers instead.
        }

        return table;
    }
}
