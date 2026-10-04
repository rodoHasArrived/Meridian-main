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
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
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
/// Configured construction context for the streaming, historical, search, corporate-action,
/// options, and brokerage factories declared by <see cref="ProviderCapabilityDescriptorCatalog"/>.
/// </summary>
/// <remarks>
/// Historical and search instances expose <see cref="IProviderMetadata"/>. Other contracts,
/// including those without metadata, resolve through typed factories in <see cref="ProviderRegistry"/>.
/// </remarks>
[ImplementsAdr("ADR-001", "Unified provider factory for capability-driven registration")]
public sealed partial class ProviderFactory
{
    private readonly AppConfig _config;
    private readonly IProviderCredentialResolver _credentialResolver;
    private readonly ISymbolResolver? _symbolResolver;
    private readonly ILogger _log;

    public ProviderFactory(
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        ILogger? log = null,
        ISymbolResolver? symbolResolver = null,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, ProviderModuleContext>? moduleContexts = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _symbolResolver = symbolResolver;
        _services = services;
        _moduleContexts = moduleContexts;
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

        // The application composition extension registers the remaining capability factories
        // from ProviderCapabilityDescriptorCatalog without opening provider connections.

        // Create and register backfill providers
        MigrationDiagnostics.IncBackfillFactoryHit();
        var backfillProviders = CreateBackfillProviders();
        foreach (var provider in backfillProviders)
        {
            registry.Register(provider);
            result.BackfillProviders.Add(ProviderIdentity.NormalizeId(provider.ProviderId));
        }

        // Create and register symbol search providers
        MigrationDiagnostics.IncSymbolSearchFactoryHit();
        var searchProviders = CreateSymbolSearchProviders();
        foreach (var provider in searchProviders)
        {
            registry.Register(provider);
            result.SymbolSearchProviders.Add(ProviderIdentity.NormalizeId(provider.ProviderId));
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
        => CreateProviders<IHistoricalDataProvider>(static p => p.Priority, "backfill");

    /// <summary>
    /// Creates all configured symbol search providers.
    /// Symbol search uses the same credentials as backfill providers.
    /// </summary>
    public IReadOnlyList<ISymbolSearchProvider> CreateSymbolSearchProviders()
        => CreateProviders<ISymbolSearchProvider>(static p => p.Priority, "symbol search");

    private IReadOnlyList<T> CreateProviders<T>(
        Func<T, int> priority,
        string providerKind)
        where T : class
    {
        var providers = new List<T>();
        foreach (var registration in ProviderCapabilityDescriptorCatalog.Descriptors.SelectMany(static d => d.Registrations()))
        {
            if (registration.Contract != typeof(T) || !IsCapabilityEnabled(registration))
                continue;

            try
            {
                var provider = (T?)registration.Factory(this);
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }
            catch (Exception ex)
            {
                // Isolate one provider's construction failure so the rest still register, but name the
                // provider and surface the exception so configuration/credential errors are not silent.
                _log.Warning(
                    ex,
                    "Failed to create {ProviderKind} provider {Provider}; skipping it for this run",
                    providerKind,
                    registration.ProviderId);
            }
        }

        return providers.OrderBy(priority).ToList();
    }


    internal IHistoricalDataProvider? CreateSyntheticBackfillProvider(SyntheticMarketDataConfig? cfg)
    {
        if (!EnabledWhenOptedIn(cfg?.Enabled))
            return null;

        return new SyntheticHistoricalDataProvider(cfg);
    }

    internal IHistoricalDataProvider? CreateIbBackfillProvider(IBOptions? cfg)
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

    internal IHistoricalDataProvider? CreateAlpacaBackfillProvider(AlpacaBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;

        var configuredOptions = ApplyAlpacaModuleSettings(new AlpacaOptions(
            KeyId: FirstNonBlank(cfg?.KeyId, _config.Alpaca?.KeyId) ?? string.Empty,
            SecretKey: FirstNonBlank(cfg?.SecretKey, _config.Alpaca?.SecretKey) ?? string.Empty,
            Feed: FirstNonBlank(cfg?.Feed, _config.Alpaca?.Feed) ?? "iex",
            UseSandbox: _config.Alpaca?.UseSandbox ?? false,
            SubscribeQuotes: _config.Alpaca?.SubscribeQuotes ?? false));
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
            feed: configuredOptions.Feed,
            adjustment: cfg?.Adjustment ?? "all",
            priority: cfg?.Priority ?? 5,
            rateLimitPerMinute: cfg?.RateLimitPerMinute ?? 200,
            log: _log);
    }

    internal IHistoricalDataProvider? CreateYahooBackfillProvider(YahooBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;
        return new YahooFinanceHistoricalDataProvider(log: _log);
    }

    internal IHistoricalDataProvider? CreatePolygonBackfillProvider(PolygonBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<PolygonHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "POLYGON_API_KEY",
            cfg?.ApiKey,
            apiKey => new PolygonHistoricalDataProvider(apiKey: apiKey, log: _log));

    internal IHistoricalDataProvider? CreateTiingoBackfillProvider(TiingoBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<TiingoHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "TIINGO_API_TOKEN",
            cfg?.ApiToken,
            token => new TiingoHistoricalDataProvider(apiToken: token, log: _log));

    internal IHistoricalDataProvider? CreateTwelveDataBackfillProvider()
        => CreateCredentialGatedBackfillProvider<TwelveDataHistoricalDataProvider>(
            enabled: true,
            "TWELVEDATA_API_KEY",
            configuredValue: null,
            apiKey => new TwelveDataHistoricalDataProvider(apiKey: apiKey, log: _log));

    internal IHistoricalDataProvider? CreateFinnhubBackfillProvider(FinnhubBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<FinnhubHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "FINNHUB_API_KEY",
            cfg?.ApiKey,
            apiKey => new FinnhubHistoricalDataProvider(apiKey: apiKey, log: _log));

    internal IHistoricalDataProvider? CreateStooqBackfillProvider(StooqBackfillConfig? cfg)
    {
        if (!EnabledByDefault(cfg?.Enabled))
            return null;
        return new StooqHistoricalDataProvider(log: _log);
    }

    internal IHistoricalDataProvider? CreateAlphaVantageBackfillProvider(AlphaVantageBackfillConfig? cfg)
        // Opt-in only: the free tier is severely rate-limited, so absent config leaves it disabled.
        => CreateCredentialGatedBackfillProvider<AlphaVantageHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "ALPHA_VANTAGE_API_KEY",
            cfg?.ApiKey,
            apiKey => new AlphaVantageHistoricalDataProvider(apiKey: apiKey, log: _log));

