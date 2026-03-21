using Meridian.Application.Config;
using Meridian.Application.UI;
using Meridian.Contracts.Store;
using Meridian.Storage;
using Meridian.Storage.Export;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Policies;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers storage and data persistence services.
/// </summary>
internal sealed class StorageFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // StorageOptions - configured from AppConfig or defaults
        services.AddSingleton<StorageOptions>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var compressionEnabled = config.Compress ?? false;

            return config.Storage?.ToStorageOptions(config.DataRoot, compressionEnabled)
                ?? StorageProfilePresets.CreateFromProfile(null, config.DataRoot, compressionEnabled);
        });

        // Source registry for data source tracking
        services.AddSingleton<ISourceRegistry>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new SourceRegistry(config.Sources?.PersistencePath);
        });

        // Core storage services
        services.AddSingleton<IFileMaintenanceService, FileMaintenanceService>();
        services.AddSingleton<IDataQualityService, DataQualityService>();
        services.AddSingleton<IStorageSearchService, StorageSearchService>();
        services.AddSingleton<ITierMigrationService, TierMigrationService>();
        services.AddSingleton<ISymbolRegistryService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var registry = new SymbolRegistryService(storageOptions.RootPath);

            // Initialize the persisted/default registry before any singleton depends on it.
            registry.InitializeAsync().GetAwaiter().GetResult();

            return registry;
        });

        // Analysis export service for data export operations
        services.AddSingleton<AnalysisExportService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new AnalysisExportService(storageOptions.RootPath);
        });

        return services;
    }
}
