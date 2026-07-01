using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>
/// Generates a Transaction Cost Analysis (TCA) report from a paper/live session fill tape.
/// Computes commission attribution, per-symbol cost breakdowns, outlier detection, and
/// execution-quality statistics (time-to-fill, limit-price improvement). This is a
/// post-processing pass over the session's persisted <see cref="ExecutionReport"/> history;
/// it requires no market data beyond the fills and orders already recorded by the session.
/// </summary>
public static class SessionTcaReporter
{
    // Fills whose commission rate exceeds this multiplier × the median per-fill rate are flagged.
    // The median (not the aggregate mean) is used so that a single outlier fill cannot inflate
    // the baseline threshold enough to avoid detection.
    private const double OutlierThresholdMultiplier = 3.0;

    // Suppress outlier flagging for fills below this minimum rate (bps) to avoid noise on
    // zero- or near-zero-commission fills.
    private const double OutlierMinimumRateBps = 1.0;

    /// <summary>
    /// Generates a <see cref="SessionTcaReport"/> for <paramref name="sessionId"/> from the
    /// session's fill history. Only reports of type <see cref="ExecutionReportType.Fill"/> or
    /// <see cref="ExecutionReportType.PartialFill"/> with a fill price and positive filled
    /// quantity contribute. <paramref name="orders"/> is optional and enriches the report with
    /// time-to-fill and limit-price-improvement statistics when supplied.
    /// Returns a zero-valued report when no usable fills exist.
    /// </summary>
    public static SessionTcaReport Generate(
        string sessionId,
        string strategyId,
        IReadOnlyList<ExecutionReport> fills,
        IReadOnlyList<OrderState>? orders = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(fills);

        var usable = new List<ExecutionReport>(fills.Count);
        foreach (var fill in fills)
        {
            if (fill.ReportType is ExecutionReportType.Fill or ExecutionReportType.PartialFill
                && fill.FillPrice is > 0m
                && fill.FilledQuantity > 0m)
            {
                usable.Add(fill);
            }
        }

        if (usable.Count == 0)
        {
            return new SessionTcaReport(
                sessionId,
                strategyId,
                DateTimeOffset.UtcNow,
                new SessionTcaCostSummary(0m, 0m, 0m, 0m, 0.0, 0, 0, 0),
                Array.Empty<SessionSymbolTcaSummary>(),
                Array.Empty<SessionTcaFillOutlier>(),
                SessionTcaExecutionQuality.Empty);
        }

        var costSummary = BuildCostSummary(usable);
        var symbolSummaries = BuildSymbolSummaries(usable);
        var outliers = DetectOutliers(usable);
        var executionQuality = BuildExecutionQuality(usable, orders);

        return new SessionTcaReport(
            sessionId,
            strategyId,
            DateTimeOffset.UtcNow,
            costSummary,
            symbolSummaries,
            outliers,
            executionQuality);
    }

