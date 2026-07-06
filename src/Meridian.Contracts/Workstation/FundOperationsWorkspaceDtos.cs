using System.Text.Json.Serialization;
using Meridian.Contracts.DirectLending;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportKindDto>))]
public enum GovernanceReportKindDto
{
    TrialBalance = 0,
    NavSummary = 1,
    AssetAllocation = 2,
    ReconciliationPack = 3,
    PerformanceReport = 4,
    HoldingsReport = 5,
    CapitalAccountStatement = 6,
    InvestorStatement = 7,
    BoardPacket = 8,
    AuditPackage = 9,
    CertifiedDataset = 10,
    CustomReport = 11
}

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportArtifactFormatDto>))]
public enum GovernanceReportArtifactFormatDto
{
    Json = 0,
    Csv = 1,
    Xlsx = 2,
    Html = 3,
    Pdf = 4
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

[JsonConverter(typeof(JsonStringEnumConverter<FundAuditEvidenceCategoryKeyDto>))]
public enum FundAuditEvidenceCategoryKeyDto
{
    SourceRecords = 0,
    NormalizedActivity = 1,
    ReconciliationCases = 2,
    LedgerEvidence = 3,
    Approvals = 4,
    ReportPack = 5,
    Exports = 6,
    RestatementLineage = 7
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
/// Query for the shared Accounting and fund-operations workspace projection.
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
/// Accounting-facing NAV attribution summary for one fund workspace.
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
/// Reporting profile metadata exposed to Accounting and Reporting workflows.
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
/// Branding and styling options applied to governed report-pack documents.
/// </summary>
public sealed record ReportBrandingThemeDto(
    string ThemeId,
    string Name,
    string FirmName,
    string PrimaryColor,
    string AccentColor,
    string TextColor,
    string BackgroundColor,
    string? LogoUri = null,
    string? FooterText = null,
    string? Disclaimer = null,
    bool IsBuiltIn = true);

[JsonConverter(typeof(JsonStringEnumConverter<PortfolioReportingCutKindDto>))]
public enum PortfolioReportingCutKindDto
{
    Fund = 0,
    Strategy = 1,
    UserTag = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<PortfolioReportingLiveViewStateDto>))]
public enum PortfolioReportingLiveViewStateDto
{
    LiveLinked = 0,
    SourceBacked = 1,
    Stale = 2,
    Blocked = 3
}

/// <summary>
/// Portfolio reporting row for exposure, cash, P&amp;L, and shadow-NAV cuts.
/// </summary>
public sealed record PortfolioReportingCutDto(
    string CutId,
    string Label,
    PortfolioReportingCutKindDto Kind,
    string Currency,
    decimal GrossExposure,
    decimal NetExposure,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal TotalCash,
    decimal PendingSettlement,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    decimal ShadowNav,
    decimal ShadowNavVariance,
    int SourceCount,
    IReadOnlyList<string> Tags,
    DateTimeOffset AsOf,
    string? EvidenceRoute = null,
    string? ShadowNavNote = null,
    string? VersionStamp = null);

/// <summary>
/// Server-owned freshness policy evidence for a portfolio reporting live view.
/// </summary>
public sealed record PortfolioReportingLiveViewFreshnessPolicyDto(
    string PolicyName,
    DateTimeOffset EvaluatedAtUtc,
    int? SourceAgeSeconds,
    int LiveLinkWindowSeconds,
    int StaleWindowSeconds,
    bool IsWithinLiveLinkWindow,
    bool IsBeyondStaleWindow,
    string Reason);

/// <summary>
/// Reporting-owned pointer to live portfolio summary, liquidity, and cash-ladder evidence.
/// </summary>
public sealed record PortfolioReportingLiveViewDto(
    string ViewId,
    string Label,
    PortfolioReportingCutKindDto Kind,
    PortfolioReportingLiveViewStateDto State,
    string Currency,
    decimal GrossExposure,
    decimal NetExposure,
    decimal TotalCash,
    decimal PendingSettlement,
    decimal TotalPnl,
    decimal ShadowNav,
    DateTimeOffset AsOf,
    DateTimeOffset? SourceAsOfUtc,
    int SourceCount,
    string Route,
    string LiquiditySummary,
    string CashLadderSummary,
    string TelemetrySummary,
    IReadOnlyList<string> Tags,
    string? CashLadderRoute = null,
    string? VersionStamp = null,
    IReadOnlyList<string>? ReadinessBlockers = null,
    DateTimeOffset? MarketTickAsOfUtc = null,
    int? MarketTickAgeSeconds = null,
    long? MarketTickSequence = null,
    string? MarketDataProvider = null,
    string? TickFreshnessSummary = null,
    bool IsMarketTickLinked = false,
    PortfolioReportingLiveViewFreshnessPolicyDto? FreshnessPolicy = null);

[JsonConverter(typeof(JsonStringEnumConverter<PortfolioReportingPnlSlicePeriodDto>))]
public enum PortfolioReportingPnlSlicePeriodDto
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3
}

/// <summary>
/// Period-scoped P&amp;L row derived from retained portfolio run timestamps.
/// </summary>
public sealed record PortfolioReportingPnlSliceDto(
    string SliceId,
    PortfolioReportingPnlSlicePeriodDto Period,
    string Label,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    decimal PriorTotalPnl,
    decimal PnlChange,
    int SourceCount,
    DateTimeOffset AsOf,
    string Route,
    string ReadinessSummary,
    IReadOnlyList<string> Tags,
    string? VersionStamp = null);

