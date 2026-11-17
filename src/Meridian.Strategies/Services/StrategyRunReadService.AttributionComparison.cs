using System.Globalization;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

public sealed partial class StrategyRunReadService
{
    /// <summary>
    /// Per-symbol P&amp;L for one calendar month of a run against the month before (UX Phase E). The month
    /// is the one containing <paramref name="periodEnd"/> (default: the run's last recorded day). Period
    /// P&amp;L is the change in each symbol's cumulative realized plus unrealized P&amp;L between month ends,
    /// read from the run's daily portfolio snapshots, with commissions and financing as their own lines;
    /// the total is the change in run equity. Returns null when the run does not exist.
    /// </summary>
    public Task<PeriodComparisonDto?> GetAttributionPeriodComparisonAsync(string runId, DateOnly? periodEnd, CancellationToken ct = default) =>
        GetAttributionPeriodComparisonCoreAsync(runId, periodEnd, scope: null, ct);

    public Task<PeriodComparisonDto?> GetAttributionPeriodComparisonAsync(
        string runId,
        DateOnly? periodEnd,
        StrategyRunReadScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return GetAttributionPeriodComparisonCoreAsync(runId, periodEnd, scope, ct);
    }

    private async Task<PeriodComparisonDto?> GetAttributionPeriodComparisonCoreAsync(
        string runId,
        DateOnly? periodEnd,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        var run = await GetRunForScopeAsync(runId, scope, ct).ConfigureAwait(false);
        return run is null ? null : BuildAttributionPeriodComparison(run, periodEnd, DateTimeOffset.UtcNow);
    }

    internal static PeriodComparisonDto BuildAttributionPeriodComparison(StrategyRunEntry run, DateOnly? periodEnd, DateTimeOffset generatedAt)
    {
        var subject = run.StrategyName;
        var notes = new List<string>
        {
            "Symbol P&L is the change in each symbol's cumulative realized plus unrealized P&L between month ends, from the run's daily snapshots, before commissions.",
            "A symbol closed during a month counts the realized result of its closed lots, not its last mark."
        };
        var snapshots = run.Metrics?.Snapshots?
            .OrderBy(static snapshot => snapshot.Date)
            .ToArray() ?? [];
        if (snapshots.Length == 0)
        {
            var empty = new PeriodComparisonWindowDto("Unavailable", null, periodEnd ?? DateOnly.FromDateTime(generatedAt.UtcDateTime), false);
            return new PeriodComparisonDto(
                PeriodComparisonStatus.Unavailable,
                "This run retained summary results only, without daily snapshots, so P&L cannot be split by period.",
                subject, null, empty, null, [], notes, generatedAt);
        }

        var firstDay = snapshots[0].Date;
        var lastDay = snapshots[^1].Date;
        var end = periodEnd is { } requested && requested < lastDay ? requested : lastDay;
        var currentStart = new DateOnly(end.Year, end.Month, 1);
        var baselineEnd = currentStart.AddDays(-1);
        var baselineStart = new DateOnly(baselineEnd.Year, baselineEnd.Month, 1);
        var current = MonthWindow(currentStart, end, isPartial: end < currentStart.AddMonths(1).AddDays(-1));
        var baseline = MonthWindow(baselineStart, baselineEnd, isPartial: firstDay > baselineStart);

        if (firstDay > baselineEnd)
        {
            return new PeriodComparisonDto(
                PeriodComparisonStatus.Unavailable,
                $"The run starts on {firstDay.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}, after the prior month ends, so there is no prior period to compare.",
                subject, null, current, baseline, [], notes, generatedAt);
        }

        // Cumulative P&L per symbol at the close of each boundary day.
        var closedLots = snapshots[^1].Accounts.Values
            .SelectMany(static account => account.ClosedLots ?? [])
            .ToArray();
        var atCurrentEnd = CumulativePnlAsOf(snapshots, closedLots, end);
        var atBaselineEnd = CumulativePnlAsOf(snapshots, closedLots, baselineEnd);
        var atBaselineStart = CumulativePnlAsOf(snapshots, closedLots, baselineStart.AddDays(-1));
        var symbols = atCurrentEnd.Keys.Union(atBaselineEnd.Keys).Union(atBaselineStart.Keys).OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase);
        var lines = symbols
            .Select(symbol => PeriodComparisonLineDto.Create(
                $"symbol:{symbol}",
                symbol,
                atCurrentEnd.GetValueOrDefault(symbol) - atBaselineEnd.GetValueOrDefault(symbol),
                atBaselineEnd.GetValueOrDefault(symbol) - atBaselineStart.GetValueOrDefault(symbol)))
            .Where(static line => line.Current != 0m || line.Prior != 0m)
            .ToList();

