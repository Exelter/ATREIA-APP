using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// The combat overlay: one transparent frame holding the boss cast bar, the target of the target and the healer's target. Each element has its own place,
/// size and scale inside the frame (<see cref="CombatElementHost"/>); the frame itself is placed, pulled and scaled like every overlay (<see cref="MovableOverlay"/>),
/// its scaling grip scaling all the elements together.
/// </summary>
public sealed class CombatOverlayWindow : MovableOverlay
{
    /// <summary>The overlay is three windows, each placed and scaled on its own: the boss cast bar, the target of the target (picture, name, effects) and its health bar.</summary>
    public enum Part { Cast, Target, Hp }

    public const string BossCastId = "BossCast",
        BossHpId = "BossHp", TargetBlockId = "TargetBlock", TargetIconId = "TargetIcon", TargetNameId = "TargetName", TargetHpId = "TargetHp", TargetEffectsId = "TargetEffects";

    private static readonly string[] TargetParts = { TargetIconId, TargetNameId, TargetHpId, TargetEffectsId };

    private static CombatStage? _building;
    private static readonly List<CombatOverlayWindow> Instances = new();

    private readonly Part _part;
    private readonly CombatStage _stage;
    private readonly BossCastView _bossCast = new();
    private readonly TargetIconView _targetIcon = new();
    private readonly TargetNameView _targetName = new();
    private readonly HpBarView _targetHp = new();
    private readonly HpBarView _bossHp = new();
    private readonly EffectBarView _targetEffects = new();
    private readonly Dictionary<string, CombatElementHost> _hosts = new();
    private bool _editing;

    /// <summary>Show sample values where nothing is going on, without the handles of the edit mode (a tool window's live view while its overlay is positioned).</summary>
    public bool SampleWhenIdle { get; set; }

    /// <summary>A tool window's view of this overlay: it always shows something - what is going on, else sample entries - even where the overlay's own switch for it is off, so the page says what the display would be.</summary>
    public bool Preview { get; set; }

    public Part PartKind => _part;

