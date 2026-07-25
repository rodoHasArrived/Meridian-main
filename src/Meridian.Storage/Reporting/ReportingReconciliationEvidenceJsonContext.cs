using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Meridian.Reporting;

namespace Meridian.Storage.Reporting;

/// <summary>
/// ADR-014 serialization metadata for current and verified legacy reconciliation evidence.
/// Legacy receipts deliberately have no break-evidence field and are read only to prove recovery
/// is required; they are never reserialized as current receipts.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReportingReconciliationEvidenceReceipt))]
[JsonSerializable(typeof(ReportingReconciliationEvidenceReceipt[]))]
[JsonSerializable(typeof(LegacyReportingReconciliationEvidenceReceipt))]
[JsonSerializable(typeof(LegacyReportingReconciliationEvidenceReceipt[]))]
public sealed partial class ReportingReconciliationEvidenceJsonContext : JsonSerializerContext;

/// <summary>
/// The exact v1 persisted receipt shape. Its omission of break evidence is intentional and must
/// remain distinct from the current receipt so integrity verification cannot manufacture evidence.
/// </summary>
public sealed record LegacyReportingReconciliationEvidenceReceipt(
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string FundId,
    string LedgerBookId,
    string AccountingPeriodId,
    string AccountingBasis,
    DateOnly AsOfDate,
    string SourceCheckpointId,
    string SourceCheckpointHash,
    string ReconciliationCheckpointId,
    string ReconciliationCheckpointHash,
    DateTimeOffset ReconciledAtUtc,
    bool HasOpenBreaks,
    ImmutableArray<string> EvidenceIds,
    string? CompletionCheckpointId = null,
    string? CompletionCheckpointHash = null);
