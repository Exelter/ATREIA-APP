using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// The CP calculator's Progression tab (<see cref="CpProgression"/>): the guides and stages by Item Level, the Combat Power bands and what the players of a band
/// wear, and the sample character (Program.CpSampleGear: an IL 102 Elyos set, Ludra's Blade at +15 breakthrough 2, Splendent White Dragon Lord earrings at
/// IL 86, no Guard) at 3,010 Item Level against the 2,500 stage: the earrings below its IL 102 tier with the crafted Elyos piece found for them, the empty
/// slots, what is left of the Road to 3,000+ and the character in the bands' terms.
/// </summary>
public static class SelfCheckProgression
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Progression (the website's guides on the character):");
        Aion2CpData.ForgetLiveModel();
        CpModel model = Aion2CpData.Model;

        // the guides by Item Level (two from 2,500 to 3,199), the stages: none below 700, IL 54 from 700 (optional, no Guard), IL 86 from 2,100, from 2,500 the crafted
        // IL 102 weapon, Guard and jewelry with Nuakum armor (IL 86)
        bool guides = string.Join(",", CpProgression.GuidesFor(650).Select(g => g.Id)) == "0"
            && string.Join(",", CpProgression.GuidesFor(2950).Select(g => g.Id)) == "2100,3000"
            && string.Join(",", CpProgression.GuidesFor(3300).Select(g => g.Id)) == "3000"
            && CpProgression.TierFor(699) is null && CpProgression.TierFor(null) is null && CpProgression.TierFor(700) is { Optional: true, Guard: null }
            && CpProgression.TierFor(2100)?.Armor?.ItemLevel == 86
            && CpProgression.TierFor(2600) is { From: 2500, Weapon.ItemLevel: 102, Armor.ItemLevel: 86, Jewelry.Crafted: true };
        Console.WriteLine($"  -> the guides and stages by Item Level (two guides at 2,950, the crafted IL 102 stage from 2,500): {guides}");

        // the bands: all classes at 3,000-3,099 (2,112 characters, the middle 202,298), the meter's Elementalist is the website's Spiritmaster, none below 2,000;
        // what the players wear from 2,500 (44 % with the IL 102 weapon at 3,000-3,199)
        bool bands = CpProgression.BandOf(3010) is { From: 3000, Count: 2112, Middle: 202_298 } && CpProgression.BandOf(3010, "Elementalist") is { From: 3000 } elementalist
            && elementalist == CpProgression.BandOf(3010, "Spiritmaster") && CpProgression.BandOf(1990) is null && CpProgression.BandOf(3050, "Gladiator") is { Count: > 0 }
            && CpProgression.WearOf(3050) is { From: 3000, Weapon.Top: 44.0 } && CpProgression.WearOf(2499) is null && CpProgression.WearOf(4000) is { From: 3400 };
        Console.WriteLine($"  -> Combat Power at 3,000-3,099 (all classes and by class) and what the players there wear: {bands}");

        // the kinds of slot by the item data; an Arcana card's grade by the item list (the id's digit counts down: 810130001 Unique, 810140001 grade 3)
        bool kinds = CpProgression.ArcanaGrade(810130001) == 4 && CpProgression.ArcanaGrade(810140001) == 3 && CpProgression.ArcanaTop(4) == 5
            && CpProgression.KindOf(Aion2CpData.Item(110120003)) == CpGearKind.Weapon && CpProgression.KindOf(Aion2CpData.Items.First(x => x.Category == "Guarder")) == CpGearKind.Guard
            && CpProgression.KindOf(Aion2CpData.Item(210730040)) == CpGearKind.Armor && CpProgression.KindOf(Aion2CpData.Item(310230114)) == CpGearKind.Jewelry
            && CpProgression.KindOf(Aion2CpData.Items.First(x => x.Category == "Bracelet")) == CpGearKind.Other && CpProgression.KindOf(null) == CpGearKind.Other;
        Console.WriteLine($"  -> weapon, Guard, armor (a cloak too), jewelry and the rest told apart, a Unique Arcana card to +5: {kinds}");

        // the sample character at 3,010: Elyos by its gear; the weapon at the tier; the IL 86 earrings Unique, below the IL 102 tier, the Splendent Wise ones (the Elyos
        // name) for them, +16 IL with a Transfer Stone and more Combat Power at their +11; the IL 102 armor past Nuakum's 86; no Guard, one earring and one ring missing
        IReadOnlyList<Aion2EquippedItem> gear = Program.CpSampleGear(17).Concat(Program.CpSampleArcana()).ToList();
        Aion2SavedCharacter c = Program.CpSampleCharacter(gear);
        var input = new CpWholeInput("Gladiator", gear, null, c.Attributes,
            c.Skills.Select(x => new Aion2SkillEntry(x.Id, x.Level, x.BaseLevel, x.Stigma, x.Equipped, x.Stones)).ToList(),
            c.Daevanion.Select(b => new Aion2DaevanionBoard(b.Board, b.Nodes)).ToList());
        CpWholeResult r = CpWhole.Evaluate(input, model);
        CpTotals totals = r.Totals.Marginal;
        char race = CpWhole.RaceOf(gear);
        IReadOnlyList<CpProgPiece> pieces = CpProgression.Read(r.Pieces, 3010, race, totals, model);
        CpProgPiece? weapon = pieces.FirstOrDefault(p => p.Kind == CpGearKind.Weapon);
        CpProgPiece? earring = pieces.FirstOrDefault(p => p.Piece.ItemId == 310230114);
        double? earringCp = earring is null ? null : CpProgression.TargetCp(earring, totals, model);
        string? targetName = earring?.TargetItem is { } target ? Aion2ItemNames.NameOf(target.Id, "en") : null;
        bool read = race == 'L' && weapon is { ItemLevel: 102, AtTier: true, Keeper: true, MaxEnchant: 15, MaxExceed: 5, Piece.Enchant: 15, Piece.Exceed: 2 }
            && earring is { Kind: CpGearKind.Jewelry, ItemLevel: 86, Unique: true, AtTier: false, Keeper: true, Target.ItemLevel: 102, TargetItem: { Level: 102, Category: "Earring" } }
            && targetName is { } name && name.StartsWith("Splendent Wise", StringComparison.Ordinal)
            && earringCp > 0 && CpProgression.TargetItemLevel(earring) == (16, 16)
            && pieces.Where(p => p.Kind == CpGearKind.Armor).Count() == 7 && pieces.Where(p => p.Kind == CpGearKind.Armor).All(p => p.AtTier)
            && CpProgression.Missing(pieces).SequenceEqual(new[] { "SubHand", "Earring", "Ring" });
        Console.WriteLine($"  -> the sample character's pieces against the 2,500 stage: the earrings to {targetName} for {earringCp:0} CP, the empty slots: {read}");

        // a piece the race can't be told for: either race's crafted piece
        bool anyRace = earring?.Piece.Data is { } earringData
            && CpProgression.TargetItem(earringData, CpProgression.TopGear, 'A', 11, 0, totals, model) is { Level: 102 }
            && CpProgression.TargetItem(earringData, CpProgression.TopGear, 'D', 11, 0, totals, model) is { } asmodian
            && Aion2ItemNames.NameOf(asmodian.Id, "en") is { } asmodianName && asmodianName.StartsWith("Splendent Ebony", StringComparison.Ordinal);
        Console.WriteLine($"  -> the Asmodian name for an Asmodian, either for a character whose race isn't known: {anyRace}");

        // the Road to 3,000+: the weapon at +15 and IL 102 done, breakthrough 3 one level (+5 IL) away, no Guard, the earrings the one jewelry piece below IL 102 (two
        // slots empty), the jewelry 12 levels from +15, four empty sockets (up to +16 IL), five Unique Arcana cards with 13 levels left to +5 (two at +5, one
        // at +2, two at +0), the armor 40 levels from +15 and 14 breakthrough levels from 2, 3,000 Item Level reached
        IReadOnlyList<CpRoadTask> road = CpProgression.Road(pieces, 3010, r.Arcana, r.Stigmas, race, totals, model);
        CpRoadTask T(string id) => road.First(t => t.Id == id);
        bool tasks = road.Count == 17 && T("wpn15").Done && T("wpn102").Done && T("il3000") is { Done: true, Left: 0 }
            && T("wpnbt3") is { Done: false, Left: 1, ItemLevel: 5 } && T("wpnbt3").Cp > 0 && T("wpnbt5") is { Left: 3, ItemLevel: 15 }
            && T("grd15") is { Done: false, Missing: 1 } && T("grd102") is { Done: false, Missing: 1 }
            && T("jew102") is { Done: false, Left: 1, ItemLevel: 16, Missing: 2 } && T("jew102").Cp > 0 && T("jew15") is { Done: false, Left: 12, ItemLevel: 12, Missing: 2 }
            && T("stones") is { Done: false, Left: 4, ItemLevel: 16 } && T("arc5") is { Done: true, Left: 0 } && T("arcmax") is { Done: false, Left: 13, ItemLevel: 13 }
            && T("arm15") is { Done: false, Left: 40, ItemLevel: 40 } && T("arm15").Cp > 0 && T("armbt") is { Done: false, Left: 14, ItemLevel: 70 } && T("armbt").Cp > 0
            && T("stig20").Left >= 0;
        Console.WriteLine($"  -> the Road to 3,000+ from the sample character ({road.Count(t => t.Done)} of {road.Count} done, the IL 102 earrings {T("jew102").Cp:0} CP): {tasks}");

        // the character in the bands' terms: the weapon +15 breakthrough 2 at IL 102, the seven armor pieces at IL 102, two of the three jewelry pieces, no Guard,
        // the Arcana cards at +2.4 on average
        CpYou you = CpProgression.You(pieces, r.Arcana, r.Stigmas, r.Boards);
        bool youOk = you.Weapon is { Enchant: 15.0, Breakthrough: 2.0, Top: 1, Count: 1, ItemLevel: 102 } && you.Guard is null && you.Armor is { Top: 7, Count: 7 }
            && you.Jewelry is { Top: 2, Count: 3, ItemLevel: 86 } && you.Arcana is double cards && Math.Abs(cards - 2.4) < 1e-9 && you.Nodes >= 0 && you.Stigmas >= 0;
        Console.WriteLine($"  -> the sample character against the band (Arcana +{you.Arcana:0.0}, {you.Nodes} Daevanion nodes, Stigma levels {you.Stigmas}): {youOk}");

        return guides && bands && kinds && read && anyRace && tasks && youOk;
    }
}
