using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class TimingDriftHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RebuildsEachDayEndFromTheLatestReconciliationThenAndEndsOnTheLiveCount()
    {
        // Newest start first, as the workspace lists them.
        var runs = new[] { Run("r2", Now.AddDays(-3)), Run("r1", Now.AddDays(-6)) };
        var reconciliations = new Dictionary<string, ReconciliationRunSummary[]>
        {
            // r1 drifts from Nov 12 15:00 until a clean reconciliation on Nov 15 15:00.
            ["r1"] = [Recon("r1", Now.AddDays(-5), drift: true), Recon("r1", Now.AddDays(-2), drift: false)],
            // r2 drifts from Nov 15 15:00.
            ["r2"] = [Recon("r2", Now.AddDays(-2), drift: true)]
        };

        var history = await Build(runs, reconciliations, truncated: false, populationSize: 6, liveCount: 1);

        Assert.Equal(7, history.Count);
        Assert.Equal(new DateOnly(2026, 11, 11), history[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 17), history[^1].Date);
        //   Nov 11: r1 started, not reconciled · Nov 12–14: r1 · Nov 15–16: r2 · today: live
        Assert.Equal(new decimal?[] { 0, 1, 1, 1, 1, 1, 1 }, history.Select(point => point.Value).ToArray());
    }

    [Fact]
    public async Task CountsOnlyTheMostRecentRunsThatHadStartedByEachDayEnd()
    {
        // With a population of one, a newer run displaces an older drifting run from the count.
        var runs = new[] { Run("new", Now.AddDays(-2)), Run("old", Now.AddDays(-10)) };
        var reconciliations = new Dictionary<string, ReconciliationRunSummary[]>
        {
            ["old"] = [Recon("old", Now.AddDays(-9), drift: true)],
            ["new"] = []
        };

        var history = await Build(runs, reconciliations, truncated: false, populationSize: 1, liveCount: 0);

        // Nov 11–15: "old" is the most recent run and drifts; from Nov 15 15:00 "new" replaces it.
        Assert.Equal(new decimal?[] { 1, 1, 1, 1, 0, 0, 0 }, history.Select(point => point.Value).ToArray());
    }

    [Fact]
    public async Task LeavesDaysBlankWhenATruncatedRunListCannotEstablishThePopulation()
    {
        var runs = new[] { Run("a", Now.AddDays(-1)), Run("b", Now.AddDays(-4)) };
        var reconciliations = new Dictionary<string, ReconciliationRunSummary[]>
        {
            ["a"] = [Recon("a", Now.AddHours(-20), drift: true)],
            ["b"] = [Recon("b", Now.AddDays(-4).AddHours(1), drift: true)]
        };

        var history = await Build(runs, reconciliations, truncated: true, populationSize: 2, liveCount: 2);

        // Only from Nov 16 15:00 have two listed runs started; before that an omitted older run
        // could have belonged to the population, so those days are unknown, not zero.
        Assert.Equal(new decimal?[] { null, null, null, null, null, 2, 2 }, history.Select(point => point.Value).ToArray());
    }

    [Fact]
    public async Task ReadsEachRunsHistoryOnceAndOnlyForRunsInSomeDaysPopulation()
    {
        var runs = new[] { Run("a", Now.AddHours(-2)), Run("b", Now.AddDays(-2)), Run("c", Now.AddDays(-30)) };
        var reads = new List<string>();

        await TimingDriftHistory.BuildAsync(runs, runListMayBeTruncated: false, populationSize: 1, liveCount: 0,
            (runId, _) =>
            {
                reads.Add(runId);
                return Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([]);
            },
            Now, CancellationToken.None, days: 7);

        // "c" is the most recent run until "b" starts; "a" started today, whose point is the live count.
        Assert.Equal(new[] { "b", "c" }, reads.Order(StringComparer.Ordinal).ToArray());
    }

    private static Task<IReadOnlyList<WorkstationMetricHistoryPointDto>> Build(
        StrategyRunSummary[] runs,
        IReadOnlyDictionary<string, ReconciliationRunSummary[]> reconciliations,
        bool truncated,
        int populationSize,
        int liveCount)
        => TimingDriftHistory.BuildAsync(runs, truncated, populationSize, liveCount,
            (runId, _) => Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>(reconciliations[runId]),
            Now, CancellationToken.None, days: 7);

    private static StrategyRunSummary Run(string runId, DateTimeOffset startedAt)
        => new(runId, "strategy", "Strategy", StrategyRunMode.Paper, StrategyRunEngine.MeridianNative, StrategyRunStatus.Running,
            startedAt, null, null, null, null, null, null, null, null, 0, startedAt);

    private static ReconciliationRunSummary Recon(string runId, DateTimeOffset createdAt, bool drift)
        => new($"{runId}-{createdAt:O}", runId, createdAt, createdAt, createdAt, 0, 0, 0, drift, 0m, 0);
}
