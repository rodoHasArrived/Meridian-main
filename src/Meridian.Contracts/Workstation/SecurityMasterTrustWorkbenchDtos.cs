using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterTrustTone>))]
public enum SecurityMasterTrustTone
{
    Unknown = 0,
    Blocked = 1,
    Review = 2,
    Trusted = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterImpactSeverity>))]
public enum SecurityMasterImpactSeverity
{
    Unknown = 0,
    None = 1,
    Low = 2,
    Medium = 3,
    High = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterRecommendedActionKind>))]
public enum SecurityMasterRecommendedActionKind
{
    ResolveSelectedConflict = 0,
    BulkResolveLowRiskConflicts = 1,
    BackfillTradingParameters = 2,
    ReviewCorporateActions = 3,
    OpenPortfolioImpact = 4,
    OpenLedgerImpact = 5,
    OpenReconciliationImpact = 6,
    OpenReportPackImpact = 7,
    EditSelectedSecurity = 8,
    RefreshTrustSnapshot = 9
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterConflictRecommendationKind>))]
public enum SecurityMasterConflictRecommendationKind
{
    PreserveWinner = 0,
    Challenger = 1,
    DismissAsEquivalent = 2,
    ManualReview = 3
}

public sealed record SecurityMasterTrustSnapshotDto(
    Guid SecurityId,
    SecurityMasterWorkstationDto Security,
    SecurityIdentityDrillInDto Identity,
    SecurityMasterEconomicDefinitionDrillInDto EconomicDefinition,
    SecurityMasterTrustPostureDto TrustPosture,
    IReadOnlyList<SecurityMasterSourceCandidateDto> ProvenanceCandidates,
    IReadOnlyList<SecurityMasterConflictAssessmentDto> ConflictAssessments,
    SecurityMasterDownstreamImpactDto DownstreamImpact,
    IReadOnlyList<SecurityMasterRecommendedActionDto> RecommendedActions,
    IReadOnlyList<SecurityMasterEventEnvelope> History,
    IReadOnlyList<CorporateActionDto> CorporateActions,
    DateTimeOffset RetrievedAtUtc)
{
    public SecurityValidationReportDto? ValidationReport { get; init; }
    public SecurityMasterIdentifierSummaryDto? IdentifierSummary { get; init; }
    public SecurityMasterSchemaCompatibilityDto? SchemaCompatibility { get; init; }
    public IReadOnlyList<SecurityMasterChangeHistoryItemDto>? ChangeHistory { get; init; }
    public SecurityMasterScheduleSummaryDto? ScheduleSummary { get; init; }
    public SecurityMasterLotModelDto? LotModel { get; init; }
    public SecurityMasterScheduleBookDto? ScheduleBook { get; init; }
    public SecurityMasterOpenLotReadModelDto? OpenLotReadModel { get; init; }
    public InstrumentPassportDto? InstrumentPassport { get; init; }
    public IReadOnlyList<CorporateActionDescriptorDto>? CorporateActionDescriptors { get; init; }
}

/// <summary>
/// Canonical-taxonomy projection of one effective corporate action for workbench surfaces:
/// the chain tip's catalog identity (canonical name, ISO 15022 CAEV alignment, display name),
/// its lifecycle state resolved at the snapshot's as-of time, and the amendment timeline
/// (original announcement first, tip last). <see cref="CorpActId"/> joins the descriptor back
/// to the raw row in <see cref="SecurityMasterTrustSnapshotDto.CorporateActions"/>.
/// </summary>
public sealed record CorporateActionDescriptorDto(
    Guid CorpActId,
    string CanonicalName,
    string? CaevCode,
    string DisplayName,
    string LifecycleState,
    bool IsCancelled,
    IReadOnlyList<CorporateActionTimelineEntryDto> Timeline);

/// <summary>
/// One event in an effective corporate action's supersede chain. <see cref="LifecycleState"/>
/// is the stored write-side state (null stored states read as Confirmed);
/// <see cref="IsAmendment"/> marks entries that superseded a prior event.
/// </summary>
public sealed record CorporateActionTimelineEntryDto(
    Guid CorpActId,
    string LifecycleState,
    DateOnly ExDate,
    DateOnly? PayDate,
    bool IsAmendment);

