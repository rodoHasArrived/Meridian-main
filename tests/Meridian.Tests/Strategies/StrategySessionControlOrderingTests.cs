using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed class StrategySessionControlOrderingTests
{
    [Fact]
    public async Task NaturalFeedCompletion_DuringRestoredResume_WaitsForInitializationBeforeCleanup()
    {
        var repository = new ReadGateRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new ProbeStrategy { BlockWarmup = true };
        var gateway = new Mock<IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        var context = new LiveStrategyExecutionContext(gateway.Object, new LiveMarketDataCache(),
            new PaperTradingPortfolio(100_000m), new HashSet<string> { "AAPL" });
        var restored = new RestoredPausedLiveStrategy(strategy, context);
        var run = StrategyRunEntry.Start(strategy.StrategyId, strategy.Name, RunType.Paper, "natural-finish-during-resume",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL" })
            .Paused("operator") with
        { TerminalStatus = StrategyRunStatus.Paused };
        await repository.RecordRunAsync(run);
        var feed = new CompletingFeed();
        var session = new LiveStrategyRunSession(run, restored, context, feed,
            Mock.Of<ExecutionSdk.IOrderManager>(), repository, auditTrail: null, NullLogger.Instance,
            useSynchronousFillFallback: false, fillReportQueueCapacity: 1);
        using var controlGate = new SemaphoreSlim(1, 1);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.SetLifecycleControlGate(controlGate);
        session.SetReportAdmissionCloser(() =>
        {
            closing.TrySetResult();
            session.CompleteExecutionReportAdmission();
        });
        manager.RegisterSession(restored, run,
            _ => throw new InvalidOperationException("This test ends the feed without an operator stop."),
            controlGate, canControl: () => !closing.Task.IsCompleted);
        // No reports are admitted in this case. Completing the inbox before execution
        // leaves no cancellation wait that could obscure the feed-completion boundary.
        session.CompleteExecutionReportAdmission();
        var execution = session.ExecuteAsync(CancellationToken.None);
        await feed.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var resume = manager.StartAsync(strategy.StrategyId, context, RunType.Paper);
        await strategy.WarmupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            feed.Complete();
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            strategy.Callbacks.Should().BeEmpty();
        }
        finally
        {
            strategy.ReleaseWarmup.TrySetResult();
        }

        (await resume.WaitAsync(TimeSpan.FromSeconds(10))).State.Should().Be(OperationTerminalState.Succeeded);
        (await execution.WaitAsync(TimeSpan.FromSeconds(10)))!.State.Should().Be(OperationTerminalState.Succeeded);
        strategy.Callbacks.Should().Equal("initialize", "finished", "stop");
        strategy.StartCalls.Should().Be(1);
        Func<Task> resumeAgain = () => manager.StartAsync(strategy.StrategyId, context, RunType.Paper);
        await resumeAgain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer accepts*");
    }

    [Fact]
    public async Task ManagerStartAfterHostStopBarrier_CannotActivatePausedSessionDuringCleanup()
    {
        var repository = new ReadGateRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new ProbeStrategy();
        var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
        var feed = new IdleFeed { BlockCancellation = true };
        var gateway = new Mock<IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        await using var engine = new LiveTradingEngine(catalog, feed, new LiveMarketDataCache(),
            gateway.Object, Mock.Of<ExecutionSdk.IOrderManager>(), new PaperTradingPortfolio(100_000m),
            repository, lifecycleManager: manager);
        var run = StrategyRunEntry.Start(strategy.StrategyId, strategy.Name, RunType.Paper, "retiring-paused-run",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL" })
            .Paused("operator") with
        { TerminalStatus = StrategyRunStatus.Paused };
        await repository.RecordRunAsync(run);
        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
        await feed.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var shutdown = engine.DisposeAsync().AsTask();
        try
        {
            await feed.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Func<Task> resume = () => manager.StartAsync(strategy.StrategyId, Mock.Of<IExecutionContext>(), RunType.Paper);
            await resume.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer accepts*");
            strategy.StartCalls.Should().Be(0);
        }
        finally
        {
            feed.ReleaseCancellation.TrySetResult();
        }
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
        var retained = await repository.GetRunByIdAsync(run.RunId);
        retained!.TerminalStatus.Should().Be(StrategyRunStatus.Paused);
        retained.EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task QueuedManagerStopAndEngineResume_CompleteWithoutOppositeGateOrder()
    {
        var repository = new ReadGateRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new ProbeStrategy();
        var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
        var feed = new IdleFeed();
        var gateway = new Mock<IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        await using var engine = new LiveTradingEngine(catalog, feed, new LiveMarketDataCache(),
            gateway.Object, Mock.Of<ExecutionSdk.IOrderManager>(), new PaperTradingPortfolio(100_000m),
            repository, lifecycleManager: manager);
        var run = StrategyRunEntry.Start(strategy.StrategyId, strategy.Name, RunType.Paper, "queued-paused-run",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL" })
            .Paused("operator") with
        { TerminalStatus = StrategyRunStatus.Paused };
        await repository.RecordRunAsync(run);
        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
        await feed.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var resumeCancellation = new CancellationTokenSource();

        repository.GateNextRead();
        var reconcile = manager.ReconcileExternalStateAsync(strategy.StrategyId);
        await repository.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stop = manager.StopAsync(strategy.StrategyId);
        var resume = engine.ResumeRunWithOutcomeAsync(run.RunId, resumeCancellation.Token);
        try
        {
            // Reconciliation owns the strategy command gate. Stop queued first; resume
            // must not hold the run gate while waiting behind that stop command.
            repository.ReleaseRead.TrySetResult();
            (await reconcile.WaitAsync(TimeSpan.FromSeconds(10))).State.Should().Be(OperationTerminalState.Succeeded);
            (await stop.WaitAsync(TimeSpan.FromSeconds(10))).State.Should().Be(OperationTerminalState.Succeeded);
            (await resume.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeNull();
        }
        finally
        {
            repository.ReleaseRead.TrySetResult();
            resumeCancellation.Cancel();
        }

        var retained = await repository.GetRunByIdAsync(run.RunId);
        retained!.TerminalStatus.Should().Be(StrategyRunStatus.Stopped);
        retained.Metrics.Should().NotBeNull();
        strategy.StartCalls.Should().Be(0, "the earlier stop wins without activating a restored paused strategy");
        engine.ActiveRunIds.Should().BeEmpty();
    }

    private sealed class ProbeStrategy : LiveStrategyBase
    {
        public override string Name => "Control ordering";
        public override string StrategyId => "control-ordering";
        public int StartCalls { get; private set; }
        public bool BlockWarmup { get; init; }
        public TaskCompletionSource WarmupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWarmup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Callbacks { get; } = new();

        protected override async Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct)
        {
            StartCalls++;
            if (BlockWarmup)
            {
                WarmupEntered.TrySetResult();
                await ReleaseWarmup.Task.WaitAsync(ct);
            }
        }

        public override void Initialize(IBacktestContext ctx) => Callbacks.Enqueue("initialize");

        public override void OnFinished(IBacktestContext ctx) => Callbacks.Enqueue("finished");

        protected override Task OnStoppedAsync(CancellationToken ct)
        {
            Callbacks.Enqueue("stop");
            return Task.CompletedTask;
        }
    }

    private sealed class CompletingFeed : ILiveMarketEventFeed, IAsyncEnumerable<LiveMarketEvent>, IAsyncEnumerator<LiveMarketEvent>
    {
        private readonly TaskCompletionSource<bool> _completed = new();
        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LiveMarketEvent Current => throw new InvalidOperationException("The feed completes without events.");
        public IAsyncEnumerator<LiveMarketEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            Subscribed.TrySetResult();
            return new ValueTask<bool>(_completed.Task);
        }

        public void Complete() => _completed.TrySetResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class IdleFeed : ILiveMarketEventFeed
    {
        public bool BlockCancellation { get; init; }
        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(IReadOnlyCollection<string> symbols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var events = Channel.CreateUnbounded<LiveMarketEvent>();
            Subscribed.TrySetResult();
            try
            {
                await foreach (var item in events.Reader.ReadAllAsync(ct))
                    yield return item;
            }
            finally
            {
                if (BlockCancellation)
                {
                    CancellationObserved.TrySetResult();
                    await ReleaseCancellation.Task;
                }
            }
        }
    }

    private sealed class ReadGateRepository : IStrategyRepository
    {
        private readonly StrategyRunStore _inner = new();
        private int _gateNextRead;
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void GateNextRead() => Interlocked.Exchange(ref _gateNextRead, 1);

        public async Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _gateNextRead, 0) != 0)
            {
                ReadEntered.TrySetResult();
                await ReleaseRead.Task.WaitAsync(ct);
            }
            return await _inner.GetRunByIdAsync(runId, ct);
        }

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) => _inner.RecordRunAsync(entry, ct);

        public Task RecordLifecycleEventAsync(StrategyRunEntry entry, StrategyRunLifecycleEventType eventType,
            CancellationToken ct = default) => _inner.RecordLifecycleEventAsync(entry, eventType, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default) =>
            _inner.GetRunsAsync(strategyId, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) => _inner.GetAllRunsAsync(ct);

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            _inner.GetLatestRunAsync(strategyId, ct);
    }
}
