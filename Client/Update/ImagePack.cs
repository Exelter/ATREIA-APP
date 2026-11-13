using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Update;

/// <summary>
/// The game's pictures as one download (9 November 2026, the owner: get the game's images out of the repository and host them on atreia.app; the app
/// downloads one image pack on its first start and keeps it under %APPDATA%). The website says which pack is current in
/// <see cref="IndexUrl"/>: <c>{"version", "file" (or "url"), "size", "sha256", "files"}</c>; the site's tools/app-images.mjs writes it with the pack.
/// The pack is a plain zip of the pictures, laid out like the app's assets folder (aion2/items/icons/…, classes/icons/…), unpacked into
/// %APPDATA%\AtreiaApp\images (<see cref="GameArt.PackFolder"/>).
/// <list type="bullet">
/// <item>First start (no pack yet): before the meter's window, a small window shows the download; Skip leaves the app without pictures until the next
/// start (<see cref="FirstStart"/>).</item>
/// <item>Every later start, in the background: a newer pack is fetched into images.next and its pictures are used right away (9 November 2026, the owner:
/// two restarts before a new picture showed would only confuse players). At the next start, before any picture is open, it moves into the place of the
/// old one (<see cref="ApplyWaiting"/>).</item>
/// </list>
/// The pack's SHA-256 is checked before anything is unpacked, and only pictures (.png, .jpg, .webp) inside the folder are taken from it.
/// </summary>
public static partial class ImagePack
{
    /// <summary>The index of the current pack, next to the update index on the website.</summary>
    public const string IndexUrl = Brand.UpdatesUrl + "images.json";

