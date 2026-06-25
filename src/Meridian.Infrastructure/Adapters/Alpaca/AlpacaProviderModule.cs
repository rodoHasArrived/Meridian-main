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
public sealed class AlpacaProviderModule : ConfigurableProviderModuleBase
{
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
        // ----------------------------------------------------------------
        // Historical backfill provider
        // Credentials prefer context (from ProviderModules config) then fall
        // back to env vars. Provider self-reports as unavailable via
        // IsAvailableAsync when neither source provides credentials.
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaHistoricalDataProvider>(_ =>
            new AlpacaHistoricalDataProvider());

        services.AddSingleton<IHistoricalDataProvider>(sp =>
            sp.GetRequiredService<AlpacaHistoricalDataProvider>());

        // ----------------------------------------------------------------
        // Symbol search provider — also prefers context over env vars.
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaSymbolSearchProviderRefactored>(_ =>
            new AlpacaSymbolSearchProviderRefactored());

        services.AddSingleton<ISymbolSearchProvider>(sp =>
            sp.GetRequiredService<AlpacaSymbolSearchProviderRefactored>());

        // ----------------------------------------------------------------
        // Streaming market data client
        // Credential resolution: context (ProviderModules config) → env vars.
        // AlpacaMarketDataClient requires non-empty credentials in its ctor.
        // ----------------------------------------------------------------
        var envCreds = AlpacaCredentialEnvironment.Resolve();
        var streamingKeyId = GetKeyId() ?? envCreds.KeyId;
        var streamingSecretKey = GetSecretKey() ?? envCreds.SecretKey;

        if (!string.IsNullOrWhiteSpace(streamingKeyId) && !string.IsNullOrWhiteSpace(streamingSecretKey))
        {
            var streamingOptions = new AlpacaOptions(
                KeyId: streamingKeyId,
                SecretKey: streamingSecretKey);

            services.AddSingleton<AlpacaMarketDataClient>(sp =>
            {
                var tradeCollector = sp.GetRequiredService<TradeDataCollector>();
                var quoteCollector = sp.GetRequiredService<QuoteCollector>();
                return new AlpacaMarketDataClient(tradeCollector, quoteCollector, streamingOptions);
            });

            services.AddSingleton<IMarketDataClient>(sp =>
                sp.GetRequiredService<AlpacaMarketDataClient>());
        }

        // ----------------------------------------------------------------
        // Brokerage gateway
        // Constructor does not validate credentials eagerly; auth errors
        // surface when orders are submitted.
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaBrokerageGateway>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var brokerageOptions = sp.GetService<AlpacaOptions>()
                                   ?? new AlpacaOptions();
            var logger = sp.GetRequiredService<ILogger<AlpacaBrokerageGateway>>();
            return new AlpacaBrokerageGateway(httpFactory, brokerageOptions, logger);
        });
        services.AddSingleton<IBrokerageAccountCatalog>(sp =>
            sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.AddSingleton<IBrokeragePortfolioSync>(sp =>
            sp.GetRequiredService<AlpacaBrokerageGateway>());
        services.AddSingleton<IBrokerageActivitySync>(sp =>
            sp.GetRequiredService<AlpacaBrokerageGateway>());
    }
}
