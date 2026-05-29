namespace Meridian.Contracts.PrivateAssets;

/// <summary>
/// Supported private-asset investment categories for fund, family-office, and operating-company books.
/// </summary>
public enum PrivateAssetTypeDto
{
    PrivateEquityFund,
    VentureFund,
    PrivateCredit,
    RealEstate,
    DirectCompanyInvestment,
    Collectible,
    OperatingBusiness,
    Other
}

/// <summary>
/// Evidence categories accepted for private-asset records. These replace free-form notes for source documents.
/// </summary>
public enum PrivateAssetEvidenceKindDto
{
    ImportedStatement,
    CapitalNotice,
    K1,
    SideLetter,
    SubscriptionDocument,
    ValuationMemo,
    CommitmentSchedule,
    Other
}

/// <summary>
/// Stable warning codes emitted by the private-asset read-model validator.
/// </summary>
public enum PrivateAssetValidationWarningCodeDto
{
    StaleNav,
    MissingValuationDate,
    MissingOwnerEntity,
    MissingCommitmentSchedule,
    MissingEvidence
}

/// <summary>
/// Typed evidence link for retained private-asset source documents.
/// </summary>
public sealed record PrivateAssetEvidenceLinkDto(
    PrivateAssetEvidenceKindDto Kind,
    string EvidenceId,
    string Label,
    string? Uri,
    DateOnly? DocumentDate,
    string? SourceSystem = null,
    string? ContentHash = null);

/// <summary>
/// Expected capital activity used to distinguish commitment coverage from ad-hoc narrative notes.
/// </summary>
public sealed record PrivateAssetCommitmentScheduleEntryDto(
    DateOnly DueDate,
    decimal Amount,
    string Currency,
    string? Description = null,
    string? EvidenceId = null);

/// <summary>
/// Shared transport DTO for one private asset and its valuation/evidence posture.
/// </summary>
public sealed record PrivateAssetDto(
    string PrivateAssetId,
    string AssetName,
    PrivateAssetTypeDto AssetType,
    string? OwningEntityId,
    string? ManagerOrSponsor,
    decimal CommitmentAmount,
    decimal CalledCapital,
    decimal UnfundedCommitment,
    decimal DistributionsToDate,
    decimal LatestNav,
    DateOnly? ValuationDate,
    string? ValuationSource,
    decimal? IRR,
    decimal? MOIC,
    decimal? TVPI,
    decimal? DPI,
    string Currency,
    IReadOnlyList<PrivateAssetEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<PrivateAssetCommitmentScheduleEntryDto>? CommitmentSchedule = null);

/// <summary>
/// Read-model warning with deterministic code, severity, and remediation text for operator surfaces.
/// </summary>
public sealed record PrivateAssetValidationWarningDto(
    PrivateAssetValidationWarningCodeDto Code,
    string Message,
    string Severity,
    string? EvidenceKind = null);

/// <summary>
/// Shared private-asset read model containing the source DTO plus derived validation warnings.
/// </summary>
public sealed record PrivateAssetReadModelDto(
    PrivateAssetDto Asset,
    IReadOnlyList<PrivateAssetValidationWarningDto> Warnings,
    DateTimeOffset RefreshedAtUtc);