    private const string Marker = "pack.json";
    private static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".webp" };

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The website's description of the current pack.</summary>
    public sealed record PackIndex(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("files")] int Files);

    private sealed record Installed(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("files")] int Files,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("installed")] DateTime At);

    private static string Current => GameArt.PackBase;

    private static string Next => GameArt.PackBase + ".next";

    private static string Old => GameArt.PackBase + ".old";

    private static HttpClient CreateClient()
    {
        // No redirects: the pack and its index come from the website itself (or from where its index names, checked in Where).
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd($"AtreiaApp/{AppVersion.Text}");
        return client;
    }

    /// <summary>A newer pack came in while the app runs and is in use now (raised on a worker thread): windows drawn from now on show its pictures.</summary>
    public static event Action? Arrived;

    /// <summary>The version of the pack in use, null when there is none.</summary>
    public static string? InstalledVersion => ReadMarker(Current)?.Version;

    private static Installed? ReadMarker(string folder)
    {
        try
        {
            string path = Path.Combine(folder, Marker);
            return File.Exists(path) ? JsonSerializer.Deserialize<Installed>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>At the start, before any window: a pack fetched during an earlier run takes over now, while none of the pictures is open.</summary>
    public static void ApplyWaiting()
    {
        try
        {
            if (ReadMarker(Next) is not { } waiting)
            {
                Remove(Next); // an unfinished one
                return;
            }

            Remove(Old);
            if (Directory.Exists(Current))
            {
                Directory.Move(Current, Old);
            }

            Directory.Move(Next, Current);
            Log($"now using pack {waiting.Version} ({waiting.Files} pictures)");
            Remove(Old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("the new pack could not take over yet: " + ex.Message);
            if (!Directory.Exists(Current) && Directory.Exists(Old))
            {
                try
                {
                    Directory.Move(Old, Current);
                }
                catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                {
                    // nothing more to do: the next start fetches a pack again
                }
            }
        }
    }

    /// <summary>Every later start, in the background: a newer pack on the website is fetched, and takes over at the next start. Never throws.</summary>
    public static async Task CheckInBackgroundAsync()
    {
        try
        {
            if (GameArt.Bundled || ReadMarker(Current) is not { } installed)
            {
                return; // pictures from the build, or none at all (the first start asks for them)
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            if (await LatestAsync(timeout.Token) is not { } index || index.Version == installed.Version)
            {
                return;
            }

            if (ReadMarker(Next)?.Version == index.Version)
            {
                // fetched before and still waiting (it could not move into place at this start): in use now
                GameArt.Use(Next);
                Arrived?.Invoke();
                return;
            }

            if (await FetchAsync(index, null, timeout.Token))
            {
                // its pictures right away; the old pack's files stay where they are (some may be open), and the new one moves into their place at the next start
                GameArt.Use(Next);
                Log($"pack {index.Version} is in use now, and moves into place at the next start");
                Arrived?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log("the check for a newer pack failed: " + ex.Message);
        }
    }

    /// <summary>The website's index of the current pack; null when it cannot be reached or says nothing usable.</summary>
    public static async Task<PackIndex?> LatestAsync(CancellationToken cancel)
    {
        try
        {
            using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            quick.CancelAfter(TimeSpan.FromSeconds(20));
            string json = await Http.GetStringAsync(IndexUrl, quick.Token).ConfigureAwait(false);
            PackIndex? index = JsonSerializer.Deserialize<PackIndex>(json);
            if (index is { Version.Length: > 0, Sha256.Length: 64 } && Where(index) is not null)
            {
                return index;
            }

            Log("the website's images.json names no usable pack");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            Log("images.json could not be read: " + ex.Message);
        }

        return null;
    }

    /// <summary>Where the pack is: its "url" when it is on the website (or a subdomain of it, like its file storage), else its "file" next to the index.</summary>
    public static Uri? Where(PackIndex index)
    {
        if (index.Url is { Length: > 0 } url)
        {
            string site = new Uri(Brand.Site).Host;
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? at) && at.Scheme == Uri.UriSchemeHttps
                && (at.Host == site || at.Host.EndsWith("." + site, StringComparison.OrdinalIgnoreCase)) ? at : null;
        }

        return index.File is { Length: > 0 } file && Regex.IsMatch(file, "^[A-Za-z0-9][A-Za-z0-9._-]*$") ? new Uri(new Uri(IndexUrl), file) : null;
    }

    /// <summary>
    /// Downloads the pack, checks its size and SHA-256, and unpacks its pictures into images.next (complete once its pack.json is written). False when
    /// anything failed or was cancelled; nothing half-done is left behind.
    /// </summary>
    public static async Task<bool> FetchAsync(PackIndex index, Action<long, long>? progress, CancellationToken cancel)
    {
        if (Where(index) is not { } from)
        {
            return false;
        }

        string zip = Current + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Current)!);
            using (HttpResponseMessage response = await Http.GetAsync(from, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? index.Size;
                await using Stream body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                await using var file = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[1 << 16];
                long got = 0, shown = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    got += read;
                    if (got > MaxPackBytes || (index.Size > 0 && got > index.Size))
                    {
                        throw new InvalidDataException($"more than {(index.Size > 0 ? index.Size : MaxPackBytes)} bytes came"); // the disk isn't filled first
                    }

                    if (got - shown >= 256 * 1024)
                    {
                        shown = got;
                        progress?.Invoke(got, total);
                    }
                }

                progress?.Invoke(got, total);
                if (index.Size > 0 && got != index.Size)
                {
                    throw new InvalidDataException($"{got} bytes came, the index says {index.Size}");
                }

                if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), index.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("the pack does not match its SHA-256");
                }
            }

            int count = await Task.Run(() => Unpack(zip, index, cancel), cancel).ConfigureAwait(false);
            Log($"fetched pack {index.Version} from {from}: {count} pictures");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        {
            Log($"pack {index.Version} could not be fetched: {ex.Message}");
            Remove(Next);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // left for the next try, which writes over it
            }
        }
    }

    /// <summary>
    /// The most a pack may hold (12 November 2026, the security check): a picture of 8 MB, 512 MB in all, 50,000 pictures. The pack of November 2026 holds
    /// about 5,000 pictures of a few KB each. The pack's SHA-256 comes from the same site as the pack, so it can't vouch for the pack's size: a pack that
    /// unpacks into more (a "zip bomb", only if the site were ever taken over) stops with nothing kept, instead of filling the disk.
    /// </summary>
    internal const long MaxPictureBytes = 8L << 20, MaxPackBytes = 512L << 20;
    internal const int MaxPictures = 50_000;

    /// <summary>The pictures of the pack into images.next, nothing else and nothing outside it; pack.json last, as the sign that it is complete.</summary>
    internal static int Unpack(string zip, PackIndex index, CancellationToken cancel)
    {
        Remove(Next);
        string root = Path.GetFullPath(Next) + Path.DirectorySeparatorChar;
        int count = 0;
        long unpacked = 0;
        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancel.ThrowIfCancellationRequested();
                if (entry.Name.Length == 0 || !Pictures.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()))
                {
                    continue; // a folder, or something that is not a picture
                }

                string path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (count >= MaxPictures || entry.Length > MaxPictureBytes)
                {
                    throw new InvalidDataException("the pack holds more than a picture pack can");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                unpacked += Extract(entry, path, Math.Min(MaxPictureBytes, MaxPackBytes - unpacked));
                count++;
            }
        }

        if (count == 0)
        {
            throw new InvalidDataException("the pack holds no pictures");
        }

        File.WriteAllText(Path.Combine(Next, Marker), JsonSerializer.Serialize(new Installed(index.Version, count, index.Sha256, DateTime.UtcNow)));
        return count;
    }

    /// <summary>One picture to its file, counted as it is unpacked (the size a zip states for a file can be wrong): more than <paramref name="room"/> bytes
    /// stops the pack.</summary>
    private static long Extract(ZipArchiveEntry entry, string path, long room)
    {
        using Stream from = entry.Open();
        using FileStream to = File.Create(path);
        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > room)
            {
                throw new InvalidDataException("the pack holds more than a picture pack can");
            }

            to.Write(buffer, 0, read);
        }

        return written;
    }

    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"{folder} could not be removed yet: {ex.Message}");
        }
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(MeterSettings.DataDirectory, "images.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // nowhere to write: nothing else to do
        }
    }
}
