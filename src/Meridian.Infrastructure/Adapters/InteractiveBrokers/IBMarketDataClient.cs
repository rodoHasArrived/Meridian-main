using System.Collections.Concurrent;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.ProviderSdk;
using Serilog;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Concrete Interactive Brokers market data client. Buildable out-of-the-box:
/// - Without IBAPI defined, delegates to IBSimulationClient (generates synthetic data for testing).
/// - With IBAPI defined, uses EnhancedIBConnectionManager + IBCallbackRouter for real TWS/Gateway.
/// </summary>
[DataSource("ib", "Interactive Brokers", Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 1, Description = "Interactive Brokers TWS/Gateway for real-time market data")]
[ImplementsAdr("ADR-001", "Interactive Brokers streaming data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class IBMarketDataClient :
    IMarketDataClient,
    IProviderConnectionDiagnosticsSource,
    IProviderRateLimitDiagnosticsSource
{
    private readonly IMarketDataClient _inner;
    private readonly bool _isSimulation;
    private readonly ProviderRateLimitTracker _streamingRateLimits;

    public IBMarketDataClient(
        IMarketEventPublisher publisher,
        TradeDataCollector tradeCollector,
        MarketDepthCollector depthCollector,
        QuoteCollector? quoteCollector = null,
        OptionDataCollector? optionCollector = null,
        IBOptions? options = null)
    {
        _streamingRateLimits = new ProviderRateLimitTracker();
        _streamingRateLimits.RegisterProvider(
            "ib",
            maxRequestsPerWindow: 50,
            window: TimeSpan.FromSeconds(1),
            minDelay: TimeSpan.FromMilliseconds(20));

#if IBAPI
        var liveClient = new IBMarketDataClientIBApi(
            publisher,
            tradeCollector,
            depthCollector,
            quoteCollector,
            optionCollector,
            options ?? new IBOptions());
        liveClient.RateLimitHit += RecordPacingViolation;
        liveClient.StreamingRequestSent += RecordStreamingRequest;
        _inner = liveClient;
        _isSimulation = false;
#else
        _inner = new IBSimulationClient(publisher);
        _isSimulation = true;
        Log.ForContext<IBMarketDataClient>().Warning(
            "Interactive Brokers market data is running in SIMULATION mode: prices are a synthetic random walk, " +
            "historical requests return no bars, and no order reaches a broker. Build with the IBAPI compile flag " +
            "and connect TWS/Gateway for real market data.");
#endif
        _inner.ConnectionDiagnosticsChanged += OnInnerConnectionDiagnosticsChanged;
    }

    /// <summary>
    /// True when running without the IBAPI reference (simulation mode).
    /// </summary>
    public bool IsSimulation => _isSimulation;

    /// <inheritdoc/>
    public bool IsSimulated => _isSimulation;

    /// <summary>
    /// True when this build carries no IBAPI reference, so every IB client it creates is a
    /// random-walk simulator. Lets hosts report simulation mode without constructing a client.
    /// </summary>
    public static bool IsSimulationBuild =>
#if IBAPI
        false;
#else
        true;
#endif

    public bool IsEnabled => _inner.IsEnabled;


    /// <inheritdoc/>
    public string ProviderId => "ib";

    /// <inheritdoc/>
    public string ProviderDisplayName => "Interactive Brokers";

    /// <inheritdoc/>
    public string ProviderDescription => "Professional-grade trading and data platform with TWS/Gateway connectivity";

    /// <inheritdoc/>
    public int ProviderPriority => 20;

    /// <inheritdoc/>
    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: true,
        maxDepthLevels: 10) with
    {
        SupportedMarkets = new[] { "US", "EU", "APAC", "Global" },
        MaxRequestsPerWindow = 50,
        RateLimitWindow = TimeSpan.FromSeconds(1),
        MinRequestDelay = TimeSpan.FromMilliseconds(20)
    };

    /// <inheritdoc/>
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("Host", null, "TWS/Gateway Host", false, "127.0.0.1"),
        new ProviderCredentialField("Port", null, "TWS/Gateway Port", false, "7497"),
        new ProviderCredentialField("ClientId", null, "Client ID", false, "1")
    };

    /// <inheritdoc/>
    public string[] ProviderNotes => new[]
    {
        "Requires TWS or IB Gateway running locally.",
        "Account required; paper trading available.",
        "Professional-grade L2 market depth."
    };

    /// <inheritdoc/>
    public string[] ProviderWarnings => new[]
    {
        "Requires Interactive Brokers account.",
        "TWS/Gateway must be running and configured."
    };


    /// <inheritdoc/>
    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    /// <inheritdoc/>
    /// <remarks>
    /// TWS/Gateway connectivity is a raw TCP socket, not a WebSocket, so
    /// <see cref="System.Net.WebSockets.WebSocketState.None"/> is reported. The inner client
    /// owns the lifecycle evidence for both live and simulation builds.
    /// </remarks>
    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
        => _inner.GetConnectionDiagnosticsSnapshot();

    /// <inheritdoc/>
    public ProviderRateLimitDiagnosticSnapshot GetRateLimitDiagnosticsSnapshot()
    {
        var status = _streamingRateLimits.GetStatus(ProviderId)
            ?? throw new InvalidOperationException("IB streaming rate-limit tracking is not initialized.");

        return new ProviderRateLimitDiagnosticSnapshot(
            ProviderId,
            ProviderRateLimitSurfaces.Streaming,
            status.ObservedAt,
            status.RequestsInWindow,
            status.MaxRequestsPerWindow,
            status.Window,
            status.IsRateLimited,
            status.ResetAt,
            status.UsageRatio,
            status.Reason);
    }

    public Task ConnectAsync(CancellationToken ct = default)
        => _inner.ConnectAsync(ct);

    public Task DisconnectAsync(CancellationToken ct = default)
        => _inner.DisconnectAsync(ct);

    private void OnInnerConnectionDiagnosticsChanged(WebSocketConnectionDiagnostics snapshot)
        => ConnectionDiagnosticsChanged?.Invoke(snapshot);

    internal void RecordPacingViolation(TimeSpan? retryAfter = null)
        => _streamingRateLimits.RecordRateLimitHit(ProviderId, retryAfter ?? TimeSpan.FromSeconds(1));

    private void RecordStreamingRequest()
        => _streamingRateLimits.RecordRequest(ProviderId);

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        var subscriptionId = _inner.SubscribeMarketDepth(cfg);
        if (_isSimulation)
            RecordStreamingRequest();
        return subscriptionId;
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        _inner.UnsubscribeMarketDepth(subscriptionId);
        if (_isSimulation)
            RecordStreamingRequest();
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        var subscriptionId = _inner.SubscribeTrades(cfg);
        if (_isSimulation)
            RecordStreamingRequest();
        return subscriptionId;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        _inner.UnsubscribeTrades(subscriptionId);
        if (_isSimulation)
            RecordStreamingRequest();
    }

    public async ValueTask DisposeAsync()
    {
        _inner.ConnectionDiagnosticsChanged -= OnInnerConnectionDiagnosticsChanged;
#if IBAPI
        if (_inner is IBMarketDataClientIBApi liveClient)
        {
            liveClient.RateLimitHit -= RecordPacingViolation;
            liveClient.StreamingRequestSent -= RecordStreamingRequest;
        }
#endif
        await _inner.DisposeAsync().ConfigureAwait(false);
        _streamingRateLimits.Dispose();
    }
}

