using System.Windows;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The meter's Grind view (10 November 2026, the owner: "you switch to grind on the meter while farming mobs: it shows how much Kinah, how much Abyss Points
/// you farm and calculates how much you're going to get per hour"). The scope switch has "Grind" after "Stagger": in place of the lines, the grind going on
/// (<see cref="Aion2Grind"/>) - Kinah and Abyss Points an hour in big, what came in this session under them, the kills and the loot (with its worth at the
/// market) - and in the header the Kinah an hour, the map and the time farmed. Per hour is the session's average, so a big kill lifts it at once and it
/// settles again over the minutes after. Any other scope or a measure picked in the menu brings the lines back; the choice is kept from one start to the next.
/// </summary>
public partial class MainWindow
{
    /// <summary>The Grind view is on.</summary>
    private bool _overlayGrind;

    private long _grindAt;
    private string _grindShown = "";

    /// <summary>The Grind switch: on, the view in place of the lines; off, the lines.</summary>
    private void SetGrindView(bool on, bool save = true)
    {
        _overlayGrind = on;
        _grindShown = "";
        _grindAt = 0;
        if (on)
        {
            _overlayBuffs = false;
            _dummyCardHidden = _dummyHeader;
            if (_overlayStagger)
            {
                SetStaggerView(false);
            }
        }

        if (save)
        {
            MeterSettings s = MeterSettings.Load();
            if (s.MeterGrindView != on)
            {
                s.MeterGrindView = on;
                s.Save();
            }
        }

        ApplyGrindView();
        RefreshRows();
        UpdateOverlayGrind();
        UpdateOverlayFold();
    }

    /// <summary>The lines or the Grind view, and the switch marking it.</summary>
    private void ApplyGrindView()
    {
        CompactOverlayRowsArea.Visibility = _overlayGrind || _overlayStagger ? Visibility.Collapsed : Visibility.Visible;
        OverlayGrindCard.Visibility = _overlayGrind ? Visibility.Visible : Visibility.Collapsed;
        UpdateScopeControls();
    }

    /// <summary>Once a second while the view is on: the numbers again (the time farmed moves, so the hourly numbers do too).</summary>
    private void UpdateOverlayGrind()
    {
        if (Headless || !_overlayGrind)
        {
            return; // the pictures fill it themselves
        }

        long tick = Environment.TickCount64;
        if (tick - _grindAt < 1000)
        {
            return;
        }

        _grindAt = tick;
        MarketBook? book = Aion2OwnServerId() is int server ? ExchangePrices.For(server) : null;
        ShowGrindView(Aion2Grind.Current, DateTime.Now, book);
    }

    /// <summary>The Grind view with the session as given (the pictures call it too).</summary>
    internal void ShowGrindView(GrindSession? session, DateTime now, MarketBook? book)
    {
        if (!_overlayGrind)
        {
            _overlayGrind = true;
            ApplyGrindView();
        }

        GrindFigures? f = session is null ? null : GrindFigures.Of(session, now, book);
        string key = f is null ? "none" : string.Join("|", session!.Id, session.Kinah, session.AbyssPoints, session.Kills, f.Items, f.LootWorth, f.FarmedText, f.Over,
            f.KinahPerHour is double k ? GrindView.Short(k) : "", f.ApPerHour is double a ? GrindView.Short(a) : "", f.KillsPerHour is double kh ? Math.Round(kh) : -1,
            f.LootPerHour is double lh ? GrindView.Short(lh) : "");
        if (key != _grindShown || OverlayGrindCard.Visibility != Visibility.Visible)
        {
            _grindShown = key;
            OverlayGrindCard.Child = GrindView.MeterView(f, Headless ? null : () =>
            {
                Aion2Grind.NewSession();
                _grindAt = 0;
                UpdateOverlayGrind();
            });
            OverlayGrindCard.Visibility = Visibility.Visible;
        }

        OverlayBarLegend.Visibility = Visibility.Collapsed; // the bar's colours belong to the lines
        OverlayTotalRateText.Text = f?.KinahPerHour is double perHour ? GrindView.Short(perHour) : "–";
        OverlayTotalUnitText.Text = "Kinah/h";
        OverlayTargetText.Text = session is { Zone: not 0 } s ? "Grind · " + LootText.MapName(s.Zone) : "Grind";
        OverlayTimeText.Text = f?.FarmedText ?? "";
    }
}
