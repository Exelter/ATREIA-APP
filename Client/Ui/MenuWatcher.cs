namespace AionDPS.Ui;

/// <summary>
/// Hides the meter's overlays while a game window is open (Arcana, enchanting, the inventory ...), by the mouse cursor (see <see cref="MenuDetector"/>): in the
/// control mode that hides it while playing, the game shows it for its windows. No pictures of the game are taken for this any more (the owner, 13 November
/// 2026, after a tester's Discord stream of the game kept pausing while the app ran: until 2.0.20 the game window's picture was also looked at, to see its HUD
/// covered); in the classic control mode, where the cursor shows all the time, nothing tells a game window, so nothing is hidden there.
/// <see cref="Hidden"/> is what <see cref="GameWindow"/> reads, so every overlay that is shown only while the game is in front steps aside by itself. Runs on the
/// UI thread, so the flag needs no lock.
/// </summary>
public sealed class MenuWatcher : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly MenuDetector _detector = new();
    private readonly ControlModeLearner _learner = new();
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DateTime _lastOwnHit = DateTime.MinValue;
    private int _look;
    private bool _held;
    private DateTime? _lastClick;

    public MenuWatcher()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>True while a menu is open and the overlays should be out of the way.</summary>
    public static bool Hidden { get; private set; }

    /// <summary>The control mode seen so far (also kept in the settings).</summary>
    public static ControlMode Mode { get; private set; } = ControlMode.Unknown;

    /// <summary>The local player just dealt damage (the control mode is learnt while fighting).</summary>
    public void NoteOwnHit() => _lastOwnHit = DateTime.UtcNow;

    public void Dispose()
    {
        _timer.Stop();
        Hidden = false;
    }

    public static bool AllOverlaysLocked(MeterSettings s) => UnlockedOverlays(s).Count == 0;

    /// <summary>Localization keys of the names of the overlays that are unlocked (being positioned), which keeps the menus from hiding anything.</summary>
    public static List<string> UnlockedOverlays(MeterSettings s)
    {
        var open = new List<string>();
        void Check(bool locked, string key)
        {
            if (!locked)
            {
                open.Add(key);
            }
        }

        Check(s.PetFarmLocked, "Settings.PetMap.CardMap");
        Check(s.PetListLocked ?? true, "Settings.PetMap.CardList");
        Check(s.PetInfoLocked, "Settings.PetInfo.Title");
        Check(s.TimetableLocked, "Settings.Hotkey.Timetable");
        Check(s.BossOverlayLocked, "Settings.Menu.Name.Boss");
        Check(s.NetworkOverlayLocked, "Settings.Menu.Name.Network");
        Check(s.CombatOverlayLocked, "Combat.Element.TargetOfTarget");
        Check(s.CombatCastOverlayLocked ?? true, "Settings.Combat.CardBossCast");
        Check(s.HotkeyOverlayLocked ?? true, "Settings.HotkeyOverlay.Title");
        Check(s.AssistOverlayLocked, "Settings.Menu.Name.Assist");
        Check(s.LootOverlayLocked, "Settings.Menu.Name.Loot");
        Check(s.GatherOverlayLocked, "Settings.Nav.Gather");
        Check(s.SeasonOverlayLocked, "Checklist.Season");
        Check(s.CraftOverlayLocked, "Settings.Menu.Name.Craft");
        Check(s.OverlayMenuLocked, "Shell.OvMenu");
        return open;
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
            if (Enum.TryParse(_settings.MenuControlMode, true, out ControlMode saved) && _learner.Mode == ControlMode.Unknown && saved != ControlMode.Unknown)
            {
                Mode = saved; // what an earlier session learnt counts until this one has seen enough
            }
        }

        bool inGame = GameWindow.ForegroundIsGame();
        bool cursor = inGame && GameWindow.IsCursorVisible();
        ControlMode learned = _learner.Note(DateTime.UtcNow, inGame, DateTime.UtcNow - _lastOwnHit < TimeSpan.FromSeconds(2), cursor);
        if (learned != ControlMode.Unknown && learned != Mode)
        {
            Mode = learned;
            _settings.MenuControlMode = learned.ToString();
            var onDisk = MeterSettings.Load();
            onDisk.MenuControlMode = learned.ToString();
            onDisk.Save();
        }

        // the cursor shows all the time in the classic mode: it tells nothing there
        if (!_settings.HideDuringMenus || Mode == ControlMode.Classic)
        {
            _detector.Reset();
            Hidden = false;
            return;
        }

        // an unlocked overlay (being placed) pauses the hiding: the cursor shows while one drags it
        bool placing = !AllOverlaysLocked(_settings);

        // the keys are asked only while the cursor shows, and every other look: during play this costs nothing
        if (!cursor)
        {
            _held = false;
            _look = 0;
        }
        else if (_look++ % 2 == 0)
        {
            _held = GameWindow.AnyKeyDown();
        }

        if (inGame && GameWindow.MouseButtonDown())
        {
            _lastClick = DateTime.UtcNow;
        }

        Hidden = _detector.Update(DateTime.UtcNow, inGame, cursor, _held, placing, _lastClick);
    }
}
