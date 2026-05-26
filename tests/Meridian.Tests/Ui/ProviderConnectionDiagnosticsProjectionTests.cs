using System.Net.WebSockets;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Meridian.Ui.Shared.Endpoints;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ProviderConnectionDiagnosticsProjectionTests
{
    [Fact]
    public void BuildByProviderId_IndexesSafeWebSocketDiagnosticsByProviderIdentity()
    {
        var heartbeat = DateTimeOffset.UtcNow.AddSeconds(-15);
        var lastMessage = DateTimeOffset.UtcNow.AddSeconds(-10);
        var reconnect = DateTimeOffset.UtcNow.AddSeconds(-30);
        var provider = new DiagnosticProvider(
            "alpaca",
            "Alpaca Streaming",
            new WebSocketConnectionDiagnostics(
                ProviderName: "Alpaca",
                LifecycleState: ProviderConnectionLifecycleState.Reconnecting,
                WebSocketState: WebSocketState.Aborted,
                IsConnected: false,
                IsReconnecting: true,
                ReconnectAttempts: 3,
                LastConnectedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                LastDisconnectedAt: DateTimeOffset.UtcNow.AddSeconds(-40),
                LastHeartbeatReceivedAt: heartbeat,
                LastMessageReceivedAt: lastMessage,
                LastReconnectAttemptAt: reconnect,
                LastError: "provider-specific transport details stay out of the projection",
                LastFailureKind: ProviderFailureKind.ProviderRateLimit,
                ConnectionAge: null,
                IdleDuration: TimeSpan.FromSeconds(10),
                ActiveSubscriptions: 4,
                FailedSubscriptions: 1,
                RecoveringSubscriptions: 2,
                LastSubscriptionMessageAt: lastMessage));

        using var registry = new ProviderRegistry();
        registry.Register(provider);

        var diagnosticsByProviderId = ProviderConnectionDiagnosticsProjection.BuildByProviderId(registry);
        var diagnostics = ProviderConnectionDiagnosticsProjection.Find(
            diagnosticsByProviderId,
            "ALPACA",
            "Alpaca Streaming");

        diagnostics.Should().NotBeNull();
        diagnostics!.LifecycleState.Should().Be("Reconnecting");
        diagnostics.WebSocketState.Should().Be("Aborted");
        diagnostics.IsConnected.Should().BeFalse();
        diagnostics.IsReconnecting.Should().BeTrue();
        diagnostics.ReconnectAttempts.Should().Be(3);
        diagnostics.LastHeartbeatReceivedAt.Should().Be(heartbeat);
        diagnostics.LastMessageReceivedAt.Should().Be(lastMessage);
        diagnostics.LastReconnectAttemptAt.Should().Be(reconnect);
        diagnostics.LastFailureKind.Should().Be("ProviderRateLimit");
        diagnostics.ActiveSubscriptions.Should().Be(4);
        diagnostics.FailedSubscriptions.Should().Be(1);
        diagnostics.RecoveringSubscriptions.Should().Be(2);
        diagnostics.LastSubscriptionMessageAt.Should().Be(lastMessage);
    }

    private sealed class DiagnosticProvider : IProviderMetadata, IProviderConnectionDiagnosticsSource
    {
        private readonly WebSocketConnectionDiagnostics _diagnostics;

        public DiagnosticProvider(
            string providerId,
            string displayName,
            WebSocketConnectionDiagnostics diagnostics)
        {
            ProviderId = providerId;
            ProviderDisplayName = displayName;
            _diagnostics = diagnostics;
        }

        public string ProviderId { get; }

        public string ProviderDisplayName { get; }

        public string ProviderDescription => "Provider diagnostics projection test double.";

        public int ProviderPriority => 10;

        public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming();

        public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
        {
            add { }
            remove { }
        }

        public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot() => _diagnostics;
    }
}