[JsonConverter(typeof(JsonStringEnumConverter<PortfolioReportingAnalyticsKindDto>))]
public enum PortfolioReportingAnalyticsKindDto
{
    TopWinner = 0,
    TopLaggard = 1,
    Contribution = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<PortfolioReportingAnalyticsScopeDto>))]
public enum PortfolioReportingAnalyticsScopeDto
{
    Security = 0,
    Strategy = 1,
    AssetClass = 2
}

/// <summary>
/// Top-N and contribution analytics row derived from retained portfolio P&amp;L sources.
/// </summary>
public sealed record PortfolioReportingAnalyticsRowDto(
    string AnalyticsId,
    PortfolioReportingAnalyticsKindDto Kind,
    PortfolioReportingAnalyticsScopeDto Scope,
    int Rank,
    string Label,
    string? Symbol,
    string? Classification,
    string Currency,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    decimal ContributionPercent,
    decimal HeatMapIntensity,
    int SourceCount,
    DateTimeOffset AsOf,
    string Route,
    string ReadinessSummary,
    IReadOnlyList<string> Tags,
    string? VersionStamp = null);

[JsonConverter(typeof(JsonStringEnumConverter<CrossFundReportingConsolidationScopeDto>))]
public enum CrossFundReportingConsolidationScopeDto
{
    Company = 0,
    Fund = 1,
    Entity = 2
}

/// <summary>
/// Cross-fund Reporting consolidation row for aggregated exposures and metrics.
/// </summary>
public sealed record CrossFundReportingConsolidationDto(
    string ConsolidationId,
    string Label,
    CrossFundReportingConsolidationScopeDto Scope,
    string Currency,
    bool IsReady,
    int FundCount,
    int EntityCount,
    int AccountCount,
    int RunCount,
    decimal GrossExposure,
    decimal NetExposure,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal TotalCash,
    decimal PendingSettlement,
    decimal TotalPnl,
    decimal ShadowNav,
    decimal ShadowNavVariance,
    int SourceCount,
    DateTimeOffset AsOf,
    string Route,
    string ReadinessSummary,
    IReadOnlyList<string> Tags,
    string? VersionStamp = null);

[JsonConverter(typeof(JsonStringEnumConverter<StructuredReportingExportPurposeDto>))]
public enum StructuredReportingExportPurposeDto
{
    Regulatory = 0,
    DataWarehouse = 1,
    InvestmentDecision = 2
}

/// <summary>
/// Structured export descriptor for governed downstream reporting consumers.
/// </summary>
public sealed record StructuredReportingExportDto(
    string ExportId,
    string Label,
    StructuredReportingExportPurposeDto Purpose,
    GovernanceReportArtifactFormatDto Format,
    string Dataset,
    string Consumer,
    int SchemaVersion,
    int RowCount,
    int FieldCount,
    int SourceCount,
    string Currency,
    DateTimeOffset AsOf,
    bool IsReady,
    string RetainedPath,
    string Route,
    string? DataDictionaryRoute = null,
    string? ValidationSummary = null,
    string? EvidenceRoute = null,
    string? VersionStamp = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? ReadinessBlockers = null,
    string? RetainedManifestPath = null,
    string? IntegrityHashSha256 = null,
    string? IntegritySummary = null,
    int? RowLineageCount = null);

public sealed record StructuredReportingExportColumnDto(
    string Name,
    string DataType,
    string? Description = null);

public sealed record StructuredReportingExportDataDictionaryFieldDto(
    string Name,
    string DataType,
    string? Description,
    int Ordinal,
    bool Required = true);

public sealed record StructuredReportingExportValidationCheckDto(
    string CheckId,
    string Status,
    string Detail);

public sealed record StructuredReportingExportRowLineageDto(
    int RowNumber,
    string RowKey,
    string RowHashSha256);

public sealed record StructuredReportingExportPayloadDto(
    StructuredReportingExportDto Export,
    IReadOnlyList<StructuredReportingExportColumnDto> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<StructuredReportingExportDataDictionaryFieldDto>? DataDictionary = null,
    IReadOnlyList<StructuredReportingExportValidationCheckDto>? ValidationChecks = null,
    string? GeneratedByPrincipalId = null,
    string? GeneratedForCompanyId = null,
    IReadOnlyList<string>? GeneratedForGroupPrincipalIds = null,
    IReadOnlyList<StructuredReportingExportRowLineageDto>? RowLineage = null);

public sealed record StructuredReportingExportRequestDto(
    string FundProfileId,
    string ExportId,
    DateTimeOffset? AsOf = null,
    string? Currency = null);

