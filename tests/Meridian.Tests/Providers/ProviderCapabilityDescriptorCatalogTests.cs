using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.ProviderSdk;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Providers;

/// <summary>
/// Guards provider capability metadata against runtime-readiness drift for inventory-only ingestion providers.
/// </summary>
public sealed class ProviderCapabilityDescriptorCatalogTests
{
    [Fact]
    public void Descriptors_match_implemented_interfaces()
    {
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            if (descriptor.Streaming is not null)
            {
                descriptor.Streaming.Should().BeAssignableTo<IMarketDataClient>();
            }

            if (descriptor.Historical is not null)
            {
                descriptor.Historical.Should().BeAssignableTo<IHistoricalDataProvider>();
            }

            if (descriptor.Search is not null)
            {
                descriptor.Search.Should().BeAssignableTo<ISymbolSearchProvider>();
            }

            if (descriptor.CorporateActions is not null)
            {
                descriptor.CorporateActions.Should().BeAssignableTo<ICorporateActionProvider>();
            }

            if (descriptor.Options is not null)
            {
                descriptor.Options.Should().BeAssignableTo<IOptionsChainProvider>();
            }

            if (descriptor.Brokerage is not null)
            {
                descriptor.Brokerage.Should().BeAssignableTo<IBrokerageGateway>();
            }
        }
    }

    [Fact]
    public async Task Descriptors_with_capabilities_are_resolvable_from_registration_paths()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new AlpacaOptions(
            KeyId: "AKTESTDESCRIPTOR0001",
            SecretKey: "descriptor-secret-for-di-tests"));
        services.AddSingleton(new IBOptions());
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();
        services.AddSingleton<QuoteCollector>();
        services.AddSingleton<TradeDataCollector>();
        services.AddSingleton<MarketDepthCollector>();

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            foreach (var implementation in descriptor.Implementations())
            {
                services.AddSingleton(implementation);
            }
        }

        RegisterInterfacesFromDescriptors(services);
        await using var provider = services.BuildServiceProvider();

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            AssertResolvable(provider, descriptor.ProviderId, descriptor.Streaming, typeof(IMarketDataClient));
            AssertResolvable(provider, descriptor.ProviderId, descriptor.Historical, typeof(IHistoricalDataProvider));
            AssertResolvable(provider, descriptor.ProviderId, descriptor.Search, typeof(ISymbolSearchProvider));
            AssertResolvable(provider, descriptor.ProviderId, descriptor.CorporateActions, typeof(ICorporateActionProvider));
            AssertResolvable(provider, descriptor.ProviderId, descriptor.Options, typeof(IOptionsChainProvider));
            AssertResolvable(provider, descriptor.ProviderId, descriptor.Brokerage, typeof(IBrokerageGateway));
        }
    }

    [Fact]
    public void Scenario_RuntimeCapabilityDrift_SupportedRuntimeProvidersExposeImplementedDescriptors()
    {
        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        descriptorsById.Keys.Should().Contain("robinhood");
        var robinhood = descriptorsById["robinhood"];
        robinhood.Streaming.Should().Be(typeof(RobinhoodMarketDataClient));
        robinhood.Historical.Should().Be(typeof(RobinhoodHistoricalDataProvider));
        robinhood.Search.Should().Be(typeof(RobinhoodSymbolSearchProvider));
        robinhood.Options.Should().Be(typeof(RobinhoodOptionsChainProvider));
        robinhood.Brokerage.Should().Be(typeof(RobinhoodBrokerageGateway));
        robinhood.CorporateActions.Should().BeNull(
            "Robinhood has no dedicated ICorporateActionProvider implementation in the runtime catalog");

        var ibkr = descriptorsById["ib"];
        ibkr.Streaming.Should().Be(typeof(IBMarketDataClient));
        ibkr.Historical.Should().Be(typeof(IBHistoricalDataProvider));
        ibkr.Brokerage.Should().Be(typeof(IBBrokerageGateway));

        descriptorsById.Keys.Should().Contain("edgar");
        var edgar = descriptorsById["edgar"];
        edgar.Search.Should().Be(typeof(EdgarSymbolSearchProvider));
        edgar.Streaming.Should().BeNull();
        edgar.Historical.Should().BeNull();
        edgar.Options.Should().BeNull();
        edgar.Brokerage.Should().BeNull();
        edgar.CorporateActions.Should().BeNull(
            "EDGAR corporate-action support is routed through Security Master/reference-data workflows, not an ICorporateActionProvider implementation");
    }

    [Fact]
    public void Scenario_BrokerageExperimentGate_MapperOnlyBrokersDoNotAdvertiseRuntimeDescriptors()
    {
        string[] mapperOnlyBrokerageProviderIds =
        [
            "tradier",
            "tradestation"
        ];

        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);
        var descriptorImplementations = ProviderCapabilityDescriptorCatalog.Descriptors
            .SelectMany(static descriptor => descriptor.Implementations())
            .Select(static implementation => implementation.FullName ?? implementation.Name)
            .ToArray();

        descriptorsById.Keys.Should().NotContain(mapperOnlyBrokerageProviderIds,
            "mapper-only brokerage assets must not become runtime provider capabilities without concrete adapters and lifecycle evidence");
        descriptorImplementations.Should().NotContain(
            implementation => implementation.Contains(".Adapters.Tradier.", StringComparison.OrdinalIgnoreCase) ||
                              implementation.Contains(".Adapters.TradeStation.", StringComparison.OrdinalIgnoreCase),
            "mapper-only brokerage assets must not enter the capability descriptor catalog until they implement shared runtime provider contracts");
    }

    [Fact]
    public void Scenario_RuntimeCapabilityDrift_FreeTierBackfillProvidersRemainFailClosed()
    {
        string[] inventoryOnlyBackfillProviders =
        [
            "fred",
            "stooq",
            "yahoo"
        ];

        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        descriptorsById.Keys.Should().Contain(inventoryOnlyBackfillProviders);

        foreach (var providerId in inventoryOnlyBackfillProviders)
        {
            descriptorsById[providerId].Streaming.Should().BeNull(
                "inventory/backfill provider '{0}' must not advertise streaming readiness without a dedicated provider implementation and evidence",
                providerId);
            descriptorsById[providerId].CorporateActions.Should().BeNull(
                "inventory/backfill provider '{0}' must not advertise corporate-action readiness without a dedicated provider implementation and evidence",
                providerId);
            descriptorsById[providerId].Brokerage.Should().BeNull(
                "inventory/backfill provider '{0}' must not advertise brokerage readiness without a dedicated provider implementation and evidence",
                providerId);
        }

        var finnhub = descriptorsById["finnhub"];
        finnhub.Historical.Should().Be(typeof(FinnhubHistoricalDataProvider));
        finnhub.Search.Should().Be(typeof(FinnhubSymbolSearchProvider));
        finnhub.CorporateActions.Should().Be(typeof(FinnhubCorporateActionProvider));
        finnhub.Streaming.Should().BeNull(
            "Finnhub has no dedicated streaming provider implementation in the runtime catalog");
        finnhub.Brokerage.Should().BeNull(
            "Finnhub is a data provider and must not advertise brokerage readiness");

        var tiingo = descriptorsById["tiingo"];
        tiingo.Historical.Should().Be(typeof(TiingoHistoricalDataProvider));
        tiingo.Search.Should().Be(typeof(TiingoSymbolSearchProvider));
        tiingo.CorporateActions.Should().Be(typeof(TiingoCorporateActionProvider));
        tiingo.Streaming.Should().BeNull(
            "Tiingo has no dedicated streaming provider implementation in the runtime catalog");
        tiingo.Brokerage.Should().BeNull(
            "Tiingo is a data provider and must not advertise brokerage readiness");

        var alphaVantage = descriptorsById["alphavantage"];
        alphaVantage.Historical.Should().Be(typeof(AlphaVantageHistoricalDataProvider));
        alphaVantage.Search.Should().Be(typeof(AlphaVantageSymbolSearchProvider));
        alphaVantage.CorporateActions.Should().Be(typeof(AlphaVantageCorporateActionProvider));
        alphaVantage.Streaming.Should().BeNull(
            "Alpha Vantage has no dedicated streaming provider implementation in the runtime catalog");
        alphaVantage.Brokerage.Should().BeNull(
            "Alpha Vantage is a data provider and must not advertise brokerage readiness");

        var nasdaq = descriptorsById["nasdaq"];
        nasdaq.Historical.Should().Be(typeof(NasdaqDataLinkHistoricalDataProvider));
        nasdaq.Search.Should().Be(typeof(NasdaqDataLinkSymbolSearchProvider));
        nasdaq.CorporateActions.Should().Be(typeof(NasdaqDataLinkCorporateActionProvider));
        nasdaq.Streaming.Should().BeNull(
            "Nasdaq Data Link has no dedicated streaming provider implementation in the runtime catalog");
        nasdaq.Brokerage.Should().BeNull(
            "Nasdaq Data Link is a data provider and must not advertise brokerage readiness");

        var fred = descriptorsById["fred"];
        fred.Historical.Should().Be(typeof(FredHistoricalDataProvider));
        fred.Search.Should().Be(typeof(FredSymbolSearchProvider));
        fred.Streaming.Should().BeNull(
            "FRED has no dedicated streaming provider implementation in the runtime catalog");
        fred.CorporateActions.Should().BeNull(
            "FRED economic-series search is reference discovery, not corporate-action readiness");
        fred.Brokerage.Should().BeNull(
            "FRED is a research data provider and must not advertise brokerage readiness");

        var twelveData = descriptorsById["twelvedata"];
        twelveData.Historical.Should().Be(typeof(TwelveDataHistoricalDataProvider));
        twelveData.Search.Should().Be(typeof(TwelveDataSymbolSearchProvider));
        twelveData.CorporateActions.Should().Be(typeof(TwelveDataCorporateActionProvider));
        twelveData.Streaming.Should().BeNull(
            "Twelve Data has no dedicated streaming provider implementation in the runtime catalog");
        twelveData.Brokerage.Should().BeNull(
            "Twelve Data is a data provider and must not advertise brokerage readiness");
    }

    [Fact]
    public void Scenario_UnsupportedYahooStreaming_RuntimeCatalogKeepsHistoricalFallbackOnly()
    {
        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        descriptorsById.Keys.Should().Contain("yahoo");
        var yahoo = descriptorsById["yahoo"];
        yahoo.Historical.Should().Be(typeof(YahooFinanceHistoricalDataProvider));
        yahoo.Streaming.Should().BeNull(
            "Yahoo Finance has no dedicated IMarketDataClient implementation or reconnect/runtime evidence");
        yahoo.Search.Should().BeNull(
            "Yahoo Finance is not a symbol-search provider in the runtime catalog");
        yahoo.CorporateActions.Should().BeNull(
            "Yahoo adjusted-bar event extraction is historical evidence, not a registered ICorporateActionProvider");
        yahoo.Brokerage.Should().BeNull(
            "Yahoo Finance is a data fallback and must never advertise brokerage readiness");
    }

    private static void RegisterInterfacesFromDescriptors(IServiceCollection services)
    {
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            if (descriptor.Streaming is not null)
            {
                services.AddSingleton(typeof(IMarketDataClient), sp => sp.GetRequiredService(descriptor.Streaming));
            }

            if (descriptor.Historical is not null)
            {
                services.AddSingleton(typeof(IHistoricalDataProvider), sp => sp.GetRequiredService(descriptor.Historical));
            }

            if (descriptor.Search is not null)
            {
                services.AddSingleton(typeof(ISymbolSearchProvider), sp => sp.GetRequiredService(descriptor.Search));
            }

            if (descriptor.CorporateActions is not null)
            {
                services.AddSingleton(typeof(ICorporateActionProvider), sp => sp.GetRequiredService(descriptor.CorporateActions));
            }

            if (descriptor.Options is not null)
            {
                services.AddSingleton(typeof(IOptionsChainProvider), sp => sp.GetRequiredService(descriptor.Options));
            }

            if (descriptor.Brokerage is not null)
            {
                services.AddSingleton(typeof(IBrokerageGateway), sp => sp.GetRequiredService(descriptor.Brokerage));
            }
        }
    }

    private static void AssertResolvable(IServiceProvider provider, string providerId, Type? implementation, Type contract)
    {
        if (implementation is null)
        {
            return;
        }

        var instances = provider.GetServices(contract).ToList();
        instances.Should().NotBeEmpty($"provider '{providerId}' advertises {contract.Name}");
        instances.Any(instance => implementation.IsInstanceOfType(instance))
            .Should()
            .BeTrue($"provider '{providerId}' should resolve {implementation.Name} via {contract.Name}");
    }
}
