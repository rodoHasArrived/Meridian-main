namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object representing a normalized, canonical market instrument symbol.
/// Distinct from <see cref="SymbolId"/> (the raw provider symbol) to prevent accidental
/// use of un-normalized symbols where a canonical symbol is expected and vice-versa.
/// </summary>
/// <remarks>
/// Use this type wherever a symbol has been through the canonicalization pipeline
/// (e.g. "AAPL.US" from Stooq → "AAPL" as the canonical form).
/// The compiler will then enforce that raw and canonical symbols are never silently interchanged.
/// Equality and comparison are case-insensitive; values are stored in uppercase.
/// </remarks>
public readonly struct CanonicalSymbol : IEquatable<CanonicalSymbol>, IComparable<CanonicalSymbol>
{
    private readonly string _value;

    /// <summary>Initializes a new <see cref="CanonicalSymbol"/> with the given canonical ticker string.</summary>
    /// <param name="value">The canonical ticker symbol (e.g. <c>"AAPL"</c>, <c>"SPY"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public CanonicalSymbol(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Canonical symbol cannot be null or whitespace.", nameof(value));
        _value = value.ToUpperInvariant();
    }

    /// <summary>Returns the canonical ticker string (uppercase).</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Implicitly converts a <see cref="CanonicalSymbol"/> to its underlying string value.</summary>
    public static implicit operator string(CanonicalSymbol s) => s.Value;

    /// <summary>Explicitly converts a raw string to a <see cref="CanonicalSymbol"/>.</summary>
    public static explicit operator CanonicalSymbol(string value) => new(value);

    /// <inheritdoc/>
    public bool Equals(CanonicalSymbol other)
        => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CanonicalSymbol other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc/>
    public int CompareTo(CanonicalSymbol other)
        => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="Equals(CanonicalSymbol)"/>
    public static bool operator ==(CanonicalSymbol left, CanonicalSymbol right) => left.Equals(right);

    /// <inheritdoc cref="Equals(CanonicalSymbol)"/>
    public static bool operator !=(CanonicalSymbol left, CanonicalSymbol right) => !left.Equals(right);
}
