using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Pipeline;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Resilience;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Connection health for a collector without failover: the feed tracks a client only when the
/// client reports a real lifecycle, follows its transport diagnostics, and the API host serves the
/// collector's monitor through <see cref="ConnectionHealthRegistry"/> or reports "not configured".
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConnectionDiagnosticsHealthFeedTests
{
    private static readonly TimeSpan NoPolling = Timeout.InfiniteTimeSpan;

    [Fact]
    public void AClientWithOnlyTheSdkDefaultDiagnostics_IsNotTracked()
    {
        // The default snapshot never claims a live connection; tracking it would let the monitor
        // time the connection out and report a streaming provider as disconnected.
        using var monitor = new ConnectionHealthMonitor();
        var client = new DiagnosticsClient(ProviderConnectionLifecycleState.Configured);

        var feed = ConnectionDiagnosticsHealthFeed.TryStart(client, monitor, "alpaca", "Alpaca", NoPolling);

        feed.Should().BeNull();
        monitor.GetSnapshot().TotalConnections.Should().Be(0);
    }

    [Fact]
    public void AConnectedClient_IsRegisteredHealthy_AndFollowsItsTransport()
    {
        using var monitor = new ConnectionHealthMonitor();
        var client = new DiagnosticsClient(ProviderConnectionLifecycleState.Connected);

        using var feed = ConnectionDiagnosticsHealthFeed.TryStart(client, monitor, "alpaca", "Alpaca", NoPolling);
        feed.Should().NotBeNull();
        Status(monitor).IsConnected.Should().BeTrue();
        Status(monitor).IsHealthy.Should().BeTrue();

        client.Raise(ProviderConnectionLifecycleState.Failed, "socket closed");
        Status(monitor).IsConnected.Should().BeFalse("a transport that reports Failed has lost its connection");

        client.Raise(ProviderConnectionLifecycleState.Connected);
        Status(monitor).IsConnected.Should().BeTrue("a reconnected transport is connected again");
    }

    [Fact]
    public void TransientStates_LeaveTheConnectionAsItWas()
    {
        using var monitor = new ConnectionHealthMonitor();
        var client = new DiagnosticsClient(ProviderConnectionLifecycleState.Connected);
        using var feed = ConnectionDiagnosticsHealthFeed.TryStart(client, monitor, "alpaca", "Alpaca", NoPolling);

        client.Raise(ProviderConnectionLifecycleState.Reconnecting);

        Status(monitor).IsConnected.Should().BeTrue(
            "reconnecting is transient; the monitor's heartbeat timeout reports one that lasts too long");
    }

    [Fact]
    public void DisposingTheFeed_StopsFollowingTheClient()
    {
        using var monitor = new ConnectionHealthMonitor();
        var client = new DiagnosticsClient(ProviderConnectionLifecycleState.Connected);
        var feed = ConnectionDiagnosticsHealthFeed.TryStart(client, monitor, "alpaca", "Alpaca", NoPolling)!;

        feed.Dispose();
        client.Raise(ProviderConnectionLifecycleState.Failed);

        client.SubscriberCount.Should().Be(0);
        Status(monitor).IsConnected.Should().BeTrue("the feed no longer applies the client's notifications");
    }

    [Fact]
    public void TheRegistry_PublishesUntilDisposed_AndAStaleHandleDoesNotClearANewerMonitor()
    {
        var registry = new ConnectionHealthRegistry();
        using var first = new ConnectionHealthMonitor();
        using var second = new ConnectionHealthMonitor();

        var firstPublication = registry.Publish(first);
        registry.Monitor.Should().BeSameAs(first);

        using (registry.Publish(second))
        {
            firstPublication.Dispose();
            registry.Monitor.Should().BeSameAs(second, "withdrawing the first monitor must not remove its replacement");
        }

        registry.Monitor.Should().BeNull();
    }

    [Fact]
    public void TheHandler_ReportsNotConfigured_WhileNoMonitorIsPublished_AndServesOneThatIs()
    {
        var registry = new ConnectionHealthRegistry();
        var handlers = new StatusEndpointHandlers(
            () => new MetricsSnapshot(), () => new PipelineStatistics(), () => Array.Empty<DepthIntegrityEvent>());
        handlers.RegisterExtendedProviders(connectionHealth: () => registry.Monitor?.GetSnapshot());

        handlers.GetConnectionHealth().Error.Should().Be("Connection health monitoring not configured");

        using var monitor = new ConnectionHealthMonitor();
        monitor.RegisterConnection("alpaca", "Alpaca");
        using (registry.Publish(monitor))
        {
            var (snapshot, error) = handlers.GetConnectionHealth();
            error.Should().BeNull();
            snapshot!.TotalConnections.Should().Be(1);
        }

        handlers.GetConnectionHealth().Error.Should().Be("Connection health monitoring not configured");
    }

    private static ConnectionStatus Status(ConnectionHealthMonitor monitor)
        => monitor.GetSnapshot().Connections.Should().ContainSingle().Subject;

    private sealed class DiagnosticsClient(ProviderConnectionLifecycleState state) : IMarketDataClient
    {
        private ProviderConnectionLifecycleState _state = state;
        private string? _lastError;
        private Action<WebSocketConnectionDiagnostics>? _changed;

        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public bool IsEnabled => true;
        public string ProviderId => "alpaca";
        public string ProviderDisplayName => "Alpaca";
        public string ProviderDescription => "Test client";
        public int ProviderPriority => 1;
        public Meridian.Infrastructure.Adapters.Core.ProviderCapabilities ProviderCapabilities
            => Meridian.Infrastructure.Adapters.Core.ProviderCapabilities.Streaming();

        public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void Raise(ProviderConnectionLifecycleState state, string? error = null)
        {
            _state = state;
            _lastError = error;
            _changed?.Invoke(GetConnectionDiagnosticsSnapshot());
        }

        public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
            => new(
                ProviderName: ProviderDisplayName,
                LifecycleState: _state,
                WebSocketState: System.Net.WebSockets.WebSocketState.None,
                IsConnected: _state == ProviderConnectionLifecycleState.Connected,
                IsReconnecting: _state == ProviderConnectionLifecycleState.Reconnecting,
                ReconnectAttempts: 0,
                LastConnectedAt: null,
                LastDisconnectedAt: null,
                LastHeartbeatReceivedAt: null,
                LastMessageReceivedAt: null,
                LastReconnectAttemptAt: null,
                LastError: _lastError,
                LastFailureKind: null,
                ConnectionAge: null,
                IdleDuration: null);

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public int SubscribeMarketDepth(Meridian.Contracts.Configuration.SymbolConfig cfg) => 1;
        public void UnsubscribeMarketDepth(int subscriptionId) { }
        public int SubscribeTrades(Meridian.Contracts.Configuration.SymbolConfig cfg) => 1;
        public void UnsubscribeTrades(int subscriptionId) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
