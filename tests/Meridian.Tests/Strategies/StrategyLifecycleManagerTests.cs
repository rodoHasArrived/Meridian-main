using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecycleManagerTests
{
    [Fact]
    public async Task PauseAsync_WhenStrategyIsRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var action = () => manager.PauseAsync("strategy-1");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_WhenStrategyAlreadyRunning_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Running));

        var action = () => manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_WhenStrategyIsRegistered_RecordsRun()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        repository.RecordedRuns.Should().HaveCount(1);
        repository.RecordedRuns[0].StrategyId.Should().Be("strategy-1");
    }

    // ------------------------------------------------------------------ //
    // StopAsync                                                            //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StopAsync_WhenStrategyIsRunning_RecordsCompletedRun()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        await manager.StopAsync("strategy-1");

        // One for Start, one for Stop (completion)
        repository.RecordedRuns.Should().HaveCount(2);
        repository.RecordedRuns[^1].StrategyId.Should().Be("strategy-1");
        repository.RecordedRuns[^1].EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StopAsync_WhenStrategyNotRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.StopAsync("unknown-strategy");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StopAsync_WhenStrategyIsRegisteredButNotStarted_ThrowsInvalidOperationException()
    {
        // A registered-but-not-started strategy cannot be stopped (invalid lifecycle transition)
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var action = () => manager.StopAsync("strategy-1");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    // ------------------------------------------------------------------ //
    // PauseAsync                                                           //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PauseAsync_WhenStrategyIsRunning_DoesNotThrow()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var action = () => manager.PauseAsync("strategy-1");

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PauseAsync_WhenStrategyNotRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.PauseAsync("unknown-strategy");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    // ------------------------------------------------------------------ //
    // GetStatuses                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public void GetStatuses_WhenNoStrategiesRegistered_ReturnsEmptyDictionary()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var statuses = manager.GetStatuses();

        statuses.Should().BeEmpty();
    }

    [Fact]
    public void GetStatuses_WithMultipleRegisteredStrategies_ReturnsAllStatuses()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-a", StrategyStatus.Registered));
        manager.Register(new StubLiveStrategy("strategy-b", StrategyStatus.Registered));

        var statuses = manager.GetStatuses();

        statuses.Should().HaveCount(2);
        statuses.Should().ContainKey("strategy-a");
        statuses.Should().ContainKey("strategy-b");
        statuses["strategy-a"].Should().Be(StrategyStatus.Registered);
        statuses["strategy-b"].Should().Be(StrategyStatus.Registered);
    }

    [Fact]
    public async Task GetStatuses_AfterStart_ReflectsRunningStatus()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var statuses = manager.GetStatuses();
        statuses["strategy-1"].Should().Be(StrategyStatus.Running);
    }

    // ------------------------------------------------------------------ //
    // Register                                                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StartAsync_WhenStrategyNotRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.StartAsync("unknown-strategy", new StubExecutionContext(), RunType.Paper);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Register_WhenCalledTwiceForSameId_ReplacesExistingEntry()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var statuses = manager.GetStatuses();

        // Re-registering keeps only one entry under the same ID
        statuses.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------ //
    // DisposeAsync                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DisposeAsync_WhenRunningStrategyExists_StopsItGracefully()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new StubLiveStrategy("strategy-1", StrategyStatus.Registered);
        manager.Register(strategy);
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        // Should not throw even when disposing with a running strategy
        var action = () => manager.DisposeAsync().AsTask();

        await action.Should().NotThrowAsync();
        strategy.Status.Should().Be(StrategyStatus.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_WhenNoStrategiesRegistered_CompletesWithoutError()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.DisposeAsync().AsTask();

        await action.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------ //
    // Helpers                                                               //
    // ------------------------------------------------------------------ //

    private sealed class InMemoryStrategyRepository : IStrategyRepository
    {
        public List<StrategyRunEntry> RecordedRuns { get; } = [];

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
        {
            RecordedRuns.Add(entry);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var run in RecordedRuns.Where(run => run.StrategyId == strategyId))
                yield return run;

            await Task.CompletedTask;
        }

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            Task.FromResult(RecordedRuns.LastOrDefault(run => run.StrategyId == strategyId));
    }

    private sealed class StubLiveStrategy(string strategyId, StrategyStatus initialStatus) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = initialStatus;

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            Status = StrategyStatus.Paused;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Status = StrategyStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class StubExecutionContext : IExecutionContext
    {
        public IOrderGateway Gateway => throw new NotImplementedException();
        public ILiveFeedAdapter Feed => throw new NotImplementedException();
        public IPortfolioState Portfolio => throw new NotImplementedException();
        public IReadOnlySet<string> Universe { get; } = new HashSet<string>();
        public DateTimeOffset CurrentTime => DateTimeOffset.UtcNow;
        public Meridian.Ledger.IReadOnlyLedger? Ledger => null;
    }
}
