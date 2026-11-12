using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Aion2;

/// <summary>A dense spawn spot of a pet: where (metres, the game's X and Y over 100), how many of its monsters spawn there, and the area's name.</summary>
public sealed record PetRouteSpot(double X, double Z, int Count, string Zone);

/// <summary>A teleport hop before a stop: the Kibelisk to teleport to and how far the stop is from it (metres).</summary>
public sealed record PetRouteTeleport(string Name, double X, double Z, int Distance);

/// <summary>How to get to a stop on a windstream island ("wind"), or back from one ("leave").</summary>
public sealed record PetRouteVia(string Kind, string Island, string? Teleport, string How, string Leave);

/// <summary>One stop of the route: where to stand (metres), how far its spots reach, the area, the island it is on (empty: the mainland), the pets
/// farmed there (meter pet ids, by English name) and the spot of each.</summary>
public sealed record PetRouteStop(int Step, double X, double Z, double Radius, string Zone, string Island, IReadOnlyList<int> Pets,
    IReadOnlyDictionary<int, PetRouteSpot> At, PetRouteTeleport? Teleport = null, PetRouteVia? Via = null);

/// <summary>A route: the map, where it starts (the camp, or where the player is), and the stops in the order to farm them.</summary>
public sealed record PetRoutePlan(string MapKey, double OriginX, double OriginZ, string OriginLabel, bool FromPlayer, IReadOnlyList<PetRouteStop> Stops);

/// <summary>
/// The pet farming route, from the camp or from where the player stands. It began as the route of atreia.app's Pet Soul Map Atlas
/// (guides/pets-route-map.html); since 12 November 2026 (the owner: "the pathing is a bit wrong ... users are reporting that it's cancer bad") it plans
/// on its own (<see cref="ChooseStops"/>): each pet is on the route once, at the first stop with nearly as many of its monsters as the stop of the
/// route with the most (<see cref="NearlyAsMany"/>), counted over everything a stop reaches; the fewest stops; the mainland's in the shortest order
/// found with teleports counted by their cost and a share of the straight way (so the route goes round the map instead of across it at every step), the
/// windstream islands after the mainland. The spawn spots, teleports, camps and islands (assets/aion2/pets/route.json) are made from the site's data by
/// Tools/pet-route/make-route.mjs.
/// </summary>
public static class PetRoute
{
    /// <summary>The genera the route can take (the special pets come from shops and events, never from monsters).</summary>
    public static readonly string[] Genera = { "Cogni", "Natura", "Fera", "Varian" };

    private sealed record Teleport(string Type, string Name, double X, double Z);

    private sealed record Island(string Teleport, string? Kibelisk, string How, string Leave);

    private sealed record MapData(string Site, Dictionary<string, (string Name, double X, double Z)> Starts, List<Teleport> Tps,
        Dictionary<string, Island> Islands, List<(int Id, string Name, List<PetRouteSpot> Spots)> Clusters);

    /// <summary>The route data: the planner's numbers (as the site's page had them: the stop's reach, what a dense spot was (no longer used: see
    /// <see cref="FarmableShare"/>), a teleport's cost in metres, how many fewest-stop sets are compared and how long the search for them may run, what an
    /// island stop costs) and the maps.</summary>
    private sealed record Data(double StopRadius, double GoodSpot, double TeleportCost, int Covers, int Nodes, double IslandStop, Dictionary<int, string> Genus, Dictionary<string, MapData> Maps);

    private static Data? _data;
    private static readonly object Gate = new();
    private static readonly Regex SkyIsland = new("Sky Island", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The route data from a file (the shipped one when null); for the checks.</summary>
    private static Data Load(string? path = null)
    {
        lock (Gate)
        {
            if (_data is not null && path is null)
            {
                return _data;
            }

            string file = path ?? Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "route.json");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            var genus = new Dictionary<int, string>();
            foreach (JsonProperty p in root.GetProperty("genus").EnumerateObject())
            {
                if (int.TryParse(p.Name, out int id))
                {
                    genus[id] = p.Value.GetString() ?? "";
                }
            }

            var maps = new Dictionary<string, MapData>();
            foreach (JsonProperty mp in root.GetProperty("maps").EnumerateObject())
            {
                JsonElement m = mp.Value;
                var starts = new Dictionary<string, (string, double, double)>();
                foreach (JsonProperty s in m.GetProperty("starts").EnumerateObject())
                {
                    starts[s.Name] = (s.Value[0].GetString() ?? "", s.Value[1].GetDouble(), s.Value[2].GetDouble());
                }

                var tps = m.GetProperty("tps").EnumerateArray().Select(t => new Teleport(t[0].GetString() ?? "", t[1].GetString() ?? "", t[2].GetDouble(), t[3].GetDouble())).ToList();
                var islands = new Dictionary<string, Island>();
                foreach (JsonProperty i in m.GetProperty("islands").EnumerateObject())
                {
                    JsonElement v = i.Value;
                    islands[i.Name] = new Island(v.GetProperty("tp").GetString() ?? "", v.GetProperty("kibelisk").ValueKind == JsonValueKind.String ? v.GetProperty("kibelisk").GetString() : null,
                        v.GetProperty("how").GetString() ?? "", v.GetProperty("leave").GetString() ?? "");
                }

                var clusters = m.GetProperty("clusters").EnumerateArray().Select(c => (c[0].GetInt32(), c[1].GetString() ?? "",
                    c[2].EnumerateArray().Select(q => new PetRouteSpot(q[0].GetDouble(), q[1].GetDouble(), q[2].GetInt32(), q[3].GetString() ?? "")).ToList())).ToList();
                maps[mp.Name] = new MapData(m.GetProperty("site").GetString() ?? mp.Name, starts, tps, islands, clusters);
            }

            int Int(string name, int fallback) => root.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;
            double Num(string name, double fallback) => root.TryGetProperty(name, out JsonElement v) ? v.GetDouble() : fallback;
            var data = new Data(root.GetProperty("stopRadius").GetDouble(), root.GetProperty("goodSpot").GetDouble(), root.GetProperty("teleportCost").GetDouble(),
                Int("covers", 24), Int("nodes", 100000), Num("islandStop", 600), genus, maps);
            if (path is null)
            {
                _data = data;
            }

            return data;
        }
    }

