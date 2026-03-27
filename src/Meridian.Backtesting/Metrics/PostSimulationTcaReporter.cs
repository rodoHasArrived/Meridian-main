namespace Meridian.Backtesting.Metrics;

/// <summary>
/// Generates a Transaction Cost Analysis (TCA) report from a completed backtest fill tape.
/// Computes commission attribution by fill, per-symbol cost breakdowns, and outlier detection.
/// This is a post-processing pass with no additional data requirements beyond the fill tape
/// already produced by the backtest engine.
/// </summary>
internal static class PostSimulationTcaReporter
{
    // Fills whose commission rate exceeds this multiplier × the run mean are flagged as outliers.
    private const double OutlierThresholdMultiplier = 3.0;

    // Suppress outlier flagging for fills below this minimum rate (bps) to avoid noise on
    // zero- or near-zero-commission fills.
    private const double OutlierMinimumRateBps = 1.0;

    /// <summary>
    /// Generates a <see cref="TcaReport"/> from the supplied <paramref name="request"/> parameters
    /// and the list of <paramref name="fills"/> produced during the run.
    /// Returns a zero-valued report when <paramref name="fills"/> is empty.
    /// </summary>
    public static TcaReport Generate(BacktestRequest request, IReadOnlyList<FillEvent> fills)
    {
        if (fills.Count == 0)
        {
            return new TcaReport(
                DateTimeOffset.UtcNow,
                request.StrategyAssemblyPath,
                new TcaCostSummary(0m, 0m, 0m, 0m, 0.0, 0, 0, 0),
                Array.Empty<SymbolTcaSummary>(),
                Array.Empty<TcaFillOutlier>());
        }

        // ── Aggregate run-level cost summary ───────────────────────────────
        decimal totalCommissions = 0m;
        decimal totalBuyNotional = 0m;
        decimal totalSellNotional = 0m;
        int buyFills = 0;
        int sellFills = 0;

        foreach (var fill in fills)
        {
            var notional = Math.Abs(fill.FilledQuantity) * fill.FillPrice;
            totalCommissions += fill.Commission;

            if (fill.FilledQuantity > 0)
            {
                totalBuyNotional += notional;
                buyFills++;
            }
            else
            {
                totalSellNotional += notional;
                sellFills++;
            }
        }

        var totalNotional = totalBuyNotional + totalSellNotional;
        var avgCommissionRateBps = totalNotional > 0
            ? (double)(totalCommissions / totalNotional) * 10_000.0
            : 0.0;

        var costSummary = new TcaCostSummary(
            totalCommissions,
            totalBuyNotional,
            totalSellNotional,
            totalNotional,
            Math.Round(avgCommissionRateBps, 2),
            fills.Count,
            buyFills,
            sellFills);

        // ── Per-symbol summaries ───────────────────────────────────────────
        var grouped = new Dictionary<string, List<FillEvent>>(StringComparer.Ordinal);
        foreach (var fill in fills)
        {
            if (!grouped.TryGetValue(fill.Symbol, out var bucket))
            {
                bucket = new List<FillEvent>();
                grouped[fill.Symbol] = bucket;
            }
            bucket.Add(fill);
        }

        var symbolSummaries = new List<SymbolTcaSummary>(grouped.Count);
        foreach (var (symbol, gFills) in grouped)
        {
            decimal symBuyNotional = 0m, symSellNotional = 0m, symCommission = 0m;
            long totalBuyQty = 0, totalSellQty = 0;
            decimal totalBuyWeighted = 0m, totalSellWeighted = 0m;

            foreach (var fill in gFills)
            {
                var notional = Math.Abs(fill.FilledQuantity) * fill.FillPrice;
                symCommission += fill.Commission;

                if (fill.FilledQuantity > 0)
                {
                    symBuyNotional += notional;
                    totalBuyQty += fill.FilledQuantity;
                    totalBuyWeighted += fill.FilledQuantity * fill.FillPrice;
                }
                else
                {
                    symSellNotional += notional;
                    var sellQty = Math.Abs(fill.FilledQuantity);
                    totalSellQty += sellQty;
                    totalSellWeighted += sellQty * fill.FillPrice;
                }
            }

            var symNotional = symBuyNotional + symSellNotional;
            var symBps = symNotional > 0
                ? (double)(symCommission / symNotional) * 10_000.0
                : 0.0;

            var avgBuyPrice = totalBuyQty > 0 ? totalBuyWeighted / totalBuyQty : 0m;
            var avgSellPrice = totalSellQty > 0 ? totalSellWeighted / totalSellQty : 0m;

            symbolSummaries.Add(new SymbolTcaSummary(
                symbol,
                symBuyNotional,
                symSellNotional,
                Math.Round(avgBuyPrice, 4),
                Math.Round(avgSellPrice, 4),
                symCommission,
                Math.Round(symBps, 2),
                gFills.Count));
        }

        // Sort descending by total commission (highest cost symbols first).
        symbolSummaries.Sort((a, b) => b.TotalCommission.CompareTo(a.TotalCommission));

        // ── Outlier detection ──────────────────────────────────────────────
        var outliers = new List<TcaFillOutlier>();
        foreach (var fill in fills)
        {
            var notional = Math.Abs(fill.FilledQuantity) * fill.FillPrice;
            if (notional <= 0m)
                continue;

            var fillBps = (double)(fill.Commission / notional) * 10_000.0;
            if (fillBps > avgCommissionRateBps * OutlierThresholdMultiplier
                && fillBps > OutlierMinimumRateBps)
            {
                outliers.Add(new TcaFillOutlier(
                    fill.FillId,
                    fill.Symbol,
                    notional,
                    fill.Commission,
                    Math.Round(fillBps, 2),
                    fill.FilledAt));
            }
        }

        // Sort descending by commission rate (worst outliers first).
        outliers.Sort((a, b) => b.CommissionRateBps.CompareTo(a.CommissionRateBps));

        return new TcaReport(
            DateTimeOffset.UtcNow,
            request.StrategyAssemblyPath,
            costSummary,
            symbolSummaries.AsReadOnly(),
            outliers.AsReadOnly());
    }
}
