using System.Security.Cryptography;
using System.Text;

namespace AionDPS.Aion2;

/// <summary>
/// One roster: the characters of one game account on one server, as the character selection lists them together (the login's character list, which holds
/// every character of the account on the server picked and nobody else's). <paramref name="Key"/> is the account's (<see cref="TicketCharacter.Account"/>);
/// characters of no known account yet are a roster of their server ("?" and the server id). The characters are in the order of <see cref="Aion2Tickets.Snapshot"/>.
/// </summary>
public sealed record TicketRoster(string Key, int ServerId, IReadOnlyList<TicketCharacter> Characters)
{
    /// <summary>The character the roster goes by: the strongest one the meter has counters of (its item level), else the first.</summary>
    public TicketCharacter Lead => Characters.OrderByDescending(c => c.Tickets.Count > 0).ThenByDescending(c => c.ItemLevel).ThenByDescending(c => c.Level)
        .ThenByDescending(c => c.SavedAt).ThenBy(c => c.Name, StringComparer.Ordinal).First();
}

/// <summary>A roster taken out of the app (<see cref="Aion2Tickets.RemoveRoster"/>): its characters as they were, the server, when.</summary>
public sealed record RemovedRoster(string Id, int ServerId, IReadOnlyList<string> Names, DateTime RemovedAt);

/// <summary>
/// The rosters (15 November 2026, the owner, on a player whose husband's characters showed in her app after he played on her PC: "if people have multiple
/// rosters allow for them to select a roster or remove a roster"). The Characters page, the home page's Odyle Energy and the season missions show one roster:
/// the one logged in with last, or the one picked. A roster taken out of the app loses its characters and their counters, and its characters are not counted
/// again when they log in on this PC, until it is brought back.
/// The game used to put an account key at the head of the character list (<see cref="Protocol.Aion2FrameDecoder.TryReadAccountKey"/>); the lists of November
/// 2026 have none (the owner's list of 12 November: Dead, rammusOK, YIELD and Fighter, no key), so a roster is known by the characters a list holds together.
/// </summary>
public static partial class Aion2Tickets
{
    private sealed class RemovedEntry
    {
        public string? Key { get; init; }
        public int ServerId { get; init; }
        public HashSet<string> Names { get; init; } = new(StringComparer.Ordinal);
        public DateTime RemovedAt { get; init; }

