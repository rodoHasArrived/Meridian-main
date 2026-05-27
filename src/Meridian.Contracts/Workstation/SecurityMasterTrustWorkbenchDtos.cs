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
}

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
