using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
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
    public async Task ConnectAsync_FatalInitializationFailureRejectsReadinessAndCleansBeforeNextConnection()
    {
        await using var supervisor = CreateSupervisor();
        var failure = new HttpRequestException("provider control failure", null, HttpStatusCode.InternalServerError);
        var cleanupEntered = NewSignal();
        var releaseCleanup = NewSignal();
        var nextTransactionEntered = false;
        var connectedPublications = 0;
        supervisor.StateChanged += snapshot =>
        {
            if (snapshot.IsConnected)
                connectedPublications++;
        };

        var rejected = supervisor.ConnectAsync(_ =>
        {
            supervisor.TryRecordInitializationFailure(failure, supervisor.ConnectionAttemptId).Should().BeTrue();
            return Task.CompletedTask;
        }, async () =>
        {
            cleanupEntered.TrySetResult(true);
            await releaseCleanup.Task;
        }, CancellationToken.None);

        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        supervisor.IsConnected.Should().BeFalse();
        connectedPublications.Should().Be(0);
        var next = supervisor.ConnectAsync(_ =>
        {
            nextTransactionEntered = true;
            return Task.CompletedTask;
        });
        nextTransactionEntered.Should().BeFalse("rejected initialization cleanup retains the operation gate");

        releaseCleanup.TrySetResult(true);
        var assertion = await FluentActions.Awaiting(() => rejected).Should().ThrowAsync<HttpRequestException>();
        assertion.Which.Should().BeSameAs(failure);
        await next.WaitAsync(TimeSpan.FromSeconds(5));
        connectedPublications.Should().Be(1);
    }

    [Fact]
    public async Task ConnectAsync_ExplicitFailureSurvivesAbortInducedCancellation()
    {
        await using var supervisor = CreateSupervisor();
        var failure = new AuthenticationException("provider rejected entitlement");

        var connecting = supervisor.ConnectAsync(_ =>
        {
            supervisor.TryRecordInitializationFailure(failure, supervisor.ConnectionAttemptId).Should().BeTrue();
            throw new OperationCanceledException("transport aborted after fatal control response");
        });

        var assertion = await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<AuthenticationException>();
        assertion.Which.Should().BeSameAs(failure);
        supervisor.GetSnapshot().LastFailureKind.Should().Be(ProviderFailureKind.AuthenticationOrAuthorizationFailure);
        supervisor.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ReconnectAsync_InitializationFailureIsResetAndOldAttemptCannotPoisonReplacement()
    {
        await using var supervisor = CreateSupervisor(maxReconnectAttempts: 3);
        await EstablishAndLoseConnectionAsync(supervisor);
        var attempts = 0;
        var cleanupCount = 0;
        long failedAttempt = 0;
        var failure = new HttpRequestException("temporary provider outage", null, HttpStatusCode.InternalServerError);

        var reconnected = await supervisor.ReconnectAsync(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                failedAttempt = supervisor.ConnectionAttemptId;
                supervisor.TryRecordInitializationFailure(failure, failedAttempt).Should().BeTrue();
            }
            else
            {
                supervisor.ConnectionAttemptId.Should().NotBe(failedAttempt);
                supervisor.TryRecordInitializationFailure(new AuthenticationException("stale failure"), failedAttempt)
                    .Should().BeFalse();
            }
            return Task.CompletedTask;
        }, () =>
        {
            cleanupCount++;
            supervisor.IsConnected.Should().BeFalse();
            return Task.CompletedTask;
        }, CancellationToken.None);

        reconnected.Should().BeTrue();
        attempts.Should().Be(2);
        cleanupCount.Should().Be(1);
        supervisor.GetSnapshot().LastFailureKind.Should().BeNull();
        supervisor.MarkConnectionLost(new AuthenticationException("stale loss"), failedAttempt).Should().BeFalse();
        supervisor.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_OrdinaryFailureDiagnosticsDoNotRejectInitialization()
    {
        await using var supervisor = CreateSupervisor();

        await supervisor.ConnectAsync(_ =>
        {
            supervisor.RecordFailure(new FormatException("nonfatal market-data diagnostic"));
            return Task.CompletedTask;
        });

        supervisor.IsConnected.Should().BeTrue();
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
        try
        {
            await transactionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var disconnectCts = new CancellationTokenSource();
            var disconnectTask = supervisor.DisconnectAsync(static _ => Task.CompletedTask, disconnectCts.Token);
            disconnectTask.IsCompleted.Should().BeFalse("the provider transaction is still blocked");

            disconnectCts.Cancel();
            Func<Task> disconnect = () => disconnectTask.WaitAsync(TimeSpan.FromSeconds(5));
            await disconnect.Should().ThrowAsync<OperationCanceledException>();
            reconnectTask.IsCompleted.Should().BeFalse("caller cancellation must not depend on provider cooperation");

            using var disposeCts = new CancellationTokenSource();
            var disposeTask = supervisor.DisposeAsync(disposeCts.Token).AsTask();
            disposeTask.IsCompleted.Should().BeFalse("disposal is waiting on the same blocked provider transaction");
            disposeCts.Cancel();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            supervisor.GetSnapshot().LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
            supervisor.IsReconnecting.Should().BeFalse();
            reconnectTask.IsCompleted.Should().BeFalse();

            releaseTransaction.TrySetResult(true);
            (await reconnectTask.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse(
                "a reconnect transaction that returns after disposal cannot reactivate the provider");
        }
        finally
        {
            releaseTransaction.TrySetResult(true);
            await supervisor.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisconnectAsync_NonCooperativeCleanup_RemainsCallerBoundedAndTerminal()
    {
        await using var supervisor = CreateSupervisor();
        await supervisor.ConnectAsync(static _ => Task.CompletedTask);
        var cleanupEntered = NewSignal();
        var releaseCleanup = NewSignal();
        using var disconnectCts = new CancellationTokenSource();

        var disconnectTask = supervisor.DisconnectAsync(async _ =>
        {
            cleanupEntered.TrySetResult(true);
            await releaseCleanup.Task;
        }, disconnectCts.Token);
        Func<Task> disconnect = () => disconnectTask.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disconnectTask.IsCompleted.Should().BeFalse("the provider cleanup is still blocked");
            disconnectCts.Cancel();
            await disconnect.Should().ThrowAsync<OperationCanceledException>();
            releaseCleanup.Task.IsCompleted.Should().BeFalse("caller cancellation must not depend on provider cleanup");
            supervisor.GetSnapshot().LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        }
        finally
        {
            releaseCleanup.TrySetResult(true);
        }
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
