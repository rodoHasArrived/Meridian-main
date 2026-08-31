using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>Capability-aware contract for an independently connectable Alpaca market-data stream.</summary>
public interface IAlpacaAssetStream : IMarketDataClient
{
    MarketDataAssetClass AssetClass { get; }
    IReadOnlyList<InstrumentType> SupportedInstrumentTypes { get; }
    bool IsSubscriptionAvailable { get; }
}

/// <summary>Resolves an Alpaca stream only when its own entitlement permits subscriptions.</summary>
public interface IAlpacaMarketDataRouter
{
    IAlpacaAssetStream Resolve(MarketDataAssetClass assetClass);
    bool TryResolve(MarketDataAssetClass assetClass, out IAlpacaAssetStream? stream);
}

public sealed class AlpacaMarketDataRouter(IEnumerable<IAlpacaAssetStream> streams) : IAlpacaMarketDataRouter
{
    private readonly IReadOnlyDictionary<MarketDataAssetClass, IAlpacaAssetStream> _streams = streams
        .GroupBy(static stream => stream.AssetClass)
        .ToDictionary(static group => group.Key, static group => group.Single());

    public IAlpacaAssetStream Resolve(MarketDataAssetClass assetClass) => TryResolve(assetClass, out var stream)
        ? stream!
        : throw new InvalidOperationException($"Alpaca {assetClass} streaming is not configured with a usable entitlement.");

    public bool TryResolve(MarketDataAssetClass assetClass, out IAlpacaAssetStream? stream)
    {
        if (_streams.TryGetValue(assetClass, out stream) && stream.IsSubscriptionAvailable)
            return true;

        stream = null;
        return false;
    }
}

/// <summary>Normalized, provider-neutral-enough Alpaca news read model; never a trade or quote.</summary>
public sealed record AlpacaNewsEvent(
    string Id, string Headline, string Summary, string Url, string Source,
    DateTimeOffset Timestamp, IReadOnlyList<string> Symbols);

public interface IAlpacaNewsEventSink
{
    void Publish(AlpacaNewsEvent newsEvent);
}

/// <summary>In-memory bounded news read model for operator diagnostics and consumers.</summary>
public sealed class AlpacaNewsEventBuffer : IAlpacaNewsEventSink
{
    private const int Capacity = 1024;
    private readonly Queue<AlpacaNewsEvent> _events = new();
    private readonly object _gate = new();
    public IReadOnlyList<AlpacaNewsEvent> Events { get { lock (_gate) return _events.ToArray(); } }
    public void Publish(AlpacaNewsEvent newsEvent)
    {
        lock (_gate)
        {
            _events.Enqueue(newsEvent);
            while (_events.Count > Capacity)
                _events.Dequeue();
        }
    }
}

public interface IAlpacaNewsSubscriptionClient
{
    int SubscribeNews(SymbolConfig cfg);
    void UnsubscribeNews(int subscriptionId);
}

/// <summary>Dedicated Alpaca US options WebSocket adapter.</summary>
public sealed class AlpacaOptionsMarketDataClient : AlpacaMarketDataClient
{
    public AlpacaOptionsMarketDataClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options) : base(trades, quotes, options) { }
    public override MarketDataAssetClass AssetClass => MarketDataAssetClass.Options;
    public override IReadOnlyList<InstrumentType> SupportedInstrumentTypes => [InstrumentType.EquityOption, InstrumentType.IndexOption];
    public override bool IsSubscriptionAvailable => string.Equals(Options.OptionsFeed, "opra", StringComparison.OrdinalIgnoreCase);
    public override string ProviderId => "alpaca-options-stream";
    public override string ProviderDisplayName => "Alpaca Options Streaming";
    protected override Uri BuildWebSocketUri() => AlpacaEndpoints.OptionsWssUri(Host);
    private string Host => Options.UseSandbox ? AlpacaEndpoints.SandboxHost : AlpacaEndpoints.LiveHost;
}

/// <summary>Dedicated Alpaca US crypto WebSocket adapter.</summary>
public sealed class AlpacaCryptoMarketDataClient : AlpacaMarketDataClient
{
    public AlpacaCryptoMarketDataClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options) : base(trades, quotes, options) { }
    public override MarketDataAssetClass AssetClass => MarketDataAssetClass.Crypto;
    public override IReadOnlyList<InstrumentType> SupportedInstrumentTypes => [InstrumentType.Crypto];
    public override bool IsSubscriptionAvailable => IsConfiguredFeed(Options.CryptoFeed);
    public override string ProviderId => "alpaca-crypto-stream";
    public override string ProviderDisplayName => "Alpaca Crypto Streaming";
    protected override Uri BuildWebSocketUri() => AlpacaEndpoints.CryptoWssUri(Host);
    private string Host => Options.UseSandbox ? AlpacaEndpoints.SandboxHost : AlpacaEndpoints.LiveHost;
}

/// <summary>Dedicated news endpoint with a normalized news sink, never price collectors.</summary>
public sealed class AlpacaNewsMarketDataClient : AlpacaMarketDataClient, IAlpacaNewsSubscriptionClient
{
    private readonly IAlpacaNewsEventSink _sink;
    public AlpacaNewsMarketDataClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options, IAlpacaNewsEventSink sink) : base(trades, quotes, options)
        => _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    public override MarketDataAssetClass AssetClass => MarketDataAssetClass.News;
    public override IReadOnlyList<InstrumentType> SupportedInstrumentTypes => [];
    public override bool IsSubscriptionAvailable => IsConfiguredFeed(Options.NewsFeed);
    public override string ProviderId => "alpaca-news-stream";
    public override string ProviderDisplayName => "Alpaca News Streaming";
    protected override Uri BuildWebSocketUri() => AlpacaEndpoints.NewsWssUri(Host);
    public override int SubscribeTrades(SymbolConfig cfg) => -1;
    public override int SubscribeMarketDepth(SymbolConfig cfg) => -1;
    public int SubscribeNews(SymbolConfig cfg) => Subscribe(cfg, "news");
    public void UnsubscribeNews(int subscriptionId) => Unsubscribe(subscriptionId);
    protected override string BuildSubscriptionPayload() => BuildNewsSubscriptionMessage(Subscriptions.GetSymbolsByKind("news"));
    protected override void HandleMessage(System.Text.Json.JsonElement element)
    {
        if (!element.TryGetProperty("T", out var type) || type.GetString() != "n")
            return;
        var timestamp = element.TryGetProperty("created_at", out var created) && DateTimeOffset.TryParse(created.GetString(), out var parsed) ? parsed : default;
        var id = element.TryGetProperty("id", out var idProp) ? idProp.ValueKind == System.Text.Json.JsonValueKind.String ? idProp.GetString() : idProp.ToString() : null;
        var headline = element.TryGetProperty("headline", out var headlineProp) ? headlineProp.GetString() : null;
        if (timestamp == default || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(headline))
            return;
        var symbols = element.TryGetProperty("symbols", out var symbolsProp) && symbolsProp.ValueKind == System.Text.Json.JsonValueKind.Array
            ? symbolsProp.EnumerateArray().Select(static item => item.GetString()).Where(static symbol => !string.IsNullOrWhiteSpace(symbol)).Select(static symbol => symbol!).ToArray() : [];
        _sink.Publish(new AlpacaNewsEvent(id!, headline!, element.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("source", out var source) ? source.GetString() ?? string.Empty : string.Empty, timestamp, symbols));
    }
    private string Host => Options.UseSandbox ? AlpacaEndpoints.SandboxHost : AlpacaEndpoints.LiveHost;
}
