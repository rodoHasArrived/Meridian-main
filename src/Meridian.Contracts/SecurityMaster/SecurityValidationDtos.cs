using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityValidationSeverityDto>))]
public enum SecurityValidationSeverityDto
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public sealed record SecurityEvidenceLinkDto(
    string EvidenceKind,
    string EvidenceId,
    string? Route,
    string? Summary);

public sealed record SecurityValidationIssueDto(
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    IReadOnlyList<string> AffectedFields,
    string SuggestedAction,
    IReadOnlyList<SecurityEvidenceLinkDto> EvidenceLinks);

public sealed record SecurityValidationReportDto(
    Guid? SecurityId,
    string Scope,
    DateTimeOffset EvaluatedAtUtc,
    bool HasBlockingIssues,
    int CriticalIssueCount,
    int ErrorIssueCount,
    IReadOnlyList<SecurityValidationIssueDto> Issues);

[JsonConverter(typeof(JsonStringEnumConverter<SecurityValidationWorkflowDto>))]
public enum SecurityValidationWorkflowDto
{
    Unknown = 0,
    StrategyRunPreflight = 1,
    LedgerPosting = 2,
    ReconciliationBreakIntake = 3,
    ReportPackEvidence = 4,
    OverrideApproval = 5
}

public sealed record SecurityValidationSnapshotRequestDto(
    SecurityValidationWorkflowDto Workflow,
    string? WorkflowReference,
    string? Actor,
    string Reason,
    IReadOnlyList<SecurityEvidenceLinkDto> EvidenceLinks);

public sealed record SecurityValidationSnapshotDto(
    Guid SnapshotId,
    Guid? SecurityId,
    string Scope,
    SecurityValidationWorkflowDto Workflow,
    string? WorkflowReference,
    string? Actor,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    string ReportHashSha256,
    SecurityValidationReportDto Report,
    IReadOnlyList<SecurityEvidenceLinkDto> EvidenceLinks);

public sealed record SecurityValidationGateResultDto(
    SecurityValidationWorkflowDto Workflow,
    string? Symbol,
    Guid? SecurityId,
    bool IsResolved,
    bool IsBlocked,
    SecurityValidationReportDto Report,
    SecurityValidationSnapshotDto? Snapshot);
