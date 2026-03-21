using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Simplified status for display in the desktop UI.
/// Includes provenance and staleness tracking (P0: explicit staleness + source provenance).
/// </summary>
public sealed class SimpleStatus
{
    public long Published { get; set; }
    public long Dropped { get; set; }
    public long Integrity { get; set; }
    public long Historical { get; set; }
    public StatusProviderInfo? Provider { get; set; }

    /// <summary>
    /// UTC timestamp when this status snapshot was retrieved from the backend.
    /// </summary>
    public DateTime? RetrievedAtUtc { get; set; }

    /// <summary>
    /// Name of the data source provider that produced these metrics.
    /// Null if sourced from fixture/offline data.
    /// </summary>
    public string? SourceProvider { get; set; }

    /// <summary>
    /// Whether this status data is considered stale (backend reported stale or too old).
    /// </summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// Age of the data in seconds since last backend update.
    /// </summary>
    public double AgeSeconds { get; set; }

    /// <summary>
    /// Data provenance: "live", "cached", "fixture", or "offline".
    /// Indicates the origin of the data being displayed.
    /// </summary>
    public string DataProvenance { get; set; } = "unknown";
}

/// <summary>
/// Provider status information.
/// </summary>
public sealed class StatusProviderInfo
{
    public string? ActiveProvider { get; set; }
    public bool IsConnected { get; set; }
    public int ConnectionCount { get; set; }
    public DateTimeOffset? LastHeartbeat { get; set; }
    public IReadOnlyList<string> AvailableProviders { get; set; } = new List<string>();

    public string DisplayStatus => IsConnected
        ? $"Connected to {ActiveProvider ?? "Unknown"}"
        : "Disconnected";
}

/// <summary>
/// Provider information for display.
/// </summary>
public sealed class ProviderInfo
{
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public bool RequiresCredentials { get; set; }
}

/// <summary>
/// Event arguments for status change events.
/// </summary>
public sealed class StatusChangedEventArgs : EventArgs
{
    public string PreviousStatus { get; }
    public string NewStatus { get; }
    public DateTime Timestamp { get; }

    public StatusChangedEventArgs(string previousStatus, string newStatus)
    {
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments for live status monitoring updates.
/// </summary>
public sealed class LiveStatusEventArgs : EventArgs
{
    public SimpleStatus? Status { get; init; }
    public DateTime Timestamp { get; init; }
    public bool IsStale { get; init; }
}

/// <summary>
/// API status response model.
/// </summary>
internal sealed class ApiStatusResponse
{
    public ApiMetrics? Metrics { get; set; }
}

/// <summary>
/// API metrics model.
/// </summary>
internal sealed class ApiMetrics
{
    public long Published { get; set; }
    public long Dropped { get; set; }
    public long Integrity { get; set; }
    public long HistoricalBars { get; set; }
}

/// <summary>
/// Provider status API response model.
/// </summary>
internal sealed class ProviderStatusResponse
{
    public string? ActiveProvider { get; set; }
    public bool IsConnected { get; set; }
    public int ConnectionCount { get; set; }
    public DateTimeOffset? LastHeartbeat { get; set; }
    public List<string>? AvailableProviders { get; set; }
}

/// <summary>
/// Abstract base class for status management shared between platforms.
/// Provides shared status tracking, live monitoring loop, and API polling logic.
/// Platform-specific HTTP client and logging are delegated to derived classes.
/// Part of Phase 2 service extraction.
/// </summary>
public abstract class StatusServiceBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string _currentStatus = "Ready";
    private readonly object _lock = new();
    private string _baseUrl = "http://localhost:8080";
    private CancellationTokenSource? _monitoringCts;
    private DateTime? _lastSuccessfulUpdate;
    private bool _isBackendReachable = true;

    public string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value;
    }

    public string CurrentStatus
    {
        get
        {
            lock (_lock) { return _currentStatus; }
        }
    }

    public DateTime? LastSuccessfulUpdate => _lastSuccessfulUpdate;
    public bool IsBackendReachable => _isBackendReachable;

    public double? SecondsSinceLastUpdate =>
        _lastSuccessfulUpdate.HasValue
            ? (DateTime.UtcNow - _lastSuccessfulUpdate.Value).TotalSeconds
            : null;

    public bool IsDataStale =>
        !_lastSuccessfulUpdate.HasValue || (DateTime.UtcNow - _lastSuccessfulUpdate.Value).TotalSeconds > 10;

    public bool IsMonitoring => _monitoringCts != null && !_monitoringCts.IsCancellationRequested;

    public event EventHandler<StatusChangedEventArgs>? StatusChanged;
    public event EventHandler<LiveStatusEventArgs>? LiveStatusReceived;
    public event EventHandler<bool>? BackendReachabilityChanged;

    /// <summary>
    /// Gets the HTTP client to use for status polling.
    /// </summary>
    protected abstract HttpClient GetHttpClient();

    /// <summary>
    /// Logs an informational message with structured properties.
    /// </summary>
    protected abstract void LogInfo(string message, params (string key, string value)[] properties);

    public void StartLiveMonitoring(int intervalSeconds = 2)
    {
        StopLiveMonitoring();
        _monitoringCts = new CancellationTokenSource();
        _ = RunLiveMonitoringLoopAsync(intervalSeconds, _monitoringCts.Token);
    }

    public void StopLiveMonitoring()
    {
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = null;
    }

