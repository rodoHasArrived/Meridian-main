namespace Meridian.Ledger;

/// <summary>
/// Investor allocation rule inside one partnership waterfall tier.
/// </summary>
public sealed record PartnershipWaterfallAllocationRule
{
    public PartnershipWaterfallAllocationRule(string investorId, decimal allocationPercent)
    {
        if (string.IsNullOrWhiteSpace(investorId))
            throw new ArgumentException("Investor identifier must not be null or whitespace.", nameof(investorId));
        if (allocationPercent <= 0m || allocationPercent > 1m)
            throw new ArgumentOutOfRangeException(nameof(allocationPercent), allocationPercent, "Waterfall allocation percent must be greater than 0 and less than or equal to 1.");

        InvestorId = investorId.Trim();
        AllocationPercent = allocationPercent;
    }

    public string InvestorId { get; }

    public decimal AllocationPercent { get; }
}
