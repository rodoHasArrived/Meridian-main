namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object representing a trading-venue or exchange code.
/// Prevents accidental interchange of venue codes with <see cref="SymbolId"/> or <see cref="ProviderId"/>.
/// </summary>
/// <remarks>
/// Venue codes follow MIC (ISO 10383) convention in uppercase (e.g. <c>"XNAS"</c>, <c>"XNYS"</c>).
/// Common short-form names (e.g. <c>"NYSE"</c>, <c>"NASDAQ"</c>) are also accepted and stored as-is.
/// </remarks>
public readonly struct VenueCode : IEquatable<VenueCode>, IComparable<VenueCode>
{
    private readonly string _value;

    /// <summary>Initializes a new <see cref="VenueCode"/>.</summary>
    /// <param name="value">The venue or exchange code (e.g. <c>"NYSE"</c>, <c>"XNAS"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public VenueCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Venue code cannot be null or whitespace.", nameof(value));
        _value = value.ToUpperInvariant();
    }

    /// <summary>Returns the venue code string (uppercase).</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Implicitly converts a <see cref="VenueCode"/> to its underlying string value.</summary>
    public static implicit operator string(VenueCode code) => code.Value;

    /// <summary>Explicitly converts a raw string to a <see cref="VenueCode"/>.</summary>
    public static explicit operator VenueCode(string value) => new(value);

    // Well-known venue constants for use in production code.
    /// <summary>New York Stock Exchange.</summary>
    public static readonly VenueCode Nyse = new("NYSE");
    /// <summary>NASDAQ National Market.</summary>
    public static readonly VenueCode Nasdaq = new("NASDAQ");
    /// <summary>Chicago Board Options Exchange.</summary>
    public static readonly VenueCode Cboe = new("CBOE");
    /// <summary>NYSE Arca electronic exchange.</summary>
    public static readonly VenueCode NyseArca = new("ARCA");
    /// <summary>Synthetic marker for an unknown or unavailable venue.</summary>
    public static readonly VenueCode Unknown = new("UNKNOWN");

    /// <inheritdoc/>
    public bool Equals(VenueCode other)
        => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VenueCode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc/>
    public int CompareTo(VenueCode other)
        => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="Equals(VenueCode)"/>
    public static bool operator ==(VenueCode left, VenueCode right) => left.Equals(right);

    /// <inheritdoc cref="Equals(VenueCode)"/>
    public static bool operator !=(VenueCode left, VenueCode right) => !left.Equals(right);
}
