namespace Meridian.Ledger;

/// <summary>
/// Audited daily portfolio valuation with balanced fair-value adjustment lines.
/// </summary>
public sealed record DailyPortfolioPricingProjection(
    DailyPortfolioPricingInput Input,
    IReadOnlyList<DailyPortfolioPricingLine> Lines,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> JournalLines)
{
    public decimal TotalCostBasis => Lines.Sum(static line => line.CostBasis);

    public decimal TotalMarketValue => Lines.Sum(static line => line.MarketValue);

    public decimal NetUnrealizedGainOrLoss => Lines.Sum(static line => line.UnrealizedGainOrLoss);

    public decimal TotalDebits => JournalLines.Sum(static line => line.debit);

    public decimal TotalCredits => JournalLines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;
}
