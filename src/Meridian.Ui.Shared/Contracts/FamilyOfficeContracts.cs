namespace Meridian.Ui.Shared.Contracts;

/// <summary>
/// Single-family-office overview payload for browser and desktop operator workstations.
/// </summary>
public sealed record FamilyOfficeOverviewDto(
    string FamilyOfficeId,
    string DisplayName,
    string BaseCurrency,
    DateOnly AsOfDate,
    FamilyBalanceSheetDto BalanceSheet,
    IReadOnlyList<FamilyEntityDto> Entities,
    FamilyOwnershipGraphDto OwnershipGraph,
    IReadOnlyList<FamilyAccountSummaryDto> Accounts,
    IReadOnlyList<FamilyAssetSummaryDto> PublicAssets,
    IReadOnlyList<PrivateAssetSummaryDto> PrivateAssets,
    IReadOnlyList<CapitalCommitmentDto> CapitalCommitments,
    IReadOnlyList<CapitalActivityDto> RecentCapitalActivity,
    FamilyOfficeReadinessDto Readiness,
    IReadOnlyList<FamilyOfficeEvidenceLinkDto> EvidenceLinks);

/// <summary>
/// Common endpoint state for family-office workstation reads, including empty and degraded guidance.
/// </summary>
public sealed record FamilyOfficeEndpointStateDto(
    bool IsEmpty,
    string? EmptyStateGuidance,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc);

public sealed record FamilyOfficeBalanceSheetResponseDto(
    FamilyBalanceSheetDto BalanceSheet,
    FamilyOfficeEndpointStateDto State);

public sealed record FamilyOfficeEntitiesResponseDto(
    IReadOnlyList<FamilyEntityDto> Entities,
    FamilyOfficeEndpointStateDto State);

public sealed record FamilyOfficeOwnershipGraphResponseDto(
    FamilyOwnershipGraphDto OwnershipGraph,
    FamilyOfficeEndpointStateDto State);

/// <summary>
/// Consolidated family balance sheet with evidence fields retained for operator review.
/// </summary>
public sealed record FamilyBalanceSheetDto(
    string FamilyOfficeId,
    string BaseCurrency,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal NetWorth,
    decimal LiquidAssets,
    decimal MarketableSecurities,
    decimal PrivateAssets,
    decimal RealAssets,
    decimal CashAndEquivalents,
    decimal UnfundedCommitments,
    string SourceSystem,
    string? SourceDocumentId,
    DateOnly AsOfDate,
    DateOnly? ValuationDate,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc);

/// <summary>
/// Legal, trust, fund, household, or operating entity in the family office structure.
/// </summary>
public sealed record FamilyEntityDto(
    string EntityId,
    string DisplayName,
    string EntityType,
    string? Jurisdiction,
    string? TaxResidency,
    string BaseCurrency,
    string? ParentEntityId,
    decimal? OwnershipPercent,
    bool IsOperatingEntity,
    IReadOnlyList<FamilyOfficeEvidenceLinkDto> EvidenceLinks);

/// <summary>
/// Ownership graph used by operator surfaces to explain control, trust, and entity relationships.
/// </summary>
public sealed record FamilyOwnershipGraphDto(
    DateOnly AsOfDate,
    IReadOnlyList<FamilyOwnershipNodeDto> Nodes,
    IReadOnlyList<FamilyOwnershipEdgeDto> Edges,
    IReadOnlyList<FamilyOfficeEvidenceLinkDto> EvidenceLinks);

public sealed record FamilyOwnershipNodeDto(
    string NodeId,
    string DisplayName,
    string NodeType,
    string? EntityId,
    string? Jurisdiction,
    string? Currency);

public sealed record FamilyOwnershipEdgeDto(
    string EdgeId,
    string SourceNodeId,
    string TargetNodeId,
    string RelationshipType,
    decimal? OwnershipPercent,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? SourceDocumentId);

public sealed record FamilyAccountSummaryDto(
    string AccountId,
    string EntityId,
    string DisplayName,
    string AccountType,
    string? Custodian,
    string? ProviderAccountId,
    string Currency,
    decimal CashBalance,
    decimal MarketValue,
    decimal AccruedIncome,
    decimal TotalEquity,
    string SourceSystem,
    string? SourceDocumentId,
    DateOnly AsOfDate,
    DateOnly? ValuationDate,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc);

public sealed record FamilyAssetSummaryDto(
    string AssetId,
    string EntityId,
    string? AccountId,
    string DisplayName,
    string AssetClass,
    string? Symbol,
    string Currency,
    decimal Quantity,
    decimal MarketValue,
    decimal CostBasis,
    decimal UnrealizedGainLoss,
    decimal PercentOfNetWorth,
    string SourceSystem,
    string? SourceDocumentId,
    DateOnly AsOfDate,
    DateOnly? ValuationDate,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc);

/// <summary>
/// Summary for assets that require valuation evidence instead of exchange-traded marks.
/// </summary>
public sealed record PrivateAssetSummaryDto(
    string PrivateAssetId,
    string EntityId,
    string DisplayName,
    string AssetType,
    string Currency,
    decimal CurrentValue,
    decimal? CommitmentAmount,
    decimal? CalledCapital,
    decimal? DistributedCapital,
    string? ValuationMethod,
    string SourceSystem,
    string? SourceDocumentId,
    DateOnly AsOfDate,
    DateOnly? ValuationDate,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc);

public sealed record CapitalCommitmentDto(
    string CommitmentId,
    string EntityId,
    string VehicleName,
    string Strategy,
    string Currency,
    decimal CommitmentAmount,
    decimal CalledAmount,
    decimal UnfundedAmount,
    decimal DistributedAmount,
    decimal CurrentNav,
    int? VintageYear,
    string SourceSystem,
    string? SourceDocumentId,
    DateOnly AsOfDate,
    DateOnly? ValuationDate,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc);

public sealed record CapitalActivityDto(
    string ActivityId,
    string CommitmentId,
    string EntityId,
    string ActivityType,
    string Currency,
    decimal Amount,
    DateOnly EffectiveDate,
    DateOnly? NoticeDate,
    DateOnly? DueDate,
    string Status,
    string SourceSystem,
    string? SourceDocumentId,
    DateOnly AsOfDate,
    DateOnly? ValuationDate,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc);

/// <summary>
/// Operator readiness projection for whether the family-office workspace can be trusted for decisions.
/// </summary>
public sealed record FamilyOfficeReadinessDto(
    string Status,
    string EvidenceCompleteness,
    string ReconciliationStatus,
    int OpenExceptionCount,
    int ItemsNeedingReviewCount,
    DateTimeOffset GeneratedAtUtc,
    string? LastReviewedBy,
    DateTimeOffset? LastReviewedAtUtc,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<FamilyOfficeEvidenceLinkDto> EvidenceLinks);

/// <summary>
/// Link to a source document, packet, or operator evidence route backing a family-office value.
/// </summary>
public sealed record FamilyOfficeEvidenceLinkDto(
    string EvidenceId,
    string SourceSystem,
    string? SourceDocumentId,
    string Label,
    string EvidenceType,
    string? Route,
    DateTimeOffset? CapturedAtUtc,
    DateOnly? AsOfDate,
    DateOnly? ValuationDate,
    string? CompletenessStatus);
