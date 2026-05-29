namespace Meridian.Ledger;

/// <summary>
/// Balanced journal projection for partnership fees and investor capital allocations.
/// </summary>
public sealed record PartnershipInvestorAllocationProjection(
    PartnershipInvestorAllocationInput Input,
    decimal GrossProfitOrLoss,
    decimal ManagementFee,
    decimal PerformanceFee,
    decimal AllocableProfitOrLoss,
    decimal EndingNavAfterFees,
    decimal UpdatedHighWaterMark,
    string Description,
    IReadOnlyList<PartnershipInvestorAllocation> InvestorAllocations,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;
}
