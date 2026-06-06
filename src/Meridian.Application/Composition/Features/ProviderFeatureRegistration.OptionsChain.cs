using System.Net.Http;
using Meridian.Application.Config;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Contracts;
using Meridian.Instruments.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition.Features;

internal sealed partial class ProviderFeatureRegistration
{
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

    private static IReadOnlyList<ProviderCatalogEntry> BuildMergedProviderCatalog(
        ProviderRegistry registry,
        IEnumerable<IOptionsChainProvider> optionProviders)
    {
        var merged = ProviderCatalog.GetStaticEntries()
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

    private static ProviderCatalogEntry? GetMergedProviderCatalogEntry(
        ProviderRegistry registry,
        IEnumerable<IOptionsChainProvider> optionProviders,
        string providerId)
    {
        return BuildMergedProviderCatalog(registry, optionProviders)
            .FirstOrDefault(entry => string.Equals(entry.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderCatalogEntry MergeCatalogEntries(
        ProviderCatalogEntry existing,
        ProviderCatalogEntry overlay)
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

        return new ProviderCatalogEntry
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
    /// <c>IEnumerable&lt;IOptionsChainProvider&gt;</c> so <see cref="OptionsChainService"/>
    /// can try configured providers before falling back to deterministic synthetic data.
    /// </summary>
    private static void RegisterOptionsChainProviders(IServiceCollection services)
    {
        // 1. Alpaca options — requires ALPACA_KEY_ID + ALPACA_SECRET_KEY
        services.AddSingleton<AlpacaOptionsChainProvider>();

        // 2. Polygon options — requires POLYGON_API_KEY
        services.AddSingleton<PolygonOptionsChainProvider>();

        // 3. Synthetic — always available, deterministic offline fallback
        services.AddSingleton<SyntheticOptionsChainProvider>();

        // Register concrete providers for ordered enumeration and provider-catalog projection.
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