    internal IHistoricalDataProvider? CreateFredBackfillProvider(FredBackfillConfig? cfg)
        => CreateCredentialGatedBackfillProvider<FredHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "FRED_API_KEY",
            cfg?.ApiKey,
            apiKey => new FredHistoricalDataProvider(apiKey: apiKey, log: _log));

    internal IHistoricalDataProvider? CreateNasdaqBackfillProvider(NasdaqBackfillConfig? cfg)
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

    internal IHistoricalDataProvider? CreateRobinhoodBackfillProvider(RobinhoodConfig? cfg)
        // Opt-in only: unofficial API that requires an explicitly supplied access token.
        => CreateCredentialGatedBackfillProvider<RobinhoodHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "ROBINHOOD_ACCESS_TOKEN",
            configuredValue: null,
            accessToken => new RobinhoodHistoricalDataProvider(
                accessToken: accessToken,
                priority: cfg!.Priority,
                log: _log));

    internal ISymbolSearchProvider? CreateSyntheticSearchProvider(SyntheticMarketDataConfig? cfg)
    {
        if (!EnabledWhenOptedIn(cfg?.Enabled))
            return null;

        return new SyntheticMarketDataClient(new NullMarketEventPublisher(), cfg);
    }

    internal ISymbolSearchProvider? CreateAlpacaSearchProvider(AlpacaBackfillConfig? cfg)
    {
        // Enabled by default unless config explicitly disables it (credential-based activation).
        if (!EnabledByDefault(cfg?.Enabled))
            return null;

        var credentials = CreateCredentialContext<AlpacaHistoricalDataProvider>(
            ("ALPACA_KEY_ID", FirstNonBlank(cfg?.KeyId, _config.Alpaca?.KeyId)),
            ("ALPACA_SECRET_KEY", FirstNonBlank(cfg?.SecretKey, _config.Alpaca?.SecretKey)));
        var keyId = credentials.Get("ALPACA_KEY_ID");
        var secretKey = credentials.Get("ALPACA_SECRET_KEY");
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
            return null;

        return new AlpacaSymbolSearchProvider(keyId, secretKey, httpClient: null, log: _log);
    }

    internal ISymbolSearchProvider? CreateFinnhubSearchProvider(FinnhubBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<FinnhubHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "FINNHUB_API_KEY",
            cfg?.ApiKey,
            apiKey => new FinnhubSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    internal ISymbolSearchProvider? CreateTiingoSearchProvider(TiingoBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<TiingoHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "TIINGO_API_TOKEN",
            cfg?.ApiToken,
            token => new TiingoSymbolSearchProvider(token, httpClient: null, log: _log));

    internal ISymbolSearchProvider? CreateAlphaVantageSearchProvider(AlphaVantageBackfillConfig? cfg)
        // Keep Alpha Vantage opt-in to avoid consuming the constrained free-tier quota implicitly.
        => CreateCredentialGatedSearchProvider<AlphaVantageHistoricalDataProvider>(
            EnabledWhenOptedIn(cfg?.Enabled),
            "ALPHA_VANTAGE_API_KEY",
            cfg?.ApiKey,
            apiKey => new AlphaVantageSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    internal ISymbolSearchProvider? CreateTwelveDataSearchProvider()
        => CreateCredentialGatedSearchProvider<TwelveDataHistoricalDataProvider>(
            enabled: true,
            "TWELVEDATA_API_KEY",
            configuredValue: null,
            apiKey => new TwelveDataSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    internal ISymbolSearchProvider? CreateFredSearchProvider(FredBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<FredHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "FRED_API_KEY",
            cfg?.ApiKey,
            apiKey => new FredSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    internal ISymbolSearchProvider? CreateNasdaqSearchProvider(NasdaqBackfillConfig? cfg)
        => CreateCredentialGatedSearchProvider<NasdaqDataLinkHistoricalDataProvider>(
            EnabledByDefault(cfg?.Enabled),
            "NASDAQ_DATA_LINK_API_KEY",
            cfg?.ApiKey,
            apiKey => new NasdaqDataLinkSymbolSearchProvider(apiKey, httpClient: null, log: _log));

    internal ISymbolSearchProvider? CreatePolygonSearchProvider(PolygonBackfillConfig? cfg)
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
        var enableSymbolResolution = _config.Backfill?.EnableSymbolResolution ?? true;

        return new CompositeHistoricalDataProvider(
            providers,
            enableSymbolResolution ? _symbolResolver : null,
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

    internal IHistoricalDataProvider? CreateCredentialGatedBackfillProvider<TCredentialSource>(
        bool enabled,
        string credentialName,
        string? configuredValue,
        Func<string, IHistoricalDataProvider> factory)
        => CreateCredentialGatedProvider<TCredentialSource, IHistoricalDataProvider>(
            enabled, credentialName, configuredValue, factory);

    internal ISymbolSearchProvider? CreateCredentialGatedSearchProvider<TCredentialSource>(
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

        return WithModuleCredentials(typeof(TProvider), _credentialResolver.CreateContext(typeof(TProvider), configuredLookup));
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
