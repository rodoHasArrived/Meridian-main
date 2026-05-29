namespace Meridian.Ledger;

/// <summary>
/// Governed lifecycle state for a financial report pack.
/// </summary>
public enum LedgerReportPackLifecycleStatus
{
    Draft,
    Validated,
    Approved,
    Published,
    Restated,
    Archived,
}

/// <summary>
/// Audit event for a report-pack lifecycle transition.
/// </summary>
public sealed record LedgerReportPackLifecycleEvent(
    LedgerReportPackLifecycleStatus FromStatus,
    LedgerReportPackLifecycleStatus ToStatus,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    IReadOnlyList<string> EvidenceLinks);

/// <summary>
/// Trace from one report value back to ledger and workflow evidence.
/// </summary>
public sealed record LedgerReportLineProvenance(
    string ArtifactName,
    string RowKey,
    string ValueName,
    decimal Value,
    string? SourceRunId,
    string? SourceSessionId,
    IReadOnlyList<Guid> LedgerJournalEntryIds,
    IReadOnlyList<Guid> LedgerEntryIds,
    IReadOnlyList<string> EvidenceLinks);

/// <summary>
/// Metadata for a published report restatement.
/// </summary>
public sealed record LedgerReportPackRestatement(
    string RestatementId,
    string Reason,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    IReadOnlyList<string> ChangedLineKeys,
    IReadOnlyList<string> EvidenceLinks);
