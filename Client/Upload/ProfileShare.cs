using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Upload;

/// <summary>One character as the website keeps it for the player window: name, server, class, level, Genus Insight (species id, level, lines: page, slot, stat,
/// value, grade), pets (id, level) and the stat frame's values the character window is made of (stat id, value; <see cref="ProfileShare.StatIds"/>) with
/// the time the game sent them (Unix milliseconds; 0: none).</summary>
public sealed record ProfileCharacter(string Name, int ServerId, int ClassCode, int Level, IReadOnlyList<ProfileGenus> Genus, IReadOnlyList<int[]> Pets, long At,
    IReadOnlyList<int[]>? Stats = null, long StatsAt = 0);

public sealed record ProfileGenus(int Id, int Level, IReadOnlyList<long[]> Fx);

/// <summary>
/// What only the app knows of the character played (6 November 2026, the owner: the player window shows "all details of the person if they are using our app;
/// if they are not, genus/pets are hidden, since we cannot get that info any other way"): its Genus Insight, its pets and its combat stats, sent to atreia.app
/// while the app is signed in and "Show my Genus Insight, pets and combat stats" (<see cref="MeterSettings.ShareProfile"/>) is on, after the privacy terms.
/// Genus and pets are read from the character file the meter keeps of the last login (aion2-character.json) a little after it changes; the same content is not
/// sent twice.
/// Switched off, the website forgets what was sent (<see cref="ForgetAsync"/>).
/// </summary>
/// <remarks>
/// The combat stats (9 November 2026, the owner: "if someone is using the app then give exact numbers, and if someone isn't don't give fake numbers"): the
/// values of the game's own stat frame (<see cref="Aion2EntityDirectory.OwnStats"/>) the character window's Attack, Defense, Accuracy, Evasion, Critical Hit,
/// Critical Hit Resist, HP and MP are made of - the website works the window's numbers out of them (its src/statsheet.js; on recordings of 9 November 2026
/// they came out exactly as the game's window showed them, and HP and MP as the maximum the game itself sends). The game sends the list again at every buff
/// that comes or goes, so they go when the list has been the same for <see cref="StatsSettle"/>, at most every <see cref="StatsEvery"/>, and only with the
/// character file of the same character.
/// </remarks>
public static class ProfileShare
{
    /// <summary>The stat ids that go: what the character window is made of (attack: 317 weapon attack, 19 additional attack, 31/33 the weapon's maximum and
    /// minimum, 425 attack increase; defense 307 and 52 with 426; accuracy 104 and 318 with 427; evasion 116 and 312 with 428; critical hit 128 with 429;
    /// critical hit resist 133 with 430; HP 193 with 196; MP 199 with 197), the base attributes (1..6), and the percent stats the stat calculator knows
    /// (damage boosts, Perfect, Smite, Block, the speeds), for lines the website may add once they are checked against the window.</summary>
    public static readonly IReadOnlySet<int> StatIds = new HashSet<int>
    {
        1, 2, 3, 4, 5, 6,
        19, 31, 33, 317, 425, 52, 307, 426, 104, 318, 427, 116, 312, 428, 128, 429, 133, 430, 193, 196, 199, 197,
        28, 32, 44, 50, 56, 102, 205, 255, 282, 379, 442, 443, 444, 445, 446, 447, 520, 589,
    };

    /// <summary>How long the stat list must have stayed the same before it goes (a buff that comes and goes again within it is never sent).</summary>
    public static readonly TimeSpan StatsSettle = TimeSpan.FromSeconds(30);

    /// <summary>The stats of one character go at most this often (the file's changes at a login go at once).</summary>
    public static readonly TimeSpan StatsEvery = TimeSpan.FromMinutes(5);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Dictionary<string, string> Sent = new(StringComparer.Ordinal);
    private static System.Threading.Timer? _timer;
    private static Func<Aion2EntityDirectory?>? _directory;
    private static DateTime _fileAt = DateTime.MinValue;
    private static string _statsSeen = "";
    private static DateTime _statsSentAt = DateTime.MinValue;
    private static int _busy;

