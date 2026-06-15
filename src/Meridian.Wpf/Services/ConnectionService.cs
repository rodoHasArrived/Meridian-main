using System;
using System.Net.Http;
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

    private readonly SemaphoreSlim _healthCheckGate = new(1, 1);
    private readonly object _httpClientLock = new();
    private HttpClient _httpClient;
    private Timer? _monitoringTimer;
    private Timer? _reconnectTimer;
    private CancellationTokenSource _monitoringCts = new();
    private CancellationTokenSource _reconnectCts = new();

    /// <summary>
    /// Gets the singleton instance of the ConnectionService.
    /// </summary>
    public static ConnectionService Instance => _instance.Value;

    private ConnectionService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(GetSettings().ServiceTimeoutSeconds)
        };
    }

    /// <inheritdoc />
    protected override async Task<bool> PerformHealthCheckCoreAsync(CancellationToken ct)
    {
        await _healthCheckGate.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            HttpClient client;
            lock (_httpClientLock)
            {
                client = _httpClient;
            }

            var response = await client.GetAsync($"{ServiceUrl}/healthz", cts.Token);
            return response.IsSuccessStatusCode;
        }
        finally
        {
            _healthCheckGate.Release();
        }
    }

    /// <inheritdoc />
    protected override void StartMonitoringTimer(int intervalMs)
    {
        _monitoringCts.Dispose();
        _monitoringCts = new CancellationTokenSource();
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
            _monitoringCts.Cancel();
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

        _reconnectCts.Dispose();
        _reconnectCts = new CancellationTokenSource();
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
            _reconnectCts.Cancel();
            _reconnectTimer.Stop();
            _reconnectTimer.Elapsed -= OnReconnectTimerElapsed;
            _reconnectTimer.Dispose();
            _reconnectTimer = null;
        }
    }

    /// <inheritdoc />
    protected override void OnSettingsUpdated(ConnectionSettings settings)
    {
        _healthCheckGate.Wait();
        try
        {
            var old = _httpClient;
            lock (_httpClientLock)
            {
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(settings.ServiceTimeoutSeconds)
                };
            }
            old.Dispose();
        }
        finally
        {
            _healthCheckGate.Release();
        }
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
        _monitoringCts.Cancel();
        _reconnectCts.Cancel();
        _healthCheckGate.Wait();
        try
        {
            _httpClient.Dispose();
        }
        finally
        {
            _healthCheckGate.Release();
            _healthCheckGate.Dispose();
            _monitoringCts.Dispose();
            _reconnectCts.Dispose();
        }
    }

    private void OnMonitoringTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _ = OnMonitoringTimerFired(_monitoringCts.Token);
    }

    private void OnReconnectTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _ = OnReconnectTimerFired(_reconnectCts.Token);
    }
}
