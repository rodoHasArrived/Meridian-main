using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Rebuilds the accounting workspace's Timing Drift count for recent days from retained strategy
/// runs and retained reconciliation history, with no stored snapshots.
/// </summary>
/// <remarks>
/// The headline counts, among the workspace's most recent runs (newest start first), those whose
/// latest reconciliation reported timing drift. For a past day end the same rule is applied to what
/// was retained then: the most recent runs that had started by that moment, each judged by its latest
/// reconciliation created before it. A run's start time never moves (a restart is a new run), so the
/// population at a past moment is known from today's list, except when that list may have been cut
/// off by the read limit and holds too few runs started by then; such days are left null rather than
/// guessed.
/// </remarks>
public static class TimingDriftHistory
{
    public const string Basis =
        "Runs with timing drift at each UTC day end, rebuilt from run start times and retained reconciliation history; today is the live count. Days the retained run list cannot establish are left blank.";

    /// <summary>
    /// <paramref name="days"/> points, oldest first. The last point is <paramref name="liveCount"/>,
    /// so it always equals the headline figure; earlier points are each UTC day's end.
    /// </summary>
    /// <param name="runs">Authorized runs in workspace order: newest start first, then run id.</param>
    /// <param name="runListMayBeTruncated">Whether older runs may be missing from <paramref name="runs"/>.</param>
    /// <param name="populationSize">How many of the most recent runs the headline counts.</param>
    /// <param name="readHistory">Retained reconciliation summaries for one run, any order.</param>
    public static async Task<IReadOnlyList<WorkstationMetricHistoryPointDto>> BuildAsync(
        IReadOnlyList<StrategyRunSummary> runs,
        bool runListMayBeTruncated,
        int populationSize,
        int liveCount,
        Func<string, CancellationToken, Task<IReadOnlyList<ReconciliationRunSummary>>> readHistory,
        DateTimeOffset now,
        CancellationToken ct,
        int days = 14)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(readHistory);
        ArgumentOutOfRangeException.ThrowIfLessThan(populationSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 2);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var populations = new List<(DateOnly Date, StrategyRunSummary[]? Runs, DateTimeOffset DayEnd)>(days - 1);
        for (var offset = days - 1; offset >= 1; offset--)
        {
            var date = today.AddDays(-offset);
            var dayEnd = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            populations.Add((date, PopulationAt(runs, runListMayBeTruncated, populationSize, dayEnd), dayEnd));
        }

        var histories = new Dictionary<string, IReadOnlyList<ReconciliationRunSummary>>(StringComparer.Ordinal);
        foreach (var runId in populations
                     .SelectMany(static population => population.Runs ?? [])
                     .Select(static run => run.RunId)
                     .Distinct(StringComparer.Ordinal))
        {
            histories[runId] = await readHistory(runId, ct).ConfigureAwait(false);
        }

        var points = new List<WorkstationMetricHistoryPointDto>(days);
        foreach (var (date, population, dayEnd) in populations)
        {
            points.Add(new(date, population is null
                ? null
                : population.Count(run => HadTimingDriftAt(histories[run.RunId], dayEnd))));
        }

        points.Add(new(today, liveCount));
        return points;
    }

    /// <summary>
    /// The most recent runs that had started before <paramref name="moment"/>, or null when the list
    /// may be missing some of them.
    /// </summary>
    public static StrategyRunSummary[]? PopulationAt(
        IReadOnlyList<StrategyRunSummary> runs,
        bool runListMayBeTruncated,
        int populationSize,
        DateTimeOffset moment)
    {
        var started = runs.Where(run => run.StartedAt < moment).Take(populationSize).ToArray();
        // A truncated list drops the oldest runs; with fewer than a full population started by then,
        // an omitted older run could have belonged to it.
        return started.Length < populationSize && runListMayBeTruncated ? null : started;
    }

    /// <summary>Whether the run's latest reconciliation created before <paramref name="moment"/> reported timing drift.</summary>
    public static bool HadTimingDriftAt(IReadOnlyList<ReconciliationRunSummary> history, DateTimeOffset moment)
    {
        ReconciliationRunSummary? latest = null;
        foreach (var summary in history)
        {
            if (summary.CreatedAt < moment && (latest is null || summary.CreatedAt > latest.CreatedAt))
                latest = summary;
        }

        return latest?.HasTimingDrift == true;
    }
}
