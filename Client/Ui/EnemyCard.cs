using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>Who the selected player is, as the game showed him: name, class, level, server, legion, Combat Power and item level, and whether he is an enemy.</summary>
public sealed record EnemyCardData(string Name, string? ClassName, int? Level, string? Server, string? Legion, int? CombatPower, int? ItemLevel, bool Enemy, int? OwnPower,
    bool Sample = false, int ServerId = 0, bool Looking = false, Rival? Rival = null)
{
    /// <summary>Whether something the card shows is not known yet (it is then looked up).</summary>
    public bool Incomplete => CombatPower is null || ItemLevel is null || ClassName is null || Level is null;

    /// <summary>His Combat Power against the own: +0.12 is 12 % more (null when one of them is not known).</summary>
    public double? Against => CombatPower is int theirs && OwnPower is int mine && mine > 0 ? (double)theirs / mine - 1 : null;

    /// <summary>The card of the player the local player has selected, when it is a player (an enemy only unless <paramref name="allies"/>); null otherwise.</summary>
    public static EnemyCardData? Of(Aion2EntityDirectory dir, bool allies, int? ownPower)
    {
        if (dir.OwnTarget is not { } target || !dir.IsPlayer(target.EntityId) || target.EntityId == dir.LocalPlayerId)
        {
            return null;
        }

        int id = target.EntityId;
        string? theirs = dir.FactionOf(id);
        string? mine = OwnFaction(dir);
        // a player who traded hits with the own character is an enemy whatever is known of his faction (11 November 2026: in the Abyss most had no server)
        // a member of the own group is never an enemy (11 November 2026)
        bool enemy = !(dir.NameFor(id) is { } named && dir.PartyNames.Contains(named)) && (dir.IsHostile(id) || (theirs is not null && mine is not null && theirs != mine));
        if (!enemy && !allies)
        {
            return null;
        }

        Aion2SeenProfile? profile = dir.SeenProfileOf(id);
        string? className = profile?.ClassId is int classId ? Aion2SkillNames.ClassFromCode(4 * classId + 1) : dir.ClassOf(id);
        string? server = dir.ServerIdOf(id) is int sid && Aion2Servers.IsKnown(sid) ? Aion2Servers.NameOf(sid) : null;
        int? power = profile?.CombatPower is int cp and > 0 and not 397228 ? cp : null; // 397228: the fixed number of the 10 against 10 arena
        string name = dir.NameFor(id) ?? "Unknown player";
        int serverId = dir.ServerIdOf(id) ?? 0;
        return new EnemyCardData(name, className, profile?.Level, server, dir.GuildOf(id), power, profile?.ItemLevel is > 0 ? profile.ItemLevel : null,
            enemy, ownPower, ServerId: serverId, Rival: Aion2Pvp.RivalOf(name, serverId));
    }

    /// <summary>The own faction: that of the own server, else the other side of a player in view who traded hits with the own character; null when not known.</summary>
    public static string? OwnFaction(Aion2EntityDirectory dir)
    {
        string? mine = dir.LocalPlayerId >= 0 ? dir.FactionOf(dir.LocalPlayerId) : null;
        mine ??= dir.LocalCharacter is { ServerId: > 0 } me ? (me.ServerId / 1000 == 1 ? "Elyos" : me.ServerId / 1000 == 2 ? "Asmodian" : null) : null;
        if (mine is null)
        {
            foreach ((int other, _) in dir.PlayersInView())
            {
                if (dir.IsHostile(other) && dir.FactionOf(other) is { } theirs)
                {
                    return theirs == "Elyos" ? "Asmodian" : "Elyos";
                }
            }
        }

        return mine;
    }

    /// <summary>What the card shows while it is placed with nobody selected.</summary>
    public static EnemyCardData SampleCard(int? ownPower) => new("Kraeger", "Templar", 45, "Nezekan", "Iron Oath", 231_400, 3_120, true, ownPower ?? 206_600, Sample: true,
        Rival: new Rival { Name = "Kraeger", Met = 4, Kills = 2, Deaths = 2, KilledBy = 1 });
}

