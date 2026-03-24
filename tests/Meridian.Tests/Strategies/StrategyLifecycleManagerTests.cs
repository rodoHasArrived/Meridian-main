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
