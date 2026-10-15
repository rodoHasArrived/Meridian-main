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
        var years = Math.Max((snapshots[^1].Date.ToDateTime(TimeOnly.MinValue) - snapshots[0].Date.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.0, 1.0 / 365.0);

        var totalReturn = initial == 0 ? 0m : (final - initial) / initial;
        var annualisedReturn = (decimal)(Math.Pow(1.0 + (double)totalReturn, 1.0 / years) - 1.0);
        var sharpe = ComputeSharpe(datedDailyReturns, request.RiskFreeRate, request.RiskFreeRateSeries);
        var sortino = ComputeSortino(datedDailyReturns, request.RiskFreeRate, request.RiskFreeRateSeries);
        var (maxDrawdown, maxDrawdownPct, recoveryDays) = ComputeMaxDrawdown(snapshots, initial);
        var calmar = maxDrawdown == 0 ? 0.0 : (double)annualisedReturn / (double)maxDrawdownPct;

        var (winRate, profitFactor, totalTrades, wins, losses) = ComputeTradeStats(fills, snapshots[^1], allCashFlows, request);
        var attribution = ComputeAttribution(fills, snapshots[^1], allCashFlows);
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
        return std < 1e-10 ? 0.0 : mean / std * Math.Sqrt(252.0);
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
        return downsideDev < 1e-10 ? 0.0 : mean / downsideDev * Math.Sqrt(252.0);
    }

    private static double ResolveDailyRiskFreeRate(
        DateOnly date,
        double annualRfrFallback,
        IReadOnlyDictionary<DateOnly, double>? annualRfrSeries)
    {
        var annualRate = annualRfrSeries is not null && annualRfrSeries.TryGetValue(date, out var value)
            ? value
            : annualRfrFallback;
        return annualRate / 252.0;
    }

    private static (decimal maxDrawdown, decimal maxDrawdownPct, int recoveryDays) ComputeMaxDrawdown(
        IReadOnlyList<PortfolioSnapshot> snapshots,
        decimal initialEquity)
    {
        var peak = initialEquity;
        var maxDd = 0m;
        var maxDdPct = 0m;
        var troughIdx = -1;
        var peakAtTrough = initialEquity;  // peak preceding the worst percentage decline
        var recoveryDays = 0;

        for (var i = 0; i < snapshots.Count; i++)
        {
            var equity = snapshots[i].TotalEquity;
            if (equity > peak)
            {
                peak = equity;
            }
            else
            {
                var dd = peak - equity;
                var ddPct = peak > 0m ? dd / peak : 0m;
                maxDd = Math.Max(maxDd, dd);
                if (ddPct > maxDdPct)
                {
                    maxDdPct = ddPct;
                    troughIdx = i;
                    peakAtTrough = peak;  // record the peak that preceded this worst trough
                }
            }
        }

        // Count calendar days from trough back to the preceding peak level.
        // Compare directly against the recorded peak — no algebraic reconstruction needed.
        if (troughIdx >= 0)
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
        IReadOnlyList<FillEvent> fills,
        PortfolioSnapshot finalSnapshot,
        IReadOnlyList<CashFlowEntry> cashFlows,
        BacktestRequest request)
    {
        // A closing order is one outcome, even when it spans fills or relieves several lots.
        // Account and symbol are part of the identity: an order id cannot join separate books.
        var orderByFill = fills
            .GroupBy(fill => (Account: ResolveAccount(fill.AccountId, request), fill.FillId))
            .ToDictionary(group => group.Key, group => group.First().OrderId);
        var outcomes = finalSnapshot.Accounts.Values
            .SelectMany(account => (account.ClosedLots ?? []).Select(lot => (
                Account: account.AccountId.ToUpperInvariant(),
                Lot: lot)))
            .GroupBy(item => (
                Account: item.Account,
                Symbol: item.Lot.Symbol.ToUpperInvariant(),
                Order: orderByFill.GetValueOrDefault((item.Account, item.Lot.CloseFillId), item.Lot.CloseFillId)))
            .Select(group => group.Sum(item =>
                item.Lot.RealizedPnl - item.Lot.EntryCommission - item.Lot.ExitCommission))
            .ToList();

        // A fractional entitlement is a booked disposal even when the action leaves no whole
        // successor and no ClosedLot. Its retained evidence is separate from order closures.
        outcomes.AddRange(cashFlows.OfType<AssetEventCashFlow>()
            .Where(flow => flow.FractionalUnits != 0m)
            .Select(flow => flow.RealizedPnl - flow.EntryCommissionDisposed));

        var wins = outcomes.Count(pnl => pnl > 0m);
        var losses = outcomes.Count(pnl => pnl < 0m);
        var grossWins = outcomes.Where(pnl => pnl > 0m).Sum();
        var grossLosses = -outcomes.Where(pnl => pnl < 0m).Sum();
        // Breakeven disposals still count as completed outcomes.
        var total = outcomes.Count;
        var winRate = total == 0 ? 0.0 : (double)wins / total;
        var profitFactor = grossLosses == 0m
            ? (grossWins > 0m ? double.PositiveInfinity : 0.0)
            : (double)(grossWins / grossLosses);
        return (winRate, profitFactor, total, wins, losses);
    }

    private static string ResolveAccount(string? accountId, BacktestRequest request) =>
        (string.IsNullOrWhiteSpace(accountId) ? request.DefaultBrokerageAccountId : accountId.Trim())
        .ToUpperInvariant();

    private static IReadOnlyDictionary<string, SymbolAttribution> ComputeAttribution(
        IReadOnlyList<FillEvent> fills,
        PortfolioSnapshot finalSnapshot,
        IReadOnlyList<CashFlowEntry> cashFlows)
    {
        // Closed lots retain the actual relieved basis, direction, account and selection decision,
        // including successor lots and SpecificId. Replaying fills loses corporate-action facts.
        var closures = finalSnapshot.Accounts.Values.SelectMany(account => account.ClosedLots ?? []).ToList();
        var positions = finalSnapshot.Accounts.Values.SelectMany(account => account.Positions.Values).ToList();
        var assetEvents = cashFlows.OfType<AssetEventCashFlow>().ToList();
        var fillsBySymbol = fills.ToLookup(fill => fill.Symbol, StringComparer.OrdinalIgnoreCase);
        var closuresBySymbol = closures.ToLookup(lot => lot.Symbol, StringComparer.OrdinalIgnoreCase);
        var positionsBySymbol = positions.ToLookup(position => position.Symbol, StringComparer.OrdinalIgnoreCase);
        var assetEventsBySymbol = assetEvents.ToLookup(flow => flow.Symbol, StringComparer.OrdinalIgnoreCase);
        var symbols = fills.Select(fill => fill.Symbol)
            .Concat(closures.Select(lot => lot.Symbol))
            .Concat(positions.Select(position => position.Symbol))
            .Concat(finalSnapshot.Positions.Keys)
            .Concat(assetEvents.Select(flow => flow.Symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            var symbolFills = fillsBySymbol[symbol];
            var realised = closuresBySymbol[symbol].Sum(lot => lot.RealizedPnl)
                // Amount includes the principal settled as cash in lieu; only its retained
                // disposal gain is P&L. Per-share consideration/income/fees are posted
                // separately by the portfolio and must also remain in symbol attribution.
                + assetEventsBySymbol[symbol].Sum(flow =>
                    flow.RealizedPnl + flow.UnitsImpacted * flow.CashPerShare);
            // Use account positions so offsetting long/short books survive aggregate netting.
            var unrealised = finalSnapshot.Accounts.Count > 0
                ? positionsBySymbol[symbol].Sum(position => position.UnrealizedPnl)
                : finalSnapshot.Positions.GetValueOrDefault(symbol)?.UnrealizedPnl ?? 0m;
            result[symbol] = new SymbolAttribution(symbol, realised, unrealised,
                symbolFills.Count(), symbolFills.Sum(fill => fill.Commission), 0m);
        }
        return result;
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
