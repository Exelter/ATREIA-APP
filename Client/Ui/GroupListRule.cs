namespace AionDPS.Ui;

/// <summary>
/// When the meter's group list goes away by itself: Find a player under the meter's bar showing "In your group" and "Fighting with you"
/// (MainWindow.AutoShowGroup), whether it opened by itself (a group formed, "Show my group") or the player opened it.
/// <list type="bullet">
/// <item>It goes when a dungeon, raid or arena is entered after it opened, and when a boss fight begins after it opened (11 November 2026, a player:
/// "pls make that this goes away once you enter the dungeon ... it sits there all the time until you x it"; 12 November 2026, the owner: "why does this
/// stay again, in dungeons. It should stop" - it still stayed when the player had opened it, when the box still held an old search's name, and when the
/// meter had missed the way into the dungeon).</item>
/// <item>One opened in the dungeon stays until a boss fight begins, one opened during a boss fight until the next one: the player wants it right then.</item>
/// <item>A search's results or a character's card stay: the player is reading them.</item>
/// </list>
/// The dungeon is the zone, or the monsters around when the zone was missed (MainWindow.InDungeonNow); that answer comes and goes as packs die, so the
/// player counts as out only after a minute without it.
/// </summary>
public sealed class GroupListRule
{
    /// <summary>How long the dungeon answer may be missing before the player counts as out of the dungeon.</summary>
    public static readonly TimeSpan InsideKept = TimeSpan.FromMinutes(1);

    /// <summary>The own character is in a boss fight while it hit a boss or was hit by one in the last 3 seconds.</summary>
    public static readonly TimeSpan FightOn = TimeSpan.FromSeconds(3);

    /// <summary>A minute without a blow and the boss fight is over (the next one is a new fight, as the meter counts them).</summary>
    public static readonly TimeSpan FightOver = TimeSpan.FromMinutes(1);

    private DateTime _insideSeen = DateTime.MinValue;

    /// <summary>Since when the player has been in a dungeon, raid or arena (MinValue: not in one).</summary>
    public DateTime InsideSince { get; private set; } = DateTime.MinValue;

    /// <summary>Since when the own boss fight has been going (MinValue: none).</summary>
    public DateTime FightSince { get; private set; } = DateTime.MinValue;

    public bool Inside => InsideSince != DateTime.MinValue;

    /// <summary>One look per tick: whether the player is in a dungeon, raid or arena right now, and the own last blow in a boss fight.</summary>
    public void Note(DateTime now, bool insideNow, DateTime lastOwnBossFight)
    {
        if (insideNow)
        {
            if (InsideSince == DateTime.MinValue)
            {
                InsideSince = now;
            }

            _insideSeen = now;
        }
        else if (now - _insideSeen > InsideKept)
        {
            InsideSince = DateTime.MinValue;
        }

        TimeSpan sinceBlow = now - lastOwnBossFight;
        if (sinceBlow < FightOn)
        {
            if (FightSince == DateTime.MinValue)
            {
                FightSince = now;
            }
        }
        else if (sinceBlow > FightOver)
        {
            FightSince = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Whether the list goes now: it shows only the group (no search's results, no card) and a dungeon or a boss fight began after it opened.
    /// </summary>
    public bool Folds(DateTime openedAt, bool showsGroupOnly) =>
        showsGroupOnly && ((Inside && openedAt < InsideSince) || (FightSince != DateTime.MinValue && openedAt < FightSince));
}
