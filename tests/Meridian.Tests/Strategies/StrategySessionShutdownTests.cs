using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Live;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Storage.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed class StrategySessionShutdownTests
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(10);
    private const string StrategyId = "coordinated-session-stop";
    private const string SessionActorId = "live-trading-engine";

    [Fact]
    public async Task OperatorStop_IdleSession_WakesAndRetainsMetricsBeforeReturningReceipt()
    {
        await using var fixture = new SessionFixture();
        var run = fixture.CreateRun();
        await fixture.LaunchAsync(run);

        var outcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);

        AssertSuccess(outcome);
        var retained = await fixture.ReloadRunAsync(run.RunId);
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        retained.EndedAt.Should().NotBeNull();
        retained.Metrics.Should().NotBeNull("a paper run needs retained metrics for promotion even when its feed is idle");
        retained.Metrics!.Metrics.TotalTrades.Should().Be(0);
        fixture.Engine.ActiveRunIds.Should().NotContain(run.RunId);
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Select(record => record.EventType).Should().ContainInOrder("StopRequested", "Completed");
        history.Should().ContainSingle(record => record.EventType == "StopRequested")
            .Which.ActorId.Should().Be(SessionActorId, "the manager delegates stop intent to the owning session");
        history.Last().TerminalOutcome.Should().BeEquivalentTo(outcome);
    }

    [Fact]
    public async Task PausedSession_StartResumesSameRun_AndStopFinalizesItsMetricsAsCompleted()
    {
        var strategy = new GatedStrategy();
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        await fixture.LaunchAsync(run);
        var warmedContext = strategy.CurrentExecutionContext;

        AssertSuccess(await fixture.Manager.PauseAsync(StrategyId).WaitAsync(WaitBudget));
        var paused = await fixture.ReloadRunAsync(run.RunId);
        StrategyRunRepositoryOrdering.MapStatus(paused!).Should().Be(StrategyRunStatus.Paused);

        var resumeContext = new Moq.Mock<IExecutionContext>(Moq.MockBehavior.Strict);
        var resumedOutcome = await fixture.Manager.StartAsync(StrategyId, resumeContext.Object, RunType.Paper)
            .WaitAsync(WaitBudget);

        AssertSuccess(resumedOutcome);
        var resumed = await fixture.ReloadRunAsync(run.RunId);
        resumed!.RunId.Should().Be(run.RunId);
        resumed.EndedAt.Should().BeNull();
        resumed.TerminalStatus.Should().BeNull();
        StrategyRunRepositoryOrdering.MapStatus(resumed).Should().Be(StrategyRunStatus.Running);
        fixture.Manager.GetStatuses()[StrategyId].Should().Be(StrategyStatus.Running);
        fixture.Engine.ActiveRunIds.Should().Equal(run.RunId);
        strategy.CurrentExecutionContext.Should().BeSameAs(warmedContext);
        strategy.StartCount.Should().Be(1, "resuming must reuse the warmed strategy and its execution context");
        var runs = await fixture.Store.QueryRunsAsync(new StrategyRunRepositoryQuery(StrategyId: StrategyId));
        runs.Should().ContainSingle().Which.RunId.Should().Be(run.RunId);

        AssertSuccess(await fixture.Manager.PauseAsync(StrategyId).WaitAsync(WaitBudget));
        var stoppedOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);

        AssertSuccess(stoppedOutcome);
        var stopped = await fixture.ReloadRunAsync(run.RunId);
        stopped!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        stopped.EndedAt.Should().NotBeNull();
        stopped.Metrics.Should().NotBeNull();
        StrategyRunRepositoryOrdering.MapStatus(stopped).Should().Be(StrategyRunStatus.Stopped);
        fixture.Manager.GetStatuses()[StrategyId].Should().Be(StrategyStatus.Stopped);
        strategy.FinishedCount.Should().Be(1);
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Select(record => record.EventType).Should().ContainInOrder(
            "Paused", "StartRequested", "Resumed", "Paused", "StopRequested", "Completed");
        history.Should().NotContain(record => record.EventType == "RecoveryAttempted");
    }

    [Fact]
    public async Task ConcurrentOperatorAndRunStops_ShareCleanupAndRetainedCompletion()
    {
        var strategy = new GatedStrategy { BlockStop = true };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        await fixture.LaunchAsync(run);

        var firstStop = fixture.Manager.StopAsync(StrategyId);
        await strategy.StopEntered.Task.WaitAsync(WaitBudget);
        var runStop = fixture.Engine.StopRunWithOutcomeAsync(run.RunId);
        var repeatedStop = fixture.Manager.StopAsync(StrategyId);
        firstStop.IsCompleted.Should().BeFalse();
        runStop.IsCompleted.Should().BeFalse();
        repeatedStop.IsCompleted.Should().BeFalse();

        strategy.ReleaseStop.TrySetResult();
        var firstOutcome = await firstStop.WaitAsync(WaitBudget);
        var runOutcome = await runStop.WaitAsync(WaitBudget);
        var repeatedOutcome = await repeatedStop.WaitAsync(WaitBudget);

        AssertSuccess(firstOutcome);
        runOutcome.Should().BeEquivalentTo(firstOutcome);
        repeatedOutcome.Should().BeEquivalentTo(firstOutcome);
        strategy.StopCount.Should().Be(1);
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Should().ContainSingle(record => record.EventType == "StopRequested");
        history.Should().ContainSingle(record => record.EventType == "Completed");
        (await fixture.ReloadRunAsync(run.RunId))!.Metrics.Should().NotBeNull();
    }

    [Fact]
    public async Task OperatorStop_WhenNaturalFinishIsRetainingStopIntent_JoinsTheSessionWriter()
    {
        var strategy = new GatedStrategy { BlockStop = true };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        var subscription = await fixture.LaunchAsync(run);
        var intent = fixture.Repository.GateNextStopIntent(SessionActorId);

        subscription.TryComplete().Should().BeTrue();
        await intent.Entered.Task.WaitAsync(WaitBudget);
        var operatorStop = fixture.Manager.StopAsync(StrategyId);
        operatorStop.IsCompleted.Should().BeFalse();
        intent.Release.TrySetResult();
        await strategy.StopEntered.Task.WaitAsync(WaitBudget);
        var stopping = await fixture.ReloadRunAsync(run.RunId);
        stopping!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StopRequested);
        stopping.ActorId.Should().Be(SessionActorId);
        stopping.EndedAt.Should().BeNull();

        operatorStop.IsCompleted.Should().BeFalse();
        strategy.ReleaseStop.TrySetResult();
        var outcome = await operatorStop.WaitAsync(WaitBudget);

        AssertSuccess(outcome);
        await AssertSingleSuccessfulCleanupAsync(fixture, run, strategy);
    }

    [Fact]
    public async Task NaturalFinish_WhenOperatorStopIsRetainingIntent_UsesTheSameSessionWriter()
    {
        var strategy = new GatedStrategy { BlockStop = true };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        var subscription = await fixture.LaunchAsync(run);
        var intent = fixture.Repository.GateNextStopIntent(SessionActorId);

        var operatorStop = fixture.Manager.StopAsync(StrategyId);
        await intent.Entered.Task.WaitAsync(WaitBudget);
        subscription.TryComplete().Should().BeTrue();
        var runStop = fixture.Engine.StopRunWithOutcomeAsync(run.RunId);
        operatorStop.IsCompleted.Should().BeFalse();
        runStop.IsCompleted.Should().BeFalse();
        intent.Release.TrySetResult();
        await strategy.StopEntered.Task.WaitAsync(WaitBudget);
        var stopping = await fixture.ReloadRunAsync(run.RunId);
        stopping!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StopRequested);
        stopping.ActorId.Should().Be(SessionActorId);
        stopping.EndedAt.Should().BeNull();

        strategy.ReleaseStop.TrySetResult();
        var outcome = await operatorStop.WaitAsync(WaitBudget);

        AssertSuccess(outcome);
        (await runStop.WaitAsync(WaitBudget)).Should().BeEquivalentTo(outcome);
        await AssertSingleSuccessfulCleanupAsync(fixture, run, strategy);
    }

    [Fact]
    public async Task OperatorStop_CancelledWait_LeavesCleanupRunningAndRetainsCompletion()
    {
        var strategy = new GatedStrategy { BlockStop = true };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        await fixture.LaunchAsync(run);
        using var cancellation = new CancellationTokenSource();

        var stop = fixture.Manager.StopAsync(StrategyId, cancellation.Token);
        await strategy.StopEntered.Task.WaitAsync(WaitBudget);
        cancellation.Cancel();

        var awaitCancelledStop = async () => await stop.WaitAsync(WaitBudget);
        await awaitCancelledStop.Should().ThrowAsync<OperationCanceledException>();
        fixture.Engine.ActiveRunIds.Should().Contain(run.RunId);
        var stopping = await fixture.ReloadRunAsync(run.RunId);
        stopping!.EndedAt.Should().BeNull();

        var completion = fixture.Engine.StopRunWithOutcomeAsync(run.RunId);
        completion.IsCompleted.Should().BeFalse();
        strategy.ReleaseStop.TrySetResult();
        var outcome = await completion.WaitAsync(WaitBudget);

        outcome.Should().NotBeNull();
        AssertSuccess(outcome!);
        (await fixture.ReloadRunAsync(run.RunId))!.Metrics.Should().NotBeNull();
        strategy.StopCount.Should().Be(1);
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Should().ContainSingle(record => record.EventType == "Completed");
    }

    [Fact]
    public async Task OperatorStop_FillAlreadyAdmitted_WaitsAndIncludesItInRetainedMetrics()
    {
        var strategy = new GatedStrategy { BlockFill = true };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun(quantity: 3);
        var subscription = await fixture.LaunchAsync(run);
        fixture.PublishTrade(subscription);
        await strategy.FillEntered.Task.WaitAsync(WaitBudget);

        var stop = fixture.Manager.StopAsync(StrategyId);
        var runStop = fixture.Engine.StopRunWithOutcomeAsync(run.RunId);
        stop.IsCompleted.Should().BeFalse("the admitted fill callback must finish before final metrics are saved");
        runStop.IsCompleted.Should().BeFalse();
        fixture.History.StopRequested.Task.IsCompleted.Should().BeFalse(
            "the session drains admitted fills before retaining its stop intent and invoking cleanup");

        strategy.ReleaseFill.Set();
        var outcome = await stop.WaitAsync(WaitBudget);

        AssertSuccess(outcome);
        (await runStop.WaitAsync(WaitBudget)).Should().BeEquivalentTo(outcome);
        var retained = await fixture.ReloadRunAsync(run.RunId);
        retained!.Metrics!.Metrics.TotalTrades.Should().Be(1);
        retained.Metrics.Fills.Should().ContainSingle(fill => fill.Symbol == "AAPL" && fill.FilledQuantity == 3);
        strategy.StopCount.Should().Be(1);
        fixture.Orders.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperatorStop_AfterCleanupFailure_RetriesOnlyCleanupAndPreservesFailedOutcome(bool failFinished)
    {
        var strategy = new GatedStrategy { StopFailuresRemaining = 1, FailFinished = failFinished };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        await fixture.LaunchAsync(run);

        var failedOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);

        failedOutcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(failedOutcome).Should().BeEmpty();
        var failed = await fixture.ReloadRunAsync(run.RunId);
        failed!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
        failed.ExceptionMessage.Should().Contain("cleanup failed once");
        if (failFinished)
        {
            failed.ExceptionMessage.Should().Contain("finished callback failed");
        }

        failed.Metrics.Should().NotBeNull();
        fixture.Engine.ActiveRunIds.Should().NotContain(run.RunId);

        var recoveredOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);

        recoveredOutcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(recoveredOutcome).Should().BeEmpty();
        var recovered = await fixture.ReloadRunAsync(run.RunId);
        recovered!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
        recovered.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
        recovered.ExceptionMessage.Should().Be(failed.ExceptionMessage);
        recovered.ExceptionType.Should().Be(failed.ExceptionType);
        recovered.EndedAt.Should().Be(failed.EndedAt);
        recovered.Metrics.Should().BeEquivalentTo(failed.Metrics);
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.StopCount.Should().Be(2);
        strategy.FinishedCount.Should().Be(1, "cleanup retry must not rerun session finalization");
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Select(record => record.EventType).Should().ContainInOrder(
            "StopRequested", "Failed", "StopRequested", "Failed");
        history.Count(record => record.EventType == "Failed").Should().Be(2);
        history.Should().NotContain(record => record.EventType == "Completed");
    }

    [Fact]
    public async Task OperatorStop_AfterCancelledCleanupRetry_PreservesSessionFailureThroughSuccessfulCleanup()
    {
        var strategy = new GatedStrategy { StopFailuresRemaining = 1, CancelStopsRemaining = 1 };
        await using var fixture = new SessionFixture(_ => strategy);
        var run = fixture.CreateRun();
        await fixture.LaunchAsync(run);

        var originalOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);
        var originalFailure = await fixture.ReloadRunAsync(run.RunId);
        var cancelledRetryOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);
        var cancelledRetry = await fixture.ReloadRunAsync(run.RunId);
        var completedCleanupOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);
        var final = await fixture.ReloadRunAsync(run.RunId);

        foreach (var outcome in new[] { originalOutcome, cancelledRetryOutcome, completedCleanupOutcome })
        {
            outcome.State.Should().Be(OperationTerminalState.Failed);
            VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        }

        cancelledRetry!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StopFailed);
        cancelledRetry.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
        cancelledRetry.ExceptionMessage.Should().Contain("cleanup failed once").And.Contain("cleanup retry cancelled");
        final!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
        final.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
        final.ExceptionMessage.Should().Be(cancelledRetry.ExceptionMessage);
        final.EndedAt.Should().Be(originalFailure!.EndedAt);
        final.Metrics.Should().BeEquivalentTo(originalFailure.Metrics);
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.StopCount.Should().Be(3);
        strategy.FinishedCount.Should().Be(1);
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Select(record => record.EventType).Should().ContainInOrder(
            "Failed", "StopRequested", "StopFailed", "StopRequested", "Failed");
        history.Should().NotContain(record => record.EventType == "Completed" || record.EventType == "Cancelled");
    }

    [Fact]
    public async Task Register_WhileSessionBoundButStillRegistered_RejectsReplacement()
    {
        await using var fixture = new SessionFixture();
        var run = fixture.CreateRun();
        await fixture.Store.RecordRunAsync(run);
        var bound = new GatedStrategy();
        fixture.Manager.RegisterSession(bound, run,
            _ => throw new InvalidOperationException("The test does not request shutdown."));
        bound.Status.Should().Be(StrategyStatus.Registered);

        var replace = () => fixture.Manager.Register(new GatedStrategy());

        replace.Should().Throw<InvalidOperationException>().WithMessage("*already has an active instance*");
        fixture.Manager.GetStatuses()[StrategyId].Should().Be(StrategyStatus.Registered);
    }

    [Theory]
    [InlineData(RunType.Paper)]
    [InlineData(RunType.Live)]
    public async Task SameStrategySecondRun_IsDeferredWithoutReplacingControls_ThenCanLaunchAfterStop(RunType secondMode)
    {
        await using var fixture = new SessionFixture();
        var first = fixture.CreateRun(quantity: 1);
        var second = fixture.CreateRun(quantity: 7, mode: secondMode);
        await fixture.LaunchAsync(first);
        await fixture.Store.RecordRunAsync(second);

        var deferred = await fixture.Engine.TryLaunchAsync(second);

        deferred.Launched.Should().BeFalse();
        deferred.Reason.Should().Contain("active run");
        fixture.Engine.ActiveRunIds.Should().Equal(first.RunId);
        fixture.Strategies.Should().ContainSingle("a deferred launch must not replace the controlled instance");
        var firstOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);
        AssertSuccess(firstOutcome);
        (await fixture.ReloadRunAsync(first.RunId))!.Metrics.Should().NotBeNull();
        var retainedSecond = await fixture.ReloadRunAsync(second.RunId);
        retainedSecond!.EndedAt.Should().BeNull();
        retainedSecond.Metrics.Should().BeNull();
        retainedSecond.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.ActivationDeferred);

        var launch = await fixture.Engine.TryLaunchAsync(retainedSecond);
        launch.Launched.Should().BeTrue(launch.Reason);
        await fixture.Feed.NextSubscriptionAsync();
        fixture.Strategies.Should().HaveCount(2);
        var secondOutcome = await fixture.Manager.StopAsync(StrategyId).WaitAsync(WaitBudget);
        AssertSuccess(secondOutcome);
        var completedSecond = await fixture.ReloadRunAsync(second.RunId);
        completedSecond!.Metrics.Should().NotBeNull();
        completedSecond.ParameterSet!["quantity"].Should().Be("7");
        completedSecond.RunType.Should().Be(secondMode);
        completedSecond.FundAccountId.Should().Be(second.FundAccountId);
        completedSecond.InputHashSha256.Should().Be(second.InputHashSha256);
        completedSecond.InputHashSha256.Should().Be(StrategyRunEntry.ComputeAccountBoundInputHash(completedSecond));
        fixture.Strategies.Should().OnlyContain(strategy => strategy.StopCount == 1);
    }

    private static void AssertSuccess(VerifiedOperationOutcome outcome)
    {
        outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    private static async Task AssertSingleSuccessfulCleanupAsync(SessionFixture fixture, StrategyRunEntry run, GatedStrategy strategy)
    {
        var retained = await fixture.ReloadRunAsync(run.RunId);
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        retained.Metrics.Should().NotBeNull();
        strategy.StopCount.Should().Be(1);
        strategy.FinishedCount.Should().Be(1);
        var history = await fixture.ReadHistoryAsync(run.RunId);
        history.Should().ContainSingle(record => record.EventType == "StopRequested")
            .Which.ActorId.Should().Be(SessionActorId);
        history.Should().ContainSingle(record => record.EventType == "Completed");
        history.Should().NotContain(record => record.EventType == "Failed" || record.EventType == "StopFailed" || record.EventType == "EvidencePersistenceFailed");
        fixture.Repository.RejectedStopIntent.Task.IsCompleted.Should().BeFalse(
            "natural completion and operator stop share a single session-owned intent append");
    }

    private sealed class SessionFixture : IAsyncDisposable
    {
        private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "meridian-session-stop-" + Guid.NewGuid().ToString("N"));
        private readonly LiveMarketDataCache _cache = new();
        private readonly Guid _fundAccountId = Guid.NewGuid();

        public SessionFixture(Func<LiveStrategyCreationContext, GatedStrategy>? createStrategy = null)
        {
            Directory.CreateDirectory(_dataRoot);
            History = new ObservedHistoryStore(new FileOperationalCaseHistoryStore(_dataRoot));
            Store = new StrategyRunStore(History);
            Repository = new GatedRepository(Store);
            Manager = new StrategyLifecycleManager(Repository, NullLogger<StrategyLifecycleManager>.Instance);
            var catalog = new LiveStrategyCatalog().Register(StrategyId, context =>
            {
                var strategy = createStrategy?.Invoke(context) ?? new GatedStrategy();
                strategy.Quantity = context.GetInt64("quantity", 1);
                Strategies.Enqueue(strategy);
                return strategy;
            });
            var gateway = new Meridian.Execution.Adapters.PaperTradingGateway(
                NullLogger<Meridian.Execution.Adapters.PaperTradingGateway>.Instance,
                securityMaster: null,
                options: null,
                liveFeed: _cache);
            Engine = new LiveTradingEngine(
                catalog, Feed, _cache, gateway, Orders,
                new Meridian.Execution.Services.PaperTradingPortfolio(100_000m), Repository,
                options: new LiveTradingEngineOptions { AllowLiveRuns = true },
                lifecycleManager: Manager,
                brokerageConfiguration: new ExecutionSdk.BrokerageConfiguration { LiveExecutionEnabled = true });
        }

        public ObservedHistoryStore History { get; }
        public StrategyRunStore Store { get; }
        public GatedRepository Repository { get; }
        public StrategyLifecycleManager Manager { get; }
        public LiveTradingEngine Engine { get; }
        public GatedFeed Feed { get; } = new();
        public FilledOrderManager Orders { get; } = new();
        public ConcurrentQueue<GatedStrategy> Strategies { get; } = new();

        public StrategyRunEntry CreateRun(long quantity = 1, RunType mode = RunType.Paper) =>
            StrategyRunEntry.StartForFundAccount(
                StrategyId, "Coordinated shutdown", mode, _fundAccountId, Guid.NewGuid().ToString("N"),
                engine: mode == RunType.Live ? "BrokerLive" : "BrokerPaper",
                parameterSet: new Dictionary<string, string>
                {
                    ["symbols"] = "AAPL",
                    ["quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });

        public async Task<ChannelWriter<LiveMarketEvent>> LaunchAsync(StrategyRunEntry run)
        {
            await Store.RecordRunAsync(run);
            var launch = await Engine.TryLaunchAsync(run);
            launch.Launched.Should().BeTrue(launch.Reason);
            return await Feed.NextSubscriptionAsync();
        }

        public void PublishTrade(ChannelWriter<LiveMarketEvent> subscription)
        {
            var trade = new Trade(DateTimeOffset.UtcNow, "AAPL", 123m, Size: 100, Aggressor: AggressorSide.Buy, SequenceNumber: 1);
            _cache.RecordTrade("AAPL", trade);
            subscription.TryWrite(new LiveMarketEvent(trade.Timestamp, trade.Symbol, trade)).Should().BeTrue();
        }

        public Task<StrategyRunEntry?> ReloadRunAsync(string runId) =>
            new StrategyRunStore(History).GetRunByIdAsync(runId);

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadHistoryAsync(string runId) =>
            History.ReadAsync(new OperationalCaseHistoryQuery { CaseType = StrategyRunStore.CaseType, CaseId = $"strategy-run:{runId}" });

        public async ValueTask DisposeAsync()
        {
            Repository.ReleaseAllGates();
            foreach (var strategy in Strategies)
            {
                strategy.ReleaseFill.Set();
                strategy.ReleaseStop.TrySetResult();
            }

            await Manager.DisposeAsync();
            await Engine.DisposeAsync();
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
    }

    private sealed class GatedStrategy : LiveStrategyBase
    {
        private int _submitted;
        private int _startCount;
        private int _stopCount;
        private int _finishedCount;
        public override string StrategyId => StrategySessionShutdownTests.StrategyId;
        public override string Name => "Coordinated shutdown";
        public long Quantity { get; set; } = 1;
        public bool BlockFill { get; init; }
        public bool BlockStop { get; init; }
        public bool FailFinished { get; init; }
        public int StopFailuresRemaining { get; set; }
        public int CancelStopsRemaining { get; set; }
        public int StopCount => Volatile.Read(ref _stopCount);
        public int StartCount => Volatile.Read(ref _startCount);
        public int FinishedCount => Volatile.Read(ref _finishedCount);
        public IExecutionContext? CurrentExecutionContext => ExecutionContext;
        public TaskCompletionSource FillEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFill { get; } = new(initialState: false);

        public override void OnTrade(Trade trade, IBacktestContext ctx)
        {
            if (Interlocked.Exchange(ref _submitted, 1) == 0)
            {
                ctx.PlaceMarketOrder(trade.Symbol, Quantity);
            }
        }

        public override void OnOrderFill(FillEvent fill, IBacktestContext ctx)
        {
            FillEntered.TrySetResult();
            if (BlockFill && !ReleaseFill.Wait(WaitBudget))
            {
                throw new TimeoutException("The test did not release the admitted fill.");
            }
        }

        public override void OnFinished(IBacktestContext ctx)
        {
            Interlocked.Increment(ref _finishedCount);
            if (FailFinished)
            {
                throw new InvalidOperationException("finished callback failed");
            }
        }

        protected override Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _startCount);
            return Task.CompletedTask;
        }

        protected override async Task OnStoppedAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _stopCount);
            StopEntered.TrySetResult();
            if (StopFailuresRemaining > 0)
            {
                StopFailuresRemaining--;
                throw new InvalidOperationException("cleanup failed once");
            }

            if (CancelStopsRemaining > 0)
            {
                CancelStopsRemaining--;
                throw new OperationCanceledException("cleanup retry cancelled");
            }

            if (BlockStop)
            {
                await ReleaseStop.Task.WaitAsync(ct);
            }
        }
    }

    private sealed class GatedFeed : ILiveMarketEventFeed
    {
        private readonly Channel<ChannelWriter<LiveMarketEvent>> _subscriptions = Channel.CreateUnbounded<ChannelWriter<LiveMarketEvent>>();

        public async Task<ChannelWriter<LiveMarketEvent>> NextSubscriptionAsync() =>
            await _subscriptions.Reader.ReadAsync().AsTask().WaitAsync(WaitBudget);

        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(
            IReadOnlyCollection<string> symbols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var events = Channel.CreateUnbounded<LiveMarketEvent>();
            _subscriptions.Writer.TryWrite(events.Writer);
            await foreach (var item in events.Reader.ReadAllAsync(ct))
            {
                yield return item;
            }
        }
    }

    private sealed class ObservedHistoryStore(IOperationalCaseHistoryStore inner) : IOperationalCaseHistoryStore
    {
        public TaskCompletionSource StopRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request, CancellationToken cancellationToken = default)
        {
            var retained = await inner.AppendAsync(request, cancellationToken);
            if (request.EventType == nameof(StrategyRunLifecycleEventType.StopRequested))
            {
                StopRequested.TrySetResult();
            }

            return retained;
        }

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(query, cancellationToken);
    }

    private sealed class StopIntentGate(string actorId)
    {
        public string ActorId { get; } = actorId;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedRepository(IStrategyRepository inner) : IStrategyRepository
    {
        private readonly Lock _gateLock = new();
        private readonly List<StopIntentGate> _gates = [];
        private StopIntentGate? _nextStopIntent;
        public TaskCompletionSource RejectedStopIntent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StopIntentGate GateNextStopIntent(string actorId)
        {
            lock (_gateLock)
            {
                var gate = new StopIntentGate(actorId);
                _gates.Add(gate);
                _nextStopIntent = gate;
                return gate;
            }
        }

        public void ReleaseAllGates()
        {
            lock (_gateLock)
            {
                foreach (var gate in _gates)
                {
                    gate.Release.TrySetResult();
                }
            }
        }

        public async Task RecordLifecycleEventAsync(StrategyRunEntry entry, StrategyRunLifecycleEventType eventType, CancellationToken ct = default)
        {
            StopIntentGate? gate = null;
            if (eventType == StrategyRunLifecycleEventType.StopRequested)
            {
                lock (_gateLock)
                {
                    if (_nextStopIntent?.ActorId == entry.ActorId)
                    {
                        gate = _nextStopIntent;
                        _nextStopIntent = null;
                    }
                }
            }

            if (gate is not null)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(ct);
            }

            try
            {
                await inner.RecordLifecycleEventAsync(entry, eventType, ct);
            }
            catch when (eventType == StrategyRunLifecycleEventType.StopRequested)
            {
                RejectedStopIntent.TrySetResult();
                throw;
            }
        }

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) => inner.RecordRunAsync(entry, ct);
        public Task<VerifiedOperationOutcome?> RecordLifecycleEventWithOutcomeAsync(StrategyRunEntry entry, StrategyRunLifecycleEventType eventType, CancellationToken ct = default) =>
            inner.RecordLifecycleEventWithOutcomeAsync(entry, eventType, ct);
        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default) => inner.GetRunsAsync(strategyId, ct);
        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) => inner.GetLatestRunAsync(strategyId, ct);
        public Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default) => inner.GetRunByIdAsync(runId, ct);
        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) => inner.GetAllRunsAsync(ct);
    }

    private sealed class FilledOrderManager : ExecutionSdk.IOrderManager
    {
        public ConcurrentQueue<ExecutionSdk.OrderRequest> Requests { get; } = new();

        public Task<ExecutionSdk.OrderResult> PlaceOrderAsync(ExecutionSdk.OrderRequest request, CancellationToken ct = default)
        {
            Requests.Enqueue(request);
            var orderId = request.ClientOrderId!;
            return Task.FromResult(new ExecutionSdk.OrderResult
            {
                Success = true,
                OrderId = orderId,
                OrderState = new ExecutionSdk.OrderState
                {
                    OrderId = orderId,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    Type = request.Type,
                    Quantity = request.Quantity,
                    FilledQuantity = request.Quantity,
                    Status = ExecutionSdk.OrderStatus.Filled,
                    AverageFillPrice = 123m,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                    StrategyId = request.StrategyId
                }
            });
        }

        public Task<ExecutionSdk.OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionSdk.OrderResult { Success = true, OrderId = orderId });
        public Task<ExecutionSdk.OrderResult> ModifyOrderAsync(string orderId, ExecutionSdk.OrderModification modification, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionSdk.OrderResult { Success = true, OrderId = orderId });
        public IReadOnlyList<ExecutionSdk.OrderState> GetOpenOrders() => [];
        public ExecutionSdk.OrderState? GetOrder(string orderId) => null;
        public Task<ExecutionSdk.KillSwitchSweepResult> CancelAllAsync(CancellationToken ct = default) =>
            Task.FromResult(ExecutionSdk.KillSwitchSweepResult.Empty);
        public IReadOnlyList<ExecutionSdk.OrderState> GetCompletedOrders(int take = 20) => [];
    }
}
