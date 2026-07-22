using Meridian.Execution.Models;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>
/// Production <see cref="IPositionTracker"/> that projects a live
/// <see cref="IPortfolioState"/> (for example <see cref="PaperTradingPortfolio"/>, or any
/// broker-backed portfolio implementation) into the real-time position and P&amp;L view that
/// risk rules consume.
/// </summary>
/// <remarks>
/// Before this adapter existed, <c>IPositionTracker</c> had no production implementation, so the
/// two most safety-critical risk rules — <c>PositionLimitRule</c> (per-symbol position caps) and
/// <c>DrawdownCircuitBreaker</c> (portfolio drawdown kill-switch) — could only be exercised against
/// test doubles and could not function in a real deployment. This type bridges those rules to the
/// portfolio state that the execution layer already maintains, without duplicating position
/// accounting: it is a thin, read-only projection over the single source of truth.
/// </remarks>
public sealed class PortfolioStatePositionTracker : IPositionTracker
{
    private readonly IPortfolioState _portfolio;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a position tracker backed by the supplied portfolio state.
    /// </summary>
    /// <param name="portfolio">The authoritative portfolio state to project. Required.</param>
    /// <param name="timeProvider">
    /// Clock used to stamp <see cref="PositionState.LastUpdated"/>. Defaults to
    /// <see cref="TimeProvider.System"/> when not supplied.
    /// </param>
    public PortfolioStatePositionTracker(IPortfolioState portfolio, TimeProvider? timeProvider = null)
    {
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public PositionState GetPosition(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        return _portfolio.Positions.TryGetValue(symbol, out var position)
            ? ToPositionState(position, _timeProvider.GetUtcNow())
            : EmptyPosition(symbol, _timeProvider.GetUtcNow());
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PositionState> GetAllPositions()
    {
        var asOf = _timeProvider.GetUtcNow();
        var snapshot = new Dictionary<string, PositionState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, position) in _portfolio.Positions)
        {
            snapshot[symbol] = ToPositionState(position, asOf);
        }

        return snapshot;
    }

    /// <inheritdoc />
    public decimal GetPortfolioValue() => _portfolio.PortfolioValue;

    /// <inheritdoc />
    public decimal GetCash() => _portfolio.Cash;

    /// <inheritdoc />
    public decimal GetUnrealizedPnl() => _portfolio.UnrealisedPnl;

    /// <inheritdoc />
    public decimal GetRealizedPnl() => _portfolio.RealisedPnl;

    private static PositionState ToPositionState(IPosition position, DateTimeOffset asOf)
    {
        var quantity = (decimal)position.Quantity;

        // IPosition exposes unrealised P&L but not the current mark directly. Recover the mark from
        // the identity UnrealizedPnl = (MarketPrice - AverageCostBasis) * Quantity so that the
        // projected PositionState (whose MarketValue and UnrealizedPnl are derived from MarketPrice)
        // stays internally consistent with the source portfolio. A flat position has no meaningful
        // mark, so fall back to the average cost basis.
        var marketPrice = quantity == 0m
            ? position.AverageCostBasis
            : position.AverageCostBasis + (position.UnrealizedPnl / quantity);

        return new PositionState
        {
            Symbol = position.Symbol,
            Quantity = quantity,
            AverageCostBasis = position.AverageCostBasis,
            MarketPrice = marketPrice,
            RealizedPnl = position.RealizedPnl,
            LastUpdated = asOf
        };
    }

    private static PositionState EmptyPosition(string symbol, DateTimeOffset asOf) => new()
    {
        Symbol = symbol,
        Quantity = 0m,
        AverageCostBasis = 0m,
        MarketPrice = 0m,
        RealizedPnl = 0m,
        LastUpdated = asOf
    };
}
