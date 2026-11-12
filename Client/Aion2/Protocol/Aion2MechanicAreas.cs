using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The shape of a boss skill on the floor, around the spot its windup aims at: what the replay draws. Sizes in centimetres; <see cref="HalfAngle"/> in degrees
/// around the facing. <see cref="Measured"/>: the size was measured on recorded fights (assets/aion2/bosses/mechanic_areas.json); else only the shape is known
/// (the game client's word, <see cref="Aion2SkillShapes"/>) and the size is a guess or the one the meter learns.
/// </summary>
/// <remarks>Two shapes belong to objects, not to windups: "square", a tile of a board (<see cref="SizeCm"/>: its side, square to the map's axes), and "wave", a hit that
/// spreads from the spot at <see cref="SpeedCmS"/> for as far as <see cref="RadiusCm"/>, a band <see cref="WidthCm"/> wide, ahead of the facing within
/// <see cref="HalfAngle"/> (180: all round).</remarks>
public sealed record MechanicArea(string Shape, double? RadiusCm = null, double? InnerCm = null, double? HalfAngle = null, double? LengthCm = null, double? WidthCm = null,
    double? BehindCm = null, bool OnTarget = false, int? LandMs = null, int Seen = 0, bool Measured = false, double? SizeCm = null, double? SpeedCmS = null)
{
    /// <summary>Everybody in the room.</summary>
    public bool Room => Shape == "room";
}

/// <summary>
/// An object that lies on a board with others like it and hurts whoever stands on its square (12 November 2026, the owner: "the actual bingo etc for ludra etc.
/// all visible"): Eternal Ludra's 6 x 6 board of 6 m tiles, of which 35 appear at once and light up in four rounds (the one that never appears is the safe
/// square), and the 3 x 3 board of 20 m cells, 7 burning at once. <see cref="SizeCm"/>: the square's side (the board's spacing), <see cref="Seconds"/>: how long
/// one stays, <see cref="TickMs"/>: the beat it hurts at once lit. When each tile lights up is its first cast (the replay keeps it).
/// </summary>
public sealed record MechanicFloor(int NpcId, double SizeCm, double Seconds, IReadOnlyList<int> Skills, int TickMs, int Seen);

/// <summary>An object whose hit spreads from where it stands at a steady speed (the wave from Eternal Ludra's arena edge): <see cref="SpeedCmS"/>, the band's
/// <see cref="WidthCm"/>, how long it runs, and <see cref="HalfAngle"/> around the way the object faces as it casts (90: only ahead of it, 180: all round).</summary>
public sealed record MechanicWave(int NpcId, int SkillId, double SpeedCmS, double WidthCm, double Seconds, double HalfAngle, int Seen)
{
    /// <summary>How far it runs.</summary>
    public double ReachCm => SpeedCmS * Seconds;
}

/// <summary>An object a boss leaves on the floor that hurts the players around it (a pool, an orb): how far it reaches and how long it stays.</summary>
public sealed record MechanicHazard(int NpcId, double RadiusCm, double Seconds, IReadOnlyList<int> Skills, int Seen);

