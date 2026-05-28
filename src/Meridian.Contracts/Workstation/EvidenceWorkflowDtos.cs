using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceStatusDto>))]
public enum EvidenceStatusDto
{
    Unknown = 0,
    Ready = 1,
    ReviewRequired = 2,
    Blocked = 3,
    Stale = 4,
    Missing = 5
}

public sealed record EvidenceSubjectDto(
    string SubjectId,
    string SubjectKind,
    string Label,
    string Workspace,
    string? Route,
    string PageTag);

public sealed record EvidenceFreshnessDto(
    DateTimeOffset? AsOf,
    bool IsStale,
    string? Reason);

public sealed record EvidenceArtifactRefDto(
    string ArtifactId,
    string Kind,
    string? Path,
    string? Route,
    DateTimeOffset GeneratedAt,
    string? Hash,
    bool Retained,
    string? CanonicalSubjectKind = null,
    string? CanonicalSubjectId = null);

public sealed record EvidenceNodeDto(
    string EvidenceId,
    EvidenceSubjectDto Subject,
    string Kind,
    EvidenceStatusDto Status,
    EvidenceFreshnessDto Freshness,
    string SourceSystem,
    string Summary,
    IReadOnlyList<EvidenceArtifactRefDto> ArtifactRefs,
    IReadOnlyList<string> RelatedWorkItemIds);

public sealed record EvidenceEdgeDto(
    string FromId,
    string ToId,
    string Relationship,
    string Reason);

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceValidationSeverityDto>))]
public enum EvidenceValidationSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed record EvidenceValidationIssueDto(
    string Code,
    EvidenceValidationSeverityDto Severity,
    string Message,
    string? EvidenceId = null,
    string? EvidenceKind = null,
    string? SourceSystem = null,
    string? RelatedWorkItemId = null);

public sealed record EvidenceVaultIdentityDto(
    string VaultId,
    string SubjectKind,
    string SubjectId,
    string ManifestPath,
    string ManifestRoute,
    DateTimeOffset RetainedAt,
    string ContentHashSha256,
    int SchemaVersion,
    string StorageKind);

public sealed record EvidenceLifecycleMetadataDto(
    DateTimeOffset? RetainUntil,
    bool LegalHold,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> AccessPolicyTags);

public sealed record EvidenceSubjectLinkageDto(
    string? EvidenceSubject,
    string? RunId,
    string? PeriodId,
    string? ReportPackId,
    string? ReconciliationCaseId);

public sealed record EvidenceVaultLookupRequestDto(
    string? EvidenceSubject,
    string? RunId,
    string? PeriodId,
    string? ReportPackId,
    string? ReconciliationCaseId);

public sealed record EvidenceEndpointErrorDto(
    string Code,
    string Message,
    string? SubjectKind = null,
    string? SubjectId = null,
    string? FileName = null,
    string? VaultId = null);

public sealed record EvidenceCompletenessDto(
    int Score,
    EvidenceStatusDto Status,
    IReadOnlyList<string> RequiredIds,
    IReadOnlyList<string> ReadyIds,
    IReadOnlyList<string> MissingIds,
    IReadOnlyList<string> StaleIds,
    IReadOnlyList<string> BlockingWorkItemIds)
{
    public IReadOnlyList<EvidenceValidationIssueDto> ValidationIssues { get; init; } = [];
    public int BlockingIssueCount { get; init; }
    public int WarningIssueCount { get; init; }
    public IReadOnlyList<string> OrphanEvidenceIds { get; init; } = [];
}

public sealed record EvidencePacketDto(
    EvidenceSubjectDto Subject,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EvidenceNodeDto> Nodes,
    IReadOnlyList<EvidenceEdgeDto> Edges,
    EvidenceCompletenessDto Completeness,
    IReadOnlyList<WorkflowActionDto> Actions,
    IReadOnlyList<string> Warnings);

public sealed record EvidenceGraphDto(
    EvidenceSubjectDto Subject,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EvidenceNodeDto> Nodes,
    IReadOnlyList<EvidenceEdgeDto> Edges,
    IReadOnlyList<string> Warnings);

public sealed record EvidenceTemplateExportSettingsDto(
    int SchemaVersion,
    bool ManifestOnly,
    string DefaultFormat);

public sealed record EvidenceTemplateDto(
    string WorkflowId,
    IReadOnlyList<string> RequiredEvidenceKinds,
    IReadOnlyList<string> OptionalEvidenceKinds,
    bool NoOrphanRule,
    EvidenceTemplateExportSettingsDto ExportSettings);

public sealed record EvidencePacketExportRequest(
    string? RequestedBy,
    string? Reason,
    bool IncludeWarnings = true)
{
    public EvidenceLifecycleMetadataDto? Lifecycle { get; init; }
    public EvidenceSubjectLinkageDto? Linkage { get; init; }
}

public sealed record EvidencePacketExportResponse(
    string SubjectKind,
    string SubjectId,
    DateTimeOffset GeneratedAt,
    string ManifestPath,
    string ManifestRoute,
    int EvidenceCount,
    int WarningCount,
    bool Retained)
{
    public EvidenceVaultIdentityDto? VaultIdentity { get; init; }
}
