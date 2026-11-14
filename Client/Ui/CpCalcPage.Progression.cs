using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The Progression tab (14 November 2026, the owner: "add to the app as another tab to the cp calc"): the website's progression guides on the character the app
/// reads (<see cref="CpProgression"/>). The stage its Item Level is at, with the guides opened for this character on atreia.app; its Combat Power against the
/// characters of the same Item Level, all classes and its own; the best next steps on the pieces worth keeping and the pieces below the stage's dungeon tier, each
/// with the Item Level and Combat Power it adds; every piece against the stage; what the players of its Item Level have; and the Road to 3,000+ checklist ticked
/// from the character. The Combat Power of a step is the calculator's (what the step adds on its own), the other characters' figures the website's.
/// </summary>
public sealed partial class CpCalcPage
{
    /// <summary>How many of the best next steps the tab lists, and how many of the pieces below the tier.</summary>
    private const int ProgSteps = 8, ProgPieces = 6;

    private static readonly Color ProgRed = Color.FromRgb(0xE0, 0x56, 0x6B);

    private UIElement ProgressionTab(View v, CpModel model, Aion2EntityDirectory? directory, IReadOnlyList<Aion2EquippedItem> gear)
    {
        // the game's own Item Level now (sent with every change), else the page's (the last check's, the login list's, the character page's)
        int? il = (directory is not null ? MainWindow.OwnItemLevel(directory) : null) ?? CharacterOf(directory).ItemLevel;
        char race = CpWhole.RaceOf(gear);
        IReadOnlyList<CpProgPiece> pieces = CpProgression.Read(v.Pieces, il, race, v.Totals, model);
        // a Clash Rune's steps aside, as on the rest of the page (players don't raise them for Combat Power)
        var steps = v.Whole.Steps.Where(s => s.Key != CpWhole.RuneKey).ToList();

        var page = new StackPanel();
        page.Children.Add(Spaced(StageCard(il)));
        page.Children.Add(Spaced(PaceCard(v, il)));

        var columns = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border next = NextStepsCard(pieces, steps, v.Totals, model);
        next.VerticalAlignment = VerticalAlignment.Top;
        Add(columns, 0, next);
        Border slots = NextPiecesCard(v, pieces, il, model);
        slots.VerticalAlignment = VerticalAlignment.Top;
        Add(columns, 2, slots);
        page.Children.Add(columns);

        page.Children.Add(Spaced(GearStageCard(pieces, steps, il)));
        if (CpProgression.WearOf(il) is { } wear)
        {
            page.Children.Add(Spaced(WearCard(v, pieces, wear)));
        }

        if (il >= CpProgression.RoadFrom)
        {
            page.Children.Add(Spaced(RoadCard(v, pieces, il, race, model)));
        }

        return page;
    }

    // ------------------------------------------------------------------ the stage and the guides

    private Border StageCard(int? il)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Prog.Stage")));
        if (il is not int level)
        {
            stack.Children.Add(Label(Loc("CpCalc.Prog.NoItemLevel"), 12.5, brush: Res("Brush.TextMuted")));
            return Card(stack);
        }

        stack.Children.Add(new TextBlock { Text = F("CpCalc.Hero.ItemLevel", Thousands(level)), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Solid(Gold) });
        foreach (CpGuide guide in CpProgression.GuidesFor(level))
        {
            stack.Children.Add(GuideRow(guide, level));
        }

