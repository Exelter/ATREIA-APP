using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

public enum ReplayRole
{
    Player,
    Boss,

    /// <summary>Another monster the group fought (an add): a small marker that moves, without areas or health.</summary>
    Add,

    /// <summary>An object a boss leaves on the floor (a pool, an orb, the Airborne zones of Eternal Ludra): a small mark, with the reach it hurts in while it stays
    /// (<see cref="AionDPS.Aion2.Protocol.Aion2MechanicAreas"/>).</summary>
    Mechanic,
}

/// <summary>Someone shown on the replay map, with where they were. <see cref="NpcId"/>: a monster's or object's NPC id, when known.</summary>
public sealed record ReplayEntity(int Id, string Name, string ClassName, ReplayRole Role, PositionTrack Track, bool IsSelf = false, double? DiedAt = null, int? NpcId = null)
{
    /// <summary>A monster with a single point (where it appeared) that never moved: it stands there until it dies.</summary>
    public bool Static => Track.T.Length == 1;

    /// <summary>
    /// The own character's place is only reported now and then (with skills and corrections; the server sends no running position of one's own character), so between two
    /// readings the replay draws a straight walk: an estimate. True when no reading lies within <see cref="ExactSeconds"/> of this moment; false for everybody else, whose
    /// movement frames are readings.
    /// </summary>
    public bool EstimatedAt(DateTime fightStart, double seconds)
    {
        if (!IsSelf || Track.T.Length == 0)
        {
            return false;
        }

        double t = seconds * 1000 - (Track.Start - fightStart).TotalMilliseconds;
        int hi = Array.BinarySearch(Track.T, (int)t);
        if (hi >= 0)
        {
            return false;
        }

        hi = ~hi;
        double before = hi > 0 ? t - Track.T[hi - 1] : double.PositiveInfinity;
        double after = hi < Track.T.Length ? Track.T[hi] - t : double.PositiveInfinity;
        return Math.Min(before, after) > ExactSeconds * 1000;
    }

    /// <summary>A reading this close in time still counts as where the character was.</summary>
    public const double ExactSeconds = 1.5;

    /// <summary>Where the entity was at <paramref name="seconds"/> from the start of the fight, between its two readings around it; null before its first reading, and after its last
    /// one for longer than <see cref="HoldSeconds"/>.</summary>
    public (double X, double Y)? At(DateTime fightStart, double seconds)
    {
        double ms = (Track.Start - fightStart).TotalMilliseconds + 0; // the track's origin relative to the fight start
        double t = seconds * 1000 - ms;
        if (DiedAt is double died && seconds >= died)
        {
            return null;
        }

        var times = Track.T;
        if (Static)
        {
            return (Track.X[0], Track.Y[0]);
        }

        if (times.Length == 0 || t < -HoldSeconds * 1000)
        {
            return null;
        }

        if (t <= times[0])
        {
            return (Track.X[0], Track.Y[0]);
        }

        int hi = Array.BinarySearch(times, (int)t);
        if (hi >= 0)
        {
            return (Track.X[hi], Track.Y[hi]);
        }

        hi = ~hi; // the first reading after t
        if (hi >= times.Length)
        {
            return t - times[^1] <= HoldSeconds * 1000 ? (Track.X[^1], Track.Y[^1]) : null;
        }

        int lo = hi - 1;
        double span = times[hi] - times[lo];
        if (span > MaxGapSeconds * 1000)
        {
            return t - times[lo] <= span / 2 ? (Track.X[lo], Track.Y[lo]) : (Track.X[hi], Track.Y[hi]); // too long a gap to draw a straight walk: it stays and jumps
        }

        double f = (t - times[lo]) / span;
        return (Track.X[lo] + (Track.X[hi] - Track.X[lo]) * f, Track.Y[lo] + (Track.Y[hi] - Track.Y[lo]) * f);
    }

    /// <summary>An entity not heard from for this long is no longer drawn.</summary>
    public const double HoldSeconds = 30;

    /// <summary>Between two readings further apart than this the entity is taken to have stayed, then jumped.</summary>
    public const double MaxGapSeconds = 8;
}