/// <summary>
/// Report/export posture for the Accounting workspace.
/// </summary>
public sealed record FundReportingSummaryDto(
    int ProfileCount,
    IReadOnlyList<string> RecommendedProfiles,
    IReadOnlyList<WorkstationReportPackDistributionPayload> ReportPackDistributions,
    IReadOnlyList<FundReportingProfileDto> Profiles,
    string Summary,
    IReadOnlyList<ReportPackWorkflowRecordDto>? WorkflowRecords = null,
    IReadOnlyList<ReportingScheduleRecordDto>? Schedules = null,
    IReadOnlyList<ReportPackDeliveryAttemptDto>? DeliveryAttempts = null,
    IReadOnlyList<PortfolioReportingCutDto>? PortfolioCuts = null,
    IReadOnlyList<StructuredReportingExportDto>? StructuredExports = null,
    IReadOnlyList<ReportBrandingThemeDto>? BrandingThemes = null,
    IReadOnlyList<PortfolioReportingLiveViewDto>? LivePortfolioViews = null,
    IReadOnlyList<CrossFundReportingConsolidationDto>? CrossFundConsolidations = null,
    IReadOnlyList<PortfolioReportingPnlSliceDto>? PnlSlices = null,
    IReadOnlyList<PortfolioReportingAnalyticsRowDto>? AnalyticsRows = null,
    string? FundProfileId = null,
    IReadOnlyList<ReportingScheduleDeliveryPlanDto>? ScheduleDeliveryPlans = null,
    FinancialRecordExplorerDto? ReportLineProvenanceExplorer = null,
    IReadOnlyList<WorkstationReportWriterDatasetSourcePayload>? ReportWriterDatasetSources = null,
    WorkstationReportAccessAuditSummaryDto? AccessAudit = null,
    IReadOnlyList<WorkstationReportingDailyWorkItemDto>? DailyWork = null,
    IReadOnlyList<ReportingStarterKitDto>? StarterKits = null,
    ReportingStarterKitStateDto? StarterKitState = null);

/// <summary>
/// Shared Accounting workspace payload combining ledger, banking, cash, reconciliation,
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
    GovernanceLifecycleProjectionDto? Governance = null,
    DirectLendingOperationsReadModelDto? DirectLendingOperations = null);

/// <summary>
/// Request to build a preview of a governed report pack for one fund profile.
/// </summary>
public sealed record FundReportPackPreviewRequestDto(
    string FundProfileId,
    GovernanceReportKindDto ReportKind = GovernanceReportKindDto.TrialBalance,
    DateTimeOffset? AsOf = null,
    string? Currency = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingThemeOverride = null);

/// <summary>
/// Asset-class total included in a report-pack preview.
/// </summary>
public sealed record FundReportAssetClassSectionDto(
    string AssetClass,
    decimal Total);

public sealed record FundAuditEvidenceCategorySummaryDto(
    FundAuditEvidenceCategoryKeyDto Key,
    string Label,
    bool IsComplete,
    string Status,
    int EvidenceCount,
    IReadOnlyList<string> EvidenceIds,
    string? Route = null);

public sealed record FundAuditPackReadinessDto(
    bool IsComplete,
    double GeneratedInSeconds,
    int SlaTargetSeconds,
    bool SlaMet,
    IReadOnlyList<FundAuditEvidenceCategoryKeyDto> MissingEvidenceCategories,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FundAuditEvidenceCategorySummaryDto> EvidenceCategorySummaries);

/// <summary>
/// Preview of a generated governed report pack without writing the artifact to disk.
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
    IReadOnlyList<FundReportAssetClassSectionDto> AssetClassSections,
    ReportBrandingThemeDto? BrandingTheme = null);

/// <summary>
/// Request to generate and persist an immutable governed report pack.
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
    int? ExpectedSchemaVersion = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingThemeOverride = null);

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
/// Source lineage captured with a generated governed report pack.
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
    int? JournalPreviewLineCount = null,
    DateOnly? EffectiveDate = null,
    decimal? Factor = null,
    decimal? CashAmount = null,
    string? Currency = null);

public sealed record LedgerAmountSecurityMasterLinkDto(
    string Symbol,
    string? Route,
    IReadOnlyList<string> RelatedEvidenceIds,
    int EvidenceCount,
    DateTimeOffset? CapturedAt = null,
    Guid? SecurityId = null,
    string? DisplayName = null,
    string? SourceSystem = null,
    string? EvidenceId = null);

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
    DateTimeOffset LastUpdatedAt,
    string? Severity = null,
    decimal Variance = 0m,
    decimal? ToleranceBand = null,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAt = null,
    string? ResolvedBy = null,
    DateTimeOffset? ResolvedAt = null,
    string? ResolutionNote = null,
    int SignoffCount = 0,
    string? SlaPolicyId = null,
    DateTimeOffset? SlaDueAt = null,
    bool SlaBreached = false,
    ReconciliationCaseSlaState SlaState = ReconciliationCaseSlaState.OnTrack,
    string? AgeBand = null,
    double BusinessAgeHours = 0,
    string? SignedOffBy = null,
    DateTimeOffset? SignedOffAt = null,
    string? SignOffNote = null);

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
/// Immutable manifest for a generated governed report pack.
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
    int SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion,
    ReportBrandingThemeDto? BrandingTheme = null)
{
    public GovernanceReportPackStatusDto Status { get; init; } = GovernanceReportPackStatusDto.Unknown;

    public IReadOnlyList<FundReportPackValidationIssueDto> ValidationIssues { get; init; } = [];

    public IReadOnlyList<FundReportPackLifecycleEventDto> LifecycleEvents { get; init; } = [];

    public FundAuditPackReadinessDto? AuditPackReadiness { get; init; }
}

/// <summary>
/// Lightweight row used when listing generated governed report packs.
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

    public FundAuditPackReadinessDto? AuditPackReadiness { get; init; }
}

public sealed record FundReportPackEvidenceBundleSourceLinkDto(
    string SourceType,
    string SourceId,
    string Label,
    string? Route,
    string SourceSystem,
    IReadOnlyList<string> RelatedEvidenceIds,
    DateTimeOffset? CapturedAtUtc = null);

public sealed record FundReportPackEvidenceBundleApprovalDto(
    GovernanceReportPackStatusDto? FromStatus,
    GovernanceReportPackStatusDto ToStatus,
    DateTimeOffset ApprovedAtUtc,
    string Actor,
    string Reason,
    string CorrelationId);

