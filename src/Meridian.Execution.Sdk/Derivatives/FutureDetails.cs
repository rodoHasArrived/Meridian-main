namespace Meridian.Execution.Sdk.Derivatives;

/// <summary>
/// Immutable specification for an exchange-traded futures contract.
/// </summary>
/// <param name="UnderlyingSymbol">The root underlying (e.g., "ES" for E-mini S&amp;P 500).</param>
/// <param name="ContractMonth">Delivery/expiry month (first day of delivery month convention).</param>
/// <param name="TickSize">Minimum price increment (e.g., 0.25 for ES).</param>
/// <param name="TickValue">Dollar value of one tick move (e.g., $12.50 for ES).</param>
/// <param name="ContractMultiplier">
///     Dollar multiplier per point of underlying (e.g., $50 for ES).
///     Relationship: TickValue = TickSize × ContractMultiplier.
/// </param>
/// <param name="Exchange">Exchange on which the contract is listed (e.g., "CME").</param>
/// <param name="Currency">Settlement currency (e.g., "USD").</param>
public sealed record FutureDetails(
    string UnderlyingSymbol,
    DateOnly ContractMonth,
    decimal TickSize,
    decimal TickValue,
    decimal ContractMultiplier,
    string Exchange,
    string Currency = "USD")
{
    /// <summary>
    /// Calculates the dollar P&amp;L of a price move from <paramref name="entryPrice"/>
    /// to <paramref name="currentPrice"/> for <paramref name="contracts"/> long contracts.
    /// </summary>
    public decimal PnlForPriceMove(decimal entryPrice, decimal currentPrice, int contracts) =>
        (currentPrice - entryPrice) * ContractMultiplier * contracts;

    /// <summary>
    /// Number of ticks between <paramref name="fromPrice"/> and <paramref name="toPrice"/>.
    /// </summary>
    public decimal TicksBetween(decimal fromPrice, decimal toPrice) =>
        TickSize == 0m ? 0m : Math.Abs(toPrice - fromPrice) / TickSize;

    /// <summary>
    /// Approximate number of calendar days remaining until contract month from <paramref name="asOf"/>.
    /// </summary>
    public int DaysToExpiry(DateOnly asOf) => ContractMonth.DayNumber - asOf.DayNumber;
}
