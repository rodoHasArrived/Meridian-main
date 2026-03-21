namespace Meridian.Contracts.Domain;

/// <summary>
/// Zero-cost value object representing a market-data subscription identifier.
/// Prevents accidental interchange of subscription IDs with other integer-typed identifiers
/// such as sequence numbers or request IDs.
/// </summary>
/// <remarks>
/// Subscription IDs are returned by <c>SubscribeTrades</c> / <c>SubscribeMarketDepth</c>
/// on <c>IMarketDataClient</c> implementations and must be passed back to unsubscribe.
/// Wrapping the bare <see langword="int"/> in this type makes the intent explicit at
/// call sites and prevents silent mixups with unrelated integer-typed parameters.
/// </remarks>
public readonly struct SubscriptionId : IEquatable<SubscriptionId>, IComparable<SubscriptionId>
{
    /// <summary>Initializes a new <see cref="SubscriptionId"/> with the given integer value.</summary>
    /// <param name="value">The subscription handle returned by the provider.</param>
    public SubscriptionId(int value)
    {
        Value = value;
    }

    /// <summary>Returns the underlying subscription handle integer.</summary>
    public int Value { get; }

    /// <summary>Implicitly converts a <see cref="SubscriptionId"/> to its underlying integer value.</summary>
    public static implicit operator int(SubscriptionId id) => id.Value;

    /// <summary>Explicitly converts an integer to a <see cref="SubscriptionId"/>.</summary>
    public static explicit operator SubscriptionId(int value) => new(value);

    /// <inheritdoc/>
    public bool Equals(SubscriptionId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SubscriptionId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(SubscriptionId other) => Value.CompareTo(other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();

    /// <inheritdoc cref="Equals(SubscriptionId)"/>
    public static bool operator ==(SubscriptionId left, SubscriptionId right) => left.Equals(right);

    /// <inheritdoc cref="Equals(SubscriptionId)"/>
    public static bool operator !=(SubscriptionId left, SubscriptionId right) => !left.Equals(right);
}
