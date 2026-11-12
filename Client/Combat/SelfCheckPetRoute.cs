using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The pet route (Aion2/PetRoute.cs). Until 12 November 2026 it had to plan the routes of atreia.app's Pet Soul Map Atlas to the last stop; players found
/// them bad (a player: "send me to a spot to farm a mob, then send me to another, where the mob from just now also spawns"), and the route now plans its
/// own way. Its checks: every pet still needed is on the route once; none is farmed later than a stop before it where as many of its monsters spawn, nor
/// at a stop when a later one has half again as many; no stop is there for pets that spawn as much at other stops; no two stops in a row on the same
/// ground stand close together; the routes of the home maps are shorter than the site's were (Verteron 22 stops, 33.9 km of line on the map; Altgard 20
/// stops, 40.4 km).
/// </summary>
public static class SelfCheckPetRoute
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Pet route:");
        bool ok = Routes();

        // every stop of every route (all genera, nothing done, from each faction's start) has spawn points of each of its pets on the meter's map
        // within 100 m of that pet's spot: the route never leads to a place where the map shows nothing of what it is there for
        foreach (var (mapKey, faction) in new[] { ("verteron", "Elyos"), ("altgard", "Asmodian"), ("abyss", "Elyos"), ("abyss", "Asmodian") })
        {
            var map = AionDPS.Aion2.Protocol.Aion2Maps.All.FirstOrDefault(m => m.Key == mapKey);
            PetRoutePlan? plan = PetRoute.Plan(mapKey, faction, PetRoute.Genera.ToHashSet(), _ => true);
            if (map is null || plan is null)
            {
                ok = false;
                Console.WriteLine($"  -> {mapKey}: no map or no route: FAIL");
                continue;
            }

            int pets = 0, covered = 0;
            var missing = new List<string>();
            foreach (PetRouteStop stop in plan.Stops)
            {
                foreach (int pet in stop.Pets)
                {
                    pets++;
                    // the pet's spot at this stop (the middle of a group of its monsters): its spawn points lie around it
                    var points = AionDPS.Aion2.Protocol.Aion2Maps.PointsOf(map, new HashSet<int> { pet });
                    PetRouteSpot spot = stop.At[pet];
                    if (points.Any(pt => Math.Sqrt((pt.X / 100.0 - spot.X) * (pt.X / 100.0 - spot.X) + (pt.Y / 100.0 - spot.Z) * (pt.Y / 100.0 - spot.Z)) <= 100))
                    {
                        covered++;
                    }
                    else
                    {
                        missing.Add($"stop {stop.Step} {PetRoute.NameOf(pet)}");
                    }
                }
            }

            bool all = covered == pets;
            ok &= all;
            Console.WriteLine($"  -> {mapKey} ({faction}): {plan.Stops.Count} stops, {covered} of {pets} pets with spawn points on the map at their spot: {(all ? "ok" : "FAIL " + string.Join(", ", missing.Take(6)))}");
        }

        ok &= PetsOfTheSpiders();
        ok &= OwnPetsOfAMap();
        ok &= RouteInARift();
        Console.WriteLine(ok ? "  -> pet route: ok" : "  -> pet route: FAIL");
        return ok;
    }

    /// <summary>The routes of the three maps: every pet once, none late or early, no stop that is not needed, no two close stops in a row, fewer stops
    /// and a shorter line than the site's route had.</summary>
    private static bool Routes()
    {
        bool ok = true;
        var all = PetRoute.Genera.ToHashSet();
        var scenarios = new List<(string Name, string Map, string Faction, HashSet<string> Genera, HashSet<int> Done, (double, double, string)? Start, int MostStops, double MostLine)>();
        foreach (var (map, faction) in new[] { ("verteron", "Elyos"), ("altgard", "Asmodian"), ("abyss", "Elyos"), ("abyss", "Asmodian") })
        {
            var ids = PetRoute.PetsOn(map);
            // the routes from the camp with every pet still needed: fewer stops than the site's (22 in Verteron, 20 in Altgard, 5 in the Abyss) and a
            // line on the map well under the site's 33.9 and 40.4 km
            var (stops, line) = map switch { "verteron" => (19, 19000.0), "altgard" => (17, 24000.0), _ => (4, 3000.0) };
            scenarios.Add(($"{map} ({faction}), every pet", map, faction, all, new HashSet<int>(), null, stops, line));
            scenarios.Add(($"{map} ({faction}), every other pet done", map, faction, all, ids.Where((_, i) => i % 2 == 1).ToHashSet(), null, int.MaxValue, double.MaxValue));
            scenarios.Add(($"{map} ({faction}), Fera only", map, faction, new HashSet<string> { "Fera" }, new HashSet<int>(), null, int.MaxValue, double.MaxValue));
        }

        scenarios.Add(("verteron, from where the player stands", "verteron", "Elyos", all, new HashSet<int>(), (-200, 100, ""), int.MaxValue, double.MaxValue));
        scenarios.Add(("altgard, from where the player stands", "altgard", "Asmodian", all, new HashSet<int>(), (900, -700, ""), int.MaxValue, double.MaxValue));
        foreach (var (name, map, faction, genera, done, start, mostStops, mostLine) in scenarios)
        {
            PetRoutePlan? plan = PetRoute.Plan(map, faction, genera, id => !done.Contains(id), start);
            var wanted = PetRoute.PetsOn(map).Where(id => PetRoute.GenusOf(id) is { } g && genera.Contains(g) && !done.Contains(id)).ToHashSet();
            var planned = plan?.Stops.SelectMany(st => st.Pets).ToList() ?? new List<int>();
            bool once = planned.Count == wanted.Count && planned.ToHashSet().SetEquals(wanted);
            var issues = plan is null ? new List<string> { "no route" } : Audit(map, plan);
            double line = 0;
            (double X, double Z)? prev = start is { } f ? (f.Item1, f.Item2) : null;
            foreach (PetRouteStop st in plan?.Stops ?? new List<PetRouteStop>())
            {
                if (prev is { } p)
                {
                    line += Math.Sqrt((st.X - p.X) * (st.X - p.X) + (st.Z - p.Z) * (st.Z - p.Z));
                }

                prev = (st.X, st.Z);
            }

            int count = plan?.Stops.Count ?? 0;
            bool good = once && issues.Count == 0 && count <= mostStops && line <= mostLine;
            ok &= good;
            Console.WriteLine($"  -> {name}: {count} stops, {planned.Count} of {wanted.Count} pets once each, line {line / 1000:0.0} km"
                + $"{(mostStops < int.MaxValue ? $" (at most {mostStops} stops, {mostLine / 1000:0} km)" : "")}: {(good ? "ok" : "FAIL " + string.Join("; ", issues.Take(4)))}");
        }

        return ok;
    }

    /// <summary>
    /// What a player would find wrong with a route: a pet farmed at a stop when a stop before it has as many of its monsters within reach (260 m; at the
    /// stop itself, within its ring), or a later stop half again as many (and 10 more: the mob just farmed spawns all over the next stops); a stop whose
    /// every pet spawns as much at another stop; two stops in a row on the same ground within 200 m.
    /// </summary>
    private static List<string> Audit(string map, PetRoutePlan plan)
    {
        var issues = new List<string>();
        int Near(int pet, PetRouteStop st, double radius) => PetRoute.CountNear(map, pet, st.X, st.Z, Math.Max(radius, 260), st.Island);
        foreach (PetRouteStop st in plan.Stops)
        {
            foreach (int pet in st.Pets)
            {
                int here = Near(pet, st, st.Radius);
                if (plan.Stops.Where(h => h.Step < st.Step).FirstOrDefault(h => Near(pet, h, 260) >= Math.Max(4, here)) is { } before)
                {
                    issues.Add($"{PetRoute.NameOf(pet)} at stop {st.Step} ({here}), as many at stop {before.Step}");
                }

                var after = plan.Stops.Where(h => h.Step > st.Step).Select(h => (h.Step, Count: Near(pet, h, 260))).FirstOrDefault(h => h.Count >= 1.5 * here && h.Count - here >= 10);
                if (after.Step > 0)
                {
                    issues.Add($"{PetRoute.NameOf(pet)} at stop {st.Step} ({here}), {after.Count} at stop {after.Step}");
                }
            }

            if (st.Pets.All(pet => plan.Stops.Any(h => h.Step != st.Step && Near(pet, h, 260) >= Math.Max(4, Near(pet, st, st.Radius)))))
            {
                issues.Add($"stop {st.Step}: its pets spawn as much at other stops");
            }
        }

        for (int i = 1; i < plan.Stops.Count; i++)
        {
            PetRouteStop a = plan.Stops[i - 1], b = plan.Stops[i];
            double apart = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
            if (a.Island == b.Island && apart <= 200)
            {
                issues.Add($"stops {a.Step} and {b.Step} {apart:0} m apart");
            }
        }

        return issues;
    }

    /// <summary>
    /// The two spider pets the meter had the wrong way round (12 November 2026, a player: "this mob i got max but still doesnt count ... why it sending
    /// me here"): the Marsh, Highland and Garden spiders and the Unstable Ashen Spider drop Soul: Mutated Aberrant Odyle Spider, the pet 1025
    /// (OdSpider_04_V01); the Aberrant Odyle Spider (1021, OdSpider_04) comes from chests and the shop only. Every soul and pet item now names its pet.
    /// </summary>
    private static bool PetsOfTheSpiders()
    {
        bool spiders = new[] { 2100030, 2100745, 2100663, 2400744 }.All(npc => AionDPS.Aion2.Protocol.Aion2Pets.PetsOfNpc(npc).SequenceEqual(new[] { 1025 }));
        bool names = AionDPS.Aion2.Protocol.Aion2Pets.PetName(1025, "en") == "Mutated Aberrant Odyle Spider" && AionDPS.Aion2.Protocol.Aion2Pets.PetName(1021, "en") == "Aberrant Odyle Spider"
            && AionDPS.Aion2.Protocol.Aion2Pets.PetOfSoulItem(534660020) == 1025 && AionDPS.Aion2.Protocol.Aion2Pets.PetOfSoulItem(534660024) == 1021
            && AionDPS.Aion2.Protocol.Aion2Pets.PetOfShopItem(534760020) == 1025 && AionDPS.Aion2.Protocol.Aion2Pets.PetOfShopItem(534760024) == 1021
            && AionDPS.Aion2.Protocol.Aion2Pets.NpcsOfPet(1021).Count == 0 && AionDPS.Aion2.Protocol.Aion2Pets.IsShopOnly(1021) && !AionDPS.Aion2.Protocol.Aion2Pets.IsShopOnly(1025)
            && PetRoute.NameOf(1025) == "Mutated Aberrant Odyle Spider" && PetRoute.NameOf(1021) is null;
        // every soul (534660xxx) and pet item (534760xxx) the data ties to a pet carries that pet's name: "Soul: X (Bound)", "Pet: X"
        var wrong = new List<string>();
        foreach (int item in Enumerable.Range(534660001, 400).Concat(Enumerable.Range(534760001, 400)))
        {
            int? pet = item < 534760000 ? AionDPS.Aion2.Protocol.Aion2Pets.PetOfSoulItem(item) : AionDPS.Aion2.Protocol.Aion2Pets.PetOfShopItem(item);
            string? itemName = pet is null ? null : AionDPS.Aion2.Protocol.Aion2ItemNames.NameOf(item, "en");
            if (pet is int id && itemName is not null)
            {
                string named = itemName.Replace("Soul: ", "").Replace("Pet: ", "").Replace(" (Bound)", "");
                if (!string.Equals(named, AionDPS.Aion2.Protocol.Aion2Pets.PetName(id, "en"), StringComparison.OrdinalIgnoreCase))
                {
                    wrong.Add($"{item} {itemName} -> {id} {AionDPS.Aion2.Protocol.Aion2Pets.PetName(id, "en")}");
                }
            }
        }

        bool good = spiders && names && wrong.Count == 0;
        Console.WriteLine($"  -> the spiders drop the Mutated Aberrant Odyle Spider (1025), the Aberrant one (1021) is chests and shop only: {spiders && names}; "
            + $"every soul and pet item names its pet: {(wrong.Count == 0 ? "ok" : "FAIL " + string.Join("; ", wrong.Take(3)))}");
        return good;
    }

    /// <summary>
    /// "Only this map's own pets" counts the maps the player farms (their side's and the Abyss): an Asmodian in a rift into Verteron also gets the pets
    /// Verteron shares with Eltnen only (Giant Bee), which no Asmodian map has; nobody gets the Mutated Aberrant Odyle Spider there (Altgard has it too).
    /// Without the player's side every other open map counts, as before.
    /// </summary>
    private static bool OwnPetsOfAMap()
    {
        var verteron = AionDPS.Aion2.Protocol.Aion2Maps.All.First(m => m.Key == "verteron");
        var altgard = AionDPS.Aion2.Protocol.Aion2Maps.All.First(m => m.Key == "altgard");
        var rift = AionDPS.Aion2.Protocol.Aion2Maps.OnlyOn(verteron, "Asmodian");
        var any = AionDPS.Aion2.Protocol.Aion2Maps.OnlyOn(verteron);
        bool sides = AionDPS.Aion2.Protocol.Aion2Maps.SideOf(verteron) == "Elyos" && AionDPS.Aion2.Protocol.Aion2Maps.SideOf(altgard) == "Asmodian"
            && AionDPS.Aion2.Protocol.Aion2Maps.SideOf(AionDPS.Aion2.Protocol.Aion2Maps.All.First(m => m.Key == "abyss")) is null;
        bool good = sides && rift.Contains(1059) && !any.Contains(1059) && any.IsSubsetOf(rift) && !rift.Contains(1025) && !any.Contains(1025)
            && !AionDPS.Aion2.Protocol.Aion2Maps.OnlyOn(altgard, "Elyos").Contains(1025);
        Console.WriteLine($"  -> this map's own pets: {rift.Count} for an Asmodian in Verteron (Giant Bee among them), {any.Count} against every map; "
            + $"the Mutated Aberrant Odyle Spider in neither: {(good ? "ok" : "FAIL")}");
        return good;
    }

    /// <summary>On the other side's land (a rift) the route walks: no Kibelisk hop anywhere, and it starts where the player is.</summary>
    private static bool RouteInARift()
    {
        var all = PetRoute.Genera.ToHashSet();
        (double X, double Z, string Zone) here = (-200, 100, "");
        PetRoutePlan? home = PetRoute.Plan("verteron", "Elyos", all, _ => true, here);
        PetRoutePlan? rift = PetRoute.Plan("verteron", "Asmodian", all, _ => true, here, teleports: false);
        bool good = home is { Stops.Count: > 0 } && rift is { Stops.Count: > 0 } && home.Stops.Any(s => s.Teleport is not null)
            && rift.Stops.All(s => s.Teleport is null) && rift.FromPlayer && Round(rift.OriginX) == -200 && Round(rift.OriginZ) == 100;
        Console.WriteLine($"  -> a route in a rift walks from the player: {rift?.Stops.Count} stops, {rift?.Stops.Count(s => s.Teleport is not null)} teleports "
            + $"(at home {home?.Stops.Count(s => s.Teleport is not null)}): {(good ? "ok" : "FAIL")}");
        return good;
    }

    /// <summary>Rounded as JavaScript's Math.round does (halves up).</summary>
    private static int Round(double v) => (int)Math.Floor(v + 0.5);
}