        // Position P&L in a backtest is gross: commissions are charged to cash, not to the lot. Fills
        // carry them; a run without fills falls back to the commission cash flows.
        var fills = run.Metrics?.Fills ?? [];
        var (currentCommission, priorCommission) = fills.Count > 0
            ? (-CommissionsWithin(fills, currentStart, end), -CommissionsWithin(fills, baselineStart, baselineEnd))
            : (CashFlowsWithin<CommissionCashFlow>(snapshots, currentStart, end), CashFlowsWithin<CommissionCashFlow>(snapshots, baselineStart, baselineEnd));
        AddIfNonZero(lines, "commissions", "Commissions", currentCommission, priorCommission);
        AddIfNonZero(lines, "margin-interest", "Margin interest",
            CashFlowsWithin<MarginInterestCashFlow>(snapshots, currentStart, end), CashFlowsWithin<MarginInterestCashFlow>(snapshots, baselineStart, baselineEnd));
        AddIfNonZero(lines, "short-rebates", "Short rebates",
            CashFlowsWithin<ShortRebateCashFlow>(snapshots, currentStart, end), CashFlowsWithin<ShortRebateCashFlow>(snapshots, baselineStart, baselineEnd));
        AddIfNonZero(lines, "cash-interest", "Cash interest",
            CashFlowsWithin<CashInterestCashFlow>(snapshots, currentStart, end), CashFlowsWithin<CashInterestCashFlow>(snapshots, baselineStart, baselineEnd));

        // The total is the run's equity change over each month, so it is the real result. Whatever the
        // lines above do not explain (dividends, corporate-action cash, rounding) is shown as Other.
        var initialEquity = run.Metrics?.Metrics.InitialCapital ?? run.Metrics?.Request.InitialCash ?? 0m;
        var equityAtCurrentEnd = EquityAsOf(snapshots, end, initialEquity);
        var equityAtBaselineEnd = EquityAsOf(snapshots, baselineEnd, initialEquity);
        var equityAtBaselineStart = EquityAsOf(snapshots, baselineStart.AddDays(-1), initialEquity);
        var currentNet = equityAtCurrentEnd - equityAtBaselineEnd;
        var priorNet = equityAtBaselineEnd - equityAtBaselineStart;
        AddIfNonZero(lines, "other", "Other (dividends, corporate actions, rounding)",
            currentNet - lines.Sum(static line => line.Current), priorNet - lines.Sum(static line => line.Prior));
        lines.Insert(0, PeriodComparisonLineDto.Create("total", "Net P&L", currentNet, priorNet, isTotal: true));

