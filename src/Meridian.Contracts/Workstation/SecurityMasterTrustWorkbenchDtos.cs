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
    DateTimeOffset RetrievedAtUtc);

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
