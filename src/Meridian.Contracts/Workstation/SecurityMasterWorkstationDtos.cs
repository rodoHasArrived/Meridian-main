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
/// </summary>
public sealed record SecurityEconomicDefinitionSummaryDto(
    string Currency,
    long Version,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>
/// Workstation-facing Security Master row used by governance and search surfaces.
/// </summary>
public sealed record SecurityMasterWorkstationDto(
    Guid SecurityId,
    string DisplayName,
    SecurityStatusDto Status,
    SecurityClassificationSummaryDto Classification,
    SecurityEconomicDefinitionSummaryDto EconomicDefinition);