    /// <summary>The genus of a pet as the site names it (Cogni, Natura, Fera, Varian, Special), null for a pet it does not list.</summary>
    public static string? GenusOf(int petId) => Load().Genus.TryGetValue(petId, out string? g) ? g : null;

    /// <summary>Whether a map (Aion2MapInfo key: verteron, altgard, abyss) has a route.</summary>
    public static bool Covers(string mapKey) => Load().Maps.ContainsKey(mapKey);

    /// <summary>The area a place is in (metres), as the route names them: the area of the nearest dense spawn spot within 400 m, else empty
    /// (the islands are told apart by it).</summary>
    public static string ZoneNear(string mapKey, double x, double z)
    {
        if (!Load().Maps.TryGetValue(mapKey, out MapData? m))
        {
            return "";
        }

        string zone = "";
        double best = 400;
        foreach (var (_, _, spots) in m.Clusters)
        {
            foreach (PetRouteSpot q in spots)
            {
                double d = Dist((x, z), (q.X, q.Z));
                if (d < best)
                {
                    best = d;
                    zone = q.Zone;
                }
            }
        }

        return zone;
    }

    /// <summary>
    /// The Kibelisk to reach a stop by: the one the route teleports to (and the walk from it, metres), else the one nearest to the stop on its map, or,
    /// for a stop on an island, the island's own (the walk unknown: -1). Null on an island without one (it is left by teleport scroll).
    /// </summary>
    public static (string Name, int Walk)? KibeliskOf(string mapKey, PetRouteStop stop)
    {
        if (stop.Teleport is { } hop)
        {
            return (hop.Name, hop.Distance);
        }

        if (!Load().Maps.TryGetValue(mapKey, out MapData? m))
        {
            return null;
        }

        if (stop.Island.Length > 0)
        {
            return m.Islands.TryGetValue(stop.Island, out Island? isle) && !string.IsNullOrEmpty(isle.Kibelisk) ? (isle.Kibelisk!, -1) : null;
        }

        Teleport? best = m.Tps.Where(t => t.Type == "t").MinBy(t => Dist((stop.X, stop.Z), (t.X, t.Z)));
        return best is null ? null : (best.Name, (int)Math.Floor(Dist((stop.X, stop.Z), (best.X, best.Z)) + 0.5));
    }

    /// <summary>The pets of a map's route (by meter pet id), for the checks.</summary>
    internal static IReadOnlyList<int> PetsOn(string mapKey) => Load().Maps.TryGetValue(mapKey, out MapData? m) ? m.Clusters.Select(c => c.Id).ToList() : new List<int>();

    /// <summary>How many of a pet's monsters spawn within <paramref name="radius"/> metres of a place, on its island (empty: the mainland); for the checks.</summary>
    internal static int CountNear(string mapKey, int petId, double x, double z, double radius, string island)
    {
        if (!Load().Maps.TryGetValue(mapKey, out MapData? m))
        {
            return 0;
        }

        string IslandOf(string zone) => zone.Length > 0 && m.Islands.ContainsKey(zone) ? zone : "";
        return m.Clusters.Where(c => c.Id == petId).SelectMany(c => c.Spots).Where(q => IslandOf(q.Zone) == island && Dist((x, z), (q.X, q.Z)) <= radius).Sum(q => q.Count);
    }

    /// <summary>The site's English name of a pet on the route (the order of the pets of a stop follows it).</summary>
    public static string? NameOf(int petId) => Load().Maps.Values.SelectMany(m => m.Clusters).FirstOrDefault(c => c.Id == petId).Name;

    /// <summary>
    /// Plans the route of a map. <paramref name="faction"/>: "Elyos" or "Asmodian" (which camp it starts at). <paramref name="needed"/>: the pets still
    /// to farm. <paramref name="from"/>: where the player stands (metres and the area's name); null starts at the camp. <paramref name="pin"/>: the stop
    /// being farmed, which stays the first while it still has pets to farm. <paramref name="teleports"/> false: a route that walks, with no Kibelisk hops
    /// (the other side's land in a rift: its Kibelisks are not the player's).
    /// </summary>
    public static PetRoutePlan? Plan(string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed,
        (double X, double Z, string Zone)? from = null, PetRouteStop? pin = null, bool teleports = true) => PlanWith(Load(), mapKey, faction, genera, needed, from, pin, teleports);