/// <summary>A skill a boss (or an object of its mechanics) winds up: its area around the spot the windup aims at, for as long as the windup lasts (<see cref="Start"/> to
/// <see cref="End"/>, seconds from the start of the fight). <see cref="Shape"/>: its shape on the floor (null: a circle); <see cref="Facing"/> the way it points (degrees, atan2 of y and x).
/// <see cref="Zone"/>: not a windup but the reach of an object on the floor while it stays (it follows <see cref="Actor"/>). A tile of a board (shape "square") is a zone
/// from when it appeared; <see cref="LitAt"/> is when it lit up and began to hurt (null: not seen, before the app kept it). A wave (shape "wave") runs from
/// <see cref="Start"/> to <see cref="End"/>, spreading from the spot.</summary>
public sealed record ReplayArea(double Start, double End, int Actor, int SkillId, string SkillName, int Target, double Facing, double X, double Y, double? RadiusCm,
    AionDPS.Aion2.Protocol.MechanicArea? Shape = null, bool Zone = false, double? LitAt = null)
{
    /// <summary>The size of an area nobody has measured yet (drawn dashed).</summary>
    public const double DefaultRadiusCm = 450;

    public double Radius => RadiusCm ?? Shape?.RadiusCm ?? DefaultRadiusCm;

    /// <summary>Its size rests on measurements (recorded fights or the hits the meter saw), not on a guess.</summary>
    public bool Sized => RadiusCm is not null || Shape is { Measured: true };
}

/// <summary>A boss's hit on a group member.</summary>
public sealed record ReplayHit(double At, int Attacker, int Victim, int SkillId);

/// <summary>A death on the replay's clock; Until is the revive (null: not revived within the fight).</summary>
public sealed record ReplayDeath(double At, int Entity, double? Until = null);

/// <summary>A boss's health over the fight: <see cref="Seconds"/> and the matching share (0-1).</summary>
public sealed record ReplayHp(int Entity, double[] Seconds, double[] Fraction);

/// <summary>What the server needs to find the paths the other clients of this fight sent: the server, the boss's NPC id and the start (UTC).</summary>
public sealed record ReplayLookup(string ServerFingerprint, int BossNpcId, DateTime StartedAtUtc);

/// <summary>Everything the replay map shows of one fight.</summary>
public sealed record ReplayData(
    string Title,
    DateTime Start,
    double DurationSeconds,
    IReadOnlyList<ReplayEntity> Entities,
    IReadOnlyList<ReplayArea> Areas,
    IReadOnlyList<ReplayHit> Hits,
    IReadOnlyList<ReplayDeath> Deaths,
    IReadOnlyList<StaggerPhase> Staggers,
    IReadOnlyList<ReplayHp> Hp,
    ReplayLookup? Lookup = null)
{
    public bool HasData => Entities.Count(e => e.Role == ReplayRole.Player) > 0 && Entities.Any(e => e.Track.T.Length >= 2) && DurationSeconds > 1;
}

