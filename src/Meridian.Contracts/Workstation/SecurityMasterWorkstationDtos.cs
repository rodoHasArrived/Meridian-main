using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Workstation-facing Security Master classification summary for governance surfaces.
/// </summary>
public sealed record SecurityClassificationSummaryDto(
    string AssetClass,
    string? SubType,
    string? PrimaryIdentifierKind,
    string? PrimaryIdentifierValue);

/// <summary>
/// Workstation-facing Security Master economic definition summary.
/// Includes optional classification fields populated when the full economic definition has been rebuilt.
/// </summary>
public sealed record SecurityEconomicDefinitionSummaryDto(
    string Currency,
    long Version,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? SubType = null,
    string? AssetFamily = null,
    string? IssuerType = null);

/// <summary>
/// Workstation-facing Security Master row used by governance and search surfaces.
/// </summary>
public sealed record SecurityMasterWorkstationDto(
    Guid SecurityId,
    string DisplayName,
    SecurityStatusDto Status,
    SecurityClassificationSummaryDto Classification,
    SecurityEconomicDefinitionSummaryDto EconomicDefinition);

/// <summary>
/// Governance drill-in showing the complete identifier and alias picture for a single security.
/// Built from the full <see cref="SecurityDetailDto"/> so it can be fetched in a single query.
/// </summary>
public sealed record SecurityIdentityDrillInDto(
    Guid SecurityId,
    string DisplayName,
    string AssetClass,
    SecurityStatusDto Status,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<SecurityIdentifierDto> Identifiers,
    IReadOnlyList<SecurityAliasDto> Aliases);
