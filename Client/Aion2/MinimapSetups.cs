using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2;

/// <summary>
/// How far the minimap reading is off on a game window of a given size before this PC has learnt its own offset (10 November 2026, the owner: "make sure to
/// account for differences in resolution"). Everyone's map checks showed every PC reading off by an amount of its own, 0 to 30 m and the same all over a map
/// (on Altgard four PCs alike at one offset, two others alike at another): the screen and minimap layout, not the map. The app learns its own within minutes
/// of walking (<see cref="MinimapCalibration"/>, per map and window size); until then the offset measured for its window size on everyone's checks is taken
/// (assets/aion2/maps/minimap_setups.json: per window size the offset in minimap pixels, so it holds at any minimap zoom; the checks carry the window size and
/// zoom from 1.21.9 and Tools/maps/fit_maps.py prints the offsets per resolution). A window size not in the table: none.
/// </summary>
public static class MinimapSetups
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "minimap_setups.json");
    private static Dictionary<string, (double X, double Y)>? _offsets;

    /// <summary>The offset (world units) a reading on a window of <paramref name="windowKey"/> ("2560x1440") is put right by, at <paramref name="metresPerPixel"/>
    /// minimap metres a pixel; (0, 0) for a window size not measured.</summary>
    public static (double X, double Y) PriorFor(string windowKey, double metresPerPixel)
    {
        var offsets = Load();
        return offsets.TryGetValue(windowKey, out var px) && metresPerPixel is > 0 and < 20 ? (px.X * metresPerPixel * 100, px.Y * metresPerPixel * 100) : (0, 0);
    }

    /// <summary>The table, for the self-check.</summary>
    internal static IReadOnlyDictionary<string, (double X, double Y)> Offsets => Load();

    private static Dictionary<string, (double X, double Y)> Load()
    {
        if (_offsets is { } cached)
        {
            return cached;
        }

        var result = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                if (doc.RootElement.TryGetProperty("offsets", out JsonElement list) && list.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in list.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() == 2)
                        {
                            result[p.Name] = (p.Value[0].GetDouble(), p.Value[1].GetDouble());
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException)
        {
            // no table: no prior, the PC learns its own as before
        }

        return _offsets = result;
    }
}
