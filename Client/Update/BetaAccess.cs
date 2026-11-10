using System.Net.Http;
using System.Text.Json;
using AionDPS.Upload;

namespace AionDPS.Update;

/// <summary>
/// Whether this app takes the betas: signed in with Discord (Account), and that Discord has the Contributor role in the Atreia Discord while a
/// beta is out. The site answers (GET /meter/api/beta, the site's src/beta.js betaFor); the answer is kept for an hour, so the update checks
/// every few minutes ask the site at most once an hour. Signed out, offline or anything unexpected: no betas, the normal updates go on as before.
/// (10 November 2026, the owner: "they link their discord as it is, can't they just get the download automatically because they have contributor".)
/// </summary>
public static class BetaAccess
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly TimeSpan Keep = TimeSpan.FromHours(1);

    private sealed record Answer(bool Yes, DateTime At, string Token);

    private static volatile Answer? _last;

    /// <summary>True when the site says this sign-in is a Contributor's and a beta is out.</summary>
    public static async Task<bool> ActiveAsync()
    {
        string? token = Account.Token;
        if (token is null)
        {
            return false;
        }

        Answer? last = _last;
        if (last is not null && last.Token == token && DateTime.UtcNow - last.At < Keep)
        {
            return last.Yes;
        }

        bool before = last is not null && last.Token == token && last.Yes;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Brand.Api}/api/beta");
            Account.Authorize(request);
            using HttpResponseMessage r = await Http.SendAsync(request);
            if ((int)r.StatusCode == 401)
            {
                Account.Rejected(token); // signed out on the site
                return false;
            }

            if (!r.IsSuccessStatusCode)
            {
                return before; // the site is busy (or an older site without the question): the last answer, asked again next time
            }

            using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            bool yes = doc.RootElement.TryGetProperty("beta", out JsonElement b) && b.ValueKind == JsonValueKind.True;
            _last = new Answer(yes, DateTime.UtcNow, token);
            return yes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return before; // offline: the last answer
        }
    }
}
