// IHttpClientFactory lives in Microsoft.Extensions.Http (transitively available)
using System.Net.Http;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Core.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers the unified <see cref="ProviderRegistry"/> and populates it with
/// streaming factory functions, backfill providers, and symbol search providers.
/// All providers are resolved through DI.
/// </summary>
internal sealed partial class ProviderFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        services.TryAddSingleton<IConfiguration>(static _ => new ConfigurationBuilder().Build());

        // DataSourceRegistry - discovers providers decorated with [DataSource] (ADR-005).
        services.AddSingleton<DataSourceRegistry>(sp =>
        {
            var registry = new DataSourceRegistry(sp.GetService<ILogger<DataSourceRegistry>>());
            registry.DiscoverFromAssemblies(typeof(NoOpMarketDataClient).Assembly);
            return registry;
        });

        // Register credential resolver
        services.AddSingleton<IProviderCredentialResolver>(sp =>
        {
            var configService = sp.GetRequiredService<ConfigurationService>();
            var fallback = new ConfigurationServiceCredentialAdapter(configService);
            var credentialStore = sp.GetService<IProviderCredentialStore>();
            return credentialStore is null
                ? fallback
                : new StoredProviderCredentialResolver(credentialStore, fallback);
        });

        // Register the unified ProviderRegistry as singleton
        services.AddSingleton<ProviderRegistry>(sp =>
        {
            var registry = new ProviderRegistry(alertDispatcher: null, LoggingSetup.ForContext<ProviderRegistry>());

            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = LoadPreparedConfig(sp, configStore);
            var credentialResolver = sp.GetRequiredService<IProviderCredentialResolver>();
            var log = LoggingSetup.ForContext("ProviderRegistration");

            var useAttributeDiscovery = config.ProviderRegistry?.UseAttributeDiscovery ?? false;
            if (useAttributeDiscovery)
            {
                var dsRegistry = sp.GetRequiredService<DataSourceRegistry>();
                RegisterStreamingFactoriesFromAttributes(registry, dsRegistry, sp, log);
            }
            else
            {
                RegisterStreamingFactories(registry, config, credentialResolver, sp, log);
            }

            RegisterBackfillProviders(
                registry,
                config,
                credentialResolver,
                sp.GetService<ISymbolResolver>(),
                log);
            RegisterSymbolSearchProviders(registry, config, credentialResolver, log);
            ProviderCatalog.InitializeFromRegistry(
                () => BuildMergedProviderCatalog(registry, sp.GetServices<IOptionsChainProvider>()),
                providerId => GetMergedProviderCatalogEntry(
                    registry,
                    sp.GetServices<IOptionsChainProvider>(),
                    providerId));

            return registry;
        });

        // Options chain providers — register the default providers first.
        // CollectorFeatureRegistration resolves all IOptionsChainProvider registrations so
        // OptionsChainService can walk the provider chain before using synthetic fallback data.
        RegisterOptionsChainProviders(services);
        RegisterOptionsChainProviders(services, options);
        RegisterCorporateActionProviders(services);
        // Keep ProviderFactory for backward compatibility
        services.AddSingleton<ProviderFactory>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = LoadPreparedConfig(sp, configStore);
            var credentialResolver = sp.GetRequiredService<IProviderCredentialResolver>();
            var logger = LoggingSetup.ForContext<ProviderFactory>();
            return new ProviderFactory(
                config,
                credentialResolver,
                logger,
                sp.GetService<ISymbolResolver>());
        });

        return services;
    }

    private static AppConfig LoadPreparedConfig(IServiceProvider sp, ConfigStore configStore)
    {
        var configService = sp.GetRequiredService<ConfigurationService>();
        return configService.LoadAndPrepareConfig(configStore.ConfigPath);
    }

    private static void RegisterCorporateActionProviders(IServiceCollection services)
    {
        // Corporate action providers take IConfiguration for credential lookup. Hosted
        // compositions (web/WPF) already carry the host configuration; bare compositions
        // (CLI wiring, composition tests) get an environment-variable-backed fallback,
        // matching the providers' own env-var credential fallbacks.
        services.TryAddSingleton<IConfiguration>(static _ =>
            new ConfigurationBuilder().AddEnvironmentVariables().Build());

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            if (descriptor.CorporateActions is null)
                continue;

            services.TryAddSingleton(descriptor.CorporateActions);
            services.AddSingleton(
                typeof(ICorporateActionProvider),
                sp => sp.GetRequiredService(descriptor.CorporateActions));
        }
    }
}