public sealed record FundReportPackEvidenceBundleDto(
    Guid BundleId,
    Guid ReportId,
    string FundProfileId,
    string DisplayName,
    GovernanceReportKindDto ReportKind,
    string Currency,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExportedAtUtc,
    string ExportedBy,
    string ManifestPath,
    string ProvenancePath,
    string SourceSnapshotHash,
    FundReportPackSnapshotDto Manifest,
    FundReportPackProvenanceDto Provenance,
    IReadOnlyList<FundReportPackEvidenceBundleApprovalDto> Approvals,
    IReadOnlyList<FundReportPackEvidenceBundleSourceLinkDto> SourceLinks,
    IReadOnlyList<FundReportPackArtifactDto> Artifacts,
    IReadOnlyList<string> Warnings,
    FundReportPackArtifactDto? BundleArtifact = null,
    string ContractName = GovernanceReportPackContract.ContractName,
    int SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion);

[JsonConverter(typeof(JsonStringEnumConverter<ReportPackDeliveryStateDto>))]
public enum ReportPackDeliveryStateDto
{
    Queued = 0,
    Delivered = 1,
    Failed = 2,
    Retried = 3,
    Blocked = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportPackDeliveryModeDto>))]
public enum ReportPackDeliveryModeDto
{
    EmailLink = 0,
    SecurePortal = 1,
    EvidenceVault = 2,
    InternalRoute = 3
}

public sealed record ReportPackDeliveryArtifactDto(
    GovernanceReportArtifactFormatDto Format,
    string ArtifactName,
    string ContentType,
    string RetainedPath,
    long ByteSize,
    string EvidenceId,
    string ChecksumSha256 = "",
    string VersionStamp = "",
    string? DownloadRoute = null);

public sealed record ReportPackDeliveryAccessLinkDto(
    string Kind,
    string Label,
    string Href,
    bool RequiresToken,
    DateTimeOffset? ExpiresAtUtc = null,
    string? Description = null);

public sealed record ReportPackDeliveryNotificationDto(
    string NotificationId,
    string Channel,
    string Recipient,
    string RecipientRole,
    ReportPackDeliveryModeDto DeliveryMode,
    string Subject,
    string Body,
    string Href,
    bool RequiresToken,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    string Status = "Ready");

public sealed record ReportPackDeliveryRecipientDto(
    string DistributionId,
    string Recipient,
    string RecipientRole,
    string Channel);

public sealed record ReportPackDeliveryApprovalStepDto(
    DateTimeOffset At,
    string Actor,
    string Action,
    ReportPackWorkflowStateDto FromState,
    ReportPackWorkflowStateDto ToState,
    string? Note = null);

public sealed record ReportPackDeliveryEvidencePacketDto(
    string PacketId,
    string PacketKind,
    string PackageId,
    Guid ReportId,
    string FundProfileId,
    string FundAccountId,
    string Period,
    IReadOnlyList<string> PackageContents,
    IReadOnlyList<string> SupportEvidenceIds,
    IReadOnlyList<ReportPackDeliveryRecipientDto> RecipientList,
    string EntitlementScope,
    IReadOnlyList<ReportPackDeliveryApprovalStepDto> ApprovalChain,
    string DatasetVersion,
    string TemplateVersion,
    string DeliveryChannel,
    DateTimeOffset DeliveredAtUtc,
    IReadOnlyList<ReportPackEvidenceLinkDto> DeliveryEvidence,
    IReadOnlyList<string> RequestHistory,
    string? AmendmentReason = null,
    string? RestatementLineage = null,
    IReadOnlyList<string>? AuditEventReferences = null,
    IReadOnlyList<string>? BlockedDownstreamOutputs = null);

public sealed record ReportPackDeliveryPackageDto(
    string PackageId,
    Guid ReportId,
    string DistributionId,
    ReportPackDeliveryModeDto DeliveryMode,
    string SecureLink,
    string PortalRoute,
    IReadOnlyList<GovernanceReportArtifactFormatDto> Formats,
    IReadOnlyList<ReportPackDeliveryArtifactDto> Artifacts,
    DateTimeOffset CreatedAtUtc,
    string RetainedManifestPath,
    string? PublicationEvidenceHash = null,
    string? IntegritySummary = null,
    string? ReportingRunId = null,
    string? ReportingTemplateId = null,
    string? ReportingScheduleId = null,
    IReadOnlyList<string>? SourceArtifacts = null,
    string? PublicationManifestId = null,
    string? PublicationRetainedManifestPath = null,
    string? PublicationSignedOffBy = null,
    string? PublicationSignedOffRole = null,
    string? PublicationSignOffReason = null,
    string? PublicationSignOffContext = null,
    DateTimeOffset? PublicationSignedOffAtUtc = null,
    IReadOnlyList<ReportPackEvidenceLinkDto>? PublicationEvidenceLinks = null,
    IReadOnlyList<ReportPackLineProvenanceDto>? LineProvenance = null,
    string? RestatementReasonCode = null,
    Guid? RestatementPriorVersionReportId = null,
    string? RestatementApprover = null,
    IReadOnlyList<ReportPackChangedLineDto>? RestatementChangedLines = null,
    IReadOnlyList<ReportPackEvidenceLinkDto>? RestatementEvidenceLinks = null,
    ReportPackDeliveryEvidencePacketDto? DeliveryEvidencePacket = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    string? ReportingRunAsOfDate = null,
    string? ReportingRunStatus = null,
    string? ReportingRunTrigger = null,
    int? ReportingRunAttemptCount = null,
    int? ReportingRunSectionCount = null,
    int? ReportingRunLineageLinkedSections = null,
    string? DeliveryAccessSummary = null,
    string? DeliveryChannelSummary = null,
    string? DownloadSummary = null,
    DateTimeOffset? AccessExpiresAtUtc = null,
    IReadOnlyList<ReportPackDeliveryAccessLinkDto>? AccessLinks = null,
    IReadOnlyList<WorkstationGeneratedReportWriterGridPayload>? GeneratedReportWriterGrids = null,
    IReadOnlyList<ReportWriterGridRenderDto>? RenderedReportWriterGrids = null,
    IReadOnlyList<ReportPackDeliveryNotificationDto>? Notifications = null,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    int? ReportWriterDatasetRowCount = null);