    public CombatOverlayWindow(Part part) : base(part switch { Part.Cast => "Aion DPS Boss Cast", Part.Hp => "Aion DPS Target Health", _ => "Aion DPS Combat" }, NewStage(), () => Load(part), (l, t, sc) => Save(part, l, t, sc),
        SystemParameters.WorkArea.Left + 40, part switch { Part.Cast => 200, Part.Hp => 540, _ => 360 }, () => LoadSize(part), (w, h) => SaveSize(part, w, h), part == Part.Target ? OverlayEdgeMode.Uniform : OverlayEdgeMode.Crop)
    {
        _part = part;
        Instances.Add(this);
        Action<string> onReset = name => { if (name == Title) { Dispatcher.Invoke(ReloadLayouts); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        _stage = _building!;
        _building = null;
        var settings = MeterSettings.Load();
        var loc = LocalizationManager.Instance;
        if (part == Part.Cast)
        {
            AddElement(BossCastId, _bossCast, loc["Combat.Element.BossCast"], settings, _stage);
        }
        else if (part == Part.Hp)
        {
            AddElement(BossHpId, _bossHp, loc["Combat.Element.BossHp"], settings, _stage);
        }
        else
        {
            AddElement(TargetIconId, _targetIcon, loc["Combat.Element.TargetIcon"], settings, _stage);
            AddElement(TargetNameId, _targetName, loc["Combat.Element.TargetName"], settings, _stage);
            AddElement(TargetHpId, _targetHp, loc["Combat.Element.TargetHp"], settings, _stage);
            AddElement(TargetEffectsId, _targetEffects, loc["Combat.Element.TargetEffects"], settings, _stage);
        }

        if (part != Part.Target)
        {
            // the cast bar and the health bar are alone in their window: the window's frame is their outline, so they simply fill it
            CombatElementHost only = _hosts.Values.Single();
            only.Chromeless = true;
            only.Layout.Left = 0;
            only.Layout.Top = 0;
            only.Layout.Scale = 1;
            only.Apply();
            _stage.SizeChanged += (_, e) =>
            {
                only.Layout.Width = e.NewSize.Width;
                only.Layout.Height = e.NewSize.Height;
                only.Apply();
            };
        }

        var (w, h) = LoadSize(part);
        _stage.Wanted = StageSize(part, w, h);
        Closed += (_, _) => Instances.Remove(this);
    }

    private static CombatStage NewStage()
    {
        _building = new CombatStage();
        return _building;
    }

    /// <summary>Where an element begins: on the stage's own units (the stage is <see cref="DefaultFrame"/> less the frame's rim).</summary>
    private static CombatElementLayout DefaultLayout(string id) => id switch
    {
        BossCastId => new CombatElementLayout { Left = 0, Top = 0, Width = 480, Height = 132 },
        TargetBlockId => new CombatElementLayout { Left = 0, Top = 0, Width = 240, Height = 152 },
        TargetIconId => new CombatElementLayout { Left = 0, Top = 0, Width = 52, Height = 52 },
        TargetNameId => new CombatElementLayout { Left = 56, Top = 0, Width = 176, Height = 52 },
        BossHpId => new CombatElementLayout { Left = 0, Top = 0, Width = 280, Height = 30 },
        TargetHpId => new CombatElementLayout { Left = 0, Top = 56, Width = 232, Height = 30 },
        TargetEffectsId => new CombatElementLayout { Left = 0, Top = 90, Width = 232, Height = 54 },
        _ => new CombatElementLayout { Left = 0, Top = 0, Width = 100, Height = 40 },
    };

    /// <summary>The frame's size before anyone pulled it: the stage plus the rim (2 + 1 px each side) and the grip's row.</summary>
    private static Size DefaultFrame(Part part) => part switch { Part.Cast => new Size(486, 152), Part.Hp => new Size(286, 50), _ => new Size(246, 172) };

    private static Size StageSize(Part part, double? frameWidth, double? frameHeight) =>
        new(Math.Max(60, (frameWidth is > 0 ? frameWidth.Value : DefaultFrame(part).Width) - 6), Math.Max(40, (frameHeight is > 0 ? frameHeight.Value : DefaultFrame(part).Height) - 20));

    /// <summary>A layout saved for the old single window can lie far outside the smaller window of its part (the cast bar was invisible then): at least a corner of it must be on the stage.</summary>
    private static bool LiesOnStage(CombatElementLayout layout, Size stage) =>
        layout.Left >= 0 && layout.Top >= 0 && layout.Left <= stage.Width - 36 && layout.Top <= stage.Height - 22;

    private void AddElement(string id, UIElement view, string caption, MeterSettings settings, CombatStage stage)
    {
        if (!settings.CombatElements.TryGetValue(id, out CombatElementLayout? layout) || layout.Width <= 0 || layout.Height <= 0
            || (ReferenceEquals(stage, _stage) && !LiesOnStage(layout, StageSize(_part, LoadSize(_part).Item1, LoadSize(_part).Item2))))
        {
            layout = DefaultLayout(id);
        }

        var host = new CombatElementHost(id, stage, view, layout, caption, SaveElement);
        _hosts[id] = host;
        stage.Children.Add(host);
    }

    private static void SaveElement(string id, CombatElementLayout layout)
    {
        var s = MeterSettings.Load();
        s.CombatElements[id] = new CombatElementLayout { Left = layout.Left, Top = layout.Top, Width = layout.Width, Height = layout.Height, Scale = layout.Scale };
        s.Save();
    }

    private static (double?, double?) LoadSize(Part part)
    {
        var s = MeterSettings.Load();
        return part switch
        {
            Part.Cast => (s.CombatCastOverlayWidth ?? DefaultFrame(part).Width, s.CombatCastOverlayHeight ?? DefaultFrame(part).Height),
            Part.Hp => (s.CombatHpOverlayWidth ?? DefaultFrame(part).Width, s.CombatHpOverlayHeight ?? DefaultFrame(part).Height),
            _ => (s.CombatOverlayWidth ?? DefaultFrame(part).Width, s.CombatOverlayHeight ?? DefaultFrame(part).Height),
        };
    }

    private static void SaveSize(Part part, double? width, double? height)
    {
        var s = MeterSettings.Load();
        if (part == Part.Cast)
        {
            s.CombatCastOverlayWidth = width;
            s.CombatCastOverlayHeight = height;
        }
        else if (part == Part.Hp)
        {
            s.CombatHpOverlayWidth = width;
            s.CombatHpOverlayHeight = height;
        }
        else
        {
            s.CombatOverlayWidth = width;
            s.CombatOverlayHeight = height;
        }

        s.Save();
        if (width is > 0 && height is > 0)
        {
            foreach (var window in Instances.Where(w => w._part == part))
            {
                window._stage.Wanted = StageSize(part, width, height);
            }
        }
    }

    /// <summary>Before the split the overlay was one window: the cast bar at its top, the target of the target 140 below. The cast bar takes over that
    /// window's place (and scale), the target of the target keeps its own spot below it - once, when the cast bar has no place of its own yet.</summary>
    private static MeterSettings LoadMigrated()
    {
        var s = MeterSettings.Load();
        if (s.CombatCastOverlayLeft is null && s.CombatCastOverlayTop is null && s.CombatOverlayLeft is double left && s.CombatOverlayTop is double top)
        {
            double scale = s.CombatOverlayScale;
            s.CombatCastOverlayLeft = left;
            s.CombatCastOverlayTop = top;
            s.CombatCastOverlayScale = scale;
            s.CombatOverlayTop = top + 140 * scale;
            s.Save();
        }

        return s;
    }

    private static (double?, double?, double) Load(Part part)
    {
        var s = LoadMigrated();
        return part switch
        {
            Part.Cast => (s.CombatCastOverlayLeft, s.CombatCastOverlayTop, s.CombatCastOverlayScale),
            Part.Hp => (s.CombatHpOverlayLeft, s.CombatHpOverlayTop, s.CombatHpOverlayScale),
            _ => (s.CombatOverlayLeft, s.CombatOverlayTop, s.CombatOverlayScale),
        };
    }

    private static void Save(Part part, double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        if (part == Part.Cast)
        {
            s.CombatCastOverlayLeft = left;
            s.CombatCastOverlayTop = top;
            s.CombatCastOverlayScale = scale;
        }
        else if (part == Part.Hp)
        {
            s.CombatHpOverlayLeft = left;
            s.CombatHpOverlayTop = top;
            s.CombatHpOverlayScale = scale;
        }
        else
        {
            s.CombatOverlayLeft = left;
            s.CombatOverlayTop = top;
            s.CombatOverlayScale = scale;
        }

        s.Save();
    }

    /// <summary>Puts the elements back where they began.</summary>
    public static void ResetLayout()
    {
        var s = MeterSettings.Load();
        s.CombatElements.Clear();
        s.Save();
        foreach (var window in Instances.ToList())
        {
            window.ReloadLayouts();
        }
    }

    private void ReloadLayouts()
    {
        var settings = MeterSettings.Load();
        foreach (var (id, host) in _hosts)
        {
            CombatElementLayout layout = settings.CombatElements.GetValueOrDefault(id) ?? DefaultLayout(id);
            host.Layout.Left = layout.Left;
            host.Layout.Top = layout.Top;
            host.Layout.Width = layout.Width;
            host.Layout.Height = layout.Height;
            host.Layout.Scale = layout.Scale;
            host.Apply();
        }
    }

    /// <summary>Unlocked, the elements show their outline and handles and a sample where nothing is going on, so they can be placed.</summary>
    public void SetEditing(bool editing)
    {
        if (_editing == editing)
        {
            return;
        }

        _editing = editing;
        foreach (CombatElementHost host in _hosts.Values)
        {
            host.SetEditing(editing);
        }
    }

    /// <summary>Shows one frame. Returns whether anything is visible (live, or a sample while editing).</summary>
    public bool Render(CombatFrame frame, MeterSettings settings)
    {
        var loc = LocalizationManager.Instance;
        if (_part == Part.Cast)
        {
            return RenderCast(frame, settings, loc);
        }

        return _part == Part.Hp ? RenderHp(frame, settings, loc) : RenderTarget(frame, settings, loc);
    }

    private TargetOfTargetData? ShownTarget(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        bool totOn = settings.CombatShowTargetOfTarget || Preview;
        TargetOfTargetData? shown = totOn ? frame.TargetOfTarget : null;
        if (totOn && shown is null && (_editing || SampleWhenIdle || Preview))
        {
            shown = new TargetOfTargetData(loc["Combat.Sample.Player"], loc["Combat.Sample.Boss"], null, false, 6400, 10000, new[]
            {
                new EffectData("Poison", null, "12", true), new EffectData("Slow", null, "5", true),
            });
        }

        return shown;
    }

    private bool RenderHp(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        Opacity = Math.Clamp(settings.BossHpOpacity, 0.0, 1.0);
        BossHpData? boss = frame.Boss;
        if (boss is null && (_editing || SampleWhenIdle || Preview) && (settings.ShowBossHp || Preview))
        {
            boss = new BossHpData(6400, 10000, Since: DateTime.Now - TimeSpan.FromSeconds(151));
        }

        if (boss is not null)
        {
            _bossHp.Show(boss.Hp, boss.Max, false, boss.Check, boss.Since is { } since ? DateTime.Now - since : null, boss.RageSeconds);
        }

        bool visible = boss is not null && (settings.ShowBossHp || Preview);
        _hosts[BossHpId].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        return visible;
    }

    private bool RenderCast(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        Opacity = Math.Clamp(settings.CombatCastOpacity, 0.0, 1.0);
        bool any = false;
        bool castOn = settings.CombatShowBossCast || Preview;
        if (castOn && frame.Casts.Count > 0)
        {
            _bossCast.Show(frame.Casts);
        }
        else if (castOn && (_editing || SampleWhenIdle || Preview))
        {
            // as many sample rows as the settings allow, so the frame can be sized to what it will hold
            _bossCast.Show(Enumerable.Range(0, Math.Clamp(settings.CombatMaxCasts, 1, 3))
                .Select(i => new CastRowData(loc["Combat.Sample.Skill"], loc["Combat.Sample.Boss"], loc["Combat.You"], true, true, false, 700 + i * 200, 1500, 400, null, Generic: true)).ToList());
        }

        bool castShown = castOn && (frame.Casts.Count > 0 || _editing || SampleWhenIdle || Preview);
        _hosts[BossCastId].Visibility = castShown ? Visibility.Visible : Visibility.Collapsed;
        any |= castShown;
        return any;
    }

    private bool RenderTarget(CombatFrame frame, MeterSettings settings, LocalizationManager loc)
    {
        Opacity = Math.Clamp(settings.CombatTargetOpacity, 0.0, 1.0);
        bool any = false;
        TargetOfTargetData? shownTot = ShownTarget(frame, settings, loc);
        if (shownTot is { } tot)
        {
            _targetIcon.Show(tot);
            _targetName.Show(tot);
            _targetHp.Show(tot.Hp, tot.MaxHp, tot.Effects is { Count: > 0 });
            _targetEffects.Show(tot.Effects ?? Array.Empty<EffectData>());
        }

        bool totShown = shownTot is not null;
        _hosts[TargetIconId].Visibility = totShown ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetNameId].Visibility = totShown ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetHpId].Visibility = totShown && settings.CombatHealerHpBar ? Visibility.Visible : Visibility.Collapsed;
        _hosts[TargetEffectsId].Visibility = totShown && settings.CombatHealerEffects ? Visibility.Visible : Visibility.Collapsed;
        any |= totShown;

        return any;
    }
}