public sealed record SecurityMasterEconomicDefinitionDrillInDto(
    Guid SecurityId,
    string AssetClass,
    string Currency,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? AssetFamily,
    string? SubType,
    string? IssuerType,
    string? RiskCountry,
    string? WinningSourceSystem,
    string? WinningSourceRecordId,
    DateTimeOffset? WinningSourceAsOf,
    string? WinningSourceUpdatedBy,
    string? WinningSourceReason);

public sealed record SecurityMasterTrustPostureDto(
    SecurityMasterTrustTone Tone,
    int TrustScore,
    string Summary,
    string GoldenCopySource,
    string GoldenCopyRule,
    string TradingParametersStatus,
    string CorporateActionReadiness,
    bool HasOpenConflicts,
    int OpenConflictCount,
    bool TradingParametersComplete,
    bool HasUpcomingCorporateActions,
    bool CorporateActionsTrusted);

public sealed record SecurityMasterSourceCandidateDto(
    Guid? ConflictId,
    string FieldPath,
    string SourceSystem,
    string DisplayValue,
    bool IsWinningSource,
    DateTimeOffset? AsOf,
    string? UpdatedBy,
    string? Reason,
    string? SourceRecordId = null,
    SecurityMasterImpactSeverity ImpactSeverity = SecurityMasterImpactSeverity.None);

public sealed record SecurityMasterConflictAssessmentDto(
    SecurityMasterConflict Conflict,
    string? CurrentWinningValue,
    string? ChallengerValue,
    string CurrentWinningSource,
    string ChallengerSource,
    SecurityMasterConflictRecommendationKind Recommendation,
    string RecommendedResolution,
    string RecommendedWinner,
    SecurityMasterImpactSeverity ImpactSeverity,
    string ImpactSummary,
    string ImpactDetail,
    bool IsBulkEligible,
    string? BulkIneligibilityReason = null);

public sealed record SecurityMasterIdentifierSummaryDto(
    string? PrimaryIdentifierKind,
    string? PrimaryIdentifierValue,
    int ActiveIdentifierCount,
    int ActiveAliasCount,
    int ProviderMappingCount,
    int DistinctProviderCount,
    bool HasPrimaryIdentifier,
    bool HasProviderMappings,
    string Summary,
    IReadOnlyList<SecurityMasterProviderSymbolMappingDto> ProviderMappings);

public sealed record SecurityMasterProviderSymbolMappingDto(
    string MappingSource,
    string MappingKind,
    string Value,
    string NormalizedValue,
    string? Provider,
    string? NormalizedProvider,
    bool IsPrimary,
    bool IsEnabled,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive);

public sealed record SecurityMasterSchemaCompatibilityDto(
    string AssetClass,
    int LegacyAssetSpecificTermsSchemaVersion,
    int EconomicTermsSchemaVersion,
    bool HasLegacyAssetSpecificTerms,
    bool HasEconomicTerms,
    bool HasClassificationPayload,
    string Summary);

public sealed record SecurityMasterChangeHistoryItemDto(
    string ChangeId,
    long StreamVersion,
    string EventType,
    DateTimeOffset ChangedAtUtc,
    DateTimeOffset? EffectiveAtUtc,
    string Actor,
    string Origin,
    string SourceSystem,
    string? SourceRecordId,
    string? Reason,
    string Summary,
    IReadOnlyList<string> ChangedFields,
    string ChangedFieldsSummary);

public sealed record SecurityMasterScheduleSummaryDto(
    bool SupportsCashflowSchedule,
    bool SupportsFactorHistory,
    bool HasEconomicScheduleTerms,
    decimal? CurrentFactor,
    DateOnly? CurrentFactorDate,
    DateOnly? NextLifecycleDate,
    string SourceSummary,
    string Summary);

public sealed record SecurityMasterScheduleBookDto(
    bool SupportsCashflowSchedule,
    bool SupportsFactorHistory,
    bool HasEconomicScheduleTerms,
    string Currency,
    decimal? CurrentFactor,
    DateOnly? CurrentFactorDate,
    DateOnly? NextLifecycleDate,
    string SourceSummary,
    string Summary,
    IReadOnlyList<SecurityMasterScheduleEventDto> Events,
    IReadOnlyList<SecurityMasterFactorPointDto> FactorHistory,
    IReadOnlyList<SecurityMasterScheduleProvenanceDto> ProvenanceHistory);