public sealed record ReportPackDeliveryAttemptDto(
    Guid AttemptId,
    Guid ReportId,
    string DistributionId,
    string Recipient,
    string RecipientRole,
    string Channel,
    ReportPackDeliveryStateDto State,
    DateTimeOffset AttemptedAtUtc,
    string Actor,
    int AttemptNumber,
    string DeliveryReference,
    string? Note = null,
    string? FailureReason = null,
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null,
    ReportPackDeliveryPackageDto? Package = null);

public sealed record ReportPackDeliveryRequestDto(
    string DistributionId,
    string? Actor = null,
    string? DeliveryReference = null,
    string? Note = null,
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<GovernanceReportArtifactFormatDto>? Formats = null,
    ReportPackDeliveryModeDto? DeliveryMode = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record ReportPackDeliveryFailureRequestDto(
    string DistributionId,
    string FailureReason,
    string? Actor = null,
    string? DeliveryReference = null,
    string? Note = null,
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record ReportPackDeliveryHistoryDto(
    Guid ReportId,
    IReadOnlyList<ReportPackDeliveryAttemptDto> Attempts);

public sealed record ReportingScheduleDeliveryTargetDto(
    string DistributionId,
    IReadOnlyList<GovernanceReportArtifactFormatDto>? Formats = null,
    ReportPackDeliveryModeDto? DeliveryMode = null,
    string? Note = null);

public sealed record ReportingScheduleDeliveryPlanDto(
    string PlanId,
    string ScheduleId,
    string TemplateId,
    string DistributionId,
    string Recipient,
    string RecipientRole,
    string Channel,
    ReportPackDeliveryModeDto DeliveryMode,
    IReadOnlyList<GovernanceReportArtifactFormatDto> Formats,
    bool IsReady,
    string ReadinessSummary,
    string Route,
    DateTimeOffset DueAtUtc,
    DateOnly NextAsOfDate,
    string Owner,
    string? Note = null,
    Guid? LastDeliveryAttemptId = null,
    ReportPackDeliveryStateDto? LastDeliveryState = null,
    DateTimeOffset? LastDeliveryAtUtc = null,
    string? LastDeliveryPackageRoute = null,
    string? LastDeliverySecureLink = null,
    IReadOnlyList<ReportPackDeliveryAccessLinkDto>? LastDeliveryAccessLinks = null,
    DateTimeOffset? LastDeliveryAccessExpiresAtUtc = null,
    string? LastDeliveryAccessSummary = null,
    string? LastDeliveryChannelSummary = null,
    string? LastDeliveryDownloadSummary = null,
    int LastDeliveryNotificationCount = 0,
    string? LastDeliveryNotificationSummary = null,
    int LastDeliveryGeneratedReportWriterGridCount = 0,
    int LastDeliveryRenderedReportWriterGridCount = 0,
    string? LastDeliveryReportWriterDatasetSummary = null,
    string? LastDeliveryReportWriterGridSummary = null,
    string? VersionStamp = null,
    int LastDeliveryArtifactCount = 0,
    string? LastDeliveryIntegritySummary = null,
    IReadOnlyList<string>? ReadinessBlockers = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    string? LastDeliveryEntitlementScope = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReportingScheduleStateDto>))]
public enum ReportingScheduleStateDto
{
    Active = 0,
    Paused = 1,
    Disabled = 2,
    Draft = 3
}

public sealed record ReportingStarterSeedScheduleDto(
    string ScheduleId,
    string TemplateId,
    string CronExpression,
    string Cadence,
    string Description,
    ReportingScheduleStateDto State = ReportingScheduleStateDto.Draft,
    string? DefaultPeriod = null,
    IReadOnlyList<ReportingScheduleDeliveryTargetDto>? DeliveryTargets = null);

public sealed record ReportingStarterKitDto(
    string KitId,
    string Archetype,
    string DisplayName,
    string Description,
    IReadOnlyList<string> TemplateIds,
    string DefaultLayoutId,
    string DefaultPeriod,
    IReadOnlyList<ReportingStarterSeedScheduleDto> SeedSchedules);

public sealed record ReportingStarterKitStateDto(
    bool IsProvisioned,
    string? SelectedKitId,
    string? Archetype,
    IReadOnlyList<string> EnabledTemplateIds,
    string? DefaultLayoutId,
    string? DefaultPeriod,
    IReadOnlyList<string> SeedScheduleIds,
    DateTimeOffset? ProvisionedAtUtc = null,
    string? ProvisionedBy = null);

public sealed record ReportingStarterKitProvisionResultDto(
    ReportingStarterKitDto Kit,
    ReportingStarterKitStateDto State,
    IReadOnlyList<ReportingScheduleRecordDto> SeededSchedules);

