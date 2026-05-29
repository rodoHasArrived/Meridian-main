namespace Meridian.Ledger;

/// <summary>
/// Governance handoff record for a material shadow-NAV variance that needs review.
/// </summary>
public sealed record ShadowNavOverrideDraft(
    Guid OverrideDraftId,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string Reason,
    string PolicyId,
    string? ReviewerGroup,
    LedgerBookKey ActualLedgerKey,
    LedgerBookKey ShadowLedgerKey,
    DateTimeOffset AsOf,
    decimal ActualNav,
    decimal ShadowNav,
    decimal NavVariance,
    IReadOnlyList<ShadowNavValidationFinding> Findings);
