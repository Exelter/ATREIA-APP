using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// The own Combat Power, Item Level and worn pieces while playing (12 November 2026, a player: "my cp ingame does not match on the atreia app sometimes when
/// I upgrade"; the owner: "show their gear live as it changes"). The frames are the game's own, from the recording of 11 to 12 November 2026 (YIELD): the
/// three Combat Power frames of a login (the last is the character's number), an enchant that failed and the Item Level of then, and the weapon going from
/// +18 to +19 in the inventory change the game sent right after the enchant.
/// </summary>
public static class SelfCheckOwnFigures
{
    // a login's three Combat Power frames (139,700 and 156,224 are partial counts; 190,050 the legion list's number of that login)
    private const string LoginFirst = "5636b4210200000000007d95030000000000";
    private const string LoginSecond = "563640620200000000007d95030000000000";
    private const string LoginLast = "563662e60200000000007d95030000000000";
    private const string Failed = "563676e50200000000007d95030000000000"; // 189,814 after a failed enchant
    private const string Equalized = "5636ac0f0600000000007d95030000000000"; // 397,228: the 10 against 10 arena's number for everybody
    private const string ItemLevelDown = "1d56940b0000970b0000"; // 2,964 now, 2,967 the highest
    private const string ItemLevelBack = "1d56970b0000970b0000";

    /// <summary>The weapon's entry of the login list (the game's id of the piece, the item, worn in slot 1, made by YIELD, +18, its stones and stats).</summary>
    private const string WeaponAt18 = "01a44a0f000068000059fa910601000000000000000b01000000055949454c440000000000000000120000000090010000000000000092000000010404bc7a1ec7000204bc7a1e68000104bc7a1e85000104bc7a1e800001880e0300000005090403aa423001000000000000000000040500200000009200c40300001a0112050000010020000000040500000000009200000000001a01000000000100000000000180c4ba00010180c4ba0000013d01330000";

    /// <summary>The inventory change right after the enchant (four entries: materials, kinah, a stone, and the weapon at +19).</summary>
    private const string WeaponTo19 = "1b5604020011eb0c0000430000213370370000000000000000014c00000000000000000000000000000a0001000b1b0200009b00003f3370372e523c00000000000100000000000000000000000000000a00010011b4070000530000519fc91e0600000000000000010a000000000000000000000000000a000101a44a0f000068000059fa910601000000000000000b01000000055949454c44000000000000000013000000000000000000000000009700000000010404bc7a1ec7000204bc7a1e68000104bc7a1e85000104bc7a1e800001880e0300000005090403aa423001000000000000000000040500200000009200c40300001a0112050000010020000000040500000000009200000000001a01000000000100000000000180c4ba00010180c4ba0000013d01330000000000000001";

    /// <summary>Not a frame of the game: the weapon's entry under its id, put into the bag (place 01, slot 76) - the piece taken off.</summary>
    private const string WeaponOff = "1b56010100a44a0f000068000059fa91060100000000000000014c0000000000000000000000000000000000000000";

