namespace Meridian.Ledger;

/// <summary>
/// Fund-specific valuation policy used when converting daily marks into ledger adjustments.
/// </summary>
public sealed record DailyPortfolioPricingPolicy
{
    public DailyPortfolioPricingPolicy(
        string fundId,
        string policyId,
        string policyName,
        string valuationMethod,
        string approvedBy,
        DateTimeOffset approvedAtUtc,
        FairValueLevel defaultFairValueLevel = FairValueLevel.Unclassified,
        StalePricePolicy? stalePricePolicy = null,
        ValuationFreshnessPolicy? freshnessPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(fundId))
            throw new ArgumentException("Fund identifier must not be null or whitespace.", nameof(fundId));
        if (string.IsNullOrWhiteSpace(policyId))
            throw new ArgumentException("Valuation policy identifier must not be null or whitespace.", nameof(policyId));
        if (string.IsNullOrWhiteSpace(policyName))
            throw new ArgumentException("Valuation policy name must not be null or whitespace.", nameof(policyName));
        if (string.IsNullOrWhiteSpace(valuationMethod))
            throw new ArgumentException("Valuation method must not be null or whitespace.", nameof(valuationMethod));
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("Policy approver must not be null or whitespace.", nameof(approvedBy));

        FundId = fundId.Trim();
        PolicyId = policyId.Trim();
        PolicyName = policyName.Trim();
        ValuationMethod = valuationMethod.Trim();
        ApprovedBy = approvedBy.Trim();
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
        DefaultFairValueLevel = defaultFairValueLevel;
        StalePricePolicy = (stalePricePolicy ?? StalePricePolicy.Of(3, StalePriceHandling.Block)).EnsureValid();
        var maximumAge = StalePricePolicy.Enabled ? StalePricePolicy.MaxAgeDays : 3;
        FreshnessPolicy = freshnessPolicy ?? new ValuationFreshnessPolicy(maximumAge,
            version: FormattableString.Invariant($"{PolicyId}@{ApprovedAtUtc:O}/mark-freshness-v1/{maximumAge}/Medium"));
    }

    public string FundId { get; }

    public string PolicyId { get; }

    public string PolicyName { get; }

    public string ValuationMethod { get; }

    public string ApprovedBy { get; }

    public DateTimeOffset ApprovedAtUtc { get; }

    /// <summary>
    /// Default ASC 820 fair-value level applied to marks that do not classify themselves.
    /// Defaults to <see cref="FairValueLevel.Unclassified"/> for backward compatibility.
    /// </summary>
    public FairValueLevel DefaultFairValueLevel { get; }

    /// <summary>
    /// Legacy configuration adapter. Freshness admission is owned by <see cref="FreshnessPolicy"/>.
    /// </summary>
    public StalePricePolicy StalePricePolicy { get; }

    public ValuationFreshnessPolicy FreshnessPolicy { get; }
}
