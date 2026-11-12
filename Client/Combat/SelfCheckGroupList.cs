using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// The meter's group list goes when a dungeon or a boss fight begins after it opened, whoever opened it, and stays when opened in there or when the
/// player reads a search or a card (12 November 2026, the owner: "why does this stay again, in dungeons. It should stop"). See <see cref="GroupListRule"/>.
/// </summary>
public static class SelfCheckGroupList
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Group list:");
        DateTime t0 = new(2026, 11, 12, 20, 0, 0), never = DateTime.MinValue;
        DateTime At(double seconds) => t0.AddSeconds(seconds);

        // opened in town when the party formed (by itself or by the player), then the party walks into the dungeon: it goes
        var rule = new GroupListRule();
        rule.Note(At(0), false, never);
        DateTime opened = At(1);
        bool staysOutside = !rule.Folds(opened, true);
        rule.Note(At(30), true, never);
        bool goesInside = rule.Folds(opened, true);

        // opened again inside (the player wants it there): it stays, also while the monsters' answer is missing for a while between packs
        DateTime openedInside = At(40);
        rule.Note(At(60), false, never);
        rule.Note(At(80), false, never);
        bool staysOpenedInside = !rule.Folds(openedInside, true) && rule.Inside;

        // the boss is pulled: the list opened before goes, one opened during the fight stays, the next fight takes that one too
        rule.Note(At(100), true, At(99.5));
        bool goesAtBoss = rule.Folds(openedInside, true);
        DateTime openedInFight = At(110);
        rule.Note(At(115), true, At(114.5));
        bool staysOpenedInFight = !rule.Folds(openedInFight, true);
        rule.Note(At(200), true, At(130));          // 70 s without a blow: the fight is over
        bool overAfter = rule.FightSince == never;
        rule.Note(At(260), true, At(259));          // the next boss
        bool goesNextFight = rule.Folds(openedInFight, true);

        // a search's results or a character's card stay through all of it
        bool readStays = !rule.Folds(opened, false) && !rule.Folds(openedInFight, false);

        // out of the dungeon for over a minute: out; nothing begun after it opened, so a list opened then stays
        var outside = new GroupListRule();
        outside.Note(At(0), true, never);
        outside.Note(At(30), false, never);
        bool stillInsideAfterGap = outside.Inside;
        outside.Note(At(100), false, never);
        bool outAfterMinute = !outside.Inside;
        bool staysInTown = !outside.Folds(At(101), true);

        bool all = staysOutside && goesInside && staysOpenedInside && goesAtBoss && staysOpenedInFight && overAfter && goesNextFight && readStays
                   && stillInsideAfterGap && outAfterMinute && staysInTown;
        Console.WriteLine($"  -> opened in town: stays there ({staysOutside}), goes in the dungeon ({goesInside}); opened inside stays ({staysOpenedInside}); " +
            $"a boss pulled after it opened takes it ({goesAtBoss}), opened in the fight it stays ({staysOpenedInFight}) until the next fight ({overAfter}, {goesNextFight}); " +
            $"a search or a card stays ({readStays}); a short gap is still inside ({stillInsideAfterGap}), a minute out is out ({outAfterMinute}, {staysInTown})");
        return all;
    }
}
