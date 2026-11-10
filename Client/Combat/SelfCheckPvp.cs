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
        var hostile = new HashSet<int>();
        void OnDied(DeathRecap r) => recap = r;
        Aion2Pvp.Died += OnDied;
        try
        {
            Aion2Pvp.Feed(hits, new[] { (me, t) }, me, Name, Class, Player, Server, _ => null, _ => false, id => hostile.Add(id));
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
        // Morrigane dies with an own hit in it, the healer's the last blow: an assist. The tally: one kill (Kraeger, the own last blow), one assist, one death
        Aion2Pvp.Feed(new[] { new DamageEvent(t.AddMinutes(21), me, morrigane, 1_000, false, "Thunderbolt"), new DamageEvent(t.AddMinutes(21).AddSeconds(1), healer, morrigane, 5_000, false, "Smite") },
            new[] { (morrigane, t.AddMinutes(21).AddSeconds(2)) }, me, Name, Class, Player, Server, _ => null, _ => false);
        bool tally = Aion2Pvp.Tally == (1, 1, 1);
        // the players who traded hits with the own character are enemies, whatever their faction (the healer and the monster are not)
        bool enemies = hostile.SetEquals(new[] { kraeger, morrigane });
        Aion2Pvp.ResetForCheck();
        Console.WriteLine($"  -> the recap: 18,500 taken in 4 s (the older hit left out), 1,200 healed: {burst}; Kraeger killed with Shield Bash (6,000), his two Shield Bashes first: {killer}; "
            + $"the attackers by damage, a crit counted: {order}");
        Console.WriteLine($"  -> rivals: the death counts for both players, killed by Kraeger, no monster: {rivals}; Kraeger dying with the own hits: a kill: {kill}; 20 minutes later a new fight: {again}");
        Console.WriteLine($"  -> kills, assists, deaths 1 / 1 / 1 (the healer's last blow an assist): {tally}; the players who traded hits are enemies, the healer and the monster not: {enemies}");
        bool radar = Radar();
        return burst && killer && order && rivals && kill && again && tally && enemies && radar;
    }

    /// <summary>
    /// The Abyss radar's picture of who is around (11 November 2026): an enemy is a player of the other faction or one who traded hits with the own character
    /// (in the Abyss most came with no server, some with no faction at all); the own faction, with no server of its own known, is the other side of such a
    /// player; each placed in metres east and south of the own character, who is within 25 m counted.
    /// </summary>
    private static bool Radar()
    {
        const int me = 10, hitter = 11, ally = 12, other = 13, nameless = 14, stranger = 15;
        var dir = new Aion2EntityDirectory { LocalPlayerId = me };
        DateTime now = DateTime.Now;
        foreach ((int id, string name) in new[] { (me, "Lumiel"), (hitter, "Kraeger"), (ally, "Seliora"), (other, "Vesh"), (nameless, "Player #54274"), (stranger, "Orrin") })
        {
            dir.Register(id, name);
            if (id != me)
            {
                dir.NotePlayerInView(id, now);
            }
        }

        dir.NoteFaction(hitter, 1);   // Elyos, hit the own character: so the own side is Asmodian
        dir.NoteFaction(ally, 2);
        dir.NoteFaction(other, 1);
        dir.NoteHostile(hitter);
        dir.NoteHostile(nameless);    // no faction at all, but traded hits
        dir.NoteLocalPosition(50_000, 80_000);
        dir.NoteAround(hitter, 51_000, 80_000);      // 10 m east
        dir.NoteAround(ally, 50_000, 80_500);        // 5 m south
        dir.NoteAround(other, 50_000, 77_000);       // 30 m north
        dir.NoteAround(nameless, 58_000, 80_000);    // 80 m east: on the rim
        Ui.RadarView view = Ui.RadarView.Of(dir, 200_000, now, (_, _) => null, (2, 3, 1));
        bool sides = view.Faction == "Asmodian" && view.Enemies.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "Kraeger", "Player #54274", "Vesh" })
            && view.Allies == 1 && view.InView == 5 && view.NoFaction == 2;
        Ui.RadarDot? Dot(int id) => view.Dots.FirstOrDefault(d => d.Id == id);
        bool placed = view.Placed && view.Dots.Count == 4 && Dot(hitter) is { Side: Ui.RadarSide.Enemy } h && Math.Abs(h.East - 10) < 0.5 && Math.Abs(h.South) < 0.5
            && Dot(ally) is { Side: Ui.RadarSide.Ally, South: > 4.5 and < 5.5 } && Dot(other) is { South: < -29.5 and > -30.5 } && Dot(stranger) is null;
        bool near = view.EnemiesNear == 1 && view.AlliesNear == 1 && view.Tally == (2, 3, 1);
        Console.WriteLine($"  -> the radar: enemies by faction and by hits, the own side from them: {sides}; placed in metres around you: {placed}; within 25 m 1 enemy, 1 ally: {near}");
        return sides && placed && near;
    }
}
