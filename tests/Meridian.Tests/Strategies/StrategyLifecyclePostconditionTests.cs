using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Execution.Interfaces;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecyclePostconditionTests
{
    [Theory]
    [InlineData(StrategyStatus.Registered)]
    [InlineData(StrategyStatus.WarmingUp)]
    [InlineData(StrategyStatus.Paused)]
    [InlineData(StrategyStatus.Stopping)]
    [InlineData(StrategyStatus.Stopped)]
    [InlineData(StrategyStatus.Faulted)]
    public async Task Start_ReturningWithoutRunning_RetainsFailure(StrategyStatus reportedStatus)
    {
        var repository = new StrategyRunStore();
        var strategy = new ReportingStrategy { StartResult = reportedStatus };
        var manager = CreateManager(repository, strategy);

        var outcome = await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);

        outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        var retained = await repository.GetLatestRunAsync(strategy.StrategyId);
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StartFailed);
        retained.ExceptionMessage.Should().Contain($"state '{reportedStatus}'").And.Contain("expected 'Running'");
    }

    [Theory]
    [InlineData(StrategyStatus.Registered)]
    [InlineData(StrategyStatus.WarmingUp)]
    [InlineData(StrategyStatus.Running)]
    [InlineData(StrategyStatus.Stopping)]
    [InlineData(StrategyStatus.Stopped)]
    [InlineData(StrategyStatus.Faulted)]
    public async Task Pause_ReturningWithoutPausing_RetainsFailure(StrategyStatus reportedStatus)
    {
        var repository = new StrategyRunStore();
        var strategy = new ReportingStrategy { PauseResult = reportedStatus };
        var manager = CreateManager(repository, strategy);
        await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);

        var outcome = await manager.PauseAsync(strategy.StrategyId);

        outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        var retained = await repository.GetLatestRunAsync(strategy.StrategyId);
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.PauseFailed);
        retained.ExceptionMessage.Should().Contain($"state '{reportedStatus}'").And.Contain("expected 'Paused'");
    }

    [Theory]
    [InlineData(StrategyStatus.Registered)]
    [InlineData(StrategyStatus.WarmingUp)]
    [InlineData(StrategyStatus.Running)]
    [InlineData(StrategyStatus.Paused)]
    [InlineData(StrategyStatus.Stopping)]
    [InlineData(StrategyStatus.Faulted)]
    public async Task Stop_ReturningWithoutStopping_RetainsFailure(StrategyStatus reportedStatus)
    {
        var repository = new StrategyRunStore();
        var strategy = new ReportingStrategy { StopResult = reportedStatus };
        var manager = CreateManager(repository, strategy);
        await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);

        var outcome = await manager.StopAsync(strategy.StrategyId);

        outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        var retained = await repository.GetLatestRunAsync(strategy.StrategyId);
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StopFailed);
        retained.ExceptionMessage.Should().Contain($"state '{reportedStatus}'").And.Contain("expected 'Stopped'");
        retained.EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task RealAdapter_ResumePreservesWarmupAndStopWaitsForCleanup()
    {
        var repository = new StrategyRunStore();
        var strategy = new CoordinatedStrategy();
        var manager = CreateManager(repository, strategy);
        var context = Context();

        var started = await manager.StartAsync(strategy.StrategyId, context, RunType.Paper);
        var paused = await manager.PauseAsync(strategy.StrategyId);
        var resumed = await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);

        started.State.Should().Be(OperationTerminalState.Succeeded);
        paused.State.Should().Be(OperationTerminalState.Succeeded);
        resumed.State.Should().Be(OperationTerminalState.Succeeded);
        strategy.WarmupCount.Should().Be(1);
        strategy.RetainedContext.Should().BeSameAs(context);

        var stopping = manager.StopAsync(strategy.StrategyId);
        await strategy.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            strategy.Status.Should().Be(StrategyStatus.Stopping);
            stopping.IsCompleted.Should().BeFalse();
            (await repository.GetLatestRunAsync(strategy.StrategyId))!
                .LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StopRequested);
        }
        finally
        {
            strategy.ReleaseCleanup.TrySetResult();
        }

        var stopped = await stopping;
        stopped.State.Should().Be(OperationTerminalState.Succeeded);
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.RetainedContext.Should().BeNull();
        (await repository.GetLatestRunAsync(strategy.StrategyId))!
            .LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
    }

    [Fact]
    public async Task RealAdapter_StartFailureRequiresCleanupBeforeRestart()
    {
        var repository = new StrategyRunStore();
        var strategy = new CoordinatedStrategy { FailWarmup = true };
        strategy.ReleaseCleanup.TrySetResult();
        var manager = CreateManager(repository, strategy);

        var failed = await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);
        failed.State.Should().Be(OperationTerminalState.Failed);
        strategy.Status.Should().Be(StrategyStatus.Faulted);
        strategy.FaultReason.Should().Be("Warmup failed.");

        var retry = () => manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);
        await retry.Should().ThrowAsync<InvalidOperationException>();

        var stopped = await manager.StopAsync(strategy.StrategyId);
        stopped.State.Should().Be(OperationTerminalState.Succeeded);
        strategy.FailWarmup = false;
        var restarted = await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);
        restarted.State.Should().Be(OperationTerminalState.Succeeded);
        strategy.WarmupCount.Should().Be(2);
        strategy.FaultReason.Should().BeNull();
    }

    [Theory]
    [InlineData(RunType.Paper, RunType.Live)]
    [InlineData(RunType.Live, RunType.Paper)]
    public async Task Resume_CannotChangeRetainedRunType(RunType originalType, RunType requestedType)
    {
        var repository = new StrategyRunStore();
        var strategy = new ReportingStrategy();
        var manager = CreateManager(repository, strategy);
        await manager.StartAsync(strategy.StrategyId, Context(), originalType);
        await manager.PauseAsync(strategy.StrategyId);
        var retained = await repository.GetLatestRunAsync(strategy.StrategyId);

        var resume = () => manager.StartAsync(strategy.StrategyId, Context(), requestedType);
        await resume.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Stop the strategy before changing its run type.*");

        strategy.Status.Should().Be(StrategyStatus.Paused);
        (await repository.GetLatestRunAsync(strategy.StrategyId)).Should().BeEquivalentTo(retained);
        var stopped = await manager.StopAsync(strategy.StrategyId);
        stopped.State.Should().Be(OperationTerminalState.Succeeded);
        var restarted = await manager.StartAsync(strategy.StrategyId, Context(), requestedType);
        restarted.State.Should().Be(OperationTerminalState.Succeeded);
        (await repository.GetLatestRunAsync(strategy.StrategyId))!.RunType.Should().Be(requestedType);
    }

    [Fact]
    public async Task Resume_WithoutRetainedRun_RefusesToInventExecutionMode()
    {
        var repository = new StrategyRunStore();
        var strategy = new ReportingStrategy();
        await strategy.StartAsync(Context());
        await strategy.PauseAsync();
        var manager = CreateManager(repository, strategy);

        var resume = () => manager.StartAsync(strategy.StrategyId, Context(), RunType.Live);
        await resume.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot resume without a retained current run.*");

        strategy.Status.Should().Be(StrategyStatus.Paused);
        (await repository.GetLatestRunAsync(strategy.StrategyId)).Should().BeNull();
    }

    private static IExecutionContext Context() => Mock.Of<IExecutionContext>();

    private static StrategyLifecycleManager CreateManager(IStrategyRepository repository, ILiveStrategy strategy)
    {
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(strategy);
        return manager;
    }

    private sealed class ReportingStrategy : BacktestStrategyBase, ILiveStrategy
    {
        public override string Name => StrategyId;
        public string StrategyId => "reporting-strategy";
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;
        public StrategyStatus StartResult { get; init; } = StrategyStatus.Running;
        public StrategyStatus PauseResult { get; init; } = StrategyStatus.Paused;
        public StrategyStatus StopResult { get; init; } = StrategyStatus.Stopped;

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StartResult;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            Status = PauseResult;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Status = StopResult;
            return Task.CompletedTask;
        }
    }

    private sealed class CoordinatedStrategy : LiveStrategyBase
    {
        public override string Name => StrategyId;
        public override string StrategyId => "coordinated-strategy";
        public int WarmupCount { get; private set; }
        public bool FailWarmup { get; set; }
        public IExecutionContext? RetainedContext => ExecutionContext;
        public TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct)
        {
            WarmupCount++;
            if (FailWarmup)
            {
                throw new InvalidOperationException("Warmup failed.");
            }

            return Task.CompletedTask;
        }

        protected override async Task OnStoppedAsync(CancellationToken ct)
        {
            CleanupEntered.TrySetResult();
            await ReleaseCleanup.Task.WaitAsync(ct);
        }
    }
}