/// <summary>Works the replay out of a fight's <see cref="FightExtras"/>: who moved where, which boss skills were wound up and what they hit. Pure, so a test can run it.</summary>
public static class ReplayBuilder
{
    /// <param name="npcOf">The NPC id of an entity (null: unknown): an object of a boss's mechanics (<see cref="Aion2MechanicAreas.IsObject"/>) is followed with its reach.</param>
    public static ReplayData Build(string title, FightExtras? extras, IReadOnlyList<DamageEvent> fightEvents, DateTime start, DateTime end,
        Func<int, (string Name, string ClassName, bool IsPlayer)> describe, Func<int, double?> radiusOf, Func<int, string> skillName, Func<int, bool>? isAdd = null, Func<int, bool>? isSelf = null,
        Func<int, int?>? npcOf = null)
    {
        double duration = Math.Max(1, (end - start).TotalSeconds);
        double Secs(DateTime at) => (at - start).TotalSeconds;
        var empty = new ReplayData(title, start, duration, Array.Empty<ReplayEntity>(), Array.Empty<ReplayArea>(), Array.Empty<ReplayHit>(), Array.Empty<ReplayDeath>(),
            Array.Empty<StaggerPhase>(), Array.Empty<ReplayHp>());
        if (extras?.Tracks is not { Count: > 0 } tracks)
        {
            return empty;
        }

        // the NPC id of a monster or an object: the live capture's word, else the one kept with the fight (a stored fight's capture may be gone)
        IReadOnlyDictionary<int, int>? keptNpcs = extras.Npcs;
        int? NpcOf(int id) => npcOf?.Invoke(id) is int live ? live : keptNpcs is { } kept && kept.TryGetValue(id, out int stored) ? stored : null;
        var entities = new List<ReplayEntity>();
        foreach (PositionTrack track in tracks.Where(t => t.T.Length >= 1))
        {
            var (name, cls, isPlayer) = describe(track.Entity);
            int? npcId = isPlayer ? null : NpcOf(track.Entity);
            bool mechanic = !isPlayer && npcId is int npc && Aion2MechanicAreas.IsObject(npc);
            bool add = !isPlayer && !mechanic && isAdd?.Invoke(track.Entity) == true;
            // a walk is asked of everybody but an add or an object on the floor (it may stand where it appeared)
            if (track.T.Length < 2 && !add && !mechanic)
            {
                continue;
            }

            // a monster the group killed is gone from the map from that moment
            double? died = (add || mechanic) && (extras.Deaths ?? Array.Empty<DeathRecord>()).FirstOrDefault(d => d.Entity == track.Entity && d.At >= start) is { Entity: > 0 } death ? Secs(death.At) : null;
            ReplayRole role = isPlayer ? ReplayRole.Player : mechanic ? ReplayRole.Mechanic : add ? ReplayRole.Add : ReplayRole.Boss;
            entities.Add(new ReplayEntity(track.Entity, name, cls, role, track, isPlayer && isSelf?.Invoke(track.Entity) == true, died, npcId));
        }

        var bossIds = entities.Where(e => e.Role == ReplayRole.Boss).Select(e => e.Id).ToHashSet();
        var playerIds = entities.Where(e => e.Role == ReplayRole.Player).Select(e => e.Id).ToHashSet();
        // the areas of the bosses and of the objects their mechanics leave on the floor (on Eternal Ludra most dangerous attacks are theirs)
        var casters = entities.Where(e => e.Role is ReplayRole.Boss or ReplayRole.Mechanic).Select(e => e.Id).ToHashSet();
        var npcOfEntity = entities.Where(e => e.NpcId is not null).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().NpcId!.Value);
        // a wave's own windup is no area of its own: the wave drawn from its cast is what it does
        bool WaveWindup(WindupRecord w) => npcOfEntity.TryGetValue(w.Actor, out int npc) && Aion2MechanicAreas.WaveOf(npc) is { } wave && wave.SkillId == w.SkillId;
        var areas = (extras.Windups ?? Array.Empty<WindupRecord>())
            .Where(w => w.HasPosition && casters.Contains(w.Actor) && w.At >= start - TimeSpan.FromSeconds(2) && w.At <= end && !WaveWindup(w))
            .Select(w => AreaOf(w, Secs(w.At), skillName, radiusOf))
            .ToList();
        var casts = extras.ObjectCasts ?? Array.Empty<ObjectCast>();
        foreach (ReplayEntity e in entities.Where(e => e.Role == ReplayRole.Mechanic))
        {
            if (e.NpcId is int tileNpc && Aion2MechanicAreas.FloorOf(tileNpc) is { } floor && e.Track.T.Length > 0)
            {
                // a tile of a board (Eternal Ludra's "bingo"): its square from when it appeared, lit from its first cast, for as long as such tiles stay
                double from = Secs(e.Track.Start);
                double until = Math.Min(from + floor.Seconds, e.DiedAt ?? double.MaxValue);
                if (until > 0 && from < duration)
                {
                    ObjectCast? lit = casts.Where(c => c.Actor == e.Id && floor.Skills.Contains(c.SkillId) && Secs(c.At) >= from - 0.5 && Secs(c.At) <= until)
                        .OrderBy(c => c.At).Cast<ObjectCast?>().FirstOrDefault();
                    int skill = lit?.SkillId ?? floor.Skills.FirstOrDefault();
                    areas.Add(new ReplayArea(from, until, e.Id, skill, skillName(skill), 0, 0, e.Track.X[0], e.Track.Y[0], floor.SizeCm / 2,
                        new MechanicArea("square", floor.SizeCm / 2, Seen: floor.Seen, Measured: true, SizeCm: floor.SizeCm), Zone: true,
                        LitAt: lit is { } c0 ? Math.Max(from, Secs(c0.At)) : null));
                }

                continue;
            }

            if (e.NpcId is int waveNpc && Aion2MechanicAreas.WaveOf(waveNpc) is { } wave)
            {
                // a wave: from each time it set out, spreading from the object's spot (or the cast's) ahead of the way it faced
                foreach (ObjectCast c in casts.Where(c => c.Actor == e.Id && c.SkillId == wave.SkillId))
                {
                    double at = Secs(c.At);
                    if (at < -wave.Seconds || at > duration)
                    {
                        continue;
                    }

                    (double X, double Y)? spot = c.HasPosition ? (c.X, c.Y) : e.At(start, at);
                    if (spot is not { } p)
                    {
                        continue;
                    }

                    areas.Add(new ReplayArea(at, at + wave.Seconds, e.Id, wave.SkillId, skillName(wave.SkillId), 0, c.Facing, p.X, p.Y, wave.ReachCm,
                        new MechanicArea("wave", wave.ReachCm, HalfAngle: wave.HalfAngle, WidthCm: wave.WidthCm, Seen: wave.Seen, Measured: true, SpeedCmS: wave.SpeedCmS)));
                }

                continue;
            }

            if (e.NpcId is int npc && Aion2MechanicAreas.HazardOf(npc) is { } hazard && e.Track.T.Length > 0)
            {
                // the reach of an object on the floor, from when it appeared for as long as such objects stay (or until it is gone)
                double from = Secs(e.Track.Start);
                double until = Math.Min(from + hazard.Seconds, e.DiedAt ?? double.MaxValue);
                if (until > 0 && from < duration)
                {
                    areas.Add(new ReplayArea(from, until, e.Id, hazard.Skills.FirstOrDefault(), skillName(hazard.Skills.FirstOrDefault()), 0, 0, e.Track.X[0], e.Track.Y[0], hazard.RadiusCm,
                        new MechanicArea("circle", hazard.RadiusCm, Seen: hazard.Seen, Measured: true), Zone: true));
                }
            }
        }

