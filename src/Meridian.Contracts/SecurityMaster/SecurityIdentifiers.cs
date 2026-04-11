using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityIdentifierKind>))]
public enum SecurityIdentifierKind
{
    Ticker,
    Isin,
    Cusip,
    Sedol,
    Figi,
    ProviderSymbol,
    InternalCode,
    /// <summary>Legal Entity Identifier (ISO 17442) — 20-char alphanumeric; required for OTC derivatives regulatory reporting.</summary>
    Lei,
    /// <summary>Refinitiv/LSEG PermID — stable cross-asset persistent identifier.</summary>
    PermId,
    /// <summary>Bloomberg Global Identifier (BBGID) — stable across corporate actions; distinct from ticker.</summary>
    Bbgid,
    /// <summary>Wertpapierkennnummer — German/Austrian exchange standard (6 alphanumeric chars).</summary>
    Wkn,
    /// <summary>Valoren — Swiss SIX exchange security number.</summary>
    Valoren,
    /// <summary>Meridian-stable ticker that survives corporate actions (Bloomberg PermTicker convention).</summary>
    PermTicker,
    /// <summary>Reuters Instrument Code — used by Refinitiv Eikon / LSEG feeds.</summary>
    Ric
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAliasScope>))]
public enum SecurityAliasScope
{
    Operations,
    Collector,
    Execution,
    Migration
}

public sealed record SecurityIdentifierDto(
    SecurityIdentifierKind Kind,
    string Value,
    bool IsPrimary,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo = null,
    string? Provider = null);

public sealed record SecurityAliasDto(
    Guid AliasId,
    Guid SecurityId,
    string AliasKind,
    string AliasValue,
    string? Provider,
    SecurityAliasScope Scope,
    string? Reason,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsEnabled);