/// <summary>
/// The enemy card (10 November 2026, the owner: "when you tab to a player in the Abyss, an enemy, it shows their class, their CP, their item level"): the
/// player the local player selected (the game's own selection message, see <see cref="Aion2EntityDirectory.OwnTarget"/>), with what his "player appeared"
/// frame told - class, level, Combat Power, item level - his server and legion, and his Combat Power against the own; what the frame left out (or a frame
/// the meter never saw) is looked up on the website and the character's official page. Enemies only, unless allies are switched on too. Placed, scaled
/// and locked like the other overlays; over the game only.
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
        OverlaySkin.SolidBackground(this, OverlayPlaces.EnemyCard);
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
        string key = card is null ? "" : string.Join("|", card.Name, card.ClassName, card.Level, card.Server, card.Legion, card.CombatPower, card.ItemLevel, card.Enemy, card.OwnPower, card.Sample,
            card.Looking, card.Rival?.Met, card.Rival?.Kills, card.Rival?.Deaths);
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
        TextBlock powerText = Big(card.CombatPower is int cp ? PlayerRow.Compact(cp) : card.Looking ? "…" : "?", OverlaySkin.Gold);
        // short like the meter's own CP (231.4K); the exact figure on hover
        powerText.ToolTip = card.CombatPower is int exact ? exact.ToString("N0", CultureInfo.CurrentCulture) : null;
        power.Children.Add(powerText);
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
        items.Children.Add(Big(card.ItemLevel is int il ? il.ToString("N0", CultureInfo.CurrentCulture) : card.Looking ? "…" : "?", Color.FromRgb(0xE9, 0xEE, 0xF2)));
        if (card.Looking && (card.CombatPower is null || card.ItemLevel is null))
        {
            items.Children.Add(new TextBlock { Text = "looking it up…", FontSize = 10.5, Foreground = new SolidColorBrush(Muted) });
        }
        Grid.SetColumn(items, 1);
        numbers.Children.Add(items);
        stack.Children.Add(numbers);

        // the record against him (the rivals): how often fought, the kills and the deaths
        if (card.Rival is { Met: > 0 } r)
        {
            stack.Children.Add(OverlaySkin.Divider(7, 5));
            var record = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            record.Inlines.Add(new System.Windows.Documents.Run(r.Met == 1 ? "Fought once" : $"Fought {r.Met}×") { Foreground = new SolidColorBrush(Muted) });
            record.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = new SolidColorBrush(Muted) });
            record.Inlines.Add(new System.Windows.Documents.Run($"{r.Kills} {(r.Kills == 1 ? "kill" : "kills")}") { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(OverlaySkin.Good) });
            record.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = new SolidColorBrush(Muted) });
            record.Inlines.Add(new System.Windows.Documents.Run($"{r.Deaths} {(r.Deaths == 1 ? "death" : "deaths")}") { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Enemy) });
            if (r.KilledBy > 0)
            {
                record.Inlines.Add(new System.Windows.Documents.Run(r.KilledBy == 1 ? "  ·  killed you once" : $"  ·  killed you {r.KilledBy}×") { Foreground = new SolidColorBrush(Muted) });
            }

            stack.Children.Add(record);
        }

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

    // What the website and the official pages told of players the game's frame told too little of (by "name|server"), and the lookups going on.
    private readonly Dictionary<string, (FoundPlayer? Found, int? CombatPower, int? ItemLevel, DateTime At)> _looked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _looking = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan LookedKept = TimeSpan.FromMinutes(20), NotFoundRetry = TimeSpan.FromMinutes(2);

    public EnemyCardController(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    /// <summary>
    /// What a lookup found of a player the game's frame told too little of, null while none did; a lookup is started when none was made yet (or the last is old),
    /// a few at a time - the radar asks for the enemies in view this way, the card for the one selected.
    /// </summary>
    public (int? Power, int? Items)? Lookup(string name, int serverId, int ownServer, bool enemy)
    {
        string key = name + "|" + serverId.ToString(CultureInfo.InvariantCulture);
        _looked.TryGetValue(key, out var got);
        bool complete = got.CombatPower is not null && got.ItemLevel is not null;
        TimeSpan age = got.At == default ? TimeSpan.MaxValue : DateTime.UtcNow - got.At;
        if ((got.At == default || age > LookedKept || (!complete && age > NotFoundRetry)) && name.Length > 0 && name != "Unknown player" && _looking.Count < 3
            && _looking.Add(key))
        {
            _ = LookUpAsync(key, name, serverId, ownServer, enemy);
        }

        return got.CombatPower is not null || got.ItemLevel is not null ? (got.CombatPower, got.ItemLevel) : null;
    }

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
            if (_overlay is not null)
            {
                OverlaySkin.SolidBackground(_overlay, OverlayPlaces.EnemyCard);
            }
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

        // never in a dungeon or raid (only its sample while it is being placed)
        EnemyCardData? card = dir is null || Aion2.Protocol.Aion2Zones.IsPveHere(dir.ZoneId, dir.LiveMobs) ? null : EnemyCardData.Of(dir, _settings.EnemyCardAllies, _ownPower);
        if (card is { Incomplete: true } && dir is not null)
        {
            card = Complete(card, dir);
        }

        if (card is null && !locked)
        {
            card = EnemyCardData.SampleCard(_ownPower);
        }

        if (card is null && _overlay is null)
        {
            return; // the window is made the first time there is something to show
        }

        // a game window the screen reader thinks open does not hide it (11 November 2026: it took the Abyss's own bars for one, for two minutes of a fight)
        bool mayShow = GameWindow.OverlayMayShow(ignoreMenu: true);
        if (dir?.OwnTarget is { } target && dir.IsPlayer(target.EntityId) && target.EntityId != dir.LocalPlayerId)
        {
            string who = dir.NameFor(target.EntityId) ?? "a player";
            string state = card is null || card.Sample ? $"{who} selected: not taken for an enemy (faction {dir.FactionOf(target.EntityId) ?? "unknown"}, own {EnemyCardData.OwnFaction(dir) ?? "unknown"})"
                : mayShow ? $"{who} shown" : $"{who} hidden: the game is not the window in front";
            PvpLog.Note("card", state, state);
        }

        _overlay ??= new EnemyCardWindow();
        _overlay.ApplyLockedIfChanged(locked);
        _overlay.Render(card);
        _overlay.ShowOverlay(card is not null && mayShow);
    }

    /// <summary>
    /// The figures the game's frame left out (10 November 2026, the owner: "load them no matter what"): a player whose "player appeared" frame was not seen (the
    /// meter started while he was already in view) has no Combat Power or item level from the game. Then the website's search (what it knows of the character),
    /// and for what it doesn't know the character's official page through the website - the card shows "…" meanwhile. Kept 20 minutes; a player not found is
    /// looked for again after two.
    /// </summary>
    private EnemyCardData Complete(EnemyCardData card, Aion2EntityDirectory dir)
    {
        int ownServer = dir.LocalCharacter?.ServerId ?? 0;
        string key = card.Name + "|" + card.ServerId.ToString(CultureInfo.InvariantCulture);
        if (_looked.TryGetValue(key, out var got))
        {
            card = card with
            {
                CombatPower = card.CombatPower ?? got.CombatPower, ItemLevel = card.ItemLevel ?? got.ItemLevel, ClassName = card.ClassName ?? got.Found?.ClassName,
                Level = card.Level ?? got.Found?.Level, Server = card.Server ?? (got.Found is { Server.Length: > 0 } f ? f.Server : null),
            };
        }

        TimeSpan age = got.At == default ? TimeSpan.MaxValue : DateTime.UtcNow - got.At;
        bool stale = got.At == default || age > LookedKept || (card.Incomplete && age > NotFoundRetry);
        // a few at a time (the website's searches are limited: tabbing through a crowd asks for the ones looked at longest)
        if (card.Incomplete && stale && card.Name != "Unknown player" && _looking.Count < 3 && _looking.Add(key))
        {
            _ = LookUpAsync(key, card.Name, card.ServerId, ownServer, card.Enemy);
        }

        return _looking.Contains(key) ? card with { Looking = true } : card;
    }

    private async Task LookUpAsync(string key, string name, int server, int ownServer, bool enemy)
    {
        try
        {
            // the server when the game named it, else the own region (the Abyss and the arenas match within a region), the other side when it is an enemy
            string region = FindPlayerClient.RegionOf(server > 0 ? server : ownServer);
            FindAnswer answer = await FindPlayerClient.SearchAsync(name, region);
            bool Fits(FoundPlayer p) => server > 0 ? p.ServerId == server : ownServer <= 0 || (p.ServerId / 1000 != ownServer / 1000) == enemy;
            FoundPlayer? found = answer.Players.FirstOrDefault(p => p.Name == name && Fits(p))
                ?? answer.Players.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && Fits(p));
            int? power = found?.CombatPower, items = found?.ItemLevel;
            if (power is null || items is null)
            {
                // the official page: the website asks it for any character, a player of the meter or not
                FoundPlayer page = found ?? new FoundPlayer(name, null, null, "", server, region, null, null, null, null, null);
                if (page.ServerId > 0)
                {
                    (int? pagePower, int? pageItems) = await FindPlayerClient.FiguresAsync(page);
                    power ??= pagePower;
                    items ??= pageItems;
                }
            }

            _looked[key] = (found, power, items, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _looked[key] = (null, null, null, DateTime.UtcNow);
        }
        finally
        {
            _looking.Remove(key);
        }
    }
}
