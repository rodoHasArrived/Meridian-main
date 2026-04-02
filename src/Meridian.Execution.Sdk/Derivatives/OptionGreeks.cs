namespace Meridian.Execution.Sdk.Derivatives;

/// <summary>
/// A point-in-time snapshot of an option position's Greek sensitivities.
/// All values are expressed in the standard "per share" convention;
/// multiply by <see cref="OptionDetails.ContractMultiplier"/> for the full contract impact.
/// </summary>
/// <param name="Delta">
///     Rate of change of option price with respect to a $1 move in the underlying.
///     Range: [−1, 0] for puts; [0, 1] for calls.
/// </param>
/// <param name="Gamma">
///     Rate of change of delta with respect to a $1 move in the underlying.
///     Always positive for long options.
/// </param>
/// <param name="Theta">
///     Time decay per calendar day (negative for long positions; the option loses value daily).
/// </param>
/// <param name="Vega">
///     Sensitivity to a 1 % (100-basis-point) change in implied volatility.
/// </param>
/// <param name="Rho">
///     Sensitivity to a 1 % change in the risk-free interest rate.
/// </param>
/// <param name="ImpliedVolatility">
///     Implied annualized volatility derived from the current market price (decimal fraction;
///     e.g., 0.25 = 25 %).
/// </param>
/// <param name="AsOf">Timestamp of the Greeks snapshot.</param>
public sealed record OptionGreeks(
    double Delta,
    double Gamma,
    double Theta,
    double Vega,
    double Rho,
    double ImpliedVolatility,
    DateTimeOffset AsOf)
{
    /// <summary>
    /// Dollar-delta: the P&amp;L sensitivity to a $1 move, scaled for a given number of
    /// <paramref name="contracts"/> with the specified <paramref name="multiplier"/>.
    /// </summary>
    public double DollarDelta(int contracts, int multiplier = 100) =>
        Delta * contracts * multiplier;

    /// <summary>
    /// Dollar-theta: daily time-decay cost, scaled for a given number of
    /// <paramref name="contracts"/> with the specified <paramref name="multiplier"/>.
    /// </summary>
    public double DollarTheta(int contracts, int multiplier = 100) =>
        Theta * contracts * multiplier;
}
