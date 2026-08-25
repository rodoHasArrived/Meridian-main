using System.Collections.Concurrent;
using System.Net.WebSockets;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Domain.Events;
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
    private readonly IMarketEventPublisher? _integrityPublisher;
    private readonly string _ruleId;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _diagnosticsSync = new();
    private readonly object _subscriptionGate = new();
    private readonly object _switchTasksGate = new();
    private readonly HashSet<Task> _switchTasks = [];
    private readonly IDisposable _transitionRegistration;
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private int _disposeState;
    private readonly ConcurrentDictionary<string, SymbolConfig> _activeDepthSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SymbolConfig> _activeTradeSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _depthSubIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _tradeSubIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _depthSubscriptionHandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _tradeSubscriptionHandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _depthSymbolsByHandle = [];
    private readonly Dictionary<int, string> _tradeSymbolsByHandle = [];
    private readonly ConcurrentDictionary<string, int> _depthSubscriberCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _tradeSubscriberCounts = new(StringComparer.OrdinalIgnoreCase);
    private int _nextSubscriptionHandle;

    /// <summary>
    /// Creates a new failover-aware client.
    /// </summary>
    /// <param name="providers">Map of provider ID to client instance.</param>
    /// <param name="failoverService">The failover orchestrator.</param>
    /// <param name="ruleId">The failover rule ID this client corresponds to.</param>
    /// <param name="initialProviderId">The provider ID to start with (typically the primary).</param>
    /// <param name="integrityPublisher">Optional publisher used to disclose completed provider
    /// switches (and their coverage-uncertain windows) on the market-event tape. Compositions
    /// without a pipeline omit it and behave exactly as before.</param>
    public FailoverAwareMarketDataClient(
        Dictionary<string, IMarketDataClient> providers,
        StreamingFailoverService failoverService,
        string ruleId,
        string initialProviderId,
        IMarketEventPublisher? integrityPublisher = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = NormalizeProviders(providers);
        _failoverService = failoverService ?? throw new ArgumentNullException(nameof(failoverService));
        _integrityPublisher = integrityPublisher;
        _ruleId = ruleId;

        var initialKey = ProviderIdentity.NormalizeId(initialProviderId);
        if (!_providers.TryGetValue(initialKey, out var initial))
            throw new ArgumentException($"Initial provider '{initialKey}' not found in provider map.", nameof(initialProviderId));

        _activeClient = initial;
        _activeProviderId = initialKey;

        _transitionRegistration = _failoverService.RegisterTransitionHandler(_ruleId, QueueTransition);
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
            var failedProviderId = _activeProviderId;
            _log.Error(ex, "Failed to connect to active provider {ProviderId}, attempting failover", failedProviderId);
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Reconnecting,
                isConnected: false,
                isReconnecting: true,
                error: ex);

            // Attempt immediate failover connection
            try
            {
                await TryFailoverConnectAsync(failedProviderId, ex, ct);
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

        string providerId;
        int subscriptionHandle;
        try
        {
            lock (_subscriptionGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

                if (_depthSubscriptionHandles.TryGetValue(cfg.Symbol, out var existingHandle))
                {
                    _depthSubscriberCounts.AddOrUpdate(cfg.Symbol, 1, static (_, current) => current + 1);
                    return existingHandle;
                }

                providerId = _activeProviderId;
                var upstreamId = _activeClient.SubscribeMarketDepth(cfg);
                if (upstreamId <= 0)
                    return upstreamId;

                subscriptionHandle = NextSubscriptionHandle();
                _activeDepthSubscriptions[cfg.Symbol] = cfg;
                _depthSubIds[cfg.Symbol] = upstreamId;
                _depthSubscriptionHandles[cfg.Symbol] = subscriptionHandle;
                _depthSymbolsByHandle[subscriptionHandle] = cfg.Symbol;
                _depthSubscriberCounts[cfg.Symbol] = 1;
            }

            _failoverService.RecordSuccess(providerId);
            return subscriptionHandle;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposeState) != 0)
        {
            return -1;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SubscribeMarketDepth failed for {Symbol} on {Provider}", cfg.Symbol, _activeProviderId);
            _failoverService.RecordFailure(_activeProviderId, $"SubscribeMarketDepth failed: {ex.Message}");
            return -1;
        }
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        lock (_subscriptionGate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
                return;

            if (!_depthSymbolsByHandle.TryGetValue(subscriptionId, out var symbol))
                return;

            if (_depthSubscriberCounts.TryGetValue(symbol, out var subscriberCount) && subscriberCount > 1)
            {
                _depthSubscriberCounts[symbol] = subscriberCount - 1;
                return;
            }

            _depthSubscriberCounts.TryRemove(symbol, out _);
            _activeDepthSubscriptions.TryRemove(symbol, out _);
            _depthSubscriptionHandles.Remove(symbol);
            _depthSymbolsByHandle.Remove(subscriptionId);

            if (_depthSubIds.TryRemove(symbol, out var upstreamId))
            {
                try
                {
                    _activeClient.UnsubscribeMarketDepth(upstreamId);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "UnsubscribeMarketDepth failed for subscription {Id}", subscriptionId);
                }
            }
        }
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        string providerId;
        int subscriptionHandle;
        try
        {
            lock (_subscriptionGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

                if (_tradeSubscriptionHandles.TryGetValue(cfg.Symbol, out var existingHandle))
                {
                    _tradeSubscriberCounts.AddOrUpdate(cfg.Symbol, 1, static (_, current) => current + 1);
                    return existingHandle;
                }

                providerId = _activeProviderId;
                var upstreamId = _activeClient.SubscribeTrades(cfg);
                if (upstreamId <= 0)
                    return upstreamId;

                subscriptionHandle = NextSubscriptionHandle();
                _activeTradeSubscriptions[cfg.Symbol] = cfg;
                _tradeSubIds[cfg.Symbol] = upstreamId;
                _tradeSubscriptionHandles[cfg.Symbol] = subscriptionHandle;
                _tradeSymbolsByHandle[subscriptionHandle] = cfg.Symbol;
                _tradeSubscriberCounts[cfg.Symbol] = 1;
            }

            _failoverService.RecordSuccess(providerId);
            return subscriptionHandle;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposeState) != 0)
        {
            return -1;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SubscribeTrades failed for {Symbol} on {Provider}", cfg.Symbol, _activeProviderId);
            _failoverService.RecordFailure(_activeProviderId, $"SubscribeTrades failed: {ex.Message}");
            return -1;
        }
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        lock (_subscriptionGate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
                return;

            if (!_tradeSymbolsByHandle.TryGetValue(subscriptionId, out var symbol))
                return;

            if (_tradeSubscriberCounts.TryGetValue(symbol, out var subscriberCount) && subscriberCount > 1)
            {
                _tradeSubscriberCounts[symbol] = subscriberCount - 1;
                return;
            }

            _tradeSubscriberCounts.TryRemove(symbol, out _);
            _activeTradeSubscriptions.TryRemove(symbol, out _);
            _tradeSubscriptionHandles.Remove(symbol);
            _tradeSymbolsByHandle.Remove(subscriptionId);

            if (_tradeSubIds.TryRemove(symbol, out var upstreamId))
            {
                try
                {
                    _activeClient.UnsubscribeTrades(upstreamId);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "UnsubscribeTrades failed for subscription {Id}", subscriptionId);
                }
            }
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            _transitionRegistration.Dispose();
            _cts.Cancel();

            Task[] pendingSwitches;
            lock (_switchTasksGate)
                pendingSwitches = _switchTasks.ToArray();

            try
            {
                await Task.WhenAll(pendingSwitches).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Expected while an in-flight provider hand-off observes shutdown.
            }

            // Wait for any synchronous subscription mutation admitted before disposal to finish.
            lock (_subscriptionGate)
            {
            }

            foreach (var kvp in _providers)
            {
                if (_providerDiagnosticsHandlers.TryGetValue(kvp.Key, out var handler))
                    kvp.Value.ConnectionDiagnosticsChanged -= handler;
            }

            foreach (var client in new HashSet<IMarketDataClient>(
                         _providers.Values,
                         ReferenceEqualityComparer.Instance))
            {
                try
                {
                    await client.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error disposing provider {ProviderId}", client.ProviderId);
                }
            }

            _switchLock.Dispose();
            _cts.Dispose();
        }
        finally
        {
            _disposeCompletion.TrySetResult();
        }
    }


    /// <summary>
    /// Gets the currently active provider ID.
    /// </summary>
    public string ActiveProviderId => _activeProviderId;

    /// <summary>
    /// Gets the underlying active client (for diagnostics).
    /// </summary>
    internal IMarketDataClient ActiveClient => _activeClient;

    private void QueueTransition(FailoverTransitionRequest transition)
    {
        Task task;
        lock (_switchTasksGate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                transition.TryCancel("The streaming provider runtime is shutting down.");
                return;
            }

            task = ExecuteTransitionAsync(transition);
            _switchTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_switchTasksGate)
                    _switchTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ExecuteTransitionAsync(FailoverTransitionRequest transition)
    {
        using var linkedCts = transition.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, transition.CancellationToken)
            : null;
        var cancellationToken = linkedCts?.Token ?? _cts.Token;
        try
        {
            await SwitchProviderAsync(transition, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transition.TryCancel(_cts.IsCancellationRequested
                ? "The streaming provider runtime shut down before the hand-off completed."
                : "The provider transition request was cancelled before the hand-off completed.");
        }
        catch (Exception ex)
        {
            transition.TryReject("The target provider could not complete the connection and subscription hand-off.");
            _log.Error(
                ex,
                "Failed to execute provider transition from {From} to {To} for rule {RuleId}",
                transition.FromProviderId,
                transition.ToProviderId,
                transition.RuleId);
        }
    }

    private async Task SwitchProviderAsync(FailoverTransitionRequest transition, CancellationToken ct)
    {
        var newProviderKey = ProviderIdentity.NormalizeId(transition.ToProviderId);
        if (!_providers.TryGetValue(newProviderKey, out var newClient))
        {
            throw new InvalidOperationException($"Cannot switch to unknown provider '{newProviderKey}'.");
        }

        if (string.Equals(_activeProviderId, newProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            transition.TryComplete();
            return;
        }

        await _switchLock.WaitAsync(ct);
        try
        {
            var previousId = _activeProviderId;
            var previousClient = _activeClient;

            // Coverage-uncertain window start: if the active feed is already down, uncertainty
            // began at the disconnect; otherwise (forced/recovery switch off a live feed) it
            // begins now, as the hand-off starts.
            var switchStartedAt = DateTimeOffset.UtcNow;
            DateTimeOffset uncertainFrom;
            lock (_diagnosticsSync)
            {
                uncertainFrom = !_isConnected && _lastDisconnectedAt.HasValue
                    ? _lastDisconnectedAt.Value
                    : switchStartedAt;
            }

            _log.Information("Switching streaming provider: {From} -> {To}", previousId, newProviderKey);
            UpdateDiagnostics(
                ProviderConnectionLifecycleState.Reconnecting,
                isConnected: _isConnected,
                isReconnecting: true);

            try
            {
                // 1. Connect the new provider
                await newClient.ConnectAsync(ct).ConfigureAwait(false);

                // 2. Re-subscribe and commit while subscription mutations are excluded. This
                // guarantees additions are either included in the hand-off snapshot or applied to
                // the new active provider, and removals cannot be resurrected by replacement IDs.
                var transitionCommitted = false;
                IReadOnlyList<string> affectedSymbols = Array.Empty<string>();
                lock (_subscriptionGate)
                {
                    ct.ThrowIfCancellationRequested();
                    var subscriptions = Resubscribe(newClient, ct);
                    ct.ThrowIfCancellationRequested();

                    transitionCommitted = transition.TryComplete(() =>
                    {
                        _activeClient = newClient;
                        _activeProviderId = newProviderKey;
                        ReplaceSubscriptions(_depthSubIds, subscriptions.Depth);
                        ReplaceSubscriptions(_tradeSubIds, subscriptions.Trades);
                    });

                    if (transitionCommitted)
                    {
                        affectedSymbols = subscriptions.Depth.Keys
                            .Concat(subscriptions.Trades.Keys)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                    else
                    {
                        ReleasePreparedSubscriptions(newClient, subscriptions);
                    }
                }

                if (!transitionCommitted)
                {
                    try
                    {
                        await newClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        _log.Warning(
                            cleanupException,
                            "Error disconnecting uncommitted failover provider {ProviderId}",
                            newProviderKey);
                    }

                    RefreshDiagnosticsFromActiveProvider();
                    return;
                }

                // Re-subscription is complete and the hand-off is committed: the
                // coverage-uncertain window closes here. Disclose the switch on the tape.
                var uncertainTo = DateTimeOffset.UtcNow;
                PublishFailoverMarkers(
                    previousId,
                    newProviderKey,
                    transition.Reason,
                    uncertainFrom,
                    uncertainTo,
                    affectedSymbols);

                UpdateDiagnostics(
                    ProviderConnectionLifecycleState.Connected,
                    isConnected: true,
                    connectedAt: DateTimeOffset.UtcNow);

                // 4. Disconnect the old provider gracefully
                try
                {
                    await previousClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error disconnecting previous provider {ProviderId} during failover", previousId);
                }

                _log.Information("Provider switch complete: now using {ProviderId}", newProviderKey);
            }
            catch
            {
                if (!ReferenceEquals(_activeClient, newClient))
                {
                    try
                    {
                        await newClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        _log.Warning(
                            cleanupException,
                            "Error disconnecting rejected failover provider {ProviderId}",
                            newProviderKey);
                    }
                }

                RefreshDiagnosticsFromActiveProvider();
                throw;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private async Task TryFailoverConnectAsync(
        string failedProviderId,
        Exception connectionFailure,
        CancellationToken ct)
    {
        var switched = await _failoverService.FailoverAfterConnectionFailureAsync(
            _ruleId,
            failedProviderId,
            $"ConnectAsync failed: {connectionFailure.Message}",
            ct).ConfigureAwait(false);

        var coordinatorProviderId = _failoverService.GetActiveProviderId(_ruleId);
        if (!switched ||
            coordinatorProviderId is null ||
            ProviderIdentity.EqualsId(coordinatorProviderId, failedProviderId) ||
            !ProviderIdentity.EqualsId(coordinatorProviderId, _activeProviderId))
        {
            _log.Error("All failover providers exhausted during connect; no provider available");
            throw new InvalidOperationException("All streaming providers failed to connect.");
        }

        _failoverService.RecordSuccess(_activeProviderId);
        UpdateDiagnostics(
            ProviderConnectionLifecycleState.Connected,
            isConnected: true,
            connectedAt: DateTimeOffset.UtcNow);
        _log.Information("Failover connect succeeded to {ProviderId}", _activeProviderId);
    }

    /// <summary>
    /// Discloses a committed provider hand-off on the market-event tape: from-provider,
    /// to-provider, transition reason, and the coverage-uncertain window between losing the old
    /// feed and completing re-subscription on the new one. One marker is published per affected
    /// symbol (or a single SYSTEM marker when nothing is subscribed), stamped with the real
    /// from-provider identity — the provider whose coverage the window belongs to. Publication is
    /// strictly best-effort: a marker failure must never break or roll back the failover itself,
    /// so failures are logged at Warning and swallowed (fail-open).
    /// </summary>
    private void PublishFailoverMarkers(
        string fromProviderId,
        string toProviderId,
        string reason,
        DateTimeOffset uncertainFrom,
        DateTimeOffset uncertainTo,
        IReadOnlyList<string> affectedSymbols)
    {
        if (_integrityPublisher is null)
            return;

        try
        {
            var symbols = affectedSymbols.Count > 0
                ? affectedSymbols
                : new[] { "SYSTEM" };
            foreach (var symbol in symbols)
            {
                var integrity = IntegrityEvent.ProviderFailover(
                    uncertainTo,
                    symbol,
                    fromProviderId,
                    toProviderId,
                    reason,
                    uncertainFrom,
                    uncertainTo);
                _integrityPublisher.TryPublish(
                    MarketEvent.Integrity(uncertainTo, symbol, integrity, fromProviderId));
            }
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to publish provider-failover integrity markers for {From} -> {To} on rule {RuleId}",
                fromProviderId,
                toProviderId,
                _ruleId);
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

    private (IReadOnlyDictionary<string, int> Depth, IReadOnlyDictionary<string, int> Trades) Resubscribe(
        IMarketDataClient newClient,
        CancellationToken ct)
    {
        var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var trades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var kvp in _activeDepthSubscriptions)
            {
                ct.ThrowIfCancellationRequested();
                var id = newClient.SubscribeMarketDepth(kvp.Value);
                if (id <= 0)
                    throw new InvalidOperationException(
                        $"Provider rejected the required depth subscription for '{kvp.Key}'.");
                depth[kvp.Key] = id;
                _log.Debug("Re-subscribed depth for {Symbol} on new provider (id={Id})", kvp.Key, id);
            }

            foreach (var kvp in _activeTradeSubscriptions)
            {
                ct.ThrowIfCancellationRequested();
                var id = newClient.SubscribeTrades(kvp.Value);
                if (id <= 0)
                    throw new InvalidOperationException(
                        $"Provider rejected the required trade subscription for '{kvp.Key}'.");
                trades[kvp.Key] = id;
                _log.Debug("Re-subscribed trades for {Symbol} on new provider (id={Id})", kvp.Key, id);
            }

            ct.ThrowIfCancellationRequested();
            return (depth, trades);
        }
        catch
        {
            foreach (var subscriptionId in depth.Values)
                TryUnsubscribeDepth(newClient, subscriptionId);
            foreach (var subscriptionId in trades.Values)
                TryUnsubscribeTrades(newClient, subscriptionId);
            throw;
        }
    }

    private static void ReplaceSubscriptions(
        ConcurrentDictionary<string, int> target,
        IReadOnlyDictionary<string, int> replacement)
    {
        target.Clear();
        foreach (var (symbol, subscriptionId) in replacement)
            target[symbol] = subscriptionId;
    }

    private void ReleasePreparedSubscriptions(
        IMarketDataClient client,
        (IReadOnlyDictionary<string, int> Depth, IReadOnlyDictionary<string, int> Trades) subscriptions)
    {
        foreach (var subscriptionId in subscriptions.Depth.Values)
            TryUnsubscribeDepth(client, subscriptionId);
        foreach (var subscriptionId in subscriptions.Trades.Values)
            TryUnsubscribeTrades(client, subscriptionId);
    }

    private int NextSubscriptionHandle()
    {
        var handle = Interlocked.Increment(ref _nextSubscriptionHandle);
        if (handle <= 0)
            throw new InvalidOperationException("Streaming subscription handle space is exhausted.");
        return handle;
    }

    private void TryUnsubscribeDepth(IMarketDataClient client, int subscriptionId)
    {
        try
        {
            client.UnsubscribeMarketDepth(subscriptionId);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup, but a leaked upstream subscription keeps streaming and
            // burning provider quota, so it must at least be visible in the logs.
            _log.Warning(ex,
                "Failed to release depth subscription {SubscriptionId} on provider {ProviderId} during failover cleanup",
                subscriptionId, client.ProviderId);
        }
    }

    private void TryUnsubscribeTrades(IMarketDataClient client, int subscriptionId)
    {
        try
        {
            client.UnsubscribeTrades(subscriptionId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "Failed to release trade subscription {SubscriptionId} on provider {ProviderId} during failover cleanup",
                subscriptionId, client.ProviderId);
        }
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
