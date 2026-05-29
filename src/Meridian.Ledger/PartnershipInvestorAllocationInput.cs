namespace Meridian.Ledger;

/// <summary>
/// Period-level partnership accounting inputs for fees, high-water marks, and investor allocations.
/// </summary>
public sealed record PartnershipInvestorAllocationInput
{
    public PartnershipInvestorAllocationInput(
        string FundId,
        string PeriodId,
        DateTimeOffset AsOf,
        decimal BeginningNav,
        decimal EndingNavBeforeFees,
        decimal HighWaterMark,
        decimal ManagementFeeRate,
        decimal PerformanceFeeRate,
        IReadOnlyList<PartnershipInvestor> Investors,
        string? Description = null)
    {
        if (string.IsNullOrWhiteSpace(FundId))
            throw new ArgumentException("Fund identifier must not be null or whitespace.", nameof(FundId));
        if (string.IsNullOrWhiteSpace(PeriodId))
            throw new ArgumentException("Period identifier must not be null or whitespace.", nameof(PeriodId));
        if (BeginningNav < 0m)
            throw new ArgumentOutOfRangeException(nameof(BeginningNav), BeginningNav, "Beginning NAV cannot be negative.");
        if (EndingNavBeforeFees < 0m)
            throw new ArgumentOutOfRangeException(nameof(EndingNavBeforeFees), EndingNavBeforeFees, "Ending NAV before fees cannot be negative.");
        if (HighWaterMark < 0m)
            throw new ArgumentOutOfRangeException(nameof(HighWaterMark), HighWaterMark, "High-water mark cannot be negative.");
        if (ManagementFeeRate < 0m || ManagementFeeRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(ManagementFeeRate), ManagementFeeRate, "Management fee rate must be between 0 and 1.");
        if (PerformanceFeeRate < 0m || PerformanceFeeRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(PerformanceFeeRate), PerformanceFeeRate, "Performance fee rate must be between 0 and 1.");

        ArgumentNullException.ThrowIfNull(Investors);
        if (Investors.Count == 0)
            throw new ArgumentException("At least one investor is required for partnership allocation.", nameof(Investors));

        this.FundId = FundId.Trim();
        this.PeriodId = PeriodId.Trim();
        this.AsOf = AsOf;
        this.BeginningNav = BeginningNav;
        this.EndingNavBeforeFees = EndingNavBeforeFees;
        this.HighWaterMark = HighWaterMark;
        this.ManagementFeeRate = ManagementFeeRate;
        this.PerformanceFeeRate = PerformanceFeeRate;
        this.Investors = Investors;
        this.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
    }

    public string FundId { get; }

    public string PeriodId { get; }

    public DateTimeOffset AsOf { get; }

    public decimal BeginningNav { get; }

    public decimal EndingNavBeforeFees { get; }

    public decimal HighWaterMark { get; }

    public decimal ManagementFeeRate { get; }

    public decimal PerformanceFeeRate { get; }

    public IReadOnlyList<PartnershipInvestor> Investors { get; }

    public string? Description { get; }
}
