namespace Meridian.Ledger;

/// <summary>
/// Balanced journal projection for tiered partnership waterfall allocations.
/// </summary>
public sealed record PartnershipWaterfallAllocationProjection(
    PartnershipWaterfallAllocationInput Input,
    string Description,
    IReadOnlyList<PartnershipWaterfallTierAllocation> TierAllocations,
    IReadOnlyList<PartnershipInvestorAllocation> InvestorAllocations,
    decimal UnallocatedProfit,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public decimal AllocatedProfit => TierAllocations.Sum(static allocation => allocation.AllocatedAmount);

    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;
}