        areas = areas.OrderBy(a => a.Start).ToList();
        var hits = fightEvents.Where(e => !e.IsHeal && !e.IsTick && casters.Contains(e.SourceObjectId) && playerIds.Contains(e.TargetObjectId))
            .Select(e => new ReplayHit(Secs(e.Timestamp), e.SourceObjectId, e.TargetObjectId, e.SkillId)).OrderBy(h => h.At).ToList();
        var deaths = (extras.Deaths ?? Array.Empty<DeathRecord>()).Where(d => playerIds.Contains(d.Entity) && d.At >= start && d.At <= end)
            .Select(d => new ReplayDeath(Secs(d.At), d.Entity, d.RevivedAt is DateTime back ? Secs(back) : null)).OrderBy(d => d.At).ToList();
        var staggers = extras.Staggers.Where(s => s.Broke && s.At >= start && s.At <= end).Select(s => new StaggerPhase(Secs(s.At), Secs(s.At) + 5)).ToList();
        var hp = (extras.Hp ?? Array.Empty<HpSeries>()).Where(h => bossIds.Contains(h.Entity) && h.T.Length >= 2)
            .Select(h => new ReplayHp(h.Entity, h.T.Select(t => Secs(h.Start) + t / 1000.0).ToArray(), h.Permille.Select(p => p / 1000.0).ToArray())).ToList();
        return new ReplayData(title, start, duration, entities, areas, hits, deaths, staggers, hp);
    }

    /// <summary>A windup as the replay draws it: its shape from what recorded fights measured or the game client's word, its size measured, else learnt by the meter.</summary>
    public static ReplayArea AreaOf(WindupRecord w, double at, Func<int, string> skillName, Func<int, double?> radiusOf)
    {
        MechanicArea? shape = Aion2MechanicAreas.AreaOf(w.SkillId);
        if (shape is { Shape.Length: 0 })
        {
            shape = null; // only the time it lands is known: a circle, as before
        }

        double? radius = shape is { Measured: true, RadiusCm: double measured } ? measured : radiusOf(w.SkillId);
        return new ReplayArea(at, at + w.DurationMs / 1000.0, w.Actor, w.SkillId, skillName(w.SkillId), w.Target, w.Facing, w.X, w.Y, radius, shape);
    }
}

/// <summary>A board of a boss (Eternal Ludra's "bingo"): the tiles that appeared together on a grid, from the first of them to the last, how many, and the cells of its grid
/// left out, where nothing lights up (on the 6 x 6 board the one square to stand on). Centres in centimetres, as the tiles'.</summary>
public sealed record ReplayBoard(double Start, double End, double SizeCm, int Tiles, IReadOnlyList<(double X, double Y)> Gaps);

/// <summary>Finds the boards among a replay's areas: the squares of the same size that appeared within moments of each other.</summary>
public static class ReplayBoards
{
    /// <summary>Tiles that appeared further apart than this are on two boards.</summary>
    private const double TogetherSeconds = 1.5;

