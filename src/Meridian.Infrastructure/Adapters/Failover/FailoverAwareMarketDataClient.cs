using System.Collections.Concurrent;
using System.Net.WebSockets;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Serilog;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.Failover;

/// <summary>
/// A composite <see cref="IMarketDataClient"/> that wraps multiple provider clients and
/// automatically switches between them based on failover rules managed by
/// <see cref="StreamingFailoverService"/>.
/// </summary>
/// <remarks>
/// This client is transparent to callers — it implements <see cref="IMarketDataClient"/>
/// and delegates to whichever provider is currently active. When a failover event occurs,
/// it disconnects the failed provider, connects the new one, and re-subscribes all active
/// symbols.
/// </remarks>
[DataSource("failover", "Failover-Aware Streaming Client", DataSourceType.Realtime, DataSourceCategory.Aggregator,
    Priority = 50, EnabledByDefault = false, Description = "Composite failover client that switches between providers on failure")]
[ImplementsAdr("ADR-001", "Failover-aware composite streaming client")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class FailoverAwareMarketDataClient : IMarketDataClient
{
    private readonly ILogger _log = LoggingSetup.ForContext<FailoverAwareMarketDataClient>();
    private readonly Dictionary<string, IMarketDataClient> _providers;
    private readonly StreamingFailoverService _failoverService;
    private readonly string _ruleId;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _diagnosticsSync = new();
    private readonly Dictionary<string, Action<WebSocketConnectionDiagnostics>> _providerDiagnosticsHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile IMarketDataClient _activeClient;
    private volatile string _activeProviderId;
    private ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
    private bool _isConnected;
    private bool _isReconnecting;
    private DateTimeOffset? _lastConnectedAt;
    private DateTimeOffset? _lastDisconnectedAt;
    private string? _lastError;
    private ProviderFailureKind? _lastFailureKind;
    private readonly ConcurrentDictionary<string, SymbolConfig> _activeDepthSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SymbolConfig> _activeTradeSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _depthSubIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _tradeSubIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _depthSubscriberCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _tradeSubscriberCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new failover-aware client.
    /// </summary>
    /// <param name="providers">Map of provider ID to client instance.</param>
    /// <param name="failoverService">The failover orchestrator.</param>
    /// <param name="ruleId">The failover rule ID this client corresponds to.</param>
    /// <param name="initialProviderId">The provider ID to start with (typically the primary).</param>
    public FailoverAwareMarketDataClient(
        Dictionary<string, IMarketDataClient> providers,
        StreamingFailoverService failoverService,
        string ruleId,
        string initialProviderId)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = NormalizeProviders(providers);
        _failoverService = failoverService ?? throw new ArgumentNullException(nameof(failoverService));
        _ruleId = ruleId;

        var initialKey = ProviderIdentity.NormalizeId(initialProviderId);
        if (!_providers.TryGetValue(initialKey, out var initial))
            throw new ArgumentException($"Initial provider '{initialKey}' not found in provider map.", nameof(initialProviderId));

        _activeClient = initial;
        _activeProviderId = initialKey;

        _failoverService.OnFailoverTriggered += HandleFailoverTriggered;
        _failoverService.OnFailoverRecovered += HandleFailoverRecovered;
        foreach (var (providerId, client) in _providers)
        {
            var capturedProviderId = providerId;
            var capturedClient = client;
            Action<WebSocketConnectionDiagnostics> handler = snapshot =>
                HandleProviderDiagnosticsChanged(capturedProviderId, capturedClient, snapshot);
            _providerDiagnosticsHandlers[providerId] = handler;
            client.ConnectionDiagnosticsChanged += handler;
        }

        _log.Information("FailoverAwareMarketDataClient initialized with {ProviderCount} providers, active: {ActiveProvider}, rule: {RuleId}",
            _providers.Count, _activeProviderId, _ruleId);
    }


    public bool IsEnabled => _activeClient.IsEnabled;

    /// <inheritdoc />
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <inheritdoc />
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var activeClient = _activeClient;
        var activeSnapshot = activeClient.GetConnectionDiagnosticsSnapshot();

        lock (_diagnosticsSync)
        {
            return activeSnapshot with
            {
                ProviderName = ProviderDisplayName,
                LifecycleState = _lifecycleState,
                IsConnected = _isConnected,
                IsReconnecting = _isReconnecting,
                LastConnectedAt = _lastConnectedAt ?? activeSnapshot.LastConnectedAt,
                LastDisconnectedAt = _lastDisconnectedAt ?? activeSnapshot.LastDisconnectedAt,
                LastError = _lastError ?? activeSnapshot.LastError,
                LastFailureKind = _lastFailureKind ?? activeSnapshot.LastFailureKind,
                ConnectionAge = _isConnected && _lastConnectedAt.HasValue
                    ? DateTimeOffset.UtcNow - _lastConnectedAt.Value
                    : null,
                ActiveSubscriptions = _activeDepthSubscriptions.Count + _activeTradeSubscriptions.Count
            };
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        UpdateDiagnostics(ProviderConnectionLifecycleState.Connecting, isConnected: false);
        try
        {
            await _activeClient.ConnectAsync(ct);
            _failoverService.RecordSuccess(_activeProviderId);
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Connected,
                isConnected: true,
                connectedAt: DateTimeOffset.UtcNow);
            _log.Information("Connected to active provider {ProviderId}", _activeProviderId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _cts.IsCancellationRequested)
        {
            UpdateDiagnostics(ProviderConnectionLifecycleState.Configured, isConnected: false);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to active provider {ProviderId}, attempting failover", _activeProviderId);
            _failoverService.RecordFailure(_activeProviderId, $"ConnectAsync failed: {ex.Message}");
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Reconnecting,
                isConnected: false,
                isReconnecting: true,
                error: ex);

            // Attempt immediate failover connection
            try
            {
                await TryFailoverConnectAsync(ct);
            }
            catch
            {
                UpdateDiagnostics(
                    ProviderConnectionLifecycleState.Failed,
                    isConnected: false,
                    error: ex);
                throw;
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        UpdateDiagnostics(ProviderConnectionLifecycleState.Disconnecting, isConnected: _isConnected);
        try
        {
            await _activeClient.DisconnectAsync(ct);
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Disconnected,
                isConnected: false,
                disconnectedAt: DateTimeOffset.UtcNow);
            _log.Information("Disconnected from active provider {ProviderId}", _activeProviderId);
        }
        catch (OperationCanceledException)
        {
            RefreshDiagnosticsFromActiveProvider();
            throw;
        }
        catch (Exception ex)
        {
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Failed,
                isConnected: false,
                error: ex);
            throw;
        }
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        if (_depthSubIds.TryGetValue(cfg.Symbol, out var existingId))
        {
            _depthSubscriberCounts.AddOrUpdate(cfg.Symbol, 1, static (_, current) => current + 1);
            return existingId;
        }

        try
        {
            var id = _activeClient.SubscribeMarketDepth(cfg);
            if (id > 0)
            {
                _activeDepthSubscriptions[cfg.Symbol] = cfg;
                _depthSubIds[cfg.Symbol] = id;
                _depthSubscriberCounts[cfg.Symbol] = 1;
                _failoverService.RecordSuccess(_activeProviderId);
            }
            return id;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SubscribeMarketDepth failed for {Symbol} on {Provider}", cfg.Symbol, _activeProviderId);
            _failoverService.RecordFailure(_activeProviderId, $"SubscribeMarketDepth failed: {ex.Message}");
            _activeDepthSubscriptions[cfg.Symbol] = cfg;
            return -1;
        }
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        var shouldUnsubscribeUpstream = true;
        var symbol = _depthSubIds.FirstOrDefault(kvp => kvp.Value == subscriptionId).Key;
        if (symbol != null)
        {
            if (_depthSubscriberCounts.TryGetValue(symbol, out var subscriberCount) && subscriberCount > 1)
            {
                _depthSubscriberCounts[symbol] = subscriberCount - 1;
                shouldUnsubscribeUpstream = false;
            }
            else
            {
                _depthSubscriberCounts.TryRemove(symbol, out _);
                _activeDepthSubscriptions.TryRemove(symbol, out _);
                _depthSubIds.TryRemove(symbol, out _);
            }
        }

        if (!shouldUnsubscribeUpstream)
            return;

        try
        {
            _activeClient.UnsubscribeMarketDepth(subscriptionId);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "UnsubscribeMarketDepth failed for subscription {Id}", subscriptionId);
        }
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        if (_tradeSubIds.TryGetValue(cfg.Symbol, out var existingId))
        {
            _tradeSubscriberCounts.AddOrUpdate(cfg.Symbol, 1, static (_, current) => current + 1);
            return existingId;
        }

        try
        {
            var id = _activeClient.SubscribeTrades(cfg);
            if (id > 0)
            {
                _activeTradeSubscriptions[cfg.Symbol] = cfg;
                _tradeSubIds[cfg.Symbol] = id;
                _tradeSubscriberCounts[cfg.Symbol] = 1;
                _failoverService.RecordSuccess(_activeProviderId);
            }
            return id;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SubscribeTrades failed for {Symbol} on {Provider}", cfg.Symbol, _activeProviderId);
            _failoverService.RecordFailure(_activeProviderId, $"SubscribeTrades failed: {ex.Message}");
            _activeTradeSubscriptions[cfg.Symbol] = cfg;
            return -1;
        }
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        var shouldUnsubscribeUpstream = true;
        var symbol = _tradeSubIds.FirstOrDefault(kvp => kvp.Value == subscriptionId).Key;
        if (symbol != null)
        {
            if (_tradeSubscriberCounts.TryGetValue(symbol, out var subscriberCount) && subscriberCount > 1)
            {
                _tradeSubscriberCounts[symbol] = subscriberCount - 1;
                shouldUnsubscribeUpstream = false;
            }
            else
            {
                _tradeSubscriberCounts.TryRemove(symbol, out _);
                _activeTradeSubscriptions.TryRemove(symbol, out _);
                _tradeSubIds.TryRemove(symbol, out _);
            }
        }

        if (!shouldUnsubscribeUpstream)
            return;

        try
        {
            _activeClient.UnsubscribeTrades(subscriptionId);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "UnsubscribeTrades failed for subscription {Id}", subscriptionId);
        }
    }



    public string ProviderId => $"failover-{_ruleId}";
    public string ProviderDisplayName => $"Failover ({_activeProviderId})";
    public string ProviderDescription => $"Failover-aware composite provider, currently active: {_activeProviderId}";
    public int ProviderPriority => _activeClient is IProviderMetadata meta ? meta.ProviderPriority : 50;
    public ProviderCapabilities ProviderCapabilities => _activeClient is IProviderMetadata meta
        ? meta.ProviderCapabilities
        : ProviderCapabilities.Streaming();



    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();

        _failoverService.OnFailoverTriggered -= HandleFailoverTriggered;
        _failoverService.OnFailoverRecovered -= HandleFailoverRecovered;

        foreach (var kvp in _providers)
        {
            if (_providerDiagnosticsHandlers.TryGetValue(kvp.Key, out var handler))
                kvp.Value.ConnectionDiagnosticsChanged -= handler;

            try
            {
                await kvp.Value.DisposeAsync();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error disposing provider {ProviderId}", kvp.Key);
            }
        }

        _switchLock.Dispose();
    }


    /// <summary>
    /// Gets the currently active provider ID.
    /// </summary>
    public string ActiveProviderId => _activeProviderId;

    /// <summary>
    /// Gets the underlying active client (for diagnostics).
    /// </summary>
    internal IMarketDataClient ActiveClient => _activeClient;

    private async void HandleFailoverTriggered(FailoverTriggeredEvent evt)
    {
        if (!string.Equals(evt.RuleId, _ruleId, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await SwitchProviderAsync(evt.ToProviderId, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to execute failover switch from {From} to {To} for rule {RuleId}",
                evt.FromProviderId, evt.ToProviderId, evt.RuleId);
        }
    }

    private async void HandleFailoverRecovered(FailoverRecoveredEvent evt)
    {
        if (!string.Equals(evt.RuleId, _ruleId, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await SwitchProviderAsync(evt.ToProviderId, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to execute recovery switch from {From} to {To} for rule {RuleId}",
                evt.FromProviderId, evt.ToProviderId, evt.RuleId);
        }
    }

    private async Task SwitchProviderAsync(string newProviderId, CancellationToken ct)
    {
        var newProviderKey = ProviderIdentity.NormalizeId(newProviderId);
        if (!_providers.TryGetValue(newProviderKey, out var newClient))
        {
            _log.Error("Cannot switch to unknown provider {ProviderId}", newProviderKey);
            return;
        }

        if (string.Equals(_activeProviderId, newProviderKey, StringComparison.OrdinalIgnoreCase))
            return;

        await _switchLock.WaitAsync(ct);
        try
        {
            var previousId = _activeProviderId;
            var previousClient = _activeClient;

            _log.Information("Switching streaming provider: {From} -> {To}", previousId, newProviderKey);
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Reconnecting,
                isConnected: _isConnected,
                isReconnecting: true);

            try
            {
                // 1. Connect the new provider
                await newClient.ConnectAsync(ct);

                // 2. Re-subscribe all active symbols on the new provider
                await ResubscribeAsync(newClient, ct);

                // 3. Swap the active client
                _activeClient = newClient;
                _activeProviderId = newProviderKey;
                UpdateDiagnostics(
                    ProviderConnectionLifecycleState.Connected,
                    isConnected: true,
                    connectedAt: DateTimeOffset.UtcNow);

                // 4. Disconnect the old provider gracefully
                try
                {
                    await previousClient.DisconnectAsync(ct);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error disconnecting previous provider {ProviderId} during failover", previousId);
                }

                _log.Information("Provider switch complete: now using {ProviderId}", newProviderKey);
            }
            catch
            {
                RefreshDiagnosticsFromActiveProvider();
                throw;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private async Task TryFailoverConnectAsync(CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct);
        try
        {
            // Try each backup provider in the rule
            foreach (var kvp in _providers)
            {
                if (string.Equals(kvp.Key, _activeProviderId, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    _log.Information("Attempting failover connect to {ProviderId}", kvp.Key);
                    await kvp.Value.ConnectAsync(ct);
                    _activeClient = kvp.Value;
                    _activeProviderId = kvp.Key;
                    _failoverService.RecordSuccess(kvp.Key);
                    UpdateDiagnostics(
                        ProviderConnectionLifecycleState.Connected,
                        isConnected: true,
                        connectedAt: DateTimeOffset.UtcNow);
                    _log.Information("Failover connect succeeded to {ProviderId}", kvp.Key);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested || _cts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failover connect to {ProviderId} also failed", kvp.Key);
                    _failoverService.RecordFailure(kvp.Key, $"ConnectAsync failed: {ex.Message}");
                }
            }

            _log.Error("All failover providers exhausted during connect; no provider available");
            throw new InvalidOperationException("All streaming providers failed to connect.");
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private static Dictionary<string, IMarketDataClient> NormalizeProviders(
        Dictionary<string, IMarketDataClient> providers)
    {
        var normalized = new Dictionary<string, IMarketDataClient>(StringComparer.OrdinalIgnoreCase);
        foreach (var (providerId, client) in providers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
            ArgumentNullException.ThrowIfNull(client);

            var key = ProviderIdentity.NormalizeId(providerId);
            if (!normalized.TryAdd(key, client))
            {
                throw new ArgumentException(
                    $"Duplicate provider '{providerId}' resolves to normalized key '{key}'.",
                    nameof(providers));
            }
        }

        return normalized;
    }

    private Task ResubscribeAsync(IMarketDataClient newClient, CancellationToken ct)
    {
        _depthSubIds.Clear();
        _tradeSubIds.Clear();

        foreach (var kvp in _activeDepthSubscriptions)
        {
            try
            {
                var id = newClient.SubscribeMarketDepth(kvp.Value);
                if (id > 0)
                    _depthSubIds[kvp.Key] = id;
                _log.Debug("Re-subscribed depth for {Symbol} on new provider (id={Id})", kvp.Key, id);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to re-subscribe depth for {Symbol} on new provider", kvp.Key);
            }
        }

        foreach (var kvp in _activeTradeSubscriptions)
        {
            try
            {
                var id = newClient.SubscribeTrades(kvp.Value);
                if (id > 0)
                    _tradeSubIds[kvp.Key] = id;
                _log.Debug("Re-subscribed trades for {Symbol} on new provider (id={Id})", kvp.Key, id);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to re-subscribe trades for {Symbol} on new provider", kvp.Key);
            }
        }

        return Task.CompletedTask;
    }

    private void HandleProviderDiagnosticsChanged(
        string providerId,
        IMarketDataClient provider,
        WebSocketConnectionDiagnostics snapshot)
    {
        if (!ReferenceEquals(provider, _activeClient) ||
            !string.Equals(providerId, _activeProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_diagnosticsSync)
        {
            _lifecycleState = snapshot.LifecycleState;
            _isConnected = snapshot.IsConnected;
            _isReconnecting = snapshot.IsReconnecting;
            _lastConnectedAt = snapshot.LastConnectedAt ?? _lastConnectedAt;
            _lastDisconnectedAt = snapshot.LastDisconnectedAt ?? _lastDisconnectedAt;
            _lastError = snapshot.LastError;
            _lastFailureKind = snapshot.LastFailureKind;
        }

        PublishDiagnostics();
    }

    private void RefreshDiagnosticsFromActiveProvider()
        => HandleProviderDiagnosticsChanged(
            _activeProviderId,
            _activeClient,
            _activeClient.GetConnectionDiagnosticsSnapshot());

    private void UpdateDiagnostics(
        ProviderConnectionLifecycleState lifecycleState,
        bool isConnected,
        bool isReconnecting = false,
        DateTimeOffset? connectedAt = null,
        DateTimeOffset? disconnectedAt = null,
        Exception? error = null)
    {
        lock (_diagnosticsSync)
        {
            _lifecycleState = lifecycleState;
            _isConnected = isConnected;
            _isReconnecting = isReconnecting;
            if (connectedAt.HasValue)
                _lastConnectedAt = connectedAt;
            if (disconnectedAt.HasValue)
                _lastDisconnectedAt = disconnectedAt;
            _lastError = error?.Message;
            _lastFailureKind = error is null ? null : ProviderFailureClassifier.Classify(error);
        }

        PublishDiagnostics();
    }

    private void PublishDiagnostics()
    {
        try
        {
            ConnectionDiagnosticsChanged?.Invoke(GetConnectionDiagnosticsSnapshot());
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failover connection diagnostics subscriber failed for {ProviderId}", _activeProviderId);
        }
    }
}
