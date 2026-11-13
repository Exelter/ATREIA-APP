using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>What the combat overlay shows at one moment: the casts (soonest first) and the target of the target. Each is null/empty when there is nothing to show.</summary>
public sealed record CombatFrame(IReadOnlyList<CastRowData> Casts, TargetOfTargetData? TargetOfTarget, BossHpData? Boss = null);

/// <summary>The fought boss's health: what it has now and the most it was seen with.</summary>
public sealed record BossHpData(long Hp, long Max, int Id = 0, string Check = "", DateTime? Since = null, int RageSeconds = AionDPS.Aion2.Protocol.Aion2BossRage.DefaultSeconds);

/// <summary>Works out the combat overlay's frame from what the packet stream said (see <see cref="Aion2CombatState"/>); no windows, so a test can run it.</summary>
public static class CombatOverlayData
{
    /// <summary>How long an entity counts as aiming at what it last used a skill on.</summary>
    private static readonly TimeSpan AimAge = TimeSpan.FromSeconds(10);

    /// <summary>A cast stays on the bar this long after it ended (its damage is about to land).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>A monster this tough counts as a boss even when the game data does not say so (new bosses, Abyss bosses).</summary>
    private const long BossHitPoints = 500_000;

    private static readonly TimeSpan BossHold = TimeSpan.FromSeconds(15);
    private static (int Id, DateTime At, int Epoch)? _lastBoss;

    public static CombatFrame Build(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        IReadOnlyList<CastRowData> casts = settings.CombatShowBossCast ? Casts(dir, settings, now, language) : Array.Empty<CastRowData>();
        TargetOfTargetData? tot = settings.CombatShowTargetOfTarget ? TargetOfTarget(dir, settings, now, language) : null;
        // the boss's health is on the meter now (MainWindow.BossBar.cs; 5 November 2026, the owner: "remove the boss bar, add it over the meter")
        return new CombatFrame(casts, tot, null);
    }

    /// <summary>The boss being fought: the own target when it is a boss, else the one that cast most recently.</summary>
    public static BossHpData? BossHp(Aion2EntityDirectory dir, DateTime now)
    {
        int? boss = dir.LocalTarget is { } clicked && !dir.IsKnownPlayer(clicked.EntityId) && IsBoss(dir, clicked.EntityId) ? clicked.EntityId : null;
        if (boss is null && dir.LocalHitTarget is { } hit && DateTime.UtcNow - hit.At < BossHold && !dir.IsKnownPlayer(hit.EntityId) && IsBoss(dir, hit.EntityId))
        {
            boss = hit.EntityId;
        }

        if (boss is null)
        {
            foreach (CastView view in dir.Combat.Casts(now, Grace).OrderByDescending(v => v.Notice.Start))
            {
                if (!dir.IsKnownPlayer(view.Notice.Actor) && IsBoss(dir, view.Notice.Actor))
                {
                    boss = view.Notice.Actor;
                    break;
                }
            }
        }

        // Aiming with the camera sends no mark message and casts come in gaps: hold the last boss for a while so the bar does not blink.
        if (boss is int found)
        {
            _lastBoss = (found, DateTime.UtcNow, dir.ZoneEpoch);
        }
        else if (_lastBoss is { } last && last.Epoch == dir.ZoneEpoch && DateTime.UtcNow - last.At < BossHold && dir.HitPoints.Latest(last.Id)?.Hp > 0)
        {
            boss = last.Id;
        }

        if (boss is not int id || dir.HitPoints.Latest(id)?.Hp is not long hp)
        {
            return null;
        }

        if (hp <= 0)
        {
            _lastBoss = null; // a dead boss leaves no bar behind, even when it is still marked as the target
            return null;
        }

        // A meter that joined mid-fight saw only a worn-down value first: the spawn frame or the table knows the real maximum.
        long max = AionDPS.Aion2.Protocol.Aion2BossHp.RealMax(dir, id) ?? 0;
        return max > 0 ? new BossHpData(Math.Min(hp, max), max, id, Since: dir.EngagedSince(id), RageSeconds: AionDPS.Aion2.Protocol.Aion2BossRage.SecondsFor(dir.BossNpcIdOf(id))) : null;
    }

