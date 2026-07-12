using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Core.Monitoring;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Contracts;
using Serilog;
using AlphaVantageBackfillConfig = Meridian.Core.Config.AlphaVantageConfig;
using FinnhubBackfillConfig = Meridian.Core.Config.FinnhubConfig;
using FredBackfillConfig = Meridian.Core.Config.FredConfig;
using NasdaqBackfillConfig = Meridian.Core.Config.NasdaqDataLinkConfig;
using PolygonBackfillConfig = Meridian.Core.Config.PolygonConfig;
using StooqBackfillConfig = Meridian.Core.Config.StooqConfig;
using TiingoBackfillConfig = Meridian.Core.Config.TiingoConfig;
// Type aliases for clarity when dealing with backfill provider configs
using YahooBackfillConfig = Meridian.Core.Config.YahooFinanceConfig;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Unified factory for creating and registering all provider types (streaming, backfill, symbol search).
/// This replaces scattered provider creation logic with a single entry point.
/// </summary>
/// <remarks>
/// The factory uses capability-driven registration where all providers implement
/// <see cref="IProviderMetadata"/> and are registered in a unified <see cref="ProviderRegistry"/>.
/// </remarks>
[ImplementsAdr("ADR-001", "Unified provider factory for capability-driven registration")]
public sealed class ProviderFactory
{
    private readonly AppConfig _config;
    private readonly IProviderCredentialResolver _credentialResolver;
    private readonly ILogger _log;

    public ProviderFactory(
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        ILogger? log = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _log = log ?? LoggingSetup.ForContext<ProviderFactory>();
    }

