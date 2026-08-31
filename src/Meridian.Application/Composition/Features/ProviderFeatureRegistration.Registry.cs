using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Core.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Meridian.Core.Monitoring;

namespace Meridian.Application.Composition.Features;

internal sealed partial class ProviderFeatureRegistration
{
    private static void RegisterStreamingFactories(
        ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        IServiceProvider sp,
        Serilog.ILogger log)
    {
        IMarketDataClient CreateInteractiveBrokersClient()
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var depthCollector = sp.GetRequiredService<MarketDepthCollector>();
            var quoteCollector = sp.GetService<QuoteCollector>();
            var optionCollector = sp.GetService<OptionDataCollector>();
            return new Infrastructure.Adapters.InteractiveBrokers.IBMarketDataClient(
                publisher,
                tradeCollector,
                depthCollector,
                quoteCollector,
                optionCollector,
                config.IB ?? new IBOptions());
        }

        // Retain the short legacy route while also registering the canonical [DataSource] id.
        registry.RegisterStreamingFactory("ib", CreateInteractiveBrokersClient);
        registry.RegisterStreamingFactory("ibkr", CreateInteractiveBrokersClient);

        registry.RegisterStreamingFactory("alpaca", () =>
        {
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            var credentialContext = credentialResolver.CreateContext(
                typeof(AlpacaMarketDataClient),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ALPACA_KEY_ID"] = config.Alpaca?.KeyId,
                    ["ALPACA_SECRET_KEY"] = config.Alpaca?.SecretKey
                });
            var keyId = credentialContext.Get("ALPACA_KEY_ID");
            var secretKey = credentialContext.Get("ALPACA_SECRET_KEY");
            return new AlpacaMarketDataClient(
                tradeCollector,
                quoteCollector,
                (config.Alpaca ?? new AlpacaOptions()) with { KeyId = keyId ?? string.Empty, SecretKey = secretKey ?? string.Empty });
        });

        registry.RegisterStreamingFactory("polygon", () =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            var reconnMetrics = sp.GetRequiredService<IReconnectionMetrics>();
            return new PolygonMarketDataClient(
                publisher,
                tradeCollector,
                quoteCollector,
                reconnectionMetrics: reconnMetrics);
        });

        IMarketDataClient CreateNyseClient()
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
        }

        // Retain the short legacy route while also registering the canonical [DataSource] id.
        registry.RegisterStreamingFactory("nyse", CreateNyseClient);
        registry.RegisterStreamingFactory("nyse-streaming", CreateNyseClient);

        registry.RegisterStreamingFactory("robinhood-live", () =>
            new RobinhoodMarketDataClient(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<QuoteCollector>(),
                sp.GetRequiredService<ILogger<RobinhoodMarketDataClient>>()));

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
        Meridian.Infrastructure.Adapters.Core.SymbolResolution.ISymbolResolver? symbolResolver,
        Serilog.ILogger log)
    {
        var factory = new ProviderFactory(config, credentialResolver, log, symbolResolver);
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
