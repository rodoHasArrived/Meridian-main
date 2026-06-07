using FluentAssertions;
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

    private static async Task WaitForMessagesAsync(RecordingMonitoringWebhookSink sink, int expectedCount)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (sink.Messages.Count >= expectedCount)
                return;

            await Task.Delay(25);
        }
    }

    private sealed class RecordingMonitoringWebhookSink : IMonitoringWebhookSink
    {
        private readonly List<(string Message, string? Title)> _messages = new();

        public IReadOnlyList<(string Message, string? Title)> Messages => _messages;

        public Task SendMonitoringMessageAsync(string message, string? title = null, CancellationToken ct = default)
        {
            _messages.Add((message, title));
            return Task.CompletedTask;
        }
    }
}
