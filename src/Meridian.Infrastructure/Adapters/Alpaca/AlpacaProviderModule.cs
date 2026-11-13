using Meridian.Core.IO;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IMarketDataClient = Meridian.Infrastructure.IMarketDataClient;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Provider module for Alpaca Markets. Registers all four Alpaca capability types —
/// streaming, historical backfill, symbol search, and brokerage — in a single DI call.
/// </summary>
/// <remarks>
/// Credentials are resolved in priority order: (1) <see cref="ProviderModuleContext"/>
/// injected via <see cref="ProviderModuleLoader"/> when a <c>ProviderModules.alpaca</c>
/// config entry is present, then (2) <c>ALPACA_KEY_ID</c> / <c>ALPACA_SECRET_KEY</c>
/// environment variables. Providers self-report as unavailable via
/// <see cref="Infrastructure.Adapters.Core.IHistoricalDataProvider.IsAvailableAsync"/>
/// when no credentials are found at registration time.
/// </remarks>
[ImplementsAdr("ADR-001", "AlpacaProviderModule bundles all Alpaca capability types")]
[ImplementsAdr("ADR-005", "Module-based provider discovery for Alpaca")]
public sealed class AlpacaProviderModule : ConfigurableProviderModuleBase, IProviderModuleCredentialHints, IProviderModuleConnectionProbe
{
    private const string AlpacaBrokerApiBase = "https://broker-api.sandbox.alpaca.markets";
    private static readonly HttpClient SharedProbeClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _probeClient;

    /// <summary>Creates the module. Module discovery requires this parameterless constructor.</summary>
    public AlpacaProviderModule() : this(SharedProbeClient)
    {
    }

    /// <summary>Creates the module with the client its setup probe sends through.</summary>
    internal AlpacaProviderModule(HttpClient probeClient) => _probeClient = probeClient;

    /// <inheritdoc/>
    public IReadOnlyList<string> CredentialKeyHints => ["keyId", "secretKey"];

    /// <inheritdoc/>
    public override string ModuleId => "alpaca";

    /// <inheritdoc/>
    public override string ModuleDisplayName => "Alpaca Markets";

    /// <inheritdoc/>
    public override ProviderCapabilities[] Capabilities =>
    [
        ProviderCapabilities.Streaming(trades: true, quotes: true),
        ProviderCapabilities.BackfillFullFeatured,
        ProviderCapabilities.SymbolSearch,
        ProviderCapabilities.Brokerage(streaming: false, backfill: false)
    ];

    /// <inheritdoc/>
    public override void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        // Credential resolution for all providers: context (ProviderModules config) → env vars.
        var tradingCredentials = ResolveTradingCredentials();
        var keyId = tradingCredentials.KeyId;
        var secretKey = tradingCredentials.SecretKey;

        // Settings resolution: context overrides AlpacaOptions defaults. useSandbox selects the
        // market-data stream sandbox only; the paper or live Trading API is chosen by
        // ResolveTradingCredentials.
        var feed = GetSetting("feed") ?? "iex";
        var useSandbox = bool.TryParse(GetSetting("useSandbox"), out var sbVal) && sbVal;
        var subscribeQuotes = bool.TryParse(GetSetting("subscribeQuotes"), out var sqVal) && sqVal;

        // ----------------------------------------------------------------
        // Historical backfill provider
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaHistoricalDataProvider>(_ =>
            new AlpacaHistoricalDataProvider(keyId: keyId, secretKey: secretKey, feed: feed));

        services.AddSingleton<IHistoricalDataProvider>(sp =>
            sp.GetRequiredService<AlpacaHistoricalDataProvider>());

        // ----------------------------------------------------------------
        // Symbol search provider
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaSymbolSearchProvider>(_ =>
            new AlpacaSymbolSearchProvider(keyId: keyId, secretKey: secretKey));

        services.AddSingleton<ISymbolSearchProvider>(sp =>
            sp.GetRequiredService<AlpacaSymbolSearchProvider>());

        // ----------------------------------------------------------------
        // Streaming market data client
        // AlpacaMarketDataClient requires non-empty credentials in its ctor.
        // ----------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(secretKey))
        {
            var streamingOptions = new AlpacaOptions(
                KeyId: keyId,
                SecretKey: secretKey,
                Feed: feed,
                UseSandbox: useSandbox,
                SubscribeQuotes: subscribeQuotes);

            services.AddSingleton<AlpacaMarketDataClient>(sp =>
            {
                var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
                var quoteCollector = sp.GetRequiredService<QuoteCollector>();
                return new AlpacaMarketDataClient(tradeCollector, quoteCollector, streamingOptions);
            });

            services.AddSingleton<IMarketDataClient>(sp =>
                sp.GetRequiredService<AlpacaMarketDataClient>());

            // Keep asset classes independently connectable. The host still selects the equities
            // adapter as its compatibility default; operators and future routing can resolve the
            // other streams without treating their entitlements as equivalent.
            services.AddSingleton<AlpacaOptionsMarketDataClient>(sp => new(
                sp.GetRequiredService<TradeDataCollector>(), sp.GetRequiredService<QuoteCollector>(), streamingOptions));
            services.AddSingleton<AlpacaCryptoMarketDataClient>(sp => new(
                sp.GetRequiredService<TradeDataCollector>(), sp.GetRequiredService<QuoteCollector>(), streamingOptions));
            services.AddSingleton<AlpacaNewsEventBuffer>();
            services.AddSingleton<IAlpacaNewsEventSink>(sp => sp.GetRequiredService<AlpacaNewsEventBuffer>());
            services.AddSingleton<AlpacaNewsMarketDataClient>(sp => new(
                sp.GetRequiredService<TradeDataCollector>(), sp.GetRequiredService<QuoteCollector>(), streamingOptions,
                sp.GetRequiredService<IAlpacaNewsEventSink>()));
            services.AddSingleton<IAlpacaAssetStream>(sp => sp.GetRequiredService<AlpacaMarketDataClient>());
            services.AddSingleton<IAlpacaAssetStream>(sp => sp.GetRequiredService<AlpacaOptionsMarketDataClient>());
            services.AddSingleton<IAlpacaAssetStream>(sp => sp.GetRequiredService<AlpacaCryptoMarketDataClient>());
            services.AddSingleton<IAlpacaAssetStream>(sp => sp.GetRequiredService<AlpacaNewsMarketDataClient>());
            services.AddSingleton<IAlpacaMarketDataRouter, AlpacaMarketDataRouter>();
        }

