using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>
/// The open world counted for the own fight only (<see cref="OwnFight"/>, 13 November 2026: a player grinding Kerubiel saw every player killing monsters
/// around him on the meter): the own character, its group and their summons, others on the monsters they fight, and a boss's fight with its healers stay;
/// players fighting their own monsters, and heals between strangers, go; a monster not fought for a minute leaves the fight. Made-up ids.
/// </summary>
public static class SelfCheckOwnFight
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] The open world counted for the own fight only:");
        const int me = 1, mate = 2, passer = 3, helper = 4, healer = 5, stranger = 6, other = 7, summon = 10;
        const int mine = 100, his = 101, mates = 102, boss = 900;
        var t0 = new DateTime(2026, 11, 13, 13, 0, 0);
        DamageEvent Hit(int from, int to, double at = 0) => new(t0.AddSeconds(at), from, to, 1000, false);
        DamageEvent Heal(int from, int to, double at = 0) => new(t0.AddSeconds(at), from, to, 500, true);
        var fight = new OwnFight();
        CombatBatch Keep(CombatBatch batch) => fight.Keep(batch, id => id is me or mate or summon, id => id == boss, id => id is >= 1 and < 10);

        var batch = new CombatBatch(
            new List<DamageEvent>
            {
                Hit(helper, mine, 0),    // another player on the own monster, a moment before the own hit: in the fight
                Hit(me, mine, 0.2),
                Hit(passer, his, 0.3),   // a passer-by on his own monster: not
                Hit(mine, me, 0.4),
                Hit(his, passer, 0.5),   // and what it does to him: not
                Hit(summon, mine, 0.6),  // the own summon
                Hit(mate, mates, 0.7),   // the group member on another monster
                Hit(passer, boss, 0.8),  // a boss nearby is everybody's fight
                Hit(boss, passer, 0.9),
                Heal(healer, passer, 1), // a healer of the boss's fighters
                Heal(stranger, other, 1.1), // strangers healing each other: not
            },
            new List<AvoidEvent> { new(t0.AddSeconds(1.2), me, mine, AvoidKind.Dodge), new(t0.AddSeconds(1.3), passer, his, AvoidKind.Parry) },
            new List<KillEvent> { new(t0.AddSeconds(2), me, mine, false), new(t0.AddSeconds(2.1), passer, his, false) });
        CombatBatch kept = Keep(batch);
        bool hits = kept.Damage.Count == 8 && !kept.Damage.Any(e => e.SourceObjectId == passer && e.TargetObjectId == his) && !kept.Damage.Any(e => e.SourceObjectId == his)
            && !kept.Damage.Any(e => e.SourceObjectId == stranger) && kept.Damage.Any(e => e.SourceObjectId == helper) && kept.Damage.Any(e => e.IsHeal && e.SourceObjectId == healer);
        bool rest = kept.Avoids.Count == 1 && kept.Avoids[0].TargetObjectId == mine && kept.Kills.Count == 1 && kept.Kills[0].VictimObjectId == mine;
        Console.WriteLine($"  -> the own fight stays, the passer-by's goes: {kept.Damage.Count} of {batch.Damage.Count} hits and heals, "
            + $"{kept.Avoids.Count} of 2 avoids, {kept.Kills.Count} of 2 kills ({hits && rest})");

        // another player on the own monster half a minute later still counts; two minutes after the own last hit on it, no longer
        CombatBatch Single(DamageEvent ev) => new(new List<DamageEvent> { ev }, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>());
        bool later = Keep(Single(Hit(helper, mine, 30))).Damage.Count == 1 && Keep(Single(Hit(helper, mine, 120))).Damage.Count == 0;
        Console.WriteLine($"  -> a monster fought half a minute ago is still the fight, one left two minutes ago is not ({later})");
        return hits && rest && later;
    }
}
