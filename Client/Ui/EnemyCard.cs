using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>Who the selected player is, as the game showed him: name, class, level, server, legion, Combat Power and item level, and whether he is an enemy.</summary>
public sealed record EnemyCardData(string Name, string? ClassName, int? Level, string? Server, string? Legion, int? CombatPower, int? ItemLevel, bool Enemy, int? OwnPower,
    bool Sample = false)
{
    /// <summary>His Combat Power against the own: +0.12 is 12 % more (null when one of them is not known).</summary>
    public double? Against => CombatPower is int theirs && OwnPower is int mine && mine > 0 ? (double)theirs / mine - 1 : null;

    /// <summary>The card of the player the local player has selected, when it is a player (an enemy only unless <paramref name="allies"/>); null otherwise.</summary>
    public static EnemyCardData? Of(Aion2EntityDirectory dir, bool allies, int? ownPower)
    {
        if (dir.OwnTarget is not { } target || !dir.IsKnownPlayer(target.EntityId) || target.EntityId == dir.LocalPlayerId)
        {
            return null;
        }

        int id = target.EntityId;
        string? theirs = dir.FactionOf(id);
        string? mine = dir.LocalPlayerId >= 0 ? dir.FactionOf(dir.LocalPlayerId) : null;
        mine ??= dir.LocalCharacter is { ServerId: > 0 } me ? (me.ServerId / 1000 == 1 ? "Elyos" : me.ServerId / 1000 == 2 ? "Asmodian" : null) : null;
        bool enemy = theirs is not null && mine is not null && theirs != mine;
        if (!enemy && !allies)
        {
            return null;
        }

        Aion2SeenProfile? profile = dir.SeenProfileOf(id);
        string? className = profile?.ClassId is int classId ? Aion2SkillNames.ClassFromCode(4 * classId + 1) : dir.ClassOf(id);
        string? server = dir.ServerIdOf(id) is int sid && Aion2Servers.IsKnown(sid) ? Aion2Servers.NameOf(sid) : null;
        int? power = profile?.CombatPower is int cp and > 0 and not 397228 ? cp : null; // 397228: the fixed number of the 10 against 10 arena
        return new EnemyCardData(dir.NameFor(id) ?? "Unknown player", className, profile?.Level, server, dir.GuildOf(id), power, profile?.ItemLevel is > 0 ? profile.ItemLevel : null,
            enemy, ownPower);
    }

    /// <summary>What the card shows while it is placed with nobody selected.</summary>
    public static EnemyCardData SampleCard(int? ownPower) => new("Kraeger", "Templar", 45, "Nezekan", "Iron Oath", 2_418_300, 3_120, true, ownPower ?? 2_160_000, Sample: true);
}

/// <summary>
/// The enemy card (10 November 2026, the owner: "when you tab to a player in the Abyss, an enemy, it shows their class, their CP, their item level"): the
/// player the local player selected (the game's own selection message, see <see cref="Aion2EntityDirectory.OwnTarget"/>), with what his "player appeared"
/// frame told - class, level, Combat Power, item level - his server and legion, and his Combat Power against the own. Enemies only, unless allies are
/// switched on too. Placed, scaled and locked like the other overlays; over the game only.
/// </summary>
public sealed class EnemyCardWindow : MovableOverlay
{
    private static readonly Color Enemy = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Ally = Color.FromRgb(0x7F, 0xB8, 0xFF);
    private static readonly Color Muted = Color.FromRgb(0x9D, 0xB3, 0xC2);
    private readonly ContentControl _content = new() { MinWidth = 250, Focusable = false };
    private string _shown = "";

    public EnemyCardWindow() : base(OverlayPlaces.EnemyCard, new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2 + 140,
        SystemParameters.WorkArea.Top + 90, LoadSize, SaveSize, OverlayEdgeMode.Width)
    {
        Body = _content;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.EnemyCardWidth, s.EnemyCardHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.EnemyCardWidth = width;
        s.EnemyCardHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.EnemyCardLeft, s.EnemyCardTop, s.EnemyCardScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.EnemyCardLeft = left;
        s.EnemyCardTop = top;
        s.EnemyCardScale = scale;
        s.Save();
    }

    public new bool HasContent => _content.Content is not null;

    public void Render(EnemyCardData? card)
    {
        string key = card is null ? "" : string.Join("|", card.Name, card.ClassName, card.Level, card.Server, card.Legion, card.CombatPower, card.ItemLevel, card.Enemy, card.OwnPower, card.Sample);
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _content.Content = card is null ? null : View(card);
    }

