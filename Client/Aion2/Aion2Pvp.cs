using System.IO;
using System.Text.Json;
using AionDPS.Combat;

namespace AionDPS.Aion2;

/// <summary>One skill of one attacker in a death recap.</summary>
public sealed record RecapSkill(string Name, int SkillId, int Hits, long Amount, int Crits);

/// <summary>One attacker in a death recap: what he took off, in how many hits, with which skills (the most first).</summary>
public sealed record RecapSource(int Id, string Name, string? ClassName, bool Player, long Amount, int Hits, IReadOnlyList<RecapSkill> Skills);

/// <summary>
/// How someone died: the hits of the last seconds before it (at most <see cref="Aion2Pvp.RecapWindow"/>), by attacker, the killing blow, the heals that came in
/// between, and how long it took from the first hit of the burst to the death.
/// </summary>
public sealed record DeathRecap(DateTime At, int Victim, string VictimName, double Seconds, long Taken, long Healed, RecapSource? Killer, string? KillingSkill,
    long KillingAmount, IReadOnlyList<RecapSource> Sources)
{
    /// <summary>The share of the damage the killer did.</summary>
    public double KillerShare => Killer is { } k && Taken > 0 ? (double)k.Amount / Taken : 0;
}

/// <summary>A player the own character fought: when first and last, how often (fights five minutes apart count apart), his deaths with the own hits on him, and
/// the own deaths with his hits in them (killed: he struck the last blow).</summary>
public sealed class Rival
{
    public string Name { get; set; } = "";
    public int ServerId { get; set; }
    public string? ClassName { get; set; }
    public int? CombatPower { get; set; }
    public DateTime FirstMet { get; set; }
    public DateTime LastMet { get; set; }
    public int Met { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int KilledBy { get; set; }
}

/// <summary>
/// The own character's fights with players and deaths (10 November 2026, the owner picked: "death recap - when you die, who hit you, with which skills, how much,
/// over how many seconds" and "rivals - the enemy card remembers players you've fought"). Fed every poll with the hits and the deaths (hit points read at 0) of
/// the stream: it keeps the last minute of hits on players, and when the own character dies it works out the recap (<see cref="Died"/>); a player the own
/// character traded hits with becomes a rival, and his deaths and the own ones count for him. Rivals are kept in aion2-rivals.json; nothing leaves the PC.
/// </summary>
public static class Aion2Pvp
{
    /// <summary>The seconds before a death a recap looks at.</summary>
    public static readonly TimeSpan RecapWindow = TimeSpan.FromSeconds(15);

    /// <summary>Hits this far apart are two fights with the same player.</summary>
    private static readonly TimeSpan NewFight = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Keep = TimeSpan.FromSeconds(60);
    private static readonly object Gate = new();
    private static readonly List<DamageEvent> Recent = new();
    private static readonly Dictionary<string, Rival> RivalStore = new(StringComparer.OrdinalIgnoreCase);
    private static bool _persist, _dirty;
    private static DateTime _savedAt = DateTime.MinValue;
    private static DeathRecap? _last;

    /// <summary>The own character died (raised on the thread that feeds, the UI's).</summary>
    public static event Action<DeathRecap>? Died;

    /// <summary>A player of the group or the own character died: the wipe review goes by these.</summary>
    public static event Action<DeathRecap>? PlayerDied;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-rivals.json");

    /// <summary>The own character's last death, null before one.</summary>
    public static DeathRecap? LastRecap
    {
        get
        {
            lock (Gate)
            {
                return _last;
            }
        }
    }

    /// <summary>Every rival, the latest fought first.</summary>
    public static IReadOnlyList<Rival> Rivals
    {
        get
        {
            lock (Gate)
            {
                return RivalStore.Values.OrderByDescending(r => r.LastMet).Select(Copy).ToList();
            }
        }
    }

    /// <summary>A player's record against the own character (null: never fought).</summary>
    public static Rival? RivalOf(string name, int serverId)
    {
        lock (Gate)
        {
            return RivalStore.TryGetValue(Key(name, serverId), out Rival? r) ? Copy(r) : null;
        }
    }

