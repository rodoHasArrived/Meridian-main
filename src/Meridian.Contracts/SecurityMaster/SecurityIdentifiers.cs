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
    /// <summary>OCC/OSI listed-option contract symbol such as AAPL240621C00150000.</summary>
    OccOptionSymbol,
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
    Ric,
    /// <summary>SEC Central Index Key — identifies an EDGAR filer or issuer, not a standalone tradable security.</summary>
    Cik,
    /// <summary>
    /// Read-tolerance member: a kind written by a newer node that this node does not recognize.
    /// Rows carrying it stay readable (mixed-version rollout) but cannot be written back as-is —
    /// the command mapping rejects <see cref="Unknown"/> so unrecognized kinds are never silently
    /// re-persisted.
    /// </summary>
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAliasScope>))]
public enum SecurityAliasScope
{
    Operations,
    Collector,
    Execution,
    Migration,
    /// <summary>Read-tolerance member for scopes written by newer nodes; see <see cref="SecurityIdentifierKind.Unknown"/>.</summary>
    Unknown
}

public sealed record SecurityIdentifierDto(
    SecurityIdentifierKind Kind,
    string Value,
    bool IsPrimary,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo = null,
    string? Provider = null,
    string? NormalizedValue = null,
    string? NormalizedProvider = null);

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

/// <summary>
/// Raised when a request would mutate an existing alias row without an append-only alias revision.
/// The current projection table is consumed by recorded-as-of reads, so an in-place correction
/// would rewrite history and must be reported as a state conflict.
/// </summary>
public sealed class SecurityAliasHistoryConflictException : InvalidOperationException
{
    public SecurityAliasHistoryConflictException(Guid aliasId)
        : base(
            $"Alias '{aliasId}' already exists with different recorded facts. " +
            "In-place replacement or retirement is unavailable until append-only alias revisions are implemented.")
    {
        AliasId = aliasId;
    }

    public Guid AliasId { get; }
}
