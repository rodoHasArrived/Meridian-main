using Meridian.Contracts.Api;
using System.Reflection;
using Meridian.Core.Config;
using Meridian.Core.Monitoring.Core;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

public static partial class ProviderServiceExtensions
{
    /// <summary>
    /// Registers built-in capability factories before the container is built. Configuration may
    /// be prepared lazily by the host, but service registration and module discovery are eager.
    /// The descriptor catalog owns built-in factories; explicitly supplied plugin assemblies own
    /// their additional modules. Attribute discovery never replaces a configured built-in factory.
    /// </summary>
    public static IServiceCollection AddProviderServices(
        this IServiceCollection services,
        AppConfig registrationConfig,
        Func<IServiceProvider, AppConfig> configFactory,
        ILogger? log = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? sidecars = null,
        params Assembly[] pluginAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrationConfig);
        ArgumentNullException.ThrowIfNull(configFactory);

        services.AddLogging();
        services.AddHttpClient();
        services.TryAddSingleton<IConfiguration>(static _ => new ConfigurationBuilder().AddEnvironmentVariables().Build());
        services.TryAddSingleton<IProviderCredentialResolver, EnvironmentCredentialResolver>();

        var contexts = BuildModuleContexts(registrationConfig.ProviderModules?.Modules ?? [], sidecars);
        var registry = new DataSourceRegistry();
        registry.ConfigureModules(contexts);
        var assemblies = new[] { typeof(ProviderFactory).Assembly }.Concat(pluginAssemblies).Distinct().ToArray();
        registry.DiscoverFromAssemblies(assemblies);

        services.AddSingleton(sp => new ProviderFactory(
            configFactory(sp), sp.GetRequiredService<IProviderCredentialResolver>(), log,
            sp.GetService<SymbolResolution.ISymbolResolver>(), sp, contexts));

        var registrationFactory = new ProviderFactory(registrationConfig, new EnvironmentCredentialResolver(), log,
            moduleContexts: contexts);
        if (registrationFactory.IsFamilyEnabled("nyse"))
            services.AddNYSEDataSource();
        var registrations = ProviderCapabilityDescriptorCatalog.Descriptors
            .SelectMany(static descriptor => descriptor.Registrations())
            .Where(registration => registrationFactory.IsFamilyEnabled(registration.ProviderId))
            .ToArray();

        foreach (var registration in registrations)
        {
            if (registration.Contract == typeof(IHistoricalDataProvider) || registration.Contract == typeof(ISymbolSearchProvider))
            {
                // Registry initialization owns these credential-gated instances. Resolving a
                // concrete adapter must reuse that instance instead of bypassing its enable gate.
                services.TryAddSingleton(registration.Implementation, sp =>
                {
                    var providers = sp.GetRequiredService<ProviderRegistry>();
                    var instance = providers.GetCapability(registration.ProviderId, registration.Contract)
                        ?? throw new InvalidOperationException($"Provider '{registration.ProviderId}' has no configured {registration.Contract.Name}.");
                    providers.TransferLifetimeToContainer(instance);
                    return instance;
                });
                continue;
            }

            object Create(IServiceProvider sp)
            {
                var factory = sp.GetRequiredService<ProviderFactory>();
                return (factory.IsCapabilityEnabled(registration) ? registration.Factory(factory) : null)
                    ?? throw new InvalidOperationException($"Provider '{registration.ProviderId}' has no configured {registration.Contract.Name}.");
            }

            if (registration.Contract == typeof(IMarketDataClient))
            {
                // A shared streaming/search type has one concrete DI service: the registry's
                // search singleton. Streaming remains a fresh, explicitly selected factory.
                if (!registrations.Any(other => other.Implementation == registration.Implementation &&
                        (other.Contract == typeof(IHistoricalDataProvider) || other.Contract == typeof(ISymbolSearchProvider))))
                    services.TryAddTransient(registration.Implementation, Create);
                continue;
            }

            services.TryAddSingleton(registration.Implementation, Create);
            if (registration.Contract != typeof(IOptionsChainProvider))
                services.AddSingleton(registration.Contract, sp => sp.GetRequiredService(registration.Implementation));
        }

