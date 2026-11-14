using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>
/// The address of a legion's own site, as the player types it in the Atreia account window ("Your legion's own site"): a copy of the Atreia website that a
/// legion runs at its own address, with its own Discord bot (the website's kit, tools/kit.mjs there). Only https, only the address itself (a path typed with
/// it is dropped), only a name (no IP address, no single word like "localhost"), and never atreia.app or its old address (the app talks to that one anyway).
/// </summary>
public static class OwnSiteAddress
{
    /// <summary>"https://host" (with ":port" when it isn't 443), or null when <paramref name="typed"/> isn't the address of a site.</summary>
    public static string? Normalize(string? typed)
    {
        string text = (typed ?? "").Trim();
        if (text.Length is 0 or > 200 || text.Any(char.IsWhiteSpace))
        {
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0
            || uri.HostNameType != UriHostNameType.Dns)
        {
            return null;
        }

        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!host.Contains('.') || IsAtreia(host))
        {
            return null;
        }

        return uri.IsDefaultPort ? "https://" + host : $"https://{host}:{uri.Port}";
    }

    /// <summary>The address typed is atreia.app's (or its old address's), for the window's words when it is refused.</summary>
    public static bool IsAtreiaAddress(string? typed)
    {
        string text = (typed ?? "").Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.HostNameType == UriHostNameType.Dns && IsAtreia(uri.IdnHost.TrimEnd('.').ToLowerInvariant());
    }

    /// <summary>atreia.app or its old address, or one of their own names (www., admin.).</summary>
    private static bool IsAtreia(string host)
    {
        foreach (string site in new[] { Brand.Site, Brand.OldSite })
        {
            string own = new Uri(site).Host;
            if (host == own || host.EndsWith("." + own, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A page of <paramref name="site"/> (an address from <see cref="Normalize"/>): https, the same name and port, any path.</summary>
    public static bool IsOnSite([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url, string site) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? page) && Uri.TryCreate(site, UriKind.Absolute, out Uri? own)
        && page.Scheme == Uri.UriSchemeHttps && string.Equals(page.Authority, own.Authority, StringComparison.OrdinalIgnoreCase);

    /// <summary>The site's name to show ("mylegion.gg").</summary>
    public static string HostOf(string site) => Uri.TryCreate(site, UriKind.Absolute, out Uri? uri) ? uri.Authority : site;
}

/// <summary>
/// The app linked to a legion's own site (<see cref="OwnSiteAddress"/>) besides its sign-in to atreia.app (<see cref="Account"/>), and the same way: the app
/// asks that site for a short code (POST /meter/api/link/start), opens the site's /meter/link page in the browser, where the player, signed in there with
/// Discord, approves it, and gets a token of that site's own (GET /meter/api/link/poll). The address, the token and the name are kept in the data folder
/// (own-site.dat), encrypted for this Windows user like account.dat. While linked, <see cref="OwnSiteShare"/> sends that site the check of the character
/// played and the legion's member list too. Unlinking signs the app out there and forgets it; the site's Discord page can also remove it (its PCs).
/// (14 November 2026, the owner: someone running their own copy of the site should be able to "easily link their app access so that they can add legion
/// -> to their app and then transfer it to their website/discord bot".)
/// </summary>
public static class OwnSite
{
    private static readonly string FilePath = Path.Combine(Brand.DataPath, "own-site.dat");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly object Gate = new();

    private static string? _site, _token, _name;
    private static LinkStart? _pending;
    private static CancellationTokenSource? _background;

    /// <summary>The linked site's address ("https://mylegion.gg"; null when not linked).</summary>
    public static string? Site
    {
        get { lock (Gate) { return _token is null ? null : _site; } }
    }

    /// <summary>Its name to show ("mylegion.gg"; null when not linked).</summary>
    public static string? Host => Site is { } site ? OwnSiteAddress.HostOf(site) : null;

    /// <summary>The Discord name the app is signed in as there (null when unknown or not linked).</summary>
    public static string? Name
    {
        get { lock (Gate) { return _token is null ? null : _name; } }
    }

    public static bool Linked => Token is not null;

    internal static string? Token
    {
        get { lock (Gate) { return _token; } }
    }

    /// <summary>Raised after linking or unlinking, and when the name there changed (not on the UI thread).</summary>
    public static event Action? Changed;

    static OwnSite()
    {
        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(FilePath))
            {
                string[] parts = Encoding.UTF8.GetString(Account.Dpapi.Unprotect(File.ReadAllBytes(FilePath))).Split('\n', 3);
                if (parts.Length >= 2 && parts[1].Length > 0 && OwnSiteAddress.Normalize(parts[0]) is { } site && site == parts[0])
                {
                    _site = site;
                    _token = parts[1];
                    _name = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
                }
            }
        }
        catch (Exception)
        {
            // another Windows user's file, or a damaged one: not linked
            _site = _token = _name = null;
        }
    }

    /// <summary>Keeps the link (token null: none). With <paramref name="onlyIf"/>, only while that token is still the one kept (a sign-out the site
    /// reported, a name it sent: never over a link made or ended meanwhile).</summary>
    private static void Save(string? site, string? token, string? name, string? onlyIf = null)
    {
        lock (Gate)
        {
            if (onlyIf is not null && _token != onlyIf)
            {
                return;
            }

            _site = token is null ? null : site;
            _token = token;
            _name = token is null ? null : name;
            try
            {
                if (token is null)
                {
                    File.Delete(FilePath);
                }
                else if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllBytes(FilePath, Account.Dpapi.Protect(Encoding.UTF8.GetBytes(site + "\n" + token + "\n" + (name ?? ""))));
                }
            }
            catch (Exception)
            {
                // the link still holds for this run
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The site answered 401 with the token kept: the app was signed out there (its Discord page), so the link is forgotten.</summary>
    internal static void Rejected(string? usedToken)
    {
        if (usedToken is not null)
        {
            Save(null, null, null, onlyIf: usedToken);
        }
    }

    /// <summary>A link waiting for its approval in the browser: the site's address, its code (shown here and on the site's page), the secret the app asks
    /// with, the page to open.</summary>
    public sealed record LinkStart(string Site, string Code, string Poll, string Url, DateTime ExpiresAt, int Interval);

    /// <summary>The link waiting for its approval (null: none); it outlives the account window, like the atreia.app sign-in does.</summary>
    public static LinkStart? Waiting
    {
        get { lock (Gate) { return _pending is { } p && DateTime.UtcNow < p.ExpiresAt && _token is null ? p : null; } }
    }

    /// <summary>
    /// Looks at the address first (an Atreia site that answers the app, at the address it answers on: one typed with www. leads to its own), then asks that
    /// site for a sign-in code. Null with the reason, in the window's words, when it can't.
    /// </summary>
    public static async Task<(LinkStart? Start, string? Error)> StartAsync(string typed)
    {
        if (OwnSiteAddress.Normalize(typed) is not { } address)
        {
            return (null, OwnSiteAddress.IsAtreiaAddress(typed)
                ? "That is atreia.app itself: the app signs in to it with \"Sign in with Discord\" above."
                : "That isn’t a site’s address. Type it like mylegion.gg.");
        }

        string site = address;
        try
        {
            using (HttpResponseMessage policy = await Http.GetAsync($"{address}/meter/api/client-policy?version={Uri.EscapeDataString(Update.AppVersion.Text.TrimStart('v'))}"))
            {
                // where it ended up after its redirects is the site's own address
                if (policy.RequestMessage?.RequestUri is { } final)
                {
                    if (OwnSiteAddress.Normalize(final.GetLeftPart(UriPartial.Authority)) is not { } answered)
                    {
                        return (null, $"{OwnSiteAddress.HostOf(address)} leads to atreia.app or to an address that isn't a site's: nothing to link there.");
                    }

                    site = answered;
                }

                bool? allowed = null;
                if (policy.IsSuccessStatusCode)
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(await policy.Content.ReadAsStringAsync());
                        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("allowed", out JsonElement a)
                            && a.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            allowed = a.ValueKind == JsonValueKind.True;
                        }
                    }
                    catch (JsonException)
                    {
                        // a page, not the app's API: not an Atreia site
                    }
                }

                if (allowed is null)
                {
                    return (null, $"{OwnSiteAddress.HostOf(site)} isn’t a copy of the Atreia website: it doesn’t answer the app.");
                }

                if (allowed == false)
                {
                    return (null, $"{OwnSiteAddress.HostOf(site)} doesn’t take this version of the app: update the app first.");
                }
            }

            using HttpResponseMessage r = await Http.PostAsJsonAsync($"{site}/meter/api/link/start", new { label = Environment.MachineName });
            using JsonDocument start = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            JsonElement root = start.RootElement;
            if (!r.IsSuccessStatusCode)
            {
                return (null, OwnSiteAddress.HostOf(site) + ": " + Account.ErrorOf(root, r));
            }

            string code = root.GetProperty("code").GetString()!, poll = root.GetProperty("poll").GetString()!;
            // the page to approve it on: the one the site named when it is on that site, else the site's own /meter/link
            string? url = root.TryGetProperty("url", out JsonElement link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null;
            return (new LinkStart(site, code, poll, OwnSiteAddress.IsOnSite(url, site) ? url : $"{site}/meter/link?code={Uri.EscapeDataString(code)}",
                root.TryGetProperty("expiresAt", out JsonElement e) && e.ValueKind == JsonValueKind.String && e.TryGetDateTime(out DateTime at) ? at.ToUniversalTime() : DateTime.UtcNow.AddMinutes(15),
                root.TryGetProperty("interval", out JsonElement i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out int s) ? Math.Clamp(s, 2, 30) : 3), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, $"{OwnSiteAddress.HostOf(site)} can’t be reached: {ex.Message}");
        }
    }

    /// <summary>One look whether the link was approved; on Linked the app is linked to that site from then on.</summary>
    public static async Task<Account.LinkState> PollAsync(LinkStart start)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync($"{start.Site}/meter/api/link/poll?poll={Uri.EscapeDataString(start.Poll)}"));
            JsonElement root = doc.RootElement;
            string status = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out JsonElement st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
            if (status == "linked" && root.TryGetProperty("token", out JsonElement tk) && tk.ValueKind == JsonValueKind.String && tk.GetString() is { Length: > 0 } token)
            {
                Finished(start);
                Save(start.Site, token, root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
                return Account.LinkState.Linked;
            }

            return status == "expired" ? Account.LinkState.Expired : Account.LinkState.Waiting;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Account.LinkState.Failed;
        }
    }

    /// <summary>A new link started (the window asks the site itself while it is open): an older one waiting in the background stops.</summary>
    public static void Started(LinkStart start)
    {
        StopBackground();
        lock (Gate)
        {
            _pending = start;
        }
    }

    /// <summary>The link is over (approved, ran out, cancelled or started anew): nothing waits any more.</summary>
    public static void Finished(LinkStart start)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_pending, start))
            {
                _pending = null;
            }
        }
    }

    /// <summary>The window's Cancel: the waiting link is dropped (its code runs out on the site by itself).</summary>
    public static void Cancel(LinkStart start)
    {
        StopBackground();
        Finished(start);
    }

    /// <summary>An account window opens: it takes over the waiting link from the background (null when there is none).</summary>
    public static LinkStart? TakeWaiting()
    {
        LinkStart? waiting = Waiting;
        if (waiting is not null)
        {
            StopBackground();
        }

        return waiting;
    }

    /// <summary>The account window closed while its link waited: the app keeps asking the site until it is approved or runs out, and is then linked
    /// (<see cref="Changed"/>) as if the window had stayed open.</summary>
    public static void WaitInBackground(LinkStart start)
    {
        StopBackground();
        var stop = new CancellationTokenSource();
        lock (Gate)
        {
            if (!ReferenceEquals(_pending, start))
            {
                return;
            }

            _background = stop;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested && !Linked && DateTime.UtcNow < start.ExpiresAt)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(start.Interval, 3, 30)), stop.Token);
                    Account.LinkState state = await PollAsync(start);
                    if (stop.IsCancellationRequested || state is Account.LinkState.Linked)
                    {
                        return;
                    }

                    if (state is Account.LinkState.Expired)
                    {
                        break;
                    }
                }

                Finished(start);
            }
            catch (OperationCanceledException)
            {
                // an account window took it over, or a new link started
            }
        });
    }

    private static void StopBackground()
    {
        CancellationTokenSource? old;
        lock (Gate)
        {
            old = _background;
            _background = null;
        }

        old?.Cancel();
    }

    /// <summary>Asks the site who the app is signed in as there (the name refreshed; a sign-out made on the site's Discord page is noticed here).</summary>
    public static async Task RefreshAsync()
    {
        string? site = Site, token = Token;
        if (site is null || token is null)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{site}/meter/api/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage r = await Http.SendAsync(request);
            if ((int)r.StatusCode == 401)
            {
                Rejected(token);
                return;
            }

            if (r.IsSuccessStatusCode)
            {
                using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
                    && n.GetString() is { } name && name != Name)
                {
                    Save(site, token, name, onlyIf: token);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // offline: the link stays
        }
    }

    /// <summary>Unlinks: the app is signed out on that site too, and forgets it.</summary>
    public static async Task UnlinkAsync()
    {
        string? site = Site, token = Token;
        StopBackground();
        lock (Gate)
        {
            _pending = null;
        }

        Save(null, null, null);
        if (site is null || token is null)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{site}/meter/api/logout") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage _ = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // offline: the site's Discord page can still remove this PC
        }
    }
}
