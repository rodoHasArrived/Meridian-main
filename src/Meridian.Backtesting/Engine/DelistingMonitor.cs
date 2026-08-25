using Meridian.Backtesting.Portfolio;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Tracks the last date each symbol produced a market event and, under
/// <see cref="DelistingPolicy.LiquidateAtLastPrice"/>, force-liquidates open positions in symbols
/// whose data has been silent for longer than the configured grace period. This keeps positions in
/// delisted (or otherwise data-dead) names from being marked at a stale price to the end of the
/// run, and records every forced liquidation for the bias-disclosure report.
/// </summary>
internal sealed class DelistingMonitor(DelistingPolicy policy, decimal haircutPercent, int graceDays)
{
    private readonly decimal _haircutPercent = Math.Clamp(haircutPercent, 0m, 1m);
    private readonly int _graceDays = Math.Max(1, graceDays);
    private readonly Dictionary<string, DateOnly> _lastEventDates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DelistingLiquidation> _liquidations = [];
    private readonly List<string> _failedLiquidationSymbols = [];

    /// <summary>Forced liquidations applied so far, in simulation order.</summary>
    public IReadOnlyList<DelistingLiquidation> Liquidations => _liquidations;

    /// <summary>Symbols whose forced liquidation was rejected by account rules and remain open.</summary>
    public IReadOnlyList<string> FailedLiquidationSymbols => _failedLiquidationSymbols;

    /// <summary>Records that <paramref name="symbol"/> produced a market event on <paramref name="date"/>.</summary>
    public void RecordEvent(string symbol, DateOnly date) => _lastEventDates[symbol] = date;

    /// <summary>
    /// Runs the day-end delisting sweep: any open position whose symbol has produced no events for
    /// more than the grace period is closed at the last observed price adjusted by the haircut
    /// (longs receive less, shorts cover higher). Working orders on liquidated symbols are cancelled.
    /// </summary>
    public void ProcessDayEnd(
        DateOnly date,
        SimulatedPortfolio portfolio,
        BacktestContext context,
        List<FillEvent> allFills,
        ILogger logger)
    {
        if (policy != DelistingPolicy.LiquidateAtLastPrice)
            return;

        HashSet<string>? liquidatedSymbols = null;
        var dayEndTimestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var (accountId, account) in portfolio.GetAccountSnapshots())
        {
            foreach (var (symbol, position) in account.Positions)
            {
                if (position.Quantity == 0)
                    continue;
                if (!_lastEventDates.TryGetValue(symbol, out var lastDataDate))
                    continue;
                if (date.DayNumber - lastDataDate.DayNumber <= _graceDays)
                    continue;
                if (!portfolio.LastPrices.TryGetValue(symbol, out var lastPrice) || lastPrice <= 0m)
                    continue;

                var price = Math.Round(
                    position.Quantity > 0
                        ? lastPrice * (1m - _haircutPercent)
                        : lastPrice * (1m + _haircutPercent),
                    4);

                var fill = new FillEvent(
                    Guid.NewGuid(),
                    Guid.Empty,   // administrative fill — not tied to a strategy order
                    symbol,
                    -position.Quantity,
                    price,
                    Commission: 0m,
                    dayEndTimestamp,
                    accountId);

                try
                {
                    fill = portfolio.ProcessFill(fill);
                }
                catch (InvalidOperationException ex)
                {
                    _failedLiquidationSymbols.Add(symbol);
                    logger.LogWarning(ex,
                        "Delisting liquidation for {Symbol} in account {AccountId} was rejected by account rules; the position remains open at a stale mark.",
                        symbol, accountId);
                    continue;
                }

                allFills.Add(fill);
                _liquidations.Add(new DelistingLiquidation(symbol, date, lastDataDate, position.Quantity, price, _haircutPercent));
                (liquidatedSymbols ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(symbol);

                logger.LogWarning(
                    "Symbol {Symbol} produced no data after {LastDataDate}; force-liquidated {Quantity} shares at {Price} on {Date} (haircut {Haircut:P0}).",
                    symbol, lastDataDate, position.Quantity, price, date, _haircutPercent);
            }
        }

        if (liquidatedSymbols is { Count: > 0 })
        {
            context.RemoveWorkingOrders(order => liquidatedSymbols.Contains(order.Symbol));
        }
    }
}
