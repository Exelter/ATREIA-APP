using System.IO;

namespace AionDPS.Linux.Platform;

/// <summary>
/// The Linux app's own notes on what it did (linux.log in the data folder, ~/.config/AtreiaApp): how the capture started, whether the clicks go through
/// the locked meter, what the update check found. For a player's report and the workflow's check of the window; past 128 KB the older half goes.
/// Never throws.
/// </summary>
internal static class LinuxLog
{
    private const int MaxBytes = 128 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AionDPS.Ui.MeterSettings.DataDirectory, "linux.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    string text = File.ReadAllText(FilePath);
                    File.WriteAllText(FilePath, text[(text.Length / 2)..]);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nothing more to do
        }
    }
}
