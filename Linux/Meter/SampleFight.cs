using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Linux.Meter;

/// <summary>
/// A made-up boss fight for the pictures of the build and the checks (the Windows app's pictures show the same group): five players of a group against
/// Transcendent Bakarma for 111 seconds, with a healer and a tank that takes the boss's blows. The same every time (a fixed seed).
/// </summary>
internal sealed class SampleFight : ICombatSource
{
    public const int BossId = 900;
    public const string BossName = "Transcendent Bakarma";

    private readonly SampleDirectory _directory = new();
    private readonly List<CombatBatch> _batches = new();

    /// <summary>(name, class, combat power, damage per second, heals per second, taken per second)</summary>
    private static readonly (string Name, string Class, int Power, double Dps, double Hps, double Taken)[] Players =
    {
        ("Kaivhan", "Gladiator", 196_500, 8_300, 0, 2_400),
        ("Nyxelle", "Templar", 187_200, 7_100, 0, 6_900),
        ("Seraphyne", "Sorcerer", 177_900, 6_800, 0, 1_300),
        ("Draegor", "Chanter", 168_600, 3_900, 210, 1_900),
        ("Lumiael", "Cleric", 159_300, 2_100, 3_200, 1_600),
    };

    public SampleFight(DateTime start, int seconds = 111, bool withBoss = true)
    {
        _directory.Boss = withBoss ? new BossLine(BossName, 3_140_000, 12_600_000, TimeSpan.FromSeconds(111), TimeSpan.FromSeconds(129)) : null;
        var random = new Random(20261113);
        var damage = new List<DamageEvent>();
        for (int p = 0; p < Players.Length; p++)
        {
            var player = Players[p];
            int id = _directory.Add(player.Name, player.Class, player.Power, isPlayer: true);
            _directory.Party.Add(player.Name);
            for (int s = 0; s < seconds; s++)
            {
                DateTime at = start.AddSeconds(s).AddMilliseconds(random.Next(0, 900));
                long hit = (long)(player.Dps * (0.75 + random.NextDouble() * 0.5));
                damage.Add(new DamageEvent(at, id, BossId, hit, IsHeal: false, Skill: "Strike", IsCritical: random.NextDouble() < 0.3));
                if (player.Hps > 0)
                {
                    int healed = (s + p) % Players.Length;
                    damage.Add(new DamageEvent(at.AddMilliseconds(50), id, healed + 1, (long)(player.Hps * (0.8 + random.NextDouble() * 0.4)), IsHeal: true, Skill: "Healing Light"));
                }

                if (player.Taken > 0 && s % 2 == 0)
                {
                    damage.Add(new DamageEvent(at.AddMilliseconds(120), BossId, id, (long)(player.Taken * 2 * (0.8 + random.NextDouble() * 0.4)), IsHeal: false, Skill: "Crushing Blow"));
                }
            }
        }

        _directory.Add(BossName, null, 0, isPlayer: false, id: BossId);
        _directory.LocalId = 1; // Kaivhan
        damage.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        _batches.Add(CombatBatch.DamageOnly(damage));
    }

    public SourceCapabilities Capabilities => SourceCapabilities.ExactIds;

    public IEntityDirectory Entities => _directory;

    public string CurrentZone => "";

    public bool InArena => false;

    public event Action<string, string>? SkillUsed
    {
        add { }
        remove { }
    }

    public event Action<SourceStatus>? StatusChanged;

    public void Start() => StatusChanged?.Invoke(new SourceStatus(SourceState.Connected, "sample fight"));

    public void Stop()
    {
    }

    public CombatBatch Poll(bool paused)
    {
        if (_batches.Count == 0)
        {
            return CombatBatch.Empty;
        }

        CombatBatch batch = _batches[0];
        _batches.RemoveAt(0);
        return paused ? CombatBatch.Empty : batch;
    }

    public void Dispose()
    {
    }

    private sealed class SampleDirectory : ISampleDirectory
    {
        private readonly Dictionary<int, (string Name, string? Class, int Power, bool Player)> _known = new();
        private int _next = 1;

        public int LocalId { get; set; } = -1;

        public HashSet<string> Party { get; } = new(StringComparer.Ordinal);

        IReadOnlySet<string> ISampleDirectory.Party => Party;

        public BossLine? Boss { get; set; }

        public int Add(string name, string? className, int power, bool isPlayer, int? id = null)
        {
            int key = id ?? _next++;
            _known[key] = (name, className, power, isPlayer);
            return key;
        }

        public string? NameFor(int id) => _known.TryGetValue(id, out var e) ? e.Name : null;

        public int GetOrAssignId(string name) => _known.FirstOrDefault(e => e.Value.Name == name).Key;

        public int LocalPlayerId => LocalId;

        public bool IsLocalPlayer(int id) => id == LocalId;

        public bool IsPlayer(int id) => _known.TryGetValue(id, out var e) && e.Player;

        public bool IsBoss(int id) => id == BossId;

        public string? ClassOf(int id) => _known.TryGetValue(id, out var e) ? e.Class : null;

        public int? PowerOf(int id) => _known.TryGetValue(id, out var e) && e.Power > 0 ? e.Power : null;
    }
}
