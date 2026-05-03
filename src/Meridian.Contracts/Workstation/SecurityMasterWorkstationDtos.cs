using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Workstation-facing Security Master classification summary for governance surfaces.
/// </summary>
public sealed record SecurityClassificationSummaryDto(
    string AssetClass,
    string? SubType,
    string? PrimaryIdentifierKind,
    string? PrimaryIdentifierValue,
    string? MatchedIdentifierKind = null,
    string? MatchedIdentifierValue = null,
    string? MatchedProvider = null,
    /// <summary>ISO 3166-1 alpha-2 country code for governance risk routing.</summary>
    string? RiskCountry = null,
    /// <summary>Issuer type classification (e.g. Sovereign, Corporate, Municipal).</summary>
    string? IssuerType = null,
    /// <summary>Canonical type name within the asset class (e.g. CommonShare, Adr, ReitShare).</summary>
    string? TypeName = null);

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
    string? IssuerType = null,
    /// <summary>ISO 3166-1 alpha-2 country code for governance risk routing.</summary>
    string? RiskCountry = null,
    /// <summary>Canonical type name within the asset class (e.g. CommonShare, Adr, ReitShare).</summary>
    string? TypeName = null);

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
/// CommonTerms fields (IssuerName, CountryOfRisk, PrimaryListingMic, SettlementCycleDays) are
/// extracted from the JSON blob when available.
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
    IReadOnlyList<SecurityAliasDto> Aliases,
    /// <summary>Issuer legal name from CommonTerms, if populated.</summary>
    string? IssuerName = null,
    /// <summary>ISO 3166-1 alpha-2 country of risk from CommonTerms, if populated.</summary>
    string? CountryOfRisk = null,
    /// <summary>Primary listing MIC from CommonTerms, if populated.</summary>
    string? PrimaryListingMic = null,
    /// <summary>Standard settlement cycle in business days from CommonTerms, if populated.</summary>
    int? SettlementCycleDays = null);
