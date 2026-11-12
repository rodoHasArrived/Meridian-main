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
    /// IHS Markit LoanX ID — the market-standard identifier for syndicated loans ("LX" followed by
    /// six digits).
    /// </summary>
    LoanX,
    /// <summary>
    /// CUSIP-format loan identifier (CLIP) assigned to syndicated loan facilities and tranches —
    /// nine alphanumeric characters.
    /// </summary>
    Clip,
    /// <summary>
    /// Listing identity — a ticker on a specific ISO 10383 market, written <c>TICKER:MIC</c>
    /// (for example <c>AAPL:XNAS</c>); see <see cref="Meridian.Contracts.Domain.ListingId"/>.
    /// </summary>
    ListingId,
    /// <summary>
    /// Unique Transaction Identifier (ISO 23897) for regulatory derivatives reporting — up to 52
    /// upper-case alphanumeric characters whose first 20 are the generating entity's LEI. Names
    /// exactly one trade, so it participates in duplicate detection like other canonical kinds.
    /// </summary>
    Uti,
    /// <summary>
    /// Unique Product Identifier (ISO 4914), issued by the ANNA Derivatives Service Bureau — 12
    /// characters: "QZ", nine digits/consonants, and an ISO 7064 MOD 31,30 check character. Names an
    /// OTC derivative <em>product</em>, so many contracts legitimately share one value.
    /// </summary>
    Upi,
    /// <summary>
    /// IHS Markit RED reference-entity code (RED6) — six alphanumeric characters naming a CDS
    /// reference entity. Shared by every contract on that entity.
    /// </summary>
    MarkitRedEntity,
    /// <summary>
    /// IHS Markit RED pair code (RED9) — nine alphanumeric characters: the six-character entity code
    /// plus a three-character reference-obligation suffix. Shared by every contract on that pair.
    /// </summary>
    MarkitRedPair,
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
/// Raised when an alias write cannot be recorded as a revision of that alias's history: the alias ID
/// is already recorded against a different security (an alias ID stays bound to the security it was
/// first recorded against), or the store could not confirm that the write was persisted.
/// </summary>
public sealed class SecurityAliasHistoryConflictException : InvalidOperationException
{
    public SecurityAliasHistoryConflictException(Guid aliasId)
        : base(
            $"Alias '{aliasId}' conflicts with its recorded history. " +
            "An alias ID stays bound to the security it was first recorded against; corrections append an alias revision, and re-pointing requires a new alias ID.")
    {
        AliasId = aliasId;
    }

    public Guid AliasId { get; }
}
