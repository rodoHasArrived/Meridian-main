using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderMarketDataCapabilityTests
{
    [Fact]
    public void IbkrStreamingCatalog_ExposesGranularEntitlementAndPacingMetadata()
    {
        var publisher = new TestMarketEventPublisher();
        var provider = new IBMarketDataClient(
            publisher,
            new TradeDataCollector(publisher),
            new MarketDepthCollector(publisher));

        ProviderCatalogEntry catalogEntry = ProviderTemplateFactory.ToCatalogEntry(provider);

        var depth = catalogEntry.Capabilities.MarketDataCapabilities
            .Single(capability => capability.Capability == "Level2Book");
        depth.AssetClasses.Should().NotBeEmpty();
        depth.Geographies.Should().NotBeEmpty();
        depth.Venues.Should().Contain("SMART");
        depth.Feed.Should().Be("IBKR TWS/Gateway");
        depth.Delivery.Should().Be("Real-time or delayed");
        depth.EntitlementState.Should().Contain("subscription");
        depth.MaxRequestsPerWindow.Should().Be(50);
        depth.PacingWindowSeconds.Should().Be(1);
        depth.MinimumRequestDelayMs.Should().Be(20);
        depth.SourceTimestamp.Should().NotBeNullOrWhiteSpace();
        depth.QualityPosture.Should().Contain("Entitlement-dependent");
    }

    [Fact]
    public void DerivedCapabilities_UseTheGranularTaxonomyForImplementedProducts()
    {
        var capabilities = ProviderCapabilities.Hybrid(depth: true) with
        {
            SupportsHistoricalTrades = true,
            SupportsHistoricalQuotes = true,
            SupportsHistoricalAuctions = true,
            SupportsOptionsChain = true,
            SupportedAssetTypes = new[] { "crypto" }
        };

        capabilities.MarketDataCapabilities.Select(static profile => profile.Capability)
            .Should().Contain(new[]
            {
                MarketDataCapabilityKind.Trades, MarketDataCapabilityKind.NbboQuotes,
                MarketDataCapabilityKind.Level1Snapshot, MarketDataCapabilityKind.Level2Book,
                MarketDataCapabilityKind.TickByTick, MarketDataCapabilityKind.Bars,
                MarketDataCapabilityKind.HistoricalTrades, MarketDataCapabilityKind.HistoricalQuotes,
                MarketDataCapabilityKind.Auctions, MarketDataCapabilityKind.Options,
                MarketDataCapabilityKind.Crypto, MarketDataCapabilityKind.CorporateActions
            });
    }
}
