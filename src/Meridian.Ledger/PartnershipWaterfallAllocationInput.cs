namespace Meridian.Ledger;

/// <summary>
/// Period-level inputs for tiered partnership waterfall allocation.
/// </summary>
public sealed record PartnershipWaterfallAllocationInput
{
    public PartnershipWaterfallAllocationInput(
        string FundId,
        string PeriodId,
        DateTimeOffset AsOf,
        decimal DistributableProfit,
        IReadOnlyList<PartnershipInvestor> Investors,
        IReadOnlyList<PartnershipWaterfallTier> Tiers,
        string? Description = null)
    {
        if (string.IsNullOrWhiteSpace(FundId))
            throw new ArgumentException("Fund identifier must not be null or whitespace.", nameof(FundId));
        if (string.IsNullOrWhiteSpace(PeriodId))
            throw new ArgumentException("Period identifier must not be null or whitespace.", nameof(PeriodId));
        if (DistributableProfit < 0m)
            throw new ArgumentOutOfRangeException(nameof(DistributableProfit), DistributableProfit, "Distributable profit cannot be negative.");
        ArgumentNullException.ThrowIfNull(Investors);
        ArgumentNullException.ThrowIfNull(Tiers);
        if (Investors.Count == 0)
            throw new ArgumentException("At least one investor is required for waterfall allocation.", nameof(Investors));
        if (Tiers.Count == 0)
            throw new ArgumentException("At least one waterfall tier is required.", nameof(Tiers));

        this.FundId = FundId.Trim();
        this.PeriodId = PeriodId.Trim();
        this.AsOf = AsOf;
        this.DistributableProfit = DistributableProfit;
        this.Investors = Investors;
        this.Tiers = Tiers;
        this.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
    }

    public string FundId { get; }

    public string PeriodId { get; }

    public DateTimeOffset AsOf { get; }

    public decimal DistributableProfit { get; }

    public IReadOnlyList<PartnershipInvestor> Investors { get; }

    public IReadOnlyList<PartnershipWaterfallTier> Tiers { get; }

    public string? Description { get; }
}
