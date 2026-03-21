using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Shared;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Test-only Polygon stub client used to exercise subscription behavior without live credentials.
/// Production Polygon behavior now fails fast when credentials are missing.
/// </summary>
public sealed class PolygonStubClient : IMarketDataClient
{
    private readonly IMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly SubscriptionManager _subscriptions = new(ProviderSubscriptionRanges.PolygonStart);
    private bool _connected;

    public PolygonStubClient(IMarketEventPublisher publisher, TradeDataCollector tradeCollector)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
    }

    public bool IsEnabled => false;
    public bool IsConnected => _connected;
    public string ProviderId => "polygon-stub";
    public string ProviderDisplayName => "Polygon Stub";
    public string ProviderDescription => "Test-only Polygon stub client";
    public int ProviderPriority => int.MaxValue;
    public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming(trades: true, quotes: true, depth: false);

    public IReadOnlyList<string> SubscribedAggregateSymbols => _subscriptions.GetSymbolsByKind("aggregates");

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _publisher.TryPublish(MarketEvent.Heartbeat(DateTimeOffset.UtcNow, source: "PolygonStub"));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return -1;
    }

    public void UnsubscribeMarketDepth(int subscriptionId)
    {
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var symbol = cfg.Symbol.Trim().ToUpperInvariant();
        var id = _subscriptions.Subscribe(symbol, "trades");
        if (id == -1)
            return -1;

        _tradeCollector.OnTrade(new MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100m,
            Size: 1,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0,
            StreamId: "POLYGON_STUB",
            Venue: "POLYGON"));

        return id;
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        _subscriptions.Unsubscribe(subscriptionId);
    }

    public int SubscribeAggregates(SymbolConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return _subscriptions.Subscribe(cfg.Symbol.Trim().ToUpperInvariant(), "aggregates");
    }

    public void UnsubscribeAggregates(int subscriptionId)
    {
        _subscriptions.Unsubscribe(subscriptionId);
    }

    public ValueTask DisposeAsync()
    {
        _subscriptions.Clear();
        return ValueTask.CompletedTask;
    }
}
