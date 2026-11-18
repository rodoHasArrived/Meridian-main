using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Rebuilds the open-break count for recent days from the retained break queue, with no stored
/// snapshots. A break counts as open at a moment when it had been detected and was not yet closed,
/// using <see cref="CrossScreenCountDefinitions.IsOpenBreak"/> for "closed". The queue keeps every
/// status, so closed breaks still contribute to the days they were open.
/// </summary>
/// <remarks>
/// Some days cannot be established and are returned as null rather than guessed: a closed break with
/// no <c>ResolvedAt</c>, and any day before a reopen (a reopen clears the earlier resolution time, so
/// whether the break was closed in between is unknown).
/// </remarks>
public static class OpenBreakHistory
{
    public const string Basis =
        "Open breaks at each UTC day end, rebuilt from break detection, resolution and reopen times; today is the live count. Days the retained times cannot establish are left blank.";

    /// <summary>
    /// <paramref name="days"/> points, oldest first. The last point is <paramref name="now"/>'s live
    /// count, so it always equals the headline figure; earlier points are each UTC day's end.
    /// </summary>
    public static IReadOnlyList<WorkstationMetricHistoryPointDto> Build(
        IReadOnlyList<ReconciliationBreakQueueItem> items, DateTimeOffset now, int days = 14)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 2);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var points = new List<WorkstationMetricHistoryPointDto>(days);
        for (var offset = days - 1; offset >= 1; offset--)
        {
            var date = today.AddDays(-offset);
            var dayEnd = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            points.Add(new(date, CountOpenAt(items, dayEnd)));
        }
        points.Add(new(today, CrossScreenCountDefinitions.CountOpenBreaks(items)));
        return points;
    }

    /// <summary>Open breaks just before <paramref name="moment"/>, or null when any break's state then is unknown.</summary>
    public static decimal? CountOpenAt(IReadOnlyList<ReconciliationBreakQueueItem> items, DateTimeOffset moment)
    {
        var open = 0;
        foreach (var item in items)
        {
            if (item.DetectedAt >= moment)
                continue;
            switch (StateAt(item, moment))
            {
                case BreakState.Open:
                    open++;
                    break;
                case BreakState.Unknown:
                    return null;
            }
        }
        return open;
    }

    private enum BreakState { Open, Closed, Unknown }

    private static BreakState StateAt(ReconciliationBreakQueueItem item, DateTimeOffset moment)
    {
        var reopened = item.ReopenedAt is not null || item.LifecycleState == ReconciliationCaseLifecycleState.Reopened;
        // Before a reopen the earlier resolution time was cleared: it may have been closed then.
        if (reopened && (item.ReopenedAt is not { } reopenedAt || moment <= reopenedAt))
            return BreakState.Unknown;
        if (CrossScreenCountDefinitions.IsOpenBreak(item.Status))
            return BreakState.Open;
        if (item.ResolvedAt is not { } resolvedAt)
            return BreakState.Unknown;
        return resolvedAt < moment ? BreakState.Closed : BreakState.Open;
    }
}
