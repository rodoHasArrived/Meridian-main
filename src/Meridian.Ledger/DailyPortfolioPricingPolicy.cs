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
        StalePricePolicy? stalePricePolicy = null)
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
        StalePricePolicy = (stalePricePolicy ?? StalePricePolicy.Disabled).EnsureValid();
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
    /// Freshness policy applied to daily marks. Defaults to <see cref="StalePricePolicy.Disabled"/>.
    /// </summary>
    public StalePricePolicy StalePricePolicy { get; }
}
