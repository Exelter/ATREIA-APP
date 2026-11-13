using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat.Sources;
using AionDPS.Data;
using AionDPS.Upload;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The scenarios of <see cref="SelfCheckAion2"/> that use the Windows app's own parts (its windows, hotkeys and overlays), and the full list.</summary>
public static partial class SelfCheckAion2
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunCombatSourceSeamScenario();
        ok &= RunTcpReassemblerScenario();
        ok &= RunTcpReassemblerPaddingScenario();
        ok &= RunGameFlowDetectorScenario();
        ok &= RunEffectAuraScenario();
        ok &= RunNetworkStatsScenario();
        ok &= RunRelayedConnectionScenario();
        ok &= RunAion2ProtocolScenario();
        ok &= RunAion2RealCaptureScenario();
        ok &= RunAion2BundleScenario();
        ok &= RunAion2NoDamageFrameScenario();
        ok &= RunAion2SummonOwnerScenario();
        ok &= RunAion2NamedSummonScenario();
        ok &= RunAion2RosterReplacesPartyScenario();
        ok &= RunAion2LeftRoomScenario();
        ok &= RunAion2OwnTargetScenario();
        ok &= RunAion2RosterFiguresScenario();
        ok &= RunAion2GuildScenario();
        ok &= RunAion2PartyByClassScenario();
        ok &= RunAion2AccentAndLeftoverSummonScenario();
        ok &= RunAion2RosterHomeServerScenario();
        ok &= RunAion2RosterBogusServerScenario();
        ok &= RunAion2ViewedServerScenario();
        ok &= RunAion2SpiritBasicAttackScenario();
        ok &= RunAion2ShieldIsNoSummonScenario();
        ok &= RunAion2TwoSorcerersScenario();
        ok &= RunAion2DotTickScenario();
        ok &= RunAion2HitPointsScenario();
        ok &= RunHpCheckScenario();
        ok &= RunAion2BossSelfHealScenario();
        ok &= RunAion2DebuffHitsScenario();
        ok &= RunAion2SoloLocalNameScenario();
        ok &= RunAion2NamesScenario();
        ok &= RunAion2MidStreamScenario();
        ok &= RunAion2CharacterScenario();
        ok &= RunAion2LoginListsScenario();
        ok &= RunAion2OwnProfileExtrasScenario();
        ok &= RunAion2ItemDetailsScenario();
        ok &= RunAion2CraftedPiecesScenario();
        ok &= RunAion2ArcanaScenario();
        ok &= RunAion2PantheonScenario();
        ok &= RunAion2SpeciesScenario();
        ok &= RunAion2CharacterStoreScenario();
        ok &= RunAion2PresetScenario();
        ok &= RunAion2SeenProfileScenario();
        ok &= RunRestartSnapshotScenario();
        ok &= RunAion2BossSpawnScenario();
        ok &= RunAion2SpawnHitPointsScenario();
        ok &= RunAion2BossRealMaxScenario();
        ok &= RunUploadSpawnMaxScenario();
        ok &= RunEqualizedArenaScenario();
        ok &= RunSeenStatsScenario();
        ok &= RunArenaRefillScenario();
        ok &= RunArenaUploadNamesScenario();
        ok &= RunClassCatalogScenario();
        ok &= RunSkillNameLanguageScenario();
        ok &= RunSettingsMigrationScenario();
        ok &= RunHotkeyBindingScenario();
        return ok;
    }

    /// <summary>The arcana of the login list, cut out of the real frame 0x1156 of 2026-10-26 (Aahz) and checked against the five in-game
    /// windows of that day: Kompass of Vigor +2 with 600 of 12.000 experience, god stat Vaizel 22, skill bonuses Klingenraserei +1,
    /// Schockaufhebung +2, Zerreissender Schlag +1, Zerschmetternde Welle +2 (the piece's own level and the rolled extra added up); Spiegel of
    /// Vigor +2 with 3.800, Kaisinel 22, Toedliche Eruption +1, Erfahrener Konterangriff +3, Schocktreffer +1, Schwaechenanalyse +1.</summary>
    private static bool RunAion2ArcanaScenario()
    {
        Console.WriteLine("[selftest] Aion 2 arcana details (experience, god stat, skill bonuses):");
        byte[] body = Convert.FromHexString("1156" + "91a74c3001000000000000000b2b000000000000000000000000000258020000000000000000000000520000002b000000000000000000000300000000000000000000000001080016000000000004801eac000160d0ab0001d0ffa70001109ca8000104801eac000060d0ab0001d0ffa70000109ca80001000000014478130000a2" + "d1b44f3001000000000000000b2d00000000000000000000000002d80e0000000000000000000000520000002d000000000000000000000300000000000000000000000001090016000000000004c00db40001a0bfb300018071b300016023b3000104c00db40000a0bfb300028071b300006023b30000000000014571030000a900");
        var wire = new List<byte>();
        int length = body.Length + 4;
        while (length >= 0x80)
        {
            wire.Add((byte)(length & 0x7f | 0x80));
            length >>= 7;
        }

        wire.Add((byte)length);
        wire.AddRange(body);
        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        source.Ingest(Segment(500, wire.ToArray()));
        var entities = (Aion2EntityDirectory)source.Entities;
        string Bonuses(Aion2EquippedItem? i) => string.Join(" ", i?.Arcana?.SkillBonuses.Select(k => $"{k.SkillId}+{k.Level}") ?? Array.Empty<string>());
        Aion2EquippedItem? compass = entities.LocalEquipment.FirstOrDefault(i => i.SlotIndex == 43);
        Aion2EquippedItem? mirror = entities.LocalEquipment.FirstOrDefault(i => i.SlotIndex == 45);
        bool compassOk = compass is { ItemId: 810330001, Enchant: 2, Arcana: { EnchantExp: 600, GodStatId: 8, GodValue: 22 } }
            && Bonuses(compass) == "11280000+1 11260000+2 11010000+1 11050000+2";
        bool mirrorOk = mirror is { ItemId: 810530001, Enchant: 2, Arcana: { EnchantExp: 3800, GodStatId: 9, GodValue: 22 } }
            && Bonuses(mirror) == "11800000+1 11780000+3 11760000+1 11740000+1";
        Console.WriteLine($"  -> Kompass +2: 600 experience, Vaizel 22, four skill bonuses: {compassOk}");
        Console.WriteLine($"  -> Spiegel +2: 3.800 experience, Kaisinel 22, four skill bonuses: {mirrorOk}");

        // the login of 2026-10-29 sent some entries with one more zero byte between the size field and the slot index: the compass again, shifted
        int sizeAt = body.AsSpan().IndexOf(new byte[] { 0x52, 0, 0, 0, 0x2b });
        byte[] shiftedBody = body[..(sizeAt + 4)].Concat(new byte[] { 0 }).Concat(body[(sizeAt + 4)..]).ToArray();
        var shiftedWire = new List<byte>();
        int shiftedLength = shiftedBody.Length + 4;
        while (shiftedLength >= 0x80)
        {
            shiftedWire.Add((byte)(shiftedLength & 0x7f | 0x80));
            shiftedLength >>= 7;
        }

        shiftedWire.Add((byte)shiftedLength);
        shiftedWire.AddRange(shiftedBody);
        using var shiftedSource = new Aion2PacketCombatSource(Aion2Protocol.Load());
        shiftedSource.Ingest(Segment(500, shiftedWire.ToArray()));
        Aion2EquippedItem? shiftedCompass = ((Aion2EntityDirectory)shiftedSource.Entities).LocalEquipment.FirstOrDefault(i => i.SlotIndex == 43);
        bool shiftedOk = shiftedCompass is { ItemId: 810330001, Enchant: 2, Arcana: { EnchantExp: 600, GodStatId: 8, GodValue: 22 } }
            && Bonuses(shiftedCompass) == "11280000+1 11260000+2 11010000+1 11050000+2";
        Console.WriteLine($"  -> Kompass with one extra zero byte behind the size field is read the same: {shiftedOk}");

        // the file on disk keeps them; an entry saved by an older meter has none
        var saved = JsonSerializer.Deserialize<Aion2SavedCharacter.SavedItem>(JsonSerializer.Serialize(new Aion2SavedCharacter.SavedItem(43, 810330001, 2, Arcana: compass?.Arcana)));
        var older = JsonSerializer.Deserialize<Aion2SavedCharacter.SavedItem>("""{"Slot":43,"ItemId":810330001,"Enchant":2}""");
        bool disk = saved?.Arcana is { EnchantExp: 600, GodValue: 22 } && saved.Arcana.SkillBonuses.Count == 4 && older is { Arcana: null };
        Console.WriteLine($"  -> saved with the character file and read back (an older entry without them still loads): {disk}");

        // the profile upload carries them (own character only, camelCase like every upload field); a piece without them sends none
        var uploadJson = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        string sent = JsonSerializer.Serialize(AionDPS.Ui.MainWindow.GearUpload(compass!), uploadJson);
        string plainSent = JsonSerializer.Serialize(AionDPS.Ui.MainWindow.GearUpload(new Aion2EquippedItem(1, 110150026, 3)), uploadJson);
        bool upload = sent.Contains("\"arcana\":{\"exp\":600,\"god\":8,\"godValue\":22,\"skillBonuses\":[{\"skill\":11280000,\"level\":1},{\"skill\":11260000,\"level\":2},{\"skill\":11010000,\"level\":1},{\"skill\":11050000,\"level\":2}]}")
            && !plainSent.Contains("arcana");
        Console.WriteLine($"  -> the profile upload carries experience, god stat and skill bonuses of an arcana piece, and nothing for other pieces: {upload}");
        return compassOk && mirrorOk && shiftedOk && disk && upload;
    }

    private static bool RunEqualizedArenaScenario()
    {
        Console.WriteLine("[selftest] Players first met in the 10 against 10 arena go online with name only:");
        var dir = new Aion2EntityDirectory();
        DateTime t0 = new(2026, 10, 22, 19, 38, 0, DateTimeKind.Utc);
        dir.NoteZone(1010);
        dir.NoteMet("Team", t0.AddSeconds(15));       // announced two seconds before the zone frame
        dir.NoteMet("Hub", t0);                       // a hub player seen long before
        dir.NoteZone(62);
        dir.NoteEqualizedArenaEntered(t0.AddSeconds(17));
        dir.NoteMet("Red", t0.AddSeconds(23));        // announced inside the arena
        bool marked = dir.MetInEqualizedArena("Team") && dir.MetInEqualizedArena("Red") && !dir.MetInEqualizedArena("Hub");
        dir.NoteZone(1010);
        dir.NoteMet("Red", t0.AddMinutes(30));        // seen again at home: the real data of him can go online
        bool cleared = !dir.MetInEqualizedArena("Red") && dir.MetInEqualizedArena("Team");
        AionDPS.Upload.ProfileUpload plain = AionDPS.Ui.MainWindow.NameOnlyProfile();
        bool empty = plain.Level is null && plain.ClassId is null && plain.Gear.Count == 0 && plain.Skills.Count == 0 && plain.Daevanion.Count == 0 && plain.GearScore is null;
        Console.WriteLine($"  -> met in the arena marked (before the zone frame and inside), a hub player not: {marked}");
        Console.WriteLine($"  -> seen again outside the arena unmarked: {cleared}");
        Console.WriteLine($"  -> the profile that goes online has no level, class, gear, skills or boards: {empty}");
        return marked && cleared && empty;
    }

    /// <summary>
    /// The game through a ping reducer on this PC (12 November 2026, the owner: "why does the network analysis show 0?" - ExitLag on 127.0.0.1:61660): no ping
    /// from its instant acknowledgements, a new connection to it is a server change; reconnects count per session; only the game's ports reach the meter.
    /// </summary>
    private static bool RunRelayedConnectionScenario()
    {
        Console.WriteLine("[selftest] Network analysis through a ping reducer or VPN on the PC, and only the game's traffic:");
        string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aiondps-selftest-network-relayed.log");
        NetworkLog.PathOverride = logFile;
        System.IO.File.Delete(logFile);
        NetworkStats.Reset();
        DateTime t0 = DateTime.Now.AddSeconds(-5);
        const string proxy = "127.0.0.1:61660", game = "127.0.0.1:50100";
        NetworkStats.Note(new TcpSegment(t0, game, proxy, 5000, new byte[100], false));
        NetworkStats.Note(new TcpSegment(t0.AddMilliseconds(1), proxy, game, 9000, new byte[0], true, 5100)); // the program on the PC acknowledges at once
        NetworkStats.Note(new TcpSegment(t0.AddSeconds(1), game, proxy, 5100, new byte[50], false));
        NetworkStats.Note(new TcpSegment(t0.AddSeconds(1).AddMilliseconds(1), proxy, game, 9000, new byte[200], true, 5150));
        NetworkSnapshot relayed = NetworkStats.Snapshot();
        bool noPing = relayed.Relayed && relayed.PingMs is null && relayed.PingAverageMs is null && relayed.JitterMs is null;
        Console.WriteLine($"  -> a connection to 127.0.0.1 is relayed, and its instant acknowledgements give no ping (not 0 ms): {noPing}");

        // the next zone through the same program: the same local address with another port of the game, a server change and not a reconnect
        NetworkStats.Note(new TcpSegment(t0.AddSeconds(2), proxy, "127.0.0.1:50101", 1, new byte[10], true, 0));
        NetworkSnapshot zone = NetworkStats.Snapshot();
        bool zoneChange = zone.Switches == 1 && zone.Reconnects == 0 && zone.SinceLastReconnect is null;
        Console.WriteLine($"  -> a new connection to the same program on the PC is a server change, not a reconnect: {zoneChange}");

        // straight to a server: a reconnect is counted and dated; a new session (after 30 s of silence) starts the counts again
        NetworkStats.Reset();
        DateTime t1 = DateTime.Now.AddSeconds(-50);
        NetworkStats.Note(new TcpSegment(t1, "193.0.2.10:13328", "192.168.0.2:50000", 1, new byte[10], true, 0));
        NetworkStats.Note(new TcpSegment(t1.AddSeconds(1), "193.0.2.10:13328", "192.168.0.2:50001", 1, new byte[10], true, 0));
        NetworkSnapshot reconnected = NetworkStats.Snapshot();
        bool counted = !reconnected.Relayed && reconnected.Reconnects == 1 && reconnected.SinceLastReconnect is { } ago && ago.TotalSeconds > 40;
        NetworkStats.Note(new TcpSegment(t1.AddSeconds(45), "193.0.2.10:13328", "192.168.0.2:50002", 1, new byte[10], true, 0));
        NetworkSnapshot fresh = NetworkStats.Snapshot();
        bool perSession = fresh.Reconnects == 0 && fresh.Switches == 0 && fresh.SinceLastReconnect is null;
        Console.WriteLine($"  -> the same server again is a reconnect, with its time: {counted}");
        Console.WriteLine($"  -> after 30 s of silence a new session starts with no reconnects and no server changes: {perSession}");

        bool local = NetworkStats.IsLocalAddress("127.0.0.1:61660") && NetworkStats.IsLocalAddress("192.168.0.3:63045") && NetworkStats.IsLocalAddress("10.8.0.1:13328")
            && NetworkStats.IsLocalAddress("172.20.1.1:13328") && NetworkStats.IsLocalAddress("::1:61660") && !NetworkStats.IsLocalAddress("193.202.112.113:13328")
            && !NetworkStats.IsLocalAddress("172.32.0.1:13328") && !NetworkStats.IsLocalAddress("104.19.193.29:443") && !NetworkStats.IsLocalAddress(null);
        Console.WriteLine($"  -> addresses on the PC or the local network told from servers on the internet: {local}");

        int[] ports = { 13328 };
        bool keeps = !NpcapCaptureService.Keeps(ports, false, 443, 50000) && !NpcapCaptureService.Keeps(ports, false, 50000, 443)
            && NpcapCaptureService.Keeps(ports, false, 13328, 50000) && NpcapCaptureService.Keeps(ports, false, 50000, 13328)
            && NpcapCaptureService.Keeps(Array.Empty<int>(), false, 443, 50000) && NpcapCaptureService.Keeps(ports, true, 443, 50000);
        Console.WriteLine($"  -> only the game's ports go on to the meter (a website's 443 never does): {keeps}");

        var settings = new MeterSettings();
        bool shows = FpsOverlayShows.Of(settings) == FpsOverlayShows.Both;
        settings.FpsOverlayShows = "ping";
        shows &= FpsOverlayShows.Of(settings) == FpsOverlayShows.Ping && !FpsOverlayShows.ShowsFps(FpsOverlayShows.Ping) && FpsOverlayShows.ShowsPing(FpsOverlayShows.Ping);
        settings.FpsOverlayShows = "nonsense";
        shows &= FpsOverlayShows.Of(settings) == FpsOverlayShows.Both && FpsOverlayShows.ShowsFps(FpsOverlayShows.Both) && FpsOverlayShows.ShowsPing(FpsOverlayShows.Both);
        settings.FpsOverlayShows = null!;
        shows &= FpsOverlayShows.Of(settings) == FpsOverlayShows.Both && !FpsOverlayShows.ShowsPing(FpsOverlayShows.Fps);
        Console.WriteLine($"  -> the FPS overlay shows the frames and the ping by default, either alone when chosen: {shows}");

        NetworkLog.PathOverride = null;
        System.IO.File.Delete(logFile);
        NetworkStats.Reset();
        return noPing && zoneChange && counted && perSession && local && keeps && shows;
    }

    private static bool RunHotkeyBindingScenario()
    {
        Console.WriteLine("[selftest] Global hotkey bindings:");
        var defaults = new MeterSettings();
        bool roundTrip = new[] { defaults.HotkeyHideUi, defaults.HotkeyPause, defaults.HotkeyCopyDamage, defaults.HotkeyClear }
            .All(text => !Ui.HotkeyBinding.Parse(text).IsEmpty && Ui.HotkeyBinding.Parse(text).ToString() == text);
        bool needsModifier = Ui.HotkeyBinding.Parse("H").IsEmpty && Ui.HotkeyBinding.Parse("Ctrl+").IsEmpty && Ui.HotkeyBinding.Parse("").IsEmpty;
        bool parsesModifiers = Ui.HotkeyBinding.Parse("ctrl+shift+F9") is { Ctrl: true, Shift: true, Alt: false } b && b.Key == System.Windows.Input.Key.F9;
        Console.WriteLine($"  -> the default bindings read back as written: {roundTrip}");
        Console.WriteLine($"  -> a key without a modifier, or nothing, is no binding: {needsModifier}");
        Console.WriteLine($"  -> modifiers and keys parse whatever the case: {parsesModifiers}");
        // the keys nobody types may be a hotkey on their own (7 Nov 2026: "allow users to change these to whatever they want"); a letter, a digit, the
        // space bar and F12 (Windows keeps it) still need a modifier
        bool alone = new[] { "F8", "F1", "F24", "NumPad5", "Add", "Insert", "Home", "End", "PageUp", "PageDown", "Pause", "Scroll" }
                .All(t => Ui.HotkeyBinding.Parse(t) is { IsEmpty: false, Ctrl: false, Alt: false } k && k.ToString() == t)
            && new[] { "A", "D5", "Space", "F12", "Tab", "Enter" }.All(t => Ui.HotkeyBinding.Parse(t).IsEmpty)
            && !Ui.HotkeyBinding.Parse("Ctrl+F12").IsEmpty && !Ui.HotkeyBinding.Parse("Shift+A").IsEmpty;
        Console.WriteLine($"  -> F keys, the number pad and Insert/Home/End/Page Up/Down work alone, letters, digits, space and F12 don't: {alone}");
        // one list holds every hotkey once (the registration, the overlay, the settings page and the overlay menu read it); two alike: the second is "twice"
        var actions = Enum.GetValues<Ui.HotkeyAction>();
        bool table = Ui.HotkeyTable.All.Length == actions.Length && actions.All(a => Ui.HotkeyTable.All.Count(e => e.Action == a) == 1)
            && Ui.HotkeyTable.All.Select(e => e.Id).Distinct().Count() == actions.Length
            && Ui.HotkeyTable.All.All(e => !Ui.HotkeyBinding.Parse(e.Get(defaults)).IsEmpty);
        var same = new MeterSettings { HotkeyLoot = "Ctrl+Alt+P" };
        var twice = Ui.GlobalHotkeys.Twice(Ui.HotkeyTable.Bindings(same));
        bool twiceFound = twice.Count == 1 && twice.ContainsKey(Ui.HotkeyAction.ToggleLootOverlay) && Ui.GlobalHotkeys.Twice(Ui.HotkeyTable.Bindings(defaults)).Count == 0;
        Console.WriteLine($"  -> the table has each of the {actions.Length} hotkeys once, each with a default: {table}; a combination given twice is found (the later one): {twiceFound}");
        // a hotkey that does nothing is marked in the overlay's list; the others are not
        var rows = Ui.HotkeyOverlayWindow.RowsOf(defaults, new Dictionary<Ui.HotkeyAction, Ui.HotkeyProblem> { [Ui.HotkeyAction.TogglePause] = Ui.HotkeyProblem.Taken });
        bool marked = rows.Count(r => r.Problem is not null) == 1 && rows.Single(r => r.Problem is not null).Keys == "Ctrl+Alt+P";
        Console.WriteLine($"  -> the hotkey overlay marks the one taken by another program: {marked}");
        return roundTrip && needsModifier && parsesModifiers && alone && table && twiceFound && marked;
    }
}
