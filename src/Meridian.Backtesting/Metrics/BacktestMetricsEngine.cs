namespace Meridian.Backtesting.Metrics;

/// <summary>
/// Computes all performance metrics from the portfolio snapshot series and cash-flow ledger
/// produced during a completed backtest run.
/// </summary>
internal static class BacktestMetricsEngine
{
    private const double RiskFreeRate = 0.04;  // 4% annualised, configurable in future

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

        var totalCommissions = allCashFlows.OfType<CommissionCashFlow>().Sum(c => Math.Abs(c.Amount));
        var totalMarginInterest = allCashFlows.OfType<MarginInterestCashFlow>().Sum(c => Math.Abs(c.Amount));
        var totalShortRebates = allCashFlows.OfType<ShortRebateCashFlow>().Sum(c => c.Amount);
        var netPnl = grossPnl - totalCommissions - totalMarginInterest + totalShortRebates;

        var dailyReturns = snapshots.Select(s => (double)s.DailyReturn).ToList();
        var years = Math.Max((snapshots[^1].Date.ToDateTime(TimeOnly.MinValue) - snapshots[0].Date.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.0, 1.0 / 365.0);

        var totalReturn = initial == 0 ? 0m : (final - initial) / initial;
        var annualisedReturn = (decimal)(Math.Pow(1.0 + (double)totalReturn, 1.0 / years) - 1.0);
        var sharpe = ComputeSharpe(dailyReturns, RiskFreeRate);
        var sortino = ComputeSortino(dailyReturns, RiskFreeRate);
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
        var recoveryDays = 0;

        for (var i = 1; i < snapshots.Count; i++)
        {
            var equity = snapshots[i].TotalEquity;
            if (equity > peak)
            {
                // Recovered — calculate recovery time for the worst DD
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
                }
            }
        }

        // Count recovery days from trough
        if (troughIdx > 0)
        {
            var troughEquity = snapshots[troughIdx].TotalEquity;
            for (var i = troughIdx + 1; i < snapshots.Count; i++)
            {
                if (snapshots[i].TotalEquity >= troughEquity / (1m - maxDdPct))
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
        // Group fills into round-trips (buy then sell) by symbol — simplified per-fill P&L
        var trades = fills.Where(f => f.FilledQuantity != 0).ToList();
        if (trades.Count == 0)
            return (0.0, 0.0, 0, 0, 0);

        // For simplicity: each fill represents a "trade"; win = positive (qty * sign contributes to profit)
        // A more precise approach groups buy/sell pairs. We use a sign-based heuristic here.
        var grossWins = 0m;
        var grossLosses = 0m;
        var wins = 0;
        var losses = 0;

        // Use fills paired with commission: net = -(qty * price) - commission relative to prior avg
        // Since we don't have prior avg here, treat positive net proceeds as wins
        foreach (var fill in trades)
        {
            var net = fill.FilledQuantity < 0
                ? Math.Abs(fill.FilledQuantity) * fill.FillPrice - fill.Commission   // sale proceeds
                : -(fill.FilledQuantity * fill.FillPrice + fill.Commission);          // buy cost (negative)

            if (net > 0)
            { grossWins += net; wins++; }
            else if (net < 0)
            { grossLosses += Math.Abs(net); losses++; }
        }

        var total = wins + losses;
        var winRate = total == 0 ? 0.0 : (double)wins / total;
        var profitFactor = grossLosses == 0 ? (grossWins > 0 ? double.PositiveInfinity : 0.0) : (double)(grossWins / grossLosses);
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

    private static decimal ComputeRealisedPnl(IReadOnlyList<FillEvent> fills)
    {
        var lotQueue = new Queue<(long qty, decimal price)>();
        var realised = 0m;

        foreach (var fill in fills.OrderBy(f => f.FilledAt))
        {
            if (fill.FilledQuantity > 0)
            {
                lotQueue.Enqueue((fill.FilledQuantity, fill.FillPrice));
            }
            else
            {
                var closeQty = Math.Abs(fill.FilledQuantity);
                while (closeQty > 0 && lotQueue.Count > 0)
                {
                    var (lotQty, lotPrice) = lotQueue.Peek();
                    if (lotQty <= closeQty)
                    {
                        realised += lotQty * (fill.FillPrice - lotPrice);
                        closeQty -= lotQty;
                        lotQueue.Dequeue();
                    }
                    else
                    {
                        realised += closeQty * (fill.FillPrice - lotPrice);
                        lotQueue = new Queue<(long, decimal)>(lotQueue.Skip(1).Prepend((lotQty - closeQty, lotPrice)));
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
