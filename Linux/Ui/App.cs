using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AionDPS.Linux.Meter;
using AionDPS.Update;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The Linux app: the dark Fluent look, then the meter's window over the game. On the first start the game's pictures (the image pack, Update/ImagePack.cs)
/// come in the background, the meter showing the download on its status line; later starts look for a newer pack the same way.
/// </summary>
internal sealed class App : Application
{
    /// <summary>Set by the picture mode (Program "render"): no window opens by itself.</summary>
    internal static bool Headless { get; set; }

    public override void Initialize()
    {
        Name = Brand.AppName;
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (!Headless && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                CrashLog.Write("error in the window thread", e.Exception);
                e.Handled = true; // noted; one bad click must not end the meter
            };

            AionDPS.Ui.MeterSettings settings = AionDPS.Ui.MeterSettings.Load();
            if (settings.Language.Length > 0)
            {
                AionDPS.Ui.LocalizationManager.Instance.Language = settings.Language;
            }

            var engine = new MeterEngine();
            var window = new MeterWindow(engine);
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.StartCapture(settings);
            _ = FetchPicturesAsync(window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The image pack: on the first start fetched with its progress on the meter's status line; later, a newer one in the background.</summary>
    private static async Task FetchPicturesAsync(MeterWindow window)
    {
        try
        {
            if (Aion2.GameArt.Bundled || ImagePack.InstalledVersion is not null)
            {
                await ImagePack.CheckInBackgroundAsync();
                return;
            }

            window.ShowImages("Getting the game's pictures…");
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            ImagePack.PackIndex? index = await ImagePack.LatestAsync(cancel.Token);
            if (index is null)
            {
                window.ShowImages(null);
                return;
            }

            bool fetched = await ImagePack.FetchAsync(index, (got, total) => Dispatcher.UIThread.Post(() =>
                window.ShowImages(total > 0 ? $"Getting the game's pictures: {got / 1048576.0:0} of {total / 1048576.0:0} MB" : "Getting the game's pictures…")), cancel.Token);
            if (fetched)
            {
                ImagePack.ApplyWaiting();
                ClassLook.Forget();
                Pictures.Forget();
            }

            window.ShowImages(null);
        }
        catch (Exception ex)
        {
            CrashLog.Write("the image pack", ex);
            Dispatcher.UIThread.Post(() => window.ShowImages(null));
        }
    }
}
