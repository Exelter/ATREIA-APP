using System.IO;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Combat;
using AionDPS.Linux.Meter;
using AionDPS.Linux.Platform;
using AionDPS.Ui;

namespace AionDPS.Linux.Checks;

/// <summary>
/// The Linux app's checks (Program "selftest", run by the Linux workflow): the Windows app's own checks that need no window - the game's frames decoded,
/// the fights, the sessions, the uploads' contents, the history - run here on Linux, then the Linux parts: the game's connections read from /proc, the
/// capture rights, the meter's lines worked out from a fight, and the game's pictures found where the app looks for them. Prints like the Windows checks:
/// a heading per check, a line per finding with True or False, and "ALL CHECKS PASSED" at the end.
/// </summary>
internal static class LinuxSelfCheck
{
    public static bool Run()
    {
        bool ok = true;
        // the Windows app's checks, in the order its SelfCheck.Run has them
        ok &= SelfCheckDamageIndex.Run();
        ok &= SelfCheckAion2.RunShared();
        ok &= SelfCheckHistory.Run();
        ok &= SelfCheckSessions.Run();
        ok &= SelfCheckCombatOverlay.Run();
        ok &= SelfCheckRotation.Run();
        ok &= SelfCheckRaidDps.Run();
        ok &= SelfCheckOverheal.Run();
        ok &= SelfCheckGroupFinder.Run();
        ok &= SelfCheckBuffUpload.Run();
        ok &= SelfCheckStream.Run();
        ok &= SelfCheckHitKinds.Run();
        ok &= SelfCheckProtocolHealth.Run();
        ok &= SelfCheckTickets.Run();
        ok &= SelfCheckBossTimers.Run();
        ok &= SelfCheckBossPattern.Run();
        ok &= SelfCheckVerify.Run();
        ok &= SelfCheckInstanceTimer.Run();
        ok &= SelfCheckNews.Run();
        ok &= SelfCheckLegion.Run();
        ok &= SelfCheckSafety.Run();
        ok &= SelfCheckFieldBosses.Run();
        ok &= SelfCheckCpPlan.Run();
        ok &= SelfCheckGatherTally.Run();
        ok &= SelfCheckReplay.Run();
        ok &= SelfCheckMechanics.Run();
        ok &= SelfCheckIdentity.Run();
        ok &= SelfCheckStatCalc.Run();
        ok &= SelfCheckEstimateRescale.Run();

        // the Linux parts
        ok &= RunGameConnectionsScenario();
        ok &= RunCaptureRightsScenario();
        ok &= RunMeterScenario();
        ok &= RunMeterPartsScenario();
        ok &= RunMeterViewsScenario();
        ok &= RunPicturesScenario();
        return ok;
    }

