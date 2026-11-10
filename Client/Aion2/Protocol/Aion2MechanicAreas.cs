using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The shape of a boss skill on the floor, around the spot its windup aims at: what the replay draws. Sizes in centimetres; <see cref="HalfAngle"/> in degrees
/// around the facing. <see cref="Measured"/>: the size was measured on recorded fights (assets/aion2/bosses/mechanic_areas.json); else only the shape is known
/// (the game client's word, <see cref="Aion2SkillShapes"/>) and the size is a guess or the one the meter learns.
/// </summary>
public sealed record MechanicArea(string Shape, double? RadiusCm = null, double? InnerCm = null, double? HalfAngle = null, double? LengthCm = null, double? WidthCm = null,
    double? BehindCm = null, bool OnTarget = false, int? LandMs = null, int Seen = 0, bool Measured = false)
{
    /// <summary>Everybody in the room.</summary>
    public bool Room => Shape == "room";
}

/// <summary>An object a boss leaves on the floor that hurts the players around it (a pool, an orb): how far it reaches and how long it stays.</summary>
public sealed record MechanicHazard(int NpcId, double RadiusCm, double Seconds, IReadOnlyList<int> Skills, int Seen);

/// <summary>
/// The boss mechanics measured on recorded fights (10 November 2026, the owner: "for Ludra, if we have more logs, make it more accurate with the animations,
/// where the mechanics happen"): 31 pulls of Eternal Ludra and the other bosses of Abyssal Forge: Ludra, read from the debug recordings by
/// Tools/mechanics/fit_areas.py. For a skill: its shape and size around the spot its windup aims at and the time from the windup's end to the hit; for the
/// objects a boss leaves on the floor: how far they reach and how long they stay. Most of Ludra's dangerous attacks are dealt by such objects, not by the boss
/// (the Airborne zones, the pools): the replay follows them and draws their reach.
/// </summary>
public static class Aion2MechanicAreas
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "bosses", "mechanic_areas.json");
    private static (Dictionary<int, MechanicArea> Areas, Dictionary<int, MechanicHazard> Hazards, HashSet<int> Objects)? _table;

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

    private static (Dictionary<int, MechanicArea> Areas, Dictionary<int, MechanicHazard> Hazards, HashSet<int> Objects) Load()
    {
        if (_table is { } cached)
        {
            return cached;
        }

        var areas = new Dictionary<int, MechanicArea>();
        var hazards = new Dictionary<int, MechanicHazard>();
        var objects = new HashSet<int>();
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

                objects.UnionWith(hazards.Keys);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException)
        {
            // No table: the replay draws the client's shapes with the learnt sizes, as before.
        }

        _table = (areas, hazards, objects);
        return _table.Value;
    }
}
