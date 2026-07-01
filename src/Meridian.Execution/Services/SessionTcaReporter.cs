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
    /// quantity contribute. Report sequences whose per-order quantities are cumulative (as
    /// emitted by brokers that report running totals, e.g. the IB gateway) are converted to
    /// incremental quantities before aggregation. <paramref name="orders"/> is optional and
    /// enriches the report with time-to-fill and limit-price-improvement statistics.
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

        // Last-writer-wins on duplicate order updates: the latest OrderState for an order id
        // carries the final limit price, creation timestamp, and executed quantity.
        var ordersById = new Dictionary<string, OrderState>(StringComparer.Ordinal);
        if (orders is not null)
        {
            foreach (var order in orders)
                ordersById[order.OrderId] = order;
        }

        var normalized = NormalizeFills(fills, ordersById);

        if (normalized.Count == 0)
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

        var costSummary = BuildCostSummary(normalized);
        var symbolSummaries = BuildSymbolSummaries(normalized);
        var outliers = DetectOutliers(normalized);
        var executionQuality = BuildExecutionQuality(normalized, ordersById);

        return new SessionTcaReport(
            sessionId,
            strategyId,
            DateTimeOffset.UtcNow,
            costSummary,
            symbolSummaries,
            outliers,
            executionQuality);
    }

    /// <summary>A fill with its effective (incremental) executed quantity.</summary>
    private sealed record TcaFill(
        string OrderId,
        string Symbol,
        OrderSide Side,
        decimal Quantity,
        decimal Price,
        decimal Commission,
        DateTimeOffset Timestamp)
    {
        public decimal Notional => Quantity * Price;
    }

    /// <summary>
    /// Filters the raw report tape down to usable fills and converts cumulative per-order
    /// quantity sequences into incremental ones. A per-order sequence is treated as cumulative
    /// when it contains multiple reports whose quantities never decrease and whose sum exceeds
    /// the executed quantity known for the order — either the order quantity (a 40-share
    /// partial followed by a 100-share final report on a 100-share order describes 100 executed
    /// shares, not 140) or, for orders still working, the executed quantity recorded in order
    /// history (cumulative partials of 10 then 20 against a 100-share order describe 20
    /// executed shares, not 30). Incremental tapes (e.g. the paper gateway) sum exactly to the
    /// executed quantity and are unaffected. When neither signal is available the sequence is
    /// left as-is because the two encodings cannot be distinguished.
    /// </summary>
    private static List<TcaFill> NormalizeFills(
        IReadOnlyList<ExecutionReport> fills,
        IReadOnlyDictionary<string, OrderState> ordersById)
    {
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

        var result = new List<TcaFill>(usable.Count);
        foreach (var orderGroup in usable.GroupBy(static f => f.OrderId, StringComparer.Ordinal))
        {
            var sequence = orderGroup.OrderBy(static f => f.Timestamp).ToList();
            ordersById.TryGetValue(orderGroup.Key, out var orderState);
            var isCumulative = IsCumulativeSequence(sequence, orderState);

            decimal previousCumulative = 0m;
            foreach (var report in sequence)
            {
                var quantity = isCumulative
                    ? report.FilledQuantity - previousCumulative
                    : report.FilledQuantity;
                previousCumulative = report.FilledQuantity;

                if (quantity <= 0m)
                    continue;

                result.Add(new TcaFill(
                    report.OrderId,
                    report.Symbol,
                    report.Side,
                    quantity,
                    report.FillPrice!.Value,
                    report.Commission ?? 0m,
                    report.Timestamp));
            }
        }

        result.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    private static bool IsCumulativeSequence(List<ExecutionReport> sequence, OrderState? orderState)
    {
        if (sequence.Count < 2)
            return false;

        decimal sum = 0m;
        decimal previous = 0m;
        foreach (var report in sequence)
        {
            if (report.FilledQuantity < previous)
                return false;
            previous = report.FilledQuantity;
            sum += report.FilledQuantity;
        }

        var orderQuantity = sequence[^1].OrderQuantity;
        if (orderQuantity > 0m && sum > orderQuantity)
            return true;

        // Order still working: compare against the executed quantity the order history knows
        // about. A cumulative tape always sums past it; an incremental tape sums exactly to it.
        return orderState is { FilledQuantity: > 0m } && sum > orderState.FilledQuantity;
    }

    private static SessionTcaCostSummary BuildCostSummary(List<TcaFill> fills)
    {
        decimal totalCommissions = 0m;
        decimal totalBuyNotional = 0m;
        decimal totalSellNotional = 0m;
        int buyFills = 0;
        int sellFills = 0;

        foreach (var fill in fills)
        {
            totalCommissions += fill.Commission;

            if (fill.Side == OrderSide.Buy)
            {
                totalBuyNotional += fill.Notional;
                buyFills++;
            }
            else
            {
                totalSellNotional += fill.Notional;
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

    private static IReadOnlyList<SessionSymbolTcaSummary> BuildSymbolSummaries(List<TcaFill> fills)
    {
        var grouped = new Dictionary<string, List<TcaFill>>(StringComparer.Ordinal);
        foreach (var fill in fills)
        {
            if (!grouped.TryGetValue(fill.Symbol, out var bucket))
            {
                bucket = new List<TcaFill>();
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
                commission += fill.Commission;

                if (fill.Side == OrderSide.Buy)
                {
                    buyNotional += fill.Notional;
                    buyQty += fill.Quantity;
                    buyWeighted += fill.Quantity * fill.Price;
                }
                else
                {
                    sellNotional += fill.Notional;
                    sellQty += fill.Quantity;
                    sellWeighted += fill.Quantity * fill.Price;
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

    private static IReadOnlyList<SessionTcaFillOutlier> DetectOutliers(List<TcaFill> fills)
    {
        // The median (not the aggregate mean) is used as the baseline so that a single high-cost
        // fill cannot inflate the threshold enough to avoid detection.
        var perFillBps = new List<double>(fills.Count);
        foreach (var fill in fills)
        {
            if (fill.Notional > 0m)
                perFillBps.Add((double)(fill.Commission / fill.Notional) * 10_000.0);
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
            if (fill.Notional <= 0m)
                continue;

            var fillBps = (double)(fill.Commission / fill.Notional) * 10_000.0;
            if (fillBps > medianBps * OutlierThresholdMultiplier
                && fillBps > OutlierMinimumRateBps)
            {
                outliers.Add(new SessionTcaFillOutlier(
                    fill.OrderId,
                    fill.Symbol,
                    fill.Notional,
                    fill.Commission,
                    Math.Round(fillBps, 2),
                    fill.Timestamp));
            }
        }

        // Sort descending by commission rate (worst outliers first).
        outliers.Sort(static (a, b) => b.CommissionRateBps.CompareTo(a.CommissionRateBps));
        return outliers;
    }

    private static SessionTcaExecutionQuality BuildExecutionQuality(
        List<TcaFill> fills,
        IReadOnlyDictionary<string, OrderState> ordersById)
    {
        if (ordersById.Count == 0)
            return SessionTcaExecutionQuality.Empty;

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

                // Positive = filled better than the limit (price improvement).
                var improvementBps = fill.Side == OrderSide.Buy
                    ? (limit - fill.Price) / limit * 10_000m
                    : (fill.Price - limit) / limit * 10_000m;

                improvementWeightedBps += improvementBps * fill.Notional;
                improvementNotional += fill.Notional;
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
