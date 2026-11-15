using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The Hidden Cube arrivals (the count the map's chime and "show the map" follow) and the militia guards of the other side (Stacy's ideas, her fork 17 October 2026).</summary>
public static class SelfCheckCubesGuards
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Hidden Cube arrivals, militia guards:");
        var directory = new Aion2EntityDirectory();
        directory.NoteEnv(10, "Cube", "Hidden Cube", 0, 0);
        directory.NoteEnv(11, "Cube", "Hidden Cube", 500, 0);
        directory.NoteEnv(10, "Cube", "Hidden Cube", 0, 0);      // the same cube announced again: no new arrival
        directory.NoteEnv(12, "Herb", "Herb", 100, 100);         // a plant is no cube
        int two = directory.CubeArrivals;
        directory.ForgetEnv(10);
        directory.NoteEnv(10, "Cube", "Hidden Cube", 0, 0);      // gone and come back: an arrival again
        bool arrivals = two == 2 && directory.CubeArrivals == 3;

        // out of range (reason 1) keeps the cube remembered and counts a departure; opened or gathered (reason 4) removes it; back in sight is an arrival again
        directory.ForgetEnv(11, 1);
        bool left = directory.CubeDepartures == 1 && directory.LiveCubes().Count == 1 && directory.CubeDetails().Count == 2;
        directory.NoteEnv(11, "Cube", "Hidden Cube", 500, 0);
        left = left && directory.CubeArrivals == 4 && directory.LiveCubes().Count == 2;
        directory.ForgetEnv(11, 4);
        left = left && directory.CubeDepartures == 1 && directory.CubeDetails().Count == 1;

        directory.NoteMobPosition(100, 2930819, 1000, 2000);     // a Guard
        directory.NoteMobPosition(101, 1170, 3000, 4000);        // a pet monster
        var guards = directory.LiveGuards();
        bool live = guards.Count == 1 && guards[0] == (1000f, 2000f);
        directory.ForgetLiveMob(100);
        bool gone = directory.LiveGuards().Count == 0;

        bool ids = Enumerable.Range(2930813, 8).All(Aion2Guards.IsGuard) && Aion2Guards.IsGuard(2989448) && !Aion2Guards.IsGuard(2930812) && !Aion2Guards.IsGuard(2989449) && !Aion2Guards.IsGuard(1170);
        // hostile only on the open map of the other side: an Elyos on Altgard (World_D), an Asmodian on Verteron (World_L); never at home, in the Abyss, in an instance or when the own side is unknown
        bool hostile = Aion2Guards.IsHostileMap("World/World_D/World_D_A", "Elyos") && Aion2Guards.IsHostileMap("World/World_L/World_L_A", "Asmodian")
            && !Aion2Guards.IsHostileMap("World/World_L/World_L_A", "Elyos") && !Aion2Guards.IsHostileMap("World/World_D/World_D_A", "Asmodian")
            && !Aion2Guards.IsHostileMap("Intersever/Abyss/Abyss_Reshanta_A", "Elyos") && !Aion2Guards.IsHostileMap("Dungeon/Fire_Temple", "Elyos")
            && !Aion2Guards.IsHostileMap("World/World_D/World_D_A", null);
        Console.WriteLine($"  -> cube arrivals counted once per cube={arrivals}, out of sight remembered={left}, guards among the live monsters={live}/gone again={gone}, the 29 Guard NPC ids={ids}, hostile maps only={hostile}");

        // the cube's metres (Ui.CubeRange): from the map's arrow, the game's own right after its report ("42 m"), the minimap's in between ("~32 m"),
        // nothing once neither is fresh
        DateTime t = DateTime.UtcNow;
        var walker = new Aion2EntityDirectory { Clock = () => t };
        var oneCube = new List<(float X, float Y)> { (4200, 0) };
        walker.NoteLocalPosition(0, 0);
        AionDPS.Ui.PlayerArrow.Reset();
        var reported = AionDPS.Ui.CubeRange.Nearest(walker, oneCube);
        t = t.AddSeconds(2);
        walker.NoteLocalPosition(1000, 0, 0, 0);
        AionDPS.Ui.PlayerArrow.Reset();
        var read = AionDPS.Ui.CubeRange.Nearest(walker, oneCube);
        t = t.AddSeconds(4);
        var unknown = AionDPS.Ui.CubeRange.Nearest(walker, oneCube);
        AionDPS.Ui.PlayerArrow.Reset();
        bool metres = reported is { Exact: true } r && Math.Abs(r.Metres - 42) < 0.01 && read is { Exact: false } m && Math.Abs(m.Metres - 32) < 0.01
            && unknown is null && AionDPS.Ui.CubeRange.Text(m) == "~32 m";
        Console.WriteLine($"  -> cube metres: {(reported is { } a1 ? AionDPS.Ui.CubeRange.Text(a1) : "none")} after the game's report, "
            + $"{(read is { } a2 ? AionDPS.Ui.CubeRange.Text(a2) : "none")} from the minimap 2 s later, {(unknown is null ? "none" : "some")} 4 s after that: {metres}");

        // the volume follows the ear (15 November 2026, a player: the lowest step of the cube chime "still super loud"): the samples are turned down by the
        // square of the setting, so the lowest step (5) is some 52 dB under the sound as made; a file that is not 16-bit PCM is left to the player's volume
        bool curve = Math.Abs(AionDPS.Ui.NotifySounds.Gain(5) - 0.0025) < 1e-9 && Math.Abs(AionDPS.Ui.NotifySounds.Gain(70) - 0.49) < 1e-9
            && AionDPS.Ui.NotifySounds.Gain(100) == 1 && AionDPS.Ui.NotifySounds.Gain(0) == 0;
        byte[] chime = System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "cube-arrival.wav"));
        int Peak(byte[] wav)
        {
            int data = wav.AsSpan().IndexOf("data"u8);
            int peak = 0;
            for (int i = data + 8; data >= 0 && i + 1 < Math.Min(wav.Length, data + 8 + BitConverter.ToInt32(wav, data + 4)); i += 2)
            {
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
            }

            return peak;
        }

        int loud = Peak(chime);
        bool scaled = AionDPS.Ui.NotifySounds.Scale(chime, AionDPS.Ui.NotifySounds.Gain(5));
        int quiet = Peak(chime);
        byte[] notPcm = (byte[])chime.Clone();
        notPcm[20] = 3; // a float WAV
        bool refuses = !AionDPS.Ui.NotifySounds.Scale(notPcm, 0.5) && !AionDPS.Ui.NotifySounds.Scale(new byte[64], 0.5);
        bool sound = curve && scaled && loud > 10_000 && quiet <= loud * 0.0025 + 1 && quiet > 0 && refuses;
        Console.WriteLine($"  -> the sound volume follows the ear={curve}, the cube chime at the lowest step peaks at {quiet} of {loud} ({20 * Math.Log10(Math.Max(1, quiet) / (double)loud):0} dB)={sound}");
        bool ok = arrivals && left && live && gone && ids && hostile && metres && sound;
        Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
        return ok;
    }
}
