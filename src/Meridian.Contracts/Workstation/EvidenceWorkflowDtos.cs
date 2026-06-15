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

public sealed partial record EvidenceArtifactRefDto(
    string ArtifactId,
    string Kind,
    string? Path,
    string? Route,
    DateTimeOffset GeneratedAt,
    string? Hash,
    bool Retained,
    string? CanonicalSubjectKind = null,
    string? CanonicalSubjectId = null);

public sealed record EvidenceArtifactCaptureDto(
    string CaptureChannel,
    string? SourceSystem,
    DateTimeOffset? ReceivedAt,
    string? ReceivedBy,
    string? SourceReference,
    string? ReceiptHash);

public sealed record EvidenceArtifactExtractionFieldDto(
    string FieldName,
    string? ExtractedValue,
    string? ExpectedValue,
    decimal? ConfidenceScore,
    string ReviewState,
    EvidenceStatusDto ValidationStatus,
    string? ValidationMessage,
    string? LinkedRecordKind,
    string? LinkedRecordId);

public sealed partial record EvidenceArtifactRefDto
{
    public EvidenceArtifactCaptureDto? Capture { get; init; }
    public IReadOnlyList<EvidenceArtifactExtractionFieldDto> ExtractedFields { get; init; } = [];
}

public sealed partial record EvidenceNodeDto(
    string EvidenceId,
    EvidenceSubjectDto Subject,
    string Kind,
    EvidenceStatusDto Status,
    EvidenceFreshnessDto Freshness,
    string SourceSystem,
    string Summary,
    IReadOnlyList<EvidenceArtifactRefDto> ArtifactRefs,
    IReadOnlyList<string> RelatedWorkItemIds);

public sealed partial record EvidenceNodeDto
{
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

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

public sealed record EvidenceSlaPolicyDto(
    string PolicyId,
    string EvidenceKind,
    string WorkflowKind,
    int FreshnessMinutes,
    EvidenceValidationSeverityDto BreachSeverity,
    bool RequiredForAssurance,
    string Description);

public sealed record EvidenceSlaAssessmentDto(
    string PolicyId,
    string EvidenceId,
    string EvidenceKind,
    string SourceSystem,
    int? AgeMinutes,
    int FreshnessMinutes,
    bool IsBreached,
    EvidenceValidationSeverityDto Severity,
    string Message);

public sealed record EvidenceAssuranceComponentDto(
    string ComponentId,
    string Label,
    int Score,
    EvidenceStatusDto Status,
    string Detail);

public sealed record MeridianAssuranceScoreDto(
    int Score,
    EvidenceStatusDto Status,
    IReadOnlyList<EvidenceAssuranceComponentDto> Components,
    IReadOnlyList<EvidenceSlaAssessmentDto> SlaAssessments);

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceProofChainLayerKindDto>))]
public enum EvidenceProofChainLayerKindDto
{
    Unknown = 0,
    Source = 1,
    Normalization = 2,
    Reconciliation = 3,
    Ledger = 4,
    CapitalAccounts = 5,
    Close = 6,
    Reporting = 7,
    Delivery = 8,
    Audit = 9
}

public sealed record EvidenceProofChainLayerDto(
    EvidenceProofChainLayerKindDto Layer,
    string Label,
    EvidenceStatusDto Status,
    int CoveragePercent,
    IReadOnlyList<string> RequiredEvidenceIds,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> ReadyEvidenceIds,
    IReadOnlyList<string> ReviewEvidenceIds,
    IReadOnlyList<string> MissingEvidenceIds,
    IReadOnlyList<string> EvidenceKinds,
    string Summary);

public sealed record EvidenceProofChainDto(
    int CoveragePercent,
    EvidenceStatusDto Status,
    int CoveredLayerCount,
    int TotalLayerCount,
    IReadOnlyList<EvidenceProofChainLayerDto> Layers,
    string Summary)
{
    public static EvidenceProofChainDto Empty { get; } = new(
        CoveragePercent: 0,
        Status: EvidenceStatusDto.Unknown,
        CoveredLayerCount: 0,
        TotalLayerCount: 0,
        Layers: [],
        Summary: "Proof-chain coverage was not evaluated.");
}

public sealed record EvidenceVaultIdentityDto(
    string VaultId,
    string SubjectKind,
    string SubjectId,
    string ManifestPath,
    string ManifestRoute,
    DateTimeOffset RetainedAt,
    string ContentHashSha256,
    int SchemaVersion,
    string StorageKind)
{
    public IReadOnlyList<EvidenceVaultArtifactDto> Artifacts { get; init; } = [];
    public IReadOnlyList<EvidenceRequestListDto> RequestLists { get; init; } = [];
    public IReadOnlyList<EvidenceSupportRequestDto> SupportRequests { get; init; } = [];
}

