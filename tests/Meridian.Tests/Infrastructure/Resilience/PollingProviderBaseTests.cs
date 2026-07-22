using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Resilience;

public sealed class PollingProviderBaseTests
{
    [Fact]
    public async Task FailedPoll_RecoversThroughOneSupervisedProbeAndReturnsConnected()
    {
        var provider = new TestPollingProvider(
            _ => Task.FromResult(false),
            _ => Task.FromResult(true));
        var observed = new ConcurrentQueue<WebSocketConnectionDiagnostics>();
        provider.ConnectionDiagnosticsChanged += observed.Enqueue;

        try
        {
            await provider.ConnectAsync();
            await WaitUntilAsync(() =>
            {
                var snapshot = provider.GetConnectionDiagnosticsSnapshot();
                return snapshot.IsConnected && snapshot.ReconnectAttempts == 1;
            });

            observed.Should().Contain(snapshot =>
                snapshot.LifecycleState == ProviderConnectionLifecycleState.Reconnecting);
            provider.PollCount.Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task NonRetryablePollFailure_StopsTheBoundedRecoveryTransaction()
    {
        var provider = new TestPollingProvider(
            _ => Task.FromException<bool>(new UnauthorizedAccessException("token rejected")),
            _ => Task.FromException<bool>(new UnauthorizedAccessException("token rejected")));

        try
        {
            await provider.ConnectAsync();
            await WaitUntilAsync(() =>
                provider.GetConnectionDiagnosticsSnapshot().LifecycleState ==
                    ProviderConnectionLifecycleState.Failed);

            var snapshot = provider.GetConnectionDiagnosticsSnapshot();
            snapshot.IsConnected.Should().BeFalse();
            snapshot.ReconnectAttempts.Should().Be(1);
            snapshot.LastFailureKind.Should().Be(
                ProviderFailureKind.AuthenticationOrAuthorizationFailure);
            provider.PollCount.Should().Be(2,
                "the supervisor must stop after the first non-retryable recovery attempt");
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disconnect_CancelsAndAwaitsAnActivePollingRecoveryProbe()
    {
        var recoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TestPollingProvider(
            _ => Task.FromResult(false),
            async ct =>
            {
                recoveryStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return true;
            });

        try
        {
            await provider.ConnectAsync();
            await recoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await provider.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

            var snapshot = provider.GetConnectionDiagnosticsSnapshot();
            snapshot.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
            snapshot.IsConnected.Should().BeFalse();
            snapshot.IsReconnecting.Should().BeFalse();
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class TestPollingProvider : PollingProviderBase
    {
        private readonly ConcurrentQueue<Func<CancellationToken, Task<bool>>> _polls;
        private int _pollCount;

        public TestPollingProvider(params Func<CancellationToken, Task<bool>>[] polls)
            : base(
                "test-polling",
                NullLogger.Instance,
                TimeSpan.FromMilliseconds(1),
                maxReconnectAttempts: 3,
                retryBaseDelay: TimeSpan.Zero,
                maxRetryDelay: TimeSpan.Zero)
        {
            _polls = new ConcurrentQueue<Func<CancellationToken, Task<bool>>>(polls);
        }

        public override bool IsEnabled => true;

        public int PollCount => Volatile.Read(ref _pollCount);

        protected override int ActiveSubscriptionCount => 1;

        protected override Task<bool> PollOnceAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _pollCount);
            return _polls.TryDequeue(out var poll)
                ? poll(ct)
                : Task.FromResult(true);
        }
    }
}
