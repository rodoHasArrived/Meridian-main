using Meridian.Ledger;

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
        var initial = ResolveInitialCapital(request);
        if (snapshots.Count == 0)
            return EmptyMetrics(initial);

        var final = snapshots[^1].TotalEquity;

        // Commissions and margin interest are stored as negative cash flows; negate to get positive totals.
        var totalCommissions = -allCashFlows.OfType<CommissionCashFlow>().Sum(c => c.Amount);
        var totalMarginInterest = -allCashFlows.OfType<MarginInterestCashFlow>().Sum(c => c.Amount);
        var totalShortRebates = allCashFlows.OfType<ShortRebateCashFlow>().Sum(c => c.Amount);

        // TotalEquity already reflects every friction: SimulatedPortfolio deducts commissions
        // from cash on each fill and posts margin interest / short rebates through cash during
        // daily accrual. Net P&L is therefore the plain equity delta; subtracting the frictions
        // again (the previous formula) double-counted them. Gross P&L adds the frictions back.
        var netPnl = final - initial;
        var grossPnl = netPnl + totalCommissions + totalMarginInterest - totalShortRebates;

        var datedDailyReturns = snapshots.Select(s => (Date: s.Date, Return: (double)s.DailyReturn)).ToList();
        var openingTimestamp = new DateTimeOffset(
            request.From.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);
        var years = Math.Max(
            (snapshots[^1].Timestamp - openingTimestamp).TotalDays / 365.0,
            1.0 / 365.0);

        var totalReturn = initial == 0 ? 0m : (final - initial) / initial;
        var annualisedReturn = (decimal)(Math.Pow(1.0 + (double)totalReturn, 1.0 / years) - 1.0);
        var sharpe = ComputeSharpe(datedDailyReturns, request.RiskFreeRate, request.RiskFreeRateSeries);
        var sortino = ComputeSortino(datedDailyReturns, request.RiskFreeRate, request.RiskFreeRateSeries);
        var (maxDrawdown, maxDrawdownPct, recoveryDays) = ComputeMaxDrawdown(snapshots);
        var calmar = maxDrawdown == 0 ? 0.0 : (double)annualisedReturn / (double)maxDrawdownPct;

        var (winRate, profitFactor, totalTrades, wins, losses) = ComputeTradeStats(fills);
        var attribution = ComputeAttribution(fills, snapshots, request);
        var xirr = ComputeXirr(initial, openingTimestamp, snapshots);

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

    private static double ComputeSharpe(
        IReadOnlyList<(DateOnly Date, double Return)> dailyReturns,
        double annualRfr,
        IReadOnlyDictionary<DateOnly, double>? annualRfrSeries)
    {
        if (dailyReturns.Count < 2)
            return 0.0;
        var excess = dailyReturns
            .Select(period => period.Return - ResolveDailyRiskFreeRate(period.Date, annualRfr, annualRfrSeries))
            .ToList();
        var mean = excess.Average();
        var std = StdDev(excess);
        return std < 1e-10 ? 0.0 : mean / std * Math.Sqrt(365.0);
    }

    private static double ComputeSortino(
        IReadOnlyList<(DateOnly Date, double Return)> dailyReturns,
        double annualRfr,
        IReadOnlyDictionary<DateOnly, double>? annualRfrSeries)
    {
        if (dailyReturns.Count < 2)
            return 0.0;
        var excess = dailyReturns
            .Select(period => period.Return - ResolveDailyRiskFreeRate(period.Date, annualRfr, annualRfrSeries))
            .ToList();
        var mean = excess.Average();
        var downside = excess.Where(r => r < 0).ToList();
        if (downside.Count == 0)
            return double.PositiveInfinity;
        var downsideDev = Math.Sqrt(downside.Select(r => r * r).Average());
        return downsideDev < 1e-10 ? 0.0 : mean / downsideDev * Math.Sqrt(365.0);
    }

    private static double ResolveDailyRiskFreeRate(
        DateOnly date,
        double annualRfrFallback,
        IReadOnlyDictionary<DateOnly, double>? annualRfrSeries)
    {
        var annualRate = annualRfrSeries is not null && annualRfrSeries.TryGetValue(date, out var value)
            ? value
            : annualRfrFallback;
        return annualRate / 365.0;
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
                    var roundTripPnl = 0m;

                    var consumption = LotConsumption.Consume(
                        EnumerateNodes(lots), closeQty, static node => node.Value.qty);

                    foreach (var slice in consumption.Slices)
                    {
                        var (lotQty, lotPrice, lotCommission) = slice.Lot.Value;
                        var consumed = (long)slice.Quantity;

                        // Allocate entry and exit commission proportionally to consumed quantity
                        var entryCommForConsumed = lotQty > 0 ? lotCommission * consumed / lotQty : 0m;
                        var exitCommForConsumed = closeQty > 0 ? order.Commission * consumed / closeQty : 0m;

                        roundTripPnl += consumed * (order.Price - lotPrice)
                            - entryCommForConsumed - exitCommForConsumed;

                        if (slice.ClosesLot)
                        {
                            lots.Remove(slice.Lot);
                        }
                        else
                        {
                            var leftoverCommission = lotQty > 0 ? lotCommission * (lotQty - consumed) / lotQty : 0m;
                            slice.Lot.Value = (lotQty - consumed, lotPrice, leftoverCommission);
                        }
                    }

                    // Only record a completed round-trip if at least some lots were consumed
                    if (consumption.Slices.Count > 0)
                    {
                        if (roundTripPnl > 0)
                        { grossWins += roundTripPnl; wins++; }
                        else if (roundTripPnl < 0)
                        { grossLosses += -roundTripPnl; losses++; }
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
        IReadOnlyList<PortfolioSnapshot> snapshots,
        BacktestRequest request)
    {
        // Attribution realized P&L honors each account's lot-selection method so it ties out
        // with the realized P&L the portfolio books via SimulatedPortfolio.RealiseLots.
        // Built tolerantly: metrics must not throw on a malformed account list (blank or
        // duplicated ids) that the engine itself would reject elsewhere.
        var lotSelectionByAccount = new Dictionary<string, LotSelectionMethod>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in request.ResolveAccounts())
        {
            if (account is null || string.IsNullOrWhiteSpace(account.AccountId))
                continue;

            lotSelectionByAccount.TryAdd(account.AccountId, account.Rules?.LotSelection ?? LotSelectionMethod.Fifo);
        }

        var result = new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase);
        var groupedFills = fills.GroupBy(f => f.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedFills)
        {
            var symbol = group.Key;
            var tradeCount = group.Count();
            var totalCommission = group.Sum(f => f.Commission);

            // Lots never cross accounts, so realized P&L is matched per (symbol, account).
            var realised = group
                .GroupBy(
                    f => string.IsNullOrWhiteSpace(f.AccountId) ? request.DefaultBrokerageAccountId : f.AccountId,
                    StringComparer.OrdinalIgnoreCase)
                .Sum(accountFills => ComputeRealisedPnl(
                    accountFills.ToList(),
                    lotSelectionByAccount.GetValueOrDefault(accountFills.Key, LotSelectionMethod.Fifo)));

            // Unrealised P&L from last snapshot
            var lastSnapshot = snapshots.Count > 0 ? snapshots[^1] : null;
            var unrealised = lastSnapshot?.Positions.TryGetValue(symbol, out var pos) == true ? pos.UnrealizedPnl : 0m;

            result[symbol] = new SymbolAttribution(symbol, realised, unrealised, tradeCount, totalCommission, 0m);
        }
        return result;
    }

    /// <summary>
    /// Computes realized P&amp;L for one (symbol, account) fill stream, honoring the account's
    /// lot-selection method and mirroring the portfolio's long/short crossing semantics.
    /// <para>
    /// NOTE: This is an independent computation over fill events for metric attribution purposes.
    /// It must produce results consistent with <c>SimulatedPortfolio.RealiseLots</c> /
    /// <c>RealiseShortLots</c>, which drive the booked portfolio accounting. If one is changed,
    /// the other must be updated in parallel.
    /// </para>
    /// </summary>
    private static decimal ComputeRealisedPnl(IReadOnlyList<FillEvent> fills, LotSelectionMethod method)
    {
        var longLots = new LinkedList<(long Qty, decimal Price)>();
        var shortLots = new LinkedList<(long Qty, decimal Price)>();
        var realised = 0m;

        foreach (var fill in fills.OrderBy(f => f.FilledAt))
        {
            var qty = fill.FilledQuantity;
            if (qty == 0)
                continue;

            if (qty > 0)
            {
                // Cover any short exposure first; the residual opens a long lot.
                var residual = ConsumeFromBook(shortLots, qty, fill.FillPrice, method, isShortBook: true, ref realised);
                if (residual > 0)
                    longLots.AddLast((residual, fill.FillPrice));
            }
            else
            {
                // Close long exposure first; the residual opens a short lot.
                var residual = ConsumeFromBook(longLots, -qty, fill.FillPrice, method, isShortBook: false, ref realised);
                if (residual > 0)
                    shortLots.AddLast((residual, fill.FillPrice));
            }
        }

        return realised;
    }

    /// <summary>
    /// Consumes up to <paramref name="quantity"/> from the book in the account's relief order,
    /// accruing realized P&amp;L, and returns the unfilled residual (which crosses the position
    /// to the other side).
    /// </summary>
    private static long ConsumeFromBook(
        LinkedList<(long Qty, decimal Price)> lots,
        long quantity,
        decimal fillPrice,
        LotSelectionMethod method,
        bool isShortBook,
        ref decimal realised)
    {
        var consumption = LotConsumption.Consume(
            OrderLots(lots, method), quantity, static node => node.Value.Qty);

        foreach (var slice in consumption.Slices)
        {
            var (lotQty, lotPrice) = slice.Lot.Value;
            realised += isShortBook
                ? slice.Quantity * (lotPrice - fillPrice)
                : slice.Quantity * (fillPrice - lotPrice);

            if (slice.ClosesLot)
                lots.Remove(slice.Lot);
            else
                slice.Lot.Value = (lotQty - (long)slice.Quantity, lotPrice);
        }

        return (long)consumption.Shortfall;
    }

    private static IEnumerable<LinkedListNode<(long Qty, decimal Price)>> OrderLots(
        LinkedList<(long Qty, decimal Price)> lots,
        LotSelectionMethod method) => method switch
        {
            LotSelectionMethod.Lifo => EnumerateNodesReverse(lots),
            LotSelectionMethod.Hifo => EnumerateNodes(lots).OrderByDescending(static n => n.Value.Price),
            // SpecificId falls back to FIFO: attribution rebuilds synthetic lots from the fill
            // stream, so the portfolio's TargetLotId values cannot match them — the same fallback
            // SimulatedPortfolio applies when the nominated lot is absent.
            _ => EnumerateNodes(lots),
        };

    private static IEnumerable<LinkedListNode<T>> EnumerateNodes<T>(LinkedList<T> list)
    {
        for (var node = list.First; node is not null; node = node.Next)
            yield return node;
    }

    private static IEnumerable<LinkedListNode<T>> EnumerateNodesReverse<T>(LinkedList<T> list)
    {
        for (var node = list.Last; node is not null; node = node.Previous)
            yield return node;
    }

    private static double ComputeXirr(
        decimal initialCapital,
        DateTimeOffset openingTimestamp,
        IReadOnlyList<PortfolioSnapshot> snapshots)
    {
        var flows = new List<(DateTimeOffset date, decimal amount)>();

        // Money-weighted return is from the investor's perspective. Trades, commissions,
        // financing accruals, dividends, and corporate-action settlements are internal portfolio
        // movements already reflected in terminal equity; including them here double-counts them.
        flows.Add((openingTimestamp, -initialCapital));

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

    private static decimal ResolveInitialCapital(BacktestRequest request)
        => request.Accounts is { Count: > 0 }
            ? request.Accounts.Sum(static account => account?.InitialCash ?? 0m)
            : request.InitialCash;

    private static BacktestMetrics EmptyMetrics(decimal initial) =>
        new(initial, initial, 0m, 0m, 0m, 0m, 0.0, 0.0, 0.0, 0m, 0m, 0, 0.0, 0.0, 0, 0, 0, 0m, 0m, 0m, 0.0,
            new Dictionary<string, SymbolAttribution>());
}