/// <summary>
/// The boss mechanics measured on recorded fights (10 November 2026, the owner: "for Ludra, if we have more logs, make it more accurate with the animations,
/// where the mechanics happen"): 31 pulls of Eternal Ludra and the other bosses of Abyssal Forge: Ludra, read from the debug recordings by
/// Tools/mechanics/fit_areas.py, and the three pulls of 12 November 2026 with the first kill (the owner: "Ludra defeated, go do it better with the actual log").
/// For a skill: its shape and size around the spot its windup aims at and the time from the windup's end to the hit; for the
/// objects a boss leaves on the floor: how far they reach and how long they stay. Most of Ludra's dangerous attacks are dealt by such objects, not by the boss
/// (the Airborne zones, the pools): the replay follows them and draws their reach.
/// </summary>
public static class Aion2MechanicAreas
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "bosses", "mechanic_areas.json");
    private static Table? _table;

    private sealed record Table(Dictionary<int, MechanicArea> Areas, Dictionary<int, MechanicHazard> Hazards, HashSet<int> Objects, Dictionary<int, MechanicFloor> Floors,
        Dictionary<int, MechanicWave> Waves);

    /// <summary>The size a shape gets while nothing measured it (a dashed outline says it is a guess).</summary>
    public const double GuessRadiusCm = 450, GuessHalfAngle = 45, GuessWidthCm = 250;

    /// <summary>What is known of a skill's area: the measured one, else the client's shape word (null: a plain circle of unknown size).</summary>
    public static MechanicArea? AreaOf(int skillId)
    {
        if (Load().Areas.TryGetValue(skillId, out MechanicArea? measured) && measured.Shape.Length > 0)
        {
            return measured;
        }

        int? land = measured?.LandMs;
        int seen = measured?.Seen ?? 0;
        return Aion2SkillShapes.KindOf(skillId) switch
        {
            "circle" => new MechanicArea("circle", LandMs: land, Seen: seen),
            "target" => new MechanicArea("circle", OnTarget: true, LandMs: land, Seen: seen),
            "cone" or "triangle" => new MechanicArea("cone", LandMs: land, Seen: seen),
            "line" => new MechanicArea("line", LandMs: land, Seen: seen),
            "cross" => new MechanicArea("cross", LandMs: land, Seen: seen),
            "ring" or "ringarc" => new MechanicArea("ring", LandMs: land, Seen: seen),
            "wide" => new MechanicArea("room", LandMs: land, Seen: seen),
            _ => land is null ? null : new MechanicArea("", LandMs: land, Seen: seen),
        };
    }

    /// <summary>The time from the end of a skill's windup to its hit, as recorded fights measured it (null: not measured, or it lands as the windup ends, which the
    /// cast bar shows anyway: under 150 ms, as the meter's own lessons).</summary>
    public static int? LandMsOf(int skillId) => Load().Areas.TryGetValue(skillId, out MechanicArea? a) && a.LandMs >= 150 ? a.LandMs : null;

    /// <summary>Whether recorded fights saw the skill hit an area (more than the one it aims at): a shape of its own.</summary>
    public static bool IsArea(int skillId) => Load().Areas.TryGetValue(skillId, out MechanicArea? a) && a.Shape.Length > 0;

    /// <summary>The object of a boss's mechanics with this NPC id, when it hurts the players around it.</summary>
    public static MechanicHazard? HazardOf(int npcId) => Load().Hazards.GetValueOrDefault(npcId);

    /// <summary>Whether an NPC is an object of a boss's mechanics (summoned on the floor; the replay follows it).</summary>
    public static bool IsObject(int npcId) => Load().Objects.Contains(npcId);

    /// <summary>The tile of a board an NPC is (null: none).</summary>
    public static MechanicFloor? FloorOf(int npcId) => Load().Floors.GetValueOrDefault(npcId);

    /// <summary>The wave an NPC sends out (null: none).</summary>
    public static MechanicWave? WaveOf(int npcId) => Load().Waves.GetValueOrDefault(npcId);

    /// <summary>Whether the casts of an NPC matter to the replay: when a tile lights up, where and which way a wave sets out.</summary>
    public static bool WatchesCasts(int npcId) => Load().Floors.ContainsKey(npcId) || Load().Waves.ContainsKey(npcId);

    private static Table Load()
    {
        if (_table is { } cached)
        {
            return cached;
        }

        var areas = new Dictionary<int, MechanicArea>();
        var hazards = new Dictionary<int, MechanicHazard>();
        var objects = new HashSet<int>();
        var floors = new Dictionary<int, MechanicFloor>();
        var waves = new Dictionary<int, MechanicWave>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("areas", out JsonElement list) && list.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in list.EnumerateObject())
                    {
                        if (!int.TryParse(p.Name, out int id) || p.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        JsonElement v = p.Value;
                        double? Num(string name) => v.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
                        string shape = v.TryGetProperty("shape", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                        areas[id] = new MechanicArea(shape, Num("radius"), Num("inner"), Num("half"), Num("length"), Num("width"), Num("behind"),
                            v.TryGetProperty("onTarget", out JsonElement t) && t.ValueKind == JsonValueKind.True, (int?)Num("landMs"), (int)(Num("seen") ?? 0), shape.Length > 0);
                    }
                }

                if (root.TryGetProperty("hazards", out JsonElement found) && found.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in found.EnumerateObject())
                    {
                        if (int.TryParse(p.Name, out int npc) && p.Value.TryGetProperty("radius", out JsonElement r) && p.Value.TryGetProperty("seconds", out JsonElement sec))
                        {
                            var skills = p.Value.TryGetProperty("skills", out JsonElement sk) && sk.ValueKind == JsonValueKind.Array
                                ? sk.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToList() : new List<int>();
                            hazards[npc] = new MechanicHazard(npc, r.GetDouble(), sec.GetDouble(), skills,
                                p.Value.TryGetProperty("seen", out JsonElement n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0);
                        }
                    }
                }

                if (root.TryGetProperty("objects", out JsonElement objs) && objs.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement o in objs.EnumerateArray())
                    {
                        if (o.ValueKind == JsonValueKind.Number)
                        {
                            objects.Add(o.GetInt32());
                        }
                    }
                }

                static double? NumOf(JsonElement v, string name) => v.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
                static List<int> IdsOf(JsonElement v, string name) => v.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Array
                    ? e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList() : new List<int>();
                if (root.TryGetProperty("floors", out JsonElement boards) && boards.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in boards.EnumerateObject())
                    {
                        if (int.TryParse(p.Name, out int npc) && p.Value.ValueKind == JsonValueKind.Object && NumOf(p.Value, "size") is double size && size > 0
                            && NumOf(p.Value, "seconds") is double seconds && seconds > 0)
                        {
                            floors[npc] = new MechanicFloor(npc, size, seconds, IdsOf(p.Value, "skills"), (int)(NumOf(p.Value, "tickMs") ?? 500), (int)(NumOf(p.Value, "seen") ?? 0));
                        }
                    }
                }

                if (root.TryGetProperty("waves", out JsonElement fronts) && fronts.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in fronts.EnumerateObject())
                    {
                        if (int.TryParse(p.Name, out int npc) && p.Value.ValueKind == JsonValueKind.Object && NumOf(p.Value, "skill") is double skill && NumOf(p.Value, "speed") is double speed
                            && speed > 0 && NumOf(p.Value, "seconds") is double seconds && seconds > 0)
                        {
                            waves[npc] = new MechanicWave(npc, (int)skill, speed, NumOf(p.Value, "width") ?? 300, seconds, NumOf(p.Value, "half") ?? 180, (int)(NumOf(p.Value, "seen") ?? 0));
                        }
                    }
                }

                objects.UnionWith(hazards.Keys);
                objects.UnionWith(floors.Keys);
                objects.UnionWith(waves.Keys);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException)
        {
            // No table: the replay draws the client's shapes with the learnt sizes, as before.
        }

        _table = new Table(areas, hazards, objects, floors, waves);
        return _table;
    }
}
