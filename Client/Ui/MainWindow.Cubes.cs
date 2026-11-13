using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The cube finder: the game announces a Hidden Cube once the player comes within about 100 m of it (Aion2EntityDirectory.LiveCubes). Its parts:
/// a line on the meter while one is near (here), the parking-sensor sound and the map brought up for it (both in PetMapController), and the
/// Tools page "Cube finder" that holds their switches and says what is in range now.
/// </summary>
public partial class MainWindow
{
    private long _cubeCalloutAt;

    /// <summary>Runs the cube line ten times a second while it is shown (the meter's own refresh comes only four times a second).</summary>
    private System.Windows.Threading.DispatcherTimer? _cubeLineTimer;

    /// <summary>Ten times a second while a cube is in range: "Hidden Cube nearby · 42 m" on the meter (Settings: the cube finder's line). It used to
    /// change only every 0.4 s, so the metres trailed the map's arrow.</summary>
    private void UpdateOverlayCubeCallout()
    {
        if (Headless || (!_compactOverlay && !MeterOverlayActive))
        {
            _cubeLineTimer?.Stop();
            return;
        }

        long tick = Environment.TickCount64;
        if (tick - _cubeCalloutAt < 90)
        {
            return;
        }

        _cubeCalloutAt = tick;
        if (!_gateSettings.CubeOverlayAlert || !_gateSettings.CubeAlertsNow() || PrivacyConsent.Restricted || _source?.Entities is not Aion2EntityDirectory directory
            || directory.LiveCubes() is not { Count: > 0 } cubes)
        {
            OverlayCubeCallout.Visibility = Visibility.Collapsed;
            _cubeLineTimer?.Stop();
            return;
        }

        ShowCubeChip(OverlayCubeCallout, OverlayCubeCalloutText, cubes.Count, NearestCubeText(directory, cubes));
        OverlayCubeCallout.Visibility = Visibility.Visible;
        if (_cubeLineTimer is null)
        {
            _cubeLineTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _cubeLineTimer.Tick += (_, _) => UpdateOverlayCubeCallout();
        }

        if (!_cubeLineTimer.IsEnabled)
        {
            _cubeLineTimer.Start();
        }
    }

    /// <summary>The distance to the nearest cube from the map's arrow (<see cref="CubeRange"/>): "42 m" right after the game reported the player's place,
    /// "~42 m" from the minimap in between; null while the place is not known.</summary>
    private static string? NearestCubeText(Aion2EntityDirectory directory, IReadOnlyList<(float X, float Y)> cubes) =>
        CubeRange.Nearest(directory, cubes) is { } near ? CubeRange.Text(near) : null;

    /// <summary>The cube chip in the meter's bar (5 November 2026: the bar stays when the rest folds away, so the cube is there in the open world): the
    /// metres to the nearest, "×2" while the place is not known with two in range; the whole line ("Hidden Cube nearby · 42 m") as its tip.</summary>
    internal static void ShowCubeChip(FrameworkElement chip, TextBlock text, int count, string? distance)
    {
        var line = new TextBlock();
        ShowCubeCallout(line, count, distance);
        chip.ToolTip = line.Text;
        text.Text = distance is not null ? (count > 1 ? distance + "  ×" + count.ToString(CultureInfo.CurrentCulture) : distance) : "×" + count.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>The meter's cube line: how many are in range and how far the nearest is (also drawn by the overlay pictures).</summary>
    internal static void ShowCubeCallout(TextBlock text, int count, string? distance)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string what = count > 1 ? string.Format(CultureInfo.CurrentCulture, loc["Cubes.LineMany"], count) : loc["Cubes.LineOne"];
        text.Text = (distance is not null ? what + " · " + distance : what) + (CubeKeysShort() is { } keys ? " · " + keys : "");
    }