public sealed record SecurityMasterScheduleEventDto(
    string EventId,
    string EventType,
    DateOnly EffectiveDate,
    DateOnly? PayDate,
    DateOnly? AccrualStartDate,
    DateOnly? AccrualEndDate,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? VarianceAmount,
    decimal? FactorStart,
    decimal? FactorEnd,
    string Currency,
    string PostingStatus,
    string SourceSystem,
    string? SourceRecordId,
    DateTimeOffset? SourceAsOfUtc,
    string? SourceUpdatedBy,
    string? SourceReason,
    bool IsDerivedFromEconomicTerms,
    bool IsCurrentProjection);

public sealed record SecurityMasterFactorPointDto(
    string PointId,
    DateOnly EffectiveDate,
    decimal Factor,
    decimal? PreviousFactor,
    string SourceSystem,
    string? SourceRecordId,
    DateTimeOffset? SourceAsOfUtc,
    string? SourceUpdatedBy,
    string? SourceReason,
    bool IsCurrentFactor);

public sealed record SecurityMasterScheduleProvenanceDto(
    string ProvenanceId,
    string Category,
    string Summary,
    DateOnly? EffectiveDate,
    string SourceSystem,
    string? SourceRecordId,
    DateTimeOffset? SourceAsOfUtc,
    string? SourceUpdatedBy,
    string? SourceReason,
    long? StreamVersion,
    string? EventType);

public sealed record SecurityMasterLotModelDto(
    string QuantityModel,
    decimal? LotSize,
    decimal? ContractMultiplier,
    bool UsesFaceValue,
    bool SupportsFactorAdjustedExposure,
    bool RequiresResolvedSecurityId,
    string Summary);

public sealed record SecurityMasterOpenLotReadModelDto(
    string QuantityModel,
    decimal? LotSize,
    decimal? ContractMultiplier,
    bool UsesFaceValue,
    bool SupportsFactorAdjustedExposure,
    bool RequiresResolvedSecurityId,
    decimal? CurrentFactor,
    DateOnly? CurrentFactorDate,
    DateTimeOffset AsOfUtc,
    string Summary,
    IReadOnlyList<SecurityMasterOpenLotDto> Lots,
    IReadOnlyList<SecurityMasterOpenLotProvenanceDto> ProvenanceHistory);

public sealed record SecurityMasterOpenLotDto(
    Guid SecurityId,
    string PortfolioId,
    string RunId,
    string? AccountScopeId,
    string? AccountScopeDisplayName,
    string? VehicleScopeId,
    string? VehicleScopeDisplayName,
    string LotId,
    string Symbol,
    DateTimeOffset TradeDate,
    DateTimeOffset? SettleDate,
    decimal OriginalQuantity,
    decimal CurrentQuantity,
    decimal? OriginalFace,
    decimal? CurrentFace,
    decimal? FactorAdjustedQuantity,
    decimal? FactorAdjustedFace,
    decimal CostBasis,
    decimal EntryPrice,
    decimal? UnrealizedPnl,
    string Currency,
    string LotStatus,
    string SourceSystem,
    string? SourceRecordId,
    DateTimeOffset AsOfUtc,
    string? SourceUpdatedBy,
    string? SourceReason,
    bool IsLongTerm,
    string? Notes);

public sealed record SecurityMasterOpenLotProvenanceDto(
    string ProvenanceId,
    string RunId,
    string PortfolioId,
    string? AccountScopeId,
    string? AccountScopeDisplayName,
    string SourceSystem,
    string? SourceRecordId,
    DateTimeOffset AsOfUtc,
    string Summary);

public sealed record SecurityMasterDownstreamImpactDto(
    string? FundProfileId,
    bool IsScoped,
    SecurityMasterImpactSeverity Severity,
    string Summary,
    string PortfolioExposureSummary,
    string LedgerExposureSummary,
    string ReconciliationExposureSummary,
    string ReportPackExposureSummary,
    int MatchedRunCount,
    int PortfolioExposureCount,
    int LedgerExposureCount,
    int ReconciliationExposureCount,
    int ReportPackExposureCount,
    IReadOnlyList<SecurityMasterImpactLinkDto> Links);

