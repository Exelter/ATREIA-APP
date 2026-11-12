using System.IO;
using System.IO.Compression;
using AionDPS.Update;

namespace AionDPS.Combat;

/// <summary>The security check of 12 November 2026: only https:// web pages are handed to Windows to open (Links.cs), and the picture pack unpacks only
/// pictures, only into its own folder and only up to its limits (Update/ImagePack.cs).</summary>
public static class SelfCheckSafety
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Safety (links the app opens, the picture pack):");
        bool ok = true;

        string[] good = { "https://atreia.app/meter/logs/abcdefghjk", "https://discord.gg/atreia", "https://aion2.plaync.com/en-us/board/notice/view?articleId=1" };
        string?[] bad =
        {
            null, "", "http://atreia.app/", "file:///C:/Windows/System32/calc.exe", @"\\attacker\share\x.exe", "C:\\Windows\\notepad.exe", "ms-settings:privacy",
            "search-ms:query=x", "javascript:alert(1)", "https://user:pass@atreia.app/", " https://atreia.app/", "https://atreia.app/\n", "https://", "HTTPS:atreia.app",
        };
        bool links = good.All(Links.IsWeb) && !bad.Any(Links.IsWeb);
        Console.WriteLine($"  -> only https web pages are opened: {links}");
        ok &= links;

        string? before = Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR");
        string folder = Path.Combine(Path.GetTempPath(), "a2h-selftest-safety-" + Environment.ProcessId);
        try
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", folder);
            Directory.CreateDirectory(folder);
            var index = new ImagePack.PackIndex("selftest", null, null, 0, new string('0', 64), 0);

            // pictures in, and only them; nothing outside the pack's folder
            string zip = Path.Combine(folder, "pack.zip");
            using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                Write(z, "items/a.png", 100);
                Write(z, "b.webp", 50);
                Write(z, "../escape.png", 10);
                Write(z, "notes.txt", 10);
            }

            int count = ImagePack.Unpack(zip, index, CancellationToken.None);
            bool kept = count == 2 && File.Exists(Path.Combine(folder, "images.next", "items", "a.png")) && !File.Exists(Path.Combine(folder, "escape.png"))
                && !File.Exists(Path.Combine(folder, "images.next", "notes.txt"));
            Console.WriteLine($"  -> the pack's pictures, inside its folder only: {kept} ({count} pictures)");
            ok &= kept;

            // a picture that unpacks into more than a picture can be (a "zip bomb"): the pack stops
            string bomb = Path.Combine(folder, "bomb.zip");
            using (ZipArchive z = ZipFile.Open(bomb, ZipArchiveMode.Create))
            {
                Write(z, "big.png", (int)ImagePack.MaxPictureBytes + 1);
            }

            bool stopped = false;
            try
            {
                ImagePack.Unpack(bomb, index, CancellationToken.None);
            }
            catch (InvalidDataException)
            {
                stopped = true;
            }

            Console.WriteLine($"  -> a pack over the limits is refused: {stopped}");
            ok &= stopped;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.WriteLine($"  -> the pack check could not run: {ex.Message}");
            ok = false;
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", before);
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a temp folder left behind
            }
        }

        return ok;
    }

    private static void Write(ZipArchive zip, string name, int bytes)
    {
        using Stream to = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        to.Write(new byte[bytes]);
    }
}
