using FluentAssertions;
using System.Collections.Concurrent;
using Meridian.Application.Monitoring;
using Meridian.DataIntegration.Monitoring;
using Xunit;
using Meridian.Contracts.Monitoring;

namespace Meridian.Tests.DataIntegration.Monitoring;

public sealed class ConnectionStatusWebhookTests
{
    [Fact]
    public async Task MarkDisconnected_SendsConnectionLostMessageThroughMonitoringSink()
    {
        using var monitor = new ConnectionHealthMonitor(new ConnectionHealthConfig
        {
            HeartbeatIntervalSeconds = 300,
            HeartbeatTimeoutSeconds = 600
        });
        var sink = new RecordingMonitoringWebhookSink();

        await using var webhook = new ConnectionStatusWebhook(
            monitor,
            sink,
            new ConnectionStatusWebhookConfig { MinAlertIntervalSeconds = 0 });

        monitor.RegisterConnection("fixture-connection", "FixtureProvider");

        monitor.MarkDisconnected("fixture-connection", "network outage");

        await WaitForMessagesAsync(sink, expectedCount: 1);
        sink.Messages.Should().ContainSingle(message =>
            message.Title == "Connection Lost"
            && message.Message.Contains("FixtureProvider", StringComparison.Ordinal)
            && message.Message.Contains("network outage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendStatusUpdateAsync_SendsManualMessageThroughMonitoringSink()
    {
        using var monitor = new ConnectionHealthMonitor(new ConnectionHealthConfig
        {
            HeartbeatIntervalSeconds = 300,
            HeartbeatTimeoutSeconds = 600
        });
        var sink = new RecordingMonitoringWebhookSink();

        await using var webhook = new ConnectionStatusWebhook(monitor, sink);

        await webhook.SendStatusUpdateAsync("manual provider status", "Provider Status");

        sink.Messages.Should().ContainSingle(message =>
            message.Title == "Provider Status"
            && message.Message == "manual provider status");
    }

    [Fact]
    public async Task EventNotifications_AreDispatchedSequentiallyInEventOrder()
    {
        using var monitor = CreateMonitor();
        var sink = new BlockingMonitoringWebhookSink();

        await using var webhook = new ConnectionStatusWebhook(
            monitor,
            sink,
            new ConnectionStatusWebhookConfig { MinAlertIntervalSeconds = 0 });

        monitor.RegisterConnection("ordered-connection", "OrderedProvider");
        monitor.MarkDisconnected("ordered-connection", "network outage");
        await sink.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        monitor.MarkConnected("ordered-connection");
        await Task.Delay(100);

        sink.StartedCount.Should().Be(1, "the second notification must wait for the first delivery");
        sink.MaximumConcurrentSends.Should().Be(1);

        sink.ReleaseFirstSend();
        await WaitForMessagesAsync(sink, expectedCount: 2);

        sink.MaximumConcurrentSends.Should().Be(1);
        sink.Messages.Select(message => message.Title).Should().ContainInOrder(
            "Connection Lost",
            "Connection Recovered");
    }

    [Fact]
    public async Task DisposeAsync_WaitsForQueuedDeliveryToDrain()
    {
        using var monitor = CreateMonitor();
        var sink = new BlockingMonitoringWebhookSink();
        var webhook = new ConnectionStatusWebhook(
            monitor,
            sink,
            new ConnectionStatusWebhookConfig { MinAlertIntervalSeconds = 0 });

        monitor.RegisterConnection("drain-connection", "DrainProvider");
        monitor.MarkDisconnected("drain-connection", "network outage");
        await sink.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposeTask = webhook.DisposeAsync().AsTask();
        await Task.Delay(100);
        disposeTask.IsCompleted.Should().BeFalse("shutdown must join an in-flight delivery");

        sink.ReleaseFirstSend();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        sink.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesBeforeNewEventsCanBeQueued()
    {
        using var monitor = CreateMonitor();
        var sink = new RecordingMonitoringWebhookSink();
        var webhook = new ConnectionStatusWebhook(
            monitor,
            sink,
            new ConnectionStatusWebhookConfig { MinAlertIntervalSeconds = 0 });

        monitor.RegisterConnection("disposed-connection", "DisposedProvider");
        await webhook.DisposeAsync();

        monitor.MarkDisconnected("disposed-connection", "after shutdown");
        await Task.Delay(100);

        sink.Messages.Should().BeEmpty();
    }

    private static ConnectionHealthMonitor CreateMonitor()
        => new(new ConnectionHealthConfig
        {
            HeartbeatIntervalSeconds = 300,
            HeartbeatTimeoutSeconds = 600
        });

    private static async Task WaitForMessagesAsync(IMonitoringWebhookRecorder sink, int expectedCount)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (sink.Messages.Count >= expectedCount)
                return;

            await Task.Delay(25);
        }
    }

    private interface IMonitoringWebhookRecorder
    {
        IReadOnlyCollection<(string Message, string? Title)> Messages { get; }
    }

    private sealed class RecordingMonitoringWebhookSink : IMonitoringWebhookSink, IMonitoringWebhookRecorder
    {
        private readonly ConcurrentQueue<(string Message, string? Title)> _messages = new();

        public IReadOnlyCollection<(string Message, string? Title)> Messages => _messages.ToArray();

        public Task SendMonitoringMessageAsync(string message, string? title = null, CancellationToken ct = default)
        {
            _messages.Enqueue((message, title));
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingMonitoringWebhookSink : IMonitoringWebhookSink, IMonitoringWebhookRecorder
    {
        private readonly ConcurrentQueue<(string Message, string? Title)> _messages = new();
        private readonly TaskCompletionSource _releaseFirstSend =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeSends;
        private int _maximumConcurrentSends;
        private int _startedCount;

        public TaskCompletionSource FirstSendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<(string Message, string? Title)> Messages => _messages.ToArray();

        public int StartedCount => Volatile.Read(ref _startedCount);

        public int MaximumConcurrentSends => Volatile.Read(ref _maximumConcurrentSends);

        public async Task SendMonitoringMessageAsync(
            string message,
            string? title = null,
            CancellationToken ct = default)
        {
            var started = Interlocked.Increment(ref _startedCount);
            var active = Interlocked.Increment(ref _activeSends);
            UpdateMaximum(active);

            try
            {
                if (started == 1)
                {
                    FirstSendStarted.TrySetResult();
                    await _releaseFirstSend.Task.WaitAsync(ct);
                }

                _messages.Enqueue((message, title));
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }

        public void ReleaseFirstSend() => _releaseFirstSend.TrySetResult();

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximumConcurrentSends);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _maximumConcurrentSends, candidate, current);
                if (observed == current)
                    return;

                current = observed;
            }
        }
    }
}
