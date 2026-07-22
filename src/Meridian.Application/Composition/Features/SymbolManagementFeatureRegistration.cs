using Meridian.Core.Logging;
using Meridian.Core.Subscriptions.Models;
using Meridian.Application.Subscriptions.Services;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Services;
using Meridian.Application.UI;
using Meridian.Application.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers symbol management and search services.
/// </summary>
/// <remarks>
/// Symbol search providers are resolved from <see cref="ProviderRegistry"/> which is populated
/// during provider registration. All symbol search operations go through <see cref="SymbolSearchService"/>.
/// </remarks>
internal sealed class SymbolManagementFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // Canonical symbol registry - identity resolution for canonicalization
        services.AddSingleton<CanonicalSymbolRegistry>();
        services.AddSingleton<Contracts.Catalog.ICanonicalSymbolRegistry>(sp =>
            sp.GetRequiredService<CanonicalSymbolRegistry>());
        services.AddSingleton<SymbolResolutionMismatchTracker>();
        services.AddSingleton<Contracts.Catalog.ISymbolResolutionMismatchReader>(sp =>
            sp.GetRequiredService<SymbolResolutionMismatchTracker>());

        // Canonical resolution spine: registry-first symbol resolver that learns
        // OpenFIGI resolutions back into the registry so all consumers converge.
        services.AddSingleton<Infrastructure.Adapters.Core.SymbolResolution.ISymbolResolver>(sp =>
        {
            var config = sp.GetRequiredService<ConfigStore>().Load();
            var mode = config.DataSources?.SymbolMappings?.ResolutionMode
                ?? config.Backfill?.SymbolResolutionMode
                ?? Contracts.Catalog.SymbolResolutionMode.Compare;
            var reportMismatches = config.DataSources?.SymbolMappings?.ReportMismatches ?? true;
            var apiKey = config.Backfill?.Providers?.OpenFigi?.ApiKey;
            var mismatchTracker = sp.GetRequiredService<SymbolResolutionMismatchTracker>();
            return new Infrastructure.Adapters.Core.SymbolResolution.CanonicalRegistrySymbolResolver(
                sp.GetRequiredService<Contracts.Catalog.ICanonicalSymbolRegistry>(),
                new OpenFigiSymbolResolver(apiKey, log: LoggingSetup.ForContext<OpenFigiSymbolResolver>()),
                LoggingSetup.ForContext<Infrastructure.Adapters.Core.SymbolResolution.CanonicalRegistrySymbolResolver>(),
                mode,
                reportMismatches ? mismatchTracker.Report : null);
        });
        services.AddHostedService<CanonicalSymbolRegistryMigrationService>();

        // Symbol import/export
        services.AddSingleton<SymbolImportExportService>();
        services.AddSingleton<TemplateService>();
        services.AddSingleton<MetadataEnrichmentService>();
        services.AddSingleton<IndexSubscriptionService>();
        services.AddSingleton<WatchlistService>();
        services.AddSingleton<BatchOperationsService>();
        services.AddSingleton<PortfolioImportService>();

        // Symbol search providers - consolidated through SymbolSearchService
        services.AddSingleton<OpenFigiClient>();
        services.AddSingleton<SymbolSearchService>(sp =>
        {
            var metadataService = sp.GetRequiredService<MetadataEnrichmentService>();
            var figiClient = sp.GetRequiredService<OpenFigiClient>();
            var log = LoggingSetup.ForContext<SymbolSearchService>();

            // Priority-based provider discovery
            var providers = GetSymbolSearchProviders(sp, log);

            return new SymbolSearchService(providers, figiClient, metadataService);
        });

        return services;
    }

    /// <summary>
    /// Gets symbol search providers from the unified ProviderRegistry.
    /// </summary>
    private static IEnumerable<ISymbolSearchProvider> GetSymbolSearchProviders(
        IServiceProvider sp,
        ILogger log)
    {
        var registry = sp.GetService<ProviderRegistry>();
        if (registry != null)
        {
            var providers = registry.GetSymbolSearchProviders();
            if (providers.Count > 0)
            {
                log.Information("Using {Count} symbol search providers from ProviderRegistry", providers.Count);
                return providers;
            }
        }

        log.Warning("No symbol search providers available from ProviderRegistry");
        return Array.Empty<ISymbolSearchProvider>();
    }
}
