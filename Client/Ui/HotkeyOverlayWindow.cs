using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The hotkey overlay: a movable, transparent list of the hotkeys the player picked in the settings (name, then the key combination).
/// Placed, scaled and locked like the pet map's list (<see cref="MovableOverlay"/>); MainWindow shows it only while the game is in front.
/// </summary>
public sealed class HotkeyOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 200, MinHeight = 40 };
    private string _shown = "";

    public HotkeyOverlayWindow() : base("Aion DPS Hotkeys", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 120, LoadSize, SaveSize, OverlayEdgeMode.Uniform)
    {
        Body = OverlaySkin.Panel(_rows, new Thickness(10, 9, 10, 8));
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.HotkeyOverlayWidth, s.HotkeyOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.HotkeyOverlayWidth = width;
        s.HotkeyOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.HotkeyOverlayLeft, s.HotkeyOverlayTop, s.HotkeyOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.HotkeyOverlayLeft = left;
        s.HotkeyOverlayTop = top;
        s.HotkeyOverlayScale = scale;
        s.Save();
    }

    /// <summary>The hotkeys the settings list, as (name, key combination, why it does nothing if it doesn't); only the ones whose switch is on and that
    /// have a key. A heading is a row without keys.</summary>
    public static List<(string Name, string Keys, HotkeyProblem? Problem)> RowsOf(MeterSettings s, IReadOnlyDictionary<HotkeyAction, HotkeyProblem>? problems = null)
    {
        var loc = LocalizationManager.Instance;
        var sections = new (string TitleKey, string[] Ids)[]
        {
            ("Settings.Hotkeys.Group.Dps", new[] { "HideUi", "Pause", "CopyDamage", "Clear", "UploadBoss", "Mode" }),
            ("Settings.Hotkeys.Group.Overlays", new[] { "AllOverlays", "MoveOverlays", "HotkeyOverlay" }),
            ("Settings.Hotkeys.Group.Modules", new[] { "Loot", "Craft", "PetMap", "PetArrows", "PetMapZoomIn", "PetMapZoomOut", "GatherArrows", "Combat", "Assist", "Timetable", "WorldBoss", "Network" }),
        };

        // three alike sections, each a heading (a row without keys) and one flat row per hotkey; a section without a hotkey is left out
        var result = new List<(string Name, string Keys, HotkeyProblem? Problem)>();
        foreach (var (titleKey, ids) in sections)
        {
            var shown = ids.Where(id => !s.HotkeyOverlayHidden.Contains(id)).Select(HotkeyTable.OfId).OfType<HotkeyTable.Entry>()
                .Select(h => (Name: loc[h.NameKey], Keys: HotkeyBinding.Parse(h.Get(s)).ToString(),
                    Problem: problems is not null && problems.TryGetValue(h.Action, out HotkeyProblem p) ? p : (HotkeyProblem?)null))
                .Where(h => h.Keys.Length > 0)
                .ToList();
            if (shown.Count > 0)
            {
                result.Add((loc[titleKey], "", null));
                result.AddRange(shown);
            }
        }

        return result;
    }

    public void Render(IReadOnlyList<(string Name, string Keys, HotkeyProblem? Problem)> rows)
    {
        string key = string.Join("\n", rows.Select(r => r.Name + "|" + r.Keys + "|" + r.Problem));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        var loc = LocalizationManager.Instance;
        // the meter's look (10 November 2026): each group under a small gold heading, its hotkeys as names and keycaps, every other line on a faint band; a hotkey
        // that does nothing says so in red (another program has it, or another of the meter's hotkeys: Settings > Hotkeys to pick another)
        bool band = false;
        foreach (var (name, keys, problem) in rows)
        {
            if (keys.Length == 0)
            {
                if (_rows.Children.Count > 0)
                {
                    _rows.Children.Add(OverlaySkin.Divider(4, 4));
                }

                _rows.Children.Add(OverlaySkin.Heading(name));
                band = false;
                continue;
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = name, FontSize = 11.5, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            n.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
            grid.Children.Add(n);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(OverlaySkin.Keys(keys, problem is null ? null : OverlaySkin.Bad));
            if (problem is { } why)
            {
                right.Children.Add(new TextBlock
                {
                    Text = loc[why == HotkeyProblem.Twice ? "Hotkeys.Problem.TwiceShort" : "Hotkeys.Problem.TakenShort"], FontSize = 10.5, Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(OverlaySkin.Bad),
                });
            }

            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            _rows.Children.Add(new Border
            {
                Child = grid, Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(5),
                Background = band ? new SolidColorBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
            });
            band = !band;
        }
    }
}
