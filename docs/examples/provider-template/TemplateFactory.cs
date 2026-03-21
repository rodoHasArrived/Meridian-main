// SCAFFOLD FILE — copy to your provider directory and rename every "Template" occurrence.
//
// This factory shows how to create and integrate your provider into the system.
// After implementing your provider classes, add them to the relevant factory methods
// and update the configuration bindings described below.
//
// Integration checklist — backfill / symbol search:
//   1. Add the creation method(s) from TemplateProviderFactory into ProviderFactory at:
//      src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs
//   2. Call the new method inside CreateBackfillProviders() and/or CreateSymbolSearchProviders().
//   3. Add `{YourProvider}Config? YourProvider = null` to BackfillProvidersConfig in BackfillConfig.cs.
//   4. Wire the new config property in ProviderFactory.CreateBackfillProviders() and/or
//      ProviderFactory.CreateSymbolSearchProviders() (follow the existing provider examples).
//
// Integration checklist — real-time streaming:
//   5. Register the streaming factory in ServiceCompositionRoot.RegisterStreamingFactories():
//      registry.RegisterFactory(sp => new {YourProvider}MarketDataClient(
//          sp.GetRequiredService<TradeDataCollector>(),
//          sp.GetRequiredService<QuoteCollector>()));
//   6. Bind {YourProvider}Options in AppConfig and resolve credentials from the composition root.
//
// See docs/development/provider-implementation.md for the full step-by-step guide.

using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Template;

/// <summary>
/// Factory helpers for creating and registering Template provider instances.
///
/// TODO: Rename to <c>{YourProvider}ProviderFactory</c>.
/// TODO: After integration testing, merge the Create* methods into
///       <c>ProviderFactory</c> in <c>Adapters/Core/ProviderFactory.cs</c>.
/// </summary>
[ImplementsAdr("ADR-001", "Template provider factory")]
internal static class TemplateProviderFactory
{
    // ------------------------------------------------------------------ //
    //  Backfill (historical data) provider                                //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a <see cref="TemplateHistoricalDataProvider"/> if the configuration is valid.
    /// Returns <see langword="null"/> when the provider is disabled or required credentials are missing.
    /// </summary>
    /// <param name="cfg">
    /// Backfill configuration for this provider.
    /// May be <see langword="null"/> when the provider has no configuration section;
    /// credentials are then resolved exclusively from environment variables.
    /// </param>
    /// <param name="log">Optional logger; omit to use a type-scoped context logger.</param>
    /// <returns>A configured provider instance, or <see langword="null"/>.</returns>
    internal static IHistoricalDataProvider? CreateBackfillProvider(
        TemplateBackfillConfig? cfg,
        ILogger? log = null)
    {
        // Return null when the provider is explicitly disabled in configuration.
        if (!(cfg?.Enabled ?? true))
            return null;

        // Resolve credentials — prefer the config value, then fall back to the env var.
        var apiKey = cfg?.ApiKey ?? Environment.GetEnvironmentVariable("TEMPLATE__APIKEY");

        // If the provider requires an API key, guard here and return null when missing.
        if (string.IsNullOrEmpty(apiKey))
            return null;

        // Forward additional constructor arguments (log, priority, rateLimitPerMinute) as needed.
        return new TemplateHistoricalDataProvider(apiKey: apiKey);
    }

    // ------------------------------------------------------------------ //
    //  Symbol search provider                                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a <see cref="TemplateSymbolSearchProvider"/> if the configuration is valid.
    /// Returns <see langword="null"/> when the provider is disabled or credentials are missing.
    /// </summary>
    /// <param name="cfg">
    /// Backfill configuration; reused here because the search and backfill providers
    /// typically share the same API key.
    /// </param>
    /// <param name="log">Optional logger.</param>
    /// <returns>A configured search provider, or <see langword="null"/>.</returns>
    // Remove this method entirely if the provider does not support symbol search.
    internal static ISymbolSearchProvider? CreateSearchProvider(
        TemplateBackfillConfig? cfg,
        ILogger? log = null)
    {
        if (cfg != null && !cfg.Enabled)
            return null;

        var apiKey = cfg?.ApiKey ?? Environment.GetEnvironmentVariable("TEMPLATE__APIKEY");

        // If credentials are required for symbol search, guard here.
        if (string.IsNullOrEmpty(apiKey))
            return null;

        return new TemplateSymbolSearchProvider(apiKey: apiKey);
    }

    // ------------------------------------------------------------------ //
    //  Real-time streaming client                                         //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a <see cref="TemplateMarketDataClient"/> for real-time streaming.
    /// Because streaming clients require collectors from the DI container, this method
    /// is typically invoked from the composition root rather than called directly.
    /// </summary>
    /// <param name="options">Streaming configuration for this provider.</param>
    /// <param name="tradeCollector">Trade event collector (resolved from DI).</param>
    /// <param name="quoteCollector">Quote event collector (resolved from DI).</param>
    /// <param name="log">Optional logger.</param>
    /// <returns>A configured streaming client, or <see langword="null"/> if credentials are missing.</returns>
    // Remove this method entirely if the provider supports historical data only.
    internal static IMarketDataClient? CreateStreamingClient(
        TemplateStreamingOptions? options,
        TradeDataCollector tradeCollector,
        QuoteCollector quoteCollector,
        ILogger? log = null)
    {
        // Resolve credentials — prefer options value, then fall back to the env var.
        var apiKey = options?.ApiKey ?? Environment.GetEnvironmentVariable("TEMPLATE__APIKEY");
        if (string.IsNullOrEmpty(apiKey))
            return null;

        // Pass options to the constructor once the TemplateStreamingOptions parameter is added.
        return new TemplateMarketDataClient(tradeCollector, quoteCollector);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Provider module — optional DI integration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Optional DI module for registering Template provider services.
/// Implement this if the provider requires additional DI registrations such as
/// named <see cref="System.Net.Http.HttpClient"/> instances, options bindings, or caching services.
///
/// TODO: Rename to <c>{YourProvider}ProviderModule</c>.
/// TODO: Remove this class entirely if no additional DI registration is needed.
/// </summary>
[ImplementsAdr("ADR-001", "Template provider DI module")]
public sealed class TemplateProviderModule : IProviderModule
{
    /// <inheritdoc/>
    public void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        // TODO: Register a named HttpClient for this provider:
        // services.AddHttpClient(HttpClientNames.TemplateHistorical, client =>
        // {
        //     client.BaseAddress = new Uri(TemplateEndpoints.BaseUrl);
        //     client.DefaultRequestHeaders.Add("Accept", "application/json");
        // });

        // TODO: Bind streaming options from configuration:
        // services.AddOptions<TemplateStreamingOptions>()
        //         .BindConfiguration("Template");

        // TODO: Register data sources in the DataSourceRegistry if needed.
        // registry.Register(new DataSourceConfiguration("template", ...));
    }
}
