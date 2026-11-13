using System.IO;
using System.Text;

namespace AionDPS;

/// <summary>
/// Linux counterpart of the Windows app's CrashLog (Client/CrashLog.cs, which also hooks WPF's own error event): what an unhandled error leaves behind,
/// <c>crash.log</c> in the data folder (~/.config/AtreiaApp), newest entry last, with the time, the version and the full exception. Past 256 KB the older
/// half is dropped. Writing never throws - the log must not make a crash worse.
/// </summary>
internal static class CrashLog
{
    private const int MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(Ui.MeterSettings.DataDirectory, "crash.log");

    /// <summary>Notes the errors nobody caught: of any thread, and of tasks nobody waited for. The window thread's are noted by the app (App.cs).</summary>
    public static void Hook()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("unhandled exception" + (e.IsTerminating ? " (the app ends)" : ""), e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => Write("task error nobody waited for", e.Exception);
    }

    public static void Write(string what, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var entry = new StringBuilder()
                    .Append("=== ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  Atreia App for Linux ").Append(Update.AppVersion.Text)
                    .Append("  ").AppendLine(what)
                    .AppendLine(ex?.ToString() ?? "(no exception object)")
                    .AppendLine();
                string existing = File.Exists(Path) ? File.ReadAllText(Path) : "";
                if (existing.Length > MaxBytes)
                {
                    int cut = existing.IndexOf("=== ", existing.Length / 2, StringComparison.Ordinal);
                    existing = cut > 0 ? existing[cut..] : "";
                }

                File.WriteAllText(Path, existing + entry);
            }
        }
        catch
        {
            // Nothing more can be done.
        }
    }
}
