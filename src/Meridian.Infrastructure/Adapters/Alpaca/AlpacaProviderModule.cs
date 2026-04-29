using Meridian.Application.Config;
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
/// This is the first module implementation and serves as the migration target for the
/// Alpaca adapter family (currently also wired individually in <c>ProviderFeatureRegistration</c>
/// and <c>ProviderFactory</c>). Once this module is used as the primary registration path,
/// the per-capability wiring in those classes can be removed incrementally.
///
/// Credential resolution falls back to environment variables at construction time
/// (<c>ALPACA_KEY_ID</c>, <c>ALPACA_SECRET_KEY</c>), so the module registers even when
/// credentials are not yet configured; providers self-report as unavailable via
/// <see cref="Infrastructure.Adapters.Core.IHistoricalDataProvider.IsAvailableAsync"/>.
/// </remarks>
[ImplementsAdr("ADR-001", "AlpacaProviderModule bundles all Alpaca capability types")]
[ImplementsAdr("ADR-005", "Module-based provider discovery for Alpaca")]
public sealed class AlpacaProviderModule : IProviderModule
{
    /// <inheritdoc/>
    public string ModuleId => "alpaca";

    /// <inheritdoc/>
    public string ModuleDisplayName => "Alpaca Markets";

    /// <inheritdoc/>
    public ProviderCapabilities[] Capabilities =>
    [
        ProviderCapabilities.Streaming(trades: true, quotes: true),
        ProviderCapabilities.BackfillFullFeatured,
        ProviderCapabilities.SymbolSearch,
        ProviderCapabilities.Brokerage(streaming: false, backfill: false)
    ];

    /// <summary>
    /// Validation always passes: providers handle missing credentials gracefully at
    /// runtime (returning empty data or reporting themselves as unavailable).
    /// </summary>
    public ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default)
        => ValueTask.FromResult(ModuleValidationResult.Valid);

    /// <inheritdoc/>
    public void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        // ----------------------------------------------------------------
        // Historical backfill provider
        // Credentials resolved from env vars inside the constructor when
        // not supplied explicitly (ALPACA_KEY_ID / ALPACA_SECRET_KEY).
        // Provider self-reports as unavailable via IsAvailableAsync when
        // credentials are not configured.
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaHistoricalDataProvider>(_ =>
            new AlpacaHistoricalDataProvider());

        services.AddSingleton<IHistoricalDataProvider>(sp =>
            sp.GetRequiredService<AlpacaHistoricalDataProvider>());

        // ----------------------------------------------------------------
        // Symbol search provider
        // Also falls back to env vars; runs in read-only mode without creds.
        // ----------------------------------------------------------------
        services.AddSingleton<AlpacaSymbolSearchProviderRefactored>(_ =>
            new AlpacaSymbolSearchProviderRefactored());

        services.AddSingleton<ISymbolSearchProvider>(sp =>
            sp.GetRequiredService<AlpacaSymbolSearchProviderRefactored>());

        // ----------------------------------------------------------------
        // Streaming market data client
        // AlpacaMarketDataClient requires non-empty credentials in its ctor.
        // Only register when credentials are discoverable at module-load time;
        // the factory resolves collectors from the DI container on first use.
        // ----------------------------------------------------------------
        var streamingKeyId = Environment.GetEnvironmentVariable("ALPACA_KEY_ID")
                             ?? Environment.GetEnvironmentVariable("ALPACA__KEYID") ?? "";
        var streamingSecretKey = Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")
                                 ?? Environment.GetEnvironmentVariable("ALPACA__SECRETKEY") ?? "";

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
                                   ?? new AlpacaOptions(
                                       KeyId: Environment.GetEnvironmentVariable("ALPACA_KEY_ID")
                                              ?? Environment.GetEnvironmentVariable("ALPACA__KEYID") ?? "",
                                       SecretKey: Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")
                                                  ?? Environment.GetEnvironmentVariable("ALPACA__SECRETKEY") ?? "");
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
