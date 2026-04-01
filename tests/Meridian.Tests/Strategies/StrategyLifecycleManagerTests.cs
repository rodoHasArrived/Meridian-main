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
    // Concurrency                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StartAsync_ConcurrentCallsForDifferentStrategies_AllComplete()
    {
        // Registering and starting many distinct strategies concurrently should not corrupt
        // internal state — each strategy should end up in Running state with a recorded run.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        const int count = 20;
        for (var i = 0; i < count; i++)
            manager.Register(new StubLiveStrategy($"strategy-{i}", StrategyStatus.Registered));

        var tasks = Enumerable
            .Range(0, count)
            .Select(i => manager.StartAsync($"strategy-{i}", new StubExecutionContext(), RunType.Paper))
            .ToArray();

        await Task.WhenAll(tasks);

        var statuses = manager.GetStatuses();
        statuses.Should().HaveCount(count);
        statuses.Values.Should().AllSatisfy(s => s.Should().Be(StrategyStatus.Running));
        repository.RecordedRuns.Should().HaveCount(count);
    }

    [Fact]
    public async Task Register_ConcurrentRegistrations_KeepsLatestEntry()
    {
        // Registering the same strategy ID from many threads concurrently should not throw;
        // the final registered entry should be present.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        const string strategyId = "shared-strategy";
        var tasks = Enumerable
            .Range(0, 50)
            .Select(_ => Task.Run(() => manager.Register(new StubLiveStrategy(strategyId, StrategyStatus.Registered))))
            .ToArray();

        var act = () => Task.WhenAll(tasks);

        await act.Should().NotThrowAsync("concurrent registrations for the same ID must not corrupt state");

        var statuses = manager.GetStatuses();
        statuses.Should().ContainKey(strategyId);
    }

    [Fact]
    public async Task GetStatuses_CalledConcurrentlyWithRegistrations_DoesNotThrow()
    {
        // GetStatuses must not throw when called concurrently while strategies are being added.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var registerTasks = Enumerable
            .Range(0, 30)
            .Select(i => Task.Run(() => manager.Register(new StubLiveStrategy($"s-{i}", StrategyStatus.Registered))))
            .ToArray();
        var readTasks = Enumerable
            .Range(0, 30)
            .Select(_ => Task.Run(() => manager.GetStatuses()))
            .ToArray();

        var act = () => Task.WhenAll(registerTasks.Concat(readTasks));

        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------ //
    // Error paths                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StartAsync_WhenRepositoryThrows_PropagatesException()
    {
        // When the repository fails to persist the run, StartAsync should surface the error.
        var repository = new FailingStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var action = () => manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*repository*");
    }

    [Fact]
    public async Task DisposeAsync_WhenOneStrategyStopThrows_ContinuesAndDisposesRemainder()
    {
        // DisposeAsync must swallow individual strategy errors and keep draining.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var throwingStrategy = new ThrowingOnStopStrategy("strategy-throws");
        var normalStrategy = new StubLiveStrategy("strategy-ok", StrategyStatus.Registered);

        manager.Register(throwingStrategy);
        manager.Register(normalStrategy);
        await manager.StartAsync("strategy-throws", new StubExecutionContext(), RunType.Paper);
        await manager.StartAsync("strategy-ok", new StubExecutionContext(), RunType.Paper);

        // Must not throw even though one strategy's StopAsync will throw.
        var action = () => manager.DisposeAsync().AsTask();

        await action.Should().NotThrowAsync();
        normalStrategy.Status.Should().Be(StrategyStatus.Stopped);
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

    /// <summary>Repository that always throws on <see cref="RecordRunAsync"/>.</summary>
    private sealed class FailingStrategyRepository : IStrategyRepository
    {
        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated repository failure.");

#pragma warning disable CS1998 // async method body has no awaits
        public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
            string strategyId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
#pragma warning restore CS1998

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            Task.FromResult<StrategyRunEntry?>(null);
    }

    /// <summary>Strategy whose <see cref="StopAsync"/> always throws.</summary>
    private sealed class ThrowingOnStopStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;

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

        public Task StopAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated stop failure.");

        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }
}
