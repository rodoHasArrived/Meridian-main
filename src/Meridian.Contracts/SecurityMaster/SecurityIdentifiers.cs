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
    InternalCode
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
    DateTimeOffset? ValidTo,
    string? Provider);

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