        var ownedFamilies = ProviderCapabilityDescriptorCatalog.Descriptors
            .Select(static descriptor => descriptor.ProviderId).ToHashSet(StringComparer.Ordinal);
        registry.RegisterModules(services, ownedFamilies, assemblies);
        // AssemblyBuilder and Type.Assembly can expose different wrappers for the same
        // module. Use module identities so explicitly supplied emitted plugins work too.
        var pluginModules = pluginAssemblies.SelectMany(assembly => assembly.GetModules())
            .Select(module => module.ModuleVersionId).ToHashSet();
        var pluginInventory = registry.Sources
            .Where(source => !ownedFamilies.Contains(source.Id) && pluginModules.Contains(source.ImplementationType.Module.ModuleVersionId))
            .ToArray();
        // Discovery publishes factory descriptors only after the owning module successfully
        // registers. Attribute metadata or an unrelated DI service cannot grant a factory.
        var pluginRegistrations = registry.ModuleCapabilityRegistrations
            .Where(registration => registrationFactory.IsFamilyEnabled(registration.ProviderId)
                && pluginInventory.Any(source => source.Id == registration.ProviderId &&
                    source.ImplementationType == registration.ImplementationType))
            .ToArray();
        services.AddSingleton(registry);

        // Availability depends on the host's final credential resolver, which can include a
        // stored credential source. Evaluate it after BuildServiceProvider, never while adding
        // descriptors. An empty candidate set remains a valid catalog configuration.
        services.AddSingleton<IEnumerable<IOptionsChainProvider>>(sp =>
        {
            var factory = sp.GetRequiredService<ProviderFactory>();
            return registrations.Where(r => r.Contract == typeof(IOptionsChainProvider) && factory.IsCapabilityEnabled(r))
                .Select(r => (IOptionsChainProvider)sp.GetRequiredService(r.Implementation))
                .Concat(pluginRegistrations.Where(r => r.Contract == typeof(IOptionsChainProvider))
                    .Select(r => (IOptionsChainProvider)sp.GetRequiredService(r.ImplementationType)))
                .ToArray();
        });
        services.AddSingleton<IOptionsChainProvider>(sp =>
        {
            var candidates = sp.GetServices<IOptionsChainProvider>().ToArray();
            return candidates.OfType<RobinhoodOptionsChainProvider>().FirstOrDefault()
                ?? (IOptionsChainProvider?)candidates.OfType<AlpacaOptionsChainProvider>().FirstOrDefault(p => p.IsCredentialsConfigured)
                ?? candidates.OfType<PolygonOptionsChainProvider>().FirstOrDefault(p => p.IsCredentialsConfigured)
                ?? (IOptionsChainProvider?)candidates.OfType<SyntheticOptionsChainProvider>().FirstOrDefault()
                ?? candidates.FirstOrDefault()
                ?? throw new InvalidOperationException("No options-chain provider is enabled.");
        });

        services.AddSingleton(sp =>
        {
            var providers = new ProviderRegistry(sp.GetService<IAlertDispatcher>(), log);
            var factory = sp.GetRequiredService<ProviderFactory>();
            foreach (var registration in registrations.Where(factory.IsCapabilityEnabled))
            {
                if (registration.Contract == typeof(IMarketDataClient))
                {
                    IMarketDataClient Create() => (IMarketDataClient)registration.Factory(factory)!;
                    providers.RegisterStreamingFactory(registration.ProviderId, Create);
                }
                else if (registration.Contract != typeof(IHistoricalDataProvider) && registration.Contract != typeof(ISymbolSearchProvider))
                {
                    providers.RegisterCapabilityFactory(registration.ProviderId, registration.Contract,
                        () => sp.GetRequiredService(registration.Implementation));
                }
            }

            factory.CreateAndRegisterAllAsync(providers).GetAwaiter().GetResult();
            foreach (var registration in pluginRegistrations)
            {
                providers.RegisterCapabilityFactory(registration.ProviderId, registration.Contract,
                    () => sp.GetRequiredService(registration.ImplementationType));
            }
            foreach (var implementation in pluginRegistrations.Select(r => r.ImplementationType).Distinct())
            {
                if (sp.GetRequiredService(implementation) is IProviderMetadata metadata)
                    providers.Register(metadata, ownsLifetime: false);
            }
            ProviderCatalog.InitializeFromRegistry(
                () => BuildMergedProviderCatalog(providers, sp.GetServices<IOptionsChainProvider>(), pluginInventory),
                id => GetMergedProviderCatalogEntry(providers, sp.GetServices<IOptionsChainProvider>(), id, pluginInventory));
            return providers;
        });
        services.AddSingleton<IEnumerable<IHistoricalDataProvider>>(sp =>
            sp.GetRequiredService<ProviderRegistry>().GetProviders<IHistoricalDataProvider>());
        services.AddSingleton<IEnumerable<ISymbolSearchProvider>>(sp =>
            sp.GetRequiredService<ProviderRegistry>().GetProviders<ISymbolSearchProvider>());
        return services;
    }
}