    /// <summary>The same with the route data of a file (the checks).</summary>
    internal static PetRoutePlan? PlanFrom(string path, string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed, (double X, double Z, string Zone)? from = null) =>
        PlanWith(Load(path), mapKey, faction, genera, needed, from, null, true);

    /// <summary>A stop the planner chose: where (metres), how far its pets' spots reach, the island (empty: the mainland), its pets (by English name) and the spot of each, the area.</summary>
    private sealed record ChosenStop(double X, double Z, double R, string Island, List<int> Pets, Dictionary<int, PetRouteSpot> At, string Zone);

    /// <summary>
    /// A pet is farmable at a place where at least this share of the most of its monsters any place has spawn within reach (<see cref="FarmableFloor"/> at
    /// least, or all it has anywhere). 12 November 2026: the route asked for 60 % of the densest single spot, so a pet whose monsters spawn in many small
    /// groups around a stop was sent to a stop of its own elsewhere ("send me to a spot to farm a mob, then send me to another, where the mob from just
    /// now also spawns"); counted over the whole reach and at 40 %, the pets of a place are farmed where the group stands.
    /// </summary>
    private const double FarmableShare = 0.4;

    private const int FarmableFloor = 4;

    /// <summary>
    /// A pet is farmed at the first stop with at least this share of the most of its monsters any stop of the route has. At the first stop it was
    /// farmable at, a later stop sometimes had twice as many of it (Verteron: Ursus 19 at one stop, 45 two stops later; Slink 20, then 42 at the next):
    /// the same "the mob from just now also spawns" as before. Now a later stop never has half again as many, and a stop before never as many.
    /// </summary>
    private const double NearlyAsMany = 0.7;

    /// <summary>
    /// A teleport also counts this share of the straight way to the stop. Every teleport cost the same before, so the order of the stops reached by
    /// teleport did not matter to the planner and the route went across the whole map at every step (its line on the map half as long again as needed);
    /// with this it goes round the map, and teleports for the long ways. It costs the route a few seconds at most.
    /// </summary>
    private const double TeleportLine = 0.2;

    /// <summary>A stop gets a teleport only when it saves at least this many metres of riding (a teleport to the Kibelisk next to the stop just left
    /// saved seconds and took more clicks than riding).</summary>
    private const double TeleportGain = 100;

    /// <summary>Stops of the same island right after each other this close are shown as one (each pet keeps its spot; the stop's ring takes them all in).</summary>
    private const double MergeMetres = 200;

    /// <summary>How many of a pet's monsters spawn within reach of a place, and the densest of its spots there.</summary>
    private readonly record struct Reach(int Count, PetRouteSpot Spot);

    /// <summary>A place a stop may be at: where (metres), its island (empty: the mainland), and the pets farmable there.</summary>
    private sealed record Place((double X, double Z) Pos, string Island, Dictionary<int, Reach> Farm);

    /// <summary>
    /// The shortest order found from <paramref name="start"/> (null: anywhere, by teleport): from every first stop the nearest next, then turning parts
    /// round (2-opt) and moving runs of up to three stops elsewhere, until nothing gets shorter by more than half a metre. A leg is walking, or a teleport
    /// (<paramref name="teleportCost"/>, and <paramref name="line"/> of the straight way) and the walk from the Kibelisk nearest the stop (<paramref name="kd"/>;
    /// infinity: no teleports). Returns the order (indices into <paramref name="pts"/>) and its length in metres.
    /// </summary>
    private static (List<int> Order, double Cost) ShortestOrder(double teleportCost, double line, IReadOnlyList<(double X, double Z)> pts, IReadOnlyList<double> kd, (double X, double Z)? start)
    {
        int n = pts.Count;
        if (n == 0)
        {
            return (new List<int>(), 0);
        }

        var leg = new double[n, n];
        var first = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double d = Dist(pts[i], pts[j]);
                leg[i, j] = Math.Min(d, teleportCost + kd[j] + line * d);
            }

