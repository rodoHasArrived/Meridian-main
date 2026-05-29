using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportKindDto>))]
public enum GovernanceReportKindDto
{
    TrialBalance = 0,
    NavSummary = 1,
    AssetAllocation = 2,
    ReconciliationPack = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportArtifactFormatDto>))]
public enum GovernanceReportArtifactFormatDto
{
    Json = 0,
    Csv = 1,
    Xlsx = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportPackStatusDto>))]
public enum GovernanceReportPackStatusDto
{
    Unknown = 0,
    Draft = 1,
    Generated = 2,
    Validated = 3,
    ReviewRequired = 4,
    Approved = 5,
    Rejected = 6,
    Exported = 7,
    Retained = 8,
    Superseded = 9,
    Restated = 10,
    InReview = 11,
    Published = 12
}

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportValidationSeverityDto>))]
public enum GovernanceReportValidationSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>
/// Version contract for governed local report-pack exports.
/// </summary>
public static class GovernanceReportPackContract
{
    public const string ContractName = "governance-report-pack";
    public const int CurrentSchemaVersion = 2;
    public const int MinimumReadableSchemaVersion = 1;

    public static bool IsReadableSchemaVersion(int schemaVersion) =>
        schemaVersion >= MinimumReadableSchemaVersion
        && schemaVersion <= CurrentSchemaVersion;
}

/// <summary>
/// Query for the shared governance and fund-operations workspace projection.
/// </summary>
/// <remarks>
/// Selection semantics mirror <see cref="FundLedgerQuery"/>:
/// null/empty selections keep the full fund ledger universe for the requested scope,
/// populated selections constrain the ledger universe first, and unknown IDs are treated
/// as no matches.
/// </remarks>
public sealed record FundOperationsWorkspaceQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    string? Currency = null,
    FundLedgerScope ScopeKind = FundLedgerScope.Consolidated,
    string? ScopeId = null,
    IReadOnlyList<string>? SelectedLedgerIds = null);

/// <summary>
/// Asset-class contribution within a NAV attribution summary.
/// </summary>
public sealed record FundNavAssetClassExposureDto(
    string AssetClass,
    decimal NetBalance);

/// <summary>
/// Governance-facing NAV attribution summary for one fund workspace.
/// </summary>
public sealed record FundNavAttributionSummaryDto(
    string Currency,
    decimal TotalNav,
    int ComponentCount,
    int EntityCount,
    int SleeveCount,
    int VehicleCount,
    IReadOnlyList<FundNavAssetClassExposureDto> AssetClassExposure);

/// <summary>
/// Reporting profile metadata exposed to governance workflows.
/// </summary>
public sealed record FundReportingProfileDto(
    string Id,
    string Name,
    string TargetTool,
    string Format,
    string Description,
    bool LoaderScript,
    bool DataDictionary);

/// <summary>
/// Report/export posture for the governance workspace.
/// </summary>
public sealed record FundReportingSummaryDto(
    int ProfileCount,
    IReadOnlyList<string> RecommendedProfiles,
    IReadOnlyList<string> ReportPackTargets,
    IReadOnlyList<FundReportingProfileDto> Profiles,
    string Summary,
    IReadOnlyList<ReportPackWorkflowRecordDto>? WorkflowRecords = null);

/// <summary>
/// Shared governance workspace payload combining ledger, banking, cash, reconciliation,
/// NAV, and reporting posture for one fund profile.
/// </summary>
public sealed record FundOperationsWorkspaceDto(
    string FundProfileId,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset AsOf,
    int RecordedRunCount,
    IReadOnlyList<string> RelatedRunIds,
    FundWorkspaceSummary Workspace,
    FundLedgerSummary Ledger,
    FundLedgerReconciliationSnapshot LedgerReconciliationSnapshot,
    IReadOnlyList<FundAccountSummary> Accounts,
    IReadOnlyList<BankAccountSnapshot> BankSnapshots,
    CashFinancingSummary CashFinancing,
    ReconciliationSummary Reconciliation,
    FundNavAttributionSummaryDto Nav,
    FundReportingSummaryDto Reporting,
    GovernanceLifecycleProjectionDto? Governance = null);