        public string Id => (Key ?? "?" + ServerId) + "@" + new DateTimeOffset(RemovedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>The roster the app shows: the one of the last login, or the one picked on the Characters page since (null: the one played).</summary>
    private static string? _shown;

    private static readonly List<RemovedEntry> _removed = new();

    /// <summary>The last character list was a removed roster's: the login that follows it is not counted.</summary>
    private static bool _ignoredLogin;

    /// <summary>The server of the character logged in now (the live record's).</summary>
    private static int _currentServerId;

    /// <summary>Every roster with a character the meter has counters of (and the one shown), by server and the name they go by.</summary>
    public static IReadOnlyList<TicketRoster> Rosters()
    {
        lock (Gate)
        {
            return RostersLocked();
        }
    }

    /// <summary>The roster the app shows (see <see cref="Aion2Tickets"/>'s rosters); null while no character is known.</summary>
    public static TicketRoster? ShownRoster()
    {
        lock (Gate)
        {
            return ShownLocked(RostersLocked());
        }
    }

    /// <summary>The characters of the roster shown, as <see cref="Snapshot"/> orders them: what the Characters page, the home page and the season missions list.</summary>
    public static IReadOnlyList<TicketCharacter> Shown() => ShownRoster()?.Characters ?? Array.Empty<TicketCharacter>();

    /// <summary>Shows another roster (picked on the Characters page); the next login with another roster's character shows that one.</summary>
    public static void ShowRoster(string key)
    {
        lock (Gate)
        {
            if (_shown == key)
            {
                return;
            }

            _shown = key;
            _dirty = true;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Takes a roster out of the app: its characters and their counters go (their holdings too), and they are not counted again when they log in on this PC
    /// (a character made on that account since is not either) until <see cref="BringBackRoster"/>. False when there is no such roster.
    /// </summary>
    public static bool RemoveRoster(string key)
    {
        List<string> names;
        lock (Gate)
        {
            List<TicketCharacter> characters = Characters.Values.Where(c => KeyOf(c) == key).ToList();
            if (characters.Count == 0)
            {
                return false;
            }

            names = characters.Select(c => c.Name).ToList();
            int server = characters.GroupBy(c => c.ServerId).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            _removed.Add(new RemovedEntry { Key = key.StartsWith('?') ? null : key, ServerId = server, Names = new HashSet<string>(names, StringComparer.Ordinal), RemovedAt = DateTime.UtcNow });
            foreach (string name in names)
            {
                Characters.Remove(name);
            }

            if (_shown == key)
            {
                _shown = null; // the roster played, else the one played last
            }

            _dirty = true;
        }

        Aion2Inventories.Forget(names);
        Changed?.Invoke();
        return true;
    }

    /// <summary>The rosters taken out of the app, the latest first.</summary>
    public static IReadOnlyList<RemovedRoster> RemovedRosters()
    {
        lock (Gate)
        {
            return _removed.OrderByDescending(e => e.RemovedAt)
                .Select(e => new RemovedRoster(e.Id, e.ServerId, e.Names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(), e.RemovedAt)).ToList();
        }
    }

    /// <summary>Lets a removed roster in again: its characters come back with their next login while the meter runs.</summary>
    public static void BringBackRoster(string id)
    {
        lock (Gate)
        {
            if (_removed.RemoveAll(e => e.Id == id) == 0)
            {
                return;
            }

            _ignoredLogin = false;
            _dirty = true;
        }

        Changed?.Invoke();
    }

    /// <summary>Whether a character is one of a removed roster's (<paramref name="serverId"/> 0: any server).</summary>
    public static bool IsRemoved(string name, int serverId = 0)
    {
        lock (Gate)
        {
            return IsRemovedLocked(name, serverId);
        }
    }

    private static bool IsRemovedLocked(string name, int serverId) =>
        _removed.Any(e => e.Names.Contains(name) && (e.ServerId == 0 || serverId == 0 || e.ServerId == serverId));

    /// <summary>
    /// The removed roster a character list is of: the same account key, or mostly the same characters (a list holds a whole account, so a character made since
    /// still is that roster's; the server is not compared, as a list gives the server a character was made on, not the one played on).
    /// </summary>
    private static RemovedEntry? RemovedListLocked(string? account, IReadOnlyList<AccountCharacter> listed)
    {
        foreach (RemovedEntry e in _removed)
        {
            int hits = listed.Count(c => e.Names.Contains(c.Name));
            if ((account is not null && e.Key == account) || (hits > 0 && hits * 2 >= listed.Count))
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>The roster of a character list without an account key: the one its characters had (the most of them), else a new one named after its oldest character.</summary>
    private static string ListedRosterKey(IReadOnlyList<AccountCharacter> listed)
    {
        string? had = listed.Select(c => Characters.TryGetValue(c.Name, out TicketCharacter? known) ? known.Account : null).OfType<string>()
            .GroupBy(a => a, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();
        if (had is not null)
        {
            return had;
        }

        AccountCharacter oldest = listed.OrderBy(c => c.CreatedMs > 0 ? c.CreatedMs : long.MaxValue).ThenBy(c => c.Name, StringComparer.Ordinal).First();
        return NewRosterKey(oldest.Name + "|" + oldest.CharacterId);
    }

    private static string NewRosterKey(string seed) =>
        "r" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("aiondps-roster:" + seed)), 0, 8).ToLowerInvariant();

    /// <summary>A character's roster: its account's, else the one of its server's characters of no known account.</summary>
    private static string KeyOf(TicketCharacter c) => c.Account ?? "?" + c.ServerId;

    private static List<TicketRoster> RostersLocked()
    {
        var rosters = new List<TicketRoster>();
        IEnumerable<TicketCharacter> ordered = Characters.Values.OrderByDescending(c => c.Tickets.Count > 0).ThenByDescending(c => c.SavedAt).ThenBy(c => c.Name, StringComparer.Ordinal);
        foreach (IGrouping<string, TicketCharacter> group in ordered.GroupBy(KeyOf, StringComparer.Ordinal))
        {
            List<TicketCharacter> characters = group.ToList();
            // a roster with no counters is a server account only seen at the character selection: not offered unless it is the one shown
            if (!characters.Any(c => c.Tickets.Count > 0) && group.Key != _shown)
            {
                continue;
            }

            int server = characters.GroupBy(c => c.ServerId).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            rosters.Add(new TicketRoster(group.Key, server, characters));
        }

        return rosters.OrderBy(r => r.ServerId).ThenBy(r => r.Lead.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static TicketRoster? ShownLocked(List<TicketRoster> rosters)
    {
        if (rosters.Count == 0)
        {
            return null;
        }

        if (_shown is { } key && rosters.FirstOrDefault(r => r.Key == key) is { } picked)
        {
            return picked;
        }

        if ((_current ?? _likely) is { } playing && rosters.FirstOrDefault(r => r.Characters.Any(c => c.Name == playing)) is { } played)
        {
            return played;
        }

        return rosters.OrderByDescending(r => r.Characters.Any(c => c.Tickets.Count > 0)).ThenByDescending(r => r.Characters.Max(c => c.SavedAt)).First();
    }

    /// <summary>
    /// Characters saved before rosters were kept have no account (the game stopped sending its key): each gets the roster of the last character list its
    /// server account showed (<see cref="Aion2RememberedCharacters"/>, one list per login), so two accounts played on one PC are two rosters before either logs in again.
    /// </summary>
    internal static void SeedRosters(IEnumerable<Aion2RememberedCharacters.Entry> remembered)
    {
        lock (Gate)
        {
            if (!Characters.Values.Any(c => c.Account is null))
            {
                return;
            }

            foreach (IGrouping<long, Aion2RememberedCharacters.Entry> list in remembered.Where(e => e.SeenMs > 0).GroupBy(e => e.SeenMs))
            {
                var names = list.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
                List<TicketCharacter> loose = Characters.Values.Where(c => c.Account is null && names.Contains(c.Name)).ToList();
                if (loose.Count == 0)
                {
                    continue;
                }

                string key = Characters.Values.Where(c => c.Account is not null && names.Contains(c.Name)).GroupBy(c => c.Account!, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                    ?? NewRosterKey(names.Min(StringComparer.Ordinal) + "|seen");
                foreach (TicketCharacter c in loose)
                {
                    Characters[c.Name] = c with { Account = key };
                }

                _dirty = true;
            }
        }
    }
}
