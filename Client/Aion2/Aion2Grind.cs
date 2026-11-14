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

/// <summary>What one monster (NPC id) gave over every grind: the kills, and the Kinah and Abyss Points that came right after them.</summary>
public sealed class MobYield
{
    public int NpcId { get; set; }
    public int Kills { get; set; }
    public long Kinah { get; set; }
    public long AbyssPoints { get; set; }
}

/// <summary>
/// The grind tracker (10 November 2026, the owner: "switch to grind on the meter while farming mobs: how much Kinah, how much Abyss Points you farm, and what
/// you're going to get per hour - changes on average: kill a marshal and it goes off, then drops steadily"). Kinah and Abyss Points come from the inventory
/// frames (0x1B56), which carry the new balance of each (Kinah 930100031, bound Kinah 930100001, Abyss Points 930100003; checked on the recordings of 7 to 10
/// November 2026): what a balance rises by counts, what is spent does not. The kills and the pickups come from the loot log (<see cref="Aion2Loot"/>: a kill
/// the player had a hand in, a drop he picked up). Only what farming brings counts (14 November 2026, a tester: "it counts the gold and AP that enters your
/// inventory rather than what you've farmed: even moving gold back and forth from storage to inventory increases the number"): Kinah that comes within half a
/// minute of a kill or a pickup, Abyss Points within two minutes of a fight (the player hitting a monster or a player: a kill of a player pays them too, which
/// the loot log does not count). Gold taken from the storage, the mail, a sale or a reward in town comes with no kill and no fight and counts for nothing. A
/// rise that comes a moment before the kill it pays (the meter reads the fights at its next poll) waits a few seconds for it. Per hour is the session's average
/// over the time farmed, the time since the last kill included up to five minutes, so a big kill shows at once and the rate settles again as the minutes go
/// by. Kept in aion2-grind.json (the last 40 sessions); nothing leaves the PC.
/// </summary>
public static class Aion2Grind
{
    public const int Kinah = 930100031, BoundKinah = 930100001, AbyssPoints = 930100003;

    /// <summary>A gap between kills longer than this is a pause: its time is not farmed.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    /// <summary>A pause this long ends the session: the next kill starts a new one.</summary>
    public static readonly TimeSpan Over = TimeSpan.FromMinutes(30);

    /// <summary>Kinah counts this soon after a kill or a pickup: a kill's loot comes within seconds; the storage, the mail or a sale come with none.</summary>
    private static readonly TimeSpan LootWindow = TimeSpan.FromSeconds(30);

    /// <summary>Abyss Points count this soon after a fight (the player hit a monster or a player) or a kill: points moved in or a reward in town come with none.</summary>
    private static readonly TimeSpan FightWindow = TimeSpan.FromMinutes(2);

    /// <summary>A rise may come this much before the kill, pickup or hit that explains it (the frames and the fights are read apart).</summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(3);

    /// <summary>A rise nothing explains yet waits this long for the kill, pickup or fight the meter notes a moment later.</summary>
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(15);

    private const int MaxSessions = 40;
    private static readonly object Gate = new();
    private static readonly List<GrindSession> Stored = new();   // the ended sessions, newest first
    private static readonly Dictionary<int, MobYield> Yields = new();   // per monster, over every grind
    // the kills of the last seconds, for the Kinah and the Abyss Points that follow them (a kill pays each once: its Kinah and its points apart)
    private static readonly List<(int Npc, DateTime At, bool PaidKinah, bool PaidAp)> RecentKills = new();
    private static readonly Dictionary<int, long> Balances = new();
    // the rises nothing explained yet: the item, how much, when (see Late)
    private static readonly List<(int Item, long Gain, DateTime At)> Pending = new();
    // the latest kill or pickup (Kinah), the latest fight or kill (Abyss Points)
    private static DateTime _lastLoot = DateTime.MinValue, _lastFight = DateTime.MinValue;
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
                Pending.Clear();
                _lastLoot = _lastFight = DateTime.MinValue;
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
                if (login || !known || balance <= before)
                {
                    continue;
                }

