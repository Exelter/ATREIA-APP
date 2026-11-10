using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDPS.Aion2;

/// <summary>One grind: from the first kill or pickup until a long pause (or "new session"). What came in: Kinah, Abyss Points, the monsters killed (per map)
/// and the items picked up (per item). <see cref="ActiveSeconds"/> is the time farmed - pauses over <see cref="Aion2Grind.Idle"/> left out - so "per hour"
/// is the session's own average.</summary>
public sealed class GrindSession
{
    public string Id { get; set; } = "";
    public string Character { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double ActiveSeconds { get; set; }
    public long Kinah { get; set; }
    public long AbyssPoints { get; set; }
    public int Kills { get; set; }
    public Dictionary<int, int> ZoneKills { get; set; } = new();
    public Dictionary<int, long> Items { get; set; } = new();

    /// <summary>The map most of the kills were on (0 = not known).</summary>
    [JsonIgnore]
    public int Zone => ZoneKills.Where(kv => kv.Key != 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();

    public GrindSession Copy() => new()
    {
        Id = Id, Character = Character, Start = Start, End = End, ActiveSeconds = ActiveSeconds, Kinah = Kinah, AbyssPoints = AbyssPoints, Kills = Kills,
        ZoneKills = new Dictionary<int, int>(ZoneKills), Items = new Dictionary<int, long>(Items),
    };

    /// <summary>An amount per hour of farming (null until a minute was farmed: the first kill alone says nothing yet).</summary>
    public double? PerHour(double amount, double activeSeconds) => activeSeconds >= 60 ? amount * 3600 / activeSeconds : null;
}

/// <summary>
/// The grind tracker (10 November 2026, the owner: "switch to grind on the meter while farming mobs: how much Kinah, how much Abyss Points you farm, and what
/// you're going to get per hour - changes on average: kill a marshal and it goes off, then drops steadily"). Kinah and Abyss Points come from the inventory
/// frames (0x1B56), which carry the new balance of each (Kinah 930100031, bound Kinah 930100001, Abyss Points 930100003; checked on the recordings of 7 to 10
/// November 2026): what a balance rises by counts, what is spent does not. The kills and the pickups come from the loot log (<see cref="Aion2Loot"/>: a kill
/// the player had a hand in, a drop he picked up). A balance that rises while no kill or pickup came for ten minutes (the storage, the mail, a trade in town)
/// starts nothing. Per hour is the session's average over the time farmed, the time since the last kill included up to five minutes, so a big kill shows at
/// once and the rate settles again as the minutes go by. Kept in aion2-grind.json (the last 40 sessions); nothing leaves the PC.
/// </summary>
public static class Aion2Grind
{
    public const int Kinah = 930100031, BoundKinah = 930100001, AbyssPoints = 930100003;

    /// <summary>A gap between kills longer than this is a pause: its time is not farmed.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    /// <summary>A pause this long ends the session: the next kill starts a new one.</summary>
    public static readonly TimeSpan Over = TimeSpan.FromMinutes(30);

    /// <summary>Kinah and Abyss Points count while a kill or pickup was this recent.</summary>
    private static readonly TimeSpan Farming = TimeSpan.FromMinutes(10);

    private const int MaxSessions = 40;
    private static readonly object Gate = new();
    private static readonly List<GrindSession> Stored = new();   // the ended sessions, newest first
    private static readonly Dictionary<int, long> Balances = new();
    private static GrindSession? _current;
    private static string _character = "";
    private static bool _persist, _dirty;
    private static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Raised (on the capturing thread) when something came in or the log changed.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-grind.json");

    /// <summary>The latest session (a copy), null before the first kill. It may be over already: <see cref="IsOver"/> tells (the next kill starts a new one).</summary>
    public static GrindSession? Current
    {
        get
        {
            lock (Gate)
            {
                return _current?.Copy();
            }
        }
    }

    /// <summary>Whether a session is over by <paramref name="now"/> (a pause of <see cref="Over"/>).</summary>
    public static bool IsOver(GrindSession session, DateTime now) => now - session.End > Over;

    /// <summary>The ended sessions, newest first (copies); the latest one is <see cref="Current"/>.</summary>
    public static IReadOnlyList<GrindSession> Sessions
    {
        get
        {
            lock (Gate)
            {
                return Stored.Select(s => s.Copy()).ToList();
            }
        }
    }

    /// <summary>The time farmed in <paramref name="session"/> as of <paramref name="now"/>: its farmed time and the time since its last kill, up to <see cref="Idle"/>.</summary>
    public static double ActiveSecondsAt(GrindSession session, DateTime now)
    {
        TimeSpan since = now - session.End;
        return session.ActiveSeconds + (since > TimeSpan.Zero && since <= Idle ? since.TotalSeconds : 0);
    }

    /// <summary>The inventory frame (0x1B56) or the login's list (<paramref name="login"/>): the balances it carries. The login's list sets them only.</summary>
    public static void NoteInventory(ReadOnlySpan<byte> frame, DateTime at, string? character, bool login = false)
    {
        IReadOnlyList<(int Kind, int Container, int Item, long Quantity)> records = Aion2Stock.RecordsOf(frame);
        DateTime now = Local(at);
        bool changed = false;
        lock (Gate)
        {
            if (character is { Length: > 0 } name && name != _character)
            {
                // another character: its balances are its own, and so is its grind
                _character = name;
                Balances.Clear();
                EndCurrent();
            }

            foreach ((int kind, int container, int item, long quantity) in records)
            {
                if (item is not (Kinah or BoundKinah or AbyssPoints) || container != Aion2Stock.InventoryContainer)
                {
                    continue;
                }

                long balance = kind == 2 ? 0 : quantity;
                bool known = Balances.TryGetValue(item, out long before);
                Balances[item] = balance;
                if (login || !known || balance <= before || _current is not { } session || now - session.End > Farming)
                {
                    continue;
                }

                Touch(now);
                if (item == AbyssPoints)
                {
                    _current!.AbyssPoints += balance - before;
                }
                else
                {
                    _current!.Kinah += balance - before;
                }

                changed = true;
            }

            if (changed)
            {
                _dirty = true;
                Save(force: false);
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>A kill the loot log counted (the player hit the monster, or picked up its drop).</summary>
    public static void NoteKill(int zone, DateTime at)
    {
        lock (Gate)
        {
            Touch(Local(at));
            _current!.Kills++;
            _current.ZoneKills[zone] = _current.ZoneKills.GetValueOrDefault(zone) + 1;
            _dirty = true;
            Save(force: false);
        }

        Changed?.Invoke();
    }

    /// <summary>An item picked up (Kinah and the other currencies count through their balance, not here).</summary>
    public static void NotePickup(int item, long count, DateTime at)
    {
        if (Aion2Inventories.IsCurrency(item) || count <= 0)
        {
            return;
        }

        lock (Gate)
        {
            Touch(Local(at));
            _current!.Items[item] = _current.Items.GetValueOrDefault(item) + count;
            _dirty = true;
            Save(force: false);
        }

        Changed?.Invoke();
    }

    /// <summary>Ends the session going on: the next kill starts a new one.</summary>
    public static void NewSession()
    {
        lock (Gate)
        {
            EndCurrent();
            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Forgets one ended session, or all of them (null).</summary>
    public static void Forget(string? id)
    {
        lock (Gate)
        {
            if (id is null)
            {
                Stored.Clear();
            }
            else
            {
                Stored.RemoveAll(s => s.Id == id);
            }

            _dirty = true;
            Save(force: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Empties the tracker without touching the file (the self-checks).</summary>
    public static void ResetForCheck()
    {
        lock (Gate)
        {
            Stored.Clear();
            Balances.Clear();
            _current = null;
            _character = "";
        }
    }

    // Under the lock: the session of this moment, a new one after a long pause; the time since the last kill counts as farmed when it is no pause.
    private static void Touch(DateTime now)
    {
        if (_current is { } c && now - c.End > Over)
        {
            EndCurrent();
        }

        if (_current is null)
        {
            _current = new GrindSession { Id = Guid.NewGuid().ToString("N")[..12], Character = _character, Start = now, End = now };
            return;
        }

        TimeSpan gap = now - _current.End;
        if (gap > TimeSpan.Zero)
        {
            if (gap <= Idle)
            {
                _current.ActiveSeconds += gap.TotalSeconds;
            }

            _current.End = now;
        }
    }

    // Under the lock: the session goes to the ended ones (when there is something to it).
    private static void EndCurrent()
    {
        if (_current is { } c && Worth(c))
        {
            Stored.Insert(0, c);
            while (Stored.Count > MaxSessions)
            {
                Stored.RemoveAt(Stored.Count - 1);
            }
        }

        _current = null;
    }

    // A session worth keeping: a minute farmed, a few kills, or something earned.
    private static bool Worth(GrindSession s) => s.ActiveSeconds >= 60 || s.Kills >= 2 || s.Kinah > 0 || s.AbyssPoints > 0 || s.Items.Count > 0;

    private static DateTime Local(DateTime at) => at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;

    /// <summary>Starts keeping the sessions in <see cref="StorePath"/> (the live meter only: a replay or a test keeps nothing). Loads what is there.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath) && JsonSerializer.Deserialize<GrindStore>(File.ReadAllText(StorePath)) is { } store)
                {
                    Stored.Clear();
                    Stored.AddRange(store.Sessions.Take(MaxSessions));
                    // a session that was going on when the meter closed goes on when the meter comes back soon enough
                    _current = store.Current is { } open && DateTime.Now - open.End <= Over ? open : null;
                    if (store.Current is { } old && _current is null && Worth(old))
                    {
                        Stored.Insert(0, old);
                    }

                    _character = _current?.Character ?? "";
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(StorePath);
                }
            }
        }
    }

    /// <summary>Writes what the last few seconds left unsaved (the meter is closing).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            Save(force: true);
        }
    }

    // Under the lock; at most every 10 s unless forced.
    private static void Save(bool force)
    {
        if (!_persist || !_dirty || !force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AionDPS.AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(new GrindStore { Current = _current, Sessions = Stored }));
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; the next save tries again.
        }
    }

    private sealed class GrindStore
    {
        public GrindSession? Current { get; set; }
        public List<GrindSession> Sessions { get; set; } = new();
    }
}
