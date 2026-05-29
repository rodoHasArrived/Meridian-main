namespace Meridian.Ledger;

/// <summary>
/// Tolerance and governance metadata for comparing an actual ledger to a shadow-NAV ledger.
/// </summary>
public sealed record ShadowNavValidationPolicy
{
    public ShadowNavValidationPolicy(
        decimal navTolerance,
        decimal accountTolerance,
        string policyId = "shadow-nav-default",
        string? reviewerGroup = null)
    {
        if (navTolerance < 0m)
            throw new ArgumentOutOfRangeException(nameof(navTolerance), navTolerance, "NAV tolerance cannot be negative.");
        if (accountTolerance < 0m)
            throw new ArgumentOutOfRangeException(nameof(accountTolerance), accountTolerance, "Account tolerance cannot be negative.");
        if (string.IsNullOrWhiteSpace(policyId))
            throw new ArgumentException("Policy identifier must not be null or whitespace.", nameof(policyId));

        NavTolerance = navTolerance;
        AccountTolerance = accountTolerance;
        PolicyId = policyId.Trim();
        ReviewerGroup = string.IsNullOrWhiteSpace(reviewerGroup) ? null : reviewerGroup.Trim();
    }

    public decimal NavTolerance { get; }

    public decimal AccountTolerance { get; }

    public string PolicyId { get; }

    public string? ReviewerGroup { get; }
}