    /// <summary>The card (the pictures draw it too).</summary>
    public static FrameworkElement View(EnemyCardData card)
    {
        Color side = card.Enemy ? Enemy : Ally;
        var stack = new StackPanel();

        // who: the class icon in his side's colour, the name, the side; under it class, level, server and legion
        var head = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(side), Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x30, side.R, side.G, side.B)), VerticalAlignment = VerticalAlignment.Center,
            Child = new Image { Source = MainWindow.ClassIcon(card.ClassName), Width = 24, Height = 24, Stretch = Stretch.Uniform },
        };
        head.Children.Add(badge);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = card.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = card.ClassName is null ? Brushes.White : ClassBrushes.Text(card.ClassName),
            TextTrimming = TextTrimming.CharacterEllipsis });
        string about = string.Join("  ·  ", new[] { card.ClassName, card.Level is int lv ? "Lv " + lv : null, card.Server }.Where(t => t is { Length: > 0 }));
        names.Children.Add(new TextBlock { Text = about, FontSize = 11, Foreground = new SolidColorBrush(Muted), TextTrimming = TextTrimming.CharacterEllipsis });
        if (card.Legion is { Length: > 0 } legion)
        {
            names.Children.Add(new TextBlock { Text = "<" + legion + ">", FontSize = 11, Foreground = new SolidColorBrush(Muted), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        var tag = new Border
        {
            Padding = new Thickness(7, 1, 7, 2), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 1, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x26, side.R, side.G, side.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, side.R, side.G, side.B)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = card.Sample ? "SAMPLE" : card.Enemy ? "ENEMY" : "ALLY", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(side) },
        };
        Grid.SetColumn(tag, 2);
        head.Children.Add(tag);
        stack.Children.Add(head);

        // the numbers: Combat Power in big with it against the own, and the item level
        var numbers = new Grid();
        numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var power = new StackPanel();
        power.Children.Add(Caption("COMBAT POWER"));
        power.Children.Add(Big(card.CombatPower is int cp ? cp.ToString("N0", CultureInfo.CurrentCulture) : "?", OverlaySkin.Gold));
        if (card.Against is double against)
        {
            bool stronger = against > 0.005;
            bool weaker = against < -0.005;
            string text = stronger ? $"▲ {against:P0} above you" : weaker ? $"▼ {-against:P0} below you" : "about as strong as you";
            power.Children.Add(new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(stronger ? Enemy : weaker ? OverlaySkin.Good : Muted) });
        }

        numbers.Children.Add(power);
        var items = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        items.Children.Add(Caption("ITEM LEVEL"));
        items.Children.Add(Big(card.ItemLevel is int il ? il.ToString("N0", CultureInfo.CurrentCulture) : "?", Color.FromRgb(0xE9, 0xEE, 0xF2)));
        Grid.SetColumn(items, 1);
        numbers.Children.Add(items);
        stack.Children.Add(numbers);
        return OverlaySkin.Panel(stack, new Thickness(12, 10, 12, 10));
    }

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x87, 0x97)) };

    private static TextBlock Big(string text, Color colour)
    {
        var t = new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colour) };
        t.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        return t;
    }
}

/// <summary>Runs the enemy card: a few times a second it reads the selection, and shows the card while the game is in front and an enemy player is selected
/// (a sample while it is placed).</summary>
public sealed class EnemyCardController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Func<Aion2EntityDirectory?> _directory;
    private EnemyCardWindow? _overlay;
    private MeterSettings _settings = MeterSettings.Load();
    private DateTime _settingsAt = DateTime.UtcNow;
    private int? _ownPower;
    private DateTime _ownPowerAt = DateTime.MinValue;

    public EnemyCardController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
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

        if (!_settings.ShowEnemyCard)
        {
            _overlay?.ShowOverlay(false);
            return;
        }

        bool locked = _settings.EnemyCardLocked;
        Aion2EntityDirectory? dir = _directory();
        if (dir is not null && DateTime.UtcNow - _ownPowerAt > TimeSpan.FromSeconds(10))
        {
            _ownPowerAt = DateTime.UtcNow;
            _ownPower = MainWindow.OwnCombatPower(dir);
        }

        EnemyCardData? card = dir is null ? null : EnemyCardData.Of(dir, _settings.EnemyCardAllies, _ownPower);
        if (card is null && !locked)
        {
            card = EnemyCardData.SampleCard(_ownPower);
        }

        if (card is null && _overlay is null)
        {
            return; // the window is made the first time there is something to show
        }

        _overlay ??= new EnemyCardWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(card);
        _overlay.ShowOverlay(card is not null && GameWindow.OverlayMayShow());
    }
}
