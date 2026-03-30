using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.StockSharp;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

/// <summary>
/// Tests for <see cref="ProviderRegistry"/> streaming client creation via
/// the dictionary-based factory approach that replaced MarketDataClientFactory.
/// </summary>
public sealed class MarketDataClientFactoryTests
{
    [Fact]
    public void SupportedSources_ContainsExpectedProviders()
    {
        // Arrange
        var registry = CreateRegistryWithFactories();

        // Assert — SupportedStreamingSources is now IReadOnlyList<string>
        registry.SupportedStreamingSources.Should().Contain("ib");
        registry.SupportedStreamingSources.Should().Contain("alpaca");
        registry.SupportedStreamingSources.Should().Contain("polygon");
        registry.SupportedStreamingSources.Should().Contain("stocksharp");
    }

    [Fact]
    public void CreateStreamingClient_IB_ReturnsIBClient()
    {
        // Arrange
        var registry = CreateRegistryWithFactories();

        // Act
        var client = registry.CreateStreamingClient(DataSourceKind.IB);

        // Assert
        client.Should().BeOfType<IBMarketDataClient>();
    }

    [Fact]
    public void CreateStreamingClient_Polygon_ReturnsPolygonClient()
    {
        // Arrange
        var registry = CreateRegistryWithFactories();

        // Act
        var client = registry.CreateStreamingClient(DataSourceKind.Polygon);

        // Assert
        client.Should().BeOfType<PolygonMarketDataClient>();
    }

    [Fact]
    public void CreateStreamingClient_Alpaca_ReturnsAlpacaClient()
    {
        // Arrange
        var registry = CreateRegistryWithFactories();

        // Act
        var client = registry.CreateStreamingClient(DataSourceKind.Alpaca);

        // Assert
        client.Should().BeOfType<AlpacaMarketDataClient>();
    }

    [Fact]
    public void CreateStreamingClient_UnknownDataSource_FallsBackToIB()
    {
        // Arrange
        var registry = CreateRegistryWithFactories();

        // Act - use an enum value with no registered factory; falls back to IB
        var client = registry.CreateStreamingClient((DataSourceKind)200);

        // Assert
        client.Should().BeOfType<IBMarketDataClient>();
    }

    [Fact]
    public void CreateStreamingClient_Alpaca_UsesCustomCredentialResolver()
    {
        // Arrange
        var resolverCalled = false;
        var registry = new ProviderRegistry();
        var (_, publisher, trade, depth, quote) = CreateDependencies();
        var config = new AppConfig { Alpaca = new AlpacaOptions { KeyId = "k", SecretKey = "s" } };

        registry.RegisterStreamingFactory(DataSourceKind.Alpaca, () =>
        {
            resolverCalled = true;
            return new AlpacaMarketDataClient(trade, quote,
                config.Alpaca! with { KeyId = "custom-key", SecretKey = "custom-secret" });
        });

        // Act
        registry.CreateStreamingClient(DataSourceKind.Alpaca);

        // Assert
        resolverCalled.Should().BeTrue();
    }

    [Fact]
    public void CreateStreamingClient_ThrowsWhenNoFactoryAndNoFallback()
    {
        // Arrange - empty registry with no IB fallback
        var registry = new ProviderRegistry();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            registry.CreateStreamingClient(DataSourceKind.IB));
    }

    [Fact]
    public void RegisterStreamingFactory_ReplacesExistingFactory()
    {
        // Arrange
        var registry = new ProviderRegistry();
        var (_, publisher, trade, depth, quote) = CreateDependencies();
        registry.RegisterStreamingFactory(DataSourceKind.IB, () =>
            new IBMarketDataClient(publisher, trade, depth));

        // Act - register a second factory for the same kind
        var secondFactoryCalled = false;
        registry.RegisterStreamingFactory(DataSourceKind.IB, () =>
        {
            secondFactoryCalled = true;
            return new IBMarketDataClient(publisher, trade, depth);
        });
        registry.CreateStreamingClient(DataSourceKind.IB);

        // Assert
        secondFactoryCalled.Should().BeTrue();
    }

    private static ProviderRegistry CreateRegistryWithFactories()
    {
        var registry = new ProviderRegistry();
        var (config, publisher, trade, depth, quote) = CreateDependencies();

        registry.RegisterStreamingFactory(DataSourceKind.IB, () =>
            new IBMarketDataClient(publisher, trade, depth));

        registry.RegisterStreamingFactory(DataSourceKind.Alpaca, () =>
            new AlpacaMarketDataClient(trade, quote,
                config.Alpaca! with { KeyId = "test-key", SecretKey = "test-secret" }));

        registry.RegisterStreamingFactory(DataSourceKind.Polygon, () =>
            new PolygonMarketDataClient(publisher, trade, quote));

        registry.RegisterStreamingFactory(DataSourceKind.StockSharp, () =>
            new StockSharpMarketDataClient(trade, depth, quote, new StockSharpConfig()));

        return registry;
    }

    private static (AppConfig config, IMarketEventPublisher publisher, TradeDataCollector trade, MarketDepthCollector depth, QuoteCollector quote) CreateDependencies()
    {
        var config = new AppConfig { Alpaca = new AlpacaOptions() };
        IMarketEventPublisher publisher = new TestMarketEventPublisher();
        var trade = new TradeDataCollector(publisher);
        var depth = new MarketDepthCollector(publisher);
        var quote = new QuoteCollector(publisher);
        return (config, publisher, trade, depth, quote);
    }
}
