using System.Collections.Immutable;

namespace Meridian.Application.Reporting;

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
    ShadowNavPack
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
    ImmutableDictionary<string, string> Tags);

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
    string? FailureReason = null);

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
    string? ScheduleId = null);

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

public sealed record ReportingApprovalDecision(
    ReportingApprovalAction Action,
    string Actor,
    string Role,
    string Notes,
    DateTimeOffset DecidedAtUtc);
