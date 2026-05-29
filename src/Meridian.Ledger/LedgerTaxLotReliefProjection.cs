namespace Meridian.Ledger;

/// <summary>
/// Realized-gain/loss projection produced by applying an account-level tax-lot relief method.
/// </summary>
public sealed record LedgerTaxLotReliefProjection(
    LedgerTaxLotReliefInput Input,
    IReadOnlyList<LedgerTaxLotReliefSelection> Selections,
    decimal Proceeds,
    decimal CostBasis,
    decimal RealizedGainOrLoss,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;
}
