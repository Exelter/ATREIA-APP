using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>One enemy on the radar: who, how strong against the own character, and his record against it.</summary>
public sealed record RadarEnemy(int Id, string Name, string? ClassName, int? CombatPower, int? ItemLevel, double? Against, Rival? Rival, DateTime Since);

/// <summary>What the radar shows: the enemies in view, the strongest first, and a much stronger one that just came (null: none).</summary>
public sealed record RadarView(IReadOnlyList<RadarEnemy> Enemies, RadarEnemy? Warning, bool Sample = false)
{
    /// <summary>A newcomer this much stronger than the own character is announced.</summary>
    public const double WarnAbove = 0.20;

    /// <summary>The enemies in view now (another faction than the own character's), with what is known of each.</summary>
    public static RadarView Of(Aion2EntityDirectory dir, int? ownPower, DateTime now, Func<int, string?, (int? Power, int? Items)?> looked)
    {
        string? mine = dir.LocalPlayerId >= 0 ? dir.FactionOf(dir.LocalPlayerId) : null;
        mine ??= dir.LocalCharacter is { ServerId: > 0 } me ? (me.ServerId / 1000 == 1 ? "Elyos" : me.ServerId / 1000 == 2 ? "Asmodian" : null) : null;
        if (mine is null)
        {
            return new RadarView(Array.Empty<RadarEnemy>(), null);
        }

        var list = new List<RadarEnemy>();
        foreach ((int id, DateTime since) in dir.PlayersInView())
        {
            if (id == dir.LocalPlayerId || dir.FactionOf(id) is not { } theirs || theirs == mine)
            {
                continue;
            }

            Aion2SeenProfile? profile = dir.SeenProfileOf(id);
            string name = dir.NameFor(id) ?? "Unknown player";
            int server = dir.ServerIdOf(id) ?? 0;
            int? power = profile?.CombatPower is int cp and > 0 and not 397228 ? cp : null;
            int? items = profile?.ItemLevel is > 0 ? profile.ItemLevel : null;
            if ((power is null || items is null) && looked(id, name) is { } got)
            {
                power ??= got.Power;
                items ??= got.Items;
            }

            string? className = profile?.ClassId is int classId ? Aion2SkillNames.ClassFromCode(4 * classId + 1) : dir.ClassOf(id);
            double? against = power is int p && ownPower is int o && o > 0 ? (double)p / o - 1 : null;
            list.Add(new RadarEnemy(id, name, className, power, items, against, Aion2Pvp.RivalOf(name, server), since));
        }

        list = list.OrderByDescending(e => e.CombatPower.HasValue).ThenByDescending(e => e.CombatPower ?? 0).ThenBy(e => e.Name, StringComparer.CurrentCulture).ToList();
        RadarEnemy? warning = list.Where(e => e.Against >= WarnAbove && now - e.Since <= TimeSpan.FromSeconds(12)).OrderByDescending(e => e.CombatPower).FirstOrDefault();
        return new RadarView(list, warning);
    }

    /// <summary>What the radar shows while it is placed with no enemy around.</summary>
    public static RadarView SampleView(DateTime now)
    {
        var kraeger = new RadarEnemy(1, "Kraeger", "Templar", 2_418_300, 3_120, 0.12, new Rival { Name = "Kraeger", Met = 4, Kills = 2, Deaths = 2, KilledBy = 1 }, now);
        return new RadarView(new[]
        {
            kraeger,
            new RadarEnemy(2, "Vesh", "Assassin", 2_205_900, 3_044, 0.02, null, now),
            new RadarEnemy(3, "Morrigane", "Sorcerer", 1_984_200, 2_967, -0.08, new Rival { Name = "Morrigane", Met = 1, Kills = 1 }, now),
            new RadarEnemy(4, "Toskar", "Gladiator", 1_712_450, 2_880, -0.21, null, now),
            new RadarEnemy(5, "Ilyre", "Cleric", null, null, null, null, now),
        }, kraeger, Sample: true);
    }
}

/// <summary>
/// The enemy radar (10 November 2026, the owner picked: "a list of every enemy player near you, strongest first, with class and CP; it warns you when someone
/// much stronger shows up"). The players in view come and go with the game's own frames (a player appeared, 0x4536; a player left, 0x4736); the ones of the
/// other faction are listed with class, Combat Power (against the own) and the record against each (<see cref="Aion2Pvp"/>). A newcomer 20 % stronger than
/// the own character shows in red at the top for a few seconds. Over the game only; empty, it is not there.
/// </summary>
public sealed class EnemyRadarWindow : MovableOverlay
{
    private const int Lines = 10;
    private static readonly Color Enemy = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private readonly ContentControl _content = new() { MinWidth = 270, Focusable = false };
    private string _shown = "";

