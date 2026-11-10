using System.IO;

namespace AionDPS.Aion2;

/// <summary>
/// A small local log of the PvP overlays (never uploaded), next to the settings (pvp-log.txt): what the radar, the enemy card and the death recap saw and
/// whether they showed, and why not (11 November 2026: they showed nothing after a fight in the Abyss, and nothing on the PC told why). A line only when
/// an overlay's state changes; trimmed when it grows.
/// </summary>
public static class PvpLog
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Last = new();

    /// <summary>A test writes to its own file instead of the real log.</summary>
    public static string? PathOverride { get; set; }

    public static string Path_ => PathOverride ?? Path.Combine(Brand.DataPath, "pvp-log.txt");

    /// <summary>Writes "topic: detail" when <paramref name="state"/> differs from the last one of the same topic.</summary>
    public static void Note(string topic, string state, string detail)
    {
        lock (Gate)
        {
            if (Last.TryGetValue(topic, out string? last) && last == state)
            {
                return;
            }

            Last[topic] = state;
        }

        Write(topic + ": " + detail);
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                string path = Path_;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 300_000)
                {
                    File.WriteAllLines(path, File.ReadLines(path).TakeLast(1500));
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