    private static SessionTcaCostSummary BuildCostSummary(List<ExecutionReport> fills)
    {
        decimal totalCommissions = 0m;
        decimal totalBuyNotional = 0m;
        decimal totalSellNotional = 0m;
        int buyFills = 0;
        int sellFills = 0;

        foreach (var fill in fills)
        {
            var notional = fill.FilledQuantity * fill.FillPrice!.Value;
            totalCommissions += fill.Commission ?? 0m;

            if (fill.Side == OrderSide.Buy)
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

        return new SessionTcaCostSummary(
            totalCommissions,
            totalBuyNotional,
            totalSellNotional,
            totalNotional,
            Math.Round(avgCommissionRateBps, 2),
            fills.Count,
            buyFills,
            sellFills);
    }

    private static IReadOnlyList<SessionSymbolTcaSummary> BuildSymbolSummaries(List<ExecutionReport> fills)
    {
        var grouped = new Dictionary<string, List<ExecutionReport>>(StringComparer.Ordinal);
        foreach (var fill in fills)
        {
            if (!grouped.TryGetValue(fill.Symbol, out var bucket))
            {
                bucket = new List<ExecutionReport>();
                grouped[fill.Symbol] = bucket;
            }
            bucket.Add(fill);
        }

        var summaries = new List<SessionSymbolTcaSummary>(grouped.Count);
        foreach (var (symbol, symbolFills) in grouped)
        {
            decimal buyNotional = 0m, sellNotional = 0m, commission = 0m;
            decimal buyQty = 0m, sellQty = 0m;
            decimal buyWeighted = 0m, sellWeighted = 0m;

            foreach (var fill in symbolFills)
            {
                var price = fill.FillPrice!.Value;
                var notional = fill.FilledQuantity * price;
                commission += fill.Commission ?? 0m;

                if (fill.Side == OrderSide.Buy)
                {
                    buyNotional += notional;
                    buyQty += fill.FilledQuantity;
                    buyWeighted += fill.FilledQuantity * price;
                }
                else
                {
                    sellNotional += notional;
                    sellQty += fill.FilledQuantity;
                    sellWeighted += fill.FilledQuantity * price;
                }
            }

            var symbolNotional = buyNotional + sellNotional;
            var symbolBps = symbolNotional > 0
                ? (double)(commission / symbolNotional) * 10_000.0
                : 0.0;

            summaries.Add(new SessionSymbolTcaSummary(
                symbol,
                buyNotional,
                sellNotional,
                Math.Round(buyQty > 0 ? buyWeighted / buyQty : 0m, 4),
                Math.Round(sellQty > 0 ? sellWeighted / sellQty : 0m, 4),
                commission,
                Math.Round(symbolBps, 2),
                symbolFills.Count));
        }

        // Sort descending by total commission (highest cost symbols first).
        summaries.Sort(static (a, b) => b.TotalCommission.CompareTo(a.TotalCommission));
        return summaries;
    }

    private static IReadOnlyList<SessionTcaFillOutlier> DetectOutliers(List<ExecutionReport> fills)
    {
        // The median (not the aggregate mean) is used as the baseline so that a single high-cost
        // fill cannot inflate the threshold enough to avoid detection.
        var perFillBps = new List<double>(fills.Count);
        foreach (var fill in fills)
        {
            var notional = fill.FilledQuantity * fill.FillPrice!.Value;
            if (notional > 0m)
                perFillBps.Add((double)((fill.Commission ?? 0m) / notional) * 10_000.0);
        }

        double medianBps = 0.0;
        if (perFillBps.Count > 0)
        {
            perFillBps.Sort();
            var mid = perFillBps.Count / 2;
            medianBps = perFillBps.Count % 2 == 0
                ? (perFillBps[mid - 1] + perFillBps[mid]) / 2.0
                : perFillBps[mid];
        }

        var outliers = new List<SessionTcaFillOutlier>();
        foreach (var fill in fills)
        {
            var notional = fill.FilledQuantity * fill.FillPrice!.Value;
            if (notional <= 0m)
                continue;

            var fillBps = (double)((fill.Commission ?? 0m) / notional) * 10_000.0;
            if (fillBps > medianBps * OutlierThresholdMultiplier
                && fillBps > OutlierMinimumRateBps)
            {
                outliers.Add(new SessionTcaFillOutlier(
                    fill.OrderId,
                    fill.Symbol,
                    notional,
                    fill.Commission ?? 0m,
                    Math.Round(fillBps, 2),
                    fill.Timestamp));
            }
        }

        // Sort descending by commission rate (worst outliers first).
        outliers.Sort(static (a, b) => b.CommissionRateBps.CompareTo(a.CommissionRateBps));
        return outliers;
    }

    private static SessionTcaExecutionQuality BuildExecutionQuality(
        List<ExecutionReport> fills,
        IReadOnlyList<OrderState>? orders)
    {
        if (orders is not { Count: > 0 })
            return SessionTcaExecutionQuality.Empty;

        // Last-writer-wins on duplicate order updates: the latest OrderState for an order id
        // carries the final limit price and creation timestamp.
        var ordersById = new Dictionary<string, OrderState>(StringComparer.Ordinal);
        foreach (var order in orders)
            ordersById[order.OrderId] = order;

        var timesToFillSeconds = new List<double>();
        decimal improvementWeightedBps = 0m;
        decimal improvementNotional = 0m;
        var limitOrderIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fill in fills)
        {
            if (!ordersById.TryGetValue(fill.OrderId, out var order))
                continue;

            var elapsed = (fill.Timestamp - order.CreatedAt).TotalSeconds;
            if (elapsed >= 0)
                timesToFillSeconds.Add(elapsed);

            if (order.LimitPrice is > 0m)
            {
                limitOrderIds.Add(order.OrderId);
                var limit = order.LimitPrice.Value;
                var price = fill.FillPrice!.Value;
                var notional = fill.FilledQuantity * price;

                // Positive = filled better than the limit (price improvement).
                var improvementBps = fill.Side == OrderSide.Buy
                    ? (limit - price) / limit * 10_000m
                    : (price - limit) / limit * 10_000m;

                improvementWeightedBps += improvementBps * notional;
                improvementNotional += notional;
            }
        }

        double medianTimeToFill = 0.0;
        if (timesToFillSeconds.Count > 0)
        {
            timesToFillSeconds.Sort();
            var mid = timesToFillSeconds.Count / 2;
            medianTimeToFill = timesToFillSeconds.Count % 2 == 0
                ? (timesToFillSeconds[mid - 1] + timesToFillSeconds[mid]) / 2.0
                : timesToFillSeconds[mid];
        }

        var avgImprovementBps = improvementNotional > 0m
            ? (double)(improvementWeightedBps / improvementNotional)
            : 0.0;

        return new SessionTcaExecutionQuality(
            limitOrderIds.Count,
            Math.Round(avgImprovementBps, 2),
            Math.Round(medianTimeToFill, 3),
            timesToFillSeconds.Count);
    }
}

