using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The fights with players and the deaths (10 November 2026): every poll's hits and deaths go to <see cref="Aion2Pvp"/>, which works out the death recap of
/// the own character (the death recap overlay shows it), counts the rivals (the enemy card and the radar show the record), and the deaths of the group (the wipe
/// review goes by them).
/// </summary>
public partial class MainWindow
{
    private void FeedPvp(Aion2EntityDirectory dir, IReadOnlyList<DamageEvent> damage, IReadOnlyList<(int Entity, DateTime At)> deaths)
    {
        if (damage.Count == 0 && deaths.Count == 0)
        {
            return;
        }

        int local = dir.LocalPlayerId >= 0 ? dir.LocalPlayerId : dir.InferLocalPlayer() ?? -1;
        IReadOnlySet<string> party = dir.PartyNames;
        Aion2Pvp.Feed(damage, deaths, local, ResolveDisplayName, id => ClassOfPlayer(dir, id), dir.IsPlayer, id => dir.ServerIdOf(id) ?? 0,
            id => dir.SeenProfileOf(id)?.CombatPower is int cp and > 0 and not 397228 ? cp : null,
            id => dir.NameFor(id) is { } name && party.Contains(name));
    }

    /// <summary>A player's class: the one his "player appeared" frame told, else the one his skills show; null for a monster.</summary>
    internal static string? ClassOfPlayer(Aion2EntityDirectory dir, int id) =>
        dir.SeenProfileOf(id)?.ClassId is int classId ? Aion2SkillNames.ClassFromCode(4 * classId + 1) : dir.ClassOf(id);
}