    public EnemyRadarWindow() : base(OverlayPlaces.EnemyRadar, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 330, SystemParameters.WorkArea.Top + 260,
        LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = _content;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.EnemyRadarWidth, s.EnemyRadarHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.EnemyRadarWidth = width;
        s.EnemyRadarHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.EnemyRadarLeft, s.EnemyRadarTop, s.EnemyRadarScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.EnemyRadarLeft = left;
        s.EnemyRadarTop = top;
        s.EnemyRadarScale = scale;
        s.Save();
    }

    public new bool HasContent => _content.Content is not null;

    public void Render(RadarView? view)
    {
        string key = view is null ? "" : string.Join("|", view.Sample, view.Warning?.Id, string.Join(",", view.Enemies.Take(Lines + 1).Select(e => $"{e.Id}:{e.CombatPower}:{e.ClassName}:{e.Rival?.Kills}:{e.Rival?.Deaths}")),
            view.Enemies.Count);
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _content.Content = view is null || view.Enemies.Count == 0 ? null : View(view);
    }

    /// <summary>The radar (the pictures draw it too).</summary>
    public static FrameworkElement View(RadarView view)
    {
        var stack = new StackPanel();
        stack.Children.Add(OverlaySkin.Heading(view.Sample ? "Enemies nearby · sample" : "Enemies nearby", view.Enemies.Count.ToString(CultureInfo.CurrentCulture), Enemy, Enemy));
        if (view.Warning is { } w)
        {
            var warn = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 5), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0x30, Enemy.R, Enemy.G, Enemy.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, Enemy.R, Enemy.G, Enemy.B)),
            };
            var text = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
            text.Inlines.Add(new System.Windows.Documents.Run("STRONGER ENEMY  ") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Enemy) });
            text.Inlines.Add(new System.Windows.Documents.Run(w.Name) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            text.Inlines.Add(new System.Windows.Documents.Run($"  {GrindView.Short(w.CombatPower ?? 0)}  ▲ {w.Against:P0}") { Foreground = new SolidColorBrush(Enemy) });
            warn.Child = text;
            stack.Children.Add(warn);
        }

        bool band = false;
        foreach (RadarEnemy e in view.Enemies.Take(Lines))
        {
            stack.Children.Add(Row(e, band));
            band = !band;
        }

        if (view.Enemies.Count > Lines)
        {
            stack.Children.Add(new TextBlock { Text = $"+ {view.Enemies.Count - Lines} more", FontSize = 10.5, Foreground = new SolidColorBrush(Faint), Margin = new Thickness(6, 2, 0, 0) });
        }

        return OverlaySkin.Panel(stack, new Thickness(11, 8, 11, 9));
    }

    private static Border Row(RadarEnemy e, bool band)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Source = MainWindow.ClassIcon(e.ClassName), Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBlock { Text = e.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = e.ClassName is null ? Brushes.White : ClassBrushes.Text(e.ClassName) };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var power = new TextBlock { Text = e.CombatPower is int cp ? GrindView.Short(cp) : "…", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0),
            Foreground = new SolidColorBrush(OverlaySkin.Gold), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right,
            ToolTip = e.ItemLevel is int il ? $"Item level {il:N0}" : null };
        power.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        Grid.SetColumn(power, 2);
        grid.Children.Add(power);
        if (e.Against is double a)
        {
            bool stronger = a > 0.005, weaker = a < -0.005;
            var vs = new TextBlock { Text = stronger ? $"▲{a:P0}" : weaker ? $"▼{-a:P0}" : "=", FontSize = 10.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(stronger ? Enemy : weaker ? OverlaySkin.Good : Muted) };
            Grid.SetColumn(vs, 3);
            grid.Children.Add(vs);
        }

        if (e.Rival is { } r && r.Met > 0)
        {
            var record = new TextBlock { Text = $"{r.Kills}–{r.Deaths}", FontSize = 10.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(r.Kills > r.Deaths ? OverlaySkin.Good : r.Kills < r.Deaths ? Enemy : Muted),
                ToolTip = $"Met {r.Met}×: you killed {r.Kills.ToString(CultureInfo.CurrentCulture)}, died {r.Deaths.ToString(CultureInfo.CurrentCulture)}" };
            Grid.SetColumn(record, 4);
            grid.Children.Add(record);
        }

        return new Border
        {
            Child = grid, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(5), Margin = new Thickness(-6, 0, -6, 0),
            Background = band ? new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
        };
    }
}