    private async Task RunLiveMonitoringLoopAsync(int intervalSeconds, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var status = await GetStatusAsync(ct);

                if (status != null)
                {
                    _lastSuccessfulUpdate = DateTime.UtcNow;
                    SetBackendReachable(true);
                    LiveStatusReceived?.Invoke(this, new LiveStatusEventArgs
                    {
                        Status = status,
                        Timestamp = DateTime.UtcNow,
                        IsStale = false
                    });
                }
                else
                {
                    SetBackendReachable(false);
                    LiveStatusReceived?.Invoke(this, new LiveStatusEventArgs
                    {
                        Status = null,
                        Timestamp = DateTime.UtcNow,
                        IsStale = IsDataStale
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { SetBackendReachable(false); LogInfo("Live monitoring error", ("error", ex.Message)); }

            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void SetBackendReachable(bool reachable)
    {
        if (_isBackendReachable != reachable)
        {
            _isBackendReachable = reachable;
            BackendReachabilityChanged?.Invoke(this, reachable);

            // Update fixture mode detector so the global banner reflects connectivity
            FixtureModeDetector.Instance.UpdateBackendReachability(reachable);
        }
    }

    public async Task<SimpleStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var httpClient = GetHttpClient();
            var statusTask = httpClient.GetAsync($"{_baseUrl}/api/status", cts.Token);
            var providerTask = GetProviderStatusAsync(cts.Token);

            await Task.WhenAll(statusTask, providerTask);

            var response = await statusTask;
            var providerInfo = await providerTask;

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var apiStatus = JsonSerializer.Deserialize<ApiStatusResponse>(json, _jsonOptions);

                if (apiStatus?.Metrics != null)
                {
                    var now = DateTime.UtcNow;
                    var activeProvider = providerInfo?.ActiveProvider;
                    var isFixtureMode = FixtureModeDetector.Instance.IsFixtureMode;

                    return new SimpleStatus
                    {
                        Published = apiStatus.Metrics.Published,
                        Dropped = apiStatus.Metrics.Dropped,
                        Integrity = apiStatus.Metrics.Integrity,
                        Historical = apiStatus.Metrics.HistoricalBars,
                        Provider = providerInfo,
                        RetrievedAtUtc = now,
                        SourceProvider = activeProvider,
                        IsStale = false, // Just retrieved, not stale
                        AgeSeconds = 0,
                        DataProvenance = isFixtureMode ? "fixture" : "live"
                    };
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException ex) { LogInfo("Status endpoint unreachable", ("error", ex.Message)); }
        catch (JsonException ex) { LogInfo("Status response parse failed", ("error", ex.Message)); }
        catch (UriFormatException ex) { LogInfo("Status request URI invalid", ("error", ex.Message)); }
        catch (InvalidOperationException ex) { LogInfo("Status request failed", ("error", ex.Message)); }

        return null;
    }

    public async Task<StatusProviderInfo?> GetProviderStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var httpClient = GetHttpClient();
            var response = await httpClient.GetAsync($"{_baseUrl}/api/providers/status", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var providerStatus = JsonSerializer.Deserialize<ProviderStatusResponse>(json, _jsonOptions);

                if (providerStatus != null)
                {
                    return new StatusProviderInfo
                    {
                        ActiveProvider = providerStatus.ActiveProvider,
                        IsConnected = providerStatus.IsConnected,
                        ConnectionCount = providerStatus.ConnectionCount,
                        LastHeartbeat = providerStatus.LastHeartbeat,
                        AvailableProviders = providerStatus.AvailableProviders ?? new List<string>()
                    };
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException ex) { LogInfo("Provider status endpoint unreachable", ("error", ex.Message)); }
        catch (JsonException ex) { LogInfo("Provider status response parse failed", ("error", ex.Message)); }
        catch (UriFormatException ex) { LogInfo("Provider status request URI invalid", ("error", ex.Message)); }
        catch (InvalidOperationException ex) { LogInfo("Provider status request failed", ("error", ex.Message)); }

        return null;
    }

    public async Task<IReadOnlyList<ProviderInfo>> GetAvailableProvidersAsync(CancellationToken ct = default)
    {
        try
        {
            var httpClient = GetHttpClient();
            var response = await httpClient.GetAsync($"{_baseUrl}/api/providers/catalog", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var providers = JsonSerializer.Deserialize<List<ProviderInfo>>(json, _jsonOptions);
                return providers ?? new List<ProviderInfo>();
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException ex) { LogInfo("Providers catalog endpoint unreachable", ("error", ex.Message)); }
        catch (JsonException ex) { LogInfo("Providers catalog response parse failed", ("error", ex.Message)); }

        return new List<ProviderInfo>();
    }

    public void UpdateStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);

        string previousStatus;

        lock (_lock)
        {
            if (_currentStatus == status) return;
            previousStatus = _currentStatus;
            _currentStatus = status;
        }

        LogInfo("Status changed", ("PreviousStatus", previousStatus), ("NewStatus", status));
        StatusChanged?.Invoke(this, new StatusChangedEventArgs(previousStatus, status));
    }

    public void SetBusy(string operation) => UpdateStatus($"Working: {operation}...");
    public void SetReady() => UpdateStatus("Ready");
    public void SetError(string errorMessage) => UpdateStatus($"Error: {errorMessage}");

    public void SetConnectionStatus(bool isConnected, string? providerName = null)
    {
        if (isConnected)
        {
            UpdateStatus(string.IsNullOrEmpty(providerName) ? "Connected" : $"Connected to {providerName}");
        }
        else
        {
            UpdateStatus(string.IsNullOrEmpty(providerName) ? "Disconnected" : $"Disconnected from {providerName}");
        }
    }
}