    /// <summary>
    /// The hits and deaths of one poll. <paramref name="nameOf"/> names anyone (players, monsters); <paramref name="isPlayer"/> tells another player;
    /// <paramref name="local"/> is the own character (-1: not known yet); <paramref name="watched"/> the players whose deaths are worth a recap of their own
    /// (the group's, for the wipe review).
    /// </summary>
    public static void Feed(IReadOnlyList<DamageEvent> damage, IReadOnlyList<(int Entity, DateTime At)> deaths, int local, Func<int, string> nameOf, Func<int, string?> classOf,
        Func<int, bool> isPlayer, Func<int, int> serverOf, Func<int, int?> powerOf, Func<int, bool> watched)
    {
        var raised = new List<(DeathRecap Recap, bool Own)>();
        lock (Gate)
        {
            foreach (DamageEvent e in damage)
            {
                if (e.Amount <= 0 && !e.IsHeal)
                {
                    continue;
                }

                Recent.Add(e);
                if (e.IsHeal || local < 0)
                {
                    continue;
                }

                // a hit between the own character and another player: a rival met
                int other = e.SourceObjectId == local && e.TargetObjectId != local ? e.TargetObjectId : e.TargetObjectId == local && e.SourceObjectId != local ? e.SourceObjectId : -1;
                if (other >= 0 && isPlayer(other) && nameOf(other) is { Length: > 0 } name && !name.StartsWith("0x", StringComparison.Ordinal))
                {
                    Rival r = RivalFor(name, serverOf(other));
                    if (r.Met == 0 || e.Timestamp - r.LastMet > NewFight)
                    {
                        r.Met++;
                    }

                    r.FirstMet = r.Met == 1 && r.FirstMet == default ? e.Timestamp : r.FirstMet;
                    r.LastMet = e.Timestamp > r.LastMet ? e.Timestamp : r.LastMet;
                    r.ClassName = classOf(other) ?? r.ClassName;
                    r.CombatPower = powerOf(other) ?? r.CombatPower;
                    _dirty = true;
                }
            }

            DateTime newest = Recent.Count > 0 ? Recent.Max(e => e.Timestamp) : DateTime.MinValue;
            Recent.RemoveAll(e => newest - e.Timestamp > Keep);

            foreach ((int victim, DateTime at) in deaths)
            {
                bool own = victim == local;
                bool player = own || isPlayer(victim);
                if (!player)
                {
                    continue;
                }

                DeathRecap recap = Build(victim, at, Recent, nameOf, classOf, isPlayer);
                if (own)
                {
                    _last = recap;
                    // every player who hit the own character in it: the death counts for him; the one with the last blow killed
                    foreach (RecapSource s in recap.Sources.Where(s => s.Player))
                    {
                        Rival r = RivalFor(s.Name, serverOf(s.Id));
                        r.Deaths++;
                        if (recap.Killer?.Id == s.Id)
                        {
                            r.KilledBy++;
                        }

                        _dirty = true;
                    }

                    raised.Add((recap, true));
                }
                else if (local >= 0 && recap.Sources.Any(s => s.Id == local) && nameOf(victim) is { Length: > 0 } name && !name.StartsWith("0x", StringComparison.Ordinal))
                {
                    // another player died with the own hits in his last seconds: a kill
                    Rival r = RivalFor(name, serverOf(victim));
                    r.Kills++;
                    _dirty = true;
                }

                if (!own && watched(victim))
                {
                    raised.Add((recap, false));
                }
            }

            Save(force: false);
        }

        foreach ((DeathRecap recap, bool own) in raised)
        {
            if (own)
            {
                Died?.Invoke(recap);
            }

            PlayerDied?.Invoke(recap);
        }
    }

