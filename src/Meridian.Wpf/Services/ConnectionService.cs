using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Timer = System.Timers.Timer;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF-specific connection service that extends <see cref="ConnectionServiceBase"/> with
/// System.Timers.Timer for monitoring and HttpClient for health checks.
/// Implements <see cref="IConnectionService"/> with singleton pattern.
/// Phase 6C.2: Shared base class extracts state management, event raising, and reconnect logic.
/// </summary>
public sealed class ConnectionService : ConnectionServiceBase, IConnectionService
{
    private static readonly Lazy<ConnectionService> _instance = new(() => new ConnectionService());

    private readonly IRemoteWorkstationClient _remoteClient;
    private Timer? _monitoringTimer;
    private Timer? _reconnectTimer;

    /// <summary>
    /// Gets the singleton instance of the ConnectionService.
    /// </summary>
    public static ConnectionService Instance => _instance.Value;

    private ConnectionService()
        : this(WpfRemoteWorkstationClient.Instance)
    {
    }

    internal ConnectionService(IRemoteWorkstationClient remoteClient)
    {
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
    }

    /// <inheritdoc />
    protected override async Task<bool> PerformHealthCheckCoreAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        return await _remoteClient.CheckHealthEndpointAsync(cts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void StartMonitoringTimer(int intervalMs)
    {
        _monitoringTimer = new Timer(intervalMs);
        _monitoringTimer.Elapsed += OnMonitoringTimerElapsed;
        _monitoringTimer.AutoReset = true;
        _monitoringTimer.Start();
    }

    /// <inheritdoc />
    protected override void StopMonitoringTimer()
    {
        if (_monitoringTimer != null)
        {
            _monitoringTimer.Stop();
            _monitoringTimer.Elapsed -= OnMonitoringTimerElapsed;
            _monitoringTimer.Dispose();
            _monitoringTimer = null;
        }
    }

    /// <inheritdoc />
    protected override void StartReconnectTimer(int delayMs)
    {
        if (_reconnectTimer != null)
            return;

        _reconnectTimer = new Timer(delayMs);
        _reconnectTimer.Elapsed += OnReconnectTimerElapsed;
        _reconnectTimer.AutoReset = false;
        _reconnectTimer.Start();
    }

    /// <inheritdoc />
    protected override void StopAutoReconnect()
    {
        if (_reconnectTimer != null)
        {
            _reconnectTimer.Stop();
            _reconnectTimer.Elapsed -= OnReconnectTimerElapsed;
            _reconnectTimer.Dispose();
            _reconnectTimer = null;
        }
    }

    /// <inheritdoc />
    protected override void OnSettingsUpdated(ConnectionSettings settings)
    {
        _remoteClient.Configure(settings.ServiceUrl, settings.ServiceTimeoutSeconds);
    }

    /// <inheritdoc />
    protected override void LogOperation(string message, params (string Key, string Value)[] properties)
    {
        LoggingService.Instance.LogInfo(message, properties);
    }

    /// <inheritdoc />
    protected override void LogWarning(string message, params (string Key, string Value)[] properties)
    {
        LoggingService.Instance.LogWarning(message, properties);
    }

    /// <inheritdoc />
    protected override void DisposePlatformResources()
    {
    }

    private async void OnMonitoringTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await OnMonitoringTimerFired();
    }

    private async void OnReconnectTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await OnReconnectTimerFired();
    }
}
