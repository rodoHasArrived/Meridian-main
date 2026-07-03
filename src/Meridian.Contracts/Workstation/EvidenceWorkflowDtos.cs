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
    string PageTag,
    Guid? LedgerBookId = null);

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
    string? ReceiptHash,
    EvidenceDocumentIntakeChannelDto? ChannelKind = null);

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

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceDocumentClassificationDto>))]
public enum EvidenceDocumentClassificationDto
{
    Unknown = 0,
    Statement = 1,
    Invoice = 2,
    CapitalNotice = 3,
    CustodianFile = 4,
    BankEvidence = 5,
    ValuationSupport = 6,
    Agreement = 7,
    TaxSupport = 8,
    AuditRequestSupport = 9,
    BankStatement = 10,
    AdminPackage = 11,
    TaxAuditSupport = 12
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceDocumentIntakeChannelDto>))]
public enum EvidenceDocumentIntakeChannelDto
{
    Unknown = 0,
    Upload = 1,
    Email = 2,
    Sftp = 3,
    Api = 4,
    PortalDownload = 5,
    LocalFile = 6,
    ImportedFileReference = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceExtractionStatusDto>))]
public enum EvidenceExtractionStatusDto
{
    NotExtracted = 0,
    Extracted = 1,
    NeedsReview = 2,
    Accepted = 3,
    Rejected = 4,
    Pending = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceDocumentLinkKindDto>))]
public enum EvidenceDocumentLinkKindDto
{
    Unknown = 0,
    Period = 1,
    Portfolio = 2,
    Account = 3,
    Instrument = 4,
    Journal = 5,
    ReconciliationCase = 6,
    ReportLine = 7,
    CloseTask = 8,
    Fund = 9,
    StatementRun = 10,
    StatementImport = 11
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceDocumentReviewStatusDto>))]
public enum EvidenceDocumentReviewStatusDto
{
    Unreviewed = 0,
    NeedsReview = 1,
    Accepted = 2,
    Rejected = 3
}

public sealed record EvidenceDocumentLinkDto(
    EvidenceDocumentLinkKindDto LinkKind,
    string ObjectId,
    string? Label = null,
    string? Route = null,
    string? Relationship = null);

public sealed record EvidenceDocumentReviewStateDto(
    EvidenceDocumentReviewStatusDto Status,
    string? Reviewer = null,
    DateTimeOffset? ReviewedAt = null,
    string? Notes = null)
{
    public IReadOnlyList<EvidenceDocumentConfirmedFieldDto> ConfirmedFields { get; init; } = [];
}

public sealed record EvidenceDocumentConfirmedFieldDto(
    string FieldName,
    string ConfirmedValue,
    string ConfirmedBy,
    DateTimeOffset ConfirmedAt,
    string? SourceFieldName = null,
    string? Notes = null);

public sealed record EvidenceDocumentAuditEventDto(
    DateTimeOffset RecordedAt,
    string Actor,
    string Action,
    string Summary,
    string? CorrelationId = null);

public sealed record EvidenceDocumentAuthorityDto(
    bool CanSupport = true,
    bool CanBlock = true,
    bool CanSuggest = true,
    bool CanLink = true,
    bool CanApprove = false,
    bool CanPost = false,
    bool CanCertify = false,
    bool CanRelease = false,
    string Boundary = "Evidence documents can support, block, suggest, and link; they cannot approve, post, certify, or release.");

public sealed record EvidenceDocumentSourceRecordDto(
    string SourceHashSha256,
    DateTimeOffset ReceivedAt,
    string SourceChannel,
    EvidenceDocumentIntakeChannelDto? ChannelKind,
    string? Actor,
    string? TenantId,
    string? Scope,
    string? SourceSystem = null,
    string? SourceReference = null,
    string? ReceiptHash = null);

public sealed record EvidenceRequestDto(
    string RequestId,
    string RequestKind,
    EvidenceValidationSeverityDto Severity,
    string Status,
    string Summary,
    string? TargetKind = null,
    string? TargetId = null,
    string? BlockedOutput = null);

public sealed record EvidenceDocumentDto(
    string DocumentId,
    string FileName,
    EvidenceDocumentClassificationDto Classification,
    string SourceHashSha256,
    DateTimeOffset ReceivedAt,
    string SourceChannel,
    string? Actor,
    string? TenantId,
    string? Scope,
    EvidenceExtractionStatusDto ExtractionStatus,
    IReadOnlyList<EvidenceDocumentLinkDto> ObjectLinks,
    EvidenceDocumentReviewStateDto ReviewerState,
    IReadOnlyList<EvidenceDocumentAuditEventDto> AuditTrail)
{
    public string? ContentType { get; init; }
    public string? SourceSystem { get; init; }
    public string? SourceReference { get; init; }
    public string? VaultId { get; init; }
    public string? ArtifactId { get; init; }
    public string? ManifestRoute { get; init; }
    public string? ExtractorId { get; init; }
    public EvidenceDocumentIntakeChannelDto? ChannelKind { get; init; }
    public EvidenceDocumentSourceRecordDto? SourceRecord { get; init; }
    public IReadOnlyList<EvidenceArtifactExtractionFieldDto> ExtractedFields { get; init; } = [];
    public EvidenceDocumentAuthorityDto Authority { get; init; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceManifestPackageKindDto>))]
public enum EvidenceManifestPackageKindDto
{
    Unknown = 0,
    EvidencePacket = 1,
    CloseBinder = 2,
    AuditPacket = 3,
    ReportSupportPackage = 4,
    TaxSupportPackage = 5,
    OperationalEventSupportPackage = 6
}