    /// <summary>
    /// The game's connections from a process table laid out like /proc: the game under Wine (named AION2.exe), another under an older Wine (named by the
    /// preloader, its command line a Windows path), and a shell; the game's sockets in the kernel's tables, one established to the game server, one
    /// listening, one to a web page (port 443, not the game's).
    /// </summary>
    private static bool RunGameConnectionsScenario()
    {
        Console.WriteLine("[selftest] Linux: the game's connections read from /proc:");
        string root = Path.Combine(Path.GetTempPath(), "atreia-proc-" + Environment.ProcessId);
        string before = GameConnections.ProcRoot;
        try
        {
            Directory.CreateDirectory(root);
            void Process(int pid, string comm, string cmdline, params long[] sockets)
            {
                string dir = Path.Combine(root, pid.ToString());
                Directory.CreateDirectory(Path.Combine(dir, "fd"));
                Directory.CreateDirectory(Path.Combine(dir, "net"));
                File.WriteAllText(Path.Combine(dir, "comm"), comm + "\n");
                File.WriteAllText(Path.Combine(dir, "cmdline"), cmdline);
                int fd = 3;
                foreach (long inode in sockets)
                {
                    File.CreateSymbolicLink(Path.Combine(dir, "fd", (fd++).ToString()), $"socket:[{inode}]");
                }

                File.CreateSymbolicLink(Path.Combine(dir, "fd", "1"), "/dev/null");
                // the kernel's table: established (01) to 3D0D0E01:3410 (port 13328), listening (0A) on 8080, established to port 443
                File.WriteAllText(Path.Combine(dir, "net", "tcp"),
                    "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n"
                    + "   0: 0100A8C0:C350 3D0D0E01:3410 01 00000000:00000000 00:00000000 00000000  1000        0 555 1 0000000000000000 20 4 30 10 -1\n"
                    + "   1: 00000000:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 556 1 0000000000000000 100 0 0 10 0\n"
                    + "   2: 0100A8C0:C351 22D7CB68:01BB 01 00000000:00000000 00:00000000 00000000  1000        0 557 1 0000000000000000 20 4 30 10 -1\n");
                File.WriteAllText(Path.Combine(dir, "net", "tcp6"), "  sl  local_address                         remote_address                        st\n");
            }

            Process(4100, "AION2.exe", "C:\\Program Files\\AION2\\Binaries\\Win64\\AION2.exe\0-lang=en\0", 555, 556, 557);
            Process(4200, "wine64-preloade", "Z:\\home\\player\\Games\\AION2\\AION2.exe\0", 900);
            Process(4300, "bash", "/bin/bash\0", 901);
            GameConnections.ProcRoot = root;
            List<int> pids = GameConnections.GamePids();
            bool found = pids.OrderBy(p => p).SequenceEqual(new[] { 4100, 4200 });
            IReadOnlyList<int> ports = GameConnections.RemotePorts();
            bool portsRead = ports.SequenceEqual(new[] { 13328 });
            bool inode = GameConnections.SocketInode("socket:[12345]") == 12345 && GameConnections.SocketInode("pipe:[7]") is null && GameConnections.SocketInode("/dev/null") is null;
            Console.WriteLine($"  -> the game is found by its name and by an older Wine's command line, the shell is not: {found} ({string.Join(", ", pids)})");
            Console.WriteLine($"  -> its established connection gives the server's port 13328; the listening socket and the web page's 443 do not: {portsRead} ({string.Join(", ", ports)})");
            Console.WriteLine($"  -> socket links are told from other links: {inode}");
            GameConnections.ProcRoot = Path.Combine(root, "none");
            bool nothing = GameConnections.RemotePorts().Count == 0 && !GameConnections.GameRunning();
            Console.WriteLine($"  -> no process table: no game, no ports, no error: {nothing}");
            return found && portsRead && inode && nothing;
        }
        finally
        {
            GameConnections.ProcRoot = before;
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool RunCaptureRightsScenario()
    {
        Console.WriteLine("[selftest] Linux: the right to read the network:");
        bool root = CaptureRights.EffectiveFrom("Name:\tAtreiaApp\nCapInh:\t0000000000000000\nCapPrm:\t000001ffffffffff\nCapEff:\t000001ffffffffff\n") == 0x1ffffffffffUL;
        bool given = CaptureRights.EffectiveFrom("CapEff:\t0000000000003000\n") is ulong caps && (caps & (1UL << 13)) != 0 && (caps & (1UL << 12)) != 0;
        bool none = CaptureRights.EffectiveFrom("CapEff:\t0000000000000000\n") == 0 && CaptureRights.EffectiveFrom(null) is null;
        bool command = CaptureRights.SetcapCommand.StartsWith("sudo setcap cap_net_raw,cap_net_admin=eip '", StringComparison.Ordinal);
        bool updates = LinuxUpdates.Newer("2.0.17", "2.0.16") == "2.0.17" && LinuxUpdates.Newer("2.0.16", "2.0.16") is null
            && LinuxUpdates.Newer("2.0.9", "2.0.16") is null && LinuxUpdates.Newer(null, "2.0.16") is null;
        Console.WriteLine($"  -> a newer version on atreia.app is told from this one and older ones: {updates}");
        Console.WriteLine($"  -> root's capabilities, the two the installer gives, none: {root && given && none}");
        Console.WriteLine($"  -> the command that gives them names this program: {command} ({CaptureRights.SetcapCommand})");
        Console.WriteLine($"  -> this run: {CaptureRights.Check()} (libpcap: {NpcapAvailability.Detect().Location ?? "missing"})");
        return root && given && none && command && updates;
    }

    /// <summary>The meter's lines from a boss fight (the made-up one of the pictures), through the same engine the window uses.</summary>
    private static bool RunMeterScenario()
    {
        Console.WriteLine("[selftest] Linux: the meter's lines:");
        using var engine = new MeterEngine();
        engine.Start(new MeterSettings { AutoScope = false, ViewScope = "Group" }, new SampleFight(DateTime.Now.AddSeconds(-115)));
        engine.Tick();
        IReadOnlyList<PlayerRow> rows = engine.ShownRows;
        bool five = rows.Count == 5 && rows.Select(r => r.Name).SequenceEqual(new[] { "Kaivhan", "Nyxelle", "Seraphyne", "Draegor", "Lumiael" });
        // every line is measured against the longest whole bar (its own part, the buffs given, the healing on the others): that one fills the line
        bool ranked = rows.Select(r => r.Rank).SequenceEqual(new[] { 1, 2, 3, 4, 5 }) && Math.Abs(rows.Sum(r => r.SharePercent) - 100) < 0.01
            && Math.Abs(rows.Max(r => r.HealFillPercent) - 100) < 0.001;
        bool target = engine.TargetText == SampleFight.BossName && engine.TimeText.Length > 0;
        double gladiator = rows[0].Dps ?? 0;
        bool rate = gladiator is > 7_500 and < 9_100;
        bool classes = rows[0].ClassName == "Gladiator" && rows[4].ClassName == "Cleric";
        bool boss = engine.CurrentBoss() is { Hp: 3_140_000, Max: 12_600_000 };
        Console.WriteLine($"  -> five lines, best first: {five} ({string.Join(", ", rows.Select(r => $"{r.Name} {r.DpsCompact}"))})");
        Console.WriteLine($"  -> ranks 1-5, the shares make 100 %, the longest bar is full: {ranked}");
        Console.WriteLine($"  -> the meter follows the boss on its own, with the fight's time: {target} ({engine.TargetText}, {engine.TimeText})");
        Console.WriteLine($"  -> the Gladiator's DPS over the boss fight is about 8.3K: {rate} ({gladiator:0})");
        Console.WriteLine($"  -> the classes and the boss line come through: {classes && boss}");

        engine.Mode = MeterEngine.MeterMode.Heal;
        bool healer = engine.ShownRows.FirstOrDefault()?.Name == "Lumiael" && engine.Unit == "HPS";
        engine.Mode = MeterEngine.MeterMode.Taken;
        bool tank = engine.ShownRows.FirstOrDefault()?.Name == "Nyxelle" && engine.Unit == "DTPS";
        Console.WriteLine($"  -> healing puts the Cleric first, damage taken the Templar: {healer && tank}");

        engine.Mode = MeterEngine.MeterMode.Damage;
        engine.SetScope(MeterEngine.MeterScope.Me);
        bool me = engine.ShownRows.Count == 1 && engine.ShownRows[0].Name == "Kaivhan";
        engine.Clear();
        bool cleared = engine.ShownRows.Count == 0 && engine.TimeText.Length == 0;
        Console.WriteLine($"  -> the scope Me leaves the own line alone, Clear empties the meter: {me && cleared}");
        return five && ranked && target && rate && classes && boss && healer && tank && me && cleared;
    }

    /// <summary>The bar's parts and the measures of the pill's menu (Windows: RankRows, UpdateRaidDps, MeterBarParts): the buffs given and the healing on the
    /// others after the own damage, rDPS crediting the buffs, All in one, and the heal lines leaving the rDPS view behind.</summary>
    private static bool RunMeterPartsScenario()
    {
        Console.WriteLine("[selftest] Linux: the bar's parts, rDPS and All in one:");
        using var engine = new MeterEngine();
        engine.Start(new MeterSettings { AutoScope = false, ViewScope = "Group" }, new SampleFight(DateTime.Now.AddSeconds(-115)));
        engine.Tick();
        PlayerRow Line(string name) => engine.AllRows.First(r => r.Name == name);
        PlayerRow chanter = Line("Draegor"), cleric = Line("Lumiael"), sorcerer = Line("Seraphyne");
        bool grey = chanter.BarGivenText.StartsWith('+') && chanter.GivenFillPercent > chanter.FillPercent + 0.5 && sorcerer.BarGivenText.Length == 0;
        bool green = cleric.BarHealText.StartsWith('+') && cleric.HealFillPercent > cleric.GivenFillPercent + 0.5;
        bool longest = engine.AllRows.Max(r => r.HealFillPercent) is > 99.9 and <= 100.0001;
        bool legend = engine.Legend is { Rdps: false, Buffs: true, Heals: true };
        Console.WriteLine($"  -> the Chanter's buffs given go on in grey after its damage ({chanter.BarGivenText}), the Sorcerer gives none: {grey}");
        Console.WriteLine($"  -> the Cleric's healing on the others goes on in green ({cleric.BarHealText}): {green}");
        Console.WriteLine($"  -> the longest whole bar fills the line, the legend says what the colours are: {longest && legend}");

        engine.PickMode(MeterEngine.MeterMode.Raid);
        bool rdpsView = engine.AllRows.All(r => r.RdpsView) && engine.Unit == "rDPS" && engine.ModeShort == "rDPS";
        bool credited = (chanter.Rdps ?? 0) > (chanter.Dps ?? 0) && (sorcerer.Rdps ?? 0) < (sorcerer.Dps ?? 0);
        bool ordered = engine.ShownRows.Select(r => r.Rdps ?? 0).SequenceEqual(engine.ShownRows.Select(r => r.Rdps ?? 0).OrderByDescending(v => v));
        double sumDps = engine.AllRows.Sum(r => r.Dps ?? 0), sumRdps = engine.AllRows.Sum(r => r.Rdps ?? 0);
        Console.WriteLine($"  -> rDPS: the view and its word, the Chanter credited its buffs, the others less: {rdpsView && credited} ({chanter.Rdps:0} vs {chanter.Dps:0})");
        Console.WriteLine($"  -> the lines ordered by rDPS, the group's rDPS adds up to its DPS: {ordered && Math.Abs(sumDps - sumRdps) < 1}");

        engine.PickMode(MeterEngine.MeterMode.Heal);
        bool healLines = engine.AllRows.All(r => !r.RdpsView) && engine.ShownRows.FirstOrDefault()?.Name == "Lumiael" && engine.ModeShort == "Heal"
            && engine.ShownRows[0].DpsCompact == PlayerRow.Compact((long)Math.Round(engine.ShownRows[0].Dps ?? 0));
        Console.WriteLine($"  -> the healing after rDPS: the lines leave the rDPS view, the Cleric first with its own rate: {healLines}");

        engine.PickMode(MeterEngine.MeterMode.All);
        PlayerRow tank = Line("Nyxelle");
        bool all = engine.AllRows.All(r => r.AllMode) && Line("Lumiael").Healing > 0 && tank.DamageTaken > 0 && tank.TotalOrTakenCompact == PlayerRow.Compact(tank.DamageTaken)
            && engine.ModeShort == "All";
        Console.WriteLine($"  -> All in one: the healing and the damage taken beside the damage: {all}");

        engine.PickMode(MeterEngine.MeterMode.Taken);
        bool takenParts = engine.AllRows.All(r => r.GivenFillPercent == r.FillPercent && r.BarGivenText.Length == 0) && engine.Legend is null && engine.ModeShort == "Tank";
        Console.WriteLine($"  -> damage taken has no parts and no legend: {takenParts}");
        return grey && green && longest && legend && rdpsView && credited && ordered && Math.Abs(sumDps - sumRdps) < 1 && healLines && all && takenParts;
    }

    /// <summary>The fold between fights and the views in place of the lines (Windows: UpdateOverlayFold, the scope switch's Stagger and Grind, Buffs on you).</summary>
    private static bool RunMeterViewsScenario()
    {
        Console.WriteLine("[selftest] Linux: the fold and the views:");
        DateTime now = DateTime.Now;
        using var quiet = new MeterEngine();
        quiet.Start(new MeterSettings { AutoScope = false, ViewScope = "Group" }, new SampleFight(now, seconds: 0, withBoss: false));
        quiet.Tick();
        bool folded = !quiet.Open(now) && quiet.ShownRows.Count == 0;
        Console.WriteLine($"  -> no fight yet: the meter is its bar alone: {folded}");

        using var engine = new MeterEngine();
        engine.Start(new MeterSettings { AutoScope = false, ViewScope = "Group" }, new SampleFight(now.AddSeconds(-115)));
        engine.Tick();
        bool open = engine.Open(now);
        engine.Clear();
        bool closed = !engine.Open(now);
        Console.WriteLine($"  -> a boss fight opens it, a clear folds it again: {open && closed}");

        using var views = new MeterEngine();
        views.Start(new MeterSettings { AutoScope = false, ViewScope = "Group" }, new SampleFight(now.AddSeconds(-115)));
        views.Tick();
        views.ScopeClicked("Stagger");
        bool stagger = views.ShowStagger && views.ModeShort == "Stagger" && views.Open(now);
        views.ScopeClicked("Grind");
        bool grind = views.ShowGrind && !views.ShowStagger && views.ModeShort == "Grind" && views.Total(now).Unit == "Kinah/h";
        views.ScopeClicked("Grind");
        bool grindOff = !views.ShowGrind && !views.ShowStagger;
        views.PickBuffs();
        bool buffs = views.ShowBuffs && views.ModeShort == "Buffs" && views.Total(now).Unit == "buffs" && views.Legend is null;
        views.ScopeClicked("Stagger");
        bool swapped = views.ShowStagger && !views.ShowBuffs;
        views.ScopeClicked("Me");
        bool scope = !views.ShowStagger && views.ShownScope == MeterEngine.MeterScope.Me && views.ShownRows.Count == 1;
        views.ChangeSetting(s => s.MeterGrind = true);
        bool everything = views.CountsEverything && views.ModeShort == "DPS · Grind";
        Console.WriteLine($"  -> Stagger in place of the lines, Grind in place of Stagger, Grind again brings the lines back: {stagger && grind && grindOff}");
        Console.WriteLine($"  -> Buffs on you, then Stagger, then a scope: one view at a time, the scope closes them: {buffs && swapped && scope}");
        Console.WriteLine($"  -> Grind: every fight shows on the pill: {everything}");

        var hits = new List<DamageEvent>();
        for (int i = 0; i < 40; i++)
        {
            hits.Add(new DamageEvent(now.AddSeconds(i * 0.5), 1, 900, 1_000 + i * 10, false, IsCritical: i % 4 == 0, Kinds: i % 2 == 0 ? HitKinds.Back : HitKinds.Front));
        }

        IReadOnlyList<(string Label, string Value)> stats = MeterEngine.DummyStatsOf(hits);
        bool dummy = stats.Count == 8 && stats[0].Value == PlayerRow.Compact(hits.Sum(h => h.Amount)) && stats[^1] == ("Max hit", PlayerRow.Compact(1_390))
            && stats.First(s => s.Label == LocalizationManager.Instance["Look.ColCrit"]).Value == "25%";
        Console.WriteLine($"  -> the dummy card's figures: {dummy} ({string.Join(", ", stats.Select(s => s.Label + " " + s.Value))})");
        return folded && open && closed && stagger && grind && grindOff && buffs && swapped && scope && everything && dummy;
    }

    /// <summary>
    /// The game's pictures where the app looks for them: the class emblems by the class names the meter uses, a skill icon and a boss picture by the names
    /// in the app's tables. Linux tells capital letters apart in file names and Windows does not, so a picture named differently there than in the tables
    /// would show on Windows and be missing here. Only when the image pack is there (the workflow puts it in place); without it the check says so.
    /// </summary>
    private static bool RunPicturesScenario()
    {
        Console.WriteLine("[selftest] Linux: the game's pictures are found by their exact names:");
        string emblems = GameArt.FolderOf("classes", "icons");
        if (!Directory.Exists(emblems))
        {
            Console.WriteLine("  -> no image pack here (the app downloads it on its first start): nothing to check");
            return true;
        }

        string[] classes = { "Gladiator", "Templar", "Ranger", "Assassin", "Sorcerer", "Spiritmaster", "Cleric", "Chanter" };
        var missing = classes.Where(c => !File.Exists(GameArt.PathOf("classes", "icons", c + ".png"))).ToList();
        bool emblemsOk = missing.Count == 0;
        string? keen = Aion2.Protocol.Aion2SkillIcons.PathForName("Keen Strike", "Gladiator");
        string? stone = Aion2.Protocol.Aion2SkillIcons.PathFor(30000241);
        bool skills = keen is not null && File.Exists(keen) && stone is not null && File.Exists(stone);
        string? classEmblem = Aion2.Protocol.Aion2Artwork.ClassEmblemPath("Gladiator");
        bool emblem = classEmblem is not null && File.Exists(classEmblem);
        Console.WriteLine($"  -> the class emblems of the meter's lines: {emblemsOk}{(missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : "")}");
        Console.WriteLine($"  -> a skill icon by name and by id: {skills}");
        Console.WriteLine($"  -> the class emblem of the combat overlay: {emblem}");
        return emblemsOk && skills && emblem;
    }
}