/// <summary>
/// Request to build a preview of a governance report pack for one fund profile.
/// </summary>
public sealed record FundReportPackPreviewRequestDto(
    string FundProfileId,
    GovernanceReportKindDto ReportKind = GovernanceReportKindDto.TrialBalance,
    DateTimeOffset? AsOf = null,
    string? Currency = null);

/// <summary>
/// Asset-class total included in a report-pack preview.
/// </summary>
public sealed record FundReportAssetClassSectionDto(
    string AssetClass,
    decimal Total);

/// <summary>
/// Preview of a generated governance report pack without writing the artifact to disk.
/// </summary>
public sealed record FundReportPackPreviewDto(
    Guid ReportId,
    string FundProfileId,
    string DisplayName,
    GovernanceReportKindDto ReportKind,
    string Currency,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    decimal TotalNetAssets,
    int TrialBalanceLineCount,
    int AssetClassSectionCount,
    IReadOnlyList<FundReportAssetClassSectionDto> AssetClassSections);

/// <summary>
/// Request to generate and persist an immutable governance report pack.
/// </summary>
public sealed record FundReportPackGenerateRequestDto(
    string FundProfileId,
    string AuditActor,
    GovernanceReportKindDto ReportKind = GovernanceReportKindDto.TrialBalance,
    DateTimeOffset? AsOf = null,
    string? Currency = null,
    string? CorrelationId = null,
    string? DecisionRationale = null,
    IReadOnlyList<GovernanceReportArtifactFormatDto>? Formats = null,
    int? ExpectedSchemaVersion = null);

/// <summary>
/// One persisted report-pack artifact and its integrity metadata.
/// </summary>
public sealed record FundReportPackArtifactDto(
    string ArtifactKind,
    GovernanceReportArtifactFormatDto Format,
    string RelativePath,
    long SizeBytes,
    string ChecksumSha256,
    int SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion);

/// <summary>
/// Source lineage captured with a generated governance report pack.
/// </summary>
public sealed record FundReportPackProvenanceDto(
    IReadOnlyList<string> RelatedRunIds,
    int JournalEntryCount,
    int LedgerEntryCount,
    int TrialBalanceLineCount,
    int ReconciliationRunCount,
    int OpenReconciliationBreakCount,
    int SecurityResolvedCount,
    int SecurityMissingCount,
    IReadOnlyList<FundReportPackLineagePointerDto> LineagePointers,
    string SourceSnapshotHash,
    int SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion);

public sealed record FundReportPackLineagePointerDto(
    string ScopeType,
    string ScopeKey,
    string EvidenceType,
    string EvidenceId,
    string? DisplayLabel = null,
    string? Route = null,
    string? SourceSystem = null,
    IReadOnlyList<string>? RelatedEvidenceIds = null,
    int? EvidenceCount = null,
    decimal? Amount = null,
    DateTimeOffset? CapturedAt = null);

public sealed record LedgerAmountProvenanceEvidenceDto(
    string EvidenceType,
    string EvidenceId,
    string? DisplayLabel = null,
    string? Route = null,
    string? SourceSystem = null,
    IReadOnlyList<string>? RelatedEvidenceIds = null,
    int? EvidenceCount = null,
    decimal? Amount = null,
    DateTimeOffset? CapturedAt = null,
    string? ProviderEventId = null,
    string? ProviderEventType = null,
    string? ProviderEvidenceSource = null,
    string? RequiredFeed = null,
    string? SecurityId = null,
    string? LedgerEffectKind = null,
    decimal? PrincipalAmount = null,
    decimal? IncomeAmount = null,
    int? JournalPreviewLineCount = null);

public sealed record LedgerAmountSecurityMasterLinkDto(
    string Symbol,
    string? Route,
    IReadOnlyList<string> RelatedEvidenceIds,
    int EvidenceCount,
    DateTimeOffset? CapturedAt = null,
    Guid? SecurityId = null);

