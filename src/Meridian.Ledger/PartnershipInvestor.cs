namespace Meridian.Ledger;

/// <summary>
/// Investor or feeder/SPV participant in a partnership allocation run.
/// </summary>
public sealed record PartnershipInvestor
{
    public PartnershipInvestor(string investorId, string displayName, decimal allocationPercent)
    {
        if (string.IsNullOrWhiteSpace(investorId))
            throw new ArgumentException("Investor identifier must not be null or whitespace.", nameof(investorId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Investor display name must not be null or whitespace.", nameof(displayName));
        if (allocationPercent <= 0m)
            throw new ArgumentOutOfRangeException(nameof(allocationPercent), allocationPercent, "Investor allocation percent must be positive.");

        InvestorId = investorId.Trim();
        DisplayName = displayName.Trim();
        AllocationPercent = allocationPercent;
    }

    public string InvestorId { get; }

    public string DisplayName { get; }

    public decimal AllocationPercent { get; }
}
