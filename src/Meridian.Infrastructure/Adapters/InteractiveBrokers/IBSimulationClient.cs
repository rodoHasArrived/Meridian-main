using System.Collections.Concurrent;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Serilog;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// IB simulation client for development and testing without TWS/Gateway.
/// Generates synthetic market data that mimics IB's data patterns.
/// Activated when DataSource is "IB" and IBAPI is not compiled in.
/// </summary>
[DataSource("ib-sim", "Interactive Brokers (Simulation)", DataSourceType.Realtime, DataSourceCategory.Broker,
    EnabledByDefault = false, Description = "Synthetic market data generator for IB development without TWS/Gateway")]
[ImplementsAdr("ADR-001", "IB simulation client for non-IBAPI builds")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class IBSimulationClient : IMarketDataClient
{
    private static readonly ILogger _log = LoggingSetup.ForContext<IBSimulationClient>();
    private readonly ConcurrentDictionary<int, (string Symbol, SymbolConfig Config)> _depthSubs = new();
    private readonly ConcurrentDictionary<int, (string Symbol, SymbolConfig Config)> _tradeSubs = new();
    private readonly Timer? _tickTimer;
    private readonly IMarketEventPublisher _publisher;
    private readonly TimeSpan _autoTickDueTime;
    private readonly TimeSpan _autoTickPeriod;
    private readonly ProviderConnectionSupervisor _connectionSupervisor;
    private readonly Random _rng = new();
    private int _nextTickerId = 10_000;
    private bool _connected;
    private bool _disposed;

    // Simulated base prices for well-known symbols
    private static readonly Dictionary<string, decimal> BasePrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPY"] = 520m,
        ["AAPL"] = 230m,
        ["MSFT"] = 420m,
        ["GOOGL"] = 170m,
        ["AMZN"] = 190m,
        ["NVDA"] = 850m,
        ["TSLA"] = 250m,
        ["META"] = 530m,
        ["QQQ"] = 440m,
        ["IWM"] = 210m,
        ["DIA"] = 390m
    };

    public IBSimulationClient(
        IMarketEventPublisher publisher,
        bool enableAutoTicks = true,
        TimeSpan? autoTickDueTime = null,
        TimeSpan? autoTickPeriod = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _autoTickDueTime = autoTickDueTime ?? TimeSpan.FromSeconds(1);
        _autoTickPeriod = autoTickPeriod ?? TimeSpan.FromSeconds(1);
        _connectionSupervisor = new ProviderConnectionSupervisor(
            providerName: ProviderDisplayName,
            maxReconnectAttempts: 0,
            retryBaseDelay: TimeSpan.Zero,
            maxRetryDelay: TimeSpan.Zero);
        _connectionSupervisor.StateChanged += OnConnectionSupervisorStateChanged;

        if (enableAutoTicks)
        {
            _tickTimer = new Timer(GenerateSimulatedTicks, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    public bool IsEnabled => true;
    public bool IsSimulation => true;

    /// <inheritdoc/>
    public bool IsSimulated => true;


    public string ProviderId => "ib-sim";
    public string ProviderDisplayName => "Interactive Brokers (Simulation)";
    public string ProviderDescription => "Simulated IB data for development. "
        + IBBuildGuidance.BuildSimulationModeMessage();
    public int ProviderPriority => 99; // Low priority — real providers should take precedence

    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: true,
        maxDepthLevels: 5) with
    {
        SupportedMarkets = new[] { "US (Simulated)" },
        MaxRequestsPerWindow = 50,
        RateLimitWindow = TimeSpan.FromSeconds(1),
        MinRequestDelay = TimeSpan.FromMilliseconds(100)
    };

    public ProviderCredentialField[] ProviderCredentialFields => Array.Empty<ProviderCredentialField>();

    public string[] ProviderNotes => new[]
    {
        "Simulation mode — no TWS/Gateway required.",
        "Generates synthetic market data for testing.",
        IBBuildGuidance.BuildSimulationModeMessage()
    };

    public string[] ProviderWarnings => new[]
    {
        "This is simulated data, not real market data.",
        "Do not use for trading decisions."
    };

    /// <inheritdoc/>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <inheritdoc/>
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
    {
        var supervisor = _connectionSupervisor.GetSnapshot();
        return new WebSocketConnectionDiagnostics(
            ProviderName: ProviderDisplayName,
            LifecycleState: supervisor.LifecycleState,
            WebSocketState: System.Net.WebSockets.WebSocketState.None,
            IsConnected: supervisor.IsConnected,
            IsReconnecting: supervisor.IsReconnecting,
            ReconnectAttempts: supervisor.ReconnectAttempts,
            LastConnectedAt: supervisor.LastConnectedAt,
            LastDisconnectedAt: supervisor.LastDisconnectedAt,
            LastHeartbeatReceivedAt: null,
            LastMessageReceivedAt: null,
            LastReconnectAttemptAt: supervisor.LastReconnectAttemptAt,
            LastError: supervisor.LastError,
            LastFailureKind: supervisor.LastFailureKind,
            ConnectionAge: supervisor.ConnectionAge,
            IdleDuration: null,
            ActiveSubscriptions: _tradeSubs.Count + _depthSubs.Count);
    }


    public Task ConnectAsync(CancellationToken ct = default)
        => _connectionSupervisor.ConnectAsync(ConnectTransportAsync, ct);

    private Task ConnectTransportAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _connected = true;
        _log.Information("[IB-SIM] Connected in simulation mode. Generating synthetic market data for subscribed symbols");
        _tickTimer?.Change(_autoTickDueTime, _autoTickPeriod);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
        => _connectionSupervisor.DisconnectAsync(DisconnectTransportAsync, ct);

    private Task DisconnectTransportAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _connected = false;
        _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _log.Information("[IB-SIM] Disconnected. Generated ticks for {TradeCount} trade subscriptions, {DepthCount} depth subscriptions",
            _tradeSubs.Count, _depthSubs.Count);
        return Task.CompletedTask;
    }

    private void OnConnectionSupervisorStateChanged(ProviderConnectionSupervisorSnapshot _)
        => ConnectionDiagnosticsChanged?.Invoke(GetConnectionDiagnosticsSnapshot());

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        var id = Interlocked.Increment(ref _nextTickerId);
        _depthSubs[id] = (cfg.Symbol, cfg);
        _log.Debug("[IB-SIM] Subscribed market depth for {Symbol} (tickerId={TickerId})", cfg.Symbol, id);
        return id;
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        _depthSubs.TryRemove(subscriptionId, out _);
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        var id = Interlocked.Increment(ref _nextTickerId);
        _tradeSubs[id] = (cfg.Symbol, cfg);
        _log.Debug("[IB-SIM] Subscribed trades for {Symbol} (tickerId={TickerId})", cfg.Symbol, id);
        return id;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        _tradeSubs.TryRemove(subscriptionId, out _);
    }

    private void GenerateSimulatedTicks(object? state)
    {
        if (!_connected || _disposed)
            return;

        try
        {
            foreach (var (id, (symbol, _)) in _tradeSubs.ToArray())
            {
                var basePrice = BasePrices.GetValueOrDefault(symbol, 100m);
                var jitter = (decimal)(_rng.NextDouble() - 0.5) * basePrice * 0.001m; // 0.1% jitter
                var price = Math.Round(basePrice + jitter, 2);
                var size = _rng.Next(1, 500) * 100L;

                var evt = MarketEvent.Trade(
                    DateTimeOffset.UtcNow,
                    symbol,
                    new Trade(
                        Timestamp: DateTimeOffset.UtcNow,
                        Symbol: symbol,
                        Price: price,
                        Size: size,
                        Aggressor: AggressorSide.Unknown,
                        SequenceNumber: 0,
                        StreamId: "IB-SIM",
                        Venue: null),
                    source: "ib-sim");

                _publisher.TryPublish(evt);
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[IB-SIM] Error generating simulated ticks");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _connected = false;
        _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _connectionSupervisor.StateChanged -= OnConnectionSupervisorStateChanged;
        _tickTimer?.Dispose();
        _tradeSubs.Clear();
        _depthSubs.Clear();
        await _connectionSupervisor.DisposeAsync().ConfigureAwait(false);
    }
}
