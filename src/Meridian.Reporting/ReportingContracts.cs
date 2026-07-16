using System.Collections.Immutable;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public enum ReportingRunTrigger
{
    AdHoc,
    Scheduled
}

public enum ReportingRunStatus
{
    Draft,
    InReview,
    Approved,
    Released,
    Failed
}

public enum ReportingTemplateFamily
{
    InvestorStatement,
    SecFilingPacket,
    ShadowNavPack,
    PerformanceReport,
    HoldingsReport,
    CapitalAccountStatement,
    BoardPacket,
    AuditPackage,
    CertifiedDataset,
    CustomReport
}

public enum ReportingApprovalAction
{
    SubmitForReview,
    Approve,
    Release
}

public sealed record ReportingTemplateMetadata(
    string TemplateId,
    ReportingTemplateFamily Family,
    string Name,
    string Version,
    ImmutableArray<string> Sections,
    ImmutableDictionary<string, string> Tags,
    IReadOnlyList<ReportWriterGridDefinitionDto>? ReportWriterGrids = null,
    ReportAccessPolicyDto? AccessPolicy = null,
    IReadOnlyList<ReportTemplateParameterDefinitionDto>? Parameters = null);

public sealed record ReportingLineageReference(
    string SectionId,
    string DatasetSnapshotId,
    string DatasetSnapshotHash,
    string ReconciliationCheckpointId,
    DateTimeOffset CapturedAtUtc);

public sealed record ReportingOutputManifest(
    string RunId,
    string TemplateId,
    DateOnly AsOfDate,
    ReportingRunStatus Status,
    ImmutableArray<ReportingSectionManifest> Sections,
    ImmutableArray<string> Artifacts,
    int AttemptCount,
    ReportingRunTrigger Trigger,
    string? ScheduleId = null,
    string? FailureReason = null,
    ImmutableArray<ReportingRunReportWriterGridArtifact> ReportWriterGrids = default,
    ImmutableArray<ReportWriterGridRenderDto> RenderedReportWriterGrids = default,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    int? ReportWriterDatasetRowCount = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    ReportAccessPolicyDto? AccessPolicy = null,
    string? RunSeriesId = null,
    int? RunAttemptOrdinal = null,
    string? PriorRunId = null,
    string? RetryReason = null,
    ImmutableArray<ReportWriterGridDiffDto> ReportWriterGridDiffs = default,
    VersionedReportTemplateIdDto? ResolvedTemplate = null,
    ReportingRunParametersDto? ResolvedParameters = null,
    ReportingRunReadinessDto? Readiness = null,
    ReportingOperationalScope? OperationalScope = null,
    ReportingAccessScope? ImmutableAccessScope = null,
    ReportingCertifiedSnapshotScope? CertifiedSnapshot = null);

public sealed record ReportingRunReportWriterGridArtifact(
    string GridId,
    string Title,
    string Kind,
    string Artifact,
    int DimensionCount,
    int MetricCount,
    int FormulaCount);

public sealed record ReportingSectionManifest(
    string SectionId,
    string DatasetSnapshotId,
    string ReconciliationCheckpointId,
    string Hash,
    ReportingLineageReference Lineage);

public sealed record ReportingJobContract(
    string JobId,
    string TemplateId,
    DateOnly AsOfDate,
    ReportingRunTrigger Trigger,
    int MaxRetries,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CronExpression = null,
    string? ScheduleId = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DatasetRows = null,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    ReportAccessPolicyDto? AccessPolicy = null,
    string? RetryReason = null,
    bool AllowRestatement = false,
    VersionedReportTemplateIdDto? ResolvedTemplate = null,
    ReportingRunParametersDto? ResolvedParameters = null,
    ReportingRunReadinessDto? Readiness = null,
    ReportingOperationalScope? OperationalScope = null,
    ReportingAccessScope? ImmutableAccessScope = null,
    ReportingCertifiedSnapshotScope? CertifiedSnapshot = null);

public sealed record ReportingScheduleContract(
    string ScheduleId,
    string TemplateId,
    string CronExpression,
    DateOnly NextAsOfDate,
    DateTimeOffset DueAtUtc,
    int MaxRetries,
    string RequestedBy);

public sealed record ReportingRunAuditEntry(
    string RunId,
    DateTimeOffset TimestampUtc,
    string Action,
    string Actor,
    string Notes);

public sealed record ReportingRunSnapshot(
    ReportingOutputManifest Manifest,
    IReadOnlyList<ReportingRunAuditEntry> AuditTrail,
    DateTimeOffset UpdatedAtUtc);

public interface IReportingRunStore
{
    IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25);
    ReportingOutputManifest? GetManifest(string runId);
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
    Task SaveAsync(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> auditTrail, CancellationToken ct = default);
}

public sealed record ReportingApprovalDecision(
    ReportingApprovalAction Action,
    string Actor,
    string Role,
    string Notes,
    DateTimeOffset DecidedAtUtc);
