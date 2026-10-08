using FluentAssertions;
using Meridian.Execution.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class LiveStrategyBaseTests
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StartAsync_ReportsWarmingUpUntilWarmupCompletes()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Starting = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };

        var start = strategy.StartAsync(NewContext());
        await entered.Task.WaitAsync(WaitBudget);

        strategy.Status.Should().Be(StrategyStatus.WarmingUp);
        start.IsCompleted.Should().BeFalse();
        release.SetResult();
        await start.WaitAsync(WaitBudget);
        strategy.Status.Should().Be(StrategyStatus.Running);
    }

    [Fact]
    public async Task StopAsync_ReportsStoppingUntilCleanupCompletes()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Stopping = async ct =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var context = NewContext();
        await strategy.StartAsync(context);

        var stop = strategy.StopAsync();
        await entered.Task.WaitAsync(WaitBudget);

        strategy.Status.Should().Be(StrategyStatus.Stopping);
        strategy.CurrentContext.Should().BeSameAs(context);
        stop.IsCompleted.Should().BeFalse();
        release.SetResult();
        await stop.WaitAsync(WaitBudget);
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.CurrentContext.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WhenPaused_ResumesWithoutRepeatingWarmupOrReplacingContext()
    {
        var strategy = new TestStrategy();
        var context = NewContext();
        await strategy.StartAsync(context);
        await strategy.PauseAsync();

        await strategy.StartAsync(NewContext());

        strategy.Status.Should().Be(StrategyStatus.Running);
        strategy.StartCallCount.Should().Be(1);
        strategy.CurrentContext.Should().BeSameAs(context);
    }

    [Fact]
    public async Task StartAsync_WhenWarmupFails_RequiresSuccessfulCleanupBeforeRestart()
    {
        var strategy = new TestStrategy
        {
            Starting = static (_, _) => throw new InvalidOperationException("Warmup history unavailable.")
        };
        var context = NewContext();

        var start = () => strategy.StartAsync(context);
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("Warmup history unavailable.");
        strategy.Status.Should().Be(StrategyStatus.Faulted);
        strategy.FaultReason.Should().Be("Warmup history unavailable.");
        strategy.CurrentContext.Should().BeSameAs(context);

        strategy.Starting = static (_, _) => Task.CompletedTask;
        await start.Should().ThrowAsync<InvalidOperationException>();
        strategy.StartCallCount.Should().Be(1);
        strategy.FaultReason.Should().Be("Warmup history unavailable.");

        await strategy.StopAsync();
        strategy.StopCallCount.Should().Be(1);
        strategy.FaultReason.Should().BeNull();
        await start();
        strategy.Status.Should().Be(StrategyStatus.Running);
        strategy.StartCallCount.Should().Be(2);
    }

    [Fact]
    public async Task StopAsync_WhenCleanupFails_RetainsFaultUntilCleanupIsRetriedSuccessfully()
    {
        var strategy = new TestStrategy
        {
            Stopping = static _ => throw new InvalidOperationException("Finalisation failed.")
        };
        await strategy.StartAsync(NewContext());

        var stop = () => strategy.StopAsync();
        await stop.Should().ThrowAsync<InvalidOperationException>().WithMessage("Finalisation failed.");
        strategy.Status.Should().Be(StrategyStatus.Faulted);
        strategy.FaultReason.Should().Be("Finalisation failed.");

        var start = () => strategy.StartAsync(NewContext());
        await start.Should().ThrowAsync<InvalidOperationException>();
        strategy.StartCallCount.Should().Be(1);
        strategy.Stopping = static _ => Task.CompletedTask;
        await stop();
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.StopCallCount.Should().Be(2);
        strategy.FaultReason.Should().BeNull();
        await start();
        strategy.Status.Should().Be(StrategyStatus.Running);
    }

    [Fact]
    public async Task StopAsync_RequestedDuringWarmup_WaitsForWarmupAndCannotBeOverwrittenByStart()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Starting = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var start = strategy.StartAsync(NewContext());
        await entered.Task.WaitAsync(WaitBudget);

        var stop = strategy.StopAsync();

        stop.IsCompleted.Should().BeFalse();
        strategy.StopCallCount.Should().Be(0);
        strategy.Status.Should().Be(StrategyStatus.WarmingUp);
        release.SetResult();
        await Task.WhenAll(start, stop).WaitAsync(WaitBudget);
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.StopCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PauseAsync_RequestedDuringWarmup_PausesOnlyAfterWarmupCompletes()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Starting = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var start = strategy.StartAsync(NewContext());
        await entered.Task.WaitAsync(WaitBudget);

        var pause = strategy.PauseAsync();

        pause.IsCompleted.Should().BeFalse();
        strategy.Status.Should().Be(StrategyStatus.WarmingUp);
        release.SetResult();
        await Task.WhenAll(start, pause).WaitAsync(WaitBudget);
        strategy.Status.Should().Be(StrategyStatus.Paused);
    }

    [Fact]
    public async Task StopAsync_ConcurrentRequests_RunCleanupOnce()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Stopping = async ct =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        await strategy.StartAsync(NewContext());
        var firstStop = strategy.StopAsync();
        await entered.Task.WaitAsync(WaitBudget);

        var secondStop = strategy.StopAsync();

        secondStop.IsCompleted.Should().BeFalse();
        strategy.StopCallCount.Should().Be(1);
        release.SetResult();
        await Task.WhenAll(firstStop, secondStop).WaitAsync(WaitBudget);
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.StopCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenWarmupIsCancelled_FaultsAndRequiresCleanup()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Starting = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        using var cancellation = new CancellationTokenSource();
        var start = strategy.StartAsync(NewContext(), cancellation.Token);
        await entered.Task.WaitAsync(WaitBudget);

        cancellation.Cancel();

        var cancelledStart = () => start;
        await cancelledStart.Should().ThrowAsync<OperationCanceledException>();
        strategy.Status.Should().Be(StrategyStatus.Faulted);
        strategy.FaultReason.Should().NotBeNullOrWhiteSpace();
        var retry = () => strategy.StartAsync(NewContext());
        await retry.Should().ThrowAsync<InvalidOperationException>();
        await strategy.StopAsync();
        strategy.Status.Should().Be(StrategyStatus.Stopped);
    }

    [Fact]
    public async Task StopAsync_WhenCleanupIsCancelled_FaultsAndAllowsCleanupRetry()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Stopping = async ct =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        await strategy.StartAsync(NewContext());
        using var cancellation = new CancellationTokenSource();
        var stop = strategy.StopAsync(cancellation.Token);
        await entered.Task.WaitAsync(WaitBudget);

        cancellation.Cancel();

        var cancelledStop = () => stop;
        await cancelledStop.Should().ThrowAsync<OperationCanceledException>();
        strategy.Status.Should().Be(StrategyStatus.Faulted);
        strategy.FaultReason.Should().NotBeNullOrWhiteSpace();
        release.SetResult();
        await strategy.StopAsync();
        strategy.Status.Should().Be(StrategyStatus.Stopped);
        strategy.StopCallCount.Should().Be(2);
    }

    [Fact]
    public async Task StopAsync_WhenCancelledWhileWaiting_LeavesCurrentOperationUnchanged()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var strategy = new TestStrategy
        {
            Starting = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var start = strategy.StartAsync(NewContext());
        await entered.Task.WaitAsync(WaitBudget);
        using var cancellation = new CancellationTokenSource();
        var stop = strategy.StopAsync(cancellation.Token);

        cancellation.Cancel();

        var cancelledStop = () => stop;
        await cancelledStop.Should().ThrowAsync<OperationCanceledException>();
        strategy.Status.Should().Be(StrategyStatus.WarmingUp);
        strategy.FaultReason.Should().BeNull();
        strategy.StopCallCount.Should().Be(0);
        release.SetResult();
        await start.WaitAsync(WaitBudget);
        strategy.Status.Should().Be(StrategyStatus.Running);
    }

    [Fact]
    public async Task StartAsync_WhenExceptionHasNoMessage_RetainsItsTypeAsFaultReason()
    {
        var strategy = new TestStrategy
        {
            Starting = static (_, _) => throw new InvalidOperationException(string.Empty)
        };

        var start = () => strategy.StartAsync(NewContext());

        await start.Should().ThrowAsync<InvalidOperationException>();
        strategy.Status.Should().Be(StrategyStatus.Faulted);
        strategy.FaultReason.Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task InvalidCommands_LeaveStateAndHooksUnchanged()
    {
        var strategy = new TestStrategy();
        var pause = () => strategy.PauseAsync();
        var stop = () => strategy.StopAsync();

        await pause.Should().ThrowAsync<InvalidOperationException>();
        await stop.Should().ThrowAsync<InvalidOperationException>();
        strategy.Status.Should().Be(StrategyStatus.Registered);
        strategy.StopCallCount.Should().Be(0);

        await strategy.StartAsync(NewContext());
        var start = () => strategy.StartAsync(NewContext());
        await start.Should().ThrowAsync<InvalidOperationException>();
        strategy.Status.Should().Be(StrategyStatus.Running);
        strategy.StartCallCount.Should().Be(1);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IExecutionContext NewContext() => Substitute.For<IExecutionContext>();

    private sealed class TestStrategy : LiveStrategyBase
    {
        public override string StrategyId => "test-strategy";
        public override string Name => "Lifecycle test strategy";
        public IExecutionContext? CurrentContext => ExecutionContext;
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public Func<IExecutionContext, CancellationToken, Task> Starting { get; set; } =
            static (_, _) => Task.CompletedTask;
        public Func<CancellationToken, Task> Stopping { get; set; } = static _ => Task.CompletedTask;

        protected override Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct)
        {
            StartCallCount++;
            return Starting(ctx, ct);
        }

        protected override Task OnStoppedAsync(CancellationToken ct)
        {
            StopCallCount++;
            return Stopping(ct);
        }
    }
}
