namespace AionDPS.Ui;

/// <summary>
/// The meter's bars in parts (5 November 2026, the owner: "grey is buff, green heals, I want them to show ... + for heals on the bar", as the meters
/// players compare against draw them): first the player's own damage in their class colour, then in grey what their buffs added to the others' damage,
/// then in green their healing on the others. In the rDPS view the own part is nDPS, so the grey part ends at rDPS and the whole bar is rDPS + healing;
/// in the damage views the own part is the damage, the grey part the damage the buffs gave the others over the same time, the green part the healing
/// on the others. Every row is measured against the longest whole bar, so the own parts still compare with each other as before.
/// </summary>
public static partial class MeterBarParts
{
    /// <summary>The three parts of one row, before they are measured against the longest bar.</summary>
    public readonly record struct Parts(double Own, double Given, double Heal)
    {
        public double Whole => Own + Given + Heal;
    }

    /// <summary>
    /// A row's parts. <paramref name="rdpsView"/>: the bars by rate (own = nDPS where rDPS is known, else the rate shown); otherwise by the damage, the given
    /// part being the buffs' rate over <paramref name="seconds"/> (the span rDPS was worked out over). <paramref name="buffs"/> false: no grey part (in the
    /// rDPS view the own part is then the whole rDPS, so the bars still follow the order); <paramref name="heals"/> false: no green part. Both false: the own
    /// part alone, as the bars always were.
    /// </summary>
    public static Parts Of(PlayerRow row, bool rdpsView, bool buffs, bool heals, double seconds)
    {
        buffs &= !row.IsEnemy;
        heals &= !row.IsEnemy;
        if (rdpsView)
        {
            bool known = row.Rdps is not null && row.Ndps is not null;
            double whole = row.Rdps ?? row.Dps ?? 0;
            double healRate = heals ? Math.Max(0, row.HealOthersRate ?? 0) : 0;
            return buffs && known
                ? new Parts(Math.Max(0, row.Ndps!.Value), Math.Max(0, row.Given ?? 0), healRate)
                : new Parts(whole, 0, healRate);
        }

        double lent = buffs && seconds > 0 && row.Given is double rate && rate > 0 ? rate * seconds : 0;
        return new Parts(row.Damage, lent, heals ? Math.Max(0, row.HealOthers) : 0);
    }

    /// <summary>Both parts on (<paramref name="on"/>) or neither.</summary>
    public static Parts Of(PlayerRow row, bool rdpsView, bool on, double seconds) => Of(row, rdpsView, on, on, seconds);

    /// <summary>Both parts on (<paramref name="on"/>) or neither.</summary>
    public static void Apply(IEnumerable<PlayerRow> rows, bool rdpsView, bool on, double seconds) => Apply(rows, rdpsView, on, on, seconds);

    /// <summary>Sets every row's three bar ends and its "+" figures, all measured against the longest whole bar of <paramref name="rows"/>; a part that is
    /// switched off (<paramref name="buffs"/>, <paramref name="heals"/>) has neither its colour nor its "+" figure.</summary>
    public static void Apply(IEnumerable<PlayerRow> rows, bool rdpsView, bool buffs, bool heals, double seconds)
    {
        var parts = rows.Select(r => (Row: r, Parts: Of(r, rdpsView, buffs, heals, seconds))).ToList();
        double longest = parts.Count > 0 ? parts.Max(p => p.Parts.Whole) : 0;
        double Percent(double value) => longest > 0 ? Math.Clamp(100.0 * value / longest, 0, 100) : 0;
        foreach (var (row, p) in parts)
        {
            row.FillPercent = Percent(p.Own);
            row.GivenFillPercent = Percent(p.Own + p.Given);
            row.HealFillPercent = Percent(p.Whole);
            row.BarGivenText = p.Given > 0 && row.Given is double given && given >= 0.5 ? "+" + PlayerRow.Compact((long)Math.Round(given)) : "";
            row.BarHealText = p.Heal > 0 && row.HealOthersRate is double heal && heal >= 0.5 ? "+" + PlayerRow.Compact((long)Math.Round(heal)) : "";
        }
    }

    /// <summary>No parts on a row (the Heal and Taken modes, PvP): the bar is its own part alone.</summary>
    public static void Clear(PlayerRow row)
    {
        row.GivenFillPercent = row.FillPercent;
        row.HealFillPercent = row.FillPercent;
        row.BarGivenText = "";
        row.BarHealText = "";
    }
}
