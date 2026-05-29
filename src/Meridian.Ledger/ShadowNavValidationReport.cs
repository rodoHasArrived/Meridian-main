namespace Meridian.Ledger;

/// <summary>
/// Point-in-time comparison between published actual books and an independent shadow-NAV book.
/// </summary>
public sealed record ShadowNavValidationReport(
    LedgerBookKey ActualLedgerKey,
    LedgerBookKey ShadowLedgerKey,
    DateTimeOffset AsOf,
    string PolicyId,
    string? ReviewerGroup,
    decimal NavTolerance,
    decimal AccountTolerance,
    decimal ActualNav,
    decimal ShadowNav,
    decimal NavVariance,
    IReadOnlyList<ShadowNavValidationFinding> Findings)
{
    public decimal AbsoluteNavVariance => Math.Abs(NavVariance);

    public bool NavWithinTolerance => AbsoluteNavVariance <= NavTolerance;

    public bool AccountVariancesWithinTolerance => Findings.All(static finding => !finding.RequiresOverride);

    public bool RequiresOverride => !NavWithinTolerance || !AccountVariancesWithinTolerance;

    public ShadowNavOverrideDraft CreateOverrideDraft(DateTimeOffset createdAtUtc, string createdBy, string reason)
    {
        if (!RequiresOverride)
            throw new InvalidOperationException("A shadow NAV override draft can only be created when validation requires override.");
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("Override creator must not be null or whitespace.", nameof(createdBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Override reason must not be null or whitespace.", nameof(reason));

        return new ShadowNavOverrideDraft(
            Guid.NewGuid(),
            createdAtUtc.ToUniversalTime(),
            createdBy.Trim(),
            reason.Trim(),
            PolicyId,
            ReviewerGroup,
            ActualLedgerKey,
            ShadowLedgerKey,
            AsOf,
            ActualNav,
            ShadowNav,
            NavVariance,
            Findings);
    }
}
