using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Robinhood;
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
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();
        services.AddSingleton<QuoteCollector>();
        services.AddSingleton<TradeDataCollector>();

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
    public void Scenario_RuntimeCapabilityDrift_FreeTierBackfillProvidersRemainFailClosed()
    {
        string[] inventoryOnlyBackfillProviders =
        [
            "alphavantage",
            "finnhub",
            "fred",
            "nasdaq",
            "stooq",
            "tiingo",
            "twelvedata",
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
