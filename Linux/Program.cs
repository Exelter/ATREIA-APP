using System.IO;
using Avalonia;
using AionDPS.Linux.Checks;
using AionDPS.Linux.Platform;
using AionDPS.Linux.Ui;

namespace AionDPS.Linux;

/// <summary>
/// Atreia App for Linux. Without arguments the meter's window; from a terminal also:
/// <list type="bullet">
/// <item><c>selftest</c> - the checks (Checks/LinuxSelfCheck.cs): exit code 0 when all passed.</item>
/// <item><c>capture-check</c> - what the app can see of the network and the game, and the command that gives it the right to read the network.</item>
/// <item><c>render &lt;out.png&gt; [scene]</c> - a picture of the meter, drawn without a screen (Ui/Render.cs).</item>
/// <item><c>--version</c> - the version.</item>
/// </list>
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        switch (mode)
        {
            case "selftest":
                // the checks write settings and stores of their own: into a folder of their own, not the player's
                Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", Path.Combine(Path.GetTempPath(), "atreia-selftest-" + Environment.ProcessId));
                bool ok = LinuxSelfCheck.Run();
                Console.WriteLine(ok ? "\n[selftest] ALL CHECKS PASSED" : "\n[selftest] SOME CHECKS FAILED");
                return ok ? 0 : 1;
            case "capture-check":
                return CaptureCheck.Run();
            case "render":
                return Render.Run(args);
            case "--version":
            case "version":
                Console.WriteLine($"{Brand.AppName} {Update.AppVersion.Text} (Linux)");
                return 0;
        }

        CrashLog.Hook();
        using SingleInstance? instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            Console.WriteLine($"{Brand.AppName} is already running.");
            return 0;
        }

        Update.ImagePack.ApplyWaiting(); // a pack fetched during the last run takes over now, before any picture is open
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>The app as Avalonia builds it (the name its tools look for).</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

/// <summary>One app at a time: the second start finds the first one's lock on a file in the data folder and ends (two would read the game twice and
/// write the same files).</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;

    private SingleInstance(FileStream held) => _lock = held;

    public static SingleInstance? TryAcquire()
    {
        try
        {
            string folder = AionDPS.Ui.MeterSettings.DataDirectory;
            Directory.CreateDirectory(folder);
            return new SingleInstance(new FileStream(Path.Combine(folder, "linux-app.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null; // held by the running app
        }
        catch (UnauthorizedAccessException)
        {
            return new SingleInstance(new FileStream(Path.Combine(Path.GetTempPath(), "atreia-app-" + Environment.UserName + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
    }

    public void Dispose() => _lock.Dispose();
}
