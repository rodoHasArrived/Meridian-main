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

public sealed record ReportingTemplateMetadata(
    string TemplateId,
    ReportingTemplateFamily Family,
    string Name,
    string Version,
    ImmutableArray<string> Sections,
    ImmutableDictionary<string, string> Tags);

public sealed record ReportingOutputManifest(
    string RunId,
    string TemplateId,
    DateOnly AsOfDate,
    ReportingRunStatus Status,
    ImmutableArray<ReportingSectionManifest> Sections,
    ImmutableArray<string> Artifacts);

public sealed record ReportingSectionManifest(
    string SectionId,
    string DatasetSnapshotId,
    string ReconciliationCheckpointId,
    string Hash);

public sealed record ReportingJobContract(
    string JobId,
    string TemplateId,
    DateOnly AsOfDate,
    ReportingRunTrigger Trigger,
    int MaxRetries,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CronExpression = null);

public sealed record ReportingRunAuditEntry(
    string RunId,
    DateTimeOffset TimestampUtc,
    string Action,
    string Actor,
    string Notes);
