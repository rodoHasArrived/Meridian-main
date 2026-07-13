using Meridian.Application.Backfill;
using Meridian.Core.Config;
using Meridian.PortfolioRecords.Accounts;
using Meridian.FinancialOperations.Banking;
using Meridian.Instruments.CryptoCurrency;
using Meridian.Instruments.Deposits;
using Meridian.Instruments.Derivatives;
using Meridian.Application.DirectLending;
using Meridian.Application.Integrations;
using Meridian.Instruments.Equity;
using Meridian.Instruments.FixedIncome;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Platform.FundOperationsPersistence;
using Meridian.Application.FundStructure;
using Meridian.Instruments.Futures;
using Meridian.Instruments.FxSpot;
using Meridian.Instruments.MoneyMarketFunds;
using Meridian.Instruments.Options;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Store;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Historical;
using Meridian.Domain.Reconciliation;
using Meridian.Entities.FundStructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Identity;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.Commodities;
using Meridian.Instruments.CertificatesOfDeposit;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Export;
using Meridian.Storage.Banking;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Integrations;
using Meridian.Storage.MoneyMarket;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Ledger;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Policies;
using Meridian.Storage.Query;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Meridian.Workflow.EnvironmentDesign;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        AssetOperationsStartup.EnsureEnvironmentDefaults();
        DirectLendingStartup.EnsureEnvironmentDefaults();
        LedgerStartup.EnsureEnvironmentDefaults();

        var securityMasterOptions = CreateSecurityMasterOptions();
        var assetOperationsOptions = CreateAssetOperationsOptions();
        var directLendingOptions = CreateDirectLendingOptions();
        var ledgerOptions = CreateLedgerOptions();

        services.TryAddSingleton<ISecurityValidationSnapshotStore, FileSecurityValidationSnapshotStore>();
        services.TryAddSingleton<ISecurityValidationGateService, SecurityValidationGateService>();
        services.AddStatementReconciliationServices();

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
        services.TryAddSingleton<IProviderIntegrationManifestStore>(sp =>
            new FileProviderIntegrationManifestStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<ProviderIntegrationTemplateCatalog>();
        services.TryAddSingleton<ProviderIntegrationDryRunService>();
        services.TryAddSingleton<IProviderIntegrationHttpTransport>(sp =>
            new ProviderIntegrationHttpClientTransport(
                new HttpClient(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ProviderIntegrationHttpClientTransport>>()));
        services.TryAddSingleton<ProviderIntegrationRestDryRunService>();
        services.TryAddSingleton<ProviderIntegrationOpenApiImportService>();
        services.TryAddSingleton<ProviderIntegrationSetupService>();
        services.TryAddSingleton<ProviderIntegrationActivationReadinessService>();
        services.TryAddSingleton<ProviderIntegrationActivationService>();
        services.TryAddSingleton<ProviderIntegrationMonitoringService>();
        services.TryAddSingleton<ProviderIntegrationSyncPlanningService>();
        services.TryAddSingleton<ProviderIntegrationSyncOrchestrationService>();
        services.TryAddSingleton<ProviderIntegrationSchemaDriftService>();
        services.TryAddSingleton<ProviderIntegrationStagingReviewService>();
        services.TryAddSingleton<ProviderIntegrationIdentityResolutionPreviewService>();
        services.TryAddSingleton<ProviderIntegrationPromotionReadinessService>();
        services.TryAddSingleton<ProviderIntegrationReconciliationHandoffService>();
        services.TryAddSingleton<ProviderIntegrationQuarantineReviewService>();
        services.TryAddSingleton<ProviderIntegrationQuarantineReplayService>();
        if (IsScopedAccessPostgresConfigured())
        {
            services.TryAddSingleton(new ScopedAccessStoreOptions
            {
                ConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING")!,
                Schema = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA") ?? "identity_access"
            });
            services.TryAddSingleton<PostgresScopedAccessAssignmentStore>(sp =>
                new PostgresScopedAccessAssignmentStore(sp.GetRequiredService<ScopedAccessStoreOptions>()));
            services.TryAddSingleton<IScopedAccessAssignmentStore>(sp =>
                sp.GetRequiredService<PostgresScopedAccessAssignmentStore>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ScopedAccessAssignmentStoreMigrationHostedService>());
        }
        else
        {
            services.TryAddSingleton<IScopedAccessAssignmentStore>(sp =>
            {
                var storageOptions = sp.GetRequiredService<StorageOptions>();
                var persistencePath = Path.Combine(storageOptions.RootPath, "governance", "user-access-assignments.json");
                return new FileScopedAccessAssignmentStore(
                    persistencePath,
                    sp.GetService<ILogger<FileScopedAccessAssignmentStore>>());
            });
        }
        services.TryAddSingleton<IAccessScopeLineageProvider, FundStructureAccessScopeLineageProvider>();
        services.TryAddSingleton<ScopedAccessService>();
        services.TryAddSingleton<IScopedAccessAssignmentService>(sp => sp.GetRequiredService<ScopedAccessService>());
        services.TryAddSingleton<IScopedAuthorizationService>(sp => sp.GetRequiredService<ScopedAccessService>());
        services.TryAddSingleton<ISecurityAssetProfileGovernanceService, SecurityAssetProfileGovernanceService>();
        services.TryAddSingleton<ISecurityAssetProfileCatalog>(sp => sp.GetRequiredService<ISecurityAssetProfileGovernanceService>());
        services.TryAddSingleton(sp => AssetClassValidatorRegistry.CreateDefault(sp.GetRequiredService<ISecurityAssetProfileCatalog>()));

        // Source registry for data source tracking
        services.AddSingleton<ISourceRegistry>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new SourceRegistry(config.Sources?.PersistencePath);
        });

        // Core storage services
        services.TryAddSingleton<StorageCatalogService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new StorageCatalogService(storageOptions.RootPath, storageOptions);
        });
        services.TryAddSingleton<IStorageCatalogService>(sp => sp.GetRequiredService<StorageCatalogService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StorageCatalogInitializationHostedService>());
        services.TryAddSingleton<DuckDbQueryService>(sp => new DuckDbQueryService(
            sp.GetRequiredService<IStorageCatalogService>(),
            sp.GetRequiredService<StorageOptions>()));
        services.AddSingleton<IFileMaintenanceService, FileMaintenanceService>();
        services.AddSingleton<IQualityTrendStore, FileQualityTrendStore>();
        services.AddSingleton<IDataQualityService, DataQualityService>();
        services.AddSingleton<IStorageSearchService, StorageSearchService>();
        services.AddSingleton<ITierMigrationService, TierMigrationService>();
        services.AddSingleton<IAuditChainService, AuditChainService>();
        services.AddSingleton<StorageChecksumService>(sp => new StorageChecksumService(null, sp.GetRequiredService<IAuditChainService>()));
        services.TryAddSingleton<SymbolRegistryService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new SymbolRegistryService(storageOptions.RootPath);
        });
        services.TryAddSingleton<ISymbolRegistryService>(sp => sp.GetRequiredService<SymbolRegistryService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SymbolRegistryInitializationHostedService>());

        // Position snapshot store — files land under {StorageRoot}/portfolios/ so the
        // LifecyclePolicyEngine governs retention automatically (ADR-002 / ADR-007).
        services.AddSingleton<IPositionSnapshotStore, JsonlPositionSnapshotStore>();

        if (LedgerStartup.IsConfigured())
        {
            services.AddSingleton(ledgerOptions);
            services.AddSingleton<PostgresLedgerJournalStore>();
            services.AddSingleton<ILedgerJournalStore>(sp => sp.GetRequiredService<PostgresLedgerJournalStore>());
            services.AddSingleton<ITransactionalLedgerJournalStore>(sp => sp.GetRequiredService<PostgresLedgerJournalStore>());
            services.AddSingleton<PostgresAccountingConfigurationStore>();
            services.AddSingleton<IAccountingConfigurationStore>(sp => sp.GetRequiredService<PostgresAccountingConfigurationStore>());
            services.AddSingleton<IAccountingActionAuditStore>(sp => sp.GetRequiredService<PostgresAccountingConfigurationStore>());
            services.AddSingleton<PostgresFundProfileTenancyRegistry>();
            services.AddSingleton<IFundProfileTenancyRegistry>(sp => sp.GetRequiredService<PostgresFundProfileTenancyRegistry>());
            services.AddSingleton<LedgerMigrationRunner>();
            services.TryAddSingleton<IOperationsStatusDerivationService, OperationsStatusDerivationService>();
            services.AddSingleton<PostgresOperationsContinuityStore>();
            services.AddSingleton<IOperationsContinuityRepository>(sp => sp.GetRequiredService<PostgresOperationsContinuityStore>());
            services.AddSingleton<IOperationsWorkflowAuditStore>(sp => sp.GetRequiredService<PostgresOperationsContinuityStore>());
            services.AddSingleton<IOperationsContinuityTransactionalCommitStore>(sp => sp.GetRequiredService<PostgresOperationsContinuityStore>());
            services.AddSingleton<ILedgerBookService>(sp =>
                new PostgresLedgerBookService(
                    sp.GetRequiredService<ILedgerJournalStore>(),
                    sp.GetService<IOperatorInboxService>()));
        }

        // Analysis export service for data export operations
        services.AddSingleton<AnalysisExportService>(sp =>
        {
            var storageOptions = sp.GetRequiredService<StorageOptions>();
            return new AnalysisExportService(storageOptions.RootPath);
        });

        services.AddSingleton<RateLimiter>(sp => new RateLimiter(5, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(0.5)));
        services.TryAddSingleton<IEdgarReferenceDataStore, FileEdgarReferenceDataStore>();
        services.TryAddSingleton<IEdgarReferenceDataProvider, EdgarReferenceDataProvider>();
        services.TryAddSingleton<IEdgarIngestOrchestrator, EdgarIngestOrchestrator>();

        if (SecurityMasterStartup.IsConfigured())
        {
            services.AddSingleton(securityMasterOptions);
            services.AddSingleton<IValidateOptions<SecurityMasterOptions>, SecurityMasterOptionsValidator>();
            services.AddSingleton<ISecurityMasterEventStore, PostgresSecurityMasterEventStore>();
            services.AddSingleton<ISecurityMasterSnapshotStore, PostgresSecurityMasterSnapshotStore>();
            services.AddSingleton<ISecurityMasterStore, PostgresSecurityMasterStore>();
            services.AddSingleton<IBondReferenceProjectionStore, PostgresBondReferenceProjectionStore>();
            services.AddSingleton<IOptionReferenceProjectionStore, PostgresOptionReferenceProjectionStore>();
            services.AddSingleton<IEquityReferenceProjectionStore, PostgresEquityReferenceProjectionStore>();
            services.AddSingleton<IFutureReferenceProjectionStore, PostgresFutureReferenceProjectionStore>();
            services.AddSingleton<IFxSpotReferenceProjectionStore, PostgresFxSpotReferenceProjectionStore>();
            services.AddSingleton<ISwapReferenceProjectionStore, PostgresSwapReferenceProjectionStore>();
            services.AddSingleton<ICommodityReferenceProjectionStore, PostgresCommodityReferenceProjectionStore>();
            services.AddSingleton<ICryptoReferenceProjectionStore, PostgresCryptoReferenceProjectionStore>();
            services.AddSingleton<IDepositReferenceProjectionStore, PostgresDepositReferenceProjectionStore>();
            services.AddSingleton<IMoneyMarketFundReferenceProjectionStore, PostgresMoneyMarketFundReferenceProjectionStore>();
            services.AddSingleton<ICertificateOfDepositReferenceProjectionStore, PostgresCertificateOfDepositReferenceProjectionStore>();
            services.AddSingleton<IOperatorOverridesStore, PostgresOperatorOverridesStore>();
            services.AddSingleton<SecurityMasterMigrationRunner>();
            services.AddSingleton<SecurityMasterAggregateRebuilder>();
            services.AddSingleton<SecurityMasterProjectionCache>();
            services.AddSingleton<SecurityMasterProjectionService>();
            services.AddSingleton<SecurityMasterRebuildOrchestrator>();
            services.AddSingleton<IUflProjectionRebuilder, UflProjectionRebuilder>();
            services.AddSingleton<ISecurityMasterService, SecurityMasterService>();
            services.AddSingleton<ISecurityMasterAmender>(sp => (ISecurityMasterAmender)sp.GetRequiredService<ISecurityMasterService>());
            services.AddSingleton<SecurityMasterQueryService>();
            services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<ISecurityMasterCorporateActionCommandService, SecurityMasterCorporateActionCommandService>();
            services.AddSingleton<ISecurityValidationService, SecurityValidationService>();
            services.AddSingleton<ICorporateActionCommandService, CorporateActionCommandService>();
            // Period-aware supersede routing: singleton over IServiceScopeFactory because the
            // workstation restatement stack it consumes is scoped; hosts without that stack
            // degrade to no proposal at call time.
            services.AddSingleton<ICorporateActionRestatementTrigger, CorporateActionSupersedeRestatementTrigger>();
            services.AddSingleton<IBondReferenceService, BondProjectionService>();
            services.AddSingleton<IOptionReferenceService, OptionProjectionService>();
            services.AddSingleton<IOptionChainImportService>(sp => (OptionProjectionService)sp.GetRequiredService<IOptionReferenceService>());
            services.AddSingleton<IEquityReferenceService, EquityProjectionService>();
            services.AddSingleton<IFutureReferenceService, FutureProjectionService>();
            services.AddSingleton<IFxSpotReferenceService, FxSpotProjectionService>();
            services.AddSingleton<ISwapReferenceService, SwapProjectionService>();
            services.AddSingleton<ICommodityReferenceService, CommodityProjectionService>();
            services.AddSingleton<ICryptoReferenceService, CryptoProjectionService>();
            services.AddSingleton<IDepositReferenceService, DepositProjectionService>();
            services.AddSingleton<IMoneyMarketFundReferenceService, MoneyMarketFundProjectionService>();
            services.AddSingleton<ICertificateOfDepositReferenceService, CertificateOfDepositProjectionService>();
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

            // Migrate-on-read upcaster for asset-specific-terms payloads, shared by the projection
            // store (queryable schema_version column + read normalization).
            services.AddSingleton<
                Meridian.Contracts.Schema.ISchemaUpcaster<Meridian.Contracts.SecurityMaster.SecurityAssetSpecificTerms>,
                Meridian.Contracts.SecurityMaster.SecurityAssetSpecificTermsV0ToCurrentUpcaster>();

            // Durable audit/versioning spine: the golden-record conflict store and the governed
            // revision-lifecycle store are Postgres-backed so resolutions and approval state survive
            // process recycles and are consistent across instances.
            services.AddSingleton<ISecurityMasterConflictService, PostgresSecurityMasterConflictService>();
            services.AddSingleton<ISecurityMasterRevisionStore, PostgresSecurityMasterRevisionStore>();

            // Clearwater-model extensions: pricing hierarchy, cash flow, entitlements, data quality
            services.AddSingleton<ISecurityMasterPricingStore, PostgresSecurityMasterPricingStore>();
            services.AddSingleton<ISecurityMasterCashFlowStore, PostgresSecurityMasterCashFlowStore>();
            services.AddSingleton<IDataVendorEntitlementStore, PostgresDataVendorEntitlementStore>();
            services.AddSingleton<ISecurityMasterQualityReportStore, PostgresSecurityMasterQualityReportStore>();
            services.AddSingleton<ISecurityMasterPricingService, SecurityMasterPricingService>();
            services.AddSingleton<IStructuredCashFlowLedgerBridge, StructuredCashFlowLedgerBridge>();
            services.AddSingleton<ISecurityMasterCashFlowService, SecurityMasterCashFlowService>();
            services.AddSingleton<IDataVendorEntitlementService, DataVendorEntitlementService>();
            services.AddSingleton<ISecurityMasterDataQualityService, SecurityMasterDataQualityService>();

            // Coverage sweep: symbols active in platform surfaces but missing from the master
            // feed RC001 RefreshControl violations, which the exception-casework loop cases.
            // Draft proposals let the operator master a flagged gap from a pre-filled record.
            services.AddSingleton<ISecurityCoverageSymbolSource, ConfiguredSymbolCoverageSource>();
            services.AddSingleton<ISecurityCoverageSymbolSource>(sp =>
                new CanonicalRegistryCoverageSource(sp.GetService<Meridian.Contracts.Catalog.ICanonicalSymbolRegistry>()));
            services.AddSingleton<SecurityMasterDraftProposalService>(sp =>
                new SecurityMasterDraftProposalService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SecurityMasterDraftProposalService>>(),
                    sp.GetService<Meridian.Infrastructure.Adapters.Core.SymbolResolution.ISymbolResolver>()));

            // Symbology lineage: ticker changes recorded as first-class amend events, and
            // era-correct per-chunk symbol resolution for rename-spanning backfills.
            services.AddSingleton<SecurityMasterTickerChangeService>();
            services.AddSingleton<Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver, SecurityMasterHistoricalSymbolTimelineResolver>();

            // Corporate-action ingest: fan-out, consensus scoring, staged apply, and the
            // inbox snapshot the workbench polls for staged proposals.
            services.AddSingleton<CorporateActionIngestOrchestrator>();
            services.AddSingleton<CorporateActionInboxState>();
        }

        if (AssetOperationsStartup.IsConfigured())
        {
            services.AddSingleton(assetOperationsOptions);
            services.AddSingleton<AssetOperationsMigrationRunner>();
            services.AddSingleton<IAssetOperationsProjectionStore, PostgresAssetOperationsProjectionStore>();
        }

        // Register null/stub implementations as fallbacks when Security Master is not configured.
        // These ensure that ASP.NET Core Minimal API routing initialises correctly (unregistered
        // service parameters cause startup crashes) while returning sensible empty / error responses.
        services.TryAddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService, NullSecurityMasterQueryService>();
        services.TryAddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService, NullSecurityMasterQueryService>();
        services.TryAddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterService, NullSecurityMasterService>();
        services.TryAddSingleton<ISecurityMasterCorporateActionCommandService, NullSecurityMasterCorporateActionCommandService>();
        services.TryAddSingleton<IBondReferenceService, NullBondReferenceService>();
        services.TryAddSingleton<IOptionReferenceService, NullOptionReferenceService>();
        services.TryAddSingleton<IOptionChainImportService, NullOptionChainImportService>();
        services.TryAddSingleton<IEquityReferenceService, NullEquityReferenceService>();
        services.TryAddSingleton<IFutureReferenceService, NullFutureReferenceService>();
        services.TryAddSingleton<IFxSpotReferenceService, NullFxSpotReferenceService>();
        services.TryAddSingleton<ISwapReferenceService, NullSwapReferenceService>();
        services.TryAddSingleton<ICommodityReferenceService, NullCommodityReferenceService>();
        services.TryAddSingleton<ICryptoReferenceService, NullCryptoReferenceService>();
        services.TryAddSingleton<IDepositReferenceService, NullDepositReferenceService>();
        services.TryAddSingleton<IMoneyMarketFundReferenceService, NullMoneyMarketFundReferenceService>();
        services.TryAddSingleton<ICertificateOfDepositReferenceService, NullCertificateOfDepositReferenceService>();
        services.TryAddSingleton<ISecurityMasterAmender, NullSecurityMasterService>();
        services.TryAddSingleton<ISecurityMasterConflictService, NullSecurityMasterConflictService>();
        services.TryAddSingleton<ISecurityMasterImportService, NullSecurityMasterImportService>();
        services.TryAddSingleton<ISecurityMasterIngestStatusService>(sp => (ISecurityMasterIngestStatusService)sp.GetRequiredService<ISecurityMasterImportService>());
        services.TryAddSingleton<ISecurityValidationService, NullSecurityValidationService>();
        services.TryAddSingleton<ICorporateActionCommandService, NullCorporateActionCommandService>();
        services.TryAddSingleton<ICorporateActionRestatementTrigger, NullCorporateActionRestatementTrigger>();
        services.TryAddSingleton<ISecurityMasterEventStore, NullSecurityMasterEventStore>();
        services.TryAddSingleton<IOperatorOverridesStore, NullOperatorOverridesStore>();
        services.TryAddSingleton<ISecurityMasterPricingService, NullSecurityMasterPricingService>();
        services.TryAddSingleton<ISecurityMasterCashFlowService, NullSecurityMasterCashFlowService>();
        services.TryAddSingleton<IDataVendorEntitlementService, NullDataVendorEntitlementService>();
        services.TryAddSingleton<ISecurityMasterDataQualityService, NullSecurityMasterDataQualityService>();
        services.TryAddSingleton<IUflProjectionRebuilder, NullUflProjectionRebuilder>();
        // Passport Workbench conflict-authority policy is storage-independent (pure precedence logic).
        services.TryAddSingleton<ISecurityMasterConflictAuthorityPolicy, SecurityMasterConflictAuthorityPolicy>();
        services.TryAddSingleton<IAssetOperationsProjectionStore, InMemoryAssetOperationsProjectionStore>();
        services.TryAddSingleton<AssetObligationProjectionService>();
        services.TryAddSingleton<IAssetOperationsCommandService, AssetOperationsProjectionCommandService>();
        services.TryAddSingleton<IAssetOperationsQueryService, AssetOperationsReadService>();

        if (DirectLendingStartup.IsConfigured())
        {
            services.AddSingleton(directLendingOptions);
            services.AddSingleton<DirectLendingEventRebuilder>();
            services.AddSingleton<IDirectLendingStateStore>(sp =>
                new PostgresDirectLendingStateStore(
                    directLendingOptions,
                    sp.GetService<ILedgerJournalStore>()));
            services.AddSingleton<IDirectLendingOperationsStore>(sp => (PostgresDirectLendingStateStore)sp.GetRequiredService<IDirectLendingStateStore>());
            services.AddSingleton<DirectLendingMigrationRunner>();
            services.AddSingleton<IDirectLendingQueryService, PostgresDirectLendingQueryService>();
            services.AddSingleton<LoanAccountingProjector>();
            services.AddSingleton<IAccrualLedgerService, AccrualLedgerService>();
            services.AddSingleton<IDirectLendingCommandService, PostgresDirectLendingCommandService>();
            services.AddSingleton<IDirectLendingService, PostgresDirectLendingService>();
            services.AddHostedService<DirectLendingOutboxDispatcher>();
            services.AddHostedService<DailyAccrualWorker>();
        }

        var useInMemoryGovernanceServices = IsInMemoryGovernanceProfileEnabled();
        EnsureGovernancePersistenceProfile(useInMemoryGovernanceServices);

        // Fund accounts and governance structure.
        if (FundAccountsStartup.IsConfigured())
        {
            FundAccountsStartup.EnsureEnvironmentDefaults();
            var faConnectionString = Environment.GetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable)!;
            var faSchema = Environment.GetEnvironmentVariable(FundAccountsStartup.SchemaVariable) ?? FundAccountsStartup.DefaultSchema;
            services.TryAddSingleton(new FundAccountStoreOptions { ConnectionString = faConnectionString, Schema = faSchema });
            services.TryAddSingleton<IFundAccountStore, PostgresFundAccountStore>();
            services.TryAddSingleton<PostgresFundAccountService>();
            services.TryAddSingleton<IFundAccountService>(sp => sp.GetRequiredService<PostgresFundAccountService>());
            services.TryAddSingleton<IAccountManagementService>(sp => sp.GetRequiredService<PostgresFundAccountService>());
            services.TryAddSingleton<IAccountQueryService>(sp => sp.GetRequiredService<PostgresFundAccountService>());
        }
        else
        {
            services.TryAddSingleton<IFundAccountService>(sp =>
            {
                var storageOptions = sp.GetRequiredService<StorageOptions>();
                var persistencePath = Path.Combine(storageOptions.RootPath, "governance", "fund-accounts.json");
                return new InMemoryFundAccountService(persistencePath);
            });
            services.TryAddSingleton<IAccountManagementService>(sp => (IAccountManagementService)sp.GetRequiredService<IFundAccountService>());
            services.TryAddSingleton<IAccountQueryService>(sp => (IAccountQueryService)sp.GetRequiredService<IFundAccountService>());
        }
        services.TryAddSingleton<IGovernanceSharedDataAccessService>(sp =>
            new GovernanceSharedDataAccessService(
                sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                sp.GetService<HistoricalDataQueryService>(),
                sp.GetService<BackfillCoordinator>()));
        services.TryAddSingleton<IFundStructurePolicyService, FundStructurePolicyService>();
        if (FundStructureStartup.IsConfigured())
        {
            FundStructureStartup.EnsureEnvironmentDefaults();
            services.TryAddSingleton(new FundStructureStoreOptions
            {
                ConnectionString = Environment.GetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable)!,
                Schema = Environment.GetEnvironmentVariable(FundStructureStartup.SchemaVariable) ?? FundStructureStartup.DefaultSchema
            });
            services.TryAddSingleton<IFundStructureStore, PostgresFundStructureStore>();
            services.TryAddSingleton<PostgresFundStructureService>();
            services.TryAddSingleton<IFundStructureService>(sp => sp.GetRequiredService<PostgresFundStructureService>());
        }
        else
        {
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
        }
        // ── Banking ──────────────────────────────────────────────────────────
        if (BankingStartup.IsConfigured())
        {
            BankingStartup.EnsureEnvironmentDefaults();
            services.TryAddSingleton(new BankingStoreOptions
            {
                ConnectionString = Environment.GetEnvironmentVariable(BankingStartup.ConnectionStringVariable)!,
                Schema = Environment.GetEnvironmentVariable(BankingStartup.SchemaVariable) ?? BankingStartup.DefaultSchema
            });
            services.TryAddSingleton<IBankingStore, PostgresBankingStore>();
            services.TryAddSingleton<PostgresBankingService>();
            services.TryAddSingleton<IBankingService>(sp => sp.GetRequiredService<PostgresBankingService>());
            services.TryAddSingleton<Meridian.Contracts.Banking.IBankTransactionSource>(sp => sp.GetRequiredService<PostgresBankingService>());
        }
        else
        {
            services.TryAddSingleton<InMemoryBankingService>();
            services.TryAddSingleton<IBankingService>(sp => sp.GetRequiredService<InMemoryBankingService>());
            services.TryAddSingleton<Meridian.Contracts.Banking.IBankTransactionSource>(sp => sp.GetRequiredService<InMemoryBankingService>());
        }
        // ── Money Market Fund ─────────────────────────────────────────────────
        if (MoneyMarketStartup.IsConfigured())
        {
            MoneyMarketStartup.EnsureEnvironmentDefaults();
            services.TryAddSingleton(new MoneyMarketStoreOptions
            {
                ConnectionString = Environment.GetEnvironmentVariable(MoneyMarketStartup.ConnectionStringVariable)!,
                Schema = Environment.GetEnvironmentVariable(MoneyMarketStartup.SchemaVariable) ?? MoneyMarketStartup.DefaultSchema
            });
            services.TryAddSingleton<IMoneyMarketFundAuxStore, PostgresMoneyMarketFundStore>();
            services.TryAddSingleton<PostgresMoneyMarketFundService>();
            services.TryAddSingleton<IMoneyMarketFundService>(sp => sp.GetRequiredService<PostgresMoneyMarketFundService>());
            services.TryAddSingleton<IMmfLiquidityService>(sp => sp.GetRequiredService<PostgresMoneyMarketFundService>());
        }
        else
        {
            services.TryAddSingleton<InMemoryMoneyMarketFundService>();
            services.TryAddSingleton<IMoneyMarketFundService>(sp => sp.GetRequiredService<InMemoryMoneyMarketFundService>());
            services.TryAddSingleton<IMmfLiquidityService>(sp => sp.GetRequiredService<InMemoryMoneyMarketFundService>());
        }
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

        services.TryAddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        services.TryAddSingleton<IBankingService, InMemoryBankingService>();
        services.TryAddSingleton<IMoneyMarketFundService, InMemoryMoneyMarketFundService>();
        services.TryAddSingleton<IMmfLiquidityService>(sp => (IMmfLiquidityService)sp.GetRequiredService<IMoneyMarketFundService>());

        services.TryAddSingleton<FundOperationsPersistenceOptions>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var persistenceConfig = config.FundOperationsPersistence;
            if (persistenceConfig?.DomainModes is null or { Count: 0 })
                return new FundOperationsPersistenceOptions();

            var domainModes = new Dictionary<FundOperationsDomain, DomainCutoverMode>();
            foreach (var (key, value) in persistenceConfig.DomainModes)
            {
                if (Enum.TryParse<FundOperationsDomain>(key, ignoreCase: true, out var domain))
                {
                    var readMode = Enum.TryParse<DomainReadMode>(value.ReadMode, ignoreCase: true, out var rm)
                        ? rm
                        : DomainReadMode.LegacyInMemory;
                    domainModes[domain] = new DomainCutoverMode(value.ShadowWritesEnabled, readMode);
                }
            }

            return new FundOperationsPersistenceOptions { DomainModes = domainModes };
        });
        services.AddSingleton<IDomainProjectionReconciliationJob>(
            _ => new NoOpDomainProjectionReconciliationJob(FundOperationsDomain.FundStructure));
        services.AddSingleton<IDomainProjectionReconciliationJob>(
            _ => new NoOpDomainProjectionReconciliationJob(FundOperationsDomain.FundAccounts));
        services.AddSingleton<IDomainProjectionReconciliationJob>(
            _ => new NoOpDomainProjectionReconciliationJob(FundOperationsDomain.DirectLending));
        services.AddSingleton<IDomainProjectionReconciliationJob>(
            _ => new NoOpDomainProjectionReconciliationJob(FundOperationsDomain.Banking));
        services.AddSingleton<IDomainProjectionReconciliationJob>(
            _ => new NoOpDomainProjectionReconciliationJob(FundOperationsDomain.MoneyMarket));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ProjectionReconciliationHostedService>());
        return services;
    }

    private static void EnsureGovernancePersistenceProfile(bool useInMemoryGovernanceServices)
    {
        if (useInMemoryGovernanceServices)
            return;

        var missing = new List<string>();
        if (!FundAccountsStartup.IsConfigured())
            missing.Add(FundAccountsStartup.ConnectionStringVariable);
        if (!FundStructureStartup.IsConfigured())
            missing.Add(FundStructureStartup.ConnectionStringVariable);

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            "Production-safe startup requires persistence-backed governance domain services. " +
            $"Configure {string.Join(", ", missing)} or set MERIDIAN_USE_INMEMORY_GOVERNANCE=true only for local/dev fixture scenarios.");
    }

    private static bool IsInMemoryGovernanceProfileEnabled()
    {
        var explicitOptIn = ParseBool("MERIDIAN_USE_INMEMORY_GOVERNANCE", false);
        if (!explicitOptIn)
        {
            return false;
        }

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "In-memory governance services are forbidden in Production. " +
                "Unset MERIDIAN_USE_INMEMORY_GOVERNANCE or run in a non-production environment.");
        }

        return true;
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
            ConnectionString = DirectLendingStartup.GetEffectiveConnectionString(),
            Schema = DirectLendingStartup.GetEffectiveSchema(),
            SnapshotIntervalVersions = ParseInt("MERIDIAN_DIRECT_LENDING_SNAPSHOT_INTERVAL", 50),
            CurrentEventSchemaVersion = ParseInt("MERIDIAN_DIRECT_LENDING_EVENT_SCHEMA_VERSION", 1),
            ProjectionEngineVersion = Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_PROJECTION_ENGINE_VERSION") ?? "dl-engine-v1",
            OutboxBatchSize = ParseInt("MERIDIAN_DIRECT_LENDING_OUTBOX_BATCH_SIZE", 50),
            OutboxPollIntervalSeconds = ParseInt("MERIDIAN_DIRECT_LENDING_OUTBOX_POLL_SECONDS", 5),
            ReplayBatchSize = ParseInt("MERIDIAN_DIRECT_LENDING_REPLAY_BATCH_SIZE", 250),
            RequireSecurityMasterReferenceForDurableWrites = ParseBool("MERIDIAN_DIRECT_LENDING_REQUIRE_SECURITY_MASTER_REFERENCE", false)
        };

    private static AssetOperationsOptions CreateAssetOperationsOptions()
        => new()
        {
            ConnectionString = Environment.GetEnvironmentVariable(AssetOperationsStartup.ConnectionStringVariable) ?? string.Empty,
            Schema = Environment.GetEnvironmentVariable(AssetOperationsStartup.SchemaVariable) ?? AssetOperationsStartup.DefaultSchema
        };

    private static LedgerJournalStoreOptions CreateLedgerOptions()
        => new()
        {
            ConnectionString = Environment.GetEnvironmentVariable(LedgerStartup.ConnectionStringVariable) ?? string.Empty,
            SchemaName = Environment.GetEnvironmentVariable(LedgerStartup.SchemaVariable) ?? LedgerStartup.DefaultSchema,
            EnablePeriodLocking = ParseBool("MERIDIAN_LEDGER_ENABLE_PERIOD_LOCKING", true)
        };

    private static bool IsScopedAccessPostgresConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING"));

    private static int ParseInt(string name, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;

    private static bool ParseBool(string name, bool defaultValue)
        => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;
    private sealed class ScopedAccessAssignmentStoreMigrationHostedService : IHostedService
    {
        private readonly PostgresScopedAccessAssignmentStore _store;

        public ScopedAccessAssignmentStoreMigrationHostedService(PostgresScopedAccessAssignmentStore store)
            => _store = store;

        public Task StartAsync(CancellationToken cancellationToken)
            => _store.EnsureMigratedAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StorageCatalogInitializationHostedService : IHostedService
    {
        private readonly StorageCatalogService _catalog;

        public StorageCatalogInitializationHostedService(StorageCatalogService catalog)
            => _catalog = catalog;

        public Task StartAsync(CancellationToken cancellationToken)
            => _catalog.InitializeAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class SymbolRegistryInitializationHostedService : IHostedService
    {
        private readonly SymbolRegistryService _registry;

        public SymbolRegistryInitializationHostedService(SymbolRegistryService registry)
            => _registry = registry;

        public Task StartAsync(CancellationToken cancellationToken)
            => _registry.InitializeAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
