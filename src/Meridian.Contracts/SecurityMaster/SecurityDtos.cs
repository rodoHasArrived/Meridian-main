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
