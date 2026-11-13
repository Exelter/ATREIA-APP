using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>
/// The open world counted for the own fight only (13 November 2026, a player grinding Kerubiel with the meter on All: "still showing killing mobs" - every
/// player killing monsters around him was on it). Whenever the open world is counted (its switch, the Grind switch, a boss nearby, or the zone not known yet
/// because the meter started in the middle of a game) a hit counts when it is done by or to the own character, a member of its group or one of their summons;
/// lands on or comes from something they fought in the last minute (others on the same monsters: a corps, a crowded spot); or lands on or comes from a boss
/// (an open-world boss's fight is everybody's). A heal counts from or to a player in that fight, or on something in it. Players fighting their own monsters
/// elsewhere stay off the meter. In a dungeon or an arena everything counts, as before. Pure: the questions about the entities are handed in.
/// </summary>
public sealed class OwnFight
{
    /// <summary>How long something the own side fought, and a player who fought it with them, stay part of the fight.</summary>
    public static readonly TimeSpan Within = TimeSpan.FromSeconds(60);

    private readonly Dictionary<int, DateTime> _fought = new();
    private readonly Dictionary<int, DateTime> _fighters = new();

    public void Clear()
    {
        _fought.Clear();
        _fighters.Clear();
    }

    /// <summary>
    /// The batch with the own fight's events only. <paramref name="ours"/>: the own character, a member of its group or one of their summons;
    /// <paramref name="boss"/>: a boss; <paramref name="player"/>: a player character.
    /// </summary>
    public CombatBatch Keep(CombatBatch batch, Func<int, bool> ours, Func<int, bool> boss, Func<int, bool> player)
    {
        var known = new Dictionary<int, bool>();
        bool Ours(int id)
        {
            if (!known.TryGetValue(id, out bool o))
            {
                known[id] = o = ours(id);
            }

            return o;
        }

        // what the own side hit, and what hit it - from the whole batch first: another player's hit on the same monster just before the own one counts too
        foreach (DamageEvent ev in batch.Damage)
        {
            if (ev.IsHeal || ev.SourceObjectId == ev.TargetObjectId)
            {
                continue;
            }

            if (Ours(ev.SourceObjectId) && !Ours(ev.TargetObjectId))
            {
                Note(_fought, ev.TargetObjectId, ev.Timestamp);
            }
            else if (Ours(ev.TargetObjectId) && !Ours(ev.SourceObjectId))
            {
                Note(_fought, ev.SourceObjectId, ev.Timestamp);
            }
        }

        bool InFight(int id, DateTime at) => Ours(id) || (_fought.TryGetValue(id, out DateTime last) && at - last <= Within) || boss(id);

        // the hits name the players fighting with the own side, then the heals of those players and on them are kept (a healer of the other party at an
        // open-world boss)
        IReadOnlyList<DamageEvent> all = batch.Damage;
        var keep = new bool[all.Count];
        for (int i = 0; i < all.Count; i++)
        {
            DamageEvent ev = all[i];
            if (!ev.IsHeal && (InFight(ev.SourceObjectId, ev.Timestamp) || InFight(ev.TargetObjectId, ev.Timestamp)))
            {
                keep[i] = true;
                if (!Ours(ev.SourceObjectId) && player(ev.SourceObjectId))
                {
                    Note(_fighters, ev.SourceObjectId, ev.Timestamp);
                }
            }
        }

        bool Fighter(int id, DateTime at) => Ours(id) || (_fighters.TryGetValue(id, out DateTime last) && at - last <= Within);
        var damage = new List<DamageEvent>(all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            DamageEvent ev = all[i];
            if (ev.IsHeal ? Fighter(ev.SourceObjectId, ev.Timestamp) || Fighter(ev.TargetObjectId, ev.Timestamp) || InFight(ev.TargetObjectId, ev.Timestamp) : keep[i])
            {
                damage.Add(ev);
            }
        }

        var avoids = batch.Avoids.Where(a => InFight(a.SourceObjectId, a.Timestamp) || InFight(a.TargetObjectId, a.Timestamp)).ToList();
        var kills = batch.Kills.Where(k => InFight(k.VictimObjectId, k.Timestamp) || (k.KillerObjectId is int killer && Ours(killer))).ToList();
        Prune(all);
        return batch with { Damage = damage, Avoids = avoids, Kills = kills };
    }

    private static void Note(Dictionary<int, DateTime> seen, int id, DateTime at)
    {
        if (!seen.TryGetValue(id, out DateTime last) || at > last)
        {
            seen[id] = at;
        }
    }

    // what has not been fought for a minute leaves the fight (the newest hit's time is the clock: a replay's events are not today's)
    private void Prune(IReadOnlyList<DamageEvent> all)
    {
        if (_fought.Count + _fighters.Count < 256 || all.Count == 0)
        {
            return;
        }

        DateTime now = all.Max(e => e.Timestamp);
        foreach (Dictionary<int, DateTime> seen in new[] { _fought, _fighters })
        {
            foreach (int id in seen.Where(kv => now - kv.Value > Within).Select(kv => kv.Key).ToList())
            {
                seen.Remove(id);
            }
        }
    }
}