public sealed record ReportingScheduleRecordDto(
    string ScheduleId,
    string TemplateId,
    string CronExpression,
    DateOnly NextAsOfDate,
    DateTimeOffset DueAtUtc,
    int MaxRetries,
    string RequestedBy,
    ReportingScheduleStateDto State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastRunAtUtc = null,
    string? LastRunId = null,
    int RunCount = 0,
    string? Description = null,
    IReadOnlyList<ReportingScheduleDeliveryTargetDto>? DeliveryTargets = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DatasetRows = null,
    string? DatasetSourceId = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingThemeOverride = null);

public sealed record ReportingScheduleUpsertRequestDto(
    string ScheduleId,
    string TemplateId,
    string CronExpression,
    DateOnly NextAsOfDate,
    DateTimeOffset DueAtUtc,
    int MaxRetries,
    string RequestedBy,
    string? Description = null,
    ReportingScheduleStateDto State = ReportingScheduleStateDto.Active,
    IReadOnlyList<ReportingScheduleDeliveryTargetDto>? DeliveryTargets = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DatasetRows = null,
    string? DatasetSourceId = null,
    string? BrandingThemeId = null,
    ReportBrandingThemeDto? BrandingThemeOverride = null);

public sealed record ReportingScheduleRunResultDto(
    ReportingScheduleRecordDto Schedule,
    WorkstationReportingRunPayload Run,
    IReadOnlyList<ReportPackDeliveryAttemptDto>? DeliveryAttempts = null,
    IReadOnlyList<string>? DeliveryWarnings = null);

public sealed record ReportingDueScheduleRunResultDto(
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<ReportingScheduleRunResultDto> Runs);

public sealed record ReportingRunRequestDto(
    string TemplateId,
    DateOnly? AsOfDate = null,
    int MaxRetries = 0,
    string? JobId = null,
    string? RequestedBy = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DatasetRows = null,
    string? DatasetSourceId = null,
    string? RetryReason = null,
    bool AllowRestatement = false);

public sealed record ReportingRunResultDto(
    WorkstationReportingRunPayload Run);

public sealed record ReportingRunAuditEntryDto(
    string RunId,
    DateTimeOffset TimestampUtc,
    string Action,
    string Actor,
    string Notes);

public sealed record ReportingRunAuditTrailDto(
    string RunId,
    string TemplateId,
    string AsOfDate,
    string Status,
    string Trigger,
    int AttemptCount,
    IReadOnlyList<ReportingRunAuditEntryDto> Entries,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    int? ReportWriterDatasetRowCount = null);

/// <summary>
/// Shared governed report-pack workflow states. The W4 happy-path lifecycle is
/// <see cref="Draft"/> to <see cref="InReview"/> to <see cref="Approved"/> to
/// <see cref="Published"/>; additional states are retained for rejection, restatement,
/// archival, and compatibility with older validation-oriented clients.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportPackWorkflowStateDto>))]
public enum ReportPackWorkflowStateDto
{
    Draft = 0,
    Validated = 1,
    InReview = 2,
    Approved = 3,
    Published = 4,
    Restated = 5,
    Archived = 6,
    /// <summary>Reviewer-returned report pack that must be resubmitted before approval and publication.</summary>
    Rejected = 7,
    PendingApproval = 8
}

public sealed record VersionedReportTemplateIdDto(string Name, int Version);
public sealed record ReportTemplateParameterDefinitionDto(string Name, bool Required);

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterGridKindDto>))]
public enum ReportWriterGridKindDto
{
    Detail = 0,
    Pivot = 1,
    TopN = 2,
    Contribution = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterAggregateFunctionDto>))]
public enum ReportWriterAggregateFunctionDto
{
    Sum = 0,
    Count = 1,
    Average = 2,
    Min = 3,
    Max = 4
}

public sealed record ReportWriterMetricDefinitionDto(
    string Name,
    string SourceField,
    ReportWriterAggregateFunctionDto Function = ReportWriterAggregateFunctionDto.Sum,
    string? Label = null);

public sealed record ReportWriterFormulaDefinitionDto(
    string Name,
    string Expression,
    string? Label = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterFilterOperatorDto>))]
public enum ReportWriterFilterOperatorDto
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    StartsWith = 3,
    EndsWith = 4,
    GreaterThan = 5,
    GreaterThanOrEqual = 6,
    LessThan = 7,
    LessThanOrEqual = 8,
    IsBlank = 9,
    IsNotBlank = 10
}

public sealed record ReportWriterFilterDefinitionDto(
    string Field,
    ReportWriterFilterOperatorDto Operator = ReportWriterFilterOperatorDto.Equals,
    string? Value = null,
    string? Label = null);

public sealed record ReportWriterGridDefinitionDto(
    string GridId,
    string Title,
    ReportWriterGridKindDto Kind,
    IReadOnlyList<string>? RowFields = null,
    IReadOnlyList<string>? ColumnFields = null,
    IReadOnlyList<ReportWriterMetricDefinitionDto>? Metrics = null,
    IReadOnlyList<ReportWriterFormulaDefinitionDto>? Formulas = null,
    int? TopN = null,
    string? SortBy = null,
    bool SortDescending = true,
    IReadOnlyList<ReportWriterFilterDefinitionDto>? Filters = null,
    IReadOnlyList<ReportWriterFormatRuleDto>? FormatRules = null,
    ReportWriterChartDefinitionDto? Chart = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterCellStyleDto>))]
