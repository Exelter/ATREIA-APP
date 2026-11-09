using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Update;

/// <summary>
/// The game's pictures as one download (9 November 2026, the owner: get the game's images out of the repository and host them on atreia.app; the app
/// downloads one image pack on its first start and keeps it under %APPDATA%). The website says which pack is current in
/// <see cref="IndexUrl"/>: <c>{"version", "file" (or "url"), "size", "sha256", "files"}</c>; the site's tools/app-images.mjs writes it with the pack.
/// The pack is a plain zip of the pictures, laid out like the app's assets folder (aion2/items/icons/…, classes/icons/…), unpacked into
/// %APPDATA%\A2HMeter\images (<see cref="GameArt.PackFolder"/>).
/// <list type="bullet">
/// <item>First start (no pack yet): before the meter's window, a small window shows the download; Skip leaves the app without pictures until the next
/// start (<see cref="FirstStart"/>).</item>
/// <item>Every later start, in the background: a newer pack is fetched into images.next and takes over at the start after (<see cref="ApplyWaiting"/>),
/// before any picture is open.</item>
/// </list>
/// The pack's SHA-256 is checked before anything is unpacked, and only pictures (.png, .jpg, .webp) inside the folder are taken from it.
/// </summary>
public static class ImagePack
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

    private static string Current => GameArt.PackFolder;

    private static string Next => GameArt.PackFolder + ".next";

    private static string Old => GameArt.PackFolder + ".old";

    private static HttpClient CreateClient()
    {
        // No redirects: the pack and its index come from the website itself (or from where its index names, checked in Where).
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd($"AtreiaApp/{AppVersion.Text}");
        return client;
    }

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

    /// <summary>
    /// The first start (no pack yet, and none came with the build): the pictures are downloaded before the meter's window opens, in a small window with
    /// the progress and a Skip button. Offline, or when the website has no pack, the window closes by itself and the app starts without pictures.
    /// </summary>
    public static void FirstStart()
    {
        if (GameArt.Bundled || ReadMarker(Current) is not null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        // not disposed here: the download's last steps can still look at it after the window closed
        var cancel = new CancellationTokenSource();
        var text = new TextBlock { Text = loc["Images.Fetching"], TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, IsIndeterminate = true, Margin = new Thickness(0, 14, 0, 6) };
        var amount = new TextBlock { FontSize = 12, Text = " " };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var skip = new Button
        {
            Content = loc["Images.Skip"], MinWidth = 84, Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true,
        };
        var panel = new StackPanel { Width = 380, Margin = new Thickness(18, 16, 18, 16) };
        panel.Children.Add(text);
        panel.Children.Add(bar);
        panel.Children.Add(amount);
        panel.Children.Add(skip);
        var window = new Window
        {
            Title = Brand.AppName, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        ThemedChrome.Apply(window);
        window.ResizeMode = ResizeMode.NoResize;

        bool closed = false;
        void Close()
        {
            if (!closed)
            {
                window.Close();
            }
        }

        window.Closed += (_, _) =>
        {
            closed = true;
            cancel.Cancel();
        };
        skip.Click += (_, _) =>
        {
            Log("skipped by the player");
            Close();
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                PackIndex? index = await LatestAsync(cancel.Token);
                if (index is not null)
                {
                    bar.IsIndeterminate = false;
                    void Show(long got, long total)
                    {
                        bar.Value = total > 0 ? Math.Min(1, got / (double)total) : 0;
                        amount.Text = string.Format(loc["Images.Progress"], got / 1048576.0, total / 1048576.0);
                    }

                    bool fetched = await FetchAsync(index, (got, total) => window.Dispatcher.InvokeAsync(() => Show(got, total)), cancel.Token);
                    if (fetched)
                    {
                        ApplyWaiting();
                    }
                }
            }
            catch (Exception ex)
            {
                Log("the first download failed: " + ex.Message);
            }

            Close();
        };
        window.ShowDialog();

        // This was the app's first window, so WPF made it the app's main window: the meter's window, created next, has to take that place itself (the
        // other windows take their styles from it, and closing it ends the app).
        try
        {
            if (Application.Current is { } app && ReferenceEquals(app.MainWindow, window))
            {
                app.MainWindow = null;
            }
        }
        catch (InvalidOperationException)
        {
            // Program sets the meter's window as the main window right after creating it anyway
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
            if (await LatestAsync(timeout.Token) is not { } index || index.Version == installed.Version || ReadMarker(Next)?.Version == index.Version)
            {
                return;
            }

            if (await FetchAsync(index, null, timeout.Token))
            {
                Log($"pack {index.Version} is ready and takes over at the next start");
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

    /// <summary>The pictures of the pack into images.next, nothing else and nothing outside it; pack.json last, as the sign that it is complete.</summary>
    private static int Unpack(string zip, PackIndex index, CancellationToken cancel)
    {
        Remove(Next);
        string root = Path.GetFullPath(Next) + Path.DirectorySeparatorChar;
        int count = 0;
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

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
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
