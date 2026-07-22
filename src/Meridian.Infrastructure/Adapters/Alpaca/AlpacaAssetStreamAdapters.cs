using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>Marker for independently connectable Alpaca asset-class stream adapters.</summary>
public interface IAlpacaAssetStream
{
    MarketDataAssetClass AssetClass { get; }
}

/// <summary>Dedicated Alpaca US options WebSocket adapter.</summary>
public sealed class AlpacaOptionsMarketDataClient : AlpacaMarketDataClient
{
    public AlpacaOptionsMarketDataClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options)
        : base(trades, quotes, options) { }

    public override MarketDataAssetClass AssetClass => MarketDataAssetClass.Options;
    public override string ProviderId => "alpaca-options-stream";
    public override string ProviderDisplayName => "Alpaca Options Streaming";

    protected override Uri BuildWebSocketUri() => AlpacaEndpoints.OptionsWssUri(Host);

    private string Host => Options.UseSandbox ? AlpacaEndpoints.SandboxHost : AlpacaEndpoints.LiveHost;
}

/// <summary>Dedicated Alpaca US crypto WebSocket adapter.</summary>
public sealed class AlpacaCryptoMarketDataClient : AlpacaMarketDataClient
{
    public AlpacaCryptoMarketDataClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options)
        : base(trades, quotes, options) { }

    public override MarketDataAssetClass AssetClass => MarketDataAssetClass.Crypto;
    public override string ProviderId => "alpaca-crypto-stream";
    public override string ProviderDisplayName => "Alpaca Crypto Streaming";

    protected override Uri BuildWebSocketUri() => AlpacaEndpoints.CryptoWssUri(Host);

    private string Host => Options.UseSandbox ? AlpacaEndpoints.SandboxHost : AlpacaEndpoints.LiveHost;
}

/// <summary>
/// Dedicated Alpaca news WebSocket adapter. News does not implement trade or quote subscriptions;
/// callers can still connect and surface its entitlement independently from price streams.
/// </summary>
public sealed class AlpacaNewsMarketDataClient : AlpacaMarketDataClient
{
    public AlpacaNewsMarketDataClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options)
        : base(trades, quotes, options) { }

    public override MarketDataAssetClass AssetClass => MarketDataAssetClass.News;
    public override string ProviderId => "alpaca-news-stream";
    public override string ProviderDisplayName => "Alpaca News Streaming";

    protected override Uri BuildWebSocketUri() => AlpacaEndpoints.NewsWssUri(Host);

    public override int SubscribeTrades(Meridian.Contracts.Configuration.SymbolConfig cfg) => -1;

    public override int SubscribeMarketDepth(Meridian.Contracts.Configuration.SymbolConfig cfg) => -1;

    private string Host => Options.UseSandbox ? AlpacaEndpoints.SandboxHost : AlpacaEndpoints.LiveHost;
}