public sealed record EvidenceManifestDto(
    string ManifestId,
    DateTimeOffset FrozenAt,
    string PackageKind,
    string PackageId,
    string ContentHashSha256,
    IReadOnlyList<EvidenceDocumentDto> Documents,
    IReadOnlyList<EvidenceRequestDto> Requests,
    IReadOnlyList<EvidenceDocumentLinkDto> ObjectLinks)
{
    public EvidenceManifestPackageKindDto PackageKindCode { get; init; } = EvidenceManifestPackageKindDto.Unknown;
}

public sealed record EvidenceDocumentExtractionRequestDto(
    string FileName,
    string? ContentType,
    string IntakeChannel,
    string? SourceSystem,
    string? SourceReference,
    IReadOnlyList<EvidenceArtifactExtractionFieldDto> ManualFields);

public sealed record EvidenceDocumentExtractionResultDto(
    EvidenceExtractionStatusDto Status,
    IReadOnlyList<EvidenceArtifactExtractionFieldDto> Fields,
    string ExtractorId,
    string? Summary);

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceDocumentIntakeSourceKindDto>))]
public enum EvidenceDocumentIntakeSourceKindDto
{
    UploadedContent = 0,
    LocalFile = 1,
    ImportedFileReference = 2,
    Email = 3,
    Sftp = 4,
    Api = 5,
    PortalDownload = 6
}

public sealed record EvidenceDocumentIntakeSourceDto(
    EvidenceDocumentIntakeSourceKindDto SourceKind,
    string? Path = null,
    string? Uri = null,
    string? DisplayName = null,
    string? ExpectedContentHashSha256 = null);

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
    public IReadOnlyList<EvidenceDocumentDto> Documents { get; init; } = [];
    public EvidenceManifestDto? ManifestSnapshot { get; init; }
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
    public EvidenceDocumentDto? Document { get; init; }
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

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceRequestListKindDto>))]
public enum EvidenceRequestListKindDto
{
    Unknown = 0,
    Evidence = 1,
    Close = 2,
    Audit = 3,
    Tax = 4,
    ReportPackage = 5,
    OperationalEvent = 6
}

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
    string Summary)
{
    public EvidenceRequestListKindDto RequestListKindCode { get; init; } = EvidenceRequestListKindDto.Unknown;
}

public sealed record EvidenceVaultRequestListQueryDto(
    string? RequestListKind = null,
    EvidenceRequestListKindDto? RequestListKindCode = null,
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
    IReadOnlyList<EvidenceSupportRequestDto> SupportRequests)
{
    public EvidenceRequestListKindDto RequestListKindCode { get; init; } = EvidenceRequestListKindDto.Unknown;
}

public sealed record EvidenceVaultDocumentQueryDto(
    EvidenceDocumentClassificationDto? Classification = null,
    EvidenceDocumentIntakeChannelDto? ChannelKind = null,
    EvidenceExtractionStatusDto? ExtractionStatus = null,
    EvidenceDocumentReviewStatusDto? ReviewStatus = null,
    EvidenceDocumentLinkKindDto? LinkKind = null,
    string? ObjectId = null,
    string? SubjectKind = null,
    string? SubjectId = null,
    string? TenantId = null,
    string? Scope = null,
    int? MaxResults = null);

public sealed record EvidenceVaultDocumentEntryDto(
    EvidenceDocumentDto Document,
    string VaultId,
    string SubjectKind,
    string SubjectId,
    string ManifestRoute,
    DateTimeOffset RetainedAt,
    string StorageKind,
    int OpenRequestCount,
    IReadOnlyList<EvidenceSupportRequestDto> SupportRequests);

public sealed record EvidenceVaultDocumentReviewRequestDto(
    EvidenceDocumentReviewStatusDto Status,
    string Reviewer,
    string? Notes = null,
    EvidenceExtractionStatusDto? ExtractionStatus = null,
    string? CorrelationId = null)
{
    public IReadOnlyList<EvidenceDocumentConfirmedFieldDto> ConfirmedFields { get; init; } = [];
}

public sealed record EvidenceVaultDocumentReviewResponseDto(
    EvidenceVaultDocumentEntryDto Entry,
    EvidenceDocumentAuditEventDto AuditEvent);

public sealed record EvidenceVaultIntakeRequestDto(
    string SubjectKind,
    string SubjectId,
    string IntakeChannel,
    string FileName,
    string? ContentBase64 = null,
    string? ContentType = null,
    string? SourceSystem = null,
    string? SourceReference = null,
    string? ReceivedBy = null,
    string? ExpectedContentHashSha256 = null,
    IReadOnlyList<EvidenceArtifactExtractionFieldDto>? ExtractedFields = null,
    EvidenceSubjectLinkageDto? Linkage = null,
    EvidenceLifecycleMetadataDto? Lifecycle = null)
{
    public EvidenceDocumentClassificationDto Classification { get; init; } = EvidenceDocumentClassificationDto.Unknown;
    public string? Actor { get; init; }
    public string? TenantId { get; init; }
    public string? Scope { get; init; }
    public EvidenceExtractionStatusDto? ExtractionStatus { get; init; }
    public EvidenceDocumentIntakeChannelDto? IntakeChannelKind { get; init; }
    public string? ExtractorId { get; init; }
    public EvidenceDocumentReviewStateDto? ReviewerState { get; init; }
    public IReadOnlyList<EvidenceDocumentLinkDto> ObjectLinks { get; init; } = [];
    public EvidenceDocumentIntakeSourceDto? IntakeSource { get; init; }
}

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
    EvidenceVaultIdentityDto VaultIdentity)
{
    public EvidenceDocumentDto? Document { get; init; }
}

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