    public static void Start(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        if (_timer is not null)
        {
            return;
        }

        Account.Changed += () =>
        {
            lock (Sent)
            {
                Sent.Clear(); // another account: everything again
            }

            _fileAt = DateTime.MinValue;
            _statsSeen = "";
            _statsSentAt = DateTime.MinValue;
        };
        PrivacyConsent.Changed += () =>
        {
            _fileAt = DateTime.MinValue;
            _statsSeen = "";
        };
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(30));
    }

    /// <summary>Switched on again: everything goes at the next tick, as after a login.</summary>
    public static void Resend()
    {
        lock (Sent)
        {
            Sent.Clear();
        }

        _fileAt = DateTime.MinValue;
        _statsSeen = "";
        _statsSentAt = DateTime.MinValue;
    }

    /// <summary>The character file's content as the website takes it, with the stat frame's values when they are of this character (<paramref name="stats"/>:
    /// <see cref="Aion2EntityDirectory.OwnStats"/>, null when not known); null when there is nothing to show (no Genus Insight, no pets, no stats).</summary>
    public static ProfileCharacter? Build(Aion2SavedCharacter c, (IReadOnlyDictionary<int, int> Values, DateTime At)? stats = null)
    {
        if (c.Name.Length == 0 || c.ServerId <= 0)
        {
            return null;
        }

        var genus = c.Species.Where(s => s.Id is > 0 and < 100).Take(8)
            .Select(s => new ProfileGenus(s.Id, s.Level, s.Effects.Take(40).Select(e => new long[] { e.Page, e.Slot, e.Stat, e.Value, Math.Clamp(e.Kind, 0, 9) }).ToList())).ToList();
        var pets = c.Pets.Where(p => p.Id > 0).Take(400).Select(p => new[] { p.Id, Math.Clamp(p.Level, 0, 9) }).ToList();
        List<int[]>? lines = stats is { } s2 ? StatLines(s2.Values) : null;
        if (genus.Count == 0 && pets.Count == 0 && lines is null)
        {
            return null;
        }

        return new ProfileCharacter(c.Name, c.ServerId, c.ClassCode, c.Level, genus, pets, new DateTimeOffset(DateTime.SpecifyKind(c.SavedAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            lines, lines is null || stats is null ? 0 : new DateTimeOffset(DateTime.SpecifyKind(stats.Value.At, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
    }

    /// <summary>The stat ids that go, in order, as [id, value]; null when the list lacks what the window's main numbers need (no HP, no attack).</summary>
    public static List<int[]>? StatLines(IReadOnlyDictionary<int, int> values)
    {
        if (!values.TryGetValue(193, out int hp) || hp <= 0 || (!values.ContainsKey(317) && !values.ContainsKey(31)))
        {
            return null;
        }

        return values.Where(v => StatIds.Contains(v.Key)).OrderBy(v => v.Key).Select(v => new[] { v.Key, v.Value }).ToList();
    }

    /// <summary>The stats of the character played when they are the character file's character and have settled; null otherwise.</summary>
    private static (IReadOnlyDictionary<int, int> Values, DateTime At)? SettledStats(string name, int serverId)
    {
        if (_directory?.Invoke() is not { } directory || directory.LocalCharacter is not { } local || local.Name != name || local.ServerId != serverId
            || directory.OwnStats is not { } stats || DateTime.UtcNow - stats.At < StatsSettle)
        {
            return null;
        }

        return stats;
    }

    private static string Signature(string name, int serverId, (IReadOnlyDictionary<int, int> Values, DateTime At)? stats) =>
        stats is { } s && StatLines(s.Values) is { } lines ? name + "|" + serverId + "|" + string.Join(',', lines.Select(l => l[0] + "=" + l[1])) : "";

    private static async Task TickAsync()
    {
        if (!Account.SignedIn || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            string path = Aion2CharacterStore.DefaultPath;
            DateTime at = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (DateTime.UtcNow - at < TimeSpan.FromSeconds(20))
            {
                return; // still being written at the login
            }

            // the character played: the stats of the moment go only with the file of the same character
            Aion2CharacterInfo? local = _directory?.Invoke()?.LocalCharacter;
            var stats = local is { ServerId: > 0 } ? SettledStats(local.Name, local.ServerId) : null;
            string seen = local is null ? "" : Signature(local.Name, local.ServerId, stats);
            bool fileNew = at != _fileAt;
            bool statsNew = seen.Length > 0 && seen != _statsSeen && DateTime.UtcNow - _statsSentAt >= StatsEvery;
            if (!fileNew && !statsNew)
            {
                return; // nothing new
            }

            if (!MeterSettings.Load().ShareProfile || !PrivacyConsent.CanUpload || UploadClient.IsBlocked)
            {
                _fileAt = at;
                _statsSeen = seen;
                return;
            }

            if (Aion2CharacterStore.Load(path) is not { } saved || Account.Token is not { } token)
            {
                _fileAt = at;
                return;
            }

            bool same = local is not null && saved.Name == local.Name && saved.ServerId == local.ServerId;
            if (Build(saved, same ? stats : null) is not { } character)
            {
                _fileAt = at;
                _statsSeen = seen;
                return;
            }

            string key = character.Name + "|" + character.ServerId;
            string body = JsonSerializer.Serialize(character with { At = 0, StatsAt = 0 }, Json);
            lock (Sent)
            {
                if (Sent.TryGetValue(key, out string? before) && before == body)
                {
                    _fileAt = at;
                    _statsSeen = seen;
                    return;
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadClient.ApiBaseUrl}/api/profile")
            {
                Content = JsonContent.Create(new { v = 1, characters = new[] { character } }, options: Json),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode || (int)response.StatusCode is 400 or 401 or 413)
            {
                _fileAt = at; // sent, or refused for good: tried again at the next change
                _statsSeen = seen;
                if (character.Stats is not null)
                {
                    _statsSentAt = DateTime.UtcNow;
                }

                if (response.IsSuccessStatusCode)
                {
                    lock (Sent)
                    {
                        Sent[key] = body;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            // tried again at the next tick
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>"Show my Genus Insight, pets and combat stats" switched off: the website forgets what this account sent. False when it couldn't be asked (not signed in, no answer).</summary>
    public static async Task<bool> ForgetAsync()
    {
        if (Account.Token is not { } token)
        {
            return false;
        }

        lock (Sent)
        {
            Sent.Clear();
        }

        _statsSeen = "";
        _statsSentAt = DateTime.MinValue;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{UploadClient.ApiBaseUrl}/api/profile");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
