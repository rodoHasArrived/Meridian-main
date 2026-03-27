namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object representing a raw, provider-independent market instrument symbol.
/// This is the "Symbol (raw)" type in the typed symbol hierarchy.
/// Prevents accidental interchange of symbols with other string-typed IDs
/// (e.g. <see cref="ProviderId"/> or <see cref="VenueCode"/>).
/// </summary>
/// <remarks>
/// <para>
/// The three symbol types form a strict hierarchy:
/// </para>
/// <list type="number">
///   <item><description><see cref="ProviderSymbol"/> — raw, provider-scoped ticker (before mapping, e.g. <c>"AAPL.US"</c> from Stooq).</description></item>
///   <item><description><see cref="SymbolId"/> — normalized ticker, provider-independent (e.g. <c>"AAPL"</c> after stripping exchange suffixes).</description></item>
///   <item><description><see cref="CanonicalSymbol"/> — fully resolved, system-wide canonical symbol produced by the canonicalization pipeline.</description></item>
/// </list>
/// <para>
/// Use this type on API boundaries and domain models instead of bare <see langword="string"/>
/// to make the compiler enforce that a symbol is never used where a provider ID or venue is expected.
/// Equality and comparison are case-insensitive to match industry conventions.
/// </para>
/// </remarks>
public readonly struct SymbolId : IEquatable<SymbolId>, IComparable<SymbolId>
{
    private readonly string _value;

    /// <summary>Initializes a new <see cref="SymbolId"/> with the given ticker string.</summary>
    /// <param name="value">The raw ticker symbol (e.g. <c>"SPY"</c>, <c>"AAPL"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public SymbolId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Symbol cannot be null or whitespace.", nameof(value));
        _value = value.ToUpperInvariant();
    }

    /// <summary>Returns the raw ticker string.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Implicitly converts a <see cref="SymbolId"/> to its underlying string value.</summary>
    public static implicit operator string(SymbolId id) => id.Value;

    /// <summary>Explicitly converts a raw string to a <see cref="SymbolId"/>.</summary>
    public static explicit operator SymbolId(string value) => new(value);

    /// <inheritdoc/>
    public bool Equals(SymbolId other)
        => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SymbolId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc/>
    public int CompareTo(SymbolId other)
        => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="Equals(SymbolId)"/>
    public static bool operator ==(SymbolId left, SymbolId right) => left.Equals(right);

    /// <inheritdoc cref="Equals(SymbolId)"/>
    public static bool operator !=(SymbolId left, SymbolId right) => !left.Equals(right);
}