    /// <summary>The recap of <paramref name="victim"/>'s death at <paramref name="at"/> from <paramref name="hits"/> (the self-checks call it too).</summary>
    public static DeathRecap Build(int victim, DateTime at, IReadOnlyList<DamageEvent> hits, Func<int, string> nameOf, Func<int, string?> classOf, Func<int, bool> isPlayer)
    {
        // the hits of the window; a gap of more than five seconds without a hit starts the burst that killed anew
        List<DamageEvent> taken = hits.Where(h => !h.IsHeal && h.TargetObjectId == victim && h.Amount > 0 && h.Timestamp <= at.AddSeconds(1) && at - h.Timestamp <= RecapWindow)
            .OrderBy(h => h.Timestamp).ToList();
        for (int i = taken.Count - 1; i > 0; i--)
        {
            if (taken[i].Timestamp - taken[i - 1].Timestamp > TimeSpan.FromSeconds(5))
            {
                taken = taken.Skip(i).ToList();
                break;
            }
        }

        DateTime from = taken.Count > 0 ? taken[0].Timestamp : at;
        long healed = hits.Where(h => h.IsHeal && h.TargetObjectId == victim && h.Timestamp >= from && h.Timestamp <= at).Sum(h => h.Amount);
        var sources = taken.GroupBy(h => h.SourceObjectId).Select(g =>
        {
            var skills = g.GroupBy(h => h.SkillId != 0 ? h.SkillId.ToString(System.Globalization.CultureInfo.InvariantCulture) : h.Skill ?? "?")
                .Select(s => new RecapSkill(SkillName(s.First()), s.First().SkillId, s.Sum(h => h.HitCount), s.Sum(h => h.Amount), s.Count(h => h.IsCritical)))
                .OrderByDescending(s => s.Amount).ToList();
            return new RecapSource(g.Key, nameOf(g.Key), classOf(g.Key), isPlayer(g.Key), g.Sum(h => h.Amount), g.Sum(h => h.HitCount), skills);
        }).OrderByDescending(s => s.Amount).ToList();
        DamageEvent? last = taken.Count > 0 ? taken[^1] : null;
        RecapSource? killer = last is { } blow ? sources.FirstOrDefault(s => s.Id == blow.SourceObjectId) : null;
        return new DeathRecap(at, victim, nameOf(victim), Math.Max(0, (at - from).TotalSeconds), taken.Sum(h => h.Amount), healed, killer,
            last is { } k ? SkillName(k) : null, last?.Amount ?? 0, sources);
    }

    private static string SkillName(DamageEvent e) =>
        e.Skill is { Length: > 0 } named ? named : e.SkillId != 0 ? Protocol.Aion2SkillNames.NameOf(e.SkillId) : "Attack";

    /// <summary>Empties the tracker without touching the file (the self-checks).</summary>
    public static void ResetForCheck()
    {
        lock (Gate)
        {
            Recent.Clear();
            RivalStore.Clear();
            _last = null;
        }
    }

    // Under the lock.
    private static Rival RivalFor(string name, int serverId)
    {
        string key = Key(name, serverId);
        if (!RivalStore.TryGetValue(key, out Rival? r))
        {
            // a player first met without his server, then with it (or the other way round), is one rival
            string? loose = RivalStore.Keys.FirstOrDefault(k => k.StartsWith(name + "|", StringComparison.OrdinalIgnoreCase) && (serverId == 0 || k.EndsWith("|0", StringComparison.Ordinal)));
            if (loose is not null && RivalStore.Remove(loose, out Rival? found))
            {
                r = found;
                r.ServerId = serverId != 0 ? serverId : r.ServerId;
                key = Key(name, r.ServerId);
            }
            else
            {
                r = new Rival { Name = name, ServerId = serverId };
            }

            RivalStore[key] = r;
        }

        return r;
    }

    private static string Key(string name, int serverId) => name + "|" + serverId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Rival Copy(Rival r) => new()
    {
        Name = r.Name, ServerId = r.ServerId, ClassName = r.ClassName, CombatPower = r.CombatPower, FirstMet = r.FirstMet, LastMet = r.LastMet, Met = r.Met, Kills = r.Kills,
        Deaths = r.Deaths, KilledBy = r.KilledBy,
    };

    /// <summary>Starts keeping the rivals in <see cref="StorePath"/> (the live meter only). Loads what is there.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath) && JsonSerializer.Deserialize<List<Rival>>(File.ReadAllText(StorePath)) is { } loaded)
                {
                    RivalStore.Clear();
                    foreach (Rival r in loaded.Where(r => r.Name.Length > 0))
                    {
                        RivalStore[Key(r.Name, r.ServerId)] = r;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(StorePath);
                }
            }
        }
    }

    public static void Flush()
    {
        lock (Gate)
        {
            Save(force: true);
        }
    }

    // Under the lock; at most every 10 s unless forced. The 2,000 met last are kept.
    private static void Save(bool force)
    {
        if (!_persist || !_dirty || !force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AionDPS.AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(RivalStore.Values.OrderByDescending(r => r.LastMet).Take(2000).ToList()));
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; the next save tries again.
        }
    }
}
