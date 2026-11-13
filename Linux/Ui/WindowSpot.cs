using System.IO;
using System.Text.Json;

namespace AionDPS.Linux.Ui;

/// <summary>
/// Where the meter stands on the screen and how it was left (locked, on top), kept in the data folder next to the shared settings
/// (~/.config/AtreiaApp/linux-window.json): the shared settings file is the Windows app's too, and its window places mean nothing here.
/// </summary>
internal sealed class WindowSpot
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public double Width { get; set; } = 470;

    public double Height { get; set; } = 330;

    public bool Locked { get; set; }

    public bool Topmost { get; set; } = true;

    private static string FilePath => Path.Combine(AionDPS.Ui.MeterSettings.DataDirectory, "linux-window.json");

    public static WindowSpot Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<WindowSpot>(File.ReadAllText(FilePath)) is { } spot)
            {
                spot.Width = Math.Clamp(spot.Width, 280, 4000);
                spot.Height = Math.Clamp(spot.Height, 120, 4000);
                return spot;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a damaged file: the meter starts where it starts the first time
        }

        return new WindowSpot();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept for this run only
        }
    }
}