    /// <summary>
    /// Creates backfill and symbol search providers and registers them with the provided registry.
    /// Streaming providers are registered separately via ProviderRegistry factory functions.
    /// </summary>
    /// <param name="registry">The registry to register providers with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary of created providers.</returns>
    public Task<ProviderCreationResult> CreateAndRegisterAllAsync(
        ProviderRegistry registry,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var result = new ProviderCreationResult();

        // Streaming providers are registered via ProviderRegistry factory functions
        // (see ServiceCompositionRoot.RegisterStreamingFactories) - not created here.

        // Create and register backfill providers
        MigrationDiagnostics.IncBackfillFactoryHit();
        var backfillProviders = CreateBackfillProviders();
        foreach (var provider in backfillProviders)
        {
            registry.Register(provider);
            result.BackfillProviders.Add(provider.ProviderId);
        }

        // Create and register symbol search providers
        MigrationDiagnostics.IncSymbolSearchFactoryHit();
        var searchProviders = CreateSymbolSearchProviders();
        foreach (var provider in searchProviders)
        {
            registry.Register(provider);
            result.SymbolSearchProviders.Add(provider.ProviderId);
        }

        _log.Information(
            "Provider factory created {BackfillCount} backfill, {SearchCount} search providers",
            result.BackfillProviders.Count,
            result.SymbolSearchProviders.Count);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Creates all configured backfill providers.
    /// </summary>
    public IReadOnlyList<IHistoricalDataProvider> CreateBackfillProviders()
    {
        var providers = new List<IHistoricalDataProvider>();
        var backfillCfg = _config.Backfill;
        var providersCfg = backfillCfg?.Providers;

        // Synthetic offline dataset
        TryAddBackfillProvider(providers, "synthetic", () => CreateSyntheticBackfillProvider(providersCfg?.Synthetic));

        // Interactive Brokers native historical path (or guidance-only stub in non-IBAPI builds)
        TryAddBackfillProvider(providers, "interactive-brokers", () => CreateIbBackfillProvider(_config.IB));

        // Alpaca Markets (highest priority when configured)
        TryAddBackfillProvider(providers, "alpaca", () => CreateAlpacaBackfillProvider(providersCfg?.Alpaca));

        // Yahoo Finance (broad free coverage)
        TryAddBackfillProvider(providers, "yahoo", () => CreateYahooBackfillProvider(providersCfg?.Yahoo));

        // Polygon.io
        TryAddBackfillProvider(providers, "polygon", () => CreatePolygonBackfillProvider(providersCfg?.Polygon));

        // Tiingo
        TryAddBackfillProvider(providers, "tiingo", () => CreateTiingoBackfillProvider(providersCfg?.Tiingo));

        // Twelve Data
        TryAddBackfillProvider(providers, "twelvedata", CreateTwelveDataBackfillProvider);

        // Finnhub
        TryAddBackfillProvider(providers, "finnhub", () => CreateFinnhubBackfillProvider(providersCfg?.Finnhub));

        // Stooq
        TryAddBackfillProvider(providers, "stooq", () => CreateStooqBackfillProvider(providersCfg?.Stooq));

        // Alpha Vantage
        TryAddBackfillProvider(providers, "alphavantage", () => CreateAlphaVantageBackfillProvider(providersCfg?.AlphaVantage));

        // FRED economic data
        TryAddBackfillProvider(providers, "fred", () => CreateFredBackfillProvider(providersCfg?.Fred));

        // Nasdaq Data Link
        TryAddBackfillProvider(providers, "nasdaq", () => CreateNasdaqBackfillProvider(providersCfg?.Nasdaq));

        // Robinhood historical bars (unofficial API, explicitly enabled)
        TryAddBackfillProvider(providers, "robinhood", () => CreateRobinhoodBackfillProvider(providersCfg?.Robinhood));

        return providers
            .OrderBy(p => p.Priority)
            .ToList();
    }

    private void TryAddBackfillProvider(
        List<IHistoricalDataProvider> providers,
        string providerLabel,
        Func<IHistoricalDataProvider?> factory)
    {
        try
        {
            var provider = factory();
            if (provider != null)
            {
                providers.Add(provider);
            }
        }
        catch (Exception ex)
        {
            // Isolate one provider's construction failure so the rest still register, but name the
            // provider and surface the exception so configuration/credential errors are not silent.
            _log.Warning(ex, "Failed to create backfill provider {Provider}; skipping it for this run", providerLabel);
        }
    }


    private IHistoricalDataProvider? CreateSyntheticBackfillProvider(SyntheticMarketDataConfig? cfg)
    {
        if (!EnabledWhenOptedIn(cfg?.Enabled))
            return null;

        return new SyntheticHistoricalDataProvider(cfg);
    }

    private IHistoricalDataProvider? CreateIbBackfillProvider(IBOptions? cfg)
    {
        if (_config.DataSource != DataSourceKind.IB && cfg is null)
            return null;

        var effectiveOptions = cfg ?? new IBOptions();
        var publisher = new NullMarketEventPublisher();
        var router = new IBCallbackRouter(
            new MarketDepthCollector(publisher, requireExplicitSubscription: false),
            new TradeDataCollector(publisher));
        var connectionManager = new EnhancedIBConnectionManager(
            router,
            host: effectiveOptions.Host,
            port: effectiveOptions.Port,
            clientId: effectiveOptions.ClientId);

        return new IBHistoricalDataProvider(connectionManager, priority: 10, log: _log);
    }

    private IHistoricalDataProvider? CreateAlpacaBackfillProvider(AlpacaBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;

        var configuredOptions = new AlpacaOptions(
            KeyId: FirstNonBlank(cfg?.KeyId, _config.Alpaca?.KeyId) ?? string.Empty,
            SecretKey: FirstNonBlank(cfg?.SecretKey, _config.Alpaca?.SecretKey) ?? string.Empty,
            Feed: FirstNonBlank(cfg?.Feed, _config.Alpaca?.Feed) ?? "iex",
            UseSandbox: _config.Alpaca?.UseSandbox ?? false,
            SubscribeQuotes: _config.Alpaca?.SubscribeQuotes ?? false);
        var credentials = CreateCredentialContext<AlpacaHistoricalDataProvider>(
            ("ALPACA_KEY_ID", configuredOptions.KeyId),
            ("ALPACA_SECRET_KEY", configuredOptions.SecretKey));
        var aliasAwareCredentials = AlpacaCredentialEnvironment.Resolve(configuredOptions);
        var keyId = FirstNonBlank(credentials.Get("ALPACA_KEY_ID"), aliasAwareCredentials.KeyId);
        var secretKey = FirstNonBlank(credentials.Get("ALPACA_SECRET_KEY"), aliasAwareCredentials.SecretKey);
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
            return null;

        return new AlpacaHistoricalDataProvider(
            keyId: keyId,
            secretKey: secretKey,
            feed: cfg?.Feed ?? configuredOptions.Feed,
            adjustment: cfg?.Adjustment ?? "all",
            priority: cfg?.Priority ?? 5,
            rateLimitPerMinute: cfg?.RateLimitPerMinute ?? 200,
            log: _log);
    }

    private IHistoricalDataProvider? CreateYahooBackfillProvider(YahooBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;
        return new YahooFinanceHistoricalDataProvider(log: _log);
    }

    private IHistoricalDataProvider? CreatePolygonBackfillProvider(PolygonBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<PolygonHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "POLYGON_API_KEY",
            cfg?.ApiKey,
            apiKey => new PolygonHistoricalDataProvider(apiKey: apiKey, log: _log));

    private IHistoricalDataProvider? CreateTiingoBackfillProvider(TiingoBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<TiingoHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "TIINGO_API_TOKEN",
            cfg?.ApiToken,
            token => new TiingoHistoricalDataProvider(apiToken: token, log: _log));

    private IHistoricalDataProvider? CreateTwelveDataBackfillProvider()
        => CreateCredentialGatedBackfillProvider<TwelveDataHistoricalDataProvider>(
            enabled: true,
            "TWELVEDATA_API_KEY",
            configuredValue: null,
            apiKey => new TwelveDataHistoricalDataProvider(apiKey: apiKey, log: _log));

    private IHistoricalDataProvider? CreateFinnhubBackfillProvider(FinnhubBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<FinnhubHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "FINNHUB_API_KEY",
            cfg?.ApiKey,
            apiKey => new FinnhubHistoricalDataProvider(apiKey: apiKey, log: _log));

    private IHistoricalDataProvider? CreateStooqBackfillProvider(StooqBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;
        return new StooqHistoricalDataProvider(log: _log);
    }

    private IHistoricalDataProvider? CreateAlphaVantageBackfillProvider(AlphaVantageBackfillConfig? cfg)
        // Opt-in only: the free tier is severely rate-limited, so absent config leaves it disabled.
        => CreateCredentialGatedBackfillProvider<AlphaVantageHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "ALPHA_VANTAGE_API_KEY",
            cfg?.ApiKey,
            apiKey => new AlphaVantageHistoricalDataProvider(apiKey: apiKey, log: _log));