#if IBAPI
[ImplementsAdr("ADR-001", "Interactive Brokers API streaming data provider")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
internal sealed class IBMarketDataClientIBApi :
    IMarketDataClient,
    IProviderConnectionDiagnosticsSource
{
    private static readonly ILogger _log = Log.ForContext<IBMarketDataClientIBApi>();
    private readonly EnhancedIBConnectionManager _conn;
    private readonly IBCallbackRouter _router;
    private readonly IBOptions _options;
    private readonly ConcurrentDictionary<int, bool> _tradeSubscriptionKinds = new();

    // Track subscription ids if you want per-symbol teardown later
    public bool IsEnabled => true;

    public IBMarketDataClientIBApi(
        IMarketEventPublisher publisher,
        TradeDataCollector tradeCollector,
        MarketDepthCollector depthCollector,
        QuoteCollector? quoteCollector = null,
        OptionDataCollector? optionCollector = null,
        IBOptions? options = null)
    {
        // Router wires IB callbacks -> collectors (collectors already publish into publisher).
        // QuoteCollector enables Level 1 BBO quote emission from reqMktData callbacks.
        // OptionDataCollector enables live greeks from tickOptionComputation callbacks.
        _options = options ?? new IBOptions();
        _router = new IBCallbackRouter(depthCollector, tradeCollector, quoteCollector, optionCollector);
        _conn = new EnhancedIBConnectionManager(
            _router,
            host: _options.Host,
            port: _options.Port,
            clientId: _options.ClientId);
        _conn.ConnectionDiagnosticsChanged += OnConnectionDiagnosticsChanged;
        _conn.PacingViolation += OnPacingViolation;
        _conn.StreamingRequestSent += OnStreamingRequestSent;
    }

    public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

    public event Action<TimeSpan?>? RateLimitHit;

    public event Action? StreamingRequestSent;

    public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
        => _conn.GetConnectionDiagnosticsSnapshot();

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _conn.ConnectAsync(ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _conn.DisconnectAsync(ct).ConfigureAwait(false);
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
        => _conn.SubscribeMarketDepth(cfg with
        {
            DepthLevels = cfg.DepthLevels > 0 ? cfg.DepthLevels : _options.DepthLevels
        });

    public void UnsubscribeMarketDepth(int subscriptionId)
        => _conn.UnsubscribeMarketDepth(subscriptionId);

    public int SubscribeTrades(SymbolConfig cfg)
    {
        if (_options.TickByTick)
        {
            var tradeSubscriptionId = _conn.SubscribeTrades(cfg);
            _tradeSubscriptionKinds[tradeSubscriptionId] = true;
            return tradeSubscriptionId;
        }

        var quoteSubscriptionId = _conn.SubscribeQuotes(cfg);
        _tradeSubscriptionKinds[quoteSubscriptionId] = false;
        return quoteSubscriptionId;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        if (_tradeSubscriptionKinds.TryRemove(subscriptionId, out var isTickByTick) && !isTickByTick)
        {
            _conn.UnsubscribeQuotes(subscriptionId);
            return;
        }

        _conn.UnsubscribeTrades(subscriptionId);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _conn.ConnectionDiagnosticsChanged -= OnConnectionDiagnosticsChanged;
            _conn.PacingViolation -= OnPacingViolation;
            _conn.StreamingRequestSent -= OnStreamingRequestSent;
            _conn.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error disposing IB connection manager during cleanup");
        }
        return ValueTask.CompletedTask;
    }

    private void OnConnectionDiagnosticsChanged(WebSocketConnectionDiagnostics snapshot)
        => ConnectionDiagnosticsChanged?.Invoke(snapshot);

    private void OnPacingViolation(object? sender, int requestId)
        => RateLimitHit?.Invoke(TimeSpan.FromSeconds(1));

    private void OnStreamingRequestSent(object? sender, EventArgs args)
        => StreamingRequestSent?.Invoke();
}
#endif