                // only what farming brings: Kinah right after a kill or a pickup, Abyss Points after a fight; anything else (the storage, the mail, a sale) waits
                // a few seconds for a kill the meter has not read yet, then counts for nothing
                long gain = balance - before;
                if (Farmed(item, now))
                {
                    Count(item, gain, now);
                    changed = true;
                }
                else
                {
                    Pending.Add((item, gain, now));
                }
            }

            Pending.RemoveAll(p => now - p.At > Late);
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

    /// <summary>A kill the loot log counted (the player hit the monster, or picked up its drop) of monster <paramref name="npc"/> (its NPC id, 0 = not known).</summary>
    public static void NoteKill(int zone, DateTime at, int npc = 0)
    {
        lock (Gate)
        {
            DateTime now = Local(at);
            _lastLoot = Later(_lastLoot, now);
            _lastFight = Later(_lastFight, now);
            Touch(now);
            _current!.Kills++;
            _current.ZoneKills[zone] = _current.ZoneKills.GetValueOrDefault(zone) + 1;
            if (npc > 0)
            {
                YieldOf(npc).Kills++;
                RecentKills.Add((npc, now, false, false));
                RecentKills.RemoveAll(k => now - k.At > TimeSpan.FromSeconds(5));
            }

            SettlePending();
            _dirty = true;
            Save(force: false);
        }

        Changed?.Invoke();
    }

    /// <summary>An item picked up: it counts as loot (Kinah and the other currencies count through their balance, not here), and the Kinah that comes with it is farmed.</summary>
    public static void NotePickup(int item, long count, DateTime at)
    {
        if (count <= 0)
        {
            return;
        }

        bool currency = Aion2Inventories.IsCurrency(item);
        lock (Gate)
        {
            DateTime now = Local(at);
            _lastLoot = Later(_lastLoot, now);
            if (!currency)
            {
                Touch(now);
                _current!.Items[item] = _current.Items.GetValueOrDefault(item) + count;
            }

            if (!SettlePending() && currency)
            {
                return;
            }

            _dirty = true;
            Save(force: false);
        }

        Changed?.Invoke();
    }

    /// <summary>The player hit something, a monster or a player (the meter's fights): the Abyss Points of the next two minutes are earned.</summary>
    public static void NoteFight(DateTime at)
    {
        lock (Gate)
        {
            DateTime now = Local(at);
            if (now <= _lastFight)
            {
                return;
            }

            _lastFight = now;
            if (!SettlePending())
            {
                return;
            }

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
            Yields.Clear();
            RecentKills.Clear();
            Pending.Clear();
            _lastLoot = _lastFight = DateTime.MinValue;
            _current = null;
            _character = "";
        }
    }

    // Under the lock: whether a rise at <paramref name="at"/> is farmed - Kinah within half a minute of a kill or a pickup, Abyss Points within two minutes of a
    // fight or a kill (a rise may come a moment before what explains it)
    private static bool Farmed(int item, DateTime at) =>
        item == AbyssPoints ? Near(at, _lastFight, FightWindow) : Near(at, _lastLoot, LootWindow);

    private static bool Near(DateTime at, DateTime activity, TimeSpan window) => activity != DateTime.MinValue && at >= activity - Skew && at - activity <= window;

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    // Under the lock: a farmed rise counts in the session, and for the monster it came from (the oldest kill of the last five seconds that paid nothing yet,
    // else the latest one: a pack killed at once pays one after the other)
    private static void Count(int item, long gain, DateTime at)
    {
        Touch(at);
        if (item == AbyssPoints)
        {
            _current!.AbyssPoints += gain;
        }
        else
        {
            _current!.Kinah += gain;
        }

        if (PayingKill(at, item == AbyssPoints) is int npc && npc > 0)
        {
            MobYield y = YieldOf(npc);
            if (item == AbyssPoints)
            {
                y.AbyssPoints += gain;
            }
            else
            {
                y.Kinah += gain;
            }
        }
    }

    // Under the lock: the rises kept a moment that a kill, a pickup or a fight now explains count (true when one did)
    private static bool SettlePending()
    {
        bool any = false;
        for (int i = 0; i < Pending.Count; i++)
        {
            (int item, long gain, DateTime at) = Pending[i];
            if (Farmed(item, at))
            {
                Count(item, gain, at);
                Pending.RemoveAt(i--);
                any = true;
            }
        }

        // what came long before the latest kill or fight stays unexplained: the storage, the mail
        DateTime latest = Later(_lastLoot, _lastFight);
        Pending.RemoveAll(p => latest - p.At > Late);
        return any;
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

    /// <summary>What each monster gave over every grind (copies), the most killed first.</summary>
    public static IReadOnlyList<MobYield> MobYields
    {
        get
        {
            lock (Gate)
            {
                return Yields.Values.OrderByDescending(y => y.Kills)
                    .Select(y => new MobYield { NpcId = y.NpcId, Kills = y.Kills, Kinah = y.Kinah, AbyssPoints = y.AbyssPoints }).ToList();
            }
        }
    }

    // Under the lock.
    private static MobYield YieldOf(int npc)
    {
        if (!Yields.TryGetValue(npc, out MobYield? y))
        {
            Yields[npc] = y = new MobYield { NpcId = npc };
        }

        return y;
    }

    // Under the lock: the monster a gain at <paramref name="now"/> came from (null: no kill in the last five seconds): the oldest kill of them that hasn't paid
    // this currency yet (a kill's Kinah and its Abyss Points come one after the other, a pack's kills each in turn).
    private static int? PayingKill(DateTime now, bool abyssPoints)
    {
        int index = RecentKills.FindIndex(k => !(abyssPoints ? k.PaidAp : k.PaidKinah) && now - k.At <= TimeSpan.FromSeconds(5) && now >= k.At.AddSeconds(-1));
        if (index >= 0)
        {
            (int npc, DateTime at, bool paidKinah, bool paidAp) = RecentKills[index];
            RecentKills[index] = (npc, at, paidKinah || !abyssPoints, paidAp || abyssPoints);
            return npc;
        }

        int latest = RecentKills.FindLastIndex(k => now - k.At <= TimeSpan.FromSeconds(5));
        return latest >= 0 ? RecentKills[latest].Npc : null;
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
                    Yields.Clear();
                    foreach (MobYield y in store.Mobs.Where(y => y.NpcId > 0))
                    {
                        Yields[y.NpcId] = y;
                    }
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
            AionDPS.AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(new GrindStore { Current = _current, Sessions = Stored, Mobs = Yields.Values.ToList() }));
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
        public List<MobYield> Mobs { get; set; } = new();
    }
}
