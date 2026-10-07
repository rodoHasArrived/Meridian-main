using System.Reflection;
using System.Reflection.Emit;
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
        registry.DiscoverFromAssemblies(typeof(StooqHistoricalDataProvider).Assembly);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_module_preserves_original_services_and_rolls_back_discovered_sources(bool discoverModule)
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();
        services.AddSingleton(new ModuleMarker());
        var originalDescriptors = services.ToArray();
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"RollbackModule{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("provider")
            .DefineType("FailingModule", TypeAttributes.Public | TypeAttributes.Sealed, typeof(FailingRegistrationModule));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        var moduleType = type.CreateType()!;

        if (discoverModule)
        {
            registry.RegisterModules(services, assembly);
            registry.Failures.Should().ContainSingle(failure => failure.Stage == "register");
        }
        else
        {
            var module = (IProviderModule)Activator.CreateInstance(moduleType)!;
            var report = await new ProviderModuleLoader().LoadModulesAsync(services, registry, [module]);
            report.FailedCount.Should().Be(1);
        }
        services.Should().Equal(originalDescriptors);
        registry.ModuleCapabilityRegistrations.Should().BeEmpty();
        registry.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejected_scoped_factory_restores_preexisting_sources_and_capabilities()
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromAssemblies(typeof(StooqHistoricalDataProvider).Assembly);
        var services = new ServiceCollection();
        var loader = new ProviderModuleLoader();
        var valid = new RegistrationMutationModule((collection, _) => collection.AddSingleton<StooqHistoricalDataProvider>());
        (await loader.LoadModulesAsync(services, registry, [valid])).AllLoaded.Should().BeTrue();
        var originalSources = registry.Sources.ToArray();
        var originalRegistrations = registry.ModuleCapabilityRegistrations.ToArray();
        var originalDescriptors = services.ToArray();
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"ProvisionalSource{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var dynamicModule = assembly.DefineDynamicModule("provider");
        var contract = dynamicModule.DefineType(DataSourceCapabilityContracts.HistoricalDataProvider,
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType()!;
        var type = dynamicModule.DefineType("ProvisionalSource", TypeAttributes.Public);
        type.AddInterfaceImplementation(contract);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.SetCustomAttribute(new CustomAttributeBuilder(typeof(DataSourceAttribute).GetConstructor(
            [typeof(string), typeof(string), typeof(DataSourceType), typeof(DataSourceCategory)])!,
            ["provisional-source", "Provisional source", DataSourceType.Reference, DataSourceCategory.Aggregator]));
        type.CreateType();
        var invalid = new RegistrationMutationModule((collection, dataSources) =>
        {
            dataSources.DiscoverFromAssemblies(assembly);
            dataSources.Sources.Should().ContainSingle(source => source.Id == "provisional-source");
            collection.AddScoped<StooqHistoricalDataProvider>();
        });

        var report = await loader.LoadModulesAsync(services, registry, [invalid]);

        report.Failed.Should().ContainSingle().Which.FailureReason.Should().Contain("singleton or transient");
        services.Should().Equal(originalDescriptors);
        registry.Sources.Should().Equal(originalSources);
        registry.ModuleCapabilityRegistrations.Should().Equal(originalRegistrations);
    }

    public abstract class FailingRegistrationModule : IProviderModule
    {
        public string ModuleId => "stooq";

        public void Register(IServiceCollection services, DataSourceRegistry registry)
        {
            services.Clear();
            services.AddSingleton<StooqHistoricalDataProvider>();
            registry.DiscoverFromAssemblies(typeof(StooqHistoricalDataProvider).Assembly);
            throw new InvalidOperationException("Registration failed after publishing provisional services.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_capability_factory_rolls_back_sources_discovered_during_registration(bool interfaceOnly)
    {
        var registry = new DataSourceRegistry();
        var services = new ServiceCollection();
        services.AddSingleton(new ModuleMarker());
        var originalDescriptors = services.ToArray();
        var module = new RegistrationMutationModule((collection, dataSources) =>
        {
            dataSources.DiscoverFromAssemblies(typeof(StooqHistoricalDataProvider).Assembly);
            if (interfaceOnly)
                collection.AddSingleton<IHistoricalDataProvider>(_ =>
                    throw new InvalidOperationException("The invalid factory must never run."));
            else
                collection.AddScoped<StooqHistoricalDataProvider>();
        });

        var report = await new ProviderModuleLoader().LoadModulesAsync(services, registry, [module]);

        report.Failed.Should().ContainSingle().Which.FailureReason.Should().Contain(
            interfaceOnly ? "without its concrete factory" : "scoped concrete factory");
        services.Should().Equal(originalDescriptors);
        registry.ModuleCapabilityRegistrations.Should().BeEmpty();
        registry.Sources.Should().BeEmpty();
    }

    private sealed class ModuleMarker;

    private sealed class RegistrationMutationModule(Action<IServiceCollection, DataSourceRegistry> register) : IProviderModule
    {
        public string ModuleId => "stooq";

        public void Register(IServiceCollection services, DataSourceRegistry registry) => register(services, registry);
    }
}