/// <summary>
/// Runs the combat overlay: a few times a second it works out what is going on (<see cref="CombatOverlayData"/>) and shows it - only while the game is in
/// front, or while the overlay is unlocked so it can be placed. The settings are read once a second.
/// </summary>
public sealed class CombatOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    /// <summary>Held against the boss's hit points: whether the damage counted matches what it lost (the meter owns the damage events).</summary>
    private readonly Func<int, HpCheckResult?>? _hpCheck;
    private DateTime _checkAt;
    private int _checkFor;
    private string _checkText = "";
    private CombatOverlayWindow? _overlay, _castOverlay;
    private readonly List<CombatOverlayWindow> _views = new();

    /// <summary>The three overlays as never shown copies whose content a tool window shows live, with sample values while nothing is going on.</summary>
    public IReadOnlyList<(string TitleKey, CombatOverlayWindow View)> CreateViews()
    {
        var made = new List<(string, CombatOverlayWindow)>();
        // the boss's health is no longer here: it is the boss line on the meter (6 November 2026, the owner: "we dont need the boss fight ... since we have it on the meter")
        foreach (var (key, part) in new[] { ("Settings.Combat.CardBossCast", CombatOverlayWindow.Part.Cast), ("Combat.Element.TargetOfTarget", CombatOverlayWindow.Part.Target) })
        {
            var view = new CombatOverlayWindow(part) { Preview = true };
            _views.Add(view);
            made.Add((key, view));
        }

        return made;
    }

    public void RemoveViews(IEnumerable<CombatOverlayWindow> views)
    {
        foreach (var view in views.ToList())
        {
            _views.Remove(view);
            view.Close();
        }
    }
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _savedLessonsAt = DateTime.UtcNow;
    private Aion2EntityDirectory? _lessonsFor;
    private readonly CastSpeech _speech = new();

    public CombatOverlayController(Func<Aion2EntityDirectory?> directory, Func<int, HpCheckResult?>? hpCheck = null)
    {
        _directory = directory;
        _hpCheck = hpCheck;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront()
    {
        _overlay?.RaiseToFront();
        _castOverlay?.RaiseToFront();
    }

    public void Dispose()
    {
        _timer.Stop();
        _directory()?.Combat.SaveIfChanged();
        _overlay?.Close();
        _castOverlay?.Close();
    }

    /// <summary>What the overlay shows now: the casts, the target of the target and the boss's health (with the check mark of its hit points).</summary>
    private CombatFrame BuildFrame(Aion2EntityDirectory? dir, MeterSettings settings)
    {
        CombatFrame frame = dir is null
            ? new CombatFrame(Array.Empty<CastRowData>(), null)
            : CombatOverlayData.Build(dir, settings, DateTime.Now, LocalizationManager.Instance.Language);
        if (frame.Boss is { } boss && _hpCheck is not null)
        {
            if (boss.Id != _checkFor || (DateTime.UtcNow - _checkAt).TotalSeconds >= 1)
            {
                HpCheckResult? check = _hpCheck(boss.Id);
                _checkText = check is null ? "" : check.OverFullHealth || check.Verdict != HpCheckVerdict.Match ? "⚠ " + check.Ratio.ToString("P0", System.Globalization.CultureInfo.CurrentCulture) : "✓";
                _checkFor = boss.Id;
                _checkAt = DateTime.UtcNow;
            }

            frame = frame with { Boss = boss with { Check = _checkText } };
        }

        return frame;
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        var settings = _settings;
        Aion2EntityDirectory? dir = _directory();
        if (dir is not null && !ReferenceEquals(dir, _lessonsFor))
        {
            _lessonsFor = dir; // what was learnt about boss skills (area or not, when the damage lands) lives between sessions
            dir.Combat.FilePath = System.IO.Path.Combine(MeterSettings.DataDirectory, "cast-skills.json");
            dir.Combat.Load();
        }

        if (dir is not null && (DateTime.UtcNow - _savedLessonsAt).TotalSeconds >= 60)
        {
            _savedLessonsAt = DateTime.UtcNow;
            dir.Combat.SaveIfChanged();
        }

        // the voice output does not depend on the overlay: it runs before the overlay's switches can end the tick
        _speech.Tick(dir, settings);

        if (_views.Count > 0)
        {
            CombatFrame viewFrame = BuildFrame(dir, settings);
            foreach (var view in _views)
            {
                // sample values only while that overlay is being positioned
                view.SampleWhenIdle = !(view.PartKind switch
                {
                    CombatOverlayWindow.Part.Cast => settings.CombatCastOverlayLocked ?? settings.CombatOverlayLocked,
                    CombatOverlayWindow.Part.Hp => settings.CombatHpOverlayLocked ?? settings.CombatOverlayLocked,
                    _ => settings.CombatOverlayLocked,
                });
                view.Render(viewFrame, settings);
            }
        }

        if (!settings.ShowCombatOverlay)
        {
            _overlay?.ShowOverlay(false);
            _castOverlay?.ShowOverlay(false);
            return;
        }

        _overlay ??= new CombatOverlayWindow(CombatOverlayWindow.Part.Target);
        _castOverlay ??= new CombatOverlayWindow(CombatOverlayWindow.Part.Cast);
        // each of the two is positioned on its own; a window that is switched off is neither shown nor positioned (the boss's health is on the meter)
        bool lockedTarget = settings.CombatOverlayLocked, lockedCast = settings.CombatCastOverlayLocked ?? settings.CombatOverlayLocked;
        foreach (var (window, locked) in new[] { (_overlay, lockedTarget), (_castOverlay, lockedCast) })
        {
            window.ApplyLockedIfChanged(locked);
            window.SetEditing(!locked);
        }

        bool inGame = GameWindow.OverlayMayShow();
        if (!inGame)
        {
            _overlay.ShowOverlay(false);
            _castOverlay.ShowOverlay(false);
            return;
        }

        CombatFrame frame = BuildFrame(dir, settings);

        // only over the game (never on the desktop), each with what it has to show
        _overlay.ShowOverlay(_overlay.Render(frame, settings) && inGame);
        _castOverlay.ShowOverlay(_castOverlay.Render(frame, settings) && inGame);
    }
}
