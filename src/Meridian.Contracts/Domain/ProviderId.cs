namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object representing a data-provider identifier.
/// Prevents accidental interchange of provider IDs with <see cref="SymbolId"/> or <see cref="VenueCode"/>.
/// </summary>
/// <remarks>
/// Provider IDs are canonically lowercase (e.g. <c>"alpaca"</c>, <c>"stooq"</c>).
/// The constructor normalises the value to lowercase automatically.
/// </remarks>
public readonly struct ProviderId : IEquatable<ProviderId>, IComparable<ProviderId>
{
    private readonly string _value;

    /// <summary>Initializes a new <see cref="ProviderId"/>.</summary>
    /// <param name="value">The raw provider identifier (e.g. <c>"alpaca"</c>, <c>"polygon"</c>).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public ProviderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Provider ID cannot be null or whitespace.", nameof(value));
        _value = value.ToLowerInvariant();
    }

    /// <summary>Returns the canonical lowercase provider ID string.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Implicitly converts a <see cref="ProviderId"/> to its underlying string value.</summary>
    public static implicit operator string(ProviderId id) => id.Value;

    /// <summary>Explicitly converts a raw string to a <see cref="ProviderId"/>.</summary>
    public static explicit operator ProviderId(string value) => new(value);

    // Well-known provider constants for use in production code.
    /// <summary>Alpaca Markets streaming and historical provider.</summary>
    public static readonly ProviderId Alpaca = new("alpaca");
    /// <summary>Polygon.io streaming and historical provider.</summary>
    public static readonly ProviderId Polygon = new("polygon");
    /// <summary>Interactive Brokers TWS/Gateway provider.</summary>
    public static readonly ProviderId InteractiveBrokers = new("ib");
    /// <summary>NYSE direct-feed provider.</summary>
    public static readonly ProviderId Nyse = new("nyse");
    /// <summary>Stooq free end-of-day historical provider.</summary>
    public static readonly ProviderId Stooq = new("stooq");
    /// <summary>Tiingo free daily historical provider.</summary>
    public static readonly ProviderId Tiingo = new("tiingo");
    /// <summary>Finnhub provider.</summary>
    public static readonly ProviderId Finnhub = new("finnhub");
    /// <summary>Alpha Vantage historical provider.</summary>
    public static readonly ProviderId AlphaVantage = new("alphavantage");

    /// <inheritdoc/>
    public bool Equals(ProviderId other)
        => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ProviderId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc/>
    public int CompareTo(ProviderId other)
        => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc cref="Equals(ProviderId)"/>
    public static bool operator ==(ProviderId left, ProviderId right) => left.Equals(right);

    /// <inheritdoc cref="Equals(ProviderId)"/>
    public static bool operator !=(ProviderId left, ProviderId right) => !left.Equals(right);
}
