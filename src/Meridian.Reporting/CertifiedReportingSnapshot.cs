using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Reporting;

/// <summary>Whether a sealed snapshot has enough authoritative inputs to be certified.</summary>
public enum ReportingSnapshotCertificationStatus
{
    Certifiable,
    NonCertifiable
}

/// <summary>
/// Identifies the ledger source used to build a reporting snapshot. Existing callers that pass
/// only an in-memory ledger remain supported and are labeled as a legacy, non-authoritative source.
/// </summary>
public sealed record ReportingSnapshotSourceContext(
    string SourceKind,
    string? SourceCheckpoint = null,
    bool IsAuthoritative = false);

/// <summary>
/// Deterministic receipt for the exact ledger and Security Master inputs frozen into a report.
/// The receipt hash deliberately excludes report id and generation time so identical inputs yield
/// the same snapshot identity.
/// </summary>
public sealed record ReportingSnapshotReceipt(
    string SnapshotId,
    string ContentHash,
    string HashAlgorithm,
    string SchemaVersion,
    ReportingSnapshotCertificationStatus CertificationStatus,
    string SourceKind,
    string? SourceCheckpoint,
    int LedgerEntryCount,
    int LedgerLineCount,
    int SecurityReferenceCount,
    IReadOnlyList<string> CertificationBlockers);

/// <summary>
/// Immutable report-ready rows and their deterministic source receipt. All external lookups are
/// completed before this object is created; rendering must consume only these frozen values.
/// </summary>
public sealed record CertifiedReportingSnapshot(
    string FundId,
    DateTimeOffset AsOf,
    ReportKind ReportKind,
    IReadOnlyList<EnrichedLedgerRow> Rows,
    ReportingSnapshotReceipt Receipt);

internal sealed record ReportingSnapshotRowInput(
    string AccountName,
    string AccountType,
    string? Symbol,
    decimal NetBalance,
    LedgerDimensionSetDto? Dimensions,
    SecurityMasterReportingReference? SecurityReference);
