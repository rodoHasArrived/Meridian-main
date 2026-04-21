namespace Meridian.Execution.MultiCurrency;

/// <summary>
/// An exchange rate between two currencies at a specific point in time.
/// </summary>
/// <param name="BaseCurrency">The currency being priced (e.g., "EUR").</param>
/// <param name="QuoteCurrency">The pricing currency (e.g., "USD").</param>
/// <param name="Rate">Units of <see cref="QuoteCurrency"/> per one unit of <see cref="BaseCurrency"/>.</param>
/// <param name="AsOf">Timestamp at which this rate is valid.</param>
public sealed record FxRate(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    DateTimeOffset AsOf)
{
    /// <summary>
    /// Converts an amount expressed in <see cref="BaseCurrency"/> to <see cref="QuoteCurrency"/>.
    /// </summary>
    public decimal Convert(decimal amount) => amount * Rate;

    /// <summary>
    /// Converts an amount expressed in <see cref="QuoteCurrency"/> back to <see cref="BaseCurrency"/>.
    /// </summary>
    public decimal ConvertInverse(decimal amount)
    {
        if (Rate == 0m) throw new InvalidOperationException("Cannot invert a zero FX rate.");
        return amount / Rate;
    }

    /// <summary>
    /// Returns the inverse rate (QuoteCurrency → BaseCurrency).
    /// </summary>
    public FxRate Inverse() => new(QuoteCurrency, BaseCurrency, 1m / Rate, AsOf);

    /// <summary>
    /// Returns a string of the form <c>EUR/USD 1.0850 @ 2024-01-15</c>.
    /// </summary>
    public override string ToString() =>
        $"{BaseCurrency}/{QuoteCurrency} {Rate:F6} @ {AsOf:yyyy-MM-dd}";
}
