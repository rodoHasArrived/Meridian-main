namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object representing a data-stream identifier.
/// Prevents accidental interchange of stream IDs with <see cref="SymbolId"/>,
/// <see cref="ProviderId"/>, or <see cref="VenueCode"/>.
/// </summary>
/// <remarks>
/// Stream IDs are provider-specific opaque identifiers (e.g. a WebSocket channel name
/// or a subscription correlation token). They are treated as case-sensitive strings
/// to preserve exact provider values.
/// </remarks>
public readonly struct StreamId : IEquatable<StreamId>, IComparable<StreamId>
{
    private readonly string _value;

    /// <summary>Initializes a new <see cref="StreamId"/>.</summary>
    /// <param name="value">The raw stream identifier (e.g. <c>"T.SPY"</c>, <c>"trades-AAPL"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public StreamId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Stream ID cannot be null or whitespace.", nameof(value));
        _value = value;
    }

    /// <summary>Returns the raw stream identifier string.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Implicitly converts a <see cref="StreamId"/> to its underlying string value.</summary>
    public static implicit operator string(StreamId id) => id.Value;

    /// <summary>Explicitly converts a raw string to a <see cref="StreamId"/>.</summary>
    public static explicit operator StreamId(string value) => new(value);

    /// <inheritdoc/>
    public bool Equals(StreamId other)
        => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StreamId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public int CompareTo(StreamId other)
        => string.Compare(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="Equals(StreamId)"/>
    public static bool operator ==(StreamId left, StreamId right) => left.Equals(right);

    /// <inheritdoc cref="Equals(StreamId)"/>
    public static bool operator !=(StreamId left, StreamId right) => !left.Equals(right);
}
