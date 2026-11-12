using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2.Capture;

namespace AionDPS.Ui;

/// <summary>The lines the network overlay can show (the ids are what <see cref="MeterSettings.NetworkOverlayHidden"/> keeps) and how they read.</summary>
public static class NetworkOverlayRows
{
    public static readonly string[] Ids = { "server", "ping", "jitter", "traffic", "loss", "stalls", "gateway", "internet", "connection", "load" };

    internal static readonly Color Good = OverlaySkin.Good;
    internal static readonly Color Warn = OverlaySkin.Warn;
    internal static readonly Color Bad = OverlaySkin.Bad;
    private static readonly Color Neutral = Color.FromRgb(0xDD, 0xE6, 0xEC);

    /// <summary>Values as they look in a game session (the measurements of a real session: a ping around 40 ms on the EU server, no loss), for a view or a frame to place without a connection.</summary>
    public static NetworkSnapshot Sample() => new(true, "203.0.113.10:13328", TimeSpan.FromMinutes(47), 1, 0, 38, 41, 33, 62, 4.2, 12.4, 1.8, 41, 160, 0, 0, TimeSpan.FromMilliseconds(120),
        0, 0, 0, 0, null, null, null, new NetworkProbeSnapshot(true, "192.168.1.1", 1, 0, 60, 14, 0, 60));

    private static string Num(double value, string format = "0.#") => value.ToString(format, CultureInfo.CurrentCulture);

    private static Color PingColor(double ms) => ms < 60 ? Good : ms < 120 ? Warn : Bad;

    /// <summary>The lines to show now: label, value and the colour of the value.</summary>
    public static List<(string Label, string Value, Color Color)> Of(NetworkSnapshot s, ICollection<string> hidden)
    {
        var loc = LocalizationManager.Instance;
        var rows = new List<(string, string, Color)>();
        bool Shown(string id) => !hidden.Contains(id);
        string Label(string id) => loc["Network.Row." + id];

        if (Shown("server"))
        {
            // never the address: the overlay is on stream, and an address on screen is an invitation to flood it
            rows.Add(s.Server is null ? (Label("server"), loc["Network.Waiting"], Neutral)
                : (Label("server"), loc[s.Relayed ? "Network.Server.Relayed" : "Network.Server.Hidden"], Good));
        }

        if (!s.Connected)
        {
            if (!Shown("server"))
            {
                rows.Add((Label("connection"), loc["Network.Waiting"], Warn));
            }

            return rows;
        }

        // through a ping reducer or VPN on this PC, ping, jitter and loss are those of the last hop to it, nothing at all (NetworkStats): said, not shown as 0
        if (Shown("ping"))
        {
            rows.Add(s.Relayed ? (Label("ping"), loc["Network.Ping.Relayed"], Neutral)
                : s.PingMs is { } ping
                ? (Label("ping"), $"{Num(ping, "0")} ms  ({string.Format(loc["Network.Ping.Detail"], Num(s.PingAverageMs ?? ping, "0"), Num(s.PingMinMs ?? ping, "0"), Num(s.PingMaxMs ?? ping, "0"))})", PingColor(ping))
                : (Label("ping"), "…", Neutral));
        }

        if (Shown("jitter"))
        {
            rows.Add(s.Relayed ? (Label("jitter"), loc["Network.Relayed.Hidden"], Neutral)
                : s.JitterMs is { } jitter
                ? (Label("jitter"), $"{Num(jitter)} ms", jitter < 10 ? Good : jitter < 30 ? Warn : Bad)
                : (Label("jitter"), "…", Neutral));
        }

        if (Shown("traffic"))
        {
            rows.Add((Label("traffic"), $"↓ {Num(s.DownKBps)}  ↑ {Num(s.UpKBps)} kB/s  ({string.Format(loc["Network.Traffic.Peak"], Num(s.DownPeakKBps, "0"))})", Neutral));
        }

        if (Shown("loss"))
        {
            int bad = s.Retransmissions + s.Gaps;
            rows.Add(s.Relayed ? (Label("loss"), loc["Network.Relayed.Hidden"], Neutral)
                : (Label("loss"), string.Format(loc["Network.Loss.Value"], s.Retransmissions, s.Gaps), bad == 0 ? Good : bad <= 5 ? Warn : Bad));
        }

        if (Shown("stalls"))
        {
            if (s.StallCount == 0)
            {
                rows.Add((Label("stalls"), loc["Network.Stalls.None"], Good));
            }
            else
            {
                string cause = s.LastStallVerdict is { } v ? " · " + loc["Network.Stalls.Cause." + v] : "";
                bool recent = s.SinceLastStall is { } ago && ago.TotalSeconds < 120;
                rows.Add((Label("stalls"), string.Format(loc["Network.Stalls.Value"], s.StallCount, Num(s.LongestStallSeconds), WorldBossText.Duration(s.SinceLastStall ?? TimeSpan.Zero)) + cause, recent ? Bad : Warn));
            }
        }

        if (Shown("gateway"))
        {
            rows.Add(ProbeRow(Label("gateway"), s.Probe.Running && s.Probe.Gateway is not null, s.Probe.GatewayMs, s.Probe.GatewayLost, s.Probe.GatewayProbes));
        }

        if (Shown("internet"))
        {
            rows.Add(ProbeRow(Label("internet"), s.Probe.Running, s.Probe.InternetMs, s.Probe.InternetLost, s.Probe.InternetProbes));
        }

        if (Shown("connection"))
        {
            bool stalled = s.SinceLastData is { } since && since.TotalSeconds >= 2;
            string text = stalled
                ? string.Format(loc["Network.Connection.Stalled"], (int)s.SinceLastData!.Value.TotalSeconds)
                : string.Format(loc["Network.Connection.Online"], WorldBossText.Duration(s.Uptime ?? TimeSpan.Zero));
            if (s.Switches > 0)
            {
                text += " · " + string.Format(loc["Network.Connection.Switches"], s.Switches);
            }

            if (s.Reconnects > 0)
            {
                text += " · " + string.Format(loc["Network.Connection.Reconnects"], s.Reconnects);
            }

            if (s.UnackedFor is { } unacked && unacked.TotalSeconds >= 2)
            {
                text += " · " + string.Format(loc["Network.Connection.Unacked"], (int)unacked.TotalSeconds);
            }

            // a reconnect is a warning for ten minutes (like a stall), then just a number of the session
            bool recentReconnect = s.SinceLastReconnect is { } ago && ago.TotalMinutes < 10;
            rows.Add((Label("connection"), text, stalled ? Bad : recentReconnect ? Warn : Good));
        }

        if (Shown("load"))
        {
            rows.Add((Label("load"), string.Format(loc["Network.Load.Value"], s.Queued, s.Dropped), s.Dropped > 0 ? Bad : s.Queued > 1000 ? Warn : Good));
        }

        return rows;

        (string, string, Color) ProbeRow(string label, bool measured, double? ms, int lost, int probes)
        {
            if (!measured || probes == 0)
            {
                return (label, "…", Neutral);
            }

            string text = string.Format(loc["Network.Probe.Value"], ms is { } m ? Num(m, "0") : "–", lost, probes);
            return (label, text, lost == 0 && (ms ?? 0) < 30 ? Good : lost <= 2 ? Warn : Bad);
        }
    }
}

