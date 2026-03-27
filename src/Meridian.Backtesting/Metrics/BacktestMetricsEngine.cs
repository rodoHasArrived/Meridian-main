namespace Meridian.Backtesting.Metrics;

/// <summary>
/// Computes all performance metrics from the portfolio snapshot series and cash-flow ledger
/// produced during a completed backtest run.
/// </summary>
internal static class BacktestMetricsEngine
{
    public static BacktestMetrics Compute(
        IReadOnlyList<PortfolioSnapshot> snapshots,
        IReadOnlyList<CashFlowEntry> allCashFlows,
        IReadOnlyList<FillEvent> fills,
        BacktestRequest request)
    {
        if (snapshots.Count == 0)
            return EmptyMetrics(request.InitialCash);

        var initial = request.InitialCash;
        var final = snapshots[^1].TotalEquity;
        var grossPnl = final - initial;

        // Commissions and margin interest are stored as negative cash flows; negate to get positive totals.
        var totalCommissions = -allCashFlows.OfType<CommissionCashFlow>().Sum(c => c.Amount);
        var totalMarginInterest = -allCashFlows.OfType<MarginInterestCashFlow>().Sum(c => c.Amount);
        var totalShortRebates = allCashFlows.OfType<ShortRebateCashFlow>().Sum(c => c.Amount);
        var netPnl = grossPnl - totalCommissions - totalMarginInterest + totalShortRebates;

        var dailyReturns = snapshots.Select(s => (double)s.DailyReturn).ToList();
        var years = Math.Max((snapshots[^1].Date.ToDateTime(TimeOnly.MinValue) - snapshots[0].Date.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.0, 1.0 / 365.0);

        var totalReturn = initial == 0 ? 0m : (final - initial) / initial;
        var annualisedReturn = (decimal)(Math.Pow(1.0 + (double)totalReturn, 1.0 / years) - 1.0);
        var sharpe = ComputeSharpe(dailyReturns, request.RiskFreeRate);
        var sortino = ComputeSortino(dailyReturns, request.RiskFreeRate);
        var (maxDrawdown, maxDrawdownPct, recoveryDays) = ComputeMaxDrawdown(snapshots);
        var calmar = maxDrawdown == 0 ? 0.0 : (double)annualisedReturn / (double)maxDrawdownPct;

        var (winRate, profitFactor, totalTrades, wins, losses) = ComputeTradeStats(fills);
        var attribution = ComputeAttribution(fills, snapshots);
        var xirr = ComputeXirr(allCashFlows, initial, snapshots);

        return new BacktestMetrics(
            initial,
            final,
            grossPnl,
            netPnl,
            totalReturn,
            annualisedReturn,
            sharpe,
            sortino,
            calmar,
            maxDrawdown,
            maxDrawdownPct,
            recoveryDays,
            profitFactor,
            winRate,
            totalTrades,
            wins,
            losses,
            totalCommissions,
            totalMarginInterest,
            totalShortRebates,
            xirr,
            attribution);
    }

    // ── Statistical helpers ──────────────────────────────────────────────────

    private static double ComputeSharpe(IReadOnlyList<double> dailyReturns, double annualRfr)
    {
        if (dailyReturns.Count < 2)
            return 0.0;
        var dailyRfr = annualRfr / 252.0;
        var excess = dailyReturns.Select(r => r - dailyRfr).ToList();
        var mean = excess.Average();
        var std = StdDev(excess);
        return std < 1e-10 ? 0.0 : mean / std * Math.Sqrt(252.0);
    }

    private static double ComputeSortino(IReadOnlyList<double> dailyReturns, double annualRfr)
    {
        if (dailyReturns.Count < 2)
            return 0.0;
        var dailyRfr = annualRfr / 252.0;
        var excess = dailyReturns.Select(r => r - dailyRfr).ToList();
        var mean = excess.Average();
        var downside = excess.Where(r => r < 0).ToList();
        if (downside.Count == 0)
            return double.PositiveInfinity;
        var downsideDev = Math.Sqrt(downside.Select(r => r * r).Average());
        return downsideDev < 1e-10 ? 0.0 : mean / downsideDev * Math.Sqrt(252.0);
    }

    private static (decimal maxDrawdown, decimal maxDrawdownPct, int recoveryDays) ComputeMaxDrawdown(
        IReadOnlyList<PortfolioSnapshot> snapshots)
    {
        var peak = snapshots[0].TotalEquity;
        var maxDd = 0m;
        var maxDdPct = 0m;
        var troughIdx = 0;
        var peakAtTrough = snapshots[0].TotalEquity;  // running peak at the time the worst trough was observed
        var recoveryDays = 0;

        for (var i = 1; i < snapshots.Count; i++)
        {
            var equity = snapshots[i].TotalEquity;
            if (equity > peak)
            {
                peak = equity;
            }
            else
            {
                var dd = peak - equity;
                var ddPct = peak == 0 ? 0m : dd / peak;
                if (ddPct > maxDdPct)
                {
                    maxDd = dd;
                    maxDdPct = ddPct;
                    troughIdx = i;
                    peakAtTrough = peak;  // record the peak that preceded this worst trough
                }
            }
        }

        // Count calendar days from trough back to the preceding peak level.
        // Compare directly against the recorded peak — no algebraic reconstruction needed.
        if (troughIdx > 0)
        {
            for (var i = troughIdx + 1; i < snapshots.Count; i++)
            {
                if (snapshots[i].TotalEquity >= peakAtTrough)
                {
                    recoveryDays = (snapshots[i].Date.ToDateTime(TimeOnly.MinValue) -
                                    snapshots[troughIdx].Date.ToDateTime(TimeOnly.MinValue)).Days;
                    break;
                }
            }
        }

        return (maxDd, maxDdPct, recoveryDays);
    }

    private static (double winRate, double profitFactor, int total, int wins, int losses) ComputeTradeStats(
        IReadOnlyList<FillEvent> fills)
    {
        if (fills.Count == 0)
            return (0.0, 0.0, 0, 0, 0);

        // Aggregate multi-fill orders (e.g. partial slices from MarketImpact model) into single
        // order-level summaries so that one order == one potential trade entry or exit.
        var orderSummaries = fills
            .Where(f => f.FilledQuantity != 0)
            .GroupBy(f => f.OrderId)
            .Select(g =>
            {
                var totalAbsQty = g.Sum(f => Math.Abs(f.FilledQuantity));
                var avgPrice = totalAbsQty == 0 ? 0m
                    : g.Sum(f => Math.Abs(f.FilledQuantity) * f.FillPrice) / totalAbsQty;
                return (
                    Symbol: g.First().Symbol,
                    FilledAt: g.Max(f => f.FilledAt),
                    Quantity: g.Sum(f => f.FilledQuantity),
                    Price: avgPrice,
                    Commission: g.Sum(f => f.Commission));
            })
            .OrderBy(o => o.FilledAt)
            .ToList();

        var grossWins = 0m;
        var grossLosses = 0m;
        var wins = 0;
        var losses = 0;

        // Per-symbol FIFO lot matching: count a round-trip each time a long lot is closed.
        foreach (var symbolOrders in orderSummaries.GroupBy(o => o.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            // Each lot entry: (quantity remaining, entry price, proportional entry commission)
            var lots = new LinkedList<(long qty, decimal price, decimal commission)>();

            foreach (var order in symbolOrders)
            {
                if (order.Quantity > 0)
                {
                    // Entry — add lot to queue
                    lots.AddLast((order.Quantity, order.Price, order.Commission));
                }
                else if (order.Quantity < 0)
                {
                    // Exit — consume lots FIFO and compute round-trip P&L
                    var closeQty = Math.Abs(order.Quantity);
                    var remaining = closeQty;
                    var roundTripPnl = 0m;

                    while (remaining > 0 && lots.Count > 0)
                    {
                        var node = lots.First!;
                        var (lotQty, lotPrice, lotCommission) = node.Value;
                        var consumed = Math.Min(lotQty, remaining);

                        // Allocate entry and exit commission proportionally to consumed quantity
                        var entryCommForConsumed = lotQty > 0 ? lotCommission * consumed / lotQty : 0m;
                        var exitCommForConsumed = closeQty > 0 ? order.Commission * consumed / closeQty : 0m;

                        roundTripPnl += consumed * (order.Price - lotPrice)
                            - entryCommForConsumed - exitCommForConsumed;
                        remaining -= consumed;

                        if (consumed >= lotQty)
                        {
                            lots.RemoveFirst();
                        }
                        else
                        {
                            // Partial lot: remove front and prepend reduced entry. O(1) time;
                            // avoids reallocating the entire collection that the old Queue required.
                            var leftoverCommission = lotQty > 0 ? lotCommission * (lotQty - consumed) / lotQty : 0m;
                            lots.RemoveFirst();
                            lots.AddFirst((lotQty - consumed, lotPrice, leftoverCommission));
                        }
                    }

                    // Only record a completed round-trip if at least some lots were consumed
                    if (remaining < closeQty)
                    {
                        if (roundTripPnl > 0) { grossWins += roundTripPnl; wins++; }
                        else if (roundTripPnl < 0) { grossLosses += -roundTripPnl; losses++; }
                    }
                }
            }
        }

        var total = wins + losses;
        var winRate = total == 0 ? 0.0 : (double)wins / total;
        var profitFactor = grossLosses == 0
            ? (grossWins > 0 ? double.PositiveInfinity : 0.0)
            : (double)(grossWins / grossLosses);
        return (winRate, profitFactor, total, wins, losses);
    }

    private static IReadOnlyDictionary<string, SymbolAttribution> ComputeAttribution(
        IReadOnlyList<FillEvent> fills,
        IReadOnlyList<PortfolioSnapshot> snapshots)
    {
        var result = new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase);
        var groupedFills = fills.GroupBy(f => f.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedFills)
        {
            var symbol = group.Key;
            var tradeCount = group.Count();
            var totalCommission = group.Sum(f => f.Commission);

            // Realised P&L from fills: FIFO pairs
            var realised = ComputeRealisedPnl(group.ToList());

            // Unrealised P&L from last snapshot
            var lastSnapshot = snapshots.Count > 0 ? snapshots[^1] : null;
            var unrealised = lastSnapshot?.Positions.TryGetValue(symbol, out var pos) == true ? pos.UnrealizedPnl : 0m;

            result[symbol] = new SymbolAttribution(symbol, realised, unrealised, tradeCount, totalCommission, 0m);
        }
        return result;
    }

    /// <summary>
    /// Computes realized P&amp;L for a single symbol's fills using FIFO lot matching.
    /// <para>
    /// NOTE: This is an independent computation over fill events for metric attribution purposes.
    /// It must produce results consistent with <c>SimulatedPortfolio.RealiseFifo</c>, which drives
    /// the live portfolio accounting. If one is changed, the other must be updated in parallel.
    /// </para>
    /// </summary>
    private static decimal ComputeRealisedPnl(IReadOnlyList<FillEvent> fills)
    {
        // Use LinkedList so partial-lot updates are O(1) AddFirst — no new collection per split.
        var lots = new LinkedList<(long qty, decimal price)>();
        var realised = 0m;

        foreach (var fill in fills.OrderBy(f => f.FilledAt))
        {
            if (fill.FilledQuantity > 0)
            {
                lots.AddLast((fill.FilledQuantity, fill.FillPrice));
            }
            else
            {
                var closeQty = Math.Abs(fill.FilledQuantity);
                while (closeQty > 0 && lots.Count > 0)
                {
                    var node = lots.First!;
                    var (lotQty, lotPrice) = node.Value;
                    if (lotQty <= closeQty)
                    {
                        realised += lotQty * (fill.FillPrice - lotPrice);
                        closeQty -= lotQty;
                        lots.RemoveFirst();
                    }
                    else
                    {
                        realised += closeQty * (fill.FillPrice - lotPrice);
                        // Partial lot: remove front and prepend reduced entry. O(1) time;
                        // avoids reallocating the entire collection that the old Queue required.
                        lots.RemoveFirst();
                        lots.AddFirst((lotQty - closeQty, lotPrice));
                        closeQty = 0;
                    }
                }
            }
        }
        return realised;
    }

    private static double ComputeXirr(
        IReadOnlyList<CashFlowEntry> cashFlows,
        decimal initialCapital,
        IReadOnlyList<PortfolioSnapshot> snapshots)
    {
        var flows = new List<(DateTimeOffset date, decimal amount)>();

        // Initial capital outflow
        var startDate = snapshots.Count > 0 ? snapshots[0].Timestamp : DateTimeOffset.UtcNow;
        flows.Add((startDate, -initialCapital));

        // All real cash flows
        foreach (var cf in cashFlows)
            flows.Add((cf.Timestamp, cf.Amount));

        // Terminal value inflow
        if (snapshots.Count > 0)
            flows.Add((snapshots[^1].Timestamp, snapshots[^1].TotalEquity));

        return XirrCalculator.Calculate(flows);
    }

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0.0;
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    private static BacktestMetrics EmptyMetrics(decimal initial) =>
        new(initial, initial, 0m, 0m, 0m, 0m, 0.0, 0.0, 0.0, 0m, 0m, 0, 0.0, 0.0, 0, 0, 0, 0m, 0m, 0m, 0.0,
            new Dictionary<string, SymbolAttribution>());
}
