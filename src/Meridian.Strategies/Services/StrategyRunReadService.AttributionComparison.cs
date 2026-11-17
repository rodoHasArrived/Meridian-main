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
    /// read from the run's daily portfolio snapshots. Returns null when the run does not exist.
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
            "Period P&L is the change in each symbol's cumulative realized plus unrealized P&L between month ends, from the run's daily snapshots.",
            "A symbol closed before a month end keeps its last recorded cumulative P&L. Margin interest is not split by period."
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

        // Cumulative P&L per symbol at the close of each boundary day, carrying closed symbols forward.
        var atCurrentEnd = CumulativePnlAsOf(snapshots, end);
        var atBaselineEnd = CumulativePnlAsOf(snapshots, baselineEnd);
        var atBaselineStart = CumulativePnlAsOf(snapshots, baselineStart.AddDays(-1));
        var symbols = atCurrentEnd.Keys.Union(atBaselineEnd.Keys).Union(atBaselineStart.Keys).OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase);
        var lines = symbols
            .Select(symbol => PeriodComparisonLineDto.Create(
                $"symbol:{symbol}",
                symbol,
                atCurrentEnd.GetValueOrDefault(symbol) - atBaselineEnd.GetValueOrDefault(symbol),
                atBaselineEnd.GetValueOrDefault(symbol) - atBaselineStart.GetValueOrDefault(symbol)))
            .Where(static line => line.Current != 0m || line.Prior != 0m)
            .ToList();

        // Position P&L in a backtest is gross: commissions are charged to cash, not to the lot. So
        // they are shown as their own negative line and deducted once, in the net total.
        var fills = run.Metrics?.Fills ?? [];
        var currentCommission = CommissionsWithin(fills, currentStart, end);
        var priorCommission = CommissionsWithin(fills, baselineStart, baselineEnd);
        var hasCommissions = currentCommission != 0m || priorCommission != 0m;
        if (hasCommissions)
        {
            lines.Add(PeriodComparisonLineDto.Create("commissions", "Commissions", -currentCommission, -priorCommission));
            notes.Add("Symbol P&L is before commissions. Commissions are the fills' commissions dated in each month, and the total is net of them.");
        }

        lines.Insert(0, PeriodComparisonLineDto.Create(
            "total",
            hasCommissions ? "Net P&L" : "Total P&L",
            lines.Sum(static line => line.Current),
            lines.Sum(static line => line.Prior),
            isTotal: true));

        return new PeriodComparisonDto(PeriodComparisonStatus.Ready, null, subject, null, current, baseline, lines, notes, generatedAt);
    }

    /// <summary>
    /// Each symbol's cumulative realized plus unrealized P&amp;L at the last snapshot on or before
    /// <paramref name="day"/>. A symbol absent from that snapshot (closed) keeps the last value it had.
    /// Before the first snapshot every symbol is zero.
    /// </summary>
    private static Dictionary<string, decimal> CumulativePnlAsOf(IReadOnlyList<PortfolioSnapshot> ordered, DateOnly day)
    {
        var cumulative = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in ordered)
        {
            if (snapshot.Date > day)
            {
                break;
            }

            // A symbol missing from this snapshot was closed: its entry keeps the last value recorded,
            // marked at that day's close, which stands in for the realized result of the close.
            foreach (var (symbol, position) in snapshot.Positions)
            {
                cumulative[symbol] = position.RealizedPnl + position.UnrealizedPnl;
            }
        }

        return cumulative;
    }

    /// <summary>Commissions on fills dated (UTC) from <paramref name="start"/> to <paramref name="end"/>, inclusive.</summary>
    private static decimal CommissionsWithin(IReadOnlyList<FillEvent> fills, DateOnly start, DateOnly end) =>
        fills
            .Where(fill => DateOnly.FromDateTime(fill.FilledAt.UtcDateTime) is var day && day >= start && day <= end)
            .Sum(static fill => fill.Commission);

    private static PeriodComparisonWindowDto MonthWindow(DateOnly start, DateOnly end, bool isPartial) =>
        new($"{start.ToString("MMM yyyy", CultureInfo.InvariantCulture)}{(isPartial ? " (partial)" : "")}", start, end, isPartial);
}