/// <summary>
/// The network overlay: ping, jitter, traffic, packet loss and connection of the game connection, measured from the traffic the capture already sees.
/// Placed, scaled and locked like the world boss overlay (<see cref="MovableOverlay"/>); <see cref="NetworkOverlayController"/> shows it only while the game is in front.
/// </summary>
public sealed class NetworkOverlayWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 280, MinHeight = 40 };

    public NetworkOverlayWindow() : base("Aion DPS Network", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Left + 40, 520, LoadSize, SaveSize, OverlayEdgeMode.Uniform)
    {
        Body = OverlaySkin.Panel(_rows, new Thickness(10, 9, 10, 8));
        // with Alt held over it (the game shows the cursor) the small overlay opens to every line
        _hover.Tick += (_, _) =>
        {
            bool open = IsVisible && AltHeld() && NativeCursor.Showing() && Under(this);
            if (open != _altOpen)
            {
                _altOpen = open;
                if (_last is { } last)
                {
                    RenderLive(last.Rows, last.Snapshot, last.Compact);
                }
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _hover.Start();
            }
            else
            {
                _hover.Stop();
            }
        };
        Closed += (_, _) => _hover.Stop();
    }

    private readonly System.Windows.Threading.DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Queue<double> _pings = new();
    private bool _altOpen;
    private (IReadOnlyList<(string Label, string Value, Color Color)> Rows, NetworkSnapshot Snapshot, bool Compact)? _last;
    private DateTime _pingAt = DateTime.MinValue;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private static bool AltHeld() => (GetAsyncKeyState(0x12) & 0x8000) != 0;

    private static bool Under(Window window)
    {
        if (!NativeCursor.TryGet(out int x, out int y) || window.Content is not FrameworkElement content || PresentationSource.FromVisual(content) is null)
        {
            return false;
        }

        try
        {
            Point p = content.PointFromScreen(new Point(x, y));
            return p.X >= 0 && p.Y >= 0 && p.X <= content.ActualWidth && p.Y <= content.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The overlay over the game (10 November 2026, the owner: "way nicer"): small by default - a dot in the colour of the worst line, the ping big, a little graph
    /// of the last minute's ping and the word for how it goes - and every line while Alt is held over it (or always, with the small one switched off).
    /// </summary>
    public void RenderLive(IReadOnlyList<(string Label, string Value, Color Color)> rows, NetworkSnapshot snapshot, bool compact)
    {
        _last = (rows, snapshot, compact);
        if (DateTime.UtcNow - _pingAt >= TimeSpan.FromSeconds(0.9) && snapshot.PingMs is double ping)
        {
            _pingAt = DateTime.UtcNow;
            _pings.Enqueue(ping);
            while (_pings.Count > 60)
            {
                _pings.Dequeue();
            }
        }

        if (!compact || _altOpen)
        {
            Render(rows);
            return;
        }

        _rows.Children.Clear();
        int worst = rows.Count == 0 ? 0 : rows.Max(r => Severity(r.Color));
        Color colour = worst == 2 ? NetworkOverlayRows.Bad : worst == 1 ? NetworkOverlayRows.Warn : NetworkOverlayRows.Good;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(colour), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock
        {
            Text = snapshot.Connected && snapshot.PingMs is double p ? p.ToString("0", CultureInfo.CurrentCulture) : "–", FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(snapshot.PingMs is double pc ? (pc < 60 ? NetworkOverlayRows.Good : pc < 120 ? NetworkOverlayRows.Warn : NetworkOverlayRows.Bad) : Color.FromRgb(0x9A, 0x9A, 0xA4)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        line.Children.Add(value);
        line.Children.Add(new TextBlock { Text = "ms", FontSize = 10, Margin = new Thickness(3, 5, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(OverlaySkin.Gold) });
        line.Children.Add(Sparkline(_pings.ToArray(), colour, 76, 20));
        var word = new TextBlock
        {
            Text = worst == 2 ? "Trouble" : worst == 1 ? "Unsteady" : "All good", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(colour),
        };
        line.Children.Add(word);
        _rows.Children.Add(line);
        // through a ping reducer the ping can't be seen (NetworkStats): the dash says why
        var hint = new TextBlock { Text = snapshot.Relayed ? "Via a ping reducer: see its window · Alt for every line" : "Hold Alt over it for every line", FontSize = 9,
            Margin = new Thickness(16, 2, 0, 0), Opacity = 0.75 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _rows.Children.Add(hint);
    }

    /// <summary>A minute of ping as if measured (the pictures).</summary>
    internal void SeedPings(IEnumerable<double> pings)
    {
        foreach (double ping in pings)
        {
            _pings.Enqueue(ping);
        }

        _pingAt = DateTime.UtcNow;
    }

    /// <summary>The ping of the last minute as a thin line over a soft fill, scaled between its lowest and highest (at least 20 ms apart).</summary>
    private static FrameworkElement Sparkline(double[] pings, Color colour, double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        if (pings.Length < 2)
        {
            canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = width, Y1 = height - 1, Y2 = height - 1, Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
            return canvas;
        }

        double low = pings.Min(), high = Math.Max(pings.Max(), low + 20);
        var points = new PointCollection();
        for (int i = 0; i < pings.Length; i++)
        {
            double x = width * i / (pings.Length - 1);
            double y = height - 2 - (height - 4) * (pings[i] - low) / (high - low);
            points.Add(new Point(x, y));
        }

        var fill = new PointCollection(points) { new Point(width, height), new Point(0, height) };
        canvas.Children.Add(new System.Windows.Shapes.Polygon
        {
            Points = fill, Fill = new LinearGradientBrush(Color.FromArgb(0x55, colour.R, colour.G, colour.B), Color.FromArgb(0x00, colour.R, colour.G, colour.B), 90),
        });
        canvas.Children.Add(new System.Windows.Shapes.Polyline { Points = points, Stroke = new SolidColorBrush(colour), StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round });
        return canvas;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.NetworkOverlayWidth, s.NetworkOverlayHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.NetworkOverlayWidth = width;
        s.NetworkOverlayHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.NetworkOverlayLeft, s.NetworkOverlayTop, s.NetworkOverlayScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.NetworkOverlayLeft = left;
        s.NetworkOverlayTop = top;
        s.NetworkOverlayScale = scale;
        s.Save();
    }

    /// <summary>
    /// The overlay in the meter's look (10 November 2026): a heading with a dot and a word for the worst line (all good, unsteady, trouble), then the lines, the
    /// name muted and the value in its colour, every other one on a faint band.
    /// </summary>
    public void Render(IReadOnlyList<(string Label, string Value, Color Color)> rows)
    {
        _rows.Children.Clear();
        int worst = rows.Count == 0 ? 0 : rows.Max(r => Severity(r.Color));
        (string word, Color colour) = worst switch
        {
            2 => ("Trouble", NetworkOverlayRows.Bad),
            1 => ("Unsteady", NetworkOverlayRows.Warn),
            _ => ("All good", NetworkOverlayRows.Good),
        };
        _rows.Children.Add(OverlaySkin.Heading(LocalizationManager.Instance["Settings.Nav.Network"], rows.Count > 0 ? word : null, colour, rows.Count > 0 ? colour : null));
        bool band = false;
        foreach (var (label, value, color) in rows)
        {
            _rows.Children.Add(OverlaySkin.Row(label, value, color, band));
            band = !band;
        }
    }

    private static int Severity(Color c) => c == NetworkOverlayRows.Bad ? 2 : c == NetworkOverlayRows.Warn ? 1 : 0;
}

/// <summary>Runs the network overlay: once a second it reads the settings and the measurements, and shows the overlay only while the game is in front
/// (or while the overlays are unlocked so it can be placed).</summary>
public sealed class NetworkOverlayController : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private NetworkOverlayWindow? _overlay;
    private readonly List<NetworkOverlayWindow> _views = new();

    /// <summary>A second, never shown copy of the overlay whose content a tool window shows live (see <see cref="MovableOverlay.DetachBody"/>).</summary>
    public NetworkOverlayWindow CreateView()
    {
        var view = new NetworkOverlayWindow();
        _views.Add(view);
        var settings = MeterSettings.Load();
        var snapshot = NetworkStats.Snapshot();
        view.Render(NetworkOverlayRows.Of(!snapshot.Connected && !settings.NetworkOverlayLocked ? NetworkOverlayRows.Sample() : snapshot, settings.NetworkOverlayHidden));
        return view;
    }

    public void RemoveView(NetworkOverlayWindow view)
    {
        _views.Remove(view);
        view.Close();
    }

    public NetworkOverlayController()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void RaiseToFront() => _overlay?.RaiseToFront();

    public void Dispose()
    {
        _timer.Stop();
        NetworkProbe.Stop();
        _overlay?.Close();
    }

    private MeterSettings? _tickSettings;
    private (DateTime WrittenUtc, long Length) _tickSettingsStamp;

    /// <summary>The settings for a tick: read again only when the file was written since the last tick (read-only use, never saved back).</summary>
    private MeterSettings TickSettings()
    {
        var stamp = MeterSettings.FileStamp();
        if (_tickSettings is null || stamp.Length < 0 || stamp != _tickSettingsStamp)
        {
            _tickSettings = MeterSettings.Load();
            _tickSettingsStamp = stamp;
        }

        return _tickSettings;
    }

    private void Tick()
    {
        var settings = TickSettings();
        var snapshot = NetworkStats.Snapshot();
        if (_views.Count > 0)
        {
            NetworkProbe.Start();
            var viewRows = NetworkOverlayRows.Of(!snapshot.Connected && !settings.NetworkOverlayLocked ? NetworkOverlayRows.Sample() : snapshot, settings.NetworkOverlayHidden);
            foreach (var view in _views)
            {
                view.Render(viewRows);
            }
        }

        if (!settings.ShowNetworkOverlay)
        {
            _overlay?.ShowOverlay(false);
            if (_views.Count == 0)
            {
                NetworkProbe.Stop();
            }

            return;
        }

        NetworkProbe.Start();
        bool locked = settings.NetworkOverlayLocked;
        _overlay ??= new NetworkOverlayWindow();
        _overlay.ApplyLockedIfChanged(locked);
        if (!GameWindow.OverlayMayShow())
        {
            _overlay.ShowOverlay(false);
            return;
        }

        // while it is positioned and nothing is connected, sample values fill it in its real size; small (ping only) unless switched off or Alt is held over it
        NetworkSnapshot shown = !snapshot.Connected && !locked ? NetworkOverlayRows.Sample() : snapshot;
        _overlay.RenderLive(NetworkOverlayRows.Of(shown, settings.NetworkOverlayHidden), shown, settings.NetworkOverlayCompact && locked);
        _overlay.ShowOverlay(true);
    }
}
