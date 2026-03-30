using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityStatusDto>))]
public enum SecurityStatusDto
{
    Active,
    Inactive
}

public sealed record SecuritySummaryDto(
    Guid SecurityId,
    string AssetClass,
    SecurityStatusDto Status,
    string DisplayName,
    string PrimaryIdentifier,
    string Currency,
    long Version);

public sealed record SecurityDetailDto(
    Guid SecurityId,
    string AssetClass,
    SecurityStatusDto Status,
    string DisplayName,
    string Currency,
    JsonElement CommonTerms,
    JsonElement AssetSpecificTerms,
    IReadOnlyList<SecurityIdentifierDto> Identifiers,
    IReadOnlyList<SecurityAliasDto> Aliases,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>
/// A field-level or identifier-level conflict detected between providers for the same security.
/// </summary>
public sealed record SecurityMasterConflict(
    Guid ConflictId,
    Guid SecurityId,
    string ConflictKind,
    string FieldPath,
    string ProviderA,
    string ValueA,
    string ProviderB,
    string ValueB,
    DateTimeOffset DetectedAt,
    string Status);

/// <summary>
/// Request to resolve or dismiss a golden record conflict.
/// </summary>
public sealed record ResolveConflictRequest(
    Guid ConflictId,
    string Resolution,
    string ResolvedBy,
    string? Reason = null);

public sealed record SecurityProjectionRecord(
    Guid SecurityId,
    string AssetClass,
    SecurityStatusDto Status,
    string DisplayName,
    string Currency,
    string PrimaryIdentifierKind,
    string PrimaryIdentifierValue,
    JsonElement CommonTerms,
    JsonElement AssetSpecificTerms,
    JsonElement Provenance,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<SecurityIdentifierDto> Identifiers,
    IReadOnlyList<SecurityAliasDto> Aliases);

public sealed record SecurityEconomicDefinitionRecord(
    Guid SecurityId,
    string AssetClass,
    string? AssetFamily,
    string SubType,
    string TypeName,
    string? IssuerType,
    string? RiskCountry,
    SecurityStatusDto Status,
    string DisplayName,
    string Currency,
    JsonElement Classification,
    JsonElement CommonTerms,
    JsonElement EconomicTerms,
    JsonElement Provenance,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<SecurityIdentifierDto> Identifiers,
    string? LegacyAssetClass,
    JsonElement? LegacyAssetSpecificTerms);

public sealed record SecuritySnapshotRecord(
    Guid SecurityId,
    long Version,
    DateTimeOffset SnapshotTimestamp,
    JsonElement Payload);

/// <summary>
/// Trading parameters for an instrument, used for order validation and fill-price rounding.
/// </summary>
public sealed record TradingParametersDto(
    Guid SecurityId,
    decimal? LotSize,
    decimal? TickSize,
    decimal? ContractMultiplier,
    decimal? MarginRequirementPct,
    string? TradingHoursUtc,
    decimal? CircuitBreakerThresholdPct,
    DateTimeOffset AsOf);

/// <summary>
/// A single corporate action event envelope returned by the corporate actions query.
/// </summary>
public sealed record CorporateActionDto(
    Guid CorpActId,
    Guid SecurityId,
    string EventType,
    DateOnly ExDate,
    DateOnly? PayDate,
    decimal? DividendPerShare,
    string? Currency,
    decimal? SplitRatio,
    Guid? NewSecurityId,
    decimal? DistributionRatio,
    Guid? AcquirerSecurityId,
    decimal? ExchangeRatio,
    decimal? SubscriptionPricePerShare,
    decimal? RightsPerShare);
