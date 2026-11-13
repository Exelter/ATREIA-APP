using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>Game windows told by the screen (<see cref="HudWatcher"/>): the HUD is learnt in fights, a covered HUD hides the overlays and an uncovered one
/// brings them back, parts shown only in fights are dropped, and the meter's own overlays and a black picture decide nothing; a wide screen needs fewer
/// points, and a HUD learnt just above the need keeps working without its fight-only parts.</summary>
public static class SelfCheckHud
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Game windows by the screen (HUD):");
        const int n = HudWatcher.Points;
        var rnd = new Random(7);
        DateTime t = new(2026, 10, 31, 20, 0, 0);
        var none = new bool[n];

        // the HUD: points 0..999 a steady edge; 1000..1599 an edge only in fights (a boss's bar); the rest the moving world
        float[] Frame(bool fight, bool menu)
        {
            var e = new float[n];
            for (int i = 0; i < n; i++)
            {
                e[i] = i < 1000 ? (menu ? 4 : 60 + (float)rnd.NextDouble() * 8)
                    : i < 1600 ? (fight && !menu ? 70 + (float)rnd.NextDouble() * 6 : 3)
                    : (float)rnd.NextDouble() * 120;
            }

            return e;
        }

        var hud = new HudWatcher();
        bool notYet = true;
        for (int i = 0; i < HudWatcher.NeedLooks - 1; i++)
        {
            hud.Judge(Frame(true, false), none, true, t = t.AddSeconds(1));
            notYet &= !hud.Ready;
        }

        hud.Judge(Frame(true, false), none, true, t = t.AddSeconds(1));
        bool learnt = hud.Ready && hud.HudPoints is >= 1500 and <= 1700;
        int before = hud.HudPoints;
        Console.WriteLine($"  -> nothing before {HudWatcher.NeedLooks} looks in fights ({notYet}); learnt then, the HUD and the fight-only bar ({learnt}, {hud.HudPoints} points)");

        // the normal screen between fights: shown; the fight-only bar is dropped after a while
        bool shown = true;
        for (int i = 0; i < 40; i++)
        {
            shown &= !hud.Judge(Frame(false, false), none, false, t = t.AddSeconds(0.3));
        }

        // the bar's 600 points go, the HUD's 1000 stay (a few points of the moving world may have looked steady by chance; they stay, harmless)
        bool pruned = before - hud.HudPoints is >= 595 and <= 600 && hud.HudPoints >= 1000;
        Console.WriteLine($"  -> the normal screen keeps the overlays ({shown}); the fight-only bar is dropped ({pruned}, {before - hud.HudPoints} dropped, {hud.HudPoints} left)");

        // a game window covers the HUD: hidden at the third look; uncovered: back at the second
        bool early = !hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3)) && !hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3));
        bool hidden = hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3)) && hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3));
        bool stays = hud.Judge(Frame(false, false), none, false, t = t.AddSeconds(0.3));
        bool back = !hud.Judge(Frame(false, false), none, false, t = t.AddSeconds(0.3));
        Console.WriteLine($"  -> a covered HUD hides after the third look ({early && hidden}); uncovered, back at the second ({stays && back})");

        // fighting: never hidden; the overlays over most of the HUD, or a black picture: no judgement
        for (int i = 0; i < 3; i++)
        {
            hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3));
        }

        bool fight = !hud.Judge(Frame(false, true), none, true, t = t.AddSeconds(0.3));
        var mostly = new bool[n];
        for (int i = 0; i < 990; i++)
        {
            mostly[i] = true;
        }

        bool underOverlays = true;
        for (int i = 0; i < 5; i++)
        {
            underOverlays &= !hud.Judge(Frame(false, true), mostly, false, t = t.AddSeconds(0.3));
        }

        bool black = true;
        for (int i = 0; i < 5; i++)
        {
            black &= !hud.Judge(null, none, false, t = t.AddSeconds(0.3));
        }

        Console.WriteLine($"  -> never while fighting ({fight}); not with the HUD under the meter's own overlays ({underOverlays}); not on a black picture ({black})");

        // a world that changes edge where the HUD is not: nothing learnt from a screen without a steady HUD
        var noHud = new HudWatcher();
        for (int i = 0; i < 60; i++)
        {
            var e = new float[n];
            for (int k = 0; k < n; k++)
            {
                e[k] = (float)rnd.NextDouble() * 120;
            }

            noHud.Judge(e, none, true, t = t.AddSeconds(1));
        }

        bool noneLearnt = !noHud.Ready;
        Console.WriteLine($"  -> a screen without a steady HUD teaches nothing ({noneLearnt})");

        // a wide screen (13 November 2026, a 3440x1440 player whose HUD was never learnt): the HUD keeps its size while the points spread over the width, so
        // fewer are needed there - 50 steady points are learnt on a 3440x1440 window and not on a 2560x1440 one
        bool needs = HudWatcher.NeedPointsFor(1920, 1080) == 60 && HudWatcher.NeedPointsFor(2560, 1600) == 60 && HudWatcher.NeedPointsFor(3440, 1440) == 45
            && HudWatcher.NeedPointsFor(5120, 1440) == 30;
        var wide = new HudWatcher();
        wide.SizeForCheck(new System.Drawing.Size(3440, 1440));
        var plain = new HudWatcher();
        plain.SizeForCheck(new System.Drawing.Size(2560, 1440));
        for (int i = 0; i < HudWatcher.NeedLooks; i++)
        {
            var e = new float[n];
            for (int k = 0; k < n; k++)
            {
                e[k] = k < 50 ? 60 + (float)rnd.NextDouble() * 8 : (float)rnd.NextDouble() * 120;
            }

            t = t.AddSeconds(1);
            wide.Judge(e, none, true, t);
            plain.Judge(e, none, true, t);
        }

        bool wideLearnt = needs && wide.Ready && !plain.Ready;
        Console.WriteLine($"  -> a wide screen needs fewer HUD points: 60 up to 16:9, 45 at 3440x1440, 30 at 5120x1440; 50 learnt there, not on 2560x1440 ({wideLearnt})");

        // a HUD learnt just above the need loses its fight-only parts between fights: it is kept while two thirds of the need are left (66 learnt, 10 of
        // them only in fights: 56 kept, with a point of the moving world that looked steady by chance perhaps), given up below (66 learnt, 30 of them only in
        // fights: 36 left)
        HudWatcher Learnt(int steady, int fightOnly)
        {
            var w = new HudWatcher();
            w.SizeForCheck(new System.Drawing.Size(2560, 1440));
            for (int i = 0; i < HudWatcher.NeedLooks; i++)
            {
                var e = new float[n];
                for (int k = 0; k < n; k++)
                {
                    e[k] = k < steady ? 60 + (float)rnd.NextDouble() * 8 : (float)rnd.NextDouble() * 120;
                }

                w.Judge(e, none, true, t = t.AddSeconds(1));
            }

            for (int i = 0; i < 40; i++)
            {
                var e = new float[n];
                for (int k = 0; k < n; k++)
                {
                    e[k] = k < steady - fightOnly ? 60 + (float)rnd.NextDouble() * 8 : k < steady ? 3 : (float)rnd.NextDouble() * 120;
                }

                w.Judge(e, none, false, t = t.AddSeconds(0.3));
            }

            return w;
        }

        HudWatcher few = Learnt(66, 10), fewer = Learnt(66, 30);
        bool kept = few.Ready && few.HudPoints is >= 56 and <= 60 && !fewer.Ready;
        Console.WriteLine($"  -> a HUD learnt just above the need keeps working without its fight-only parts ({few.Ready}, {few.HudPoints} points); "
            + $"given up below two thirds of the need ({!fewer.Ready}) ({kept})");
        return notYet && learnt && shown && pruned && early && hidden && stays && back && fight && underOverlays && black && noneLearnt && wideLearnt && kept;
    }
}