public sealed record SecurityMasterImpactLinkDto(
    string Target,
    string Label,
    string Summary,
    SecurityMasterImpactSeverity Severity,
    bool IsActive);

public sealed record SecurityMasterRecommendedActionDto(
    SecurityMasterRecommendedActionKind Kind,
    string Title,
    string Detail,
    bool IsPrimary,
    bool IsEnabled,
    Guid? ConflictId = null,
    string? Target = null);

/// <summary>
/// Governed instrument passport that combines identifiers, mappings, lifecycle,
/// corporate-action, pricing, and downstream usage evidence for a Security Master record.
/// </summary>
public sealed record InstrumentPassportProviderConfidenceDto(
    string Provider,
    string ProviderSource,
    string MappingKind,
    string Symbol,
    string NormalizedSymbol,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset? FreshnessAsOf,
    int? FreshnessMinutes,
    decimal ConfidenceScore,
    string ConfidenceReason,
    IReadOnlyList<Guid> IdentifierConflictIds,
    IReadOnlyList<string> IdentifierConflictSummaries,
    IReadOnlyList<SecurityMasterChangeHistoryItemDto> OverrideHistory);

public sealed record InstrumentPassportReferenceDataWorkbenchDto(
    string Status,
    string Summary,
    IReadOnlyList<InstrumentPassportReferenceDataWorkbenchSectionDto> Sections,
    IReadOnlyList<InstrumentPassportOperationsHandoffDto> OperationsHandoffs);

public sealed record InstrumentPassportReferenceDataWorkbenchSectionDto(
    string SectionId,
    string Title,
    string Status,
    string Summary,
    int EvidenceCount,
    int BlockingIssueCount);

public sealed record InstrumentPassportOperationsHandoffDto(
    string HandoffId,
    string Target,
    string Title,
    string Detail,
    string Status,
    bool IsEnabled)
{
    public string? Owner { get; init; }
    public string? BlockerReason { get; init; }
    public IReadOnlyList<string> ImpactedOutputs { get; init; } = [];
    public IReadOnlyList<string> LinkedCases { get; init; } = [];
    public string? Route { get; init; }
}

public sealed record InstrumentPassportOperationsWorkbenchDto(
    string Status,
    string Summary,
    IReadOnlyList<InstrumentPassportOperationsWorkbenchPanelDto> Panels,
    IReadOnlyList<InstrumentPassportOperationsReadinessDto> Readiness,
    IReadOnlyList<InstrumentPassportOperationsHandoffDto> Handoffs);

public sealed record InstrumentPassportOperationsWorkbenchPanelDto(
    string PanelId,
    string Title,
    string Status,
    string Summary,
    IReadOnlyList<InstrumentPassportOperationsWorkbenchItemDto> Items);

public sealed record InstrumentPassportOperationsWorkbenchItemDto(
    string ItemId,
    string Label,
    string Value,
    string Status,
    string Detail,
    int EvidenceCount,
    int BlockingIssueCount,
    string? Route = null);

public sealed record InstrumentPassportOperationsReadinessDto(
    string ReadinessId,
    string Label,
    string Status,
    bool IsReady,
    string Summary,
    int EvidenceCount,
    int BlockingIssueCount,
    string NextAction,
    string? Route = null);

public sealed record SecurityMasterOperatingModelDto(
    Guid SecurityId,
    string? ClientId,
    string? AccountId,
    string? FundProfileId,
    string Status,
    string Summary,
    IReadOnlyList<SecurityMasterOperatingModelStageDto> Stages,
    IReadOnlyList<SecurityMasterEntitlementApplicabilityDto> EntitlementApplicability,
    IReadOnlyList<SecurityMasterOperatorMetadataDto> OperatorMetadata,
    SecurityMasterManualChangeApprovalPostureDto ManualChangeApproval,
    IReadOnlyList<InstrumentPassportReferenceDataWorkbenchSectionDto> Controls,
    DateTimeOffset RetrievedAtUtc);

public sealed record SecurityMasterOperatingModelStageDto(
    string StageId,
    string Title,
    string Status,
    string Summary,
    int EvidenceCount,
    int BlockingIssueCount);

