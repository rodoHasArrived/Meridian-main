using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunStoreTests
{
    [Fact]
    public async Task GetRunByIdAsync_ReturnsRecordedRun()
    {
        var store = new StrategyRunStore();
        var expected = CreateRun("run-a", "strategy-1", RunType.Backtest, startedAt: new DateTimeOffset(2026, 4, 20, 14, 0, 0, TimeSpan.Zero));

        await store.RecordRunAsync(expected);

        var actual = await store.GetRunByIdAsync("run-a");

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task GetRunsByIdsAsync_ReturnsRequestedRunsInInputOrder()
    {
        var store = new StrategyRunStore();
        var runA = CreateRun("run-a", "strategy-1", RunType.Backtest, new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero));
        var runB = CreateRun("run-b", "strategy-1", RunType.Paper, new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero));
        var runC = CreateRun("run-c", "strategy-2", RunType.Live, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero), endedAt: null);

        await store.RecordRunAsync(runA);
        await store.RecordRunAsync(runB);
        await store.RecordRunAsync(runC);

        var results = await store.GetRunsByIdsAsync(["run-c", "missing", "run-a"]);

        results.Select(static run => run.RunId).Should().Equal("run-c", "run-a");
    }

    [Fact]
    public async Task QueryRunsAsync_FiltersAndOrdersByLastUpdatedDescending()
    {
        var store = new StrategyRunStore();
        var olderCompleted = CreateRun(
            "run-old",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));
        var newestCompleted = CreateRun(
            "run-new",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var runningPaper = CreateRun(
            "run-running",
            "strategy-1",
            RunType.Paper,
            new DateTimeOffset(2026, 4, 20, 13, 0, 0, TimeSpan.Zero),
            endedAt: null);

        await store.RecordRunAsync(olderCompleted);
        await store.RecordRunAsync(newestCompleted);
        await store.RecordRunAsync(runningPaper);

        var results = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            StrategyId: "strategy-1",
            RunTypes: [RunType.Backtest],
            Status: StrategyRunStatus.Completed,
            Limit: 10));

        results.Select(static run => run.RunId).Should().Equal("run-new", "run-old");
    }

    [Fact]
    public async Task RecordRunAsync_ReplacesExistingRunAcrossIndexes()
    {
        var store = new StrategyRunStore();
        var original = CreateRun(
            "run-replace",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));
        var updated = original with
        {
            EndedAt = new DateTimeOffset(2026, 4, 20, 11, 30, 0, TimeSpan.Zero),
            TerminalStatus = StrategyRunStatus.Failed
        };

        await store.RecordRunAsync(original);
        await store.RecordRunAsync(updated);

        var byId = await store.GetRunByIdAsync("run-replace");
        var byStrategy = new List<StrategyRunEntry>();
        await foreach (var run in store.GetRunsAsync("strategy-1"))
        {
            byStrategy.Add(run);
        }

        var queried = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            StrategyId: "strategy-1",
            Status: StrategyRunStatus.Failed,
            Limit: 10));

        byId.Should().Be(updated);
        byStrategy.Should().ContainSingle();
        byStrategy[0].Should().Be(updated);
        queried.Should().ContainSingle().Which.Should().Be(updated);
    }

    private static StrategyRunEntry CreateRun(
        string runId,
        string strategyId,
        RunType runType,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt = null,
        StrategyRunStatus? terminalStatus = null)
    {
        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyId,
            RunType: runType,
            StartedAt: startedAt,
            EndedAt: endedAt,
            Metrics: null,
            PortfolioId: $"{strategyId}-{runId}-portfolio",
            LedgerReference: $"{strategyId}-{runId}-ledger",
            AuditReference: $"{runId}-audit",
            Engine: runType.ToString(),
            TerminalStatus: terminalStatus);
    }
}