public sealed record LedgerAmountReconciliationCaseDto(
    string CaseId,
    string Status,
    string LifecycleState,
    string? Owner,
    string? Team,
    string? RequiredSignoffRole,
    string? SignoffStatus,
    string? ExceptionRoute,
    string? RecommendedAction,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastUpdatedAt);

public sealed record LedgerAmountReconciliationStateDto(
    int ReconciliationRunCount,
    int OpenBreakCount,
    string? Route,
    IReadOnlyList<string> RelatedCaseIds,
    IReadOnlyList<LedgerAmountReconciliationCaseDto>? RelatedCases = null);

public sealed record LedgerAmountApprovalStateDto(
    GovernanceReportPackStatusDto ReportStatus,
    string? LatestApprovalActor,
    DateTimeOffset? LatestApprovalAt,
    int LifecycleEventCount);

public sealed record LedgerAmountReportUsageDto(
    Guid ReportId,
    string DisplayName,
    GovernanceReportKindDto ReportKind,
    string FundProfileId,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    string Currency,
    string? ReportRoute);

public sealed record LedgerAmountStrategyRunLinkDto(
    string RunId,
    string? DisplayLabel = null,
    string? Route = null,
    string? SourceSystem = null,
    DateTimeOffset? CapturedAt = null,
    bool IsLineScoped = false);

public sealed record LedgerAmountProvenanceDetailDto(
    Guid ReportId,
    string ScopeKey,
    string AccountName,
    string? Symbol,
    decimal Amount,
    string Currency,
    IReadOnlyList<LedgerAmountProvenanceEvidenceDto> Evidence,
    LedgerAmountSecurityMasterLinkDto? SecurityMaster,
    LedgerAmountReconciliationStateDto Reconciliation,
    LedgerAmountApprovalStateDto Approval,
    LedgerAmountReportUsageDto ReportUsage,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LedgerAmountStrategyRunLinkDto>? StrategyRuns = null);

/// <summary>
/// Structured readiness issue captured when a governed report pack is generated.
/// </summary>
public sealed record FundReportPackValidationIssueDto(
    string Code,
    GovernanceReportValidationSeverityDto Severity,
    string Title,
    string Message,
    Guid? AffectedReportId = null,
    string? AffectedSection = null,
    string? AffectedLineItem = null,
    string? AffectedAccount = null,
    string? AffectedSecurity = null,
    DateTimeOffset? AffectedPeriod = null,
    string? SuggestedAction = null,
    string? EvidenceLink = null);

/// <summary>
/// Audit event describing a report-pack lifecycle transition.
/// </summary>
public sealed record FundReportPackLifecycleEventDto(
    GovernanceReportPackStatusDto? FromStatus,
    GovernanceReportPackStatusDto ToStatus,
    DateTimeOffset ChangedAt,
    string Actor,
    string Reason,
    string CorrelationId);

/// <summary>
/// Immutable manifest for a generated governance report pack.
/// </summary>
public sealed record FundReportPackSnapshotDto(
    Guid ReportId,
    string FundProfileId,
    string DisplayName,
    GovernanceReportKindDto ReportKind,
    string Currency,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    decimal TotalNetAssets,
    string AuditActor,
    string CorrelationId,
    string? DecisionRationale,
    FundReportPackProvenanceDto Provenance,
    IReadOnlyList<FundReportPackArtifactDto> Artifacts,
    IReadOnlyList<string> Warnings,
    string ContractName = GovernanceReportPackContract.ContractName,
    int SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion)
{
    public GovernanceReportPackStatusDto Status { get; init; } = GovernanceReportPackStatusDto.Unknown;

    public IReadOnlyList<FundReportPackValidationIssueDto> ValidationIssues { get; init; } = [];

    public IReadOnlyList<FundReportPackLifecycleEventDto> LifecycleEvents { get; init; } = [];
}

