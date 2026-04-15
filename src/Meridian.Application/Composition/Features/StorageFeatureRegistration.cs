using Meridian.Application.Config;
using Meridian.Application.DirectLending;
using Meridian.Application.EnvironmentDesign;
using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Store;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Export;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Policies;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers storage and data persistence services.
/// </summary>
internal sealed class StorageFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        SecurityMasterStartup.EnsureEnvironmentDefaults();
        DirectLendingStartup.EnsureEnvironmentDefaults();

        var securityMasterOptions = CreateSecurityMasterOptions();
        var directLendingOptions = CreateDirectLendingOptions();

        // StorageOptions - configured from AppConfig or defaults
        services.AddSingleton<StorageOptions>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var compressionEnabled = config.Compress ?? false;
            var dataRoot = configStore.GetDataRoot(config);

            return config.Storage?.ToStorageOptions(dataRoot, compressionEnabled)
                ?? StorageProfilePresets.CreateFromProfile(null, dataRoot, compressionEnabled);
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
        services.AddSingleton<IAuditChainService, AuditChainService>();
        services.AddSingleton<StorageChecksumService>(sp => new StorageChecksumService(null, sp.GetRequiredService<IAuditChainService>()));
        services.AddSingleton<ISymbolRegistryService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var registry = new SymbolRegistryService(storageOptions.RootPath);

            // Initialize the persisted/default registry before any singleton depends on it.
            registry.InitializeAsync().GetAwaiter().GetResult();

            return registry;
        });

        // Position snapshot store — files land under {StorageRoot}/portfolios/ so the
        // LifecyclePolicyEngine governs retention automatically (ADR-002 / ADR-007).
        services.AddSingleton<IPositionSnapshotStore, JsonlPositionSnapshotStore>();

        // Analysis export service for data export operations
        services.AddSingleton<AnalysisExportService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new AnalysisExportService(storageOptions.RootPath);
        });

        services.AddSingleton<RateLimiter>(sp => new RateLimiter(5, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(0.5)));

        if (SecurityMasterStartup.IsConfigured())
        {
            services.AddSingleton(securityMasterOptions);
            services.AddSingleton<IValidateOptions<SecurityMasterOptions>, SecurityMasterOptionsValidator>();
            services.AddSingleton<ISecurityMasterEventStore, PostgresSecurityMasterEventStore>();
            services.AddSingleton<ISecurityMasterSnapshotStore, PostgresSecurityMasterSnapshotStore>();
            services.AddSingleton<ISecurityMasterStore, PostgresSecurityMasterStore>();
            services.AddSingleton<SecurityMasterMigrationRunner>();
            services.AddSingleton<SecurityMasterAggregateRebuilder>();
            services.AddSingleton<SecurityMasterProjectionCache>();
            services.AddSingleton<SecurityMasterProjectionService>();
            services.AddSingleton<SecurityMasterRebuildOrchestrator>();
            services.AddSingleton<ISecurityMasterService, SecurityMasterService>();
            services.AddSingleton<ISecurityMasterAmender>(sp => (ISecurityMasterAmender)sp.GetRequiredService<ISecurityMasterService>());
            services.AddSingleton<SecurityMasterQueryService>();
            services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<ISecurityResolver, SecurityResolver>();
            services.AddHostedService<SecurityMasterProjectionWarmupService>();
            services.AddSingleton<IPolygonCorporateActionFetcher, PolygonCorporateActionFetcher>();
            services.AddSingleton<PolygonCorporateActionFetcher>(sp => (PolygonCorporateActionFetcher)sp.GetRequiredService<IPolygonCorporateActionFetcher>());
            services.AddHostedService<PolygonCorporateActionFetcher>(sp => sp.GetRequiredService<PolygonCorporateActionFetcher>());
            services.AddSingleton<ITradingParametersBackfillService, TradingParametersBackfillService>();

            // Security Master bulk import services
            services.AddSingleton<SecurityMasterCsvParser>();
            services.AddSingleton<ISecurityMasterImportService, SecurityMasterImportService>();
            services.AddSingleton<ISecurityMasterIngestStatusService>(sp => (ISecurityMasterIngestStatusService)sp.GetRequiredService<ISecurityMasterImportService>());
            services.AddSingleton<ISecurityMasterConflictService, SecurityMasterConflictService>();
        }

        // Register null/stub implementations as fallbacks when Security Master is not configured.
        // These ensure that ASP.NET Core Minimal API routing initialises correctly (unregistered
        // service parameters cause startup crashes) while returning sensible empty / error responses.
        services.TryAddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService, NullSecurityMasterQueryService>();
        services.TryAddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService, NullSecurityMasterQueryService>();
        services.TryAddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterService, NullSecurityMasterService>();
        services.TryAddSingleton<ISecurityMasterAmender, NullSecurityMasterService>();
        services.TryAddSingleton<ISecurityMasterConflictService, NullSecurityMasterConflictService>();
        services.TryAddSingleton<ISecurityMasterImportService, NullSecurityMasterImportService>();
        services.TryAddSingleton<ISecurityMasterIngestStatusService>(sp => (ISecurityMasterIngestStatusService)sp.GetRequiredService<ISecurityMasterImportService>());
        services.TryAddSingleton<ISecurityMasterEventStore, NullSecurityMasterEventStore>();

        if (DirectLendingStartup.IsConfigured())
        {
            services.AddSingleton(directLendingOptions);
            services.AddSingleton<DirectLendingEventRebuilder>();
            services.AddSingleton<IDirectLendingStateStore, PostgresDirectLendingStateStore>();
            services.AddSingleton<IDirectLendingOperationsStore>(sp => (PostgresDirectLendingStateStore)sp.GetRequiredService<IDirectLendingStateStore>());
            services.AddSingleton<DirectLendingMigrationRunner>();
            services.AddSingleton<IDirectLendingQueryService, PostgresDirectLendingQueryService>();
            services.AddSingleton<IDirectLendingCommandService, PostgresDirectLendingCommandService>();
            services.AddSingleton<IDirectLendingService, PostgresDirectLendingService>();
            services.AddHostedService<DirectLendingOutboxDispatcher>();
            services.AddHostedService<DailyAccrualWorker>();
        }

        // Fund accounts and governance structure: keep the in-memory working set, but
        // persist local-first snapshots under the configured storage root so operator
        // setup survives restarts while the deeper Postgres governance wave remains future work.
        services.TryAddSingleton<IFundAccountService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var persistencePath = Path.Combine(storageOptions.RootPath, "governance", "fund-accounts.json");
            return new InMemoryFundAccountService(persistencePath);
        });
        services.TryAddSingleton<IGovernanceSharedDataAccessService>(sp =>
            new GovernanceSharedDataAccessService(
                sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                sp.GetService<HistoricalDataQueryService>(),
                sp.GetService<BackfillCoordinator>()));
        services.TryAddSingleton<IFundStructureService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var fundAccountService = sp.GetRequiredService<IFundAccountService>();
            var sharedDataAccessService = sp.GetService<IGovernanceSharedDataAccessService>();
            var securityMasterQueryService = sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
            var persistencePath = Path.Combine(storageOptions.RootPath, "governance", "fund-structure.json");
            return new InMemoryFundStructureService(
                fundAccountService,
                sharedDataAccessService,
                securityMasterQueryService,
                persistencePath);
        });
        services.TryAddSingleton<EnvironmentDesignerService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            var persistencePath = Path.Combine(storageOptions.RootPath, "governance", "environment-designer.json");
            return new EnvironmentDesignerService(persistencePath);
        });
        services.TryAddSingleton<IEnvironmentDesignService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.TryAddSingleton<IEnvironmentValidationService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.TryAddSingleton<IEnvironmentPublishService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.TryAddSingleton<IEnvironmentRuntimeProjectionService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        return services;
    }

    private static SecurityMasterOptions CreateSecurityMasterOptions()
        => new()
        {
            ConnectionString = Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable) ?? string.Empty,
            Schema = Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable) ?? SecurityMasterStartup.DefaultSchema,
            SnapshotIntervalVersions = ParseInt("MERIDIAN_SECURITY_MASTER_SNAPSHOT_INTERVAL", 50),
            ProjectionReplayBatchSize = ParseInt("MERIDIAN_SECURITY_MASTER_REPLAY_BATCH_SIZE", 500),
            PreloadProjectionCache = ParseBool("MERIDIAN_SECURITY_MASTER_PRELOAD_CACHE", true),
            ResolveInactiveByDefault = ParseBool("MERIDIAN_SECURITY_MASTER_RESOLVE_INACTIVE", true)
        };

    private static DirectLendingOptions CreateDirectLendingOptions()
        => new()
        {
            ConnectionString = Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable) ?? string.Empty,
            Schema = Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable) ?? DirectLendingStartup.DefaultSchema,
            SnapshotIntervalVersions = ParseInt("MERIDIAN_DIRECT_LENDING_SNAPSHOT_INTERVAL", 50),
            CurrentEventSchemaVersion = ParseInt("MERIDIAN_DIRECT_LENDING_EVENT_SCHEMA_VERSION", 1),
            ProjectionEngineVersion = Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_PROJECTION_ENGINE_VERSION") ?? "dl-engine-v1",
            OutboxBatchSize = ParseInt("MERIDIAN_DIRECT_LENDING_OUTBOX_BATCH_SIZE", 50),
            OutboxPollIntervalSeconds = ParseInt("MERIDIAN_DIRECT_LENDING_OUTBOX_POLL_SECONDS", 5),
            ReplayBatchSize = ParseInt("MERIDIAN_DIRECT_LENDING_REPLAY_BATCH_SIZE", 250)
        };

    private static int ParseInt(string name, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;

    private static bool ParseBool(string name, bool defaultValue)
        => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;
}