/// <summary>Runs the radar: twice a second it reads who is in view, and shows the radar while the game is in front and an enemy is near (a sample while it is placed).</summary>
public sealed class EnemyRadarController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly Func<int, string?, (int? Power, int? Items)?> _looked;
    private EnemyRadarWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private int? _ownPower;
    private DateTime _ownPowerAt = DateTime.MinValue;

    public EnemyRadarController(Func<Aion2EntityDirectory?> directory, Func<int, string?, (int? Power, int? Items)?> looked)
    {
        _directory = directory;
        _looked = looked;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.ShowEnemyRadar)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.EnemyRadarLocked;
        Aion2EntityDirectory? dir = _directory();
        if (dir is not null && DateTime.UtcNow - _ownPowerAt > TimeSpan.FromSeconds(10))
        {
            _ownPowerAt = DateTime.UtcNow;
            _ownPower = MainWindow.OwnCombatPower(dir);
        }

        RadarView? view = dir is null ? null : RadarView.Of(dir, _ownPower, DateTime.Now, _looked);
        if ((view is null || view.Enemies.Count == 0) && !locked)
        {
            view = RadarView.SampleView(DateTime.Now);
        }

        if ((view is null || view.Enemies.Count == 0) && _overlay is null)
        {
            return;
        }

        _overlay ??= new EnemyRadarWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(view);
        _overlay.ShowOverlay(view is { Enemies.Count: > 0 } && GameWindow.OverlayMayShow());
    }
}

/// <summary>
/// The death recap (10 November 2026, the owner picked: "when you die, a card shows who hit you, with which skills, how much, and over how many seconds"):
/// the own character's death as <see cref="Aion2Pvp"/> worked it out - the killing blow, every attacker with his share and skills, the heals that came, and the
/// seconds from the first hit of the burst. It comes up at the death and goes after half a minute. Over the game only.
/// </summary>
public sealed class DeathRecapWindow : MovableOverlay
{
    private static readonly Color Red = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private static readonly Color Faint = Color.FromRgb(0x6F, 0x87, 0x97);
    private readonly ContentControl _content = new() { MinWidth = 300, Focusable = false };
    private string _shown = "";

    public DeathRecapWindow() : base(OverlayPlaces.DeathRecap, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 - 170,
        SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.55, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = _content;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.DeathRecapWidth, s.DeathRecapHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.DeathRecapWidth = width;
        s.DeathRecapHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.DeathRecapLeft, s.DeathRecapTop, s.DeathRecapScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.DeathRecapLeft = left;
        s.DeathRecapTop = top;
        s.DeathRecapScale = scale;
        s.Save();
    }

    public new bool HasContent => _content.Content is not null;

    public void Render(DeathRecap? recap, bool sample)
    {
        string key = recap is null ? "" : sample ? "sample" : recap.At.Ticks + "|" + recap.Victim; // the sample is made anew each tick: drawn once
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _content.Content = recap is null ? null : View(recap, sample);
    }

