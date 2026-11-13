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
        Console.WriteLine($"  -> root's capabilities, the two the installer gives, none: {root && given && none}");
        Console.WriteLine($"  -> the command that gives them names this program: {command} ({CaptureRights.SetcapCommand})");
        Console.WriteLine($"  -> this run: {CaptureRights.Check()} (libpcap: {NpcapAvailability.Detect().Location ?? "missing"})");
        return root && given && none && command;
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
        bool ranked = rows.Select(r => r.Rank).SequenceEqual(new[] { 1, 2, 3, 4, 5 }) && Math.Abs(rows.Sum(r => r.SharePercent) - 100) < 0.01 && rows[0].FillPercent == 100;
        bool target = engine.TargetText == SampleFight.BossName && engine.TimeText.Length > 0;
        double gladiator = rows[0].Dps ?? 0;
        bool rate = gladiator is > 7_500 and < 9_100;
        bool classes = rows[0].ClassName == "Gladiator" && rows[4].ClassName == "Cleric";
        bool boss = engine.CurrentBoss() is { Hp: 3_140_000, Max: 12_600_000 };
        Console.WriteLine($"  -> five lines, best first: {five} ({string.Join(", ", rows.Select(r => $"{r.Name} {r.DpsCompact}"))})");
        Console.WriteLine($"  -> ranks 1-5, the shares make 100 %, the best bar is full: {ranked}");
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
