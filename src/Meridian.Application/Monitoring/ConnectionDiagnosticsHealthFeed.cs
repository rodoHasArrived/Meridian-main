using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Feeds a <see cref="ConnectionHealthMonitor"/> from a streaming client's transport diagnostics,
/// for a collector running without streaming failover (which feeds its own monitor).
/// </summary>
/// <remarks>
/// <para>Nothing here runs per market event. The feed applies each
/// <see cref="IProviderConnectionDiagnosticsSource.ConnectionDiagnosticsChanged"/> notification and
/// also polls <see cref="IProviderConnectionDiagnosticsSource.GetConnectionDiagnosticsSnapshot"/>
/// on a timer shorter than the monitor's heartbeat timeout: a connected transport is a heartbeat,
/// and a transport that reports <c>Failed</c> or <c>Disconnected</c> marks the connection lost.</para>
/// <para>Only clients that supervise their own lifecycle are tracked. The SDK's default
/// diagnostics never claim a live connection, so registering such a client would let the monitor
/// time it out and report a streaming provider as disconnected; <see cref="TryStart"/> returns null
/// for it instead, and the connection is simply not listed.</para>
/// </remarks>
public sealed class ConnectionDiagnosticsHealthFeed : IDisposable
{
    /// <summary>Default poll interval: a quarter of the monitor's default 60-second heartbeat timeout.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    private readonly IMarketDataClient _client;
    private readonly ConnectionHealthMonitor _monitor;
    private readonly string _connectionId;
    private readonly Timer _poll;
    private readonly object _gate = new();
    private bool _disposed;

    private ConnectionDiagnosticsHealthFeed(
        IMarketDataClient client,
        ConnectionHealthMonitor monitor,
        string connectionId,
        string providerName,
        WebSocketConnectionDiagnostics initial,
        TimeSpan pollInterval)
    {
        _client = client;
        _monitor = monitor;
        _connectionId = connectionId;

        _monitor.RegisterConnection(connectionId, providerName);
        Apply(initial);
        _client.ConnectionDiagnosticsChanged += Apply;
        _poll = new Timer(_ => Poll(), null, pollInterval, pollInterval);
    }

    /// <summary>
    /// Starts feeding <paramref name="monitor"/> for <paramref name="client"/>, or returns null when
    /// the client does not supervise its connection lifecycle and so cannot be monitored truthfully.
    /// Call after the client has connected.
    /// </summary>
    public static ConnectionDiagnosticsHealthFeed? TryStart(
        IMarketDataClient client,
        ConnectionHealthMonitor monitor,
        string connectionId,
        string providerName,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var snapshot = client.GetConnectionDiagnosticsSnapshot();
        if (!ReportsLiveLifecycle(snapshot))
            return null;

        return new ConnectionDiagnosticsHealthFeed(
            client, monitor, connectionId, string.IsNullOrWhiteSpace(providerName) ? connectionId : providerName,
            snapshot, pollInterval ?? DefaultPollInterval);
    }

    /// <summary>
    /// True when a connected client's diagnostics come from a lifecycle supervisor rather than the
    /// SDK default, which only ever reports <c>Configured</c>, <c>Disabled</c> or <c>NotConfigured</c>.
    /// </summary>
    internal static bool ReportsLiveLifecycle(WebSocketConnectionDiagnostics snapshot)
        => snapshot.LifecycleState is not (ProviderConnectionLifecycleState.NotConfigured
            or ProviderConnectionLifecycleState.Configured
            or ProviderConnectionLifecycleState.Disabled);

    private void Poll()
    {
        try
        {
            Apply(_client.GetConnectionDiagnosticsSnapshot());
        }
        catch (ObjectDisposedException)
        {
            // The client is shutting down; the collector disposes this feed next.
        }
    }

    private void Apply(WebSocketConnectionDiagnostics snapshot)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (snapshot.IsConnected)
            {
                _monitor.MarkConnected(_connectionId);
                _monitor.RecordHeartbeat(_connectionId);
            }
            else if (snapshot.LifecycleState is ProviderConnectionLifecycleState.Failed
                     or ProviderConnectionLifecycleState.Disconnected)
            {
                _monitor.MarkDisconnected(_connectionId, string.IsNullOrWhiteSpace(snapshot.LastError)
                    ? $"Transport reported {snapshot.LifecycleState}"
                    : $"Transport reported {snapshot.LifecycleState}: {snapshot.LastError}");
            }

            // Connecting, Reconnecting and Degraded are transient: the monitor's own heartbeat
            // timeout reports a connection that stays in them too long.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _client.ConnectionDiagnosticsChanged -= Apply;
        _poll.Dispose();
    }
}
