using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The page of all characters as one board (10 November 2026, the owner: "do this for the character page which shows all useful details including ludra entry etc
/// and warnings for 13/14 tickets nightmare etc (ticket wasted)"; a tester: "have a checklist like this so i can quick glance which alt has what", and "remember
/// some people have 8 chars"): the counters as rows in sections that fold (kept folded from one start to the next), every character a column, each cell the count
/// of its cap with a bar and what comes next ("full Sun 08:00"); red where the next refill is lost because the counter is full, amber where the week's tickets
/// go unused at the reset. The counters the server's characters share are one cell across them all. The season missions and the inventory are tabs of their own.
/// </summary>
public sealed partial class ChecklistWindow
{
    private enum Tone
    {
        Plain,
        Good,
        Warn,
        Lost,
        Muted,
    }

    /// <summary>One cell: the count (bold), something on top of it ("+55" charged), the cap ("/840"), how full the bar is, a small line under it, and what tells it.</summary>
    private sealed record BoardCell(string Main, string? Extra, string? Cap, double? Fill, string? Sub, Tone Tone, string? Tip = null);

    /// <summary>A row: its name and what refills it ("+2 a day"), a cell per character, or one cell for all of them (the server's shared counters).</summary>
    private sealed record BoardRow(string Name, string? Hint, string? Tip, Func<TicketCharacter, BoardCell>? Each, BoardCell? Shared = null);

    private sealed record BoardSection(string Id, string Title, string? When, IReadOnlyList<BoardRow> Rows);

    // the row names' column; a character's column is at least this wide (eight characters in the app's width), the rest is shared out
    private const double BoardNameWidth = 190, BoardColumnMin = 92;

    // ---- the tabs of the page of all characters ----

    /// <summary>The page of all characters: every character side by side, their season missions, what they hold.</summary>
    private UIElement RosterTabs(string tab)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return Segments(new[] { ("board", loc["Checklist.Tab.Board"]), ("season", loc["Checklist.Season"]), ("inventory", loc["Checklist.Tab.Inventory"]) }, tab, chosen =>
        {
            MeterSettings s = MeterSettings.Load();
            s.ChecklistRosterTab = chosen;
            s.Save();
            Render();
        });
    }

    /// <summary>A row of tabs in one rounded box, the chosen one raised (the look of the character's own tabs).</summary>
    private static UIElement Segments(IEnumerable<(string Id, string Caption)> tabs, string active, Action<string> choose)
    {
        var segments = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(3), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 10) };
        segments.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        segments.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((string id, string caption) in tabs)
        {
            bool on = id == active;
            var pill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 4, 16, 5), Child = Label(caption, on ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: true) };
            if (on)
            {
                pill.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
            }
            else
            {
                string chosen = id;
                pill.Background = Brushes.Transparent;
                pill.Cursor = Cursors.Hand;
                pill.MouseEnter += (_, _) => pill.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                pill.MouseLeave += (_, _) => pill.Background = Brushes.Transparent;
                pill.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    choose(chosen);
                };
            }

            line.Children.Add(pill);
        }

        segments.Child = line;
        return segments;
    }

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

    // ---- the board ----

    private UIElement Board(IReadOnlyList<TicketCharacter> all, IReadOnlyDictionary<string, Aion2Tickets.OdyleState?> odyle, IReadOnlyDictionary<string, long> inItems,
        string? online, DateTime now, DateTime utcNow, string language)
    {
        IReadOnlyList<BoardSection> sections = BoardSections(all, odyle, inItems, now, utcNow, language);
        int columns = all.Count;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BoardNameWidth) });
        for (int i = 0; i < columns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = BoardColumnMin });
        }

        int row = 0;
        void NewRow() => grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement element, int column, int span = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            grid.Children.Add(element);
        }

        // the characters on top, each a click away from its own page
        NewRow();
        var corner = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        corner.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        Put(corner, 0);
        for (int i = 0; i < columns; i++)
        {
            Put(BoardHead(all[i], all[i].Name == online, now), i + 1);
        }

        row++;
        foreach (BoardSection section in sections)
        {
            bool folded = IsFolded("board." + section.Id);
            NewRow();
            Put(BoardSectionHead(section, folded), 0, columns + 1);
            row++;
            if (folded)
            {
                continue;
            }

            int stripe = 0;
            foreach (BoardRow r in section.Rows)
            {
                NewRow();
                // every other line a shade lighter, to follow a line across (a tester: "every other lane should have a bit of a highlight for readability")
                if (stripe++ % 2 == 1)
                {
                    var shade = new Border { Opacity = 0.45, IsHitTestVisible = false };
                    shade.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                    Put(shade, 0, columns + 1);
                }

                Put(BoardRowName(r), 0);
                if (r.Shared is { } shared)
                {
                    Put(BoardCellView(shared, wide: true), 1, columns);
                }
                else if (r.Each is { } each)
                {
                    for (int i = 0; i < columns; i++)
                    {
                        Put(BoardCellView(each(all[i]), wide: false), i + 1);
                    }
                }

                row++;
            }
        }

        // wider than the page (many characters on a narrow window): it scrolls sideways, the wheel still scrolls the page
        // (a scroll viewer gives its content all the width it wants: the grid is given the viewer's width, so its columns share it out, never less than they need)
        double least = BoardNameWidth + columns * BoardColumnMin;
        grid.Width = least;
        var sideways = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
        sideways.SizeChanged += (_, e) => grid.Width = Math.Max(least, e.NewSize.Width);
        sideways.PreviewMouseWheel += (sender, e) =>
        {
            if (e.Handled || sender is not ScrollViewer viewer || viewer.Parent is not UIElement up)
            {
                return;
            }

            e.Handled = true;
            up.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
        };
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(0, 4, 0, 6), Margin = new Thickness(0, 0, 0, 12),
            Child = sideways, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    /// <summary>A character's head of its column: emblem and name in its class colour, "Playing" or when it was seen; a green line under the one logged in.</summary>
    private UIElement BoardHead(TicketCharacter c, bool online, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        var stack = new StackPanel { Margin = new Thickness(10, 8, 8, 8) };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
        {
            top.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        TextBlock name = Label(c.Name, "Brush.Text", 12.5, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        if (className is not null)
        {
            name.Foreground = ClassBrushes.Text(className);
        }

        top.Children.Add(name);
        stack.Children.Add(top);
        string seen = online ? loc["Checklist.Board.Playing"] : string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterSeen"], AgoText(now - c.SavedAt.ToLocalTime()));
        TextBlock sub = Label(seen, online ? "Brush.Heal" : "Brush.TextSubtle", 10.5);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        sub.Margin = new Thickness(className is not null ? 24 : 0, 1, 0, 0);
        stack.Children.Add(sub);

        var head = new Border { Child = stack, BorderThickness = new Thickness(0, 0, 0, online ? 2 : 1), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = c.Name + (Strength(c, loc) is { } strength ? "  ·  " + strength : "") };
        if (online)
        {
            head.SetResourceReference(Border.BorderBrushProperty, "Brush.Heal");
        }
        else
        {
            head.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }

        head.MouseEnter += (_, _) => head.Background = Tint(accent, 0x16);
        head.MouseLeave += (_, _) => head.Background = Brushes.Transparent;
        string picked = c.Name;
        head.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCharacter(picked);
        };
        return head;
    }

    /// <summary>A section's line: its chevron and title in capitals, when it resets on the right; a click folds it.</summary>
    private UIElement BoardSectionHead(BoardSection section, bool folded)
    {
        var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 5) };
        if (section.When is { Length: > 0 } when)
        {
            TextBlock right = Label(when, "Brush.TextSubtle", 10.5);
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Chevron(folded));
        left.Children.Add(Label(section.Title.ToUpper(LocalizationManager.Instance.Culture), "Brush.TextMuted", 10.5, bold: true));
        dock.Children.Add(left);
        var head = new Border { Child = dock, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = LocalizationManager.Instance[folded ? "Checklist.Unfold" : "Checklist.Fold"] };
        string id = "board." + section.Id;
        head.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleFold(id);
        };
        return head;
    }

    private static UIElement BoardRowName(BoardRow r)
    {
        var stack = new StackPanel { Margin = new Thickness(14, 8, 8, 8), VerticalAlignment = VerticalAlignment.Center, ToolTip = r.Tip ?? r.Name };
        TextBlock name = Label(r.Name, "Brush.Text", 12.5, bold: true);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(name);
        if (r.Hint is { Length: > 0 } hint)
        {
            TextBlock small = Label(hint, "Brush.TextSubtle", 10.5);
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            small.Margin = new Thickness(0, 1, 0, 0);
            stack.Children.Add(small);
        }

        return stack;
    }

    /// <summary>A cell: the count in the colour of what it means, what is on top of it and the cap, a thin bar, and the line under it; a red or amber wash where
    /// something is lost or about to go unused.</summary>
    private static UIElement BoardCellView(BoardCell cell, bool wide)
    {
        string brush = cell.Tone switch
        {
            Tone.Good => "Brush.Heal",
            Tone.Warn => "Brush.Warning",
            Tone.Lost => "Brush.Danger",
            Tone.Muted => "Brush.TextSubtle",
            _ => "Brush.Text",
        };
        var stack = new StackPanel { Margin = new Thickness(10, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
        var line = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var main = new System.Windows.Documents.Run(cell.Main) { FontSize = 13.5, FontWeight = FontWeights.SemiBold };
        main.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brush);
        line.Inlines.Add(main);
        if (cell.Extra is { Length: > 0 } extra)
        {
            var plus = new System.Windows.Documents.Run(" " + extra) { FontSize = 11, FontWeight = FontWeights.SemiBold };
            plus.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Accent");
            line.Inlines.Add(plus);
        }

        if (cell.Cap is { Length: > 0 } cap)
        {
            var of = new System.Windows.Documents.Run(" " + cap) { FontSize = 11 };
            of.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextSubtle");
            line.Inlines.Add(of);
        }

        stack.Children.Add(line);
        if (cell.Fill is double fill)
        {
            var track = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 1), MaxWidth = wide ? double.PositiveInfinity : 150, HorizontalAlignment = HorizontalAlignment.Stretch };
            var under = new Border { CornerRadius = new CornerRadius(2) };
            under.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            track.Children.Add(under);
            var bar = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
            bar.SetResourceReference(Border.BackgroundProperty, cell.Tone switch
            {
                Tone.Lost => "Brush.Danger",
                Tone.Warn => "Brush.Warning",
                Tone.Good => "Brush.Heal",
                Tone.Muted => "Brush.TextSubtle",
                _ => "Brush.Accent",
            });
            double share = Math.Clamp(fill, 0, 1);
            track.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * share;
            track.Children.Add(bar);
            stack.Children.Add(track);
        }

        if (cell.Sub is { Length: > 0 } sub)
        {
            TextBlock small = Label(sub, cell.Tone is Tone.Lost or Tone.Warn ? brush : "Brush.TextSubtle", 10.5, bold: cell.Tone is Tone.Lost or Tone.Warn);
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            small.Margin = new Thickness(0, 1, 0, 0);
            stack.Children.Add(small);
        }

        var box = new Border { Child = stack, ToolTip = cell.Tip, Background = Brushes.Transparent };
        if (cell.Tone is Tone.Lost or Tone.Warn)
        {
            box.Background = Tint(Resource(cell.Tone == Tone.Lost ? "Brush.Danger" : "Brush.Warning", cell.Tone == Tone.Lost ? Color.FromRgb(0xE5, 0x5B, 0x5B) : Color.FromRgb(0xF0, 0xB7, 0x1C)),
                cell.Tone == Tone.Lost ? (byte)0x2E : (byte)0x1E);
        }

        return box;
    }

    // ---- what is on the board ----

    private IReadOnlyList<BoardSection> BoardSections(IReadOnlyList<TicketCharacter> all, IReadOnlyDictionary<string, Aion2Tickets.OdyleState?> odyle,
        IReadOnlyDictionary<string, long> inItems, DateTime now, DateTime utcNow, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        CultureInfo culture = loc.Culture;
        var sections = new List<BoardSection>();
        bool Seen(int id) => all.Any(c => c.Tickets.ContainsKey(id) || Aion2Tickets.IsLocked(c, id));
        DateTime nextDaily = ChecklistManual.LastDailyReset(now).AddDays(1), nextWeekly = ChecklistManual.LastWeeklyReset(now).AddDays(7);
        string Resets(DateTime next) => string.Format(culture, loc["Checklist.Board.Resets"], (next - now > TimeSpan.FromDays(1) ? next.ToString("ddd", culture) + " " : "") + next.ToString("t", culture),
            SpanText(next - now));

        // the energy: the basic energy against its cap with the charged on top, then all of it together with the runs it makes
        var energy = new List<BoardRow>();
        if (Seen(Aion2Tickets.OdyleId))
        {
            string odyleName = Aion2Tickets.Find(Aion2Tickets.OdyleId)?.NameIn(language) ?? "Odyle Energy";
            energy.Add(new BoardRow(odyleName, loc["Checklist.Board.OdyleHint"], null, c => OdyleCell(c, odyle.GetValueOrDefault(c.Name), utcNow, culture)));
            int per = OdyleClaim;
            energy.Add(new BoardRow(loc["Checklist.Board.Total"], loc["Checklist.Board.TotalHint"], null, c =>
            {
                if (odyle.GetValueOrDefault(c.Name) is not { } o)
                {
                    return Unknown(c, Aion2Tickets.OdyleId);
                }

                long items = inItems.GetValueOrDefault(c.Name), total = o.Count + o.Bonus + items;
                return new BoardCell(total.ToString("N0", CultureInfo.CurrentCulture), null, null, null, string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.Runs"], total / per, per),
                    total >= per ? Tone.Plain : Tone.Muted,
                    string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.TotalTip"], o.Count.ToString("N0", CultureInfo.CurrentCulture), o.Bonus.ToString("N0", CultureInfo.CurrentCulture),
                        items.ToString("N0", CultureInfo.CurrentCulture)));
            }));
            sections.Add(new BoardSection("energy", loc["Checklist.Board.Energy"], null, energy));
        }

        // today: the daily tasks, Nightmare and the two daily dungeons
        var daily = new List<BoardRow>();
        if (all.Any(c => c.Tickets.ContainsKey(Aion2Tickets.MissionsId)))
        {
            daily.Add(new BoardRow(loc["Checklist.RosterTasks"], string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.OfDay"], Aion2Tickets.MissionsPerDay), loc["Checklist.RosterTasksTip"],
                c => TasksCell(c, now)));
        }

        foreach (int id in new[] { 4, 102, 103 })
        {
            if (Aion2Tickets.Find(id) is { } def && Seen(id))
            {
                daily.Add(new BoardRow(BoardName(def, language), BoardHint(def, language), def.NameIn(language), c => TicketCell(c, def, now, culture)));
            }
        }

        if (daily.Count > 0)
        {
            sections.Add(new BoardSection("daily", loc["Checklist.Board.Daily"], Resets(nextDaily), daily));
        }

        // the counters the characters of a server share (the keys, the daily dungeon's week): one cell across them all, as the character seen last has them
        var shared = new List<BoardRow>();
        foreach (int id in new[] { 1, 8, DailyDungeonTicket })
        {
            if (Aion2Tickets.Find(id) is { } def && Seen(id))
            {
                TicketCharacter? latest = all.Where(c => c.Tickets.ContainsKey(id)).OrderByDescending(c => c.SavedAt).FirstOrDefault();
                BoardCell cell = latest is null ? new BoardCell("–", null, null, null, null, Tone.Muted, loc["Checklist.RosterUnknown"]) : TicketCell(latest, def, now, culture);
                shared.Add(new BoardRow(BoardName(def, language), BoardHint(def, language), def.NameIn(language), null, cell));
            }
        }

        if (shared.Count > 0)
        {
            sections.Add(new BoardSection("server", loc["Checklist.Board.Shared"], null, shared));
        }

        // this week: the trials and raids (Ludra's Abyssal Forge with its boss), the dungeons' rewards, the cubes not yet taken
        var weekly = new List<BoardRow>();
        foreach (int id in new[] { 80000001, 70000001 })
        {
            if (Aion2Tickets.Find(id) is { } def && Seen(id))
            {
                weekly.Add(new BoardRow(BoardName(def, language), BoardHint(def, language), def.NameIn(language), c => TicketCell(c, def, now, culture)));
            }
        }

        foreach ((int attempts, int boss) in new[] { (90000001, 90000002), (90000003, 90000004), (90000005, 90000006) })
        {
            if (Aion2Tickets.Find(attempts) is { } def && Seen(attempts))
            {
                TicketDef? bossDef = Aion2Tickets.Find(boss);
                weekly.Add(new BoardRow(BoardName(def, language), BoardHint(def, language), def.NameIn(language), c => RaidCell(c, def, bossDef, now, culture)));
            }
        }

        foreach (int id in new[] { 60000006, 60000002, 60015101, 60000010 })
        {
            if (Aion2Tickets.Find(id) is { } def && Seen(id))
            {
                weekly.Add(new BoardRow(BoardName(def, language), BoardHint(def, language), def.NameIn(language), c => TicketCell(c, def, now, culture)));
            }
        }

        if (weekly.Count > 0)
        {
            sections.Add(new BoardSection("weekly", loc["Checklist.Board.Weekly"], Resets(nextWeekly), weekly));
        }

        // the Abyss: the corridors open to the side since the last siege, the hours of access left
        var abyss = new List<BoardRow>();
        if (Aion2Corridors.WindowAt(now) is { } corridors)
        {
            abyss.Add(new BoardRow(Aion2.Protocol.Aion2Zones.NameOf(Aion2Corridors.ZoneId, language) ?? loc["Checklist.Corridor"], loc["Checklist.Board.CorridorHint"], loc["Checklist.CorridorTip"],
                c => CorridorCell(c, all, corridors)));
        }

        foreach (int id in TimeTickets.Where(id => id != 6))
        {
            if (Aion2Tickets.Find(id) is { } def && all.Any(c => c.Tickets.ContainsKey(id)))
            {
                abyss.Add(new BoardRow(BoardName(def, language), null, def.NameIn(language), c => TimeCell(c, def)));
            }
        }

        if (abyss.Count > 0)
        {
            sections.Add(new BoardSection("abyss", loc["Checklist.Board.Abyss"], null, abyss));
        }

        return sections;
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

    /// <summary>What refills a counter, under its name: "daily dungeon · +1 a day", "+2 a day", "3 a week".</summary>
    private static string? BoardHint(TicketDef def, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string? refill = def.DailyCount > 0 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.PerDay"], def.DailyCount)
            : def.WeeklyCount > 0 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.PerWeek"], def.WeeklyCount) : null;
        string? kind = def.Id is 102 or 103 or DailyDungeonTicket ? loc["Checklist.Board.DailyDungeon"] : def.Id is >= 90000001 and <= 90000006 ? loc["Checklist.Board.Raid"] : null;
        return kind is null ? refill : refill is null ? kind : kind + "  ·  " + refill;
    }

    private static BoardCell Unknown(TicketCharacter c, int id)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return Aion2Tickets.IsLocked(c, id)
            ? new BoardCell("–", null, null, null, string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.Locked"], Aion2Tickets.UnlockLevel(id)), Tone.Muted,
                string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterLocked"], Aion2Tickets.UnlockLevel(id)))
            : new BoardCell("–", null, null, null, null, Tone.Muted, loc["Checklist.RosterUnknown"]);
    }

    /// <summary>
    /// A counter of one character now (worked out from the refills since the meter saw it, "~"): red with "−1 at 08:00" where the next daily refill runs over the
    /// cap, amber with "use by Wed 08:00" where the week's tickets are left on the last day before the weekly reset sets them back; else when it is full, or the
    /// next refill when none is left.
    /// </summary>
    private static BoardCell TicketCell(TicketCharacter c, TicketDef def, DateTime now, CultureInfo culture)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (Aion2Tickets.IsLocked(c, def.Id) || !c.Tickets.TryGetValue(def.Id, out TicketValue value))
        {
            return Unknown(c, def.Id);
        }

        bool premium = Aion2Tickets.HasPremium(c, DateTime.UtcNow) != false;
        int count = Aion2Tickets.Expected(def, value.Count, c.SavedAt.ToLocalTime(), now, premium);
        int cap = Math.Max(premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max, count);
        string main = (count != value.Count ? "~" : "") + count.ToString(CultureInfo.CurrentCulture);
        string? extra = value.Bonus > 0 ? "+" + value.Bonus.ToString(CultureInfo.CurrentCulture) : null;
        double? fill = cap > 0 ? count / (double)cap : null;
        string tip = def.NameIn(loc.Language) + ": " + count.ToString(CultureInfo.CurrentCulture) + " / " + cap.ToString(CultureInfo.CurrentCulture);
        DateTime? next = Aion2Tickets.NextReset(def, now);
        string At(DateTime when) => (when.Date != now.Date ? when.ToString("ddd", culture) + " " : "") + when.ToString("t", culture);

        int lost = Aion2Tickets.LostAtNextRefill(def, count, premium);
        if (lost > 0 && next is { } refill)
        {
            return new BoardCell(main, extra, "/" + cap, fill, string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.LostAt"], lost, At(refill)), Tone.Lost,
                tip + "\n" + string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.LostTip"], lost, At(refill), def.DailyCount));
        }

        bool weeklyOnly = def.WeeklyCount > 0 && def.DailyCount <= 0;
        if (weeklyOnly && count > 0 && next is { } reset && reset - now < TimeSpan.FromDays(1))
        {
            return new BoardCell(main, extra, "/" + cap, fill, string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.UseBy"], At(reset)), Tone.Warn,
                tip + "\n" + string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.UseByTip"], count, At(reset), def.WeeklyCount));
        }

        if (count <= 0)
        {
            string? coming = next is { } then ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.NextAt"], def.DailyCount > 0 ? def.DailyCount : def.WeeklyCount, At(then)) : null;
            return new BoardCell(main, extra, "/" + cap, fill, coming, Tone.Muted, tip);
        }

        // when the daily refills fill it
        string? sub = null;
        if (def.DailyCount > 0 && count < cap && next is { } first)
        {
            int refills = (cap - count + def.DailyCount - 1) / def.DailyCount;
            sub = string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.FullAt"], At(first.AddDays(refills - 1)));
        }

        return new BoardCell(main, extra, "/" + cap, fill, sub, Tone.Plain, tip);
    }

    /// <summary>A raid of the week: the attempts left, and under them whether its final boss's reward is still to take.</summary>
    private static BoardCell RaidCell(TicketCharacter c, TicketDef attempts, TicketDef? boss, DateTime now, CultureInfo culture)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        BoardCell cell = TicketCell(c, attempts, now, culture);
        if (boss is null || Aion2Tickets.IsLocked(c, boss.Id) || !c.Tickets.TryGetValue(boss.Id, out TicketValue value) || cell.Main == "–")
        {
            return cell;
        }

        bool premium = Aion2Tickets.HasPremium(c, DateTime.UtcNow) != false;
        int left = Aion2Tickets.Expected(boss, value.Count, c.SavedAt.ToLocalTime(), now, premium);
        string bossLine = left > 0 ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.BossLeft"], left) : loc["Checklist.Board.BossDone"];
        string tip = (cell.Tip ?? "") + "\n" + boss.NameIn(loc.Language) + ": " + left.ToString(CultureInfo.CurrentCulture) + " / " + Math.Max(boss.Max, left).ToString(CultureInfo.CurrentCulture);
        // a warning stays the line under the count; else the boss's reward is
        return cell.Tone is Tone.Warn or Tone.Lost ? cell with { Tip = tip }
            : cell with { Sub = bossLine, Tone = left == 0 && cell.Tone == Tone.Muted ? Tone.Muted : left == 0 ? Tone.Good : cell.Tone, Tip = tip };
    }

    private static BoardCell TasksCell(TicketCharacter c, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (!c.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions))
        {
            return new BoardCell("–", null, null, null, null, Tone.Muted, loc["Checklist.RosterUnknown"]);
        }

        bool stale = Aion2Tickets.MissionsStale(missions, now);
        int done = stale ? 0 : missions.Count, of = Aion2Tickets.MissionsPerDay;
        return done >= of
            ? new BoardCell("✓ " + done.ToString(CultureInfo.CurrentCulture), null, "/" + of, 1, loc["Checklist.Board.AllIn"], Tone.Good, loc["Checklist.RosterTasksTip"])
            : new BoardCell((stale ? "~" : "") + done.ToString(CultureInfo.CurrentCulture), null, "/" + of, done / (double)of, null, Tone.Plain, loc["Checklist.RosterTasksTip"]);
    }

    /// <summary>The basic Odyle Energy against its cap with the charged energy on top; red once it is full (its recharge goes nowhere), amber in the last hours.</summary>
    private static BoardCell OdyleCell(TicketCharacter c, Aion2Tickets.OdyleState? o, DateTime utcNow, CultureInfo culture)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (o is null)
        {
            return Unknown(c, Aion2Tickets.OdyleId);
        }

        string main = (o.Estimated ? "~" : "") + o.Count.ToString("N0", CultureInfo.CurrentCulture);
        string? extra = o.Bonus > 0 ? "+" + o.Bonus.ToString("N0", CultureInfo.CurrentCulture) : null;
        string cap = "/" + o.Cap.ToString("N0", CultureInfo.CurrentCulture);
        double fill = o.Cap > 0 ? o.Count / (double)o.Cap : 0;
        string tip = string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterOdyleLine"], o.Count, o.Cap, o.Bonus, o.PerTick)
            + (o.NextTick is { } tick ? "\n" + string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterNextTick"], o.PerTick, tick.ToLocalTime().ToString("t", culture)) : "");
        if (o.Full)
        {
            return new BoardCell(main, extra, cap, fill, loc["Checklist.Board.OdyleFull"], Tone.Lost, tip + "\n" + loc["Checklist.RosterFullWaste"]);
        }

        if (o.FullAt is { } at)
        {
            DateTime local = at.ToLocalTime();
            bool soon = at - utcNow < TimeSpan.FromHours(6);
            string when = soon ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.FullIn"], SpanText(at - utcNow))
                : string.Format(CultureInfo.CurrentCulture, loc["Checklist.Board.FullAt"], (local.Date != DateTime.Today ? local.ToString("ddd", culture) + " " : "") + local.ToString("t", culture));
            return new BoardCell(main, extra, cap, fill, when, soon ? Tone.Warn : Tone.Plain, tip);
        }

        return new BoardCell(main, extra, cap, fill, null, Tone.Plain, tip);
    }

    private static BoardCell CorridorCell(TicketCharacter c, IReadOnlyList<TicketCharacter> all, Aion2Corridors.Window window)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Aion2Corridors.State? state = Aion2Corridors.Of(c, all, window);
        if (state is null)
        {
            return new BoardCell("–", null, null, null, null, Tone.Muted, window.SiegeOn ? loc["Checklist.CorridorSiege"] : loc["Checklist.CorridorNotSeen"]);
        }

        string tip = state.Open == 0 ? loc["Checklist.CorridorNone"]
            : state.From is null ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.CorridorLeft"], state.Left, state.Open)
            : string.Format(CultureInfo.CurrentCulture, loc["Checklist.CorridorFrom"], state.Open, state.From);
        return new BoardCell((state.From is null ? "" : "~") + state.Left.ToString(CultureInfo.CurrentCulture), null, "/" + state.Open.ToString(CultureInfo.CurrentCulture),
            state.Open > 0 ? state.Left / (double)state.Open : null, null, state.Left > 0 ? Tone.Plain : Tone.Muted, tip);
    }

    /// <summary>Hours of access left ("7:00").</summary>
    private static BoardCell TimeCell(TicketCharacter c, TicketDef def)
    {
        if (Aion2Tickets.IsLocked(c, def.Id) || !c.Tickets.TryGetValue(def.Id, out TicketValue value))
        {
            return Unknown(c, def.Id);
        }

        TimeSpan left = TimeSpan.FromMilliseconds(Math.Max(0, value.TimeMs));
        return new BoardCell($"{(int)left.TotalHours}:{left.Minutes:00}", null, null, null, null, left > TimeSpan.Zero ? Tone.Plain : Tone.Muted, def.NameIn(LocalizationManager.Instance.Language));
    }
}

