using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AionDPS.Linux.Meter;
using AionDPS.Linux.Platform;
using AionDPS.Ui;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The pictures of the build (Program "render &lt;out.png&gt; [scene]"), drawn without a screen, as the Windows app's render modes draw its windows for the
/// checks of its look: the meter over a dark stand-in for the game, filled from the made-up boss fight (<see cref="SampleFight"/>) through the real engine.
/// Scenes: meter (damage), heal, taken, waiting (no game yet), permission (the right to read the network missing).
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

        // which faces the app carries and which one the meter's text gets (the Linux workflow prints it)
        try
        {
            var assets = Avalonia.Platform.AssetLoader.GetAssets(new Uri("avares://AtreiaApp/"), null).Select(u => u.AbsolutePath).ToList();
            Console.WriteLine($"render: {assets.Count} resources: {string.Join(", ", assets.Take(12))}");
            foreach (FontWeight weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold })
            {
                bool found = FontManager.Current.TryGetGlyphTypeface(new Typeface(Palette.UiFont, FontStyle.Normal, weight), out IGlyphTypeface? face);
                Console.WriteLine($"render: the meter's face {Palette.UiFont} {weight} -> {(found ? face!.FamilyName + " " + face.Weight : "not found")}; default {FontManager.Current.DefaultFontFamily.Name}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("render: faces could not be listed: " + ex.Message);
        }

        using var engine = new MeterEngine();
        var settings = new MeterSettings { AutoScope = false, ViewScope = "Group" };
        if (scene is not ("waiting" or "permission"))
        {
            engine.Start(settings, new SampleFight(DateTime.Now.AddSeconds(-115)));
            engine.Tick();
            engine.Mode = scene switch { "heal" => MeterEngine.MeterMode.Heal, "taken" => MeterEngine.MeterMode.Taken, _ => MeterEngine.MeterMode.Damage };
        }
        else
        {
            engine.ApplySettings(settings);
        }

        var view = new MeterView(engine);
        view.Update();
        (string text, bool warning, string? action) = StatusLine.For(scene == "permission" ? CaptureRights.Rights.NotGiven : CaptureRights.Rights.Granted, engine, null, null);
        view.ShowStatus(text, warning, action);
        view.Width = 470;
        view.Height = scene is "waiting" or "permission" ? 230 : 330;

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
            Width = view.Width + 40,
            Height = view.Height + 40,
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

        TextBlock? sample = view.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text is { Length: > 0 });
        Console.WriteLine($"render: a line of the meter (\"{sample?.Text}\") is drawn in {sample?.FontFamily} {sample?.FontWeight}");
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
}
