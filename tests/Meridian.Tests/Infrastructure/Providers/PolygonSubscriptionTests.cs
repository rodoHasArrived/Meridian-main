using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Adapters;

/// <summary>
/// Unit tests for Polygon subscription management and reconnect behavior.
/// Part of B3 tranche 1 (infrastructure provider unit tests) improvement.
/// Tests subscription lifecycle, resubscription after reconnect, and multi-symbol scenarios.
/// </summary>
public sealed class PolygonSubscriptionTests : IDisposable
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private IReadOnlyList<MarketEvent> _publishedEvents => _publisher.PublishedEvents;

    private readonly string? _originalPolygonApiKey;
    private readonly string? _originalPolygonApiKeyAlt;

    public PolygonSubscriptionTests()
    {
        _publisher = new TestMarketEventPublisher();
        _tradeCollector = new TradeDataCollector(_publisher, null);
        _quoteCollector = new QuoteCollector(_publisher);

        _originalPolygonApiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY");
        _originalPolygonApiKeyAlt = Environment.GetEnvironmentVariable("POLYGON__APIKEY");
        Environment.SetEnvironmentVariable("POLYGON_API_KEY", null);
        Environment.SetEnvironmentVariable("POLYGON__APIKEY", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("POLYGON_API_KEY", _originalPolygonApiKey);
        Environment.SetEnvironmentVariable("POLYGON__APIKEY", _originalPolygonApiKeyAlt);
    }

    private PolygonMarketDataClient CreateClient(PolygonOptions? options = null)
    {
        return new PolygonMarketDataClient(
            _publisher, _tradeCollector, _quoteCollector, options);
    }

    private PolygonStubClient CreateStubClient() => new(_publisher, _tradeCollector);

    #region Multi-Symbol Subscription Tests

    [Fact]
    public void SubscribeTrades_MultipleSymbols_ReturnsUniqueIds()
    {
        var client = CreateStubClient();
        var symbols = new[] { "AAPL", "MSFT", "GOOGL" };
        var ids = new List<int>();

        foreach (var symbol in symbols)
        {
            var id = client.SubscribeTrades(new SymbolConfig(symbol));
            ids.Add(id);
        }

        ids.Should().HaveCount(3);
        ids.Distinct().Should().HaveCount(3, "each subscription should have a unique ID");
    }

    [Fact]
    public void SubscribeTrades_MultipleSymbols_EmitsTradeForEach()
    {
        var client = CreateStubClient();
        var symbols = new[] { "AAPL", "MSFT", "GOOGL" };

        foreach (var symbol in symbols)
        {
            client.SubscribeTrades(new SymbolConfig(symbol));
        }

        _publishedEvents.Where(e => e.Type == MarketEventType.Trade)
            .Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void UnsubscribeTrades_AfterMultipleSubscriptions_DoesNotThrow()
    {
        var client = CreateStubClient();
        var id1 = client.SubscribeTrades(new SymbolConfig("AAPL"));
        var id2 = client.SubscribeTrades(new SymbolConfig("MSFT"));

        var act1 = () => client.UnsubscribeTrades(id1);
        var act2 = () => client.UnsubscribeTrades(id2);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void UnsubscribeTrades_NonExistentId_DoesNotThrow()
    {
        var client = CreateStubClient();

        var act = () => client.UnsubscribeTrades(99999);
        act.Should().NotThrow();
    }

    #endregion

    #region Aggregate Subscription Lifecycle Tests

    [Fact]
    public void SubscribeAggregates_MultipleSymbols_TracksAll()
    {
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = CreateClient(options);

        client.SubscribeAggregates(new SymbolConfig("SPY"));
        client.SubscribeAggregates(new SymbolConfig("QQQ"));
        client.SubscribeAggregates(new SymbolConfig("DIA"));

        client.SubscribedAggregateSymbols.Should().Contain("SPY");
        client.SubscribedAggregateSymbols.Should().Contain("QQQ");
        client.SubscribedAggregateSymbols.Should().Contain("DIA");
    }

    [Fact]
    public void SubscribeAggregates_DuplicateSymbol_DoesNotDuplicate()
    {
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = CreateClient(options);

        client.SubscribeAggregates(new SymbolConfig("SPY"));
        client.SubscribeAggregates(new SymbolConfig("SPY"));

        // Should track exactly once
        client.SubscribedAggregateSymbols.Count(s => s == "SPY").Should().Be(1);
    }

    [Fact]
    public void UnsubscribeAggregates_AllSymbols_ClearsTracking()
    {
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = CreateClient(options);

        var id1 = client.SubscribeAggregates(new SymbolConfig("SPY"));
        var id2 = client.SubscribeAggregates(new SymbolConfig("QQQ"));

        client.UnsubscribeAggregates(id1);
        client.UnsubscribeAggregates(id2);

        client.SubscribedAggregateSymbols.Should().BeEmpty();
    }

    #endregion

    #region Connection Lifecycle Tests

    [Fact]
    public async Task ConnectAsync_ThenDisconnect_CompletesCleanly()
    {
        var client = CreateStubClient();

        await client.ConnectAsync();
        await client.DisconnectAsync();

        // Should complete without throwing
        _publishedEvents.Should().Contain(e => e.Type == MarketEventType.Heartbeat);
    }

    [Fact]
    public async Task ConnectAsync_MultipleConnections_DoesNotThrow()
    {
        var client = CreateStubClient();

        await client.ConnectAsync();
        await client.ConnectAsync();

        // Double connect should be idempotent
        _publishedEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DisconnectAsync_WithoutConnect_DoesNotThrow()
    {
        var client = CreateStubClient();

        // Should complete without throwing even if not connected
        await client.DisconnectAsync();
    }

    #endregion

    #region Provider Metadata Tests

    [Fact]
    public void ProviderMetadata_HasExpectedDefaults()
    {
        var client = CreateStubClient();

        var metadata = client as Meridian.Infrastructure.IMarketDataClient;
        metadata.Should().NotBeNull();
        client.IsEnabled.Should().BeFalse("stub clients are test-only and not live-enabled");
    }

    [Fact]
    public void IsEnabled_WithValidKey_ReturnsTrue()
    {
        var options = new PolygonOptions(ApiKey: "valid_polygon_api_key_12345678901234");
        var client = CreateClient(options);

        client.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region Options Configuration Tests

    [Fact]
    public void Constructor_DefaultOptions_DisablesAggregates()
    {
        var options = new PolygonOptions();
        var client = CreateClient(options);
        var config = new SymbolConfig("SPY");

        var id = client.SubscribeAggregates(config);
        id.Should().Be(-1, "aggregates disabled by default");
    }

    [Fact]
    public void Constructor_CustomFeed_AcceptsConfiguration()
    {
        var options = new PolygonOptions(
            ApiKey: "valid_polygon_api_key_12345678901234",
            Feed: "crypto");
        var client = CreateClient(options);

        client.Should().NotBeNull();
        client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_DelayedFeed_AcceptsConfiguration()
    {
        var options = new PolygonOptions(
            ApiKey: "valid_polygon_api_key_12345678901234",
            UseDelayed: true);
        var client = CreateClient(options);

        client.Should().NotBeNull();
        client.IsEnabled.Should().BeTrue();
    }

    #endregion
}
