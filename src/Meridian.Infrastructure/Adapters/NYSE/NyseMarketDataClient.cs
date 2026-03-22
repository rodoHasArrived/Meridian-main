using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using System.Collections.Concurrent;
using System.Reactive.Disposables;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Bridges <see cref="NYSEDataSource"/> into the unified <see cref="IMarketDataClient"/> abstraction
/// used by the provider registry.
/// </summary>
[DataSource("nyse-streaming", "NYSE Streaming", Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Exchange,
    Priority = 5, Description = "Unified NYSE streaming client backed by NYSEDataSource")]
public sealed class NyseMarketDataClient : IMarketDataClient
{
    private readonly NYSEDataSource _source;
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ConcurrentDictionary<int, int> _linkedQuoteSubscriptions = new();

    public NyseMarketDataClient(
        TradeDataCollector tradeCollector,
        MarketDepthCollector depthCollector,
        QuoteCollector quoteCollector,
        NYSEOptions? options = null)
    {
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _depthCollector = depthCollector ?? throw new ArgumentNullException(nameof(depthCollector));
        _quoteCollector = quoteCollector ?? throw new ArgumentNullException(nameof(quoteCollector));
        _source = new NYSEDataSource(options ?? new NYSEOptions(), logger: LoggingSetup.ForContext<NYSEDataSource>());

        _subscriptions.Add(_source.Trades.Subscribe(OnTrade));
        _subscriptions.Add(_source.Quotes.Subscribe(OnQuote));
        _subscriptions.Add(_source.DepthUpdates.Subscribe(OnDepth));
    }

    public bool IsEnabled => true;

    public string ProviderId => "nyse";

    public string ProviderDisplayName => "NYSE Direct";

    public string ProviderDescription => "NYSE direct market data feed bridged into the unified streaming provider model";

    public int ProviderPriority => 5;

    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(
        trades: true,
        quotes: true,
        depth: true) with
    {
        SupportedMarkets = new[] { "US" }
    };

    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("ApiKey", "NYSE_API_KEY", "NYSE API Key", true),
        new ProviderCredentialField("ApiSecret", "NYSE_API_SECRET", "NYSE API Secret", true),
        new ProviderCredentialField("ClientId", "NYSE_CLIENT_ID", "NYSE Client ID", false)
    };

    public string[] ProviderNotes => new[]
    {
        "Uses the NYSE Direct websocket + REST authentication flow.",
        "Streaming lifecycle is handled by the shared WebSocketConnectionManager via NYSEDataSource."
    };

    public string[] ProviderWarnings => new[]
    {
        "Requires NYSE credentials and feed entitlements."
    };

    public Task ConnectAsync(CancellationToken ct = default)
        => _source.ConnectAsync(ct);

    public Task DisconnectAsync(CancellationToken ct = default)
        => _source.DisconnectAsync(ct);

    public int SubscribeMarketDepth(SymbolConfig cfg)
        => _source.SubscribeMarketDepth(cfg);

    public void UnsubscribeMarketDepth(int subscriptionId)
        => _source.UnsubscribeMarketDepth(subscriptionId);

    public int SubscribeTrades(SymbolConfig cfg)
    {
        var subId = _source.SubscribeTrades(cfg);
        var quoteSubId = _source.SubscribeQuotes(cfg);
        _linkedQuoteSubscriptions[subId] = quoteSubId;
        return subId;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        _source.UnsubscribeTrades(subscriptionId);

        if (_linkedQuoteSubscriptions.TryRemove(subscriptionId, out var quoteSubscriptionId))
        {
            _source.UnsubscribeQuotes(quoteSubscriptionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _subscriptions.Dispose();
        await _source.DisposeAsync().ConfigureAwait(false);
    }

    private void OnTrade(RealtimeTrade trade)
    {
        _tradeCollector.OnTrade(new MarketTradeUpdate(
            trade.Timestamp,
            trade.Symbol,
            trade.Price,
            trade.Size,
            trade.Side,
            trade.SequenceNumber ?? 0,
            trade.SourceId,
            trade.Exchange,
            SplitConditions(trade.Conditions)));
    }

    private void OnQuote(RealtimeQuote quote)
    {
        _quoteCollector.OnQuote(new MarketQuoteUpdate(
            quote.Timestamp,
            quote.Symbol,
            quote.BidPrice,
            quote.BidSize,
            quote.AskPrice,
            quote.AskSize,
            quote.SequenceNumber,
            quote.SourceId,
            quote.BidExchange ?? quote.AskExchange));
    }

    private void OnDepth(RealtimeDepthUpdate depth)
    {
        _depthCollector.OnDepth(new MarketDepthUpdate(
            depth.Timestamp,
            depth.Symbol,
            (ushort)Math.Max(0, depth.Level),
            depth.Operation,
            depth.Side,
            depth.Price,
            depth.Size,
            depth.MarketMaker,
            depth.SequenceNumber ?? 0,
            depth.SourceId));
    }

    private static string[]? SplitConditions(string? conditions)
    {
        if (string.IsNullOrWhiteSpace(conditions))
        {
            return null;
        }

        return conditions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
