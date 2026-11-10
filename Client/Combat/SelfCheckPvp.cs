using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The fights with players (Aion2/Aion2Pvp.cs): a death recap - the burst that killed (an older hit left out), each attacker with his share and skills, the
/// killing blow, the heals in it, the seconds - and the rivals: a fight counted once per five minutes, a death of the own character for every player in it (the
/// last blow: killed by), a kill when another player dies with the own hits in his last seconds.
/// </summary>
public static class SelfCheckPvp
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Fights with players (death recap, rivals):");
        Aion2Pvp.ResetForCheck();
        const int me = 1, kraeger = 2, morrigane = 3, mob = 4, healer = 5;
        DateTime t = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Local);
        string Name(int id) => id switch { me => "Lumiel", kraeger => "Kraeger", morrigane => "Morrigane", mob => "Balaur Warrior", healer => "Seliora", _ => "#" + id };
        string? Class(int id) => id switch { kraeger => "Templar", morrigane => "Sorcerer", healer => "Cleric", me => "Chanter", _ => null };
        bool Player(int id) => id is me or kraeger or morrigane or healer;
        int Server(int id) => id is kraeger or morrigane ? 2303 : 1303;

        var hits = new List<DamageEvent>
        {
            new(t.AddSeconds(-20), mob, me, 900, false, "Cleave"),                         // long before the burst: left out
            new(t.AddSeconds(-4), kraeger, me, 5_000, false, "Shield Bash"),               // a fight with Kraeger begins
            new(t.AddSeconds(-3), morrigane, me, 3_000, false, "Flame Bolt", IsCritical: true),
            new(t.AddSeconds(-2.5), healer, me, 1_200, true, "Healing Light"),
            new(t.AddSeconds(-2), me, kraeger, 2_000, false, "Word of Wind"),
            new(t.AddSeconds(-1.5), kraeger, me, 4_000, false, "Punishing Blow"),
            new(t.AddSeconds(-1), mob, me, 500, false, "Cleave"),
            new(t, kraeger, me, 6_000, false, "Shield Bash"),                              // the killing blow
        };
        DeathRecap? recap = null;
        void OnDied(DeathRecap r) => recap = r;
        Aion2Pvp.Died += OnDied;
        try
        {
            Aion2Pvp.Feed(hits, new[] { (me, t) }, me, Name, Class, Player, Server, _ => null, _ => false);
        }
        finally
        {
            Aion2Pvp.Died -= OnDied;
        }

        bool burst = recap is { Taken: 18_500, Healed: 1_200 } && Math.Abs(recap.Seconds - 4) < 0.01;
        bool killer = recap?.Killer is { Name: "Kraeger", Amount: 15_000, Player: true } k && k.Skills[0] is { Name: "Shield Bash", Hits: 2, Amount: 11_000 }
            && recap.KillingSkill == "Shield Bash" && recap.KillingAmount == 6_000;
        bool order = recap?.Sources.Select(s => s.Name).SequenceEqual(new[] { "Kraeger", "Morrigane", "Balaur Warrior" }) == true && recap.Sources[1].Skills[0].Crits == 1;

        // the rivals: Kraeger and Morrigane each count the death, Kraeger killed; a minute later Kraeger dies with the own hits on him: a kill, the same fight
        Rival? kr = Aion2Pvp.RivalOf("Kraeger", 2303), mo = Aion2Pvp.RivalOf("Morrigane", 2303);
        bool rivals = kr is { Met: 1, Deaths: 1, KilledBy: 1, Kills: 0, ClassName: "Templar" } && mo is { Met: 1, Deaths: 1, KilledBy: 0 } && Aion2Pvp.RivalOf("Balaur Warrior", 1303) is null;
        Aion2Pvp.Feed(new[] { new DamageEvent(t.AddSeconds(60), me, kraeger, 9_000, false, "Thunderbolt") }, new[] { (kraeger, t.AddSeconds(61)) }, me, Name, Class, Player, Server,
            _ => null, _ => false);
        Rival? after = Aion2Pvp.RivalOf("Kraeger", 2303);
        bool kill = after is { Kills: 1, Met: 1 };
        Aion2Pvp.Feed(new[] { new DamageEvent(t.AddMinutes(20), kraeger, me, 1_000, false, "Shield Bash") }, Array.Empty<(int, DateTime)>(), me, Name, Class, Player, Server,
            _ => null, _ => false);
        bool again = Aion2Pvp.RivalOf("Kraeger", 2303) is { Met: 2 };
        Aion2Pvp.ResetForCheck();
        Console.WriteLine($"  -> the recap: 18,500 taken in 4 s (the older hit left out), 1,200 healed: {burst}; Kraeger killed with Shield Bash (6,000), his two Shield Bashes first: {killer}; "
            + $"the attackers by damage, a crit counted: {order}");
        Console.WriteLine($"  -> rivals: the death counts for both players, killed by Kraeger, no monster: {rivals}; Kraeger dying with the own hits: a kill: {kill}; 20 minutes later a new fight: {again}");
        return burst && killer && order && rivals && kill && again;
    }
}
