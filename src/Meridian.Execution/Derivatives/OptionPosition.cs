using Meridian.Execution.Sdk.Derivatives;

namespace Meridian.Execution.Derivatives;

/// <summary>
/// An option position carrying contract details and live Greek sensitivities.
/// Implements <see cref="IDerivativePosition"/> so it can participate in
/// portfolio-level P&amp;L and margin calculations alongside equity positions.
/// </summary>
public sealed class OptionPosition : IDerivativePosition
{
    /// <summary>
    /// Initialises an option position.
    /// </summary>
    /// <param name="symbol">Option contract symbol (e.g., "AAPL 240119C00150000").</param>
    /// <param name="contracts">Number of contracts (positive = long, negative = short).</param>
    /// <param name="averageCostBasis">Average entry premium paid per contract.</param>
    /// <param name="markPrice">Current market price of the option.</param>
    /// <param name="realizedPnl">Realized P&amp;L from prior partial closes.</param>
    /// <param name="details">Contract specification (strike, expiry, right, style).</param>
    /// <param name="greeks">Optional current Greek sensitivities.</param>
    public OptionPosition(
        string symbol,
        long contracts,
        decimal averageCostBasis,
        decimal markPrice,
        decimal realizedPnl,
        OptionDetails details,
        OptionGreeks? greeks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(details);

        Symbol = symbol;
        Contracts = contracts;
        AverageCostBasis = averageCostBasis;
        MarkPrice = markPrice;
        RealizedPnl = realizedPnl;
        Details = details;
        Greeks = greeks;
    }

    /// <inheritdoc/>
    public string Symbol { get; }

    /// <inheritdoc/>
    public long Contracts { get; }

    /// <inheritdoc/>
    public decimal AverageCostBasis { get; }

    /// <inheritdoc/>
    public decimal MarkPrice { get; private set; }

    /// <inheritdoc/>
    public decimal UnrealizedPnl =>
        (MarkPrice - AverageCostBasis) * Contracts * Details.ContractMultiplier;

    /// <inheritdoc/>
    public decimal RealizedPnl { get; private set; }

    /// <inheritdoc/>
    public DerivativeKind Kind => DerivativeKind.Option;

    /// <summary>Contract specification (strike, expiry, right, style, multiplier).</summary>
    public OptionDetails Details { get; }

    /// <summary>
    /// Current option Greek sensitivities, or <c>null</c> when Greeks have not been provided.
    /// </summary>
    public OptionGreeks? Greeks { get; private set; }

    /// <summary>
    /// Returns <c>true</c> when the option is in the money at the given underlying price.
    /// </summary>
    public bool IsInTheMoney(decimal underlyingPrice) => Details.IsInTheMoney(underlyingPrice);

    /// <summary>
    /// Updates the mark price and optionally the Greek sensitivities.
    /// </summary>
    public OptionPosition WithMark(decimal newMarkPrice, OptionGreeks? newGreeks = null) =>
        new(Symbol, Contracts, AverageCostBasis, newMarkPrice, RealizedPnl, Details, newGreeks ?? Greeks);

    /// <summary>
    /// Returns a new position with additional realized P&amp;L after a partial or full close.
    /// </summary>
    public OptionPosition WithAdditionalRealizedPnl(decimal additionalRealizedPnl) =>
        new(Symbol, Contracts, AverageCostBasis, MarkPrice, RealizedPnl + additionalRealizedPnl, Details, Greeks);

    /// <summary>
    /// Computes the intrinsic value of the option given the current underlying price.
    /// </summary>
    public decimal IntrinsicValue(decimal underlyingPrice) =>
        Details.IntrinsicValue(underlyingPrice) * Math.Abs(Contracts) * Details.ContractMultiplier;

    /// <summary>
    /// Computes the time value (extrinsic value) of the position.
    /// </summary>
    public decimal TimeValue(decimal underlyingPrice) =>
        Math.Max(0m, Math.Abs(MarkPrice) * Math.Abs(Contracts) * Details.ContractMultiplier
            - IntrinsicValue(underlyingPrice));
}