    public static IReadOnlyList<ReplayBoard> Of(IEnumerable<ReplayArea> areas)
    {
        var tiles = areas.Where(a => a.Shape is { Shape: "square", SizeCm: > 0.0 } && double.IsFinite(a.X) && double.IsFinite(a.Y)).OrderBy(a => a.Start).ToList();
        var used = new bool[tiles.Count];
        var boards = new List<ReplayBoard>();
        for (int i = 0; i < tiles.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            double size = tiles[i].Shape!.SizeCm!.Value;
            var group = new List<ReplayArea>();
            for (int j = i; j < tiles.Count && tiles[j].Start - tiles[i].Start <= TogetherSeconds; j++)
            {
                if (!used[j] && Math.Abs(tiles[j].Shape!.SizeCm!.Value - size) < 1)
                {
                    used[j] = true;
                    group.Add(tiles[j]);
                }
            }

            // the grid from its lowest corner: a cell with no tile is a gap (only on a board that looks like one: 3 x 3 cells or more, most of them filled)
            double x0 = group.Min(a => a.X), y0 = group.Min(a => a.Y);
            var cells = group.Select(a => ((int)Math.Round((a.X - x0) / size), (int)Math.Round((a.Y - y0) / size))).ToHashSet();
            int across = cells.Max(c => c.Item1) + 1, down = cells.Max(c => c.Item2) + 1;
            var gaps = new List<(double X, double Y)>();
            if (across is >= 3 and <= 12 && down is >= 3 and <= 12 && cells.Count >= 0.6 * across * down)
            {
                for (int cx = 0; cx < across; cx++)
                {
                    for (int cy = 0; cy < down; cy++)
                    {
                        if (!cells.Contains((cx, cy)))
                        {
                            gaps.Add((x0 + cx * size, y0 + cy * size));
                        }
                    }
                }
            }

            boards.Add(new ReplayBoard(group.Min(a => a.Start), group.Max(a => a.End), size, group.Count, gaps));
        }

        return boards;
    }
}

/// <summary>Puts the paths the other clients of the fight sent (merged by the server) into the replay: a path seen by both gets the readings of both, one only they saw is added.</summary>
public static class ReplayMerge
{
    /// <summary>Two readings closer than this are the same one.</summary>
    private const int SameReadingMs = 250;

    public static ReplayData Apply(ReplayData data, IReadOnlyList<AionDPS.Upload.ServerReplayTrack> others)
    {
        if (others.Count == 0)
        {
            return data;
        }

        var entities = data.Entities.ToList();
        foreach (var other in others)
        {
            ReplayRole role = other.Kind switch { "player" => ReplayRole.Player, "add" => ReplayRole.Add, _ => ReplayRole.Boss };
            int at = entities.FindIndex(e => e.Role == role && string.Equals(e.Name, other.Name, StringComparison.Ordinal));
            var points = new List<(long Ms, int X, int Y)>();
            if (at >= 0)
            {
                PositionTrack own = entities[at].Track;
                long ownStart = new DateTimeOffset(own.Start.ToUniversalTime()).ToUnixTimeMilliseconds();
                for (int i = 0; i < own.T.Length; i++)
                {
                    points.Add((ownStart + own.T[i], own.X[i], own.Y[i]));
                }
            }

            int ownCount = points.Count;
            for (int i = 0; i < other.T.Length && i < other.X.Length && i < other.Y.Length; i++)
            {
                points.Add((other.StartMs + other.T[i], other.X[i], other.Y[i]));
            }

            // in time order; of two readings within a quarter second the own one stays (it comes first in the list)
            var ordered = points.Select((p, index) => (p, index)).OrderBy(v => v.p.Ms).ThenBy(v => v.index).Select(v => v.p).ToList();
            var kept = new List<(long Ms, int X, int Y)>();
            foreach (var p in ordered)
            {
                if (kept.Count == 0 || p.Ms - kept[^1].Ms >= SameReadingMs)
                {
                    kept.Add(p);
                }
            }

            if (kept.Count == 0 || (at < 0 && role == ReplayRole.Player && kept.Count < 2) || (at >= 0 && kept.Count == ownCount))
            {
                continue;
            }

            long first = kept[0].Ms;
            DateTime origin = DateTimeOffset.FromUnixTimeMilliseconds(first).LocalDateTime;
            var track = new PositionTrack(at >= 0 ? entities[at].Id : -1_000_000 - entities.Count, origin, kept.Select(p => (int)(p.Ms - first)).ToArray(), kept.Select(p => p.X).ToArray(), kept.Select(p => p.Y).ToArray());
            if (at >= 0)
            {
                entities[at] = entities[at] with { Track = track };
            }
            else
            {
                entities.Add(new ReplayEntity(track.Entity, other.Name, other.ClassName ?? "", role, track));
            }
        }

        return data with { Entities = entities };
    }
}