    /// <summary>"3 keys", "1 key" for the cube lines (the meter's, the cube note); null while the bag of the character in game is not known.</summary>
    internal static string? CubeKeysShort()
    {
        if (Aion2Inventories.CubeKeys(Aion2Inventories.PlayingName()) is not { } keys)
        {
            return null;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        return keys.Bag == 1 ? loc["Cubes.KeyShort"] : string.Format(CultureInfo.CurrentCulture, loc["Cubes.KeysShort"], keys.Bag);
    }

    /// <summary>
    /// The Hidden Cube keys of the character in game (or most likely in game), for the Cube finder page: how many in the bag (and in storage), and when there are
    /// none in the bag - or the bag is not known yet - with the alerts set to need a key, that they stay quiet. Null while no character is known and the
    /// alerts do not need a key.
    /// </summary>
    private static (string Text, bool None)? CubeKeysLine(bool needsKey)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string? name = Aion2Inventories.PlayingName();
        if (name is null)
        {
            return needsKey ? (loc["Cubes.KeysNoCharacter"], true) : null;
        }

        if (Aion2Inventories.CubeKeys(name) is not { } keys)
        {
            return (string.Format(CultureInfo.CurrentCulture, loc[needsKey ? "Cubes.KeysUnknownQuiet" : "Cubes.KeysUnknown"], name), needsKey);
        }

        string text = keys.Bag == 0 ? string.Format(CultureInfo.CurrentCulture, loc["Cubes.KeysNone"], name)
            : keys.Bag == 1 ? string.Format(CultureInfo.CurrentCulture, loc["Cubes.KeysOne"], name)
            : string.Format(CultureInfo.CurrentCulture, loc["Cubes.KeysMany"], name, keys.Bag);
        if (keys.Storage > 0)
        {
            text += string.Format(CultureInfo.CurrentCulture, loc[keys.Bag == 0 ? "Cubes.KeysStorageOnly" : "Cubes.KeysStorage"], keys.Storage);
        }

        if (keys.Bag == 0)
        {
            text += " " + loc[needsKey ? "Cubes.KeysQuiet" : "Cubes.KeysLoud"];
        }

        return (text, keys.Bag == 0);
    }

    /// <summary>Tools → Cube finder: what the game announces now, and the switches of the line, the sound and the map.</summary>
    private ToolPage BuildCubesPage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var column = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        column.Children.Add(PageTitle(loc["Cubes.Title"], loc["Cubes.Lede"]));

        // what is in range now
        var nowLine = ShellText("", "Brush.Text", 20, bold: true);
        var nowSub = Wrapped("", "Brush.TextSubtle", 12);
        nowSub.Margin = new Thickness(0, 4, 0, 0);
        var nowStack = new StackPanel();
        var nowHead = new StackPanel { Orientation = Orientation.Horizontal };
        var cube = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M9,1 L16.5,5 L16.5,13 L9,17 L1.5,13 L1.5,5 Z M1.5,5 L9,9 L16.5,5 M9,9 L9,17"),
            Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        nowHead.Children.Add(cube);
        nowHead.Children.Add(nowLine);
        nowStack.Children.Add(nowHead);
        nowStack.Children.Add(nowSub);
        // the character's keys (9 November 2026, the owner: "show how many keys the char has")
        var keysLine = Wrapped("", "Brush.Text", 13);
        keysLine.Margin = new Thickness(0, 10, 0, 0);
        nowStack.Children.Add(keysLine);
        void ShowKeys()
        {
            (string Text, bool None)? keys = CubeKeysLine(MeterSettings.Load().CubeNeedsKey);
            keysLine.Visibility = keys is null ? Visibility.Collapsed : Visibility.Visible;
            keysLine.Text = keys is { } k ? "🔑 " + k.Text : "";
            keysLine.SetResourceReference(TextBlock.ForegroundProperty, keys is { None: true } ? "Brush.Warning" : "Brush.Text");
        }
        column.Children.Add(HomeCard(loc["Cubes.Now"], nowStack, null, 0));

        void ShowNow()
        {
            ShowKeys();
            if (PrivacyConsent.Restricted || _source?.Entities is not Aion2EntityDirectory directory)
            {
                nowLine.Text = loc["Cubes.NoGame"];
                nowSub.Text = loc["Cubes.NoGameHow"];
                return;
            }

            var cubes = directory.LiveCubes();
            string? distance = NearestCubeText(directory, cubes);
            nowLine.Text = cubes.Count == 0 ? loc["Cubes.None"]
                : (cubes.Count > 1 ? string.Format(CultureInfo.CurrentCulture, loc["Cubes.LineMany"], cubes.Count) : loc["Cubes.LineOne"])
                  + (distance is not null ? " · " + distance : "");
            int seen = Math.Max(0, directory.CubeArrivals);
            nowSub.Text = string.Format(CultureInfo.CurrentCulture, loc["Cubes.Seen"], seen);
        }

