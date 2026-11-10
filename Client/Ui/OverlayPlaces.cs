using System.Windows;

namespace AionDPS.Ui;

/// <summary>
/// "Reset size and position" of one overlay: the place, the size and the scale it was given are cleared in the settings, and the open window takes
/// its start values again. An overlay is named by its window title; its settings have the same prefix (Left, Top, Width, Height, Scale).
/// </summary>
public static class OverlayPlaces
{
    public const string Assist = "Aion DPS Assist", Effects = "Aion DPS Effects", Network = "Aion DPS Network", Dummy = "Aion DPS Dummy Test", WorldBoss = "Aion DPS World Boss", Hotkeys = "Aion DPS Hotkeys",
        Timetable = "Aion DPS Timetable", PetMap = "Aion DPS Pet Map", PetList = "Aion DPS Pet List", PetInfo = "Aion DPS Pet Farming",
        Loot = "Aion DPS Loot", Gather = "Aion DPS Gathered", Season = "Aion DPS Season", Craft = "Aion DPS Crafting", BossCast = "Aion DPS Boss Cast", BossHp = "Aion DPS Target Health", TargetOfTarget = "Aion DPS Combat",
        Menu = "Aion DPS Overlay Menu", Fps = "Aion DPS FPS", EnemyCard = "Aion DPS Enemy Card",
        EnemyRadar = "Aion DPS Enemy Radar", DeathRecap = "Aion DPS Death Recap";

    private static readonly Dictionary<string, string> Prefixes = new()
    {
        [Assist] = "AssistOverlay", [Effects] = "EffectsOverlay", [Loot] = "LootOverlay", [Gather] = "GatherOverlay", [Season] = "SeasonOverlay", [Craft] = "CraftOverlay", [Network] = "NetworkOverlay", [Dummy] = "DummyOverlay", [WorldBoss] = "BossOverlay", [Hotkeys] = "HotkeyOverlay",
        [Timetable] = "Timetable", [PetMap] = "PetMap", [PetList] = "PetList", [PetInfo] = "PetFarm",
        [BossCast] = "CombatCastOverlay", [BossHp] = "CombatHpOverlay", [TargetOfTarget] = "CombatOverlay", [Menu] = "OverlayMenu", [Fps] = "FpsOverlay", [EnemyCard] = "EnemyCard", [EnemyRadar] = "EnemyRadar", [DeathRecap] = "DeathRecap",
    };

    private static readonly Dictionary<string, string[]> CombatElements = new()
    {
        [BossCast] = new[] { CombatOverlayWindow.BossCastId },
        [BossHp] = new[] { CombatOverlayWindow.BossHpId },
        [TargetOfTarget] = new[] { CombatOverlayWindow.TargetIconId, CombatOverlayWindow.TargetNameId, CombatOverlayWindow.TargetHpId, CombatOverlayWindow.TargetEffectsId, CombatOverlayWindow.TargetBlockId },
    };

    /// <summary>
    /// The saved places of the overlays (and of Skill Assist's own skills) that <paramref name="move"/> puts elsewhere - given the saved place and size
    /// (an estimate when no size is kept), it answers the new top left corner, or null to leave it. For overlays whose window is not open: an open one is moved
    /// through its window. The Atreia button is left where it is (it belongs over the game).
    /// </summary>
    public static void MoveSaved(Func<Rect, Point?> move)
    {
        var s = MeterSettings.Load();
        var type = typeof(MeterSettings);
        bool changed = false;
        foreach ((string title, string prefix) in Prefixes)
        {
            if (title == Menu || type.GetProperty(prefix + "Left") is not { } leftProperty || type.GetProperty(prefix + "Top") is not { } topProperty
                || leftProperty.GetValue(s) is not double left || topProperty.GetValue(s) is not double top)
            {
                continue;
            }

            double width = type.GetProperty(prefix + "Width")?.GetValue(s) is double w && w > 0 ? w : 300;
            double height = type.GetProperty(prefix + "Height")?.GetValue(s) is double h && h > 0 ? h : 160;
            if (move(new Rect(left, top, width, height)) is { } to)
            {
                leftProperty.SetValue(s, to.X);
                topProperty.SetValue(s, to.Y);
                changed = true;
            }
        }

        foreach (Dictionary<int, double[]> places in new[] { s.AssistCooldownPlaces, s.AssistCooldownTilePlaces })
        {
            foreach ((int skill, double[] at) in places.ToList())
            {
                if (at is [double left, double top] && double.IsFinite(left) && double.IsFinite(top) && move(new Rect(left, top, 200, 32)) is { } to)
                {
                    places[skill] = new[] { to.X, to.Y };
                    changed = true;
                }
            }
        }

        if (changed)
        {
            s.Save();
        }
    }

    /// <summary>An overlay was reset: its window (if open) puts the start values on again.</summary>
    public static event Action<string>? WasReset;

    public static void Reset(string title)
    {
        if (!Prefixes.TryGetValue(title, out string? prefix))
        {
            return;
        }

        var s = MeterSettings.Load();
        var type = typeof(MeterSettings);
        foreach (string part in new[] { "Left", "Top", "Width", "Height" })
        {
            var property = type.GetProperty(prefix + part);
            if (property is not null && property.PropertyType == typeof(double?))
            {
                property.SetValue(s, null);
            }
        }

        type.GetProperty(prefix + "Scale")?.SetValue(s, 1.0);
        if (CombatElements.TryGetValue(title, out string[]? ids))
        {
            foreach (string id in ids)
            {
                s.CombatElements.Remove(id);
            }
        }

        s.OverlayShifts.Remove(title);
        s.Save();
        WasReset?.Invoke(title);
    }
}