    public static bool Run()
    {
        Console.WriteLine("[selftest] The own Combat Power, Item Level and gear while playing:");
        bool ok = true;
        var at = new DateTime(2026, 11, 12, 8, 29, 41, DateTimeKind.Utc);

        var directory = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), directory);
        directory.SetLocalCharacter(new Aion2CharacterInfo(4711, "Selftester", 11, 45, Array.Empty<Aion2EquippedItem>(), at.AddMinutes(-30), ServerId: 2302));

        Feed(decoder, LoginFirst, at);
        Feed(decoder, LoginSecond, at);
        Feed(decoder, LoginLast, at);
        bool login = directory.OwnLiveCombatPower == 190050 && Ui.MainWindow.OwnCombatPower(directory) == 190050;
        Console.WriteLine($"  -> the last of a login's three is the Combat Power: {login} ({directory.OwnLiveCombatPower})");
        ok &= login;

        Feed(decoder, Failed, at.AddSeconds(20));
        bool changed = Ui.MainWindow.OwnCombatPower(directory) == 189814;
        Feed(decoder, Equalized, at.AddSeconds(25));
        bool equalized = directory.OwnLiveCombatPower == 189814;
        Console.WriteLine($"  -> a change shows at once, the arena's number for everybody is left out: {changed && equalized} ({directory.OwnLiveCombatPower})");
        ok &= changed && equalized;

        Feed(decoder, ItemLevelDown, at.AddSeconds(20));
        bool down = directory.OwnLiveItemLevel == 2964 && Ui.MainWindow.OwnItemLevel(directory) == 2964;
        Feed(decoder, ItemLevelBack, at.AddSeconds(30));
        bool back = directory.OwnLiveItemLevel == 2967;
        Console.WriteLine($"  -> the Item Level follows the game: {down && back} ({directory.OwnLiveItemLevel})");
        ok &= down && back;

        // back at the character selection: the game's character list (its content does not matter here)
        var list = new byte[48];
        list[0] = 0x26;
        list[1] = 0x36;
        decoder.Decode(list, at.AddMinutes(2));
        bool forgotten = directory.OwnLiveCombatPower is null && directory.OwnLiveItemLevel is null;
        Console.WriteLine($"  -> the character selection ends the character's numbers: {forgotten}");
        ok &= forgotten;

        // a second character logs in (its numbers a few seconds before its record), a third one ten minutes later without any
        Feed(decoder, "56367df1010000000000df00030000000000", at.AddMinutes(5)); // 127,357 (the second character of that recording)
        directory.SetLocalCharacter(new Aion2CharacterInfo(4712, "Second", 11, 45, Array.Empty<Aion2EquippedItem>(), at.AddMinutes(5).AddSeconds(7), ServerId: 2302));
        bool kept = directory.OwnLiveCombatPower == 127357;
        directory.SetLocalCharacter(new Aion2CharacterInfo(4713, "Third", 11, 45, Array.Empty<Aion2EquippedItem>(), at.AddMinutes(15), ServerId: 2302));
        bool dropped = directory.OwnLiveCombatPower is null;
        Console.WriteLine($"  -> a login's numbers go to the character named right after, never to the next one: {kept && dropped}");
        ok &= kept && dropped;

        // the gear: the login list, then the weapon enchanted (+18 -> +19) and taken off
        var gear = new Aion2EntityDirectory();
        var gearDecoder = new Aion2FrameDecoder(Aion2Protocol.Load(), gear);
        Feed(gearDecoder, "115600" + WeaponAt18, at);
        Feed(gearDecoder, WeaponTo19, at.AddMinutes(1));
        bool unnamed = Enchant(gear) == 18; // nothing is merged before the game named the character in this run
        gear.SetLocalCharacter(new Aion2CharacterInfo(4711, "Selftester", 11, 45, Array.Empty<Aion2EquippedItem>(), at.AddMinutes(2), ServerId: 2302));
        Feed(gearDecoder, WeaponTo19, at.AddMinutes(3));
        Aion2EquippedItem? weapon = gear.LocalEquipment.FirstOrDefault(i => i.SlotIndex == 1);
        bool enchanted = weapon is { ItemId: 110230105, Enchant: 19, Stones.Count: 4 } && gear.LocalEquipment.Count == 1;
        Console.WriteLine($"  -> an enchant shows on the piece at once (+19, its stones kept), not before the character is named: {enchanted && unnamed}");
        ok &= enchanted && unnamed;

        Feed(gearDecoder, WeaponOff, at.AddMinutes(4));
        bool off = gear.LocalEquipment.All(i => i.SlotIndex != 1);
        Console.WriteLine($"  -> a piece put into the bag leaves its slot: {off}");
        ok &= off;

        return ok;
    }

    private static void Feed(Aion2FrameDecoder decoder, string hex, DateTime at) => decoder.Decode(Convert.FromHexString(hex), at);

    private static int? Enchant(Aion2EntityDirectory directory) => directory.LocalEquipment.FirstOrDefault(i => i.SlotIndex == 1)?.Enchant;
}
