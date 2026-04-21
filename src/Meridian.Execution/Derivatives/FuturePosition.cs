using Meridian.Execution.Sdk.Derivatives;

namespace Meridian.Execution.Derivatives;

/// <summary>
/// A futures contract position that tracks daily mark-to-market (MTM) cash settlements.
/// </summary>
/// <remarks>
/// Unlike equity positions where unrealized P&amp;L accumulates until the position is closed,
/// futures use daily cash settlement: each trading day the difference between the settlement
/// price and the previous day's settlement price is either debited from or credited to the
/// account's cash balance. This resets the cost basis to the daily settlement price.
/// </remarks>
public sealed class FuturePosition : IDerivativePosition
{
    /// <summary>
    /// Initialises a new futures position.
    /// </summary>
    /// <param name="symbol">Contract symbol.</param>
    /// <param name="contracts">Number of contracts (positive = long, negative = short).</param>
    /// <param name="entryPrice">Price at which the position was entered.</param>
    /// <param name="lastSettlementPrice">Most recent daily settlement price.</param>
    /// <param name="realizedPnl">Cumulative daily MTM cash flows that have already settled.</param>
    /// <param name="details">Contract specification.</param>
    public FuturePosition(
        string symbol,
        long contracts,
        decimal entryPrice,
        decimal lastSettlementPrice,
        decimal realizedPnl,
        FutureDetails details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(details);

        Symbol = symbol;
        Contracts = contracts;
        AverageCostBasis = entryPrice;
        LastSettlementPrice = lastSettlementPrice;
        RealizedPnl = realizedPnl;
        Details = details;
    }

    /// <inheritdoc/>
    public string Symbol { get; }

    /// <inheritdoc/>
    public long Contracts { get; }

    /// <inheritdoc/>
    public decimal AverageCostBasis { get; }

    /// <summary>Most recent daily settlement price for this contract.</summary>
    public decimal LastSettlementPrice { get; private set; }

    /// <inheritdoc/>
    public decimal MarkPrice => LastSettlementPrice;

    /// <inheritdoc/>
    public decimal UnrealizedPnl =>
        (LastSettlementPrice - AverageCostBasis) * Contracts * Details.ContractMultiplier;

    /// <inheritdoc/>
    public decimal RealizedPnl { get; private set; }

    /// <inheritdoc/>
    public DerivativeKind Kind => DerivativeKind.Future;

    /// <summary>Contract specification (tick size, value, multiplier, etc.).</summary>
    public FutureDetails Details { get; }

    /// <summary>
    /// Performs an end-of-day mark-to-market cash settlement.
    /// Returns the cash flow for this settlement (positive = credit to account).
    /// The <see cref="LastSettlementPrice"/> is updated and the cumulative
    /// <see cref="RealizedPnl"/> is increased by the settlement amount.
    /// </summary>
    /// <param name="newSettlementPrice">Today's official settlement price.</param>
    /// <returns>
    ///     Cash amount to credit (or debit if negative) to the portfolio's cash balance.
    /// </returns>
    public decimal SettleDaily(decimal newSettlementPrice)
    {
        var dailyCashFlow =
            (newSettlementPrice - LastSettlementPrice) * Contracts * Details.ContractMultiplier;

        RealizedPnl += dailyCashFlow;
        LastSettlementPrice = newSettlementPrice;

        return dailyCashFlow;
    }

    /// <summary>
    /// Returns a new <see cref="FuturePosition"/> with the updated settlement price applied.
    /// Does NOT mutate this instance.
    /// </summary>
    public FuturePosition WithSettlement(decimal newSettlementPrice)
    {
        var dailyCashFlow =
            (newSettlementPrice - LastSettlementPrice) * Contracts * Details.ContractMultiplier;

        return new FuturePosition(
            Symbol,
            Contracts,
            AverageCostBasis,
            newSettlementPrice,
            RealizedPnl + dailyCashFlow,
            Details);
    }
}