    /// <summary>The recap card (the pictures draw it too).</summary>
    public static FrameworkElement View(DeathRecap recap, bool sample = false)
    {
        var stack = new StackPanel();
        string seconds = recap.Seconds.ToString(recap.Seconds < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " s";
        stack.Children.Add(OverlaySkin.Heading(sample ? "You died · sample" : "You died", "in " + seconds, Red, Red));

        // the killing blow
        if (recap.Killer is { } killer)
        {
            var blow = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            blow.Inlines.Add(new System.Windows.Documents.Run("Killed by ") { Foreground = new SolidColorBrush(Muted) });
            blow.Inlines.Add(new System.Windows.Documents.Run(killer.Name) { FontWeight = FontWeights.SemiBold, Foreground = killer.ClassName is { } c ? ClassBrushes.Text(c) : Brushes.White });
            if (recap.KillingSkill is { } skill)
            {
                blow.Inlines.Add(new System.Windows.Documents.Run("  ·  " + skill + "  ") { Foreground = new SolidColorBrush(Muted) });
                blow.Inlines.Add(new System.Windows.Documents.Run(recap.KillingAmount.ToString("N0", CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Red) });
            }

            stack.Children.Add(blow);
        }

        string totals = $"{recap.Taken.ToString("N0", CultureInfo.CurrentCulture)} taken" + (recap.Healed > 0 ? $"  ·  {recap.Healed.ToString("N0", CultureInfo.CurrentCulture)} healed" : "");
        stack.Children.Add(new TextBlock { Text = totals, FontSize = 11, Foreground = new SolidColorBrush(Faint), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(OverlaySkin.Divider(2, 5));

        // who: share bar, amount, his skills under it
        foreach (RecapSource s in recap.Sources.Take(5))
        {
            double share = recap.Taken > 0 ? (double)s.Amount / recap.Taken : 0;
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            if (s.Player)
            {
                grid.Children.Add(new Image { Source = MainWindow.ClassIcon(s.ClassName), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            var name = new TextBlock { Text = s.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = s.ClassName is { } cn ? ClassBrushes.Text(cn) : new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF2)) };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var amount = new TextBlock { Text = s.Amount.ToString("N0", CultureInfo.CurrentCulture), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0),
                Foreground = Brushes.White };
            amount.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            Grid.SetColumn(amount, 2);
            grid.Children.Add(amount);
            var pct = new TextBlock { Text = share.ToString("P0", CultureInfo.CurrentCulture), FontSize = 11, TextAlignment = TextAlignment.Right, Foreground = new SolidColorBrush(Muted),
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pct, 3);
            grid.Children.Add(pct);
            stack.Children.Add(grid);
            stack.Children.Add(ShareBar(share, s.Player ? Red : Color.FromRgb(0xC8, 0x9B, 0x5A)));
            // his two heaviest skills on the card; all of them on hover
            static string SkillText(RecapSkill k) => $"{k.Name} ×{k.Hits} {k.Amount.ToString("N0", CultureInfo.CurrentCulture)}";
            string skills = string.Join("  ·  ", s.Skills.Take(2).Select(SkillText));
            if (skills.Length > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = skills, FontSize = 10.5, Foreground = new SolidColorBrush(Faint), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 3),
                    ToolTip = s.Skills.Count > 2 ? string.Join("\n", s.Skills.Select(SkillText)) : null,
                });
            }
        }

        if (recap.Sources.Count > 5)
        {
            stack.Children.Add(new TextBlock { Text = $"+ {recap.Sources.Count - 5} more", FontSize = 10.5, Foreground = new SolidColorBrush(Faint) });
        }

        if (recap.Sources.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "No hits were seen before it (a fall, or the meter came up after).", FontSize = 11.5, Foreground = new SolidColorBrush(Muted),
                TextWrapping = TextWrapping.Wrap });
        }

        return OverlaySkin.Panel(stack, new Thickness(12, 9, 12, 10));
    }

    private static Border ShareBar(double share, Color colour)
    {
        var grid = new Grid();
        double pct = Math.Clamp(share * 100, 0, 100);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(colour) });
        return new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2, 0, 0), ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Child = grid };
    }

    /// <summary>What the card shows while it is placed.</summary>
    public static DeathRecap Sample(DateTime now) => new(now, 0, "You", 4.2, 41_930, 3_100,
        new RecapSource(1, "Kraeger", "Templar", true, 28_410, 6, new[] { new RecapSkill("Shield Bash", 0, 3, 18_200, 1), new RecapSkill("Punishing Blow", 0, 2, 7_100, 0), new RecapSkill("Attack", 0, 1, 3_110, 0) }),
        "Shield Bash", 8_214,
        new[]
        {
            new RecapSource(1, "Kraeger", "Templar", true, 28_410, 6, new[] { new RecapSkill("Shield Bash", 0, 3, 18_200, 1), new RecapSkill("Punishing Blow", 0, 2, 7_100, 0), new RecapSkill("Attack", 0, 1, 3_110, 0) }),
            new RecapSource(2, "Morrigane", "Sorcerer", true, 9_120, 3, new[] { new RecapSkill("Flame Bolt", 0, 2, 6_800, 1), new RecapSkill("Frost Shard", 0, 1, 2_320, 0) }),
            new RecapSource(3, "Balaur Warrior", null, false, 4_400, 2, new[] { new RecapSkill("Cleave", 0, 2, 4_400, 0) }),
        });
}

/// <summary>Shows the death recap when the own character dies, for half a minute (a sample while it is placed).</summary>
public sealed class DeathRecapController : IDisposable
{
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(30);
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DeathRecapWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private DeathRecap? _recap;
    private DateTime _diedAt = DateTime.MinValue;

    public DeathRecapController()
    {
        Aion2Pvp.Died += OnDied;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void OnDied(DeathRecap recap)
    {
        _recap = recap;
        _diedAt = DateTime.UtcNow;
        Tick();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        Aion2Pvp.Died -= OnDied;
        _timer.Stop();
        _overlay?.Close();
    }

    private void Tick()
    {
        if ((DateTime.UtcNow - _settingsAt).TotalSeconds >= 1)
        {
            _settings = MeterSettings.Load();
            _settingsAt = DateTime.UtcNow;
        }

        if (!_settings.ShowDeathRecap)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.DeathRecapLocked;
        DeathRecap? recap = _recap is not null && DateTime.UtcNow - _diedAt < Shown ? _recap : null;
        bool sample = false;
        if (recap is null && !locked)
        {
            recap = DeathRecapWindow.Sample(DateTime.Now);
            sample = true;
        }

        if (recap is null && _overlay is null)
        {
            return;
        }

        _overlay ??= new DeathRecapWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(recap, sample);
        // the game shows its own death window: the recap stays over it (the meter's game-window rule does not hold for it)
        _overlay.ShowOverlay(recap is not null && GameWindow.OverlayMayShow(ignoreMenu: true));
    }
}