        stack.Children.Add(Label(Loc("CpCalc.Prog.StageNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 12, 0, 0)));
        return Card(stack);
    }

    /// <summary>A guide for the Item Level: how far into it the character is (the Road to 3,000+ up to 3,000), and the button that opens it on atreia.app.</summary>
    private UIElement GuideRow(CpGuide guide, int level)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int goal = guide.To == int.MaxValue ? CpProgression.RoadGoal : guide.To;
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = guide.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        string where = level >= goal
            ? F("CpCalc.Prog.GuidePast", Thousands(level), Thousands(goal))
            : F("CpCalc.Prog.GuideAt", Thousands(level), Thousands(guide.From), Thousands(goal), Thousands(goal - level));
        left.Children.Add(LevelBar(level - guide.From, goal - guide.From, Gold, where));
        Add(row, 0, left);
        string url = GuideUrl(guide);
        var open = new Button
        {
            Content = Loc("CpCalc.Prog.Open"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = F("CpCalc.Prog.OpenTip", guide.Name),
        };
        open.Click += (_, _) => OpenWeb(url);
        Add(row, 1, open);
        return row;
    }

    /// <summary>The guide's page with the character (region, server, name: the guide reads it from the game's official character page), at "Make this guide yours".</summary>
    private string GuideUrl(CpGuide guide)
    {
        string url = Brand.Site + guide.Path;
        string region = _server > 0 ? FindPlayerClient.RegionOf(_server) : "all";
        if (_character is { Length: > 0 } name && region != "all")
        {
            url += "?region=" + Uri.EscapeDataString(region) + "&server=" + Uri.EscapeDataString(Aion2Servers.NameOf(_server)) + "&name=" + Uri.EscapeDataString(name);
        }

        return url + "#your-character";
    }

    private static void OpenWeb(string url)
    {
        if (!Links.IsWeb(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // no browser to open it in: nothing to do
        }
    }

    // ------------------------------------------------------------------ Combat Power at the Item Level

    private Border PaceCard(View v, int? il)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Prog.Pace")));
        if (il is not int level || CpProgression.BandOf(level) is not { } all)
        {
            stack.Children.Add(Label(Loc("CpCalc.Prog.PaceNone"), 12.5, brush: Res("Brush.TextMuted")));
            return Card(stack);
        }

        // the number at the top of the page: the game's at the last login (or the character page's), else the calculator's
        double cp = v.GameCp ?? v.Whole.Total;
        stack.Children.Add(new TextBlock { Text = F("CpCalc.Prog.PaceYou", Thousands(cp), Thousands(level)), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        stack.Children.Add(Label(Loc(v.GameCp is null ? "CpCalc.Prog.PaceEstimate" : "CpCalc.Prog.PaceGame"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 0)));
        stack.Children.Add(PaceRow(Loc("CpCalc.Prog.PaceAll"), all, cp));
        if (ClassName() is { } cls && CpProgression.BandOf(level, cls) is { } own)
        {
            stack.Children.Add(PaceRow(F("CpCalc.Prog.PaceClass", cls), own, cp));
        }

        stack.Children.Add(Label(Loc(cp < all.Low ? "CpCalc.Prog.PaceLow" : "CpCalc.Prog.PaceOk"), 12.5, margin: new Thickness(0, 14, 0, 0)));
        stack.Children.Add(Label(Loc("CpCalc.Prog.PaceNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 6, 0, 0)));
        return Card(stack);
    }

    /// <summary>One comparison: who, the band's middle and middle half, the character's distance from the middle, and the band as a bar with the character on it.</summary>
    private static UIElement PaceRow(string who, CpBand band, double cp)
    {
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = who, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        words.Children.Add(new TextBlock
        {
            Text = F("CpCalc.Prog.PaceTypical", Thousands(band.Middle), Thousands(band.Low), Thousands(band.High), Thousands(band.From), Thousands(band.From + 99), Thousands(band.Count)),
            FontSize = 11.5, Foreground = Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap,
        });
        Add(head, 0, words);
        double diff = cp - band.Middle, share = diff / Math.Max(1, band.Middle) * 100;
        Color tone = cp < band.Low ? ProgRed : cp > band.High ? Green : Gold;
        string where = Loc(cp < band.Low ? "CpCalc.Prog.Below" : cp > band.High ? "CpCalc.Prog.Above" : "CpCalc.Prog.Inside");
        string percent = (share >= 0 ? "+" : "−") + Math.Abs(share).ToString("0.#", CultureInfo.CurrentCulture) + " %";
        Add(head, 1, Chip(Signed(diff) + " (" + percent + ")  ·  " + where, tone, new Thickness(12, 0, 0, 0)));
        box.Children.Add(head);
        box.Children.Add(RangeBar(band, cp, tone));
        return box;
    }

    /// <summary>The band on a scale: the middle half as the bar's bright part, the middle as a tick, the character as a dot.</summary>
    private static UIElement RangeBar(CpBand band, double cp, Color tone)
    {
        double spread = Math.Max(1, band.High - band.Low);
        double lo = Math.Min(band.Low, cp) - spread * 0.5, hi = Math.Max(band.High, cp) + spread * 0.5;
        var layers = new Grid { Height = 16, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, band.Low - lo), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, band.High - band.Low), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, hi - band.High), GridUnitType.Star) });
        Add(track, 0, new Border { Background = Res("Brush.Border"), CornerRadius = new CornerRadius(4, 0, 0, 4) });
        Add(track, 1, new Border { Background = Tint(Gold, 0x70) });
        Add(track, 2, new Border { Background = Res("Brush.Border"), CornerRadius = new CornerRadius(0, 4, 4, 0) });
        layers.Children.Add(track);
        layers.Children.Add(Marker(band.Middle - lo, hi - band.Middle,
            new Border { Width = 2, Height = 16, Background = Res("Brush.Text"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-1, 0, 0, 0) }));
        layers.Children.Add(Marker(cp - lo, hi - cp,
            new Ellipse { Width = 12, Height = 12, Fill = Solid(tone), Stroke = Res("Brush.Panel"), StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 0, 0, 0) }));
        return layers;
    }

    /// <summary>A mark at a place on a scale: the scale split there, the mark at the start of the second part.</summary>
    private static Grid Marker(double before, double after, FrameworkElement mark)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, before), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, after), GridUnitType.Star) });
        mark.VerticalAlignment = VerticalAlignment.Center;
        Add(grid, 1, mark);
        return grid;
    }

    // ------------------------------------------------------------------ the next steps and the next pieces

    /// <summary>Enhancement and breakthrough on the pieces worth keeping, the most Combat Power first, and what all of them to the top add up to.</summary>
    private Border NextStepsCard(IReadOnlyList<CpProgPiece> pieces, IReadOnlyList<CpStep> steps, CpTotals totals, CpModel model)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Prog.Next")));
        var keepers = pieces.Where(p => p.Keeper).ToDictionary(p => p.Piece.Slot);
        var best = steps.Where(s => s.Kind is CpStepKind.Enhance or CpStepKind.Breakthrough && keepers.ContainsKey(s.Slot)).OrderByDescending(s => s.Gain).Take(ProgSteps).ToList();
        if (best.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.Prog.NextNone"), 12.5, brush: Res("Brush.TextMuted")));
        }

        foreach (CpStep s in best)
        {
            stack.Children.Add(ProgStepRow(s, s.Kind == CpStepKind.Breakthrough ? 5 : 1));
        }

        // every piece worth keeping to its highest enhancement (its Combat Power exact), then breakthrough to the top (its Item Level)
        int enhanceIl = 0, breakIl = 0;
        double enhanceCp = 0;
        foreach (CpProgPiece p in keepers.Values.Where(p => p.Piece.Data is { Category: not "Rune" } && p.MaxEnchant > 0))
        {
            CpItemData data = p.Piece.Data!;
            enhanceIl += Math.Max(0, p.MaxEnchant - p.Piece.Enchant);
            enhanceCp += CpCalc.ItemCp(data, Math.Max(p.MaxEnchant, p.Piece.Enchant), p.Piece.Exceed, totals, model) - CpCalc.ItemCp(data, p.Piece.Enchant, p.Piece.Exceed, totals, model);
            breakIl += 5 * Math.Max(0, p.MaxExceed - p.Piece.Exceed);
        }

        string sum = enhanceIl > 0 ? F("CpCalc.Prog.ToMax", Thousands(enhanceIl), Signed(enhanceCp)) : Loc("CpCalc.Prog.AllMax");
        if (breakIl > 0)
        {
            sum += F("CpCalc.Prog.ThenBreak", Thousands(breakIl));
        }

        stack.Children.Add(Label(sum + ".", 12, margin: new Thickness(0, 10, 0, 0)));
        stack.Children.Add(Label(Loc("CpCalc.Prog.NextNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 6, 0, 0)));
        return Card(stack);
    }

    /// <summary>A step with the Item Level it adds: the Item Level as a gold tag, the Combat Power as a green one, what it is and what it costs, how sure it is.</summary>
    private UIElement ProgStepRow(CpStep s, int itemLevel)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(row, 0, Pill(itemLevel > 0 ? "+" + Thousands(itemLevel) + " IL" : "", Gold));
        Add(row, 1, Pill(s.Certainty == CpCertainty.Range ? Range(s.Gain, s.GainMax, signed: true) : Signed(s.Gain), Green));
        if (StepIcon(s) is { } icon)
        {
            icon.Margin = new Thickness(0, 0, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Add(row, 2, icon);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = StepText(s), FontSize = 12.5, Foreground = Res("Brush.Text"), TextWrapping = TextWrapping.Wrap });
        if (CostText(s) is { } cost)
        {
            text.Children.Add(new TextBlock { Text = cost.Text, FontSize = 11, Foreground = cost.Free ? Solid(Green) : Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap,
                ToolTip = cost.Tip, Margin = new Thickness(0, 1, 0, 0) });
        }

        Add(row, 3, text);
        var (chipText, chipColor, tip) = Certainty(s);
        Add(row, 4, Chip(chipText, chipColor, new Thickness(10, 0, 0, 0), tip));
        return new Border { Child = row, Padding = new Thickness(0, 4, 0, 4) };
    }

    /// <summary>A figure as a soft tag in a colour (nothing for an empty text).</summary>
    private static Border Pill(string text, Color color) => new()
    {
        Background = text.Length > 0 ? Tint(color, 0x1E) : Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 4), Margin = new Thickness(0, 0, 8, 0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Solid(color) },
    };

    /// <summary>The pieces below the stage's tier, the lowest first, with the piece to get; then the quick Item Level (empty sockets, Arcana levels) and empty slots.</summary>
    private Border NextPiecesCard(View v, IReadOnlyList<CpProgPiece> pieces, int? il, CpModel model)
    {
        var stack = new StackPanel();
        var below = pieces.Where(p => !p.AtTier).OrderBy(p => p.ItemLevel).ThenBy(p => p.Kind).ThenBy(p => p.Piece.Slot).ToList();
        stack.Children.Add(Heading(Loc(below.All(p => p.Unique) ? "CpCalc.Prog.Pieces" : "CpCalc.Prog.Weakest")));
        CpTier? tier = CpProgression.TierFor(il);
        if (tier is null)
        {
            stack.Children.Add(Label(Loc(il is null ? "CpCalc.Prog.TierUnknown" : "CpCalc.Prog.TierNone"), 12.5, brush: Res("Brush.TextMuted")));
        }
        else if (below.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.Prog.PiecesNone"), 12.5, brush: Res("Brush.TextMuted")));
        }

        foreach (CpProgPiece p in below.Take(ProgPieces))
        {
            stack.Children.Add(ReplaceRow(p, v.Totals, model));
        }

        if (below.Count > ProgPieces)
        {
            stack.Children.Add(Label(F("CpCalc.Prog.PiecesMore", below.Count - ProgPieces), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
        }

        if (tier is not null && below.Count > 0)
        {
            stack.Children.Add(Label(Loc(tier.Optional ? "CpCalc.Prog.TierOptional" : "CpCalc.Prog.TransferNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        }

        // quick Item Level: every empty manastone socket (1 to 4 IL a stone by its colour), every Arcana level (+1 IL)
        var quick = new List<UIElement>();
        foreach (CpProgPiece p in pieces.Where(p => p.Piece.EmptyStones > 0))
        {
            quick.Add(Row("+" + Thousands(4 * p.Piece.EmptyStones) + " IL", F("CpCalc.Prog.QuickStones", SlotOf(p.Piece.ItemId), p.Piece.EmptyStones), null, Solid(Gold), p.Piece.ItemId));
        }

        foreach (CpArcanaRow a in v.Whole.Arcana)
        {
            int top = CpProgression.ArcanaTop(CpProgression.ArcanaGrade(a.ItemId));
            if (a.Level < top)
            {
                quick.Add(Row("+" + Thousands(top - a.Level) + " IL", F("CpCalc.Prog.QuickArcana", LootText.ItemName(a.ItemId), a.Level, top), null, Solid(Gold), a.ItemId));
            }
        }

        if (quick.Count > 0)
        {
            stack.Children.Add(SubHead(Loc("CpCalc.Prog.Quick")));
            foreach (UIElement q in quick.Take(8))
            {
                stack.Children.Add(q);
            }
        }

        if (CpProgression.Missing(pieces) is { Count: > 0 } missing)
        {
            stack.Children.Add(Label(F("CpCalc.Prog.Missing", MissingText(missing)), 12, brush: Res("Brush.Warning"), margin: new Thickness(0, 12, 0, 0)));
        }

        return Card(stack);
    }

    /// <summary>
    /// A slot below the stage's tier: the stage's piece of the same kind, the Item Level and Combat Power it adds (a Unique piece keeps its enhancement,
    /// breakthrough and soul lines with a Transfer Stone; a new piece starts at +0) and where the guide gets it.
    /// </summary>
    private UIElement ReplaceRow(CpProgPiece p, CpTotals totals, CpModel model)
    {
        CpTierGear target = p.Target!;
        var (now, later) = CpProgression.TargetItemLevel(p);
        double? cp = CpProgression.TargetCp(p, totals, model);
        var row = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Add(row, 0, Pill(SignedIl(now), now >= 0 ? Gold : ProgRed));
        Add(row, 1, Pill(cp is double c ? Signed(c) : "", Green));
        var icon = IconTile(p.TargetItem?.Id ?? p.Piece.ItemId, 26, null);
        icon.Margin = new Thickness(0, 0, 9, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Add(row, 2, icon);
        var words = new StackPanel();
        string name = p.TargetItem is { } item ? LootText.ItemName(item.Id) : target.Source;
        words.Children.Add(new TextBlock
        {
            Text = (p.Optional ? Loc("CpCalc.Prog.Optional") : "") + F("CpCalc.Prog.ReplaceHead", SlotOf(p.Piece.ItemId), name, target.ItemLevel),
            FontSize = 12.5, Foreground = Res("Brush.Text"), TextWrapping = TextWrapping.Wrap,
        });
        string how = p.Unique
            ? F("CpCalc.Prog.TransferKeeps", p.Piece.Enchant, p.Piece.Exceed)
            : later != now ? F("CpCalc.Prog.ReplaceLater", SignedIl(later), p.Piece.Enchant) : Loc("CpCalc.Prog.ReplaceNew");
        words.Children.Add(new TextBlock { Text = how, FontSize = 11.5, Foreground = Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
        words.Children.Add(new TextBlock
        {
            Text = F("CpCalc.Prog.ReplaceFrom", target.Source + (target.Also is { } also ? "; " + also : "")), FontSize = 11.5, Foreground = Res("Brush.TextSubtle"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0),
        });
        Add(row, 3, words);
        return row;
    }

    /// <summary>The empty slots by name, a slot missing twice (the earrings, the rings) once with ×2.</summary>
    private static string MissingText(IReadOnlyList<string> slots) =>
        string.Join(", ", slots.GroupBy(s => s).Select(g => Loc("Details.Seen.Cat." + g.Key) + (g.Count() > 1 ? " ×" + g.Count().ToString(CultureInfo.CurrentCulture) : "")));

    // ------------------------------------------------------------------ every piece against the stage

    private Border GearStageCard(IReadOnlyList<CpProgPiece> pieces, IReadOnlyList<CpStep> steps, int? il)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Prog.Gear")));
        CpTier? tier = CpProgression.TierFor(il);
        if (tier is null)
        {
            stack.Children.Add(Label(Loc(il is null ? "CpCalc.Prog.TierUnknown" : "CpCalc.Prog.TierNone"), 12.5, brush: Res("Brush.TextMuted")));
        }
        else
        {
            stack.Children.Add(Label(F("CpCalc.Prog.TierHead", Thousands(tier.From)), 12.5, FontWeights.SemiBold));
            foreach (CpGearKind kind in new[] { CpGearKind.Weapon, CpGearKind.Guard, CpGearKind.Armor, CpGearKind.Jewelry })
            {
                if (tier.For(kind) is { } g)
                {
                    string text = F("CpCalc.Prog.TierGear", Loc("CpCalc.Prog.Kind." + kind), g.ItemLevel, g.Source + (g.Also is { } also ? " (" + also + ")" : ""));
                    stack.Children.Add(Label("•  " + text, 12.5, margin: new Thickness(10, 3, 0, 0)));
                }
            }
        }

        // the weapon and the Guard together, then the armor, the jewelry, the rest
        foreach (var group in pieces.GroupBy(p => p.Kind == CpGearKind.Guard ? CpGearKind.Weapon : p.Kind).OrderBy(g => g.Key))
        {
            stack.Children.Add(SubHead(Loc("CpCalc.Prog.Group." + group.Key)));
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) };
            foreach (CpProgPiece p in group)
            {
                grid.Children.Add(PieceTile(p, steps));
            }

            stack.Children.Add(grid);
        }

        return Card(stack);
    }

    /// <summary>A piece against the stage: its picture with its levels, its slot and Item Level, its name, its levels against its tables' top, the stage's verdict and its next step.</summary>
    private UIElement PieceTile(CpProgPiece p, IReadOnlyList<CpStep> steps)
    {
        CpPiece piece = p.Piece;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = IconTile(piece.ItemId, 40, LevelBadge(piece));
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Add(grid, 0, icon);
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = SlotOf(piece.ItemId) + "  ·  IL " + p.ItemLevel.ToString(CultureInfo.CurrentCulture), FontSize = 11, Foreground = Res("Brush.TextSubtle") });
        words.Children.Add(new TextBlock
        {
            Text = LootText.ItemName(piece.ItemId), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Solid(LootText.GradeColor(piece.ItemId)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(new TextBlock { Text = Figures(p), FontSize = 11.5, Foreground = Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap });
        Color? tone = null;
        if (TierChip(p) is { } chip)
        {
            chip.Margin = new Thickness(0, 5, 0, 0);
            words.Children.Add(chip);
            tone = p.AtTier ? (Color?)null : p.Unique ? Gold : ProgRed;
        }

        if (NextFor(p, steps) is { } next)
        {
            words.Children.Add(new TextBlock { Text = next, FontSize = 11.5, Foreground = Res("Brush.Text"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        }

        Add(grid, 1, words);
        return new Border
        {
            Child = grid, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = tone is Color c ? Tint(c, 0x80) : Res("Brush.Border"),
        };
    }

    /// <summary>The levels against the tables' top: "+12 of +15 · breakthrough 0 of 5", and the empty manastone sockets.</summary>
    private static string Figures(CpProgPiece p)
    {
        var parts = new List<string> { p.MaxEnchant > 0 ? F("CpCalc.Prog.Enchant", p.Piece.Enchant, p.MaxEnchant) : "+" + p.Piece.Enchant.ToString(CultureInfo.CurrentCulture) };
        if (p.MaxExceed > 0)
        {
            parts.Add(F("CpCalc.Prog.Exceed", p.Piece.Exceed, p.MaxExceed));
        }

        if (p.Piece.EmptyStones > 0)
        {
            parts.Add(F("CpCalc.Prog.Empty", p.Piece.EmptyStones));
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>The stage's verdict on a piece: at the tier, the next tier (Unique: its levels move on), a weak slot, below an optional tier; none for the slots the stage names no gear for.</summary>
    private static Border? TierChip(CpProgPiece p)
    {
        if (p.Target is not { } target)
        {
            return null;
        }

        return p.AtTier ? Chip(Loc("CpCalc.Prog.AtTier"), Green, new Thickness(0))
            : p.Optional ? Chip(F("CpCalc.Prog.BelowOptional", target.ItemLevel), Grey, new Thickness(0))
            : p.Unique ? Chip(F("CpCalc.Prog.NextTier", target.ItemLevel), Gold, new Thickness(0), Loc("CpCalc.Prog.NextTierTip"))
            : Chip(F("CpCalc.Prog.Weak", target.ItemLevel), ProgRed, new Thickness(0), Loc("CpCalc.Prog.WeakTip"));
    }

    /// <summary>A piece's next step in one line: the next enhancement or breakthrough with its Item Level, Combat Power and chance; "done for now" at the top.</summary>
    private static string? NextFor(CpProgPiece p, IReadOnlyList<CpStep> steps)
    {
        if (!p.Keeper)
        {
            return Loc("CpCalc.Prog.NotKeeper");
        }

        CpStep? s = steps.FirstOrDefault(x => x.Slot == p.Piece.Slot && x.Kind is CpStepKind.Enhance or CpStepKind.Breakthrough);
        if (s is null)
        {
            return p.FullyEnhanced && p.Piece.Exceed >= p.MaxExceed ? Loc("CpCalc.Prog.Done") : null;
        }

        bool breakthrough = s.Kind == CpStepKind.Breakthrough;
        string text = F(breakthrough ? "CpCalc.Prog.NextBreak" : "CpCalc.Prog.NextEnhance", s.ToLevel, breakthrough ? 5 : 1, Signed(s.Gain));
        return s.Chance is double c && c < 1 ? text + "  ·  " + F("CpCalc.Prog.Chance", (c * 100).ToString("0.#", CultureInfo.CurrentCulture)) : text;
    }

    // ------------------------------------------------------------------ what the players of the Item Level have

    private Border WearCard(View v, IReadOnlyList<CpProgPiece> pieces, CpWearBand band)
    {
        var stack = new StackPanel();
        string range = band.To == int.MaxValue ? Thousands(band.From) + "+" : Thousands(band.From) + "–" + Thousands(band.To - 1);
        stack.Children.Add(Heading(F("CpCalc.Prog.Wear", range)));
        CpYou you = CpProgression.You(pieces, v.Whole.Arcana, v.Whole.Stigmas, v.Whole.Boards);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        // a line of the table: what, the character's (green when as far as the band, gold when not), the band's
        void AddLine(string what, string mine, string theirs, bool? ahead, bool header = false)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock Cell(string text, Brush brush, bool strong) => new()
            {
                Text = header ? text.ToUpper(CultureInfo.CurrentCulture) : text, FontSize = header ? 11 : 12.5, Foreground = brush, Margin = new Thickness(0, 4, 12, 4),
                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            };
            Brush muted = Res("Brush.TextMuted");
            Brush mineBrush = header ? muted : ahead switch { true => Solid(Green), false => Solid(Gold), _ => Res("Brush.Text") };
            TextBlock[] cells = { Cell(what, header ? muted : Res("Brush.Text"), false), Cell(mine, mineBrush, !header), Cell(theirs, muted, false) };
            for (int k = 0; k < cells.Length; k++)
            {
                SetRow(cells[k], r);
                SetColumn(cells[k], k);
                grid.Children.Add(cells[k]);
            }

            r++;
        }

        AddLine("", Loc("CpCalc.Prog.WearYou"), Loc("CpCalc.Prog.WearThem"), null, header: true);
        AddLine(Loc("CpCalc.Prog.WearRow.Weapon"), Mine(you.Weapon, single: true), Theirs(band.Weapon, single: true), Ahead(you.Weapon, band.Weapon));
        AddLine(Loc("CpCalc.Prog.WearRow.Guard"), Mine(you.Guard, single: true), Theirs(band.Guard, single: true), Ahead(you.Guard, band.Guard));
        AddLine(Loc("CpCalc.Prog.WearRow.Armor"), Mine(you.Armor, single: false), Theirs(band.Armor, single: false), Ahead(you.Armor, band.Armor));
        AddLine(Loc("CpCalc.Prog.WearRow.Jewelry"), Mine(you.Jewelry, single: false), Theirs(band.Jewelry, single: false), Ahead(you.Jewelry, band.Jewelry));
        AddLine(Loc("CpCalc.Prog.WearRow.Arcana"), you.Arcana is double a ? "+" + a.ToString("0.0", CultureInfo.CurrentCulture) : "–",
            "+" + band.Arcana.ToString("0.0", CultureInfo.CurrentCulture), you.Arcana is double b ? b >= band.Arcana - 0.05 : (bool?)null);
        bool boards = v.Whole.Boards.Count > 0, stigmas = v.Whole.Stigmas.Count > 0;
        AddLine(Loc("CpCalc.Prog.WearRow.Nodes"), boards ? Thousands(you.Nodes) : "–", Thousands(band.Nodes), boards ? you.Nodes >= band.Nodes : (bool?)null);
        AddLine(Loc("CpCalc.Prog.WearRow.Stigmas"), stigmas ? Thousands(you.Stigmas) : "–", Thousands(band.Stigmas), stigmas ? you.Stigmas >= band.Stigmas : (bool?)null);
        stack.Children.Add(grid);
        stack.Children.Add(Label(F("CpCalc.Prog.WearNote", Thousands(band.Count)), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0)));
        return Card(stack);
    }

    /// <summary>The character's pieces of a kind: "+15 · ◆2 · IL 86" for the weapon and the Guard, "+13.3 · ◆1.0 · IL 102: 2 of 7" for armor and jewelry.</summary>
    private static string Mine(CpYouGroup? g, bool single)
    {
        if (g is null)
        {
            return Loc("CpCalc.Prog.WearNone");
        }

        string format = single ? "0" : "0.0";
        string levels = "+" + g.Enchant.ToString(format, CultureInfo.CurrentCulture) + "  ·  ◆" + g.Breakthrough.ToString(format, CultureInfo.CurrentCulture);
        return levels + "  ·  " + (single ? F("CpCalc.Prog.WearIl", g.ItemLevel) : F("CpCalc.Prog.WearTop", g.Top, g.Count));
    }

    /// <summary>The band's pieces of a kind: "+15.0 · ◆3.2 · IL 102: 19 %".</summary>
    private static string Theirs(CpWearGroup g, bool single) =>
        "+" + g.Enchant.ToString("0.0", CultureInfo.CurrentCulture) + "  ·  ◆" + g.Breakthrough.ToString("0.0", CultureInfo.CurrentCulture) + "  ·  "
        + F(single ? "CpCalc.Prog.WearShare" : "CpCalc.Prog.WearShareOf", g.Top.ToString("0", CultureInfo.CurrentCulture));

    /// <summary>Whether the character's pieces are as far as the band's: enhancement and breakthrough together, by the Item Level they make (+1 and +5 a level).</summary>
    private static bool? Ahead(CpYouGroup? mine, CpWearGroup theirs) =>
        mine is null ? (bool?)null : mine.Enchant + 5 * mine.Breakthrough >= theirs.Enchant + 5 * theirs.Breakthrough - 0.05;

    // ------------------------------------------------------------------ the Road to 3,000+

    private Border RoadCard(View v, IReadOnlyList<CpProgPiece> pieces, int? il, char race, CpModel model)
    {
        IReadOnlyList<CpRoadTask> tasks = CpProgression.Road(pieces, il, v.Whole.Arcana, v.Whole.Stigmas, race, v.Totals, model);
        int done = tasks.Count(t => t.Done);
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Prog.Road")));
        stack.Children.Add(LevelBar(done, tasks.Count, Green, F("CpCalc.Prog.RoadCount", done, tasks.Count)));
        foreach (var group in tasks.GroupBy(t => t.Group))
        {
            stack.Children.Add(SubHead(Loc("CpCalc.Prog.RoadGroup." + group.Key)));
            foreach (CpRoadTask t in group)
            {
                stack.Children.Add(TaskRow(t));
            }
        }

        stack.Children.Add(Label(Loc("CpCalc.Prog.RoadNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 12, 0, 0)));
        return Card(stack);
    }

    /// <summary>A task: ticked or not, its title and the guide's note, and what is left with what it adds.</summary>
    private static UIElement TaskRow(CpRoadTask t)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(row, 0, new TextBlock
        {
            Text = t.Done ? "✓" : "○", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = t.Done ? Solid(Green) : Res("Brush.TextSubtle"), VerticalAlignment = VerticalAlignment.Top,
        });
        var words = new StackPanel();
        words.Children.Add(new TextBlock
        {
            Text = Loc("CpCalc.Prog.Task." + t.Id), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = t.Done ? Res("Brush.TextMuted") : Res("Brush.Text"),
            TextWrapping = TextWrapping.Wrap,
        });
        words.Children.Add(new TextBlock { Text = Loc("CpCalc.Prog.Task." + t.Id + ".Note"), FontSize = 11.5, Foreground = Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap });
        Add(row, 1, words);
        if (!t.Done && LeftText(t) is { } left)
        {
            Add(row, 2, new TextBlock
            {
                Text = left, FontSize = 11.5, Foreground = Res("Brush.Text"), TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, MaxWidth = 330,
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return row;
    }

    /// <summary>What is left of a task in words: levels, pieces, sockets, cards, Stigma levels or Item Level, with the Item Level and Combat Power it adds; the empty slots.</summary>
    private static string? LeftText(CpRoadTask t)
    {
        string text = t.Id switch
        {
            "stones" => F("CpCalc.Prog.Left.Sockets", t.Left, t.ItemLevel),
            "arc5" => F("CpCalc.Prog.Left.Cards", t.Left),
            "arcmax" => t.Left > 0 ? F("CpCalc.Prog.Left.ArcanaLevels", t.Left) : "",
            "stig20" => F("CpCalc.Prog.Left.Stigma", t.Left),
            "il3000" => F("CpCalc.Prog.Left.ItemLevel", Thousands(t.Left)),
            "wpn102" or "grd102" or "jew102" => t.Left > 0 ? F("CpCalc.Prog.Left.Pieces", t.Left, Thousands(t.ItemLevel), Signed(t.Cp)) : "",
            _ => t.Left > 0 ? F("CpCalc.Prog.Left.Levels", t.Left, Thousands(t.ItemLevel), Signed(t.Cp)) : "",
        };
        if (t.Missing > 0)
        {
            text = (text.Length > 0 ? text + "  ·  " : "") + F("CpCalc.Prog.Left.Missing", t.Missing);
        }

        return text.Length > 0 ? text : null;
    }

    // ------------------------------------------------------------------ text

    /// <summary>A small capitalised heading inside a card.</summary>
    private static TextBlock SubHead(string text) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextSubtle"), Margin = new Thickness(0, 14, 0, 6),
    };

    /// <summary>A whole number with the thousands grouped: 3,010.</summary>
    private static string Thousands(double v) => Math.Round(v).ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>An Item Level change: "+16 IL", "−3 IL".</summary>
    private static string SignedIl(int v) => (v >= 0 ? "+" : "−") + Thousands(Math.Abs(v)) + " IL";
}
