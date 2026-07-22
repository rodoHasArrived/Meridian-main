using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaAssetStreamRoutingTests
{
    private static AlpacaOptions Options(string? optionsFeed = "opra", string cryptoFeed = "us", string newsFeed = "basic") =>
        new(KeyId: "key", SecretKey: "secret", OptionsFeed: optionsFeed, CryptoFeed: cryptoFeed, NewsFeed: newsFeed);

    private static (TradeDataCollector Trades, QuoteCollector Quotes) Collectors()
    {
        var publisher = new TestMarketEventPublisher();
        return (new TradeDataCollector(publisher), new QuoteCollector(publisher));
    }

    [Fact]
    public void Router_ResolvesEachEntitledAssetClass_WithItsOwnInstrumentClass()
    {
        var (trades, quotes) = Collectors();
        var options = Options();
        var streams = new IAlpacaAssetStream[]
        {
            new AlpacaMarketDataClient(trades, quotes, options),
            new AlpacaOptionsMarketDataClient(trades, quotes, options),
            new AlpacaCryptoMarketDataClient(trades, quotes, options),
            new AlpacaNewsMarketDataClient(trades, quotes, options, new AlpacaNewsEventBuffer())
        };
        var router = new AlpacaMarketDataRouter(streams);

        router.Resolve(MarketDataAssetClass.Options).SupportedInstrumentTypes.Should().Contain(InstrumentType.EquityOption);
        router.Resolve(MarketDataAssetClass.Crypto).SupportedInstrumentTypes.Should().ContainSingle().Which.Should().Be(InstrumentType.Crypto);
        router.Resolve(MarketDataAssetClass.News).Should().BeOfType<AlpacaNewsMarketDataClient>();
    }

    [Fact]
    public void Router_UnavailableOptionsEntitlement_FailsClosed_WithoutEquitiesFallback()
    {
        var (trades, quotes) = Collectors();
        var options = Options(optionsFeed: "indicative");
        var router = new AlpacaMarketDataRouter([
            new AlpacaMarketDataClient(trades, quotes, options),
            new AlpacaOptionsMarketDataClient(trades, quotes, options)]);

        router.TryResolve(MarketDataAssetClass.Options, out var stream).Should().BeFalse();
        stream.Should().BeNull();
        var action = () => router.Resolve(MarketDataAssetClass.Options);
        action.Should().Throw<InvalidOperationException>().WithMessage("*Options*");
    }


    [Fact]
    public void ReconnectSubscriptionPayload_RetainsOnlyTheAssetStreamSubscriptions()
    {
        var (trades, quotes) = Collectors();
        var client = new TestAlpacaClient(trades, quotes, Options());
        client.SubscribeTrades(new SymbolConfig("BTC/USD")).Should().BeGreaterThan(0);

        using var doc = JsonDocument.Parse(client.ReconnectSubscriptionPayload());
        doc.RootElement.GetProperty("trades").EnumerateArray().Select(x => x.GetString())
            .Should().ContainSingle().Which.Should().Be("BTC/USD");
        doc.RootElement.TryGetProperty("news", out _).Should().BeFalse();
    }

    [Fact]
    public void NewsSubscription_UsesNewsChannel_AndNewsDoesNotExposeTradeOrQuoteSubscriptions()
    {
        var (trades, quotes) = Collectors();
        var client = new AlpacaNewsMarketDataClient(trades, quotes, Options(), new AlpacaNewsEventBuffer());
        client.SubscribeTrades(new SymbolConfig("AAPL")).Should().Be(-1);
        client.SubscribeMarketDepth(new SymbolConfig("AAPL")).Should().Be(-1);
        client.SubscribeNews(new SymbolConfig("AAPL")).Should().BeGreaterThan(0);

        using var doc = JsonDocument.Parse(AlpacaMarketDataClient.BuildNewsSubscriptionMessage(["AAPL"]));
        doc.RootElement.GetProperty("news").EnumerateArray().Select(x => x.GetString()).Should().ContainSingle().Which.Should().Be("AAPL");
        doc.RootElement.TryGetProperty("trades", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("quotes", out _).Should().BeFalse();
    }

    [Fact]
    public void NewsMessage_NormalizesIntoNewsSink_WithoutPublishingPriceEvents()
    {
        var (trades, quotes) = Collectors();
        var sink = new AlpacaNewsEventBuffer();
        var client = new AlpacaNewsMarketDataClient(trades, quotes, Options(), sink);
        var method = typeof(AlpacaNewsMarketDataClient).GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var doc = JsonDocument.Parse("""{"T":"n","id":123,"headline":"Earnings beat","summary":"Results","url":"https://news","source":"wire","created_at":"2025-01-02T03:04:05Z","symbols":["AAPL"]}""");

        method.Invoke(client, [doc.RootElement]);

        sink.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(new AlpacaNewsEvent("123", "Earnings beat", "Results", "https://news", "wire", DateTimeOffset.Parse("2025-01-02T03:04:05Z"), ["AAPL"]));
    }


    [Fact]
    public void ProviderModule_RegistersCapabilityRouterAndAllAssetStreams()
    {
        var previousKey = Environment.GetEnvironmentVariable("ALPACA_KEY_ID");
        var previousSecret = Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ALPACA_KEY_ID", "key");
            Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", "secret");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient();
            var publisher = new TestMarketEventPublisher();
            services.AddSingleton<Meridian.Domain.Events.IMarketEventPublisher>(publisher);
            services.AddSingleton(new TradeDataCollector(publisher));
            services.AddSingleton(new QuoteCollector(publisher));
            new AlpacaProviderModule().Register(services, new DataSourceRegistry());

            using var provider = services.BuildServiceProvider();
            provider.GetServices<IAlpacaAssetStream>().Select(stream => stream.AssetClass).Should().BeEquivalentTo(
                [MarketDataAssetClass.Equities, MarketDataAssetClass.Options, MarketDataAssetClass.Crypto, MarketDataAssetClass.News]);
            provider.GetRequiredService<IAlpacaMarketDataRouter>().Resolve(MarketDataAssetClass.Crypto)
                .Should().BeOfType<AlpacaCryptoMarketDataClient>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALPACA_KEY_ID", previousKey);
            Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", previousSecret);
        }
    }

    private sealed class TestAlpacaClient(TradeDataCollector trades, QuoteCollector quotes, AlpacaOptions options)
        : AlpacaMarketDataClient(trades, quotes, options)
    {
        public string ReconnectSubscriptionPayload() => BuildSubscriptionPayload();
    }

    [Fact]
    public void CapabilityCatalog_DeclaresAllAlpacaStreamingAssetClasses()
    {
        var alpaca = Meridian.Infrastructure.Adapters.Core.ProviderCapabilityDescriptorCatalog.Descriptors.Single(x => x.ProviderId == "alpaca");
        alpaca.SupportedStreamingAssetClasses.Should().BeEquivalentTo([MarketDataAssetClass.Equities, MarketDataAssetClass.Options, MarketDataAssetClass.Crypto, MarketDataAssetClass.News]);
    }
}