    private IHistoricalDataProvider? CreateFredBackfillProvider(FredBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<FredHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "FRED_API_KEY",
            cfg?.ApiKey,
            apiKey => new FredHistoricalDataProvider(apiKey: apiKey, log: _log));

    private IHistoricalDataProvider? CreateNasdaqBackfillProvider(NasdaqBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;

        // Nasdaq Data Link serves a limited free universe without a key, so an absent
        // credential is not disqualifying — the key only raises rate limits.
        var credentials = CreateCredentialContext<NasdaqDataLinkHistoricalDataProvider>(
            ("NASDAQ_DATA_LINK_API_KEY", cfg?.ApiKey));
        var apiKey = credentials.Get("NASDAQ_DATA_LINK_API_KEY");
        return new NasdaqDataLinkHistoricalDataProvider(
            apiKey: apiKey,
            database: cfg?.Database ?? "WIKI",
            log: _log);
    }

    private IHistoricalDataProvider? CreateRobinhoodBackfillProvider(RobinhoodConfig? cfg)
        // Opt-in only: unofficial API that requires an explicitly supplied access token.
        => CreateCredentialGatedBackfillProvider<RobinhoodHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "ROBINHOOD_ACCESS_TOKEN",
            configuredValue: null,
            accessToken => new RobinhoodHistoricalDataProvider(
                accessToken: accessToken,
                priority: cfg!.Priority,
                log: _log));

    /// <summary>
    /// Creates all configured symbol search providers.
    /// Symbol search uses the same credentials as backfill providers.
    /// </summary>
    public IReadOnlyList<ISymbolSearchProvider> CreateSymbolSearchProviders()
    {
        var providers = new List<ISymbolSearchProvider>();
        var backfillProviders = _config.Backfill?.Providers;

        // Synthetic reference universe search
        TryAddSearchProvider(providers, "synthetic", () => CreateSyntheticSearchProvider(backfillProviders?.Synthetic));

        // Alpaca Symbol Search (uses same credentials as Alpaca backfill)
        TryAddSearchProvider(providers, "alpaca", () => CreateAlpacaSearchProvider(backfillProviders?.Alpaca));

        // Finnhub Symbol Search (uses same credentials as Finnhub backfill)
        TryAddSearchProvider(providers, "finnhub", () => CreateFinnhubSearchProvider(backfillProviders?.Finnhub));

        // Tiingo Symbol Search (uses same credentials as Tiingo backfill)
        TryAddSearchProvider(providers, "tiingo", () => CreateTiingoSearchProvider(backfillProviders?.Tiingo));

        // Alpha Vantage Symbol Search (uses same credentials as Alpha Vantage backfill)
        TryAddSearchProvider(providers, "alphavantage", () => CreateAlphaVantageSearchProvider(backfillProviders?.AlphaVantage));

        // Twelve Data Symbol Search (uses same credentials as Twelve Data backfill)
        TryAddSearchProvider(providers, "twelvedata", CreateTwelveDataSearchProvider);

        // FRED Symbol Search (uses same credentials as FRED backfill)
        TryAddSearchProvider(providers, "fred", () => CreateFredSearchProvider(backfillProviders?.Fred));

        // Nasdaq Data Link Symbol Search (uses same credentials as Nasdaq Data Link backfill)
        TryAddSearchProvider(providers, "nasdaq", () => CreateNasdaqSearchProvider(backfillProviders?.Nasdaq));

        // Polygon Symbol Search (uses same credentials as Polygon backfill)
        TryAddSearchProvider(providers, "polygon", () => CreatePolygonSearchProvider(backfillProviders?.Polygon));

        return providers
            .OrderBy(p => p.Priority)
            .ToList();
    }

    private void TryAddSearchProvider(
        List<ISymbolSearchProvider> providers,
        string providerLabel,
        Func<ISymbolSearchProvider?> factory)
    {
        try
        {
            var provider = factory();
            if (provider != null)
            {
                providers.Add(provider);
            }
        }
        catch (Exception ex)
        {
            // Isolate one provider's construction failure so the rest still register, but name the
            // provider and surface the exception so configuration/credential errors are not silent.
            _log.Warning(ex, "Failed to create symbol search provider {Provider}; skipping it for this run", providerLabel);
        }
    }


    private ISymbolSearchProvider? CreateSyntheticSearchProvider(SyntheticMarketDataConfig? cfg)
    {
        if (!EnabledWhenOptedIn(cfg?.Enabled))
            return null;

        return new SyntheticMarketDataClient(new NullMarketEventPublisher(), cfg);
    }

    private ISymbolSearchProvider? CreateAlpacaSearchProvider(AlpacaBackfillConfig? cfg)
    {
        // Enabled by default unless config explicitly disables it (credential-based activation).
        if (!EnabledByDefault(cfg?.Enabled))
            return null;

        var credentials = CreateCredentialContext<AlpacaHistoricalDataProvider>(
            ("ALPACA_KEY_ID", cfg?.KeyId),
            ("ALPACA_SECRET_KEY", cfg?.SecretKey));
        var keyId = credentials.Get("ALPACA_KEY_ID");
        var secretKey = credentials.Get("ALPACA_SECRET_KEY");
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
            return null;

        return new AlpacaSymbolSearchProviderRefactored(keyId, secretKey, httpClient: null, log: _log);
    }

    private ISymbolSearchProvider? CreateFinnhubSearchProvider(FinnhubBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<FinnhubHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "FINNHUB_API_KEY",
            cfg?.ApiKey,
            apiKey => new FinnhubSymbolSearchProviderRefactored(apiKey, httpClient: null, log: _log));

    private ISymbolSearchProvider? CreateTiingoSearchProvider(TiingoBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<TiingoHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "TIINGO_API_TOKEN",
            cfg?.ApiToken,
            token => new TiingoSymbolSearchProvider(token, httpClient: null, log: _log));

    private ISymbolSearchProvider? CreateAlphaVantageSearchProvider(AlphaVantageBackfillConfig? cfg)
        // Keep Alpha Vantage opt-in to avoid consuming the constrained free-tier quota implicitly.
        => CreateCredentialGatedSearchProvider<AlphaVantageHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "ALPHA_VANTAGE_API_KEY",
            cfg?.ApiKey,
            apiKey => new AlphaVantageSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    private ISymbolSearchProvider? CreateTwelveDataSearchProvider()
        => CreateCredentialGatedSearchProvider<TwelveDataHistoricalDataProvider>(
            enabled: true,
            "TWELVEDATA_API_KEY",
            configuredValue: null,
            apiKey => new TwelveDataSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    private ISymbolSearchProvider? CreateFredSearchProvider(FredBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<FredHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "FRED_API_KEY",
            cfg?.ApiKey,
            apiKey => new FredSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    private ISymbolSearchProvider? CreateNasdaqSearchProvider(NasdaqBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<NasdaqDataLinkHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "NASDAQ_DATA_LINK_API_KEY",
            cfg?.ApiKey,
            apiKey => new NasdaqDataLinkSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    private ISymbolSearchProvider? CreatePolygonSearchProvider(PolygonBackfillConfig? cfg)
        // Enabled by default unless config explicitly disables it (credential-based activation).
        => CreateCredentialGatedSearchProvider<PolygonHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "POLYGON_API_KEY",
            cfg?.ApiKey,
            apiKey => new PolygonSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    /// <summary>
    /// Creates a composite backfill provider with automatic failover.
    /// </summary>
    public CompositeHistoricalDataProvider CreateCompositeBackfillProvider(
        IReadOnlyList<IHistoricalDataProvider> providers)
    {
        var openFigiApiKey = _config.Backfill?.Providers?.OpenFigi?.ApiKey;
        var enableSymbolResolution = _config.Backfill?.EnableSymbolResolution ?? true;

        OpenFigiSymbolResolver? symbolResolver = null;
        if (enableSymbolResolution)
        {
            symbolResolver = new OpenFigiSymbolResolver(openFigiApiKey, log: _log);
        }

        return new CompositeHistoricalDataProvider(
            providers,
            symbolResolver,
            enableCrossValidation: false,
            log: _log);
    }

    /// <summary>
    /// A provider participates unless its configuration explicitly disables it.
    /// Absent configuration (<see langword="null"/>) is treated as enabled.
    /// </summary>
    private static bool EnabledByDefault(bool? configuredEnabled) => configuredEnabled != false;

    /// <summary>
    /// A provider participates only when its configuration explicitly enables it.
    /// Absent configuration (<see langword="null"/>) is treated as disabled (opt-in).
    /// </summary>
    private static bool EnabledWhenOptedIn(bool? configuredEnabled) => configuredEnabled == true;

    /// <summary>
    /// Shared template for the single-credential, credential-gated providers. Resolves one
    /// credential through the attribute-based <typeparamref name="TCredentialSource"/> context
    /// and invokes <paramref name="factory"/> only when the provider is <paramref name="enabled"/>
    /// and the credential resolves to a non-blank value. Returns <see langword="null"/> (provider
    /// omitted) when disabled or the credential is absent.
    /// </summary>
    /// <typeparam name="TCredentialSource">
    /// The provider type whose <c>[RequiresCredential]</c> attributes drive credential resolution.
    /// Symbol-search providers reuse their historical-provider counterpart's credential metadata.
    /// </typeparam>
    /// <typeparam name="TResult">The provider interface returned by <paramref name="factory"/>.</typeparam>
    private TResult? CreateCredentialGatedProvider<TCredentialSource, TResult>(
        bool enabled,
        string credentialName,
        string? configuredValue,
        Func<string, TResult> factory)
        where TResult : class
    {
        if (!enabled)
            return null;

        // Null-conditional guards against a custom/test-double resolver returning a null context.
        var credential = CreateCredentialContext<TCredentialSource>((credentialName, configuredValue))?.Get(credentialName);
        return string.IsNullOrWhiteSpace(credential) ? null : factory(credential);
    }

    private IHistoricalDataProvider? CreateCredentialGatedBackfillProvider<TCredentialSource>(
        bool enabled,
        string credentialName,
        string? configuredValue,
        Func<string, IHistoricalDataProvider> factory)
        => CreateCredentialGatedProvider<TCredentialSource, IHistoricalDataProvider>(
            enabled, credentialName, configuredValue, factory);

    private ISymbolSearchProvider? CreateCredentialGatedSearchProvider<TCredentialSource>(
        bool enabled,
        string credentialName,
        string? configuredValue,
        Func<string, ISymbolSearchProvider> factory)
        => CreateCredentialGatedProvider<TCredentialSource, ISymbolSearchProvider>(
            enabled, credentialName, configuredValue, factory);

    private ICredentialContext CreateCredentialContext<TProvider>(params (string Name, string? Value)[] configuredValues)
    {
        IReadOnlyDictionary<string, string?>? configuredLookup = null;
        if (configuredValues.Length > 0)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (name, value) in configuredValues)
            {
                values[name] = value;
            }

            configuredLookup = values;
        }

        return _credentialResolver.CreateContext(typeof(TProvider), configuredLookup);
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

internal sealed class NullMarketEventPublisher : Meridian.Domain.Events.IMarketEventPublisher
{
    public bool TryPublish(in Meridian.Domain.Events.MarketEvent evt) => true;
}


/// <summary>
/// Result of provider creation operation.
/// </summary>
public sealed class ProviderCreationResult
{
    public List<string> BackfillProviders { get; } = new();
    public List<string> SymbolSearchProviders { get; } = new();

    public int TotalProviders =>
        BackfillProviders.Count + SymbolSearchProviders.Count;

    public bool HasBackfillProviders => BackfillProviders.Count > 0;
    public bool HasSymbolSearchProviders => SymbolSearchProviders.Count > 0;
}

/// <summary>
/// Generic provider credential context factory used by runtime provider registration.
/// </summary>
public interface IProviderCredentialResolver
{
    ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null);
}

/// <summary>
/// Credential resolver that reads from environment variables.
/// Follows the same pattern as ConfigurationService.
/// </summary>
public sealed class EnvironmentCredentialResolver : IProviderCredentialResolver
{
    public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
    {
        return AttributeCredentialResolver.ForType(providerType, credentialName =>
        {
            if (configuredValues is not null &&
                configuredValues.TryGetValue(credentialName, out var configuredValue))
            {
                return configuredValue;
            }

            return null;
        });
    }
}
