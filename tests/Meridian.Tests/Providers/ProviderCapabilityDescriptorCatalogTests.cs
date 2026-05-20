using Meridian.Execution.Sdk;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Providers;

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
    public void Descriptors_with_capabilities_are_resolvable_from_registration_paths()
    {
        var services = new ServiceCollection();
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            foreach (var implementation in descriptor.Implementations())
            {
                services.AddSingleton(implementation);
            }
        }

        RegisterInterfacesFromDescriptors(services);
        using var provider = services.BuildServiceProvider();

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
