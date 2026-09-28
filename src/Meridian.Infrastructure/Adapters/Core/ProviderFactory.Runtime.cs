using Meridian.Core.Config;
using Meridian.Core.Monitoring;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Meridian.Infrastructure.Adapters.Core;

public sealed partial class ProviderFactory
{
    private readonly IServiceProvider? _services;
    private readonly IReadOnlyDictionary<string, ProviderModuleContext>? _moduleContexts;

    internal AppConfig Config => _config;

    internal object CreateAdapter(Type implementation)
        => ActivatorUtilities.CreateInstance(Services, implementation);

    internal T CreateAdapter<T>() where T : class => (T)CreateAdapter(typeof(T));

    internal ICorporateActionProvider CreateCorporateActionProvider<TProvider, TCredentialSource>(
        params (string Name, string? Value)[] configuredValues) where TProvider : class, ICorporateActionProvider
    {
        var credentials = CreateCredentialContext<TCredentialSource>(configuredValues);
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(Services.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(configuredValues.Select(value =>
                new KeyValuePair<string, string?>(value.Name, credentials.Get(value.Name))))
            .Build();
        return ActivatorUtilities.CreateInstance<TProvider>(Services, configuration);
    }

    private IServiceProvider Services => _services ?? throw new InvalidOperationException(
        "This capability requires the application service provider. Use AddProviderServices during composition.");

    private ICredentialContext WithModuleCredentials(Type implementation, ICredentialContext fallback)
    {
        var id = implementation.GetDataSourceMetadata()?.Id;
        return id is not null && _moduleContexts?.TryGetValue(ProviderIdentity.NormalizeId(id), out var context) == true
            ? new ModuleCredentialContext(context.Credentials, fallback)
            : fallback;
    }

    private sealed class ModuleCredentialContext(
        IReadOnlyDictionary<string, string?> credentials, ICredentialContext fallback) : ICredentialContext
    {
        public string? Get(string name)
        {
            var key = name switch
            {
                _ when name.EndsWith("_KEY_ID", StringComparison.Ordinal) => "keyId",
                _ when name.EndsWith("_SECRET_KEY", StringComparison.Ordinal) => "secretKey",
                _ when name.EndsWith("_API_KEY", StringComparison.Ordinal) => "apiKey",
                _ when name.EndsWith("_API_TOKEN", StringComparison.Ordinal) => "apiToken",
                _ when name.EndsWith("_ACCESS_TOKEN", StringComparison.Ordinal) => "accessToken",
                _ => name
            };
            if ((credentials.TryGetValue(name, out var value) || credentials.TryGetValue(key, out value)) &&
                !string.IsNullOrWhiteSpace(value))
                return value;
            return fallback.Get(name);
        }

        public bool IsConfigured(string name) => !string.IsNullOrWhiteSpace(Get(name));
    }

    internal bool IsFamilyEnabled(string providerId)
    {
        var settings = _config.ProviderModules?.Modules?
            .Where(pair => ProviderIdentity.EqualsId(pair.Key, providerId))
            .Select(static pair => pair.Value)
            .SingleOrDefault();
        return settings?.Enabled != false;
    }

    internal bool IsCapabilityEnabled(ProviderCapabilityRegistration registration)
        => IsFamilyEnabled(registration.ProviderId) && registration.IsEnabled(this);

    internal bool HasRobinhoodOptionsCredentials =>
        _config.Backfill?.Providers?.Robinhood?.Enabled == true &&
        !string.IsNullOrWhiteSpace(GetRobinhoodAccessToken());

    private string? GetRobinhoodAccessToken()
        => CreateCredentialContext<RobinhoodHistoricalDataProvider>().Get("ROBINHOOD_ACCESS_TOKEN");

    internal IMarketDataClient CreateIbStreamingClient() => new IBMarketDataClient(
        Services.GetRequiredService<IMarketEventPublisher>(),
        Services.GetRequiredService<TradeDataCollector>(),
        Services.GetRequiredService<MarketDepthCollector>(),
        Services.GetService<QuoteCollector>(),
        Services.GetService<OptionDataCollector>(),
        _config.IB ?? new IBOptions());

    internal IMarketDataClient CreateAlpacaStreamingClient() => new AlpacaMarketDataClient(
        Services.GetRequiredService<TradeDataCollector>(),
        Services.GetRequiredService<QuoteCollector>(),
        ResolveAlpacaOptions());

    private AlpacaOptions ResolveAlpacaOptions()
    {
        var credentials = CreateCredentialContext<AlpacaMarketDataClient>(
            ("ALPACA_KEY_ID", FirstNonBlank(_config.Alpaca?.KeyId, _config.Backfill?.Providers?.Alpaca?.KeyId)),
            ("ALPACA_SECRET_KEY", FirstNonBlank(_config.Alpaca?.SecretKey, _config.Backfill?.Providers?.Alpaca?.SecretKey)));
        return ApplyAlpacaModuleSettings(_config.Alpaca ?? new AlpacaOptions()) with
        {
            KeyId = credentials.Get("ALPACA_KEY_ID") ?? string.Empty,
            SecretKey = credentials.Get("ALPACA_SECRET_KEY") ?? string.Empty
        };
    }

    private AlpacaOptions ApplyAlpacaModuleSettings(AlpacaOptions options)
    {
        if (_moduleContexts is null || !_moduleContexts.TryGetValue("alpaca", out var context))
            return options;
        return options with
        {
            Feed = context.GetSetting("feed") ?? options.Feed,
            UseSandbox = bool.TryParse(context.GetSetting("useSandbox"), out var sandbox) ? sandbox : options.UseSandbox,
            SubscribeQuotes = bool.TryParse(context.GetSetting("subscribeQuotes"), out var quotes) ? quotes : options.SubscribeQuotes
        };
    }

    internal IMarketDataClient CreatePolygonStreamingClient()
    {
        var credentials = CreateCredentialContext<PolygonMarketDataClient>(
            ("POLYGON_API_KEY", FirstNonBlank(_config.Polygon?.ApiKey, _config.Backfill?.Providers?.Polygon?.ApiKey)));
        return new PolygonMarketDataClient(
            Services.GetRequiredService<IMarketEventPublisher>(),
            Services.GetRequiredService<TradeDataCollector>(),
            Services.GetRequiredService<QuoteCollector>(),
            options: (_config.Polygon ?? new PolygonOptions()) with { ApiKey = credentials.Get("POLYGON_API_KEY") ?? string.Empty },
            reconnectionMetrics: Services.GetService<IReconnectionMetrics>());
    }

    internal IMarketDataClient CreateNyseStreamingClient() => new NyseMarketDataClient(
        Services.GetRequiredService<TradeDataCollector>(),
        Services.GetRequiredService<MarketDepthCollector>(),
        Services.GetRequiredService<QuoteCollector>(),
        Services.GetRequiredService<IHttpClientFactory>(),
        Services.GetService<NYSEOptions>());

    internal IMarketDataClient CreateRobinhoodStreamingClient() => new RobinhoodMarketDataClient(
        Services.GetRequiredService<IHttpClientFactory>(),
        Services.GetRequiredService<QuoteCollector>(),
        Services.GetRequiredService<ILogger<RobinhoodMarketDataClient>>(), GetRobinhoodAccessToken());

    internal IMarketDataClient CreateSyntheticStreamingClient() => new SyntheticMarketDataClient(
        Services.GetRequiredService<IMarketEventPublisher>(), _config.Synthetic);

    internal ISymbolSearchProvider? CreateRobinhoodSearchProvider()
        => _config.Backfill?.Providers?.Robinhood?.Enabled == true
            ? new RobinhoodSymbolSearchProvider(log: _log)
            : null;

    internal IOptionsChainProvider CreateAlpacaOptionsProvider()
    {
        var options = ResolveAlpacaOptions();
        return new AlpacaOptionsChainProvider(Services.GetRequiredService<IHttpClientFactory>(),
            options.KeyId, options.SecretKey, log: _log);
    }

    internal IOptionsChainProvider CreatePolygonOptionsProvider()
    {
        var credentials = CreateCredentialContext<PolygonMarketDataClient>(
            ("POLYGON_API_KEY", FirstNonBlank(_config.Polygon?.ApiKey, _config.Backfill?.Providers?.Polygon?.ApiKey)));
        return new PolygonOptionsChainProvider(Services.GetRequiredService<IHttpClientFactory>(),
            credentials.Get("POLYGON_API_KEY") ?? string.Empty, log: _log);
    }

    internal IOptionsChainProvider CreateRobinhoodOptionsProvider() => new RobinhoodOptionsChainProvider(
        Services.GetRequiredService<IHttpClientFactory>(),
        Services.GetRequiredService<ILogger<RobinhoodOptionsChainProvider>>(), GetRobinhoodAccessToken());

    internal IBrokerageGateway CreateIbBrokerageGateway() => new IBBrokerageGateway(
        _config.IB ?? new IBOptions(), Services.GetRequiredService<ILogger<IBBrokerageGateway>>());

    internal IBrokerageGateway CreateAlpacaBrokerageGateway()
    {
        var options = ResolveAlpacaOptions();
        var stream = new AlpacaTradeUpdatesClient(options, Services.GetRequiredService<ILogger<AlpacaTradeUpdatesClient>>());
        return new AlpacaBrokerageGateway(Services.GetRequiredService<IHttpClientFactory>(), options,
            Services.GetRequiredService<ILogger<AlpacaBrokerageGateway>>(), stream);
    }

    internal IBrokerageGateway CreateRobinhoodBrokerageGateway() => new RobinhoodBrokerageGateway(
        Services.GetRequiredService<IHttpClientFactory>(),
        Services.GetRequiredService<ILogger<RobinhoodBrokerageGateway>>(), GetRobinhoodAccessToken());
}