        notes.Add("Commissions, margin interest, short rebates and cash interest come from the run's fills and daily cash flows in each month.");
        notes.Add("Net P&L is the change in run equity over the month; Other is what the lines above do not explain, such as dividends and corporate-action cash.");
        return new PeriodComparisonDto(PeriodComparisonStatus.Ready, null, subject, null, current, baseline, lines, notes, generatedAt);
    }

    private static void AddIfNonZero(List<PeriodComparisonLineDto> lines, string key, string label, decimal current, decimal prior)
    {
        if (current != 0m || prior != 0m)
        {
            lines.Add(PeriodComparisonLineDto.Create(key, label, current, prior));
        }
    }

    /// <summary>
    /// Each symbol's cumulative realized plus unrealized P&amp;L at the last snapshot on or before
    /// <paramref name="day"/>. A symbol missing from that snapshot is flat: it keeps its last recorded
    /// realized P&amp;L plus the realized result of the lots closed after that snapshot, up to the day.
    /// Before the first snapshot every symbol is zero.
    /// </summary>
    private static Dictionary<string, decimal> CumulativePnlAsOf(IReadOnlyList<PortfolioSnapshot> ordered, IReadOnlyList<ClosedLot> closedLots, DateOnly day)
    {
        var lastSeen = new Dictionary<string, (DateTimeOffset At, decimal Realized, decimal Total)>(StringComparer.OrdinalIgnoreCase);
        PortfolioSnapshot? latest = null;
        foreach (var snapshot in ordered)
        {
            if (snapshot.Date > day)
            {
                break;
            }

            latest = snapshot;
            foreach (var (symbol, position) in snapshot.Positions)
            {
                lastSeen[symbol] = (snapshot.Timestamp, position.RealizedPnl, position.RealizedPnl + position.UnrealizedPnl);
            }
        }

        var cumulative = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (latest is null)
        {
            return cumulative;
        }

        foreach (var (symbol, seen) in lastSeen)
        {
            cumulative[symbol] = latest.Positions.ContainsKey(symbol)
                ? seen.Total
                : seen.Realized + ClosedLotPnl(closedLots, symbol, after: seen.At, day);
        }

        // A symbol opened and closed between two snapshots never appears in one.
        foreach (var symbol in closedLots.Select(static lot => lot.Symbol).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!lastSeen.ContainsKey(symbol))
            {
                cumulative[symbol] = ClosedLotPnl(closedLots, symbol, after: DateTimeOffset.MinValue, day);
            }
        }

        return cumulative;
    }

    private static decimal ClosedLotPnl(IReadOnlyList<ClosedLot> closedLots, string symbol, DateTimeOffset after, DateOnly day) =>
        closedLots
            .Where(lot => string.Equals(lot.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                && lot.ClosedAt > after
                && DateOnly.FromDateTime(lot.ClosedAt.UtcDateTime) <= day)
            .Sum(static lot => lot.RealizedPnl);

    private static decimal EquityAsOf(IReadOnlyList<PortfolioSnapshot> ordered, DateOnly day, decimal initialEquity)
    {
        var equity = initialEquity;
        foreach (var snapshot in ordered)
        {
            if (snapshot.Date > day)
            {
                break;
            }

            equity = snapshot.TotalEquity;
        }

        return equity;
    }

    /// <summary>Signed sum of the <typeparamref name="T"/> cash flows on snapshots dated from <paramref name="start"/> to <paramref name="end"/>.</summary>
    private static decimal CashFlowsWithin<T>(IReadOnlyList<PortfolioSnapshot> ordered, DateOnly start, DateOnly end)
        where T : CashFlowEntry =>
        ordered
            .Where(snapshot => snapshot.Date >= start && snapshot.Date <= end)
            .SelectMany(static snapshot => snapshot.DayCashFlows ?? [])
            .OfType<T>()
            .Sum(static flow => flow.Amount);

    /// <summary>Commissions on fills dated (UTC) from <paramref name="start"/> to <paramref name="end"/>, inclusive.</summary>
    private static decimal CommissionsWithin(IReadOnlyList<FillEvent> fills, DateOnly start, DateOnly end) =>
        fills
            .Where(fill => DateOnly.FromDateTime(fill.FilledAt.UtcDateTime) is var day && day >= start && day <= end)
            .Sum(static fill => fill.Commission);

    private static PeriodComparisonWindowDto MonthWindow(DateOnly start, DateOnly end, bool isPartial) =>
        new($"{start.ToString("MMM yyyy", CultureInfo.InvariantCulture)}{(isPartial ? " (partial)" : "")}", start, end, isPartial);
}