public sealed record SecurityMasterEntitlementApplicabilityDto(
    Guid EntitlementId,
    string VendorName,
    DataVendorDataType DataType,
    string Scope,
    string? ClientId,
    string? AccountId,
    string? FundProfileId,
    Guid? SecurityId,
    bool IsApplicable,
    bool IsMostSpecific,
    DataVendorEntitlementStatus Status,
    bool RequiresDirectClientContract,
    string? ContractReference,
    string Summary);

public sealed record SecurityMasterOperatorMetadataDto(
    string MetadataId,
    string VendorName,
    DataVendorDataType DataType,
    string SourceCategory,
    string ExpectedRefreshCadence,
    int? DefaultMaxDaysStale,
    bool RequiresDirectClientContract,
    string? OperatorMetadata,
    string Summary);

public sealed record SecurityMasterManualChangeApprovalPostureDto(
    string PolicyKey,
    OperationsGateKeyDto Gate,
    string Route,
    string RequiredPermission,
    int RequiredDistinctApprovals,
    bool RequiresIndependentReviewer,
    string EvidenceRequirement,
    string Status,
    int ManualChangeCount,
    int UnapprovedManualChangeCount,
    string Summary);

public sealed record InstrumentPassportClassificationProfileDto(
    string InstrumentType,
    string DisplayName,
    string SecurityMasterAssetClass,
    string? AssetFamily,
    string? SubType,
    string DefaultProviderSecurityType,
    bool IsTradeable,
    bool IsReferenceOnly,
    bool IsDerivative,
    bool RequiresUnderlying,
    bool ProducesCashFlows,
    bool RequiresLotTracking,
    string SettlementModel,
    IReadOnlyList<string> CompatibleSecurityMasterAssetClasses,
    IReadOnlyList<string> PreferredIdentifierKinds,
    IReadOnlyList<string> RequiredEconomicTerms,
    IReadOnlyList<string> ProviderCapabilities,
    IReadOnlyList<string> LifecycleEvents,
    IReadOnlyList<string> ValidationRules,
    IReadOnlyList<string> LedgerBehaviorHints,
    IReadOnlyList<string> RiskModelHints,
    string Summary);

public sealed record InstrumentPassportDto(
    Guid SecurityId,
    SecurityIdentityDrillInDto Identity,
    SecurityMasterEconomicDefinitionDrillInDto EconomicDefinition,
    SecurityMasterIdentifierSummaryDto IdentifierSummary,
    IReadOnlyList<SecurityMasterProviderSymbolMappingDto> ProviderMappings,
    IReadOnlyList<SecurityMasterChangeHistoryItemDto> LifecycleEvents,
    IReadOnlyList<CorporateActionDto> CorporateActions,
    InstrumentPassportPricingDto Pricing,
    SecurityMasterDownstreamImpactDto Usage,
    SecurityMasterTrustPostureDto TrustPosture,
    DateTimeOffset RetrievedAtUtc)
{
    public IReadOnlyList<InstrumentPassportProviderConfidenceDto> ProviderConfidence { get; init; } = [];
    public InstrumentPassportReferenceDataWorkbenchDto? ReferenceDataWorkbench { get; init; }
    public SecurityMasterOperatingModelDto? OperatingModel { get; init; }
    public InstrumentPassportOperationsWorkbenchDto? OperationsWorkbench { get; init; }
    public InstrumentPassportClassificationProfileDto? ClassificationProfile { get; init; }
}

public sealed record InstrumentPassportPricingDto(
    string Status,
    string Summary,
    TradingParametersDto? TradingParameters,
    decimal? LotSize,
    decimal? TickSize,
    decimal? ContractMultiplier,
    string? TradingHoursUtc,
    decimal? CircuitBreakerThresholdPct);

public sealed record BulkResolveSecurityMasterConflictsRequest(
    IReadOnlyList<Guid> ConflictIds,
    string ResolvedBy,
    string? Reason,
    string? FundProfileId);

public sealed record BulkResolveSecurityMasterConflictsResult(
    int Requested,
    int Eligible,
    int Resolved,
    int Skipped,
    IReadOnlyList<Guid> ResolvedConflictIds,
    IReadOnlyDictionary<Guid, string> SkippedReasons);
