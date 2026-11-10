using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>The meter's fight as a share card (10 November 2026): the menu's "Copy this fight as an image" puts the lines shown, by damage, on a clean card for Discord.</summary>
public partial class MainWindow
{
    private void ShareMeterFight()
    {
        List<PlayerRow> rows = _rows.Where(r => !r.IsEnemy && r.Damage > 0).OrderByDescending(r => r.Damage).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        string? own = (_liveSource?.Entities as Aion2EntityDirectory)?.LocalCharacter?.Name;
        long all = rows.Sum(r => r.Damage);
        var lines = rows.Select(r => new ShareLine(r.Name, r.ClassName is { Length: > 0 } c ? c : null, r.Dps ?? 0, r.Damage, all > 0 ? (double)r.Damage / all : 0,
            own is not null && r.Name == own)).ToList();
        TimeSpan length = _lastShownHits.Count > 1 ? _lastShownHits.Max(h => h.Timestamp) - _lastShownHits.Min(h => h.Timestamp) : TimeSpan.Zero;
        string title = OverlayTargetText.Text is { Length: > 0 } shown ? shown : "Fight";
        var card = ShareCard.Fight(_overlayBossShown ? "BOSS FIGHT" : "FIGHT", title, DateTime.Now.ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            length, lines);
        bool copied = ShareCard.Copy(card, title, out _);
        CubeToast.Note(copied ? "Copied as an image: paste it in Discord with Ctrl+V" : "The clipboard is busy: try again", 4, anywhere: true);
    }
}
