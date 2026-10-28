using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.Ui;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Tests.Application.Composition;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class ProviderModuleCompositionTests : IDisposable
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Discovered_module_owns_its_configured_factory_before_container_build(bool enabled)
    {
        var assembly = CreatePluginAssembly();
        var config = new AppConfig(ProviderModules: new ProviderModulesConfig(new()
        {
            ["fred"] = new(Enabled: false),
            [" COMPOSITION-PLUGIN "] = new(Enabled: enabled, Settings: new() { ["marker"] = "configured-by-module" })
        }));
        var services = new ServiceCollection();
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);
        var count = services.Count;
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var capability = registry.GetCapability<ICorporateActionProvider>(" COMPOSITION-PLUGIN ");
        var inventory = ProviderCatalog.Get(" COMPOSITION-PLUGIN ");
        inventory.Should().NotBeNull();
        inventory!.Capabilities.SupportsCorporateActions.Should().BeTrue();
        inventory.Capabilities.SupportsStreaming.Should().BeFalse();
        inventory.Capabilities.MarketDataCapabilities.Should().BeEmpty("factory inventory is not observed product readiness");

        if (enabled)
        {
            capability.Should().BeAssignableTo<PluginCorporateActions>();
            ((PluginCorporateActions)capability!).Marker.Should().Be("configured-by-module");
            capability.Should().BeSameAs(provider.GetRequiredService(capability.GetType()));
            var discovered = provider.GetRequiredService<DataSourceRegistry>();
            discovered.GetRegistrationReport().RegisteredModuleCount.Should().Be(1);
            discovered.ModuleCapabilityRegistrations.Should()
                .ContainSingle(registration => registration.ProviderId == "composition-plugin"
                    && registration.Contract == typeof(ICorporateActionProvider)
                    && registration.ImplementationType == capability.GetType());
        }
        else
        {
            capability.Should().BeNull();
            var discovered = provider.GetRequiredService<DataSourceRegistry>();
            discovered.GetRegistrationReport().RegisteredModuleCount.Should().Be(0);
            discovered.ModuleCapabilityRegistrations.Should().BeEmpty();
        }
        services.Count.Should().Be(count);
    }

    [Fact]
    public async Task Failing_module_cannot_publish_services_or_resolvable_capabilities()
    {
        var assembly = CreatePluginAssembly(typeof(ThrowingPluginModule));
        var adapter = GetAdapterType(assembly);
        var config = CreateEnabledPluginConfig();
        var services = new ServiceCollection();
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);
        var count = services.Count;
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var discovered = provider.GetRequiredService<DataSourceRegistry>();

        registry.GetCapability<ICorporateActionProvider>(" COMPOSITION-PLUGIN ").Should().BeNull();
        provider.GetService(adapter).Should().BeNull("a module that throws after adding a factory must roll it back");
        discovered.ModuleCapabilityRegistrations.Should().BeEmpty();
        discovered.GetRegistrationReport().RegisteredModuleCount.Should().Be(0);
        discovered.GetRegistrationReport().Failures.Should().ContainSingle()
            .Which.Stage.Should().Be("register");
        services.Count.Should().Be(count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attributed_type_and_preexisting_service_are_not_module_owned_capabilities(bool unrelatedModule)
    {
        var assembly = CreatePluginAssembly(typeof(UnrelatedPluginModule), includeModule: unrelatedModule);
        var adapter = GetAdapterType(assembly);
        var config = CreateEnabledPluginConfig();
        var services = new ServiceCollection();
        // An existing service can legitimately share an attributed type. Only a successful
        // module's own factory registrations may turn that metadata into provider availability.
        var preexisting = Activator.CreateInstance(adapter, "preexisting-service")!;
        services.AddSingleton(adapter, preexisting);
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);
        var count = services.Count;
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var discovered = provider.GetRequiredService<DataSourceRegistry>();

        provider.GetRequiredService(adapter).Should().BeSameAs(preexisting);
        discovered.Sources.Should().Contain(source => source.ImplementationType == adapter);
        discovered.GetRegistrationReport().RegisteredModuleCount.Should().Be(unrelatedModule ? 1 : 0);
        discovered.ModuleCapabilityRegistrations.Should().BeEmpty();
        registry.GetCapability<ICorporateActionProvider>(" COMPOSITION-PLUGIN ").Should().BeNull();
        services.Count.Should().Be(count);
    }

    [Theory]
    [InlineData("interface-only")]
    [InlineData("throw-after-register")]
    [InlineData("validation-failed")]
    [InlineData("scoped")]
    public async Task Invalid_module_factory_registration_is_reported_without_partial_providers(string registrationMode)
    {
        var assembly = CreatePluginAssembly();
        var config = new AppConfig(ProviderModules: new ProviderModulesConfig(new()
        {
            ["fred"] = new(Enabled: false),
            [" COMPOSITION-PLUGIN "] = new(Settings: new() { ["registrationMode"] = registrationMode })
        }));
        var services = new ServiceCollection();
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);

        await using var provider = services.BuildServiceProvider();
        var discovery = provider.GetRequiredService<DataSourceRegistry>();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<ICorporateActionProvider>("composition-plugin").Should().BeNull();
        discovery.ModuleCapabilityRegistrations.Should().BeEmpty();
        discovery.GetRegistrationReport().Failures.Should().ContainSingle(failure =>
            failure.ModuleId == "composition-plugin"
            && failure.Stage == (registrationMode == "validation-failed" ? "validate" : "register"));
        if (registrationMode == "interface-only")
            discovery.Failures.Single().ErrorMessage.Should().Contain("concrete factory");
        if (registrationMode == "scoped")
            discovery.Failures.Single().ErrorMessage.Should().Contain("singleton or transient");
        provider.GetService<ModuleRegistrationMarker>().Should().BeNull("failed modules publish no partial DI registrations");
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == discovery.Sources.Single(source => source.Id == "composition-plugin").ImplementationType);
    }

    [Fact]
    public async Task NoOp_module_cannot_claim_an_unrelated_preexisting_concrete_service()
    {
        var assembly = CreatePluginAssembly();
        var config = new AppConfig(ProviderModules: new ProviderModulesConfig(new()
        {
            ["fred"] = new(Enabled: false),
            ["composition-plugin"] = new(Settings: new() { ["registrationMode"] = "no-op" })
        }));
        var adapter = assembly.GetTypes().Single(type => typeof(PluginCorporateActions).IsAssignableFrom(type));
        var services = new ServiceCollection();
        services.AddSingleton(adapter, _ => Activator.CreateInstance(adapter, "unrelated registration")!);
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ProviderRegistry>().GetCapability<ICorporateActionProvider>("composition-plugin")
            .Should().BeNull();
        provider.GetRequiredService<DataSourceRegistry>().ModuleCapabilityRegistrations.Should().BeEmpty();
        ((PluginCorporateActions)provider.GetRequiredService(adapter)).Marker.Should().Be("unrelated registration");
    }

    [Fact]
    public async Task Per_contract_gate_cannot_be_bypassed_by_a_shared_multi_interface_implementation()
    {
        var assembly = CreatePluginAssembly(typeof(GatedPluginModule), adapterBase: typeof(CombinedProvider));
        var config = CreateEnabledPluginConfig();
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
            config.ProviderModules!.Modules![descriptor.ProviderId] = new(Enabled: false);
        var services = new ServiceCollection();
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var combined = (CombinedProvider)registry.GetCapability<ISymbolSearchProvider>("composition-plugin")!;

        combined.Should().NotBeNull();
        registry.GetAllProviderMetadata().Should().Contain(combined);
        registry.GetCapability<IHistoricalDataProvider>("composition-plugin").Should().BeNull();
        registry.GetProviders<IHistoricalDataProvider>().Should().BeEmpty();
        registry.GetProvider<IHistoricalDataProvider>("composition-plugin").Should().BeNull();
        registry.GetBackfillProvider("composition-plugin").Should().BeNull();
        registry.GetBackfillProviders().Should().BeEmpty();
        (await registry.GetBestBackfillProviderAsync()).Should().BeNull();
        (await registry.IsAnyProviderAvailableAsync<IHistoricalDataProvider>()).Should().BeFalse();
        (await registry.GetProviderAvailabilityAsync<IHistoricalDataProvider>()).Should().BeEmpty();
        provider.GetServices<IHistoricalDataProvider>().Should().BeEmpty();
        registry.GetProvider<ISymbolSearchProvider>("composition-plugin").Should().BeSameAs(combined);
        registry.GetSymbolSearchProviders().Should().ContainSingle().Which.Should().BeSameAs(combined);
        (await registry.GetBestSymbolSearchProviderAsync()).Should().BeSameAs(combined);
        (await registry.GetProviderAvailabilityAsync<ISymbolSearchProvider>())["composition-plugin"].Should().BeTrue();
        combined.HistoricalAvailabilityChecks.Should().Be(0);
        combined.SearchAvailabilityChecks.Should().Be(2);
    }

    [Fact]
    public async Task Failed_options_constructor_is_not_retried_by_enumeration_or_catalog_reads()
    {
        FailingOptionsProvider.ConstructionAttempts = 0;
        var assembly = CreatePluginAssembly(adapterBase: typeof(FailingOptionsProvider));
        var config = CreateEnabledPluginConfig();
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
            config.ProviderModules!.Modules![descriptor.ProviderId] = new(Enabled: false);
        var services = new ServiceCollection();
        services.AddProviderServices(config, _ => config, pluginAssemblies: [assembly]);
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IOptionsChainProvider>("composition-plugin").Should().BeNull();
        provider.GetServices<IOptionsChainProvider>().Should().BeEmpty();
        var catalog = provider.GetRequiredService<IProviderCatalog>();
        catalog.GetAll().Should().Contain(entry => entry.ProviderId == "composition-plugin"
            && entry.Capabilities.SupportsOptionsChain);
        catalog.Get("composition-plugin").Should().NotBeNull();
        FailingOptionsProvider.ConstructionAttempts.Should().Be(1);
    }

    private static AppConfig CreateEnabledPluginConfig() => new(ProviderModules: new ProviderModulesConfig(new()
    {
        ["fred"] = new(Enabled: false),
        [" COMPOSITION-PLUGIN "] = new(Enabled: true, Settings: new() { ["marker"] = "configured-by-module" })
    }));

    private static Type GetAdapterType(Assembly assembly) => assembly.GetTypes()
        .Single(type => type.IsSubclassOf(typeof(PluginCorporateActions)));

    // Only this explicitly supplied assembly is discovered. Positive resolution must come
    // from its configured module; a preexisting service is used only in the negative cases.
    private static Assembly CreatePluginAssembly(Type? moduleBase = null, bool includeModule = true, Type? adapterBase = null)
    {
        adapterBase ??= typeof(PluginCorporateActions);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"CompositionPlugin{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("provider");
        var adapter = module.DefineType("ConfiguredCorporateActions", TypeAttributes.Public | TypeAttributes.Sealed, adapterBase);
        var constructor = adapter.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, adapterBase.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(string)], null)!);
        il.Emit(OpCodes.Ret);
        adapter.SetCustomAttribute(new CustomAttributeBuilder(typeof(DataSourceAttribute).GetConstructor(
            [typeof(string), typeof(string), typeof(DataSourceType), typeof(DataSourceCategory)])!,
            ["composition-plugin", "Composition plugin", DataSourceType.Reference, DataSourceCategory.Aggregator]));
        adapter.CreateType();
        if (includeModule)
        {
            var registration = module.DefineType("ConfiguredModule", TypeAttributes.Public | TypeAttributes.Sealed, moduleBase ?? typeof(PluginModule));
            registration.DefineDefaultConstructor(MethodAttributes.Public);
            registration.CreateType();
        }
        return assembly;
    }

    public abstract class PluginCorporateActions(string marker) : ICorporateActionProvider
    {
        public string Marker { get; } = marker;
        public string ProviderId => "composition-plugin";
        public Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(string ticker, Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionCommand>>([]);
    }

    public abstract class PluginModule : ConfigurableProviderModuleBase
    {
        public override string ModuleId => "composition-plugin";
        public override string ModuleDisplayName => "Composition plugin";
        public override bool RequiresExternalConfig => true;
        public override ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default)
            => ValueTask.FromResult(GetSetting("registrationMode") == "validation-failed"
                ? ModuleValidationResult.Failure("Configured plugin prerequisite is missing.")
                : ModuleValidationResult.Valid);
        public override void Register(IServiceCollection services, DataSourceRegistry registry)
        {
            if (GetSetting("registrationMode") == "no-op")
                return;
            var adapter = registry.Sources.Single(source => source.Id == ModuleId).ImplementationType;
            services.AddSingleton<ModuleRegistrationMarker>();
            if (GetSetting("registrationMode") == "interface-only")
            {
                services.AddSingleton<ICorporateActionProvider>(_ =>
                    (ICorporateActionProvider)Activator.CreateInstance(adapter, GetSetting("marker"))!);
                return;
            }
            if (GetSetting("registrationMode") == "scoped")
            {
                services.AddScoped(adapter, _ => Activator.CreateInstance(adapter, GetSetting("marker"))!);
                return;
            }
            services.AddSingleton(adapter, _ => Activator.CreateInstance(adapter, GetSetting("marker"))!);
            if (GetSetting("registrationMode") == "throw-after-register")
                throw new InvalidOperationException("Module registration failed after adding descriptors.");
        }
    }

    public abstract class ThrowingPluginModule : PluginModule
    {
        public override void Register(IServiceCollection services, DataSourceRegistry registry)
        {
            base.Register(services, registry);
            throw new InvalidOperationException("Module failed after registering its factory.");
        }
    }

    public abstract class GatedPluginModule : PluginModule, IProviderModuleCapabilityAvailability
    {
        public bool IsCapabilityConfigured(Type contract, IServiceProvider services)
            => contract == typeof(ISymbolSearchProvider);
    }

    public abstract class CombinedProvider(string marker) : IHistoricalDataProvider, ISymbolSearchProvider
    {
        public string Marker { get; } = marker;
        public string Name => "composition-plugin";
        public string DisplayName => "Combined provider";
        public string Description => "Combined provider";
        public int Priority => 1;
        public int HistoricalAvailabilityChecks { get; private set; }
        public int SearchAvailabilityChecks { get; private set; }
        string IProviderMetadata.ProviderId => Name;
        string IProviderMetadata.ProviderDisplayName => DisplayName;
        string IProviderMetadata.ProviderDescription => Description;
        int IProviderMetadata.ProviderPriority => Priority;
        ProviderCapabilities IProviderMetadata.ProviderCapabilities => new() { SupportsBackfill = true, SupportsSymbolSearch = true };
        Task<bool> IHistoricalDataProvider.IsAvailableAsync(CancellationToken ct)
        {
            HistoricalAvailabilityChecks++;
            throw new InvalidOperationException("The denied historical capability must not be probed.");
        }
        Task<bool> ISymbolSearchProvider.IsAvailableAsync(CancellationToken ct)
        {
            SearchAvailabilityChecks++;
            return Task.FromResult(true);
        }
        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>([]);
        public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);
        public Task<SymbolDetails?> GetDetailsAsync(SymbolId symbol, CancellationToken ct)
            => Task.FromResult<SymbolDetails?>(null);
    }

    public abstract class FailingOptionsProvider : IOptionsChainProvider
    {
        public static int ConstructionAttempts;
        protected FailingOptionsProvider(string marker)
        {
            ConstructionAttempts++;
            throw new InvalidOperationException("Options provider failed to initialize.");
        }
        public OptionsChainCapabilities Capabilities => OptionsChainCapabilities.Basic;
        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string underlyingSymbol, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DateOnly>>([]);
        public Task<IReadOnlyList<decimal>> GetStrikesAsync(string underlyingSymbol, DateOnly expiration, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<decimal>>([]);
        public Task<OptionChainSnapshot?> GetChainSnapshotAsync(string underlyingSymbol, DateOnly expiration, int? strikeRange, CancellationToken ct)
            => Task.FromResult<OptionChainSnapshot?>(null);
        public Task<OptionQuote?> GetOptionQuoteAsync(OptionContractSpec contract, CancellationToken ct)
            => Task.FromResult<OptionQuote?>(null);
    }

    public abstract class UnrelatedPluginModule : PluginModule
    {
        public override void Register(IServiceCollection services, DataSourceRegistry registry)
            => services.AddSingleton(new UnrelatedModuleService());
    }

    public sealed class UnrelatedModuleService { }

    public sealed class ModuleRegistrationMarker { }

    public void Dispose()
    {
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;
    }
}
