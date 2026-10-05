using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.ProviderSdk;

public sealed class ProviderModuleRegistrationTests
{
    [Fact]
    public async Task Successful_module_publishes_its_factory_contracts_without_constructing_adapters()
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();
        var module = new RegistrationMutationModule((collection, _) =>
            collection.AddSingleton<StooqHistoricalDataProvider>(_ =>
                throw new InvalidOperationException("Composition must not instantiate the adapter.")));

        var report = await new ProviderModuleLoader().LoadModulesAsync(services, registry, [module]);

        report.AllLoaded.Should().BeTrue();
        registry.ModuleCapabilityRegistrations.Should().ContainSingle().Which.Should().Be(
            new ProviderModuleCapabilityRegistration("stooq", typeof(IHistoricalDataProvider), typeof(StooqHistoricalDataProvider)));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(StooqHistoricalDataProvider));
    }

    [Fact]
    public async Task Existing_service_and_attributes_do_not_publish_a_module_factory()
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(typeof(StooqHistoricalDataProvider).Assembly);
        var services = new ServiceCollection();
        services.AddSingleton<StooqHistoricalDataProvider>();
        var module = new RegistrationMutationModule((collection, _) => collection.AddSingleton<ModuleMarker>());

        var report = await new ProviderModuleLoader().LoadModulesAsync(services, registry, [module]);

        report.AllLoaded.Should().BeTrue();
        registry.Sources.Should().Contain(source => source.ImplementationType == typeof(StooqHistoricalDataProvider));
        registry.ModuleCapabilityRegistrations.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_module_preserves_original_services_and_rolls_back_discovered_sources()
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();
        services.AddSingleton(new ModuleMarker());
        var originalDescriptors = services.ToArray();
        var module = new RegistrationMutationModule((collection, dataSources) =>
        {
            collection.Clear();
            collection.AddSingleton<StooqHistoricalDataProvider>();
            dataSources.DiscoverFromAssemblies(typeof(StooqHistoricalDataProvider).Assembly);
            throw new InvalidOperationException("Registration failed after publishing provisional services.");
        });

        var report = await new ProviderModuleLoader().LoadModulesAsync(services, registry, [module]);

        report.FailedCount.Should().Be(1);
        services.Should().Equal(originalDescriptors);
        registry.ModuleCapabilityRegistrations.Should().BeEmpty();
        registry.Sources.Should().BeEmpty();
    }

    private sealed class ModuleMarker;

    private sealed class RegistrationMutationModule(Action<IServiceCollection, DataSourceRegistry> register) : IProviderModule
    {
        public void Register(IServiceCollection services, DataSourceRegistry registry) => register(services, registry);
    }
}