/// <summary>
/// Lightweight row used when listing generated governance report packs.
/// </summary>
public sealed record FundReportPackHistoryItemDto(
    Guid ReportId,
    string FundProfileId,
    string DisplayName,
    GovernanceReportKindDto ReportKind,
    string Currency,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    decimal TotalNetAssets,
    string AuditActor,
    int ArtifactCount,
    int WarningCount,
    string RelativeManifestPath,
    int SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion)
{
    public GovernanceReportPackStatusDto Status { get; init; } = GovernanceReportPackStatusDto.Unknown;

    public int ValidationIssueCount { get; init; }

    public int LifecycleEventCount { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportPackWorkflowStateDto>))]
public enum ReportPackWorkflowStateDto
{
    Draft = 0,
    Validated = 1,
    InReview = 2,
    PendingApproval = 2,
    Approved = 3,
    Published = 4,
    Restated = 5,
    Archived = 6
}

public sealed record VersionedReportTemplateIdDto(string Name, int Version);
public sealed record ReportTemplateParameterDefinitionDto(string Name, bool Required);
public sealed record ReportTemplateDefinitionDto(VersionedReportTemplateIdDto TemplateId, string DisplayName, IReadOnlyList<ReportTemplateParameterDefinitionDto> Parameters);
public sealed record RenderReportTemplateRequestDto(VersionedReportTemplateIdDto TemplateId, IReadOnlyDictionary<string, string> Parameters);
public sealed record RenderReportTemplateResponseDto(VersionedReportTemplateIdDto TemplateId, string RenderedContent, IReadOnlyList<string> MissingRequiredParameters);

public sealed record ReportPackAuditEventDto(DateTimeOffset At, string Actor, string Action, ReportPackWorkflowStateDto FromState, ReportPackWorkflowStateDto ToState, string? Note = null);
public sealed record ReportPackEvidenceLinkDto(string EvidenceId, string Label, string? Route, string Source, DateTimeOffset? CapturedAtUtc = null);
public sealed record ReportPackChangedLineDto(string LineKey, string PreviousValue, string CurrentValue, IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null);
public sealed record ReportPackLineProvenanceDto(
    string LineKey,
    string SourceKind,
    string SourceId,
    string EvidenceId,
    string? RunId = null,
    string? LedgerEntryId = null,
    string? ReconciliationCaseId = null,
    string? ReportValue = null,
    string? SourceSessionId = null,
    string? ReconciliationRunId = null);
public sealed record ReportPackPublicationManifestDto(
    string ManifestId,
    string RetainedManifestPath,
    string EvidenceHash,
    string SignedOffBy,
    DateTimeOffset SignedOffAt,
    IReadOnlyList<ReportPackEvidenceLinkDto> EvidenceLinks);
public sealed record ReportPackPublishRequestDto(
    string SignedOffBy,
    string EvidenceHash,
    string ManifestId,
    string RetainedManifestPath,
    IReadOnlyList<ReportPackEvidenceLinkDto> EvidenceLinks,
    string? Note = null);
public sealed record ReportPackCreateRequestDto(
    string FundProfileId,
    string FundAccountId,
    string Period,
    VersionedReportTemplateIdDto TemplateId,
    IReadOnlyList<ReportPackLineProvenanceDto>? LineProvenance = null);
public sealed record ReportPackRestatementMetadataDto(
    string ReasonCode,
    string Approver,
    Guid PriorVersionReportId,
    IReadOnlyList<ReportPackChangedLineDto> ChangedLines,
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null);
public sealed record ReportPackRestateRequestDto(
    string ReasonCode,
    Guid PriorVersionReportId,
    IReadOnlyList<ReportPackChangedLineDto> ChangedLines,
    string? Approver = null);
public sealed record ReportPackWorkflowRecordDto(
    Guid ReportId,
    string FundProfileId,
    string FundAccountId,
    string Period,
    VersionedReportTemplateIdDto TemplateId,
    ReportPackWorkflowStateDto State,
    int Version,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ReportPackAuditEventDto> AuditTrail,
    ReportPackRestatementMetadataDto? Restatement,
    IReadOnlyList<ReportPackLineProvenanceDto>? LineProvenance = null,
    ReportPackPublicationManifestDto? Publication = null);
