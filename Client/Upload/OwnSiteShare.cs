using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AionDPS.Aion2;

namespace AionDPS.Upload;

/// <summary>
/// What the app sends atreia.app for the legion, sent to the legion's own site too while the app is linked to one (<see cref="OwnSite"/>): the check of the
/// character played (the same list as <see cref="CharacterVerify.Build"/>, POST /meter/api/verify there), so it counts as the player's on that site and in its
/// bot's Discord servers, and, with <see cref="Ui.MeterSettings.ShareLegionList"/> on, the legion's member list (<see cref="LegionShare.Build"/>, POST
/// /meter/api/legion there), so the rosters of that bot's servers that follow the legion fill by themselves (that site takes the list once the character is
/// verified there, so the check goes first). A site of its own: what atreia.app answers, or whether the app is signed in there at all, changes nothing here,
/// and the other way round. Once per character and per legion per run of the app; when the site can't take it, tried again later, quietly (after 5, 15, 45
/// minutes and so on, at most 3 hours apart); a refusal isn't sent again in this run. Only with the privacy policy accepted, and not while the app is too
/// old to upload.
/// </summary>
public static class OwnSiteShare
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) }; // the site asks NCSOFT about characters it doesn't know
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(5), LongestRetry = TimeSpan.FromHours(3);
    private static readonly object Gate = new();
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (DateTime At, int Tries)> Later = new(StringComparer.Ordinal);
    private static Func<Aion2EntityDirectory?>? _directory;
    private static System.Threading.Timer? _timer;
    private static string? _linkedWith;
    private static int _busy;

    /// <summary>How the last check of the character went on the linked site in this run (null before the first one, and after linking anew).</summary>
    public static VerifyResult? LastVerify { get; private set; }

    /// <summary>How the last send of the legion's list went on the linked site in this run (null before the first one, and after linking anew).</summary>
    public static LegionShareResult? LastLegion { get; private set; }

    /// <summary>Raised after a send had an outcome, and when the link changed (not on the UI thread).</summary>
    public static event Action? Changed;

    /// <summary>The app's start: <paramref name="directory"/> gives the live capture's directory (null while there is none); every 30 seconds it is looked at.</summary>
    public static void Start(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        if (_timer is not null)
        {
            return;
        }

        _linkedWith = OwnSite.Token;
        OwnSite.Changed += () =>
        {
            // linked anew, to another site or unlinked: everything again; the name refreshed for the same link changes nothing
            string? now = OwnSite.Token;
            if (now == _linkedWith)
            {
                return;
            }

            _linkedWith = now;
            lock (Gate)
            {
                Done.Clear();
                Later.Clear();
            }

            LastVerify = null;
            LastLegion = null;
            Changed?.Invoke();
        };
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(30));
    }

    private static async Task TickAsync()
    {
        if (!OwnSite.Linked || _directory?.Invoke() is not { } directory || !Ui.PrivacyConsent.CanUpload || UploadClient.IsBlocked
            || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (CharacterVerify.Build(directory.LocalCharacter, directory.AccountCharacters()) is { } check)
            {
                await VerifyAsync(check);
            }

            if (Ui.MeterSettings.Load().ShareLegionList
                && LegionShare.Build(directory.LocalCharacter, directory.AccountCharacters(), directory.LegionRoster()) is { } list)
            {
                await LegionAsync(list, now: false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException)
        {
            // nothing to do now: the next tick looks again
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// The list kept on this PC (the Legion page's "Send this list to the website now", the game need not run), to the linked site now, whatever was sent
    /// before in this run. Null when it can't be sent (not linked, uploads off, no list).
    /// </summary>
    public static async Task<LegionShareResult?> SendLegionNowAsync(History.LegionSnapshot snapshot)
    {
        if (!OwnSite.Linked || !Ui.PrivacyConsent.CanUpload || UploadClient.IsBlocked || LegionShare.FromSnapshot(snapshot) is not { } upload)
        {
            return null;
        }

        try
        {
            await LegionAsync(upload, now: true);
            return LastLegion;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException)
        {
            return new LegionShareResult(upload.Legion, upload.Members.Count, "later");
        }
    }

    private static async Task VerifyAsync(VerifyUpload upload)
    {
        string key = "verify|" + upload.Character.Name + "|" + upload.Character.ServerId;
        if (Waits(key) || OwnSite.Site is not { } site || OwnSite.Token is not { } token)
        {
            return;
        }

        string name = upload.Character.Name, server = CharacterVerify.ServerName(upload.Character.ServerId);
        (int Status, string Body)? answer = await PostAsync(site, token, "/meter/api/verify", upload);
        if (answer is not { } a)
        {
            PutOff(key);
            Tell(verify: new VerifyResult(name, server, "later"));
            return;
        }

        if (a.Status == 401)
        {
            OwnSite.Rejected(token); // signed out on that site: linking again starts over (OwnSite.Changed)
            return;
        }

        (bool ok, _, string? code, string? shownName, string? shownServer) = CharacterVerify.Read(a.Body);
        if (a.Status == 200 && ok)
        {
            Finish(key);
            Tell(verify: new VerifyResult(shownName ?? name, shownServer ?? server, "verified"));
            return;
        }

        // the site's refusals stand for this run; anything else (NCSOFT busy, a character not listed yet, too many checks, the site down) is asked again later
        string? refused = code switch { "MISMATCH" or "INVALID_INPUT" => "mismatch", "TAKEN" => "taken", "TOO_MANY" => "full", _ => null };
        if (refused is not null)
        {
            Finish(key);
        }
        else
        {
            PutOff(key);
        }

        Tell(verify: new VerifyResult(name, server, refused ?? "later"));
    }

    private static async Task LegionAsync(LegionUpload upload, bool now)
    {
        string key = "legion|" + upload.Legion + "|" + upload.Character.ServerId;
        if ((!now && Waits(key)) || OwnSite.Site is not { } site || OwnSite.Token is not { } token)
        {
            return;
        }

        (int Status, string Body)? answer = await PostAsync(site, token, "/meter/api/legion", upload);
        if (answer is not { } a)
        {
            PutOff(key);
            Tell(legion: new LegionShareResult(upload.Legion, upload.Members.Count, "later"));
            return;
        }

        if (a.Status == 401)
        {
            OwnSite.Rejected(token);
            return;
        }

        (bool ok, int known, int lookup, string? code, string? error, bool kept) = LegionShare.Read(a.Body);
        if (a.Status == 200 && ok && kept)
        {
            Finish(key);
            Tell(legion: new LegionShareResult(upload.Legion, upload.Members.Count, "sent", known, lookup));
        }
        else if (code is "INVALID_INPUT" or "LEGION_MISMATCH")
        {
            // the site's refusals stand for this run (a legion the site has under another name is read again there every day)
            Finish(key);
            Tell(legion: new LegionShareResult(upload.Legion, upload.Members.Count, "refused", Message: error));
        }
        else
        {
            // sent a moment ago (the site takes one list every few minutes from a player), the character not verified there yet, the site busy
            PutOff(key);
            Tell(legion: new LegionShareResult(upload.Legion, upload.Members.Count, "later", Message: error));
        }
    }

    /// <summary>One POST to the linked site with its token: the answer's status and text, or null when the site can't be reached.</summary>
    private static async Task<(int Status, string Body)?> PostAsync<T>(string site, string token, string path, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, site + path) { Content = JsonContent.Create(body, options: CharacterVerify.JsonOptions) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static bool Waits(string key)
    {
        lock (Gate)
        {
            return Done.Contains(key) || (Later.TryGetValue(key, out var later) && DateTime.UtcNow < later.At);
        }
    }

    private static void Finish(string key)
    {
        lock (Gate)
        {
            Done.Add(key);
            Later.Remove(key);
        }
    }

    /// <summary>Tried again later: 5 minutes after the first failure, three times as long after each next one, at most 3 hours.</summary>
    private static void PutOff(string key)
    {
        lock (Gate)
        {
            int tries = Later.TryGetValue(key, out var was) ? was.Tries + 1 : 1;
            double minutes = Math.Min(LongestRetry.TotalMinutes, FirstRetry.TotalMinutes * Math.Pow(3, Math.Min(tries - 1, 6)));
            Later[key] = (DateTime.UtcNow.AddMinutes(minutes), tries);
        }
    }

    private static void Tell(VerifyResult? verify = null, LegionShareResult? legion = null)
    {
        if (verify is not null)
        {
            LastVerify = verify;
        }

        if (legion is not null)
        {
            LastLegion = legion;
        }

        Changed?.Invoke();
    }
}