            first[i] = start is { } s0 ? Math.Min(Dist(s0, pts[i]), teleportCost + kd[i] + line * Dist(s0, pts[i])) : teleportCost + kd[i];
        }

        double Total(int[] o)
        {
            double c = first[o[0]];
            for (int k = 1; k < n; k++)
            {
                c += leg[o[k - 1], o[k]];
            }

            return c;
        }

        int[]? best = null;
        double bestC = double.PositiveInfinity;
        for (int f = 0; f < n; f++)
        {
            var o = new int[n];
            var used = new bool[n];
            o[0] = f;
            used[f] = true;
            for (int k = 1; k < n; k++)
            {
                int p = o[k - 1], bi = -1;
                for (int i = 0; i < n; i++)
                {
                    if (!used[i] && (bi < 0 || leg[p, i] < leg[p, bi]))
                    {
                        bi = i;
                    }
                }

                o[k] = bi;
                used[bi] = true;
            }

            double c = Total(o);
            if (c < bestC)
            {
                bestC = c;
                best = o;
            }
        }

        var buf = new int[n];
        for (bool imp = true; imp;)
        {
            imp = false;
            for (int a = 0; a < n - 1; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        buf[k] = k < a || k > b ? best![k] : best![a + b - k];
                    }

                    double c = Total(buf);
                    if (c < bestC - 0.5)
                    {
                        Array.Copy(buf, best!, n);
                        bestC = c;
                        imp = true;
                    }
                }
            }

            // a run of best[a..a+len) taken out and put back at position b of what is left (turned round on the second go); the first move that
            // shortens the way is taken, and the search starts over
            bool moved = false;
            for (int len = 1; len <= 3 && !moved; len++)
            {
                for (int a = 0; a + len <= n && !moved; a++)
                {
                    for (int b = 0; b <= n - len && !moved; b++)
                    {
                        if (b == a)
                        {
                            continue;
                        }

                        for (int rev = 0; rev < 2 && !moved; rev++)
                        {
                            int k = 0;
                            for (int r = 0; r <= n - len; r++)
                            {
                                if (r == b)
                                {
                                    for (int t = 0; t < len; t++)
                                    {
                                        buf[k++] = best![rev == 1 ? a + len - 1 - t : a + t];
                                    }
                                }

                                if (r < n - len)
                                {
                                    buf[k++] = best![r < a ? r : r + len];
                                }
                            }

                            double c = Total(buf);
                            if (c < bestC - 0.5)
                            {
                                Array.Copy(buf, best!, n);
                                bestC = c;
                                imp = true;
                                moved = true;
                            }
                        }
                    }
                }
            }
        }

        return (best!.ToList(), bestC);
    }

    /// <summary>
    /// The stops of a route, in their order (12 November 2026, the owner: "the pathing is a bit wrong ... users are reporting that it's cancer bad").
    /// The places a stop may be at are the spawn spots of the pets still to farm, each also moved twice to the middle of the pets farmable there; a pet is
    /// farmable at a place where enough of its monsters spawn within reach (<see cref="FarmableShare"/>). Of the fewest places that farm every pet (searched
    /// exactly over the sets of pets no other place outdoes, up to <see cref="Data.Covers"/> such sets, at most <see cref="Data.Nodes"/> steps) the one that
    /// ends with the fewest stops, then the shortest way, is taken: its places put in order (the island the player is on, the mainland by the shortest way
    /// found, then the windstream islands), each pet farmed at the first stop of that order with nearly as many of its monsters as the stop with the most
    /// (<see cref="NearlyAsMany"/>), places left without pets dropped, places of the same island right after each other merged where every pet of both
    /// stays farmable, and a place whose every pet is farmable at another stop of the route left out.
    /// </summary>
    private static List<ChosenStop> ChooseStops(Data data, MapData m, Func<string, string> islandOf, List<(int Id, string Name, List<PetRouteSpot> Spots)> pets,
        Func<(double X, double Z), (Teleport? T, double D)> nearTp, (double X, double Z)? start0, string startIsl, Dictionary<int, string> nameOf)
    {
        if (pets.Count == 0)
        {
            return new List<ChosenStop>();
        }

        double stopR = data.StopRadius, tp = data.TeleportCost;
        var left = pets.Select(p => p.Id).ToList();

        // within reach of a place: per pet, how many of its monsters spawn there and its densest spot
        Dictionary<int, Reach> ReachAt((double X, double Z) p, string island)
        {
            var found = new Dictionary<int, Reach>();
            foreach (var (id, _, spots) in pets)
            {
                int count = 0;
                PetRouteSpot? top = null;
                double topD = 0;
                foreach (PetRouteSpot q in spots)
                {
                    if (islandOf(q.Zone) != island)
                    {
                        continue;
                    }

                    double d = Dist(p, (q.X, q.Z));
                    if (d > stopR)
                    {
                        continue;
                    }

                    count += q.Count;
                    if (top is null || q.Count > top.Count || (q.Count == top.Count && d < topD))
                    {
                        top = q;
                        topD = d;
                    }
                }

                if (top is not null)
                {
                    found[id] = new Reach(count, top);
                }
            }

            return found;
        }

        var most = new Dictionary<int, int>();
        void Note(Dictionary<int, Reach> r)
        {
            foreach (var (id, v) in r)
            {
                most[id] = Math.Max(most.GetValueOrDefault(id), v.Count);
            }
        }

        double Need(int id) => Math.Max(Math.Min(FarmableFloor, most[id]), most[id] * FarmableShare);
        Dictionary<int, Reach> Farmable(Dictionary<int, Reach> r) => r.Where(kv => kv.Value.Count >= Need(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        (double X, double Z) Middle(IEnumerable<Reach> rs)
        {
            double x = 0, z = 0, w = 0;
            foreach (Reach r in rs)
            {
                x += r.Spot.X * r.Count;
                z += r.Spot.Z * r.Count;
                w += r.Count;
            }

            return (x / w, z / w);
        }

        // every spawn spot (each place once), then each moved twice to the middle of the pets farmable there; the most of each pet any of them has
        var evaluated = new List<((double X, double Z) Pos, string Island, Dictionary<int, Reach> Near)>();
        var anchorSeen = new HashSet<(double, double)>();
        foreach (var (_, _, spots) in pets)
        {
            foreach (PetRouteSpot q in spots)
            {
                if (anchorSeen.Add((q.X, q.Z)))
                {
                    string island = islandOf(q.Zone);
                    var r = ReachAt((q.X, q.Z), island);
                    Note(r);
                    evaluated.Add(((q.X, q.Z), island, r));
                }
            }
        }

        int anchors = evaluated.Count;
        for (int i = 0; i < anchors; i++)
        {
            var (pos, island, r) = evaluated[i];
            for (int k = 0; k < 2; k++)
            {
                var f = Farmable(r);
                if (f.Count == 0)
                {
                    break;
                }

                pos = Middle(f.Values);
                r = ReachAt(pos, island);
                Note(r);
                evaluated.Add((pos, island, r));
            }
        }

        var places = new List<Place>();
        var placeKeys = new HashSet<(double, double, string)>();
        foreach (var (pos, island, r) in evaluated)
        {
            if (placeKeys.Add((Math.Floor(pos.X + 0.5), Math.Floor(pos.Z + 0.5), island)) && Farmable(r) is { Count: > 0 } f)
            {
                places.Add(new Place(pos, island, f));
            }
        }

        int pc = left.Count, words = (pc + 31) >> 5;
        var mask = places.Select(pl =>
        {
            var w = new uint[words];
            for (int k = 0; k < pc; k++)
            {
                if (pl.Farm.ContainsKey(left[k]))
                {
                    w[k >> 5] |= 1u << (k & 31);
                }
            }

            return w;
        }).ToList();

        // the sets of pets a place can farm: each once (the place with the most monsters of them), and only those no other set holds and more
        int Monsters(Place pl) => pl.Farm.Values.Sum(v => v.Count);
        var byKey = new Dictionary<string, int>();
        for (int i = 0; i < places.Count; i++)
        {
            string key = string.Join(",", mask[i]);
            if (!byKey.TryGetValue(key, out int j) || Monsters(places[i]) > Monsters(places[j]))
            {
                byKey[key] = i;
            }
        }

        var uniq = byKey.Values.OrderBy(i => i).ToList();
        bool Within(uint[] a, uint[] b)
        {
            for (int w = 0; w < words; w++)
            {
                if ((a[w] & ~b[w]) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        int Bits(uint[] a)
        {
            int c = 0;
            for (int w = 0; w < words; w++)
            {
                c += System.Numerics.BitOperations.PopCount(a[w]);
            }

            return c;
        }

        var sets = uniq.Where(i => !uniq.Any(j => j != i && Within(mask[i], mask[j]) && !Within(mask[j], mask[i]))).ToList();
        var byPet = Enumerable.Range(0, pc).Select(p => sets.Where(i => ((mask[i][p >> 5] >> (p & 31)) & 1) != 0).ToList()).ToList();
        int maxSize = sets.Count == 0 ? 1 : sets.Max(i => Bits(mask[i]));
        int nodes = 0, bestK = int.MaxValue;
        var sols = new List<List<int>>();
        void Search(uint[] unc, List<int> chosen)
        {
            if (++nodes > data.Nodes)
            {
                return;
            }

            int open = Bits(unc);
            if (open == 0)
            {
                if (chosen.Count < bestK)
                {
                    bestK = chosen.Count;
                    sols.Clear();
                }

                if (chosen.Count == bestK && sols.Count < data.Covers)
                {
                    sols.Add(chosen.ToList());
                }

                return;
            }

            if (chosen.Count + (open + maxSize - 1) / maxSize > bestK)
            {
                return;
            }

            // the open pet the fewest sets farm (the first such)
            int bp = -1, bn = int.MaxValue;
            for (int p = 0; p < pc; p++)
            {
                if (((unc[p >> 5] >> (p & 31)) & 1) != 0 && byPet[p].Count < bn)
                {
                    bn = byPet[p].Count;
                    bp = p;
                }
            }

            if (bn == 0)
            {
                return; // a pet no place farms (cannot be: each is farmable where it has the most)
            }

            var opts = byPet[bp].Select(i =>
            {
                int c = 0;
                for (int w = 0; w < words; w++)
                {
                    c += System.Numerics.BitOperations.PopCount(mask[i][w] & unc[w]);
                }

                return (I: i, C: c);
            }).OrderByDescending(o => o.C).ThenBy(o => o.I).ToList();
            foreach (var (i, _) in opts)
            {
                var u = new uint[words];
                for (int w = 0; w < words; w++)
                {
                    u[w] = unc[w] & ~mask[i][w];
                }

                chosen.Add(i);
                Search(u, chosen);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        var all = new uint[words];
        for (int p = 0; p < pc; p++)
        {
            all[p >> 5] |= 1u << (p & 31);
        }

        Search(all, new List<int>());
        if (sols.Count == 0)
        {
            // none found in the steps given: the place that farms the most of what is left, until nothing is left
            var greedy = new List<int>();
            var unc = all.ToArray();
            while (Bits(unc) > 0)
            {
                int bi = -1, bc = 0;
                for (int i = 0; i < places.Count; i++)
                {
                    int c = 0;
                    for (int w = 0; w < words; w++)
                    {
                        c += System.Numerics.BitOperations.PopCount(mask[i][w] & unc[w]);
                    }

                    if (c > bc)
                    {
                        bc = c;
                        bi = i;
                    }
                }

                if (bi < 0)
                {
                    break;
                }

                greedy.Add(bi);
                for (int w = 0; w < words; w++)
                {
                    unc[w] &= ~mask[bi][w];
                }
            }

            sols.Add(greedy);
        }

        // the order of a set of places: the island the player is on, then the mainland (the shortest way found), then the islands with a Kibelisk, then
        // those without (you leave those by teleport scroll): on an island the nearest next from the island's way in, then improved by turning parts round
        (double X, double Z)? TpAt(string name) => m.Tps.FirstOrDefault(t => t.Type == "t" && t.Name == name) is { } t ? (t.X, t.Z) : null;
        int Rank(string a) => a == startIsl ? 0 : a.Length == 0 ? 1 : m.Islands[a].Kibelisk is not null ? 2 : 3;
        List<Place> Sequence(List<Place> list)
        {
            var result = new List<Place>();
            (double X, double Z)? cur = start0;
            foreach (string area in list.Select(p => p.Island).Distinct().OrderBy(Rank).ToList())
            {
                var rest = list.Where(p => p.Island == area).ToList();
                if (area.Length == 0)
                {
                    var pts = rest.Select(p => p.Pos).ToList();
                    var (o, _) = ShortestOrder(tp, TeleportLine, pts, pts.Select(p => nearTp(p).D).ToList(), cur);
                    var ordered = o.Select(k => rest[k]).ToList();
                    result.AddRange(ordered);
                    if (ordered.Count > 0)
                    {
                        cur = ordered[^1].Pos;
                    }

                    continue;
                }

                (double X, double Z)? entry = area != startIsl ? TpAt(m.Islands[area].Teleport) ?? cur : cur;
                var sub = new List<Place>();
                (double X, double Z)? c0 = entry;
                while (rest.Count > 0)
                {
                    Place bi = rest[0];
                    foreach (Place p in rest)
                    {
                        if (Dist(c0, p.Pos) < Dist(c0, bi.Pos))
                        {
                            bi = p;
                        }
                    }

                    sub.Add(bi);
                    rest.Remove(bi);
                    c0 = bi.Pos;
                }

                (double X, double Z)? P(int k) => k < 0 ? entry : sub[k].Pos;
                double D2((double X, double Z)? a, (double X, double Z)? b) => a is { } p && b is { } q ? Dist(p, q) : double.NaN;
                bool better = true;
                for (int pass = 0; better && pass < 80; pass++)
                {
                    better = false;
                    for (int a = -1; a < sub.Count - 2; a++)
                    {
                        for (int b = a + 2; b < sub.Count; b++)
                        {
                            var pa = P(a);
                            var pb = P(a + 1);
                            var pcc = P(b);
                            (double X, double Z)? pd = b + 1 < sub.Count ? P(b + 1) : null;
                            double now = D2(pa, pb) + (pd is not null ? D2(pcc, pd) : 0), turned = D2(pa, pcc) + (pd is not null ? D2(pb, pd) : 0);
                            if (turned < now - 0.5)
                            {
                                sub.Reverse(a + 1, b - a);
                                better = true;
                            }
                        }
                    }
                }

                result.AddRange(sub);
                cur = sub[^1].Pos;
            }

            return result;
        }

        // each pet at the first place of the order with nearly as many of its monsters as the place of the order with the most
        Dictionary<int, Place> Assign(List<Place> seq)
        {
            var asg = new Dictionary<int, Place>();
            foreach (int n in left)
            {
                int top = 0;
                foreach (Place p in seq)
                {
                    if (p.Farm.TryGetValue(n, out Reach have) && have.Count > top)
                    {
                        top = have.Count;
                    }
                }

                if (seq.FirstOrDefault(p => p.Farm.TryGetValue(n, out Reach got) && got.Count >= NearlyAsMany * top) is { } at)
                {
                    asg[n] = at;
                }
            }

            return asg;
        }

        (double Cost, List<(Place P, List<(int Pet, Reach R)> Mine)> Stops) Build(List<int> sol)
        {
            var list = sol.Select(i => places[i]).ToList();
            for (int round = 0; round < 8; round++)
            {
                var seq = Sequence(list);
                var asg = Assign(seq);
                var used = seq.Where(p => asg.Values.Contains(p)).ToList();
                bool changed = used.Count < seq.Count;
                // the same island, right after each other and close: one place, where every pet of both stays farmable (the middle, else either place)
                var merged = new List<(Place P, List<Place> From)>();
                foreach (Place p in used)
                {
                    if (merged.Count > 0 && merged[^1].P.Island == p.Island && Dist(merged[^1].P.Pos, p.Pos) <= MergeMetres)
                    {
                        var (last, members) = merged[^1];
                        var group = members.Append(p).ToList();
                        var mine = left.Where(n => asg.TryGetValue(n, out Place? a) && group.Contains(a)).ToList();
                        Place? one = null;
                        foreach (var pos in new[] { Middle(mine.Select(n => asg[n].Farm[n])), last.Pos, p.Pos })
                        {
                            var f = Farmable(ReachAt(pos, p.Island));
                            if (mine.All(f.ContainsKey))
                            {
                                one = new Place(pos, p.Island, f);
                                break;
                            }
                        }

                        if (one is not null)
                        {
                            merged[^1] = (one, group);
                            changed = true;
                            continue;
                        }
                    }

                    merged.Add((p, new List<Place> { p }));
                }

                list = merged.Select(x => x.P).ToList();
                if (!changed)
                {
                    break;
                }
            }

            var order = Sequence(list);
            var assigned = Assign(order);
            // a place whose every pet is farmable at another place of the route is left out
            for (bool again = true; again;)
            {
                again = false;
                foreach (Place p in order)
                {
                    var mine = left.Where(n => assigned.TryGetValue(n, out Place? a) && a == p).ToList();
                    if (mine.Count > 0 && mine.All(n => order.Any(o => o != p && o.Farm.ContainsKey(n))))
                    {
                        order = Sequence(order.Where(o => o != p).ToList());
                        assigned = Assign(order);
                        again = true;
                        break;
                    }
                }
            }

            // stops of the same island right after each other and close are shown as one
            var stops = new List<(Place P, List<(int Pet, Reach R)> Mine)>();
            foreach (Place p in order)
            {
                var mine = left.Where(n => assigned.TryGetValue(n, out Place? a) && a == p).Select(n => (n, p.Farm[n])).ToList();
                if (mine.Count == 0)
                {
                    continue;
                }

                if (stops.Count > 0 && stops[^1].P.Island == p.Island && Dist(stops[^1].P.Pos, p.Pos) <= MergeMetres)
                {
                    var both = stops[^1].Mine.Concat(mine).ToList();
                    stops[^1] = (new Place(Middle(both.Select(x => x.Item2)), p.Island, both.ToDictionary(x => x.Item1, x => x.Item2)), both);
                    continue;
                }

                stops.Add((p, mine));
            }

            double cost = 0;
            (double X, double Z)? prev = start0;
            foreach (var (p, _) in stops)
            {
                if (p.Island.Length > 0)
                {
                    cost += data.IslandStop;
                    prev = p.Pos;
                    continue;
                }

                double walk = prev is { } q ? Dist(q, p.Pos) : double.PositiveInfinity;
                cost += Math.Min(walk, tp + nearTp(p.Pos).D + (prev is null ? 0 : TeleportLine * walk));
                prev = p.Pos;
            }

            return (cost, stops);
        }

        (double Cost, List<(Place P, List<(int Pet, Reach R)> Mine)> Stops)? pick = null;
        foreach (List<int> sol in sols)
        {
            var r = Build(sol);
            if (pick is not { } pk || r.Stops.Count < pk.Stops.Count || (r.Stops.Count == pk.Stops.Count && r.Cost < pk.Cost - 1e-9))
            {
                pick = r;
            }
        }

        var result = new List<ChosenStop>();
        foreach (var (p, mine) in pick?.Stops ?? new List<(Place P, List<(int Pet, Reach R)> Mine)>())
        {
            double r = mine.Max(x => Dist(p.Pos, (x.R.Spot.X, x.R.Spot.Z))) + 50;
            var ids = mine.Select(x => x.Pet).OrderBy(id => nameOf[id], StringComparer.Ordinal).ToList();
            var spots = mine.ToDictionary(x => x.Pet, x => x.R.Spot);
            // the area the stop is in: the one most of its pets' spots are in (the first such in the order of the pets)
            var counts = new List<(string Zone, int Count)>();
            foreach (int id in ids)
            {
                string zone = spots[id].Zone;
                int ix = counts.FindIndex(zc => zc.Zone == zone);
                if (ix < 0)
                {
                    counts.Add((zone, 1));
                }
                else
                {
                    counts[ix] = (zone, counts[ix].Count + 1);
                }
            }

            int top = counts.Max(zc => zc.Count);
            string area = counts.First(zc => zc.Count == top).Zone;
            result.Add(new ChosenStop(p.Pos.X, p.Pos.Z, r, p.Island, ids, spots, area.Length > 0 ? area : "Open area"));
        }

        return result;
    }

    private static double Dist((double X, double Z) a, (double X, double Z) b) => Hypot(a.X - b.X, a.Z - b.Z);

    /// <summary>
    /// The distance as JavaScript's Math.hypot works it out (V8: scaled by the larger value, the squares added with Kahan's compensation): the route's
    /// planner began as the website's, and planned its routes to the last digit with it (where two orders of the stops cost the same, that decided).
    /// </summary>
    private static double Hypot(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        double max = Math.Max(x, y);
        if (double.IsInfinity(max))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (max == 0)
        {
            return 0;
        }

        double sum = 0, compensation = 0;
        double a = x / max, b = y / max;
        double summand = a * a - compensation;
        double preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
        summand = b * b - compensation;
        preliminary = sum + summand;
        sum = preliminary;
        return Math.Sqrt(sum) * max;
    }

    private static double Dist((double X, double Z)? a, (double X, double Z) b) => a is { } p ? Dist(p, b) : double.NaN;

    private static PetRoutePlan? PlanWith(Data data, string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed,
        (double X, double Z, string Zone)? from, PetRouteStop? pin, bool teleports)
    {
        if (!data.Maps.TryGetValue(mapKey, out MapData? m))
        {
            return null;
        }

        string side = string.Equals(faction, "Elyos", StringComparison.OrdinalIgnoreCase) ? "elyos" : "asmodian";
        double tpCost = data.TeleportCost;
        string IslandOf(string zone) => zone.Length > 0 && m.Islands.ContainsKey(zone) ? zone : "";
        var names = m.Clusters.Where(c => data.Genus.TryGetValue(c.Id, out string? g) && genera.Contains(g) && needed(c.Id)).ToList();
        var nameOf = names.ToDictionary(c => c.Id, c => c.Name);
        var pinned = pin is null ? new List<int>() : pin.Pets.Where(id => nameOf.ContainsKey(id)).ToList();
        var camp = m.Starts.TryGetValue(side, out var own) ? own : m.Starts.Values.First();

        // the Kibelisks one can teleport to (home maps only; not the island or Sky Island ones; none on the other side's land)
        var islandKibs = m.Islands.Values.Select(i => i.Kibelisk).Where(k => k is not null).ToHashSet();
        var kibs = !teleports || m.Site == "reshanta" ? new List<Teleport>()
            : m.Tps.Where(t => t.Type == "t" && !islandKibs.Contains(t.Name) && !SkyIsland.IsMatch(t.Name)).ToList();
        (Teleport? T, double D) NearTp((double X, double Z) p)
        {
            Teleport? b = null;
            double bd = double.PositiveInfinity;
            foreach (Teleport t in kibs)
            {
                double d = Dist(p, (t.X, t.Z));
                if (d < bd)
                {
                    bd = d;
                    b = t;
                }
            }

            return (b, bd);
        }

        bool freeStart = pinned.Count == 0 && from is null && kibs.Count > 0;
        // where the route starts (null: anywhere, by teleport)
        (double X, double Z)? start0 = null;
        if (pinned.Count > 0)
        {
            start0 = (pin!.X, pin.Z);
        }
        else if (from is { } f0)
        {
            start0 = (f0.X, f0.Z);
        }
        else if (!freeStart)
        {
            start0 = (camp.X, camp.Z);
        }

        PetRouteStop? pinStop = pinned.Count > 0
            ? new PetRouteStop(1, pin!.X, pin.Z, pin.Radius, pin.Zone, IslandOf(pin.Zone), pinned.OrderBy(id => nameOf[id], StringComparer.Ordinal).ToList(), pinned.ToDictionary(id => id, id => pin.At[id]))
            : null;
        string startIsl = pinStop is not null ? pinStop.Island : from is { } fz ? IslandOf(fz.Zone) : "";
        var stops = ChooseStops(data, m, IslandOf, names.Where(c => !pinned.Contains(c.Id)).ToList(), NearTp, start0, startIsl, nameOf);

        var groups = new List<PetRouteStop>();
        if (pinStop is not null)
        {
            groups.Add(pinStop);
        }

        foreach (ChosenStop s in stops)
        {
            groups.Add(new PetRouteStop(groups.Count + 1, s.X, s.Z, s.R, s.Zone, s.Island, s.Pets, s.At));
        }

        double ox, oz;
        string label;
        bool fromPlayer = pinStop is not null || from is not null;
        if (pinStop is not null)
        {
            (ox, oz, label) = (pinStop.X, pinStop.Z, "YOU · " + pinStop.Zone);
        }
        else if (from is { } start)
        {
            (ox, oz, label) = (start.X, start.Z, "YOU · " + start.Zone);
        }
        else
        {
            (ox, oz, label) = (camp.X, camp.Z, "START · " + camp.Name);
        }

        // the windstream islands: how to get there, and back
        string was = startIsl;
        for (int k = 0; k < groups.Count; k++)
        {
            PetRouteStop g = groups[k];
            if (g.Island != was)
            {
                if (g.Island.Length > 0)
                {
                    Island isl = m.Islands[g.Island];
                    groups[k] = g with { Via = new PetRouteVia("wind", g.Island, isl.Teleport, isl.How, isl.Leave) };
                }
                else
                {
                    Island? from2 = m.Islands.TryGetValue(was, out Island? i) ? i : null;
                    string how = from2 is not null && from2.Kibelisk is null
                        ? $"You are on {was}, which has no Kibelisk: use a teleport scroll, then continue here."
                        : "Teleport back to the mainland with a Kibelisk, then continue here.";
                    groups[k] = g with { Via = new PetRouteVia("leave", was, null, how, "") };
                }
            }

            was = g.Island;
        }

        // teleport hops on the mainland: where the nearest Kibelisk and a short walk beat riding from the last stop, as the route counts them, by a margin
        if (kibs.Count > 0)
        {
            (double X, double Z)? prev = freeStart ? null : (ox, oz);
            for (int k = 0; k < groups.Count; k++)
            {
                PetRouteStop g = groups[k];
                if (g.Island.Length == 0 && (g.Via is null || g.Via.Kind == "leave"))
                {
                    var (t, d) = NearTp((g.X, g.Z));
                    double walk = prev is { } p ? Dist(p, (g.X, g.Z)) : double.PositiveInfinity;
                    if (t is not null && (prev is null || tpCost + d + TeleportLine * walk + TeleportGain < walk))
                    {
                        groups[k] = g with { Teleport = new PetRouteTeleport(t.Name, t.X, t.Z, (int)Math.Floor(d + 0.5)) };
                    }
                }

                prev = (g.X, g.Z);
            }

            if (freeStart && groups.Count > 0 && groups[0].Teleport is { } firstHop)
            {
                (ox, oz, label) = (firstHop.X, firstHop.Z, "START · Teleport: " + firstHop.Name);
            }
        }

        return new PetRoutePlan(mapKey, ox, oz, label, fromPlayer, groups);
    }
}