public sealed partial class ChecklistWindow
{
    // ---- the characters as a bar over the page (the column on the left took the width eight characters' columns need) ----

    /// <summary>"All characters" with how many there are.</summary>
    private static UIElement RosterChip(bool active, int characters, Action choose)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock glyph = Label("\uE716", active ? "Brush.Accent" : "Brush.TextMuted", 13);
        glyph.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        line.Children.Add(glyph);
        line.Children.Add(Label(loc["Checklist.Roster"], active ? "Brush.Text" : "Brush.TextMuted", 12.5, bold: true));
        var count = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = Label(characters.ToString(CultureInfo.CurrentCulture), "Brush.TextMuted", 11, bold: true) };
        count.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
        line.Children.Add(count);
        Color accent = Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        return Chip(line, active, accent, string.Format(CultureInfo.CurrentCulture, loc["Checklist.RosterCount"], characters), choose);
    }

    /// <summary>A character: its emblem, its name in its class colour, a green dot while it is the one logged in; all it is in the tool tip.</summary>
    private static UIElement CharacterChip(TicketCharacter c, bool active, bool online, Action choose)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        Color accent = className is not null ? ClassBrushes.ClassColor(className) : Resource("Brush.Accent", Color.FromRgb(0xE0, 0xB6, 0x4A));
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, CultureInfo.CurrentCulture) is ImageSource icon)
        {
            line.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        TextBlock name = Label(c.Name, c.Tickets.Count > 0 ? "Brush.Text" : "Brush.TextSubtle", 12.5, bold: true);
        if (className is not null && c.Tickets.Count > 0)
        {
            name.Foreground = ClassBrushes.Text(className);
        }

        line.Children.Add(name);
        if (online)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Heal");
            line.Children.Add(dot);
        }

        var tip = new List<string> { c.Name };
        if (className is not null)
        {
            tip.Add(AionDPS.Data.ClassCatalog.DisplayName(className, loc.Language));
        }

        if (Strength(c, loc) is { } strength)
        {
            tip.Add(strength);
        }

        if (c.ServerId > 0)
        {
            tip.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId));
        }

        if (!string.IsNullOrEmpty(c.Legion))
        {
            tip.Add("<" + c.Legion + ">");
        }

        tip.Add(online ? loc["Checklist.OnlineNow"] : c.LastOnlineMs > 0
            ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.LastOnline"], DateTimeOffset.FromUnixTimeMilliseconds(c.LastOnlineMs).LocalDateTime.ToString("g", loc.Culture)) : "");
        return Chip(line, active, accent, string.Join("\n", tip.Where(t => t.Length > 0)), choose);
    }

    /// <summary>The pill of a chip: lit in its colour while chosen, a shade under the mouse otherwise.</summary>
    private static UIElement Chip(UIElement content, bool active, Color accent, string tip, Action choose)
    {
        var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1),
            Child = content, Cursor = Cursors.Hand, ToolTip = tip };
        if (active)
        {
            chip.Background = Tint(accent, 0x24);
            chip.BorderBrush = Tint(accent, 0xA0);
        }
        else
        {
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        }

        chip.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            choose();
        };
        return chip;
    }
}
