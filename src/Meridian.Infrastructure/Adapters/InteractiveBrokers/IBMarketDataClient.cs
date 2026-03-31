using System.Threading;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
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
public sealed class IBMarketDataClient : IMarketDataClient
{
    private readonly IMarketDataClient _inner;
    private readonly bool _isSimulation;

    public IBMarketDataClient(IMarketEventPublisher publisher, TradeDataCollector tradeCollector, MarketDepthCollector depthCollector, QuoteCollector? quoteCollector = null, OptionDataCollector? optionCollector = null)
    {
#if IBAPI
        _inner = new IBMarketDataClientIBApi(publisher, tradeCollector, depthCollector, quoteCollector, optionCollector);
        _isSimulation = false;
#else
        _inner = new IBSimulationClient(publisher);
        _isSimulation = true;
#endif
    }

    /// <summary>
    /// True when running without the IBAPI reference (simulation mode).
    /// </summary>
    public bool IsSimulation => _isSimulation;

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
        new ProviderCredentialField("Port", null, "TWS/Gateway Port", false, "7496"),
        new ProviderCredentialField("ClientId", null, "Client ID", false, "0")
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


    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);

    public int SubscribeMarketDepth(SymbolConfig cfg) => _inner.SubscribeMarketDepth(cfg);
    public void UnsubscribeMarketDepth(int subscriptionId) => _inner.UnsubscribeMarketDepth(subscriptionId);

    public int SubscribeTrades(SymbolConfig cfg) => _inner.SubscribeTrades(cfg);
    public void UnsubscribeTrades(int subscriptionId) => _inner.UnsubscribeTrades(subscriptionId);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

#if IBAPI
[ImplementsAdr("ADR-001", "Interactive Brokers API streaming data provider")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
internal sealed class IBMarketDataClientIBApi : IMarketDataClient
{
    private static readonly ILogger _log = Log.ForContext<IBMarketDataClientIBApi>();
    private readonly EnhancedIBConnectionManager _conn;
    private readonly IBCallbackRouter _router;

    // Track subscription ids if you want per-symbol teardown later
    public bool IsEnabled => true;

    public IBMarketDataClientIBApi(IMarketEventPublisher publisher, TradeDataCollector tradeCollector, MarketDepthCollector depthCollector, QuoteCollector? quoteCollector = null, OptionDataCollector? optionCollector = null)
    {
        // Router wires IB callbacks -> collectors (collectors already publish into publisher).
        // QuoteCollector enables Level 1 BBO quote emission from reqMktData callbacks.
        // OptionDataCollector enables live greeks from tickOptionComputation callbacks.
        _router = new IBCallbackRouter(depthCollector, tradeCollector, quoteCollector, optionCollector);
        _conn = new EnhancedIBConnectionManager(_router, host: "127.0.0.1", port: 7497, clientId: 1);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _conn.ConnectAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _conn.DisconnectAsync().ConfigureAwait(false);
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
        => _conn.SubscribeMarketDepth(cfg);

    public void UnsubscribeMarketDepth(int subscriptionId)
        => _conn.UnsubscribeMarketDepth(subscriptionId);

    public int SubscribeTrades(SymbolConfig cfg)
        => _conn.SubscribeTrades(cfg);

    public void UnsubscribeTrades(int subscriptionId)
        => _conn.UnsubscribeTrades(subscriptionId);

    public ValueTask DisposeAsync()
    {
        try
        {
            _conn.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error disposing IB connection manager during cleanup");
        }
        return ValueTask.CompletedTask;
    }
}
#endif