    private static bool IsBoss(Aion2EntityDirectory dir, int entity) =>
        dir.BossNpcIdOf(entity) is not null || (dir.HitPoints.MaxOf(entity) ?? 0) >= BossHitPoints;

    /// <summary>The casts the bar shows; <paramref name="all"/> (for the voice output) skips the bar's own filter and limit and keeps every cast in the order it began.</summary>
    public static IReadOnlyList<CastRowData> Casts(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language, bool all = false)
    {
        var loc = LocalizationManager.Instance;
        int local = dir.LocalPlayerId;
        long hp = local >= 0 ? dir.HitPoints.Latest(local)?.Hp ?? 0 : 0;
        var scored = new List<(double Score, DateTime End, CastRowData Row)>();
        foreach (CastView view in dir.Combat.Casts(now, Grace))
        {
            CastNotice cast = view.Notice;
            if (dir.IsKnownPlayer(cast.Actor))
            {
                continue; // a player's windup is no boss attack
            }

            bool boss = IsBoss(dir, cast.Actor);
            if (!boss && !settings.CombatCastsOfAllMonsters)
            {
                continue;
            }

            bool atYou = local >= 0 && cast.Target == local;
            long expected = view.MaxOnYou > 0 ? view.MaxOnYou : view.MaxAny;
            bool lethal = hp > 0 && expected >= hp && (atYou || view.IsArea);
            if (!all && settings.CombatCastFilter == "you" && !atYou)
            {
                continue;
            }

            if (!all && settings.CombatCastFilter == "area" && !atYou && !view.IsArea)
            {
                continue;
            }

            string target = atYou
                ? loc["Combat.You"]
                : cast.Target <= 0 || cast.Target == cast.Actor ? loc["Combat.AllAround"] : Name(dir, cast.Target, language);
            bool generic = Aion2SkillNames.IsGeneric(cast.SkillId);
            // a skill the client has no name for says what it paints on the floor ("Attack to the front") instead of a bare "Attack"
            string? shape = generic ? Aion2SkillShapes.KindOf(cast.SkillId) : null;
            string skill = shape is not null ? loc["Combat.Shape." + shape] : Aion2SkillNames.DisplayOf(cast.SkillId);
            string english = shape is not null ? loc.In("Combat.Shape." + shape, "en") : Aion2SkillNames.EnglishOf(cast.SkillId); // what the recorded voice says
            string caster = Name(dir, cast.Actor, language);
            var row = new CastRowData(skill, caster, target, atYou, view.IsArea, lethal, (now - cast.Start).TotalMilliseconds, cast.DurationMs, view.LandMs, Aion2SkillIcons.PathFor(cast.SkillId) ?? (dir.BossNpcIdOf(cast.Actor) is int bossNpc ? Aion2Artwork.BossPicturePath(bossNpc, Aion2Npcs.NameOf(bossNpc, "en")) : null), generic,
                $"{cast.Actor}:{cast.SkillId}:{cast.Start.Ticks}", !atYou && (view.IsArea || cast.Target <= 0 || cast.Target == cast.Actor), english);
            double score = (atYou ? 200 : 0) + (lethal ? 100 : 0) + (view.IsArea ? 50 : 0) + (boss ? 40 : 0);
            scored.Add((score, cast.End, row));
        }

        if (all)
        {
            return scored.OrderBy(s => s.End).Select(s => s.Row).ToList();
        }

        int most = Math.Clamp(settings.CombatMaxCasts, 1, 3);
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.End).Take(most).OrderBy(s => s.End).Select(s => s.Row).ToList();
    }

    public static TargetOfTargetData? TargetOfTarget(Aion2EntityDirectory dir, MeterSettings settings, DateTime now, string language)
    {
        if (dir.LocalTarget is not { } clicked)
        {
            return null;
        }

        // the health bar is always the player's own target's; picture, name and effects are the target of the target's when there is one, else the own target's
        int shown = dir.Combat.TargetOf(clicked.EntityId, now, AimAge) is int aimed && aimed != clicked.EntityId ? aimed : clicked.EntityId;
        var (name, image, isBoss) = Describe(dir, shown, language);
        return new TargetOfTargetData(name, Name(dir, clicked.EntityId, language), image, isBoss,
            settings.CombatHealerHpBar ? dir.HitPoints.Latest(clicked.EntityId)?.Hp : null, settings.CombatHealerHpBar ? dir.HitPoints.MaxOf(clicked.EntityId) : null,
            settings.CombatHealerEffects ? Effects(dir, settings, shown, now) : null);
    }

    /// <summary>The debuffs on an entity (effects from monsters or from the room, no buffs); none when effects are switched off.</summary>
    private static IReadOnlyList<EffectData> Effects(Aion2EntityDirectory dir, MeterSettings settings, int id, DateTime now)
    {
        var effects = new List<EffectData>();
        if (!settings.CombatHealerEffects)
        {
            return effects;
        }

        foreach (BuffNotice buff in dir.Combat.BuffsOn(id, now))
        {
            // only debuffs: put on by a monster (the boss) or by nobody (the room)
            if (!dir.IsKnownMonster(buff.Caster) && buff.Caster > 0)
            {
                continue;
            }

            string time = buff.Ends is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds)).ToString() : "∞";
            string english = Aion2SkillNames.NameOf(buff.SkillId);
            if (System.Text.RegularExpressions.Regex.IsMatch(english, @"^Test\d*_"))
            {
                continue; // an internal test state of the game's data, not an effect the player knows
            }

            string? icon = Aion2SkillIcons.PathFor(buff.SkillId) ?? Aion2SkillIcons.PathForName(english, null) ?? Aion2Artwork.EffectIconPath(buff.SkillId);
            if (icon is null && buff.Caster <= 0 && long.TryParse(english, out _))
            {
                continue; // a nameless effect from nobody is a system state (rest, speed ...), not something the room does to you
            }

            effects.Add(new EffectData(Aion2SkillNames.Display(english), icon, time, true));
        }

        return effects;
    }

    /// <summary>The name of an entity: a player's, a monster's (in the chosen language), or its number when nothing is known.</summary>
    public static string Name(Aion2EntityDirectory dir, int id, string language) => Describe(dir, id, language).Name;

    public static (string Name, string? Image, bool IsBoss) Describe(Aion2EntityDirectory dir, int id, string language)
    {
        if (dir.IsKnownPlayer(id) || dir.IsLocalPlayer(id))
        {
            // the own character's class is also known from the login record, before it used a skill
            string? cls = dir.ClassOf(id) ?? (dir.IsLocalPlayer(id) && dir.LocalCharacter is { } own ? Aion2SkillNames.ClassFromCode(own.ClassCode) : null);
            return (dir.NameFor(id) ?? LocalizationManager.Instance["Combat.You"], cls is null ? null : Aion2Artwork.ClassEmblemPath(cls), false);
        }

        int? npc = dir.NpcIdOf(id);
        bool boss = dir.BossNpcIdOf(id) is not null;
        string name = (npc is int n ? Aion2Npcs.NameOf(n, language) : null) ?? dir.MonsterNameFor(id) ?? "#" + id;
        return (name, boss && npc is int b ? Aion2Artwork.BossPicturePath(b, Aion2Npcs.NameOf(b, "en")) : null, boss);
    }
}

/// <summary>One cast the bar shows. <paramref name="Key"/> names the cast itself (for the voice output, so it speaks each cast once); <paramref name="All"/>: aimed at no one in particular or at someone else but hitting an area (not at you).</summary>
public sealed record CastRowData(
    string Name, string Detail, string TargetText, bool AtYou, bool Area, bool Lethal, double ElapsedMs, int CastMs, int? LandMs, string? IconPath, bool Generic = false, string Key = "", bool All = false,
    string English = "");

/// <summary>The target of the target: who the clicked target is aiming at, with its hit points and effects (null/empty when the stream said nothing).</summary>
public sealed record TargetOfTargetData(string Name, string From, string? ImagePath, bool IsBoss, long? Hp = null, long? MaxHp = null, IReadOnlyList<EffectData>? Effects = null);

/// <summary>An effect on the healer's target.</summary>
public sealed record EffectData(string Label, string? IconPath, string Time, bool Debuff);