        // ----------------------------------------------------------------
        // Brokerage gateway
        // Constructor does not validate credentials eagerly; auth errors
        // surface when orders are submitted.
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaBrokerageGateway>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<AlpacaBrokerageGateway>>();
        var streamLogger = sp.GetRequiredService<ILogger<AlpacaTradeUpdatesClient>>();
        if (!tradingCredentials.HasCredentials)
        {
            // No module credentials: fall back to a registered AlpacaOptions or empty, and let
            // the gateway resolve whatever the environment holds when it connects.
            var fallbackOptions = sp.GetService<AlpacaOptions>() ?? new AlpacaOptions();
            return new AlpacaBrokerageGateway(httpFactory, fallbackOptions, logger,
                new AlpacaTradeUpdatesClient(fallbackOptions, streamLogger, sp.GetRequiredService<IAtomicFileWriter>()));
        }

        // The gateway and its trade-update stream are pinned to the same key pair and the same
        // paper or live environment the setup probe checks, across requests and reconnects.
        var brokerageOptions = new AlpacaOptions(
            KeyId: keyId, SecretKey: secretKey, Feed: feed, UseSandbox: useSandbox, SubscribeQuotes: subscribeQuotes);
        var stream = new AlpacaTradeUpdatesClient(brokerageOptions, streamLogger, sp.GetRequiredService<IAtomicFileWriter>(),
            credentials: tradingCredentials);
        return new AlpacaBrokerageGateway(httpFactory, brokerageOptions, logger, stream, tradingCredentials);
    });
        services.AddSingleton<IBrokerageAccountCatalog>(sp =>
            sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.AddSingleton<IBrokeragePortfolioSync>(sp =>
            sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.AddSingleton<IBrokerageActivitySync>(sp =>
            sp.GetRequiredService<AlpacaBrokerageGateway>());
    }

    /// <summary>
    /// The one decision of which key pair and which Trading API environment this module uses,
    /// shared by the registered brokerage gateway and the setup probe. Keys come from the module
    /// context, then the environment. Paper or live comes from
    /// <c>ALPACA_TRADING_ENVIRONMENT</c> and is paper unless that says live. The
    /// <c>useSandbox</c> setting is not consulted: it selects the market-data stream sandbox.
    /// </summary>
    internal AlpacaCredentialSnapshot ResolveTradingCredentials()
    {
        var environment = AlpacaCredentialEnvironment.Resolve();
        return environment with
        {
            KeyId = GetKeyId() ?? environment.KeyId,
            SecretKey = GetSecretKey() ?? environment.SecretKey,
        };
    }

    /// <summary>The Trading API host the gateway and the probe use for these credentials.</summary>
    internal static string TradingApiBaseUrl(AlpacaCredentialSnapshot credentials) =>
        AlpacaTradingApi.BaseUrl(credentials.UseSandbox);

    /// <inheritdoc/>
    public async Task<ModuleProbeResult> ProbeConnectionAsync(CancellationToken ct = default)
    {
        var credentials = ResolveTradingCredentials();
        if (!credentials.HasCredentials)
            return ModuleProbeResult.Failure("No credentials available to probe connection");

        // The same key pair and environment the registered brokerage gateway is pinned to, so
        // the probe validates what the module will actually connect to.
        var keyId = credentials.KeyId;
        var secretKey = credentials.SecretKey;
        var useSandbox = credentials.UseSandbox;
        var probeBase = TradingApiBaseUrl(credentials);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{probeBase}/v2/account");
            request.Headers.Add("APCA-API-KEY-ID", keyId);
            request.Headers.Add("APCA-API-SECRET-KEY", secretKey);
            using var response = await _probeClient.SendAsync(request, ct).ConfigureAwait(false);
            sw.Stop();
            if (response.IsSuccessStatusCode)
                return ModuleProbeResult.Success(sw.Elapsed.TotalMilliseconds,
                    $"Alpaca {(useSandbox ? "paper" : "live")} account reachable");

            return ModuleProbeResult.Failure($"HTTP {(int)response.StatusCode} from Alpaca API");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ModuleProbeResult.Failure($"Connection failed: {ex.Message}");
        }
    }
}
