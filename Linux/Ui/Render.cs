using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AionDPS.Linux.Meter;
using AionDPS.Linux.Platform;
using AionDPS.Ui;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The pictures of the build (Program "render &lt;out.png&gt; [scene]"), drawn without a screen, as the Windows app's render modes draw its windows for the
/// checks of its look: the meter over a dark stand-in for the game, filled from the made-up boss fight (<see cref="SampleFight"/>) through the real engine.
/// Scenes (the Windows render-overlay's): meter (damage, the bar's parts and their legend), raid (rDPS), heal, taken, all (All in one), callout (the boss's next
/// casts), buffs, stagger, stagger-failed, grind, dummy, bar (folded between fights), waiting (no game yet), permission (the right to read the network
/// missing); fonts (the faces side by side).
/// </summary>
internal static class Render
{
    public static int Run(string[] args)
    {
        string output = args.Length > 1 ? args[1] : "meter.png";
        string scene = args.Length > 2 ? args[2] : "meter";
        App.Headless = true;
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        if (scene == "fonts")
        {
            return Fonts(output);
        }

        using var engine = new MeterEngine();
        DateTime now = DateTime.Now;
        var settings = new MeterSettings { AutoScope = false, ViewScope = "Group" };
        if (scene is "waiting" or "permission")
        {
            engine.ApplySettings(settings);
        }
        else if (scene == "bar")
        {
            // folded between fights: the bar (the own character) and the counters, nothing of a fight
            engine.Start(settings, new SampleFight(now, seconds: 0, withBoss: false));
            engine.Tick();
        }
        else
        {
            engine.Start(settings, new SampleFight(now.AddSeconds(-115)));
            engine.Tick();
        }

        // the counters at the foot, as on any meter in the game (the Windows pictures show the same)
        if (scene is not ("waiting" or "permission"))
        {
            engine.SampleTickets = MeterEngine.SampleTicketList();
        }

        switch (scene)
        {
            case "heal":
                engine.Mode = MeterEngine.MeterMode.Heal;
                break;
            case "taken":
                engine.Mode = MeterEngine.MeterMode.Taken;
                break;
            case "raid":
                engine.Mode = MeterEngine.MeterMode.Raid;
                break;
            case "all":
                engine.Mode = MeterEngine.MeterMode.All;
                break;
            case "callout":
                // the boss's next casts as the uploads learnt them, and its enrage
                engine.SampleCallout = new AionDPS.Upload.BossCalloutFrame(SampleFight.BossName,
                    new[] { new AionDPS.Upload.BossCallout("Searing Breath", 4.2, false), new AionDPS.Upload.BossCallout("Tail Sweep", 13.6, false) }, TimeSpan.FromSeconds(129), 42);
                break;
            case "buffs":
                engine.SampleBuffs = new[]
                {
                    new BuffLine("Blessing of Rock", null, 100, false), new BuffLine("Word of Inspiration", null, 86, false), new BuffLine("Promise of Wind", null, 74, false),
                    new BuffLine("Fury", null, 41, false), new BuffLine("Weakening Brand", null, 18, true),
                };
                engine.ShowForPicture("buffs");
                break;
            case "stagger" or "stagger-failed":
            {
                // a check running 5.2 s into an 8 s cast, or failed with the next one expected; the boss's earlier checks as chips under it
                bool failed = scene == "stagger-failed";
                var (checks, at) = StaggerSample.Series(failed);
                engine.SampleStagger = (checks, checks[^1], StaggerSample.People, failed ? "Next stagger check: Extinction for Eternity in 41 s" : null);
                engine.SampleStaggerAt = at;
                engine.ShowForPicture("stagger");
                break;
            }

            case "grind":
                // 34 minutes farmed: Kinah and Abyss Points an hour in big, kills and loot under them
                engine.SampleGrind = GrindSample.Current(now);
                engine.SampleGrindBook = GrindSample.Book();
                engine.ShowForPicture("grind");
                break;
            case "dummy":
            {
                // a dummy parse running: its card stands in for the lines
                var t0 = new DateTime(2026, 11, 5, 12, 0, 0);
                var hits = new List<AionDPS.Combat.DamageEvent>();
                for (int i = 0; i < 180; i++)
                {
                    AionDPS.Combat.HitKinds kinds = (i % 9 == 0 ? AionDPS.Combat.HitKinds.Perfect : 0) | (i % 6 == 0 ? AionDPS.Combat.HitKinds.Double : 0)
                        | (i % 4 == 0 ? AionDPS.Combat.HitKinds.Back : AionDPS.Combat.HitKinds.Front);
                    hits.Add(new AionDPS.Combat.DamageEvent(t0.AddSeconds(i * 0.23), 1, 900, 4_000 + (i * 37 % 9_000), false, IsCritical: i % 5 < 2, Kinds: kinds));
                }

                engine.SampleDummy = ("0:19", 0.32, "Run 3  ·  running", "0:41 / 1:00", 27_600, hits);
                engine.ShowForPicture("dummy");
                break;
            }
        }

        engine.UpdateCallout(now);
        var view = new MeterView(engine) { Width = 470 };
        view.Update();
        (string text, bool warning, string? action) = StatusLine.For(scene == "permission" ? CaptureRights.Rights.NotGiven : CaptureRights.Rights.Granted, engine, null, null);
        view.ShowStatus(text, warning, action);

        // a dark, uneven stand-in for the game behind the see-through panel
        var backdrop = new Border
        {
            Padding = new Thickness(20),
            Child = view,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#3B4A3A"), 0),
                    new GradientStop(Color.Parse("#5B4B3A"), 0.45),
                    new GradientStop(Color.Parse("#25303F"), 1),
                },
            },
        };
        var window = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = backdrop,
            SystemDecorations = SystemDecorations.None,
            FontFamily = Palette.UiFont,
        };
        window.Show();
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("render: nothing was drawn");
            return 1;
        }

        string? folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output));
        if (folder is not null)
        {
            System.IO.Directory.CreateDirectory(folder);
        }

        frame.Save(output);
        Console.WriteLine($"render: {scene} -> {output} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
        window.Close();
        return 0;
    }

    /// <summary>The faces side by side (a check of the look: the site's face must be the one drawn, not the system's).</summary>
    private static int Fonts(string output)
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        foreach ((string label, FontFamily family) in new[]
        {
            ("A2H Sans", Palette.UiFont),
            ("A2H Sans, file", new FontFamily("avares://AtreiaApp/Fonts/A2HSans-Regular.ttf#A2H Sans")),
            ("Inter", new FontFamily("fonts:Inter#Inter")),
            ("DejaVu Sans", new FontFamily("DejaVu Sans")),
            ("Cinzel", Palette.TitleFont),
        })
        {
            foreach (FontWeight weight in new[] { FontWeight.Normal, FontWeight.SemiBold })
            {
                panel.Children.Add(new TextBlock { Text = $"{label} {weight}: Transcendent Bakarma 28.2K DPS 1:51", FontFamily = family, FontWeight = weight, FontSize = 15, Foreground = Brushes.White });
            }
        }

        var window = new Window { Width = 560, Height = 330, Content = panel, Background = Brushes.Black, SystemDecorations = SystemDecorations.None };
        window.Show();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame?.Save(output);
        Console.WriteLine($"render: fonts -> {output}");
        return frame is null ? 1 : 0;
    }
}
