namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object pairing a provider identifier with a raw, provider-specific
/// symbol string. Represents the symbol exactly as the upstream provider exposes it —
/// before any canonicalization is applied.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="ProviderSymbol"/> at provider boundaries (ingestion adapters, backfill
/// clients, symbol-search providers) where the raw, vendor-specific ticker must be carried
/// alongside the provider identity. Examples:
/// </para>
/// <list type="bullet">
///   <item><description>Stooq yields <c>"AAPL.US"</c> for the symbol later canonicalized to <c>"AAPL"</c>.</description></item>
///   <item><description>Polygon yields <c>"X:BTCUSD"</c> for a crypto pair.</description></item>
///   <item><description>IB yields <c>"AAPL"</c> directly, but provider context is still needed for resolution.</description></item>
/// </list>
/// <para>
/// The three symbol types form a strict hierarchy:
/// </para>
/// <list type="number">
///   <item><description><see cref="ProviderSymbol"/> — raw, provider-scoped ticker (before mapping).</description></item>
///   <item><description><see cref="SymbolId"/> — raw canonical ticker, provider-independent (e.g. after normalization).</description></item>
///   <item><description><see cref="CanonicalSymbol"/> — fully resolved, system-wide canonical symbol (e.g. after the canonicalization pipeline).</description></item>
/// </list>
/// <para>
/// Equality is case-insensitive on both <see cref="Provider"/> and <see cref="Symbol"/> fields.
/// </para>
/// </remarks>
public readonly struct ProviderSymbol : IEquatable<ProviderSymbol>, IComparable<ProviderSymbol>
{
    /// <summary>The provider that owns this symbol representation.</summary>
    public ProviderId Provider { get; }

    /// <summary>The raw symbol string as supplied by the provider.</summary>
    public SymbolId Symbol { get; }

    /// <summary>
    /// Initializes a new <see cref="ProviderSymbol"/>.
    /// </summary>
    /// <param name="provider">Provider identifier (e.g. <see cref="ProviderId.Stooq"/>).</param>
    /// <param name="symbol">Raw symbol from the provider (e.g. <c>"AAPL.US"</c>).</param>
    public ProviderSymbol(ProviderId provider, SymbolId symbol)
    {
        Provider = provider;
        Symbol = symbol;
    }

    /// <summary>
    /// Convenience constructor that wraps a raw string symbol.
    /// </summary>
    /// <param name="provider">Provider identifier.</param>
    /// <param name="rawSymbol">Raw symbol string from the provider.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawSymbol"/> is null or whitespace.</exception>
    public ProviderSymbol(ProviderId provider, string rawSymbol)
        : this(provider, new SymbolId(rawSymbol))
    {
    }

    /// <summary>
    /// Deconstructs the value into its provider and symbol components.
    /// </summary>
    public void Deconstruct(out ProviderId provider, out SymbolId symbol)
    {
        provider = Provider;
        symbol = Symbol;
    }

    /// <inheritdoc/>
    public bool Equals(ProviderSymbol other)
        => Provider.Equals(other.Provider) && Symbol.Equals(other.Symbol);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ProviderSymbol other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Provider.GetHashCode(), Symbol.GetHashCode());

    /// <inheritdoc/>
    public int CompareTo(ProviderSymbol other)
    {
        var providerCmp = Provider.CompareTo(other.Provider);
        return providerCmp != 0 ? providerCmp : Symbol.CompareTo(other.Symbol);
    }

    /// <summary>Returns a human-readable representation in the form <c>"provider:SYMBOL"</c>.</summary>
    public override string ToString() => $"{Provider.Value}:{Symbol.Value}";

    /// <inheritdoc cref="Equals(ProviderSymbol)"/>
    public static bool operator ==(ProviderSymbol left, ProviderSymbol right) => left.Equals(right);

    /// <inheritdoc cref="Equals(ProviderSymbol)"/>
    public static bool operator !=(ProviderSymbol left, ProviderSymbol right) => !left.Equals(right);
}
