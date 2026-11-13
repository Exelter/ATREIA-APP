using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDPS.Update;

namespace AionDPS.Linux.Platform;

/// <summary>
/// Whether a newer Atreia App for Linux is out. The release workflow puts each release's Linux download on atreia.app next to the Windows one
/// (<see cref="DownloadUrl"/>) with a small index (<see cref="IndexUrl"/>: <c>{"version", "file", "sha256", "size"}</c>); the app reads the index at its
/// start and every few hours and says so on the meter's status line. The Linux app does not replace itself: the download is unpacked and installed again
/// (install.sh), which also gives the new program the right to read the network.
/// </summary>
internal static class LinuxUpdates
{
    public const string IndexUrl = Brand.UpdatesUrl + "linux.json";

    public const string DownloadUrl = Brand.Site + "/download/Atreia-App-Linux-x64.tar.gz";

    private static readonly HttpClient Http = CreateClient();

    private sealed record Index(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("size")] long Size);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd($"AtreiaApp-Linux/{AppVersion.Text}");
        return client;
    }

    /// <summary>The version on atreia.app when it is newer than this one; null when not (or when the site cannot be asked).</summary>
    public static async Task<string?> NewerAsync(CancellationToken cancel)
    {
        try
        {
            string json = await Http.GetStringAsync(IndexUrl, cancel).ConfigureAwait(false);
            return Newer(JsonSerializer.Deserialize<Index>(json)?.Version, AppVersion.Text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary><paramref name="offered"/> when it is a newer version than <paramref name="running"/>, else null.</summary>
    internal static string? Newer(string? offered, string running) =>
        offered is { Length: > 0 } && AppVersion.Parse(offered) > AppVersion.Parse(running) ? offered : null;

    /// <summary>Opens the download in the browser.</summary>
    public static void OpenDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", DownloadUrl) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to hand it to: the status line names the address
        }
    }
}
