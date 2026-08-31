namespace Meridian.Ledger;

/// <summary>
/// Capital-gain character of a relieved tax lot. This is the split every downstream statement
/// (1099-B box, K-1 line, investor tax letter) needs, so the ledger carries it on the relief
/// projection rather than leaving it to be re-derived per report.
/// </summary>
public enum TaxCharacter
{
    /// <summary>Held for one year or less; taxed at ordinary rates.</summary>
    ShortTerm,

    /// <summary>Held for more than one year.</summary>
    LongTerm,
}

/// <summary>
/// Holding-period classification for realized gains and losses.
/// </summary>
public static class TaxCharacterRule
{
    /// <summary>
    /// Classifies a disposal by holding period under IRC §1222/§1223: the holding period starts the
    /// day after acquisition and must exceed one year to be long-term, so a lot acquired on
    /// 2025-01-01 becomes long-term only when sold on 2026-01-02 or later.
    /// <para>
    /// The comparison uses calendar years rather than a 365-day count so a holding period spanning
    /// a leap day is not silently promoted a day early.
    /// </para>
    /// </summary>
    /// <param name="holdingPeriodStart">
    /// The lot's effective holding-period start — its acquisition date, or an earlier date carried
    /// forward from a prior wash sale (see <see cref="LedgerTaxLot.HoldingPeriodStart"/>).
    /// </param>
    /// <param name="disposalDate">The sale date.</param>
    public static TaxCharacter Classify(DateOnly holdingPeriodStart, DateOnly disposalDate)
        => disposalDate > holdingPeriodStart.AddYears(1) ? TaxCharacter.LongTerm : TaxCharacter.ShortTerm;

    /// <summary>
    /// Whole days held, measured from the effective holding-period start to the disposal date.
    /// Reported alongside <see cref="Classify"/> so an operator can see how close a lot sits to the
    /// long-term boundary. Negative spans (a disposal dated before the start) clamp to zero.
    /// </summary>
    public static int HoldingPeriodDays(DateOnly holdingPeriodStart, DateOnly disposalDate)
        => Math.Max(0, disposalDate.DayNumber - holdingPeriodStart.DayNumber);
}
