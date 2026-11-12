using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The boss mechanics measured on recorded Ludra fights (assets/aion2/bosses/mechanic_areas.json): the shapes, the objects on the floor, her boards and waves, the
/// replay drawing them (and sending them to the site) and the cast bar counting with them.</summary>
public static class SelfCheckMechanics
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Boss mechanics measured on recorded fights (Abyssal Forge: Ludra):");
        bool ok = true;

        // the table: Eternal Ludra's line, her circle on a player, Ketu's room-wide attack, a pool on the floor; a skill the fits did not trust keeps the client's word
        MechanicArea? line = Aion2MechanicAreas.AreaOf(1803140), onTarget = Aion2MechanicAreas.AreaOf(1803250), cone = Aion2MechanicAreas.AreaOf(1803120), room = Aion2MechanicAreas.AreaOf(1801969);
        MechanicHazard? pool = Aion2MechanicAreas.HazardOf(2920755);
        bool table = line is { Shape: "line", Measured: true, LengthCm: > 0, WidthCm: > 0 } && onTarget is { Shape: "circle", OnTarget: true, RadiusCm: > 1000 }
            && cone is { Shape: "cone", Measured: false } && room is { Room: true } && pool is { RadiusCm: > 100 and < 1000, Seconds: > 5 } && Aion2MechanicAreas.IsObject(2920793)
            && !Aion2MechanicAreas.IsObject(2301014) && Aion2MechanicAreas.LandMsOf(1803130) is > 1000 && Aion2MechanicAreas.AreaOf(1236440) is null or { Measured: false };
        Console.WriteLine($"  -> Ludra's line, her circle on a player, Ketu's room-wide attack and the pools are in the table; a skill without a trusted fit keeps the client's shape: {table}");
        ok &= table;

        // the replay: the boss's line in its measured size, a pool on the floor with its reach while it stays, its hit on a player counted, the pool no boss
        DateTime at = new(2026, 11, 8, 19, 0, 0);
        const int boss = 100, player = 1, poolId = 200;
        var tracks = new[]
        {
            new PositionTrack(player, at, new[] { 0, 10000, 20000 }, new[] { 0, 500, 500 }, new[] { 0, 0, 500 }),
            new PositionTrack(boss, at, new[] { 0, 20000 }, new[] { 800, 800 }, new[] { 800, 800 }),
            new PositionTrack(poolId, at.AddSeconds(2), new[] { 0 }, new[] { 300 }, new[] { 300 }),
        };
        var extras = new FightExtras(Array.Empty<SkillUse>(), Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string>(), tracks,
            new[] { new WindupRecord(at.AddSeconds(5), boss, 1803140, boss, 1500, 90, 800, 1100, true) }, Array.Empty<DeathRecord>());
        var events = new[] { new DamageEvent(at.AddSeconds(4), poolId, player, 900, false, "Attack", SkillId: 1803185) };
        ReplayData data = ReplayBuilder.Build("Ludra", extras, events, at, at.AddSeconds(20), id => id == player ? ("Me", "Cleric", true) : (id == boss ? "Eternal Ludra" : "", "", false),
            _ => 333, _ => "Attack", null, null, id => id == boss ? 2301014 : id == poolId ? 2920755 : null);
        ReplayArea? lineArea = data.Areas.FirstOrDefault(a => !a.Zone);
        ReplayArea? zone = data.Areas.FirstOrDefault(a => a.Zone);
        bool replay = lineArea is { Shape.Shape: "line", Sized: true, Facing: 90 } && zone is { Actor: poolId } && Math.Abs(zone.Start - 2) < 0.01 && zone.End > 7 && zone.Radius == pool!.RadiusCm
            && data.Entities.Single(e => e.Id == poolId).Role == ReplayRole.Mechanic && data.Entities.Single(e => e.Id == boss).Role == ReplayRole.Boss && data.Hits.Count == 1;
        Console.WriteLine($"  -> the replay draws the boss's line in its measured size, the pool's reach while it stays and counts the pool's hit on the group: {replay}");
        ok &= replay;

        // her boards (12 November 2026, the owner: "the actual bingo etc for ludra etc. all visible"): the 6 x 6 board and the 3 x 3 one are in the table with the side of
        // a tile, her waves with their speed; no board is a pool any more
        MechanicFloor? bingo = Aion2MechanicAreas.FloorOf(2920772), nine = Aion2MechanicAreas.FloorOf(2920748);
        MechanicWave? edge = Aion2MechanicAreas.WaveOf(2920760);
        bool floors = bingo is { SizeCm: 600, Seconds: > 15 } && bingo.Skills.Contains(1803014) && nine is { SizeCm: 2000 } && Aion2MechanicAreas.HazardOf(2920772) is null
            && Aion2MechanicAreas.HazardOf(2920748) is null && edge is { SkillId: 1803315, SpeedCmS: > 500, HalfAngle: 90 } && edge.ReachCm > 1000
            && Aion2MechanicAreas.WatchesCasts(2920772) && Aion2MechanicAreas.WatchesCasts(2920760) && !Aion2MechanicAreas.WatchesCasts(2920755) && Aion2MechanicAreas.IsObject(2920748);
        Console.WriteLine($"  -> her boards (6 x 6 tiles of 6 m, 3 x 3 of 20 m) and her waves are in the table, the boards no pools: {floors}");
        ok &= floors;

        // a board on the replay: each of the 35 tiles a square from when it appeared, lit from its first cast; the square left out found as the one to stand on; a wave
        // from where it set out; the board's tiles send no paths, so the group and the boss go first; the squares and the wave go to the site with when and how fast
        const int firstTile = 1000, waveId = 300;
        var board = new List<PositionTrack>();
        var casts = new List<ObjectCast>();
        for (int cx = 0, n = 0; cx < 6; cx++)
        {
            for (int cy = 0; cy < 6; cy++)
            {
                if (cx == 2 && cy == 3)
                {
                    continue;
                }

                int id = firstTile + n++;
                board.Add(new PositionTrack(id, at.AddSeconds(3), new[] { 0 }, new[] { 10000 + cx * 600 }, new[] { 20000 + cy * 600 }));
                casts.Add(new ObjectCast(at.AddSeconds(7 + n % 4 * 3.7), id, 1803014, 0, 10000 + cx * 600, 20000 + cy * 600, true));
                casts.Add(new ObjectCast(at.AddSeconds(7.5 + n % 4 * 3.7), id, 1803014, 0, 10000 + cx * 600, 20000 + cy * 600, true)); // its beat: no second light-up
            }
        }

        board.Add(new PositionTrack(waveId, at.AddSeconds(4), new[] { 0 }, new[] { 5000 }, new[] { 5000 }));
        casts.Add(new ObjectCast(at.AddSeconds(5), waveId, 1803315, 45, 5000, 5000, true));
        var keep = new Aion2CombatState();
        foreach (ObjectCast c in casts)
        {
            keep.NoteObjectCast(c, onceWithin: c.Actor != waveId ? TimeSpan.FromSeconds(bingo!.Seconds + 1) : null);
        }

        var all = board.Select(t => t.Entity).ToHashSet();
        List<ObjectCast> kept = keep.Slice(at, at.AddSeconds(30), _ => "x", id => all.Contains(id)).ObjectCasts?.ToList() ?? new();
        var boardExtras = extras with { Tracks = board.Concat(tracks).ToList(), ObjectCasts = kept };
        Func<int, int?> npcs = id => id == boss ? 2301014 : id == poolId ? 2920755 : id == waveId ? 2920760 : id >= firstTile ? 2920772 : null;
        ReplayData boardData = ReplayBuilder.Build("Ludra", boardExtras, events, at, at.AddSeconds(30),
            id => id == player ? ("Me", "Cleric", true) : (id == boss ? "Eternal Ludra" : $"0x{id:X8}", "", false), _ => 333, _ => "Attack", null, id => id == player, npcs);
        var squares = boardData.Areas.Where(a => a.Shape is { Shape: "square" }).ToList();
        ReplayBoard? found = ReplayBoards.Of(boardData.Areas).SingleOrDefault();
        ReplayArea? waveArea = boardData.Areas.SingleOrDefault(a => a.Shape is { Shape: "wave" });
        bool drawn = kept.Count == 36 && squares.Count == 35 && squares.All(q => q.Zone && q.Shape!.SizeCm == 600 && q.LitAt is double lit && lit >= q.Start + 3.9)
            && found is { SizeCm: 600, Known: true, LastLit: > 7 } && found.Tiles.Count == 35 && found.Gaps.Count == 1 && found.Gaps[0] == (10000 + 2 * 600.0, 20000 + 3 * 600.0)
            && waveArea is { Start: 5, Facing: 45, X: 5000, Y: 5000 } && waveArea.Shape is { SpeedCmS: 1150 } && Math.Abs(waveArea.Radius - edge!.ReachCm) < 1;
        Console.WriteLine($"  -> the replay draws the board's 35 tiles lit one by one, finds the square left out and the wave where it set out: {drawn}");
        ok &= drawn;

        var upTracks = AionDPS.Upload.ReplayUploadBuilder.Tracks(boardData, includeOthers: false);
        var (upAreas, _) = AionDPS.Upload.ReplayUploadBuilder.Attacks(boardData, at, includeOthers: false);
        bool sent = upTracks.Select(t => t.Kind).SequenceEqual(new[] { "player", "boss", "mech", "mech" }) && upTracks[0].IsSelf
            && upAreas.Count(u => u.Shape == "square" && u.Size == 600 && u.LitMs is int litMs && litMs >= u.StartMs + 3900) == 35
            && upAreas.SingleOrDefault(u => u.Shape == "wave") is { Speed: 1150, Width: 350, Half: 90, StartMs: 5000 };
        Console.WriteLine($"  -> to the site: the group and the boss first, no path for a tile, the squares with when they lit and the wave with its speed: {sent}");
        ok &= sent;

        // kept with the fight: the casts and the NPC ids come back from the file, so a stored fight still shows its board
        FightExtras stored = History.ExtrasBlob.Unpack(History.ExtrasBlob.Pack(boardExtras with { Npcs = all.ToDictionary(id => id, id => npcs(id)!.Value) }));
        bool file = stored.ObjectCasts is { Count: 36 } back && back[0] == kept[0] && stored.Npcs is { } ids && ids[firstTile] == 2920772 && ids[waveId] == 2920760;
        ReplayData again = ReplayBuilder.Build("Ludra", stored, events, at, at.AddSeconds(30),
            id => id == player ? ("Me", "Cleric", true) : (id == boss ? "Eternal Ludra" : $"0x{id:X8}", "", false), _ => 333, _ => "Attack", null, id => id == player, _ => null);
        file &= again.Areas.Count(a => a.Shape is { Shape: "square" }) == 35 && again.Areas.Any(a => a.Shape is { Shape: "wave" });
        Console.WriteLine($"  -> a stored fight keeps the casts and NPC ids, and its board and wave are drawn without the capture: {file}");
        ok &= file;

        // a measured circle beats the size the meter learnt; an unmeasured one keeps it
        var w = new WindupRecord(at, boss, 1803130, boss, 1500, 0, 0, 0, true);
        bool sizes = ReplayBuilder.AreaOf(w, 0, _ => "", _ => 333).RadiusCm is > 1000 && ReplayBuilder.AreaOf(w with { SkillId = 1236440 }, 0, _ => "", _ => 333).RadiusCm == 333;
        Console.WriteLine($"  -> a measured size beats the one the meter learnt, which still counts for a skill not measured: {sizes}");
        ok &= sizes;

        // the cast bar: before the meter has seen a Ludra attack land, the measured time from the windup's end to the hit counts
        var state = new Aion2CombatState();
        state.NoteWindup(new CastNotice(boss, 1803130, player, at, 1500, 0, 0, 0, 0, true));
        CastView? view = state.Casts(at.AddMilliseconds(500), TimeSpan.FromSeconds(1)).FirstOrDefault();
        bool cast = view is { IsArea: true, LandMs: > 1000 };
        Console.WriteLine($"  -> the cast bar counts Ludra's attack as an area and knows when it lands from the first pull: {cast}");
        ok &= cast;
        return ok;
    }
}
