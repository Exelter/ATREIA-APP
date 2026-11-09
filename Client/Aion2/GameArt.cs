using System.IO;

namespace AionDPS.Aion2;

/// <summary>
/// Where the game's pictures are. 9 November 2026, the owner: the game's images (NCSOFT's item, skill, pet, boss and wing icons, the maps, the Daevanion
/// tiles, the class and race emblems) are taken out of the public repository and hosted on atreia.app, not encrypted, renamed or disguised. The app no
/// longer ships them: it downloads them once as an image pack from the website (Update/ImagePack) into %APPDATA%\AtreiaApp\images, laid out like its own
/// assets folder (aion2\items\icons\…, aion2\maps\altgard\…, classes\icons\…, races\icons\…).
/// <para>A picture is looked for in the pack first, then in the app's own assets folder: a build on a developer's PC or on the build machines has the
/// pack unpacked there (Tools/release/images.ps1). A missing picture is never an error: the windows draw their plain tile, as they always did.</para>
/// </summary>
public static class GameArt
{
    private static readonly string AppAssets = Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>The unpacked image pack: %APPDATA%\AtreiaApp\images (or in the data folder the tests set).</summary>
    public static string PackFolder => Path.Combine(global::AionDPS.Ui.MeterSettings.DataDirectory, "images");

    /// <summary>The pictures came with this build (unpacked into its assets folder: a developer's build, the checks): nothing to download.</summary>
    public static bool Bundled => Directory.Exists(Path.Combine(AppAssets, "aion2", "skills", "icons"))
        && Directory.EnumerateFiles(Path.Combine(AppAssets, "aion2", "skills", "icons"), "*.png").Any();

    /// <summary>
    /// A picture by its place under assets (<c>"aion2", "items", "icons", "Icon_X.png"</c> or <c>"aion2/items/icons/Icon_X.png"</c>): the pack's copy when
    /// it has one, else the app folder's. That one may be missing too; callers check, as they did before.
    /// </summary>
    public static string PathOf(params string[] parts)
    {
        string relative = Relative(parts);
        string packed = Path.Combine(PackFolder, relative);
        return File.Exists(packed) ? packed : Path.Combine(AppAssets, relative);
    }

    /// <summary>A folder of pictures by its place under assets (<c>"aion2", "maps", "altgard"</c>): the pack's when it has it, else the app folder's.</summary>
    public static string FolderOf(params string[] parts)
    {
        string relative = Relative(parts);
        string packed = Path.Combine(PackFolder, relative);
        return Directory.Exists(packed) ? packed : Path.Combine(AppAssets, relative);
    }

    private static string Relative(string[] parts) =>
        Path.Combine(parts.Select(p => p.Replace('/', Path.DirectorySeparatorChar)).ToArray());
}