/// <summary>TCA report over a paper/live session fill tape.</summary>
public sealed record SessionTcaReport(
    string SessionId,
    string StrategyId,
    DateTimeOffset GeneratedAtUtc,
    SessionTcaCostSummary CostSummary,
    IReadOnlyList<SessionSymbolTcaSummary> SymbolSummaries,
    IReadOnlyList<SessionTcaFillOutlier> Outliers,
    SessionTcaExecutionQuality ExecutionQuality);

/// <summary>Aggregate session-level cost summary.</summary>
public sealed record SessionTcaCostSummary(
    decimal TotalCommissions,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal TotalNotional,
    double CommissionRateBps,
    int TotalFills,
    int BuyFills,
    int SellFills);

/// <summary>Per-symbol cost breakdown, sorted by total commission descending.</summary>
public sealed record SessionSymbolTcaSummary(
    string Symbol,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal AvgBuyPrice,
    decimal AvgSellPrice,
    decimal TotalCommission,
    double CommissionRateBps,
    int TotalFills);

/// <summary>A fill whose commission rate significantly exceeds the session median.</summary>
public sealed record SessionTcaFillOutlier(
    string OrderId,
    string Symbol,
    decimal Notional,
    decimal Commission,
    double CommissionRateBps,
    DateTimeOffset FilledAt);

/// <summary>
/// Execution-quality statistics derived by joining fills to their originating orders.
/// <see cref="AvgLimitPriceImprovementBps"/> is notional-weighted; positive values mean fills
/// executed better than their limit price.
/// </summary>
public sealed record SessionTcaExecutionQuality(
    int OrdersWithLimitPrice,
    double AvgLimitPriceImprovementBps,
    double MedianTimeToFillSeconds,
    int TimedFillCount)
{
    public static SessionTcaExecutionQuality Empty { get; } = new(0, 0.0, 0.0, 0);
}
