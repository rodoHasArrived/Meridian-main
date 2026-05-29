namespace Meridian.Ledger;

/// <summary>
/// Investor-level profit/loss allocation resulting from a partnership accounting projection.
/// </summary>
public sealed record PartnershipInvestorAllocation(
    PartnershipInvestor Investor,
    decimal AllocatedProfitOrLoss,
    LedgerAccount CapitalAccount);
