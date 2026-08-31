using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityStatusDto>))]
public enum SecurityStatusDto
{
    Active,
    Inactive,
    /// <summary>
    /// Read-tolerance member: a status written by a newer node that this node does not recognize.
    /// Treated as not-Active by filters, so unrecognized states degrade to conservative visibility
    /// instead of failing every read of the row.
    /// </summary>
    Unknown
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
    string Status)
{
    /// <summary>The source the resolver chose as the winner; set atomically when the conflict resolves.</summary>
    public string? ResolvedWinnerSource { get; init; }

    /// <summary>The actor who resolved the conflict; set atomically when the conflict resolves.</summary>
    public string? ResolvedBy { get; init; }

    /// <summary>The resolver-supplied reason; set atomically when the conflict resolves.</summary>
    public string? ResolvedReason { get; init; }

    /// <summary>When the conflict was resolved; set atomically when the conflict resolves.</summary>
    public DateTimeOffset? ResolvedAt { get; init; }
}

/// <summary>
/// Request to resolve or dismiss a golden record conflict. <see cref="ChosenWinnerSource"/> records
/// the operator-selected winning source so the resolution and its winner are captured atomically
/// with the conflict's open→resolved transition.
/// </summary>
public sealed record ResolveConflictRequest(
    Guid ConflictId,
    string Resolution,
    string ResolvedBy,
    string? Reason = null,
    string? ChosenWinnerSource = null);

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
    DateTimeOffset AsOf)
{
    public bool? IsMarginable { get; init; }
    public bool? IsShortable { get; init; }
    public bool? IsEasyToBorrow { get; init; }
    public bool? IsFractionable { get; init; }
    public decimal? MinimumOrderSize { get; init; }
    public decimal? MinimumTradeIncrement { get; init; }
    public decimal? PriceIncrement { get; init; }
}

/// <summary>
/// A single corporate action event envelope returned by the corporate actions query.
/// <para><see cref="LifecycleState"/> holds a writeable
/// <see cref="CorporateActionLifecycleStates"/> value; null reads as Confirmed for events
/// written before lifecycle support. <see cref="SupersedesCorpActId"/> marks this event as
/// an amendment (or, with a Cancelled state, a cancellation) of a prior event — the store
/// stays append-only and readers fold chains via the effective-state projector.
/// <see cref="RedemptionPricePercentOfPar"/> carries BondCall/BondMaturityRedemption
/// pricing as a percent of par.</para>
/// </summary>
/// <summary>
/// One corporate-action event. The typed columns carry the economics of the historically declared
/// event types; <paramref name="Payload"/> is the generic per-event-type envelope (a JSON object)
/// for event types with no dedicated columns — tender offers, crypto forks, returns of capital,
/// principal paydowns, option contract adjustments, delistings, and any future type — so a new
/// event type never needs another nullable column. Well-known payload keys are documented in
/// <see cref="CorporateActionPayloads"/>.
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
    decimal? RightsPerShare,
    DateOnly? RecordDate = null,
    string? LifecycleState = null,
    Guid? SupersedesCorpActId = null,
    decimal? RedemptionPricePercentOfPar = null,
    JsonElement? Payload = null,
    int PayloadSchemaVersion = CorporateActionPayloads.CurrentSchemaVersion);

/// <summary>
/// Preferred-equity-specific terms returned by the preferred-terms query.
/// </summary>
public sealed record PreferredEquityTermsDto(
    Guid SecurityId,
    string? Classification,
    decimal? DividendRate,
    string? DividendType,
    bool? IsCumulative,
    decimal? RedemptionPrice,
    DateOnly? RedemptionDate,
    DateOnly? CallableDate,
    bool? ParticipatesInCommonDividends,
    decimal? AdditionalDividendThreshold,
    string? LiquidationPreferenceKind,
    decimal? LiquidationPreferenceMultiple,
    long Version);

/// <summary>
/// Convertible-equity-specific terms returned by the convertible-equity-terms query.
/// </summary>
public sealed record ConvertibleEquityTermsDto(
    Guid SecurityId,
    string? Classification,
    Guid? UnderlyingSecurityId,
    decimal? ConversionRatio,
    decimal? ConversionPrice,
    DateOnly? ConversionStartDate,
    DateOnly? ConversionEndDate,
    long Version);
