using System.Collections.Concurrent;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
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

    private volatile IMarketDataClient _activeClient;
    private volatile string _activeProviderId;
    private readonly ConcurrentDictionary<string, SymbolConfig> _activeDepthSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SymbolConfig> _activeTradeSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _depthSubIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _tradeSubIds = new(StringComparer.OrdinalIgnoreCase);

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
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _failoverService = failoverService ?? throw new ArgumentNullException(nameof(failoverService));
        _ruleId = ruleId;

        if (!_providers.TryGetValue(initialProviderId, out var initial))
            throw new ArgumentException($"Initial provider '{initialProviderId}' not found in provider map.", nameof(initialProviderId));

        _activeClient = initial;
        _activeProviderId = initialProviderId;

        _failoverService.OnFailoverTriggered += HandleFailoverTriggered;
        _failoverService.OnFailoverRecovered += HandleFailoverRecovered;

        _log.Information("FailoverAwareMarketDataClient initialized with {ProviderCount} providers, active: {ActiveProvider}, rule: {RuleId}",
            _providers.Count, _activeProviderId, _ruleId);
    }


    public bool IsEnabled => _activeClient.IsEnabled;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            await _activeClient.ConnectAsync(ct);
            _failoverService.RecordSuccess(_activeProviderId);
            _log.Information("Connected to active provider {ProviderId}", _activeProviderId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to active provider {ProviderId}, attempting failover", _activeProviderId);
            _failoverService.RecordFailure(_activeProviderId, $"ConnectAsync failed: {ex.Message}");

            // Attempt immediate failover connection
            await TryFailoverConnectAsync(ct);
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _activeClient.DisconnectAsync(ct);
        _log.Information("Disconnected from active provider {ProviderId}", _activeProviderId);
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        try
        {
            var id = _activeClient.SubscribeMarketDepth(cfg);
            if (id > 0)
            {
                _activeDepthSubscriptions[cfg.Symbol] = cfg;
                _depthSubIds[cfg.Symbol] = id;
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
        // Find and remove the symbol for this subscription ID
        var symbol = _depthSubIds.FirstOrDefault(kvp => kvp.Value == subscriptionId).Key;
        if (symbol != null)
        {
            _activeDepthSubscriptions.TryRemove(symbol, out _);
            _depthSubIds.TryRemove(symbol, out _);
        }

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
        try
        {
            var id = _activeClient.SubscribeTrades(cfg);
            if (id > 0)
            {
                _activeTradeSubscriptions[cfg.Symbol] = cfg;
                _tradeSubIds[cfg.Symbol] = id;
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
        var symbol = _tradeSubIds.FirstOrDefault(kvp => kvp.Value == subscriptionId).Key;
        if (symbol != null)
        {
            _activeTradeSubscriptions.TryRemove(symbol, out _);
            _tradeSubIds.TryRemove(symbol, out _);
        }

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
        _failoverService.OnFailoverTriggered -= HandleFailoverTriggered;
        _failoverService.OnFailoverRecovered -= HandleFailoverRecovered;

        foreach (var kvp in _providers)
        {
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
            await SwitchProviderAsync(evt.ToProviderId, CancellationToken.None);
        }
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
            await SwitchProviderAsync(evt.ToProviderId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to execute recovery switch from {From} to {To} for rule {RuleId}",
                evt.FromProviderId, evt.ToProviderId, evt.RuleId);
        }
    }

    private async Task SwitchProviderAsync(string newProviderId, CancellationToken ct)
    {
        if (!_providers.TryGetValue(newProviderId, out var newClient))
        {
            _log.Error("Cannot switch to unknown provider {ProviderId}", newProviderId);
            return;
        }

        if (string.Equals(_activeProviderId, newProviderId, StringComparison.OrdinalIgnoreCase))
            return;

        await _switchLock.WaitAsync(ct);
        try
        {
            var previousId = _activeProviderId;
            var previousClient = _activeClient;

            _log.Information("Switching streaming provider: {From} -> {To}", previousId, newProviderId);

            // 1. Connect the new provider
            await newClient.ConnectAsync(ct);

            // 2. Re-subscribe all active symbols on the new provider
            await ResubscribeAsync(newClient, ct);

            // 3. Swap the active client
            _activeClient = newClient;
            _activeProviderId = newProviderId;

            // 4. Disconnect the old provider gracefully
            try
            {
                await previousClient.DisconnectAsync(ct);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error disconnecting previous provider {ProviderId} during failover", previousId);
            }

            _log.Information("Provider switch complete: now using {ProviderId}", newProviderId);
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
                    _log.Information("Failover connect succeeded to {ProviderId}", kvp.Key);
                    return;
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
}
