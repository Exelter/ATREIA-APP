using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// What a character's page shares with all its cards: each card folds by its title and stays folded from one start to the next (10 November 2026, a tester: "let
/// me fold sections and keep in memory which i folded"), the names a player knows the counters by, and short times ("−1 at 08:00"). (The board of all characters
/// side by side that came with them went again on 11 November 2026, the owner: "go back to old design and keep the single character design".)
/// </summary>
public sealed partial class ChecklistWindow
{
    // ---- folding ----

    private static bool IsFolded(string id) => MeterSettings.Load().ChecklistFolded.Contains(id);

    private void ToggleFold(string id)
    {
        MeterSettings s = MeterSettings.Load();
        if (!s.ChecklistFolded.Remove(id))
        {
            s.ChecklistFolded.Add(id);
        }

        s.Save();
        Render();
    }

    /// <summary>The chevron of a part that folds: down while it is open, right while it is folded.</summary>
    private static TextBlock Chevron(bool folded)
    {
        TextBlock glyph = Label(folded ? "\uE76C" : "\uE70D", "Brush.TextMuted", 9);
        glyph.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        glyph.Margin = new Thickness(0, 1, 8, 0);
        return glyph;
    }

    /// <summary>
    /// A card of a character's counters that folds by its title (a chevron at its right), kept folded from one start to the next: folded, only the title stays
    /// (with what it carries on the right, like the Odyle Energy's sum).
    /// </summary>
    private UIElement? FoldCard(UIElement? card, string id)
    {
        if (card is not Border { Child: StackPanel stack } || stack.Children.Count == 0 || stack.Children[0] is not FrameworkElement title)
        {
            return card;
        }

        bool folded = IsFolded(id);
        stack.Children.RemoveAt(0);
        if (folded)
        {
            stack.Children.Clear();
        }

        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = folded ? new Thickness(0) : title.Margin,
            ToolTip = LocalizationManager.Instance[folded ? "Checklist.Unfold" : "Checklist.Fold"] };
        title.Margin = new Thickness(0);
        TextBlock chevron = Chevron(folded);
        chevron.Margin = new Thickness(12, 0, 0, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chevron, Dock.Right);
        head.Children.Add(chevron);
        head.Children.Add(title);
        head.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleFold(id);
        };
        stack.Children.Insert(0, head);
        return card;
    }

    /// <summary>The names a player knows them by (English; the game's own words in the other languages): the daily dungeons said to be daily dungeons, Ludra at her
    /// raid, no "[Sanctuary]" or "Entry Count" around them (a tester: "what even are those 2?").</summary>
    private static readonly Dictionary<int, string> BoardNames = new()
    {
        [1] = "Shugo Festival keys", [8] = "Dimensional Invasion keys", [4] = "Nightmare", [102] = "Abandoned Balaur Fortress", [103] = "Fafnium Laboratory",
        [101] = "Unknown Fissure", [80000001] = "Ascension Trial", [70000001] = "Subjugation", [90000001] = "Ludra · Abyssal Forge", [90000003] = "Corroded Decontamination Facility",
        [90000005] = "Chalice of Muspel", [60000006] = "Conquest rewards", [60000002] = "Transcendence rewards", [60015101] = "Ordeal boss kills", [60000010] = "Cubes not yet taken",
        [3] = "Lower Abyss access", [9] = "Middle Abyss access", [13] = "Upper Abyss access",
    };

    private static string BoardName(TicketDef def, string language) =>
        language == "en" && BoardNames.TryGetValue(def.Id, out string? name) ? name : TicketLabel(def, language);

    /// <summary>When, as short as a cell needs it: the time within a day, else the day and the time (<paramref name="dayOnly"/>: the day alone, the counters refill
    /// at the reset's time anyway).</summary>
    private static string When(DateTime at, DateTime now, CultureInfo culture, bool dayOnly = false) =>
        at - now < TimeSpan.FromDays(1) ? at.ToString("t", culture) : dayOnly ? at.ToString("ddd", culture) : at.ToString("ddd", culture) + " " + at.ToString("t", culture);
}
