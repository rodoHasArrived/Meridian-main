namespace Meridian.Ledger;

/// <summary>
/// Audited daily portfolio valuation with balanced fair-value adjustment lines.
/// </summary>
public sealed record DailyPortfolioPricingProjection(
    DailyPortfolioPricingInput Input,
    IReadOnlyList<DailyPortfolioPricingLine> Lines,
    IReadOnlyList<DailyPortfolioPricingJournalLine> JournalLines)
{
    public decimal TotalCostBasis => Lines.Sum(static line => line.CostBasis);

    public decimal TotalMarketValue => Lines.Sum(static line => line.MarketValue);

    public decimal NetUnrealizedGainOrLoss => Lines.Sum(static line => line.UnrealizedGainOrLoss);

    /// <summary>Prior carrying value used to calculate this run's ledger delta.</summary>
    public decimal TotalPriorCarryingValue => Lines.Sum(static line => line.PriorCarryingValue);

    /// <summary>
    /// Net ledger movement required to bring the durable carrying value to the current market
    /// value. Unlike <see cref="NetUnrealizedGainOrLoss"/>, this amount is not cumulative.
    /// </summary>
    public decimal NetMarkAdjustment => Lines.Sum(static line => line.MarkAdjustment);

    public decimal TotalDebits => JournalLines.Sum(static line => line.debit);

    public decimal TotalCredits => JournalLines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;
}

/// <summary>
/// One balanced-journal component tied to the exact priced security/account line that produced it.
/// The association keeps per-security drafts deterministic even when several securities share an
/// unrealized gain/loss account.
/// </summary>
public sealed record DailyPortfolioPricingJournalLine(
    DailyPortfolioPricingLine PricingLine,
    LedgerAccount account,
    decimal debit,
    decimal credit,
    LedgerLineDimensionSet? dimensions);