public enum ReportWriterCellStyleDto
{
    None = 0,
    Success = 1,
    Warning = 2,
    Danger = 3,
    Info = 4
}

public sealed record ReportWriterFormatRuleDto(
    string Column,
    ReportWriterFilterOperatorDto Operator,
    ReportWriterCellStyleDto Style,
    string? Value = null,
    string? Label = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterChartTypeDto>))]
public enum ReportWriterChartTypeDto
{
    Bar = 0,
    StackedBar = 1,
    Line = 2,
    Area = 3,
    Pie = 4
}

public sealed record ReportWriterChartDefinitionDto(
    ReportWriterChartTypeDto Type,
    string CategoryField,
    IReadOnlyList<string> ValueColumns,
    string? Title = null);

public sealed record ReportWriterChartSeriesDto(
    string Key,
    string Label,
    IReadOnlyList<decimal> Values);

public sealed record ReportWriterChartRenderDto(
    ReportWriterChartTypeDto Type,
    string Title,
    string CategoryField,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ReportWriterChartSeriesDto> Series,
    IReadOnlyList<string> Warnings);

public sealed record ReportWriterGridColumnDto(
    string Key,
    string Label,
    string Role);

public sealed record ReportWriterGridRowDto(
    string RowKey,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, ReportWriterCellStyleDto>? StyleHints = null);

public sealed record ReportWriterMetricLineageDto(
    string Name,
    string SourceField,
    string Function);

public sealed record ReportWriterFormulaLineageDto(
    string Name,
    string Expression,
    IReadOnlyList<string> SourceFields);

public sealed record ReportWriterFilterLineageDto(
    string Field,
    string Operator,
    string? Value,
    string? Label = null);

public sealed record ReportWriterGridDataDictionaryFieldDto(
    string Key,
    string Label,
    string Role,
    string SourceField,
    string DataType,
    bool IsGenerated,
    string Description);

public sealed record ReportWriterGridValidationCheckDto(
    string CheckId,
    string Status,
    string Detail);

public sealed record ReportWriterGridLineageDto(
    int InputRowCount,
    int OutputRowCount,
    IReadOnlyList<string> SourceFields,
    IReadOnlyList<ReportWriterMetricLineageDto> Metrics,
    IReadOnlyList<ReportWriterFormulaLineageDto> Formulas,
    int? FilteredInputRowCount = null,
    IReadOnlyList<ReportWriterFilterLineageDto>? Filters = null);

public sealed record ReportWriterGridRenderDto(
    string GridId,
    string Title,
    ReportWriterGridKindDto Kind,
    IReadOnlyList<ReportWriterGridColumnDto> Columns,
    IReadOnlyList<ReportWriterGridRowDto> Rows,
    IReadOnlyList<string> Warnings,
    ReportWriterGridLineageDto? Lineage = null,
    IReadOnlyList<ReportWriterGridDataDictionaryFieldDto>? DataDictionary = null,
    IReadOnlyList<ReportWriterGridValidationCheckDto>? ValidationChecks = null,
    ReportWriterChartRenderDto? Chart = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterDiffRowStateDto>))]
public enum ReportWriterDiffRowStateDto
{
    Unchanged = 0,
    Changed = 1,
    Added = 2,
    Removed = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportWriterDiffDirectionDto>))]
public enum ReportWriterDiffDirectionDto
{
    Flat = 0,
    Up = 1,
    Down = 2
}

public sealed record ReportWriterGridDiffCellDto(
    string Column,
    string? PriorValue,
    string? CurrentValue,
    string? Delta,
    ReportWriterDiffDirectionDto Direction);

public sealed record ReportWriterGridDiffRowDto(
    string RowKey,
    ReportWriterDiffRowStateDto State,
    IReadOnlyDictionary<string, string> PriorValues,
    IReadOnlyDictionary<string, string> CurrentValues,
    IReadOnlyList<ReportWriterGridDiffCellDto> Cells);

public sealed record ReportWriterGridDiffDto(
    string GridId,
    string Title,
    IReadOnlyList<ReportWriterGridColumnDto> Columns,
    IReadOnlyList<ReportWriterGridDiffRowDto> Rows,
    IReadOnlyList<string> Warnings,
    int AddedRowCount,
    int RemovedRowCount,
    int ChangedRowCount,
    int UnchangedRowCount);

[JsonConverter(typeof(JsonStringEnumConverter<ReportAccessModeDto>))]
public enum ReportAccessModeDto
{
    Private = 0,
    Restricted = 1,
    CompanyWide = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportAccessPrincipalKindDto>))]
public enum ReportAccessPrincipalKindDto
{
    User = 0,
    Group = 1,
    Company = 2
}

public sealed record ReportAccessPrincipalDto(
    ReportAccessPrincipalKindDto Kind,
    string PrincipalId,
    string? DisplayName = null);

public sealed record ReportAccessPolicyDto(
    ReportAccessModeDto Mode = ReportAccessModeDto.CompanyWide,
    string? OwnerPrincipalId = null,
    IReadOnlyList<ReportAccessPrincipalDto>? Principals = null,
    string? CompanyId = null,
    bool AllowOwnerAccess = true);

public sealed record ReportAccessEvaluationDto(
    bool IsAccessible,
    string Reason,
    IReadOnlyList<ReportAccessPrincipalDto> MatchedPrincipals);