public sealed record EvidenceVaultArtifactDto(
    string ArtifactId,
    string Kind,
    string RelativePath,
    string ContentHashSha256,
    long SizeBytes,
    DateTimeOffset RetainedAt,
    string? SourcePath,
    string? SourceRoute,
    string? CanonicalSubjectKind,
    string? CanonicalSubjectId)
{
    public EvidenceArtifactCaptureDto? Capture { get; init; }
    public IReadOnlyList<EvidenceArtifactExtractionFieldDto> ExtractedFields { get; init; } = [];
}

public sealed record EvidenceSupportRequestDto(
    string RequestId,
    string RequestKind,
    string EvidenceId,
    string? EvidenceKind,
    EvidenceValidationSeverityDto Severity,
    string Status,
    string Summary,
    string? SourceSystem,
    string? WorkItemId,
    string? BlockedOutput);

public sealed record EvidenceRequestListDto(
    string RequestListId,
    string RequestListKind,
    string TargetKind,
    string TargetId,
    EvidenceValidationSeverityDto HighestSeverity,
    string Status,
    int RequestCount,
    IReadOnlyList<string> RequestIds,
    IReadOnlyList<string> EvidenceKinds,
    IReadOnlyList<string> BlockedOutputs,
    string Summary);

public sealed record EvidenceVaultRequestListQueryDto(
    string? RequestListKind = null,
    string? TargetKind = null,
    string? TargetId = null,
    string? Status = null,
    string? SubjectKind = null,
    string? SubjectId = null,
    int? MaxResults = null);

public sealed record EvidenceVaultRequestListEntryDto(
    string RequestListId,
    string RequestListKind,
    string TargetKind,
    string TargetId,
    EvidenceValidationSeverityDto HighestSeverity,
    string Status,
    int RequestCount,
    int OpenRequestCount,
    IReadOnlyList<string> RequestIds,
    IReadOnlyList<string> EvidenceKinds,
    IReadOnlyList<string> BlockedOutputs,
    string Summary,
    string VaultId,
    string SubjectKind,
    string SubjectId,
    string ManifestRoute,
    DateTimeOffset RetainedAt,
    IReadOnlyList<EvidenceSupportRequestDto> SupportRequests);

public sealed record EvidenceVaultIntakeRequestDto(
    string SubjectKind,
    string SubjectId,
    string IntakeChannel,
    string FileName,
    string ContentBase64,
    string? ContentType = null,
    string? SourceSystem = null,
    string? SourceReference = null,
    string? ReceivedBy = null,
    string? ExpectedContentHashSha256 = null,
    IReadOnlyList<EvidenceArtifactExtractionFieldDto>? ExtractedFields = null,
    EvidenceSubjectLinkageDto? Linkage = null,
    EvidenceLifecycleMetadataDto? Lifecycle = null);

public sealed record EvidenceVaultIntakeResponseDto(
    string IntakeId,
    string SubjectKind,
    string SubjectId,
    string IntakeChannel,
    string FileName,
    string RelativePath,
    string ContentHashSha256,
    long SizeBytes,
    DateTimeOffset CapturedAt,
    EvidenceArtifactCaptureDto Capture,
    IReadOnlyList<EvidenceArtifactExtractionFieldDto> ExtractedFields,
    EvidenceVaultIdentityDto VaultIdentity);

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
    string? ReconciliationCaseId,
    string? AccountingRecordId = null,
    string? ReportPackDeliveryAttemptId = null,
    string? ReportPackDeliveryPackageId = null);

public sealed record EvidenceVaultLookupRequestDto(
    string? EvidenceSubject,
    string? RunId,
    string? PeriodId,
    string? ReportPackId,
    string? ReconciliationCaseId,
    string? AccountingRecordId = null,
    string? ReportPackDeliveryAttemptId = null,
    string? ReportPackDeliveryPackageId = null);

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
    public IReadOnlyList<EvidenceSlaPolicyDto> SlaPolicies { get; init; } = [];
    public IReadOnlyList<EvidenceSlaAssessmentDto> SlaAssessments { get; init; } = [];
    public MeridianAssuranceScoreDto AssuranceScore { get; init; } = new(
        Score: 0,
        Status: EvidenceStatusDto.Unknown,
        Components: [],
        SlaAssessments: []);
}

public sealed record EvidencePacketDto(
    EvidenceSubjectDto Subject,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EvidenceNodeDto> Nodes,
    IReadOnlyList<EvidenceEdgeDto> Edges,
    EvidenceCompletenessDto Completeness,
    IReadOnlyList<WorkflowActionDto> Actions,
    IReadOnlyList<string> Warnings)
{
    public EvidenceProofChainDto ProofChain { get; init; } = EvidenceProofChainDto.Empty;
}

public sealed record EvidenceGraphDto(
    EvidenceSubjectDto Subject,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EvidenceNodeDto> Nodes,
    IReadOnlyList<EvidenceEdgeDto> Edges,
    IReadOnlyList<string> Warnings)
{
    public EvidenceProofChainDto ProofChain { get; init; } = EvidenceProofChainDto.Empty;
}

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
