using Meridian.Contracts.Domain.Models;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Verdict describing a backfill result whose newest bar is materially older than the end of
/// the requested range — e.g. a provider whose dataset is frozen (Nasdaq WIKI stopped in
/// March 2018) returning years-old prices for a request that expects data through today.
/// </summary>
/// <param name="LatestSessionDate">Session date of the newest bar in the result.</param>
/// <param name="ExpectedThrough">The date the request expected data to reach (requested end, capped at today).</param>
/// <param name="StaleDays">Calendar days between <paramref name="LatestSessionDate"/> and <paramref name="ExpectedThrough"/>.</param>
/// <param name="Description">Human-readable description for logs and validation signals.</param>
public sealed record StaleBarsVerdict(
    DateOnly LatestSessionDate,
    DateOnly ExpectedThrough,
    int StaleDays,
    string Description);

/// <summary>
/// Recency and sanity validation for backfilled bars. OHLC invariants are already enforced by
/// the <see cref="HistoricalBar"/> constructor; this adds the request-relative checks the
/// constructor cannot express: results whose newest bar falls far short of the requested range
/// end (stale/frozen datasets) and bars dated in the future.
/// </summary>
public static class BackfillBarValidation
{
    /// <summary>
    /// Calendar-day gap tolerated between the requested range end and the newest returned bar
    /// before a result is considered stale. Wide enough to absorb weekends, market holidays,
    /// and provider publish lag; narrow enough to catch datasets frozen months or years ago.
    /// </summary>
    public const int DefaultStaleToleranceDays = 10;

    /// <summary>
    /// Evaluates whether a non-empty daily-bar result is stale relative to the requested range.
    /// Historical-era requests (an explicit <paramref name="requestedTo"/> in the past) are
    /// compared against that end date, so backfilling old ranges never trips this check.
    /// </summary>
    /// <returns>A verdict when the result is stale; <c>null</c> when it is acceptable.</returns>
    public static StaleBarsVerdict? EvaluateDailyRecency(
        IReadOnlyList<HistoricalBar> bars,
        DateOnly? requestedTo,
        DateOnly? today = null,
        int staleToleranceDays = DefaultStaleToleranceDays)
    {
        if (bars.Count == 0)
            return null;

        var utcToday = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedThrough = requestedTo is { } to && to < utcToday ? to : utcToday;

        var latest = bars[0].SessionDate;
        foreach (var bar in bars)
        {
            if (bar.SessionDate > latest)
                latest = bar.SessionDate;
        }

        var staleDays = expectedThrough.DayNumber - latest.DayNumber;
        if (staleDays <= staleToleranceDays)
            return null;

        return new StaleBarsVerdict(
            latest,
            expectedThrough,
            staleDays,
            $"newest bar {latest:yyyy-MM-dd} is {staleDays} calendar days short of the requested range end {expectedThrough:yyyy-MM-dd}");
    }

    /// <summary>
    /// Removes bars dated after today (one day of tolerance for exchange-local time zones ahead
    /// of UTC). A future session date is provider garbage that would otherwise be persisted as
    /// legitimate history.
    /// </summary>
    public static IReadOnlyList<HistoricalBar> RemoveFutureDatedBars(
        IReadOnlyList<HistoricalBar> bars,
        out int removedCount,
        DateOnly? today = null)
    {
        removedCount = 0;
        if (bars.Count == 0)
            return bars;

        var latestAllowed = (today ?? DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(1);
        var hasFuture = false;
        foreach (var bar in bars)
        {
            if (bar.SessionDate > latestAllowed)
            {
                hasFuture = true;
                break;
            }
        }

        if (!hasFuture)
            return bars;

        var filtered = new List<HistoricalBar>(bars.Count);
        foreach (var bar in bars)
        {
            if (bar.SessionDate > latestAllowed)
                removedCount++;
            else
                filtered.Add(bar);
        }

        return filtered;
    }
}