public sealed record ReportTemplateDefinitionDto(
    VersionedReportTemplateIdDto TemplateId,
    string DisplayName,
    IReadOnlyList<ReportTemplateParameterDefinitionDto> Parameters,
    IReadOnlyList<string>? Sections = null,
    IReadOnlyList<ReportWriterGridDefinitionDto>? Grids = null,
    ReportAccessPolicyDto? AccessPolicy = null);
public sealed record RenderReportTemplateRequestDto(
    VersionedReportTemplateIdDto TemplateId,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DatasetRows = null,
    IReadOnlyList<ReportWriterGridDefinitionDto>? Grids = null);
public sealed record RenderReportTemplateResponseDto(
    VersionedReportTemplateIdDto TemplateId,
    string RenderedContent,
    IReadOnlyList<string> MissingRequiredParameters,
    IReadOnlyList<ReportWriterGridRenderDto>? Grids = null,
    IReadOnlyList<string>? Warnings = null);

[JsonConverter(typeof(JsonStringEnumConverter<ReportTemplateLifecycleStatusDto>))]
public enum ReportTemplateLifecycleStatusDto
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Rejected = 3,
    Superseded = 4
}

public sealed record ReportTemplateAuditEventDto(
    DateTimeOffset At,
    string Actor,
    string Action,
    ReportTemplateLifecycleStatusDto FromStatus,
    ReportTemplateLifecycleStatusDto ToStatus,
    string? Note = null);

public sealed record ReportTemplateGovernanceRecordDto(
    ReportTemplateDefinitionDto Definition,
    ReportTemplateLifecycleStatusDto Status,
    string Family,
    bool IsBuiltIn,
    bool IsLatestApproved,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string UpdatedBy,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> ValidationIssues,
    IReadOnlyList<ReportTemplateAuditEventDto> AuditTrail,
    string? SubmittedBy = null,
    DateTimeOffset? SubmittedAt = null,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAt = null,
    string? RejectedBy = null,
    DateTimeOffset? RejectedAt = null,
    string? DecisionRationale = null,
    string? ApprovalReference = null,
    VersionedReportTemplateIdDto? BasedOnTemplateId = null);

public sealed record ReportTemplateDraftRequestDto(
    string Name,
    string DisplayName,
    IReadOnlyList<string> Sections,
    IReadOnlyList<ReportTemplateParameterDefinitionDto> Parameters,
    string? Family = null,
    int? BasedOnVersion = null,
    string? Rationale = null,
    IReadOnlyList<ReportWriterGridDefinitionDto>? Grids = null,
    ReportAccessPolicyDto? AccessPolicy = null);

public sealed record ReportTemplateDecisionRequestDto(
    string Rationale,
    string? ApprovalReference = null);

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
    string? ReconciliationRunId = null,
    string? ProviderEventId = null,
    string? SecurityMasterId = null,
    string? SecurityDefinitionId = null,
    string? ReconciliationOutcome = null,
    string? ApprovalId = null,
    string? FinancialRecordExplorerId = null,
    string? FinancialRecordHref = null);
public sealed record ReportPackPublicationManifestDto(
    string ManifestId,
    string RetainedManifestPath,
    string EvidenceHash,
    string SignedOffBy,
    DateTimeOffset SignedOffAt,
    IReadOnlyList<ReportPackEvidenceLinkDto> EvidenceLinks,
    ReportBrandingThemeDto? BrandingTheme = null,
    string? SignedOffRole = null,
    string? SignOffReason = null,
    string? SignOffContext = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
/// <summary>Request payload for publishing an approved report pack into retained evidence storage.</summary>
public sealed record ReportPackPublishRequestDto(
    string SignedOffBy,
    string EvidenceHash,
    string ManifestId,
    string RetainedManifestPath,
    IReadOnlyList<ReportPackEvidenceLinkDto> EvidenceLinks,
    string? Note = null,
    ReportBrandingThemeDto? BrandingTheme = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
/// <summary>Optional action-origin metadata for report-pack workflow commands that historically used an empty body.</summary>
public sealed record ReportPackWorkflowActionRequestDto(
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
/// <summary>Request payload for rejecting an in-review report pack with reviewer metadata and supporting evidence.</summary>
public sealed record ReportPackRejectRequestDto(
    string Reason,
    string Actor = "",
    string ActorRole = "",
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null);
public sealed record ReportPackCreateRequestDto(
    string FundProfileId,
    string FundAccountId,
    string Period,
    VersionedReportTemplateIdDto TemplateId,
    IReadOnlyList<ReportPackLineProvenanceDto>? LineProvenance = null,
    ReportAccessPolicyDto? AccessPolicy = null);
/// <summary>Durable metadata captured when a published report pack is restated.</summary>
public sealed record ReportPackRestatementMetadataDto(
    string ReasonCode,
    string Approver,
    Guid PriorVersionReportId,
    IReadOnlyList<ReportPackChangedLineDto> ChangedLines,
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null);
/// <summary>Durable rejection decision retained on a report pack after review-state rejection.</summary>
public sealed record ReportPackRejectionMetadataDto(
    string Reason,
    string Actor,
    string ActorRole,
    DateTimeOffset RejectedAt,
    IReadOnlyList<ReportPackEvidenceLinkDto>? EvidenceLinks = null);
public sealed record ReportPackRestateRequestDto(
    string ReasonCode,
    Guid PriorVersionReportId,
    IReadOnlyList<ReportPackChangedLineDto> ChangedLines,
    string? Approver = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);
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
    ReportPackPublicationManifestDto? Publication = null,
    ReportPackRejectionMetadataDto? Rejection = null,
    ReportAccessPolicyDto? AccessPolicy = null);
