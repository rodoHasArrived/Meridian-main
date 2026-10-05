using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Contracts.Api;
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
            provider.GetRequiredService<DataSourceRegistry>().GetRegistrationReport().RegisteredModuleCount.Should().Be(1);
            provider.GetRequiredService<DataSourceRegistry>().ModuleCapabilityRegistrations.Should()
                .ContainSingle(registration => registration.ProviderId == "composition-plugin"
                    && registration.Contract == typeof(ICorporateActionProvider)
                    && registration.ImplementationType == capability.GetType());
        }
        else
        {
            capability.Should().BeNull();
            provider.GetRequiredService<DataSourceRegistry>().GetRegistrationReport().RegisteredModuleCount.Should().Be(0);
            provider.GetRequiredService<DataSourceRegistry>().ModuleCapabilityRegistrations.Should().BeEmpty();
        }
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

    // Only this explicitly supplied assembly is discovered. The test never registers a
    // provider implementation directly: discovery must invoke its configured module.
    private static Assembly CreatePluginAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"CompositionPlugin{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("provider");
        var adapter = module.DefineType("ConfiguredCorporateActions", TypeAttributes.Public | TypeAttributes.Sealed, typeof(PluginCorporateActions));
        var constructor = adapter.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(PluginCorporateActions).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(string)], null)!);
        il.Emit(OpCodes.Ret);
        adapter.SetCustomAttribute(new CustomAttributeBuilder(typeof(DataSourceAttribute).GetConstructor(
            [typeof(string), typeof(string), typeof(DataSourceType), typeof(DataSourceCategory)])!,
            ["composition-plugin", "Composition plugin", DataSourceType.Reference, DataSourceCategory.Aggregator]));
        adapter.CreateType();
        var registration = module.DefineType("ConfiguredModule", TypeAttributes.Public | TypeAttributes.Sealed, typeof(PluginModule));
        registration.DefineDefaultConstructor(MethodAttributes.Public);
        registration.CreateType();
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

    public sealed class ModuleRegistrationMarker { }

    public void Dispose()
    {
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;
    }
}
