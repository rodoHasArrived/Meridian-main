namespace Meridian.Ledger;

/// <summary>
/// Audit event for an automated journal approval transition.
/// </summary>
public sealed record AutomatedJournalApprovalEvent(
    AutomatedJournalApprovalStatus FromStatus,
    AutomatedJournalApprovalStatus ToStatus,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    IReadOnlyList<string> EvidenceLinks);
