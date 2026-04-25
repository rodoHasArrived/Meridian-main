namespace Meridian.Execution.Sdk.Derivatives;

/// <summary>
/// Specifies which side of the options market a contract grants access to.
/// </summary>
public enum OptionRight
{
    /// <summary>The holder may buy the underlying.</summary>
    Call,

    /// <summary>The holder may sell the underlying.</summary>
    Put,
}

/// <summary>
/// Exercise style of an option contract.
/// </summary>
public enum OptionStyle
{
    /// <summary>May only be exercised at expiry (European-style).</summary>
    European,

    /// <summary>May be exercised at any time before or at expiry (American-style).</summary>
    American,
}

/// <summary>
/// Immutable specification for an options contract.
/// </summary>
/// <param name="UnderlyingSymbol">The underlying instrument (e.g., "AAPL").</param>
/// <param name="Strike">The strike price.</param>
/// <param name="Expiry">Expiration date.</param>
/// <param name="Right">Call or Put.</param>
/// <param name="Style">American or European.</param>
/// <param name="ContractMultiplier">
///     Number of underlying shares per contract. Standard equity options use 100.
/// </param>
/// <param name="Exchange">Optional exchange on which the option is listed.</param>
public sealed record OptionDetails(
    string UnderlyingSymbol,
    decimal Strike,
    DateOnly Expiry,
    OptionRight Right,
    OptionStyle Style = OptionStyle.American,
    int ContractMultiplier = 100,
    string? Exchange = null)
{
    /// <summary>
    /// Computes the intrinsic value of the option given the current underlying price.
    /// Returns 0 when the option is out of the money.
    /// </summary>
    public decimal IntrinsicValue(decimal underlyingPrice) => Right switch
    {
        OptionRight.Call => Math.Max(0m, underlyingPrice - Strike),
        OptionRight.Put => Math.Max(0m, Strike - underlyingPrice),
        _ => 0m
    };

    /// <summary>
    /// Indicates whether the option is in the money given the current underlying price.
    /// </summary>
    public bool IsInTheMoney(decimal underlyingPrice) => IntrinsicValue(underlyingPrice) > 0m;

    /// <summary>
    /// Approximate number of calendar days remaining until expiry from <paramref name="asOf"/>.
    /// </summary>
    public int DaysToExpiry(DateOnly asOf) => Expiry.DayNumber - asOf.DayNumber;

    /// <summary>
    /// Approximate time-to-expiry in years (calendar days / 365).
    /// </summary>
    public double TimeToExpiryYears(DateOnly asOf) => DaysToExpiry(asOf) / 365.0;
}