        if (Headless)
        {
            nowLine.Text = loc["Cubes.LineOne"] + " · " + string.Format(CultureInfo.CurrentCulture, loc["Cubes.Metres"], 42);
            nowSub.Text = string.Format(CultureInfo.CurrentCulture, loc["Cubes.Seen"], 3);
            keysLine.Text = "🔑 " + string.Format(CultureInfo.CurrentCulture, loc["Cubes.KeysMany"], "Seraphyne", 3);
        }
        else
        {
            ShowNow();
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => ShowNow();
        if (!Headless)
        {
            timer.Start();
        }

        // the switches: each change is saved and applied at once
        MeterSettings s0 = MeterSettings.Load();
        var switches = new StackPanel();
        void Save(Action<MeterSettings> change)
        {
            MeterSettings s = MeterSettings.Load();
            change(s);
            s.Save();
            ApplySettingsEverywhere(s);
            _gateSettingsAt = DateTime.MinValue; // the meter's line follows at once
        }

        CheckBox Switch(string title, string tip, bool on, Action<MeterSettings, bool> set)
        {
            var text = new StackPanel();
            text.Children.Add(ShellText(title, "Brush.Text", 13.5, bold: true));
            TextBlock small = Wrapped(tip, "Brush.TextMuted", 12);
            small.Margin = new Thickness(0, 2, 0, 0);
            small.MaxWidth = 700; // the check box gives its content all the room it asks for: the text wraps by itself only below this
            text.Children.Add(small);
            var box = new CheckBox { IsChecked = on, Content = text, Margin = new Thickness(0, 0, 0, 12), VerticalContentAlignment = VerticalAlignment.Top };
            box.Click += (_, _) => Save(s => set(s, box.IsChecked == true));
            switches.Children.Add(box);
            return box;
        }

        Switch(loc["Cubes.Line"], loc["Cubes.LineTip"], s0.CubeOverlayAlert, (s, v) => s.CubeOverlayAlert = v);
        Switch(loc["Cubes.Popup"], loc["Cubes.PopupTip"], s0.CubePopup, (s, v) => s.CubePopup = v);
        CheckBox sound = Switch(loc["Cubes.Chime"], loc["Cubes.ChimeTip"], s0.CubeArrivalSound, (s, v) => s.CubeArrivalSound = v);
        Switch(loc["Cubes.Beeps"], loc["Cubes.BeepsTip"], s0.CubeBeeps, (s, v) => s.CubeBeeps = v);
        Switch(loc["Cubes.OnlyKey"], loc["Cubes.OnlyKeyTip"], s0.CubeNeedsKey, (s, v) => s.CubeNeedsKey = v);
        var volumeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, -4, 0, 14) };
        volumeRow.Children.Add(ShellText(loc["Cubes.Volume"], "Brush.TextMuted", 12));
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(s0.CubeSoundVolume, 0, 100), Width = 180, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 5 };
        var volumeSave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        volumeSave.Tick += (_, _) =>
        {
            volumeSave.Stop();
            Save(s => s.CubeSoundVolume = (int)volume.Value);
        };
        volume.ValueChanged += (_, _) =>
        {
            volumeSave.Stop();
            volumeSave.Start();
        };
        volumeRow.Children.Add(volume);
        var test = new Button { Content = loc["Cubes.Test"], Padding = new Thickness(12, 3, 12, 3), VerticalAlignment = VerticalAlignment.Center };
        test.Click += (_, _) => NotifySounds.PlayCubeArrival((int)volume.Value);
        volumeRow.Children.Add(test);
        switches.Children.Insert(switches.Children.IndexOf(sound) + 1, volumeRow);
        // two cards (3 November 2026, cleaner): what tells you a cube is near, and what the map does; Switch adds to the panel `switches` holds now
        StackPanel alerts = switches;
        switches = new StackPanel();
        Switch(loc["Cubes.Map"], loc["Cubes.MapTip"], s0.ShowMapOnCube, (s, v) => s.ShowMapOnCube = v);
        Switch(loc["Cubes.Mark"], loc["Cubes.MarkTip"], s0.PetMapGatherItems.Contains("Hidden Cube"), (s, v) =>
        {
            s.PetMapGatherItems.Remove("Hidden Cube");
            if (v)
            {
                s.PetMapGatherItems.Add("Hidden Cube");
            }
        });
        column.Children.Add(HomeCard(loc["Cubes.Alerts"], alerts, null, 0));
        column.Children.Add(HomeCard(loc["Cubes.OnMap"], switches, null, 0));

        var more = new StackPanel { Orientation = Orientation.Horizontal };
        more.Children.Add(ShellLink(loc["Cubes.OpenMap"], () => ShowShellPage("petmap")));
        column.Children.Add(more);
        TextBlock note = Wrapped(loc["Cubes.Note"], "Brush.TextSubtle", 11.5);
        note.Margin = new Thickness(0, 14, 0, 0);
        column.Children.Add(note);

        return new ToolPage
        {
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = column },
            Cleanup = () =>
            {
                timer.Stop();
                if (volumeSave.IsEnabled)
                {
                    volumeSave.Stop();
                    Save(s => s.CubeSoundVolume = (int)volume.Value);
                }
            },
        };
    }
}
