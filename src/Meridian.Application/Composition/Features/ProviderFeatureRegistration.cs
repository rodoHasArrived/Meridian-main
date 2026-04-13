// IHttpClientFactory lives in Microsoft.Extensions.Http (transitively available)
using System.Net.Http;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            ProviderCatalog.InitializeFromRegistry(
                () => BuildMergedProviderCatalog(registry, sp.GetServices<IOptionsChainProvider>()),
                providerId => GetMergedProviderCatalogEntry(
                    registry,
                    sp.GetServices<IOptionsChainProvider>(),
                    providerId));

            return registry;
        });

        // Options chain providers — register the default providers first.
        // CollectorFeatureRegistration resolves a single IOptionsChainProvider, and Microsoft DI
        // returns the last registration for single-service resolution.
        RegisterOptionsChainProviders(services);
        RegisterOptionsChainProviders(services, options);
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

    private static void RegisterOptionsChainProviders(
        IServiceCollection services,
        CompositionOptions options)
    {
        if (!options.EnableHttpClientFactory)
            return;

        var config = new ConfigStore(options.ConfigPath).Load();
        var robinhoodEnabled = config.Backfill?.Providers?.Robinhood?.Enabled == true;
        var accessToken = Environment.GetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN");

        if (!robinhoodEnabled || string.IsNullOrWhiteSpace(accessToken))
            return;

        services.AddSingleton<IOptionsChainProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<RobinhoodOptionsChainProvider>>();
            return new RobinhoodOptionsChainProvider(httpClientFactory, logger, accessToken);
        });
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
            var credentialContext = credentialResolver.CreateContext(
                typeof(Infrastructure.Adapters.Alpaca.AlpacaMarketDataClient),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ALPACA_KEY_ID"] = config.Alpaca?.KeyId,
                    ["ALPACA_SECRET_KEY"] = config.Alpaca?.SecretKey
                });
            var keyId = credentialContext.Get("ALPACA_KEY_ID");
            var secretKey = credentialContext.Get("ALPACA_SECRET_KEY");
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
    private static IReadOnlyList<Meridian.Contracts.Api.ProviderCatalogEntry> BuildMergedProviderCatalog(
        ProviderRegistry registry,
        IEnumerable<IOptionsChainProvider> optionProviders)
    {
        var merged = Meridian.Contracts.Api.ProviderCatalog.GetStaticEntries()
            .ToDictionary(entry => entry.ProviderId, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in registry.GetProviderCatalog())
        {
            if (merged.TryGetValue(entry.ProviderId, out var existing))
            {
                merged[entry.ProviderId] = MergeCatalogEntries(existing, entry);
            }
            else
            {
                merged[entry.ProviderId] = entry;
            }
        }

        foreach (var provider in optionProviders)
        {
            var optionEntry = ProviderTemplateFactory.ToCatalogEntry(provider);
            if (merged.TryGetValue(optionEntry.ProviderId, out var existing))
            {
                merged[optionEntry.ProviderId] = MergeCatalogEntries(existing, optionEntry);
            }
            else
            {
                merged[optionEntry.ProviderId] = optionEntry;
            }
        }

        return merged.Values
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Meridian.Contracts.Api.ProviderCatalogEntry? GetMergedProviderCatalogEntry(
        ProviderRegistry registry,
        IEnumerable<IOptionsChainProvider> optionProviders,
        string providerId)
    {
        return BuildMergedProviderCatalog(registry, optionProviders)
            .FirstOrDefault(entry => string.Equals(entry.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private static Meridian.Contracts.Api.ProviderCatalogEntry MergeCatalogEntries(
        Meridian.Contracts.Api.ProviderCatalogEntry existing,
        Meridian.Contracts.Api.ProviderCatalogEntry overlay)
    {
        var mergedCredentials = existing.CredentialFields
            .Concat(overlay.CredentialFields)
            .GroupBy(
                field => $"{field.Name}|{field.EnvironmentVariable}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var mergedNotes = existing.Notes
            .Concat(overlay.Notes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedWarnings = existing.Warnings
            .Concat(overlay.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedMarkets = existing.SupportedMarkets
            .Concat(overlay.SupportedMarkets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedDataTypes = existing.DataTypes
            .Concat(overlay.DataTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingCaps = existing.Capabilities;
        var overlayCaps = overlay.Capabilities;
        var maxDepthLevels = Math.Max(existingCaps.MaxDepthLevels ?? 0, overlayCaps.MaxDepthLevels ?? 0);
        var mergedCapabilities = new CapabilityInfo
        {
            SupportsStreaming = existingCaps.SupportsStreaming || overlayCaps.SupportsStreaming,
            SupportsMarketDepth = existingCaps.SupportsMarketDepth || overlayCaps.SupportsMarketDepth,
            MaxDepthLevels = maxDepthLevels > 0 ? maxDepthLevels : null,
            SupportsAdjustedPrices = existingCaps.SupportsAdjustedPrices || overlayCaps.SupportsAdjustedPrices,
            SupportsDividends = existingCaps.SupportsDividends || overlayCaps.SupportsDividends,
            SupportsSplits = existingCaps.SupportsSplits || overlayCaps.SupportsSplits,
            SupportsIntraday = existingCaps.SupportsIntraday || overlayCaps.SupportsIntraday,
            SupportsTrades = existingCaps.SupportsTrades || overlayCaps.SupportsTrades,
            SupportsQuotes = existingCaps.SupportsQuotes || overlayCaps.SupportsQuotes,
            SupportsOptionsChain = existingCaps.SupportsOptionsChain || overlayCaps.SupportsOptionsChain,
            SupportsBrokerage = existingCaps.SupportsBrokerage || overlayCaps.SupportsBrokerage,
            SupportsAuctions = existingCaps.SupportsAuctions || overlayCaps.SupportsAuctions
        };

        return new Meridian.Contracts.Api.ProviderCatalogEntry
        {
            ProviderId = existing.ProviderId,
            DisplayName = string.IsNullOrWhiteSpace(existing.DisplayName) ? overlay.DisplayName : existing.DisplayName,
            Description = string.Equals(existing.Description, overlay.Description, StringComparison.OrdinalIgnoreCase)
                ? existing.Description
                : $"{existing.Description} {overlay.Description}".Trim(),
            ProviderType = existing.ProviderType,
            RequiresCredentials = existing.RequiresCredentials || overlay.RequiresCredentials,
            CredentialFields = mergedCredentials,
            RateLimit = existing.RateLimit ?? overlay.RateLimit,
            Notes = mergedNotes,
            Warnings = mergedWarnings,
            SupportedMarkets = mergedMarkets,
            DataTypes = mergedDataTypes,
            Capabilities = mergedCapabilities
        };
    }

    /// <summary>
    /// Registers <see cref="IOptionsChainProvider"/> implementations in priority order.
    /// <list type="number">
    ///   <item>If Alpaca credentials are present, Alpaca is selected as the single active provider.</item>
    ///   <item>Else if Polygon credentials are present, Polygon is selected.</item>
    ///   <item>Otherwise, the <see cref="SyntheticOptionsChainProvider"/> is used as the fallback.</item>
    /// </list>
    /// <see cref="CollectorFeatureRegistration"/> resolves
    /// <c>IOptionsChainProvider</c> via <c>GetService&lt;IOptionsChainProvider&gt;()</c>;
    /// Microsoft DI returns the last registration for single-service resolution.
    /// All providers are also exposed via <c>IEnumerable&lt;IOptionsChainProvider&gt;</c>
    /// for enumeration and health checks.
    /// </summary>
    private static void RegisterOptionsChainProviders(IServiceCollection services)
    {
        // 1. Alpaca options — requires ALPACA_KEY_ID + ALPACA_SECRET_KEY
        services.AddSingleton<AlpacaOptionsChainProvider>();

        // 2. Polygon options — requires POLYGON_API_KEY
        services.AddSingleton<PolygonOptionsChainProvider>();

        // 3. Synthetic — always available, deterministic offline fallback
        services.AddSingleton<SyntheticOptionsChainProvider>();

        // Register via the interface:
        //   • GetService<IOptionsChainProvider>() → Synthetic by default (last registration wins)
        //   • GetServices<IOptionsChainProvider>() → all three (for enumeration)
        services.AddSingleton<IOptionsChainProvider>(sp => sp.GetRequiredService<AlpacaOptionsChainProvider>());
        services.AddSingleton<IOptionsChainProvider>(sp => sp.GetRequiredService<PolygonOptionsChainProvider>());
        services.AddSingleton<IOptionsChainProvider>(sp => sp.GetRequiredService<SyntheticOptionsChainProvider>());

        // Register the "best available" single provider so GetService<IOptionsChainProvider>() returns
        // the highest-priority configured provider rather than always resolving to a fixed registration.
        // Resolution order: Alpaca (if credentials present) → Polygon (if credentials present) → Synthetic.
        services.AddSingleton<IOptionsChainProvider>(sp =>
        {
            var alpaca = sp.GetRequiredService<AlpacaOptionsChainProvider>();
            if (alpaca.IsCredentialsConfigured)
                return alpaca;

            var polygon = sp.GetRequiredService<PolygonOptionsChainProvider>();
            if (polygon.IsCredentialsConfigured)
                return polygon;

            return sp.GetRequiredService<SyntheticOptionsChainProvider>();
        });
    }
}
