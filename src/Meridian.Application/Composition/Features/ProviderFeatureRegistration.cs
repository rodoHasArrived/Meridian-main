// IHttpClientFactory lives in Microsoft.Extensions.Http (transitively available)
using System.Net.Http;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers the unified <see cref="ProviderRegistry"/> and populates it with
/// streaming factory functions, backfill providers, and symbol search providers.
/// All providers are resolved through DI.
/// </summary>
internal sealed class ProviderFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // DataSourceRegistry - discovers providers decorated with [DataSource] (ADR-005).
        services.AddSingleton<DataSourceRegistry>(sp =>
        {
            var registry = new DataSourceRegistry();
            registry.DiscoverFromAssemblies(typeof(NoOpMarketDataClient).Assembly);
            return registry;
        });

        // Register credential resolver
        services.AddSingleton<IProviderCredentialResolver>(sp =>
        {
            var configService = sp.GetRequiredService<ConfigurationService>();
            return new ConfigurationServiceCredentialAdapter(configService);
        });

        // Register the unified ProviderRegistry as singleton
        services.AddSingleton<ProviderRegistry>(sp =>
        {
            var registry = new ProviderRegistry(alertDispatcher: null, LoggingSetup.ForContext<ProviderRegistry>());

            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
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

            RegisterBackfillProviders(registry, config, credentialResolver, log);
            RegisterSymbolSearchProviders(registry, config, credentialResolver, log);

            return registry;
        });

        // Keep ProviderFactory for backward compatibility
        services.AddSingleton<ProviderFactory>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var credentialResolver = sp.GetRequiredService<IProviderCredentialResolver>();
            var logger = LoggingSetup.ForContext<ProviderFactory>();
            return new ProviderFactory(config, credentialResolver, logger);
        });

        return services;
    }

    private static void RegisterStreamingFactories(
        ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        IServiceProvider sp,
        Serilog.ILogger log)
    {
        registry.RegisterStreamingFactory("ib", () =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var depthCollector = sp.GetRequiredService<MarketDepthCollector>();
            return new Infrastructure.Adapters.InteractiveBrokers.IBMarketDataClient(
                publisher, tradeCollector, depthCollector);
        });

        registry.RegisterStreamingFactory("alpaca", () =>
        {
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            var (keyId, secretKey) = credentialResolver.ResolveAlpacaCredentials(
                config.Alpaca?.KeyId, config.Alpaca?.SecretKey);
            return new Infrastructure.Adapters.Alpaca.AlpacaMarketDataClient(
                tradeCollector, quoteCollector,
                config.Alpaca! with { KeyId = keyId ?? "", SecretKey = secretKey ?? "" });
        });

        registry.RegisterStreamingFactory("polygon", () =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            var reconnMetrics = sp.GetRequiredService<IReconnectionMetrics>();
            return new Infrastructure.Adapters.Polygon.PolygonMarketDataClient(
                publisher, tradeCollector, quoteCollector,
                reconnectionMetrics: reconnMetrics);
        });

        registry.RegisterStreamingFactory("stocksharp", () =>
        {
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var depthCollector = sp.GetRequiredService<MarketDepthCollector>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            return new Infrastructure.Adapters.StockSharp.StockSharpMarketDataClient(
                tradeCollector, depthCollector, quoteCollector,
                config.StockSharp ?? new StockSharpConfig());
        });

        registry.RegisterStreamingFactory("nyse", () =>
        {
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var depthCollector = sp.GetRequiredService<MarketDepthCollector>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new Infrastructure.Adapters.NYSE.NyseMarketDataClient(
                tradeCollector,
                depthCollector,
                quoteCollector,
                httpClientFactory);
        });

        registry.RegisterStreamingFactory("synthetic", () =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            return new SyntheticMarketDataClient(publisher, config.Synthetic);
        });

        log.Information("Registered streaming factories for {Count} data sources",
            registry.SupportedStreamingSources.Count);
    }

    private static void RegisterStreamingFactoriesFromAttributes(
        ProviderRegistry registry,
        DataSourceRegistry dsRegistry,
        IServiceProvider sp,
        Serilog.ILogger log)
    {
        foreach (var source in dsRegistry.Sources)
        {
            if (!typeof(IMarketDataClient).IsAssignableFrom(source.ImplementationType))
                continue;

            var implType = source.ImplementationType;
            registry.RegisterStreamingFactory(source.Id, () =>
            {
                var instance = sp.GetService(implType) as IMarketDataClient;
                if (instance != null)
                    return instance;

                return (IMarketDataClient)ActivatorUtilities.CreateInstance(sp, implType);
            });

            log.Information("Auto-registered streaming factory for \"{Id}\" from [DataSource] on {Type}",
                source.Id, implType.Name);
        }

        log.Information("Attribute-based discovery registered {Count} streaming factories",
            registry.SupportedStreamingSources.Count);
    }

    private static void RegisterBackfillProviders(
        ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        Serilog.ILogger log)
    {
        var factory = new ProviderFactory(config, credentialResolver, log);
        var providers = factory.CreateBackfillProviders();
        foreach (var provider in providers)
        {
            registry.Register(provider);
        }
    }

    private static void RegisterSymbolSearchProviders(
        ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        Serilog.ILogger log)
    {
        var factory = new ProviderFactory(config, credentialResolver, log);
        var providers = factory.CreateSymbolSearchProviders();
        foreach (var provider in providers)
        {
            registry.Register(provider);
        }
    }
}
