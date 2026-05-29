namespace Meridian.Ledger;

/// <summary>
/// Allocation to one investor from one waterfall tier.
/// </summary>
public sealed record PartnershipWaterfallTierAllocation(
    string TierId,
    string TierDescription,
    PartnershipInvestor Investor,
    decimal AllocatedAmount,
    LedgerAccount CapitalAccount);
