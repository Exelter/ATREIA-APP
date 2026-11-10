using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The boss mechanics measured on recorded Ludra fights (assets/aion2/bosses/mechanic_areas.json): the shapes, the objects on the floor, the replay drawing them
/// and the cast bar counting with them.</summary>
public static class SelfCheckMechanics
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Boss mechanics measured on recorded fights (Abyssal Forge: Ludra):");
        bool ok = true;

        // the table: Eternal Ludra's line, her circle on a player, Ketu's room-wide attack, a pool on the floor; a skill the fits did not trust keeps the client's word
        MechanicArea? line = Aion2MechanicAreas.AreaOf(1803140), onTarget = Aion2MechanicAreas.AreaOf(1803250), cone = Aion2MechanicAreas.AreaOf(1803120), room = Aion2MechanicAreas.AreaOf(1801969);
        MechanicHazard? pool = Aion2MechanicAreas.HazardOf(2920748);
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
            _ => 333, _ => "Attack", null, null, id => id == boss ? 2301014 : id == poolId ? 2920748 : null);
        ReplayArea? lineArea = data.Areas.FirstOrDefault(a => !a.Zone);
        ReplayArea? zone = data.Areas.FirstOrDefault(a => a.Zone);
        bool replay = lineArea is { Shape.Shape: "line", Sized: true, Facing: 90 } && zone is { Actor: poolId } && Math.Abs(zone.Start - 2) < 0.01 && zone.End > 7 && zone.Radius == pool!.RadiusCm
            && data.Entities.Single(e => e.Id == poolId).Role == ReplayRole.Mechanic && data.Entities.Single(e => e.Id == boss).Role == ReplayRole.Boss && data.Hits.Count == 1;
        Console.WriteLine($"  -> the replay draws the boss's line in its measured size, the pool's reach while it stays and counts the pool's hit on the group: {replay}");
        ok &= replay;

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
