using System.Net.WebSockets;
using System.Security.Authentication;
using System.Diagnostics;
using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using Xunit;

namespace Meridian.Tests.Infrastructure.Resilience;

public sealed class ProviderConnectionSupervisorTests
{
    [Fact]
    public async Task ConnectAsync_PublishesConnectedOnlyAfterCompleteTransaction()
    {
        await using var supervisor = CreateSupervisor();
        var transactionEntered = NewSignal();
        var releaseTransaction = NewSignal();

        var connectTask = supervisor.ConnectAsync(async ct =>
        {
            transactionEntered.TrySetResult(true);
            await releaseTransaction.Task.WaitAsync(ct);
        });

        await transactionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var duringTransaction = supervisor.GetSnapshot();
        duringTransaction.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Connecting);
        duringTransaction.IsConnected.Should().BeFalse();

        releaseTransaction.TrySetResult(true);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        var completed = supervisor.GetSnapshot();
        completed.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Connected);
        completed.IsConnected.Should().BeTrue();
        completed.LastConnectedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReconnectAsync_ReplayFailuresConsumeBoundedFullTransactionAttempts()
    {
        await using var supervisor = CreateSupervisor(maxReconnectAttempts: 3);
        await EstablishAndLoseConnectionAsync(supervisor);
        var transportConnections = 0;
        var authentications = 0;
        var replayAttempts = 0;

        var reconnected = await supervisor.ReconnectAsync(_ =>
        {
            transportConnections++;
            authentications++;
            replayAttempts++;
            throw new WebSocketException("subscription replay failed after transport connection");
        });

        reconnected.Should().BeFalse();
        transportConnections.Should().Be(3);
        authentications.Should().Be(3);
        replayAttempts.Should().Be(3);

        var failed = supervisor.GetSnapshot();
        failed.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Failed);
        failed.ReconnectAttempts.Should().Be(3);
        failed.LastFailureKind.Should().Be(ProviderFailureKind.TransientNetworkFailure);

        var duplicateTrigger = await supervisor.ReconnectAsync(_ => Task.CompletedTask);
        duplicateTrigger.Should().BeFalse("a terminal reconnect cycle must not be restarted by a stale loss notification");
        replayAttempts.Should().Be(3);
    }

    [Fact]
    public async Task ReconnectAsync_ConcurrentCallersJoinOneTrackedOperation()
    {
        await using var supervisor = CreateSupervisor();
        await EstablishAndLoseConnectionAsync(supervisor);
        var transactionEntered = NewSignal();
        var releaseTransaction = NewSignal();
        var transactionCount = 0;

        var first = supervisor.ReconnectAsync(async ct =>
        {
            Interlocked.Increment(ref transactionCount);
            transactionEntered.TrySetResult(true);
            await releaseTransaction.Task.WaitAsync(ct);
        });

        await transactionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = supervisor.ReconnectAsync(_ =>
        {
            Interlocked.Increment(ref transactionCount);
            return Task.CompletedTask;
        });

        second.Should().BeSameAs(first);
        releaseTransaction.TrySetResult(true);

        (await first.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        transactionCount.Should().Be(1);
        supervisor.GetSnapshot().ReconnectAttempts.Should().Be(1);
    }

    [Fact]
    public async Task DisconnectAsync_CancelsAndAwaitsTrackedReconnectBeforeCleanup()
    {
        await using var supervisor = CreateSupervisor();
        await EstablishAndLoseConnectionAsync(supervisor);
        var transactionEntered = NewSignal();
        var transactionCancelled = NewSignal();
        var cleanupCalled = false;

        var reconnectTask = supervisor.ReconnectAsync(async ct =>
        {
            transactionEntered.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                transactionCancelled.TrySetResult(true);
            }
        });

        await transactionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await supervisor.DisconnectAsync(_ =>
        {
            transactionCancelled.Task.IsCompleted.Should().BeTrue(
                "transport cleanup must run after the reconnect transaction observes cancellation");
            cleanupCalled = true;
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(5));

        (await reconnectTask.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
        cleanupCalled.Should().BeTrue();
        supervisor.GetSnapshot().LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAndDispose_NonCooperativeReconnect_RemainCallerBoundedAndTerminal()
    {
        var supervisor = CreateSupervisor();
        await EstablishAndLoseConnectionAsync(supervisor);
        var transactionEntered = NewSignal();
        var releaseTransaction = NewSignal();
        var reconnectTask = supervisor.ReconnectAsync(async _ =>
        {
            transactionEntered.TrySetResult(true);
            await releaseTransaction.Task;
        });
        await transactionEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using var disconnectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        var elapsed = Stopwatch.StartNew();
        Func<Task> disconnect = async () => await supervisor.DisconnectAsync(
            static _ => Task.CompletedTask,
            disconnectCts.Token);

        await disconnect.Should().ThrowAsync<OperationCanceledException>();
        elapsed.Stop();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750));

        using var disposeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        await supervisor.DisposeAsync(disposeCts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        supervisor.GetSnapshot().LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        supervisor.IsReconnecting.Should().BeFalse();

        releaseTransaction.TrySetResult(true);
        (await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1))).Should().BeFalse(
            "a reconnect transaction that returns after disposal cannot reactivate the provider");
        await supervisor.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_NonCooperativeCleanup_RemainsCallerBoundedAndTerminal()
    {
        await using var supervisor = CreateSupervisor();
        await supervisor.ConnectAsync(static _ => Task.CompletedTask);
        var cleanupEntered = NewSignal();
        var releaseCleanup = NewSignal();
        using var disconnectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        var elapsed = Stopwatch.StartNew();

        Func<Task> disconnect = async () => await supervisor.DisconnectAsync(async _ =>
        {
            cleanupEntered.TrySetResult(true);
            await releaseCleanup.Task;
        }, disconnectCts.Token);

        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await disconnect.Should().ThrowAsync<OperationCanceledException>();
        elapsed.Stop();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750));
        supervisor.GetSnapshot().LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        releaseCleanup.TrySetResult(true);
    }

    [Fact]
    public async Task ReconnectAsync_NonRetryableAuthenticationFailureStopsAfterFirstAttempt()
    {
        await using var supervisor = CreateSupervisor(maxReconnectAttempts: 5);
        await EstablishAndLoseConnectionAsync(supervisor);
        var attempts = 0;

        var reconnected = await supervisor.ReconnectAsync(_ =>
        {
            attempts++;
            throw new AuthenticationException("provider rejected credentials");
        });

        reconnected.Should().BeFalse();
        attempts.Should().Be(1);

        var failed = supervisor.GetSnapshot();
        failed.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Failed);
        failed.ReconnectAttempts.Should().Be(1);
        failed.LastFailureKind.Should().Be(ProviderFailureKind.AuthenticationOrAuthorizationFailure);
    }

    private static ProviderConnectionSupervisor CreateSupervisor(int maxReconnectAttempts = 3)
        => new(
            providerName: "test-provider",
            maxReconnectAttempts: maxReconnectAttempts,
            retryBaseDelay: TimeSpan.Zero,
            maxRetryDelay: TimeSpan.Zero);

    private static async Task EstablishAndLoseConnectionAsync(ProviderConnectionSupervisor supervisor)
    {
        await supervisor.ConnectAsync(_ => Task.CompletedTask);
        supervisor.MarkConnectionLost().Should().BeTrue();
        supervisor.GetSnapshot().LifecycleState.Should().Be(ProviderConnectionLifecycleState.Degraded);
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
