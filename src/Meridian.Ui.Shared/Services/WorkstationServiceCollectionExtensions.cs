using Meridian.Application.Accounting;
using Meridian.Application.Composition;
using Meridian.Application.Config.Credentials;
using Meridian.Application.DirectLending;
using Meridian.Application.FundStructure;
using Meridian.Application.Reconciliation;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Audit.Compliance;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Plaid;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Core.Contracts;
using Meridian.DataIntegration.AccountingSystem.Fixtures;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.DataIntegration.Credentials;
using Meridian.Documents;
using Meridian.Domain.Collectors;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Identity;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.Plaid;
using Meridian.Instruments.AssetOperations;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Reporting;
using Meridian.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.Operations;
using Meridian.Storage.Reporting;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Extensibility;
using Meridian.Ui.Shared.Services.Acceptance;
using Meridian.Ui.Shared.Services.CoveredCall;
using Meridian.Ui.Shared.Streaming;
using Meridian.Ui.Shared.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Registers the shared web-workstation service graph consumed by endpoint and host surfaces.
/// </summary>
public static class WorkstationServiceCollectionExtensions
{
    public static IServiceCollection AddWorkstationSharedServices(this IServiceCollection services)
    {
        // Unified persistence config must resolve before the reporting/scoped-access
        // registrations below read the per-domain connection-string variables.
        Meridian.Storage.MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();
        services.TryAddSingleton<ProviderDataReadModelService>();
        // Persisted evidence does not enable live IB access in a guidance/non-vendor build.
        services.TryAddSingleton<IBDurableResultStore>(sp =>
        {
            var root = sp.GetRequiredService<StorageOptions>().RootPath;
            return new JsonIBDurableResultStore(Path.Combine(root, "providers", "interactive-brokers", "results.json"));
        });
        services.TryAddSingleton<IBResultQueryService>();

        var isProductionComposition = ProductionServiceRegistrationPolicy.IsProductionComposition(services);

        services.TryAddSingleton<ConfigStore>(sp =>
        {
            var core = sp.GetRequiredService<Meridian.Application.UI.ConfigStore>();
            return new ConfigStore(core.ConfigPath);
        });

        // The UI coordinator is a read-model façade over the core coordinator. Reuse the
        // composition-root core singleton when it is registered so UI endpoints and non-UI
        // callers (execution gateway, governance summaries) share one run gate; otherwise
        // build a façade-owned core from the shared UI config store.
        services.AddSingleton<BackfillCoordinator>(sp =>
        {
            var core = sp.GetService<Meridian.Application.Backfill.BackfillCoordinator>();
            if (core is not null)
            {
                return new BackfillCoordinator(core);
            }

            var configStore = sp.GetRequiredService<ConfigStore>();
            var registry = sp.GetService<ProviderRegistry>();
            var factory = sp.GetService<ProviderFactory>();
            return new BackfillCoordinator(configStore, registry, factory);
        });

        services.AddHttpClient();
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        // Options consumed by workstation endpoints must bind in the shared registration so
        // every host of these endpoints (UiServer and AddUiSharedServices alike) honors
        // operator configuration rather than compiled defaults.
        services.AddOptions<Meridian.Storage.Services.DataReplacementCostOptions>()
            .BindConfiguration(Meridian.Storage.Services.DataReplacementCostOptions.SectionName);
        services.AddOptions<Meridian.Storage.Query.DataQueryOptions>()
            .BindConfiguration(Meridian.Storage.Query.DataQueryOptions.SectionName);
        services.TryAddScoped<IWorkstationTenantContextAccessor, HttpContextWorkstationTenantContextAccessor>();
        // SEC-005 slice 4c-ii: ambient caller-tenant accessor consumed by the singleton Postgres ledger
        // store for tenant read predicates. Singleton + IHttpContextAccessor-backed (no captive scope).
        services.TryAddSingleton<IFundScopeTenantAccessor, WorkstationFundScopeTenantAccessor>();
        // SEC-005 slice 4c-iii: fund-scoped write tenant gate switch. Off by default (detection-first) so
        // the tenantless legacy admin still writes; a shared multi-tenant deployment opts into fail-closed
        // enforcement via MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED=true.
        services.TryAddSingleton(new FundScopedWriteTenantOptions(
            Enforce: string.Equals(
                Environment.GetEnvironmentVariable("MERIDIAN_FUND_SCOPED_WRITE_TENANT_REQUIRED"),
                "true",
                StringComparison.OrdinalIgnoreCase)));
        services.TryAddSingleton<IRolePermissionProfileStore, FileRolePermissionProfileStore>();
        services.TryAddSingleton<IUserAccountStore, FileUserAccountStore>();
        services.TryAddSingleton<IAccessRoleAssignmentStore, UserAccountAccessRoleAssignmentStore>();
        services.TryAddSingleton<IComplianceApprovalStore, FileComplianceApprovalStore>();
        services.TryAddSingleton<IComplianceApprovalResolver>(sp =>
            sp.GetRequiredService<IComplianceApprovalStore>());
        services.TryAddSingleton<ICompliancePolicyEngine, CompliancePolicyEngine>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING")))
        {
            var hasProcessWideScopedAccessMigration = services.Any(
                static descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    string.Equals(
                        descriptor.ImplementationType?.Name,
                        "ScopedAccessAssignmentStoreMigrationHostedService",
                        StringComparison.Ordinal));
            services.TryAddSingleton(new ScopedAccessStoreOptions
            {
                ConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING")!,
                Schema = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA") ?? "identity_access"
            });
            services.TryAddSingleton<PostgresScopedAccessAssignmentStore>(sp =>
                new PostgresScopedAccessAssignmentStore(
                    sp.GetRequiredService<ScopedAccessStoreOptions>()));
            services.TryAddSingleton<IScopedAccessAssignmentStore>(sp =>
                sp.GetRequiredService<PostgresScopedAccessAssignmentStore>());
            if (!hasProcessWideScopedAccessMigration)
            {
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<
                        IHostedService,
                        WorkstationScopedAccessMigrationHostedService>());
            }
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
        services.TryAddSingleton<ScopedAccessService>();
        services.TryAddSingleton<IScopedAccessAssignmentService>(sp => sp.GetRequiredService<ScopedAccessService>());
        services.TryAddSingleton<IScopedAuthorizationService>(sp => sp.GetRequiredService<ScopedAccessService>());
        services.TryAddSingleton<UserProfileRegistry>();
        services.TryAddSingleton<LoginSessionService>();
        services.TryAddSingleton<InitialAccountBootstrapService>();
        services.TryAddSingleton<DemoTenantProvisioner>();
        services.TryAddSingleton<FirstRunExperienceService>();
        services.TryAddSingleton<DesktopWorkstationLaunchService>();
        services.TryAddSingleton<DesktopLaunchTicketService>();
        if (!isProductionComposition)
        {
            services.TryAddSingleton<IOperatorInboxService, InMemoryOperatorInboxService>();
            services.TryAddSingleton<ImmutableAuditLogService>();
        }
        services.TryAddSingleton<FeatureCapabilitySettingsService>();
        services.TryAddSingleton<IngestionOperationsService>();
        services.TryAddSingleton<StorageAssuranceService>();
        services.TryAddSingleton<SensitiveActionPolicyEngine>();
        services.TryAddSingleton<AccessReviewService>();
        services.TryAddSingleton<IFundAccountTraversalQueryService, FundAccountTraversalQueryService>();
        services.TryAddSingleton<FundStructureSetupWorkflowService>();

        services.TryAddSingleton<IOperationalCaseHistoryStore>(sp =>
            new FileOperationalCaseHistoryStore(ResolveConfigDataRoot(sp)));
        services.TryAddSingleton<IStrategyRepository>(sp =>
            new StrategyRunStore(sp.GetRequiredService<IOperationalCaseHistoryStore>()));
        services.TryAddSingleton<PromotionRecordStoreOptions>(sp =>
            new PromotionRecordStoreOptions(Path.Combine(ResolveConfigDataRoot(sp), "strategies", "promotions")));
        services.TryAddSingleton<IPromotionRecordStore>(sp =>
            new JsonlPromotionRecordStore(
                sp.GetRequiredService<PromotionRecordStoreOptions>(),
                sp.GetRequiredService<ILogger<JsonlPromotionRecordStore>>()));
        services.TryAddSingleton<StrategyDesignStoreOptions>(sp =>
            new StrategyDesignStoreOptions(Path.Combine(ResolveConfigDataRoot(sp), "strategies", "designer")));
        services.TryAddSingleton<IStrategyDesignRepository>(sp =>
            new JsonlStrategyDesignRepository(
                sp.GetRequiredService<StrategyDesignStoreOptions>(),
                sp.GetRequiredService<ILogger<JsonlStrategyDesignRepository>>()));
        services.TryAddSingleton<StrategyDesignService>();
        services.TryAddSingleton<StrategyEngineRegistry>();
        services.TryAddSingleton<StrategyEngineValidationService>();
        services.TryAddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        services.TryAddSingleton<PortfolioReadService>();
        if (!services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IAssetOperationsProjectionStore)))
        {
            services.TryAddSingleton<InMemoryAssetOperationsProjectionStore>();
            services.TryAddSingleton<IAssetOperationsProjectionStore>(sp =>
                sp.GetRequiredService<InMemoryAssetOperationsProjectionStore>());
        }
        services.TryAddSingleton<IInstrumentPositionProjectionStore>(sp =>
            sp.GetService<IAssetOperationsProjectionStore>() as IInstrumentPositionProjectionStore
            ?? throw new InvalidOperationException(
                "The configured Asset Operations projection store must also implement " +
                $"{nameof(IInstrumentPositionProjectionStore)}."));
        services.TryAddSingleton<IAssetAccountingEventProjectionStore>(sp =>
            sp.GetService<IAssetOperationsProjectionStore>() as IAssetAccountingEventProjectionStore
            ?? throw new InvalidOperationException(
                "The configured Asset Operations projection store must also implement " +
                $"{nameof(IAssetAccountingEventProjectionStore)}."));
        services.TryAddSingleton<IAssetOperationsCommandService, AssetOperationsProjectionCommandService>();
        services.TryAddSingleton<IAssetOperationsQueryService, AssetOperationsReadService>();
        services.TryAddSingleton<IFactorPaydownProjectionService, FactorPaydownProjectionService>();
        services.TryAddSingleton<IPortfolioCashBalanceProvider, PortfolioLedgerCashBalanceProvider>();
        services.TryAddSingleton<IPortfolioCashLadderQueryService, PortfolioCashLadderReadService>();
        services.TryAddSingleton<ISecurityMasterOperationalReadinessService, SecurityMasterOperationalReadinessService>();
        services.TryAddSingleton<IMultiAssetCoverageReadService, MultiAssetCoverageReadService>();
        services.TryAddSingleton<LedgerReadService>();
        services.TryAddSingleton<StrategyRunReadService>();
        services.TryAddSingleton<StrategyRunComparisonService>();
        services.TryAddSingleton<AuditTrailExplorerService>();
        services.TryAddSingleton<IFinancialRecordExplorerSavedViewStore>(sp =>
            new FileFinancialRecordExplorerSavedViewStore(
                ResolveWorkstationDataDirectory(sp),
                sp.GetRequiredService<ILogger<FileFinancialRecordExplorerSavedViewStore>>()));
        services.TryAddSingleton<FinancialRecordExplorerReadService>();
        services.TryAddSingleton<IDirectLendingServicerStatementService, DirectLendingServicerStatementService>();
        services.TryAddSingleton<DirectLendingOperationsReadService>();
        services.TryAddSingleton<CashFlowProjectionService>();
        services.TryAddSingleton<StrategyRunContinuityService>();
        services.TryAddSingleton<IBacktestPreflightService, BacktestPreflightService>();

        services.TryAddSingleton(BrokerageConnectionOptions.RobinhoodFromEnvironment());
        services.TryAddSingleton<BrokerageConnectionService>();
        services.TryAddSingleton<AlpacaBrokerageConnectionService>();
        services.AddDefaultProviderSetupHandlers();
        services.TryAddSingleton<IProviderSetupRegistry, ProviderSetupRegistry>();
        services.TryAddSingleton<ProviderConnectionLifecycleService>();
        services.TryAddSingleton<ProviderReadinessService>();
        services.TryAddSingleton<IQuickBooksOnlineConnectionStore, QuickBooksOnlineProviderCredentialConnectionStore>();
        services.AddHttpClient<IQuickBooksOnlineClient, QuickBooksOnlineHttpClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, XeroFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, NetSuiteFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, QuickBooksOnlineAccountingProvider>());
        services.TryAddSingleton<AccountingSystemIntegrationService>();
        services.TryAddSingleton<IAccountingMigrationRunArtifactStore>(sp =>
            new FileAccountingMigrationRunArtifactStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "migration-run-artifacts.json"),
                sp.GetRequiredService<ILogger<FileAccountingMigrationRunArtifactStore>>()));
        services.TryAddSingleton<FileAccountingMigrationRunWorkerPlanStore>(sp =>
            new FileAccountingMigrationRunWorkerPlanStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "migration-run-worker-plans.json"),
                sp.GetRequiredService<ILogger<FileAccountingMigrationRunWorkerPlanStore>>()));
        services.TryAddSingleton<IAccountingMigrationRunWorkerPlanStore>(sp =>
            sp.GetRequiredService<FileAccountingMigrationRunWorkerPlanStore>());
        services.TryAddSingleton<IAccountingMigrationRunWorkerPlanWriter>(sp =>
            sp.GetRequiredService<FileAccountingMigrationRunWorkerPlanStore>());
        services.TryAddSingleton<AccountingMigrationRunExecutionService>();
        services.TryAddSingleton<IAccountingTenantAdministrationProfileStore>(sp =>
            new FileAccountingTenantAdministrationProfileStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "tenant-administration-profiles.json"),
                sp.GetRequiredService<ILogger<FileAccountingTenantAdministrationProfileStore>>()));
        services.TryAddSingleton<IAccountingProductionCertificationProfileStore>(sp =>
            new FileAccountingProductionCertificationProfileStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "production-certification-profiles.json"),
                sp.GetRequiredService<ILogger<FileAccountingProductionCertificationProfileStore>>()));
        services.TryAddSingleton<IAccountingProductionCertificationEvidenceAuthority,
            EvidenceVaultAccountingProductionCertificationEvidenceAuthority>();
        services.TryAddSingleton<AccountingProductionCertificationCommandService>();
        services.TryAddSingleton<AccountingProductionReadinessService>();
        services.TryAddSingleton(ResolvePlaidOptions);
        services.TryAddSingleton<IPlaidConnectionRepository>(sp =>
            new FilePlaidConnectionRepository(ResolveWorkstationDataDirectory(sp)));
        services.TryAddSingleton<IPlaidClient>(sp =>
            new PlaidHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlaidHttpClient))));
        services.TryAddSingleton<PlaidWorkstationService>();
        services.TryAddSingleton<IPlaidIngestionService>(sp => sp.GetRequiredService<PlaidWorkstationService>());
        services.TryAddSingleton<IPlaidTransferService>(sp => sp.GetRequiredService<PlaidWorkstationService>());
        services.TryAddSingleton(sp => new BankFeedTransportService(
            sp.GetRequiredService<IFundAccountService>(),
            sp.GetServices<IEtlSourceReader>(),
            sp.GetService<IPlaidIngestionService>()));
        services.TryAddSingleton(BrokeragePortfolioSyncOptions.Default);
        services.TryAddSingleton<BrokeragePortfolioSyncService>();
        services.TryAddSingleton<ProviderLedgerReconciliationService>();
        // Reconcile statement runs against Meridian's own retained account records (positions + cash)
        // instead of the fail-closed empty book. Replace (not TryAdd) so this wins over the
        // EmptyInternalReconciliationPopulationProvider that AddStatementReconciliationServices
        // registers via TryAddSingleton, regardless of composition order.
        services.Replace(ServiceDescriptor.Singleton<IInternalReconciliationPopulationProvider, RetainedInternalReconciliationPopulationProvider>());
        // Normalize cross-currency statement lines against their base-currency internal balance using an
        // operator-maintained FX rate table (reconciliation/fx-rates.json under the data root) instead of
        // the identity-only default that fails every genuine cross-currency line closed to a break. A
        // missing or empty table preserves that fail-closed behavior until rates are supplied.
        services.Replace(ServiceDescriptor.Singleton<IReconciliationFxRateProvider>(sp =>
            FileReconciliationFxRateProvider.Load(
                sp.GetRequiredService<StorageOptions>().RootPath,
                sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(FileReconciliationFxRateProvider).FullName!))));
        // Resolve the run's selected tolerance profile from the operator-maintained profile table
        // (reconciliation/tolerance-profiles.json) instead of only the built-in default, so a run
        // configured with a registered non-default profile is matched with that profile's thresholds; an
        // unknown id fails closed at the workflow rather than silently applying the default.
        services.Replace(ServiceDescriptor.Singleton<IStatementToleranceProfileProvider>(sp =>
            FileStatementToleranceProfileProvider.Load(
                sp.GetRequiredService<StorageOptions>().RootPath,
                sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(FileStatementToleranceProfileProvider).FullName!))));
        services.TryAddSingleton<FundAccountCloseReadinessService>();

        services.TryAddSingleton<ICashSyncOrchestrationService, CashSyncOrchestrationService>();

        services.TryAddSingleton(Dk1TrustGateReadinessOptions.Default);
        services.TryAddSingleton<Dk1TrustGateReadinessService>();
        services.TryAddSingleton<TradingOperatorReadinessService>();
        services.TryAddSingleton<ITradingOperatorReadinessProvider>(sp =>
            sp.GetRequiredService<TradingOperatorReadinessService>());
        services.TryAddSingleton<ILiveOrderReadinessGate, TradingOperatorLiveOrderReadinessGate>();
        services.TryAddSingleton<CollateralExposureService>();
        services.TryAddSingleton<RiskRuleRuntimeService>();
        // Cross-strategy portfolio aggregation: the registry tracks every active run's
        // portfolio; the host paper portfolio is pre-registered so the aggregate surface
        // (and the exposure feed below) reflects the workstation book even before
        // strategy runs register their own portfolios.
        services.TryAddSingleton<PortfolioRegistry>(sp =>
        {
            var registry = new PortfolioRegistry();
            if (sp.GetService<Meridian.Execution.Models.IPortfolioState>() is
                Meridian.Execution.Models.IMultiAccountPortfolioState hostPortfolio)
            {
                registry.Register("workstation-paper", hostPortfolio);
            }

            return registry;
        });
        services.TryAddSingleton<IAggregatePortfolioService, AggregatePortfolioService>();
        // Exposure feed for the portfolio-aware rules: sourced from the same aggregation
        // service the Portfolio workspace reports, so enforcement and display agree.
        services.TryAddSingleton<Meridian.Risk.IPortfolioExposureProvider>(sp =>
            new AggregatePortfolioExposureProvider(
                sp.GetRequiredService<IAggregatePortfolioService>(),
                sp.GetService<Meridian.Execution.Models.IPortfolioState>(),
                sp.GetService<PortfolioRegistry>(),
                sp.GetService<Meridian.Domain.Collectors.QuoteCollector>(),
                sp.GetService<Meridian.Domain.Collectors.TradeDataCollector>(),
                // Lazy accessor, not a constructor dependency: the OMS depends on the risk
                // validator that consumes this provider, so resolving it eagerly would
                // close a DI cycle. Working orders still reserve their exposure.
                orderManagerAccessor: sp.GetService<Meridian.Execution.Sdk.IOrderManager>));
        // Governed-approval queue for escalated orders (severity outcome: Escalate parks).
        // Queue transitions persist atomically so parked approvals survive restarts.
        services.TryAddSingleton<RiskEscalationQueueService>(sp => new RiskEscalationQueueService(
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RiskEscalationQueueService>>(),
            sp.GetService<ExecutionAuditTrailService>(),
            sp.GetService<RiskEscalationQueueOptions>()));
        // Enforced pre-trade risk path: Meridian.Risk's CompositeRiskValidator is the
        // IRiskValidator the OMS invokes before routing an order, composed of the operator-tuned
        // guardrails (thresholds sourced live from RiskRuleRuntimeService and the operator
        // controls, so the dashboard and the enforcement can never disagree) plus any additional
        // IRiskRule registrations a host contributes. Rules run in registration order: drawdown
        // circuit breaker, order-rate throttle, the position-limit back-stop (position
        // limits are also enforced upstream by the operator-controls gate with its
        // manual-override semantics), then the portfolio-aware gross-exposure,
        // symbol-concentration, and order-notional rules fed from the aggregate portfolio.
        // Severities map to real outcomes: Warning flags, Error rejects, Escalate parks the
        // order in the governed-approval queue, Critical also trips the execution circuit breaker.
        services.TryAddSingleton<Meridian.Execution.IRiskValidator>(sp =>
        {
            var runtime = sp.GetRequiredService<RiskRuleRuntimeService>();
            var orderRateThrottle = new Meridian.Risk.Rules.OrderRateThrottle(
                () => runtime.MaxOrdersPerMinute,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Meridian.Risk.Rules.OrderRateThrottle>>());

            // Point the dashboard at the instance that enforces, so the reported rate is the number
            // the gate will compare against the ceiling rather than a reconstruction from audit
            // history that cannot see in-flight reservations.
            runtime.OrderRateUsageProbe = () => orderRateThrottle.CurrentUsage;

            var rules = new List<Meridian.Risk.IRiskRule>
            {
                new DrawdownGuardrailRule(runtime),
                orderRateThrottle,
            };

            var operatorControls = sp.GetService<Meridian.Execution.Services.ExecutionOperatorControlService>();
            if (sp.GetService<Meridian.Execution.Sdk.IPositionTracker>() is { } positionTracker)
            {
                rules.Add(new Meridian.Risk.Rules.PositionLimitRule(
                    positionTracker,
                    () => operatorControls?.GetSnapshot().DefaultMaxPositionSize,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Meridian.Risk.Rules.PositionLimitRule>>()));
            }

            var exposureProvider = sp.GetRequiredService<Meridian.Risk.IPortfolioExposureProvider>();
            rules.Add(new Meridian.Risk.Rules.GrossExposureRule(
                exposureProvider,
                () => runtime.MaxGrossExposure,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Meridian.Risk.Rules.GrossExposureRule>>()));
            rules.Add(new Meridian.Risk.Rules.SymbolConcentrationRule(
                exposureProvider,
                () => runtime.MaxSymbolConcentrationPercent,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Meridian.Risk.Rules.SymbolConcentrationRule>>()));
            rules.Add(new Meridian.Risk.Rules.OrderNotionalRule(
                exposureProvider,
                () => runtime.MaxOrderNotional,
                () => runtime.EscalateOrderNotional,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Meridian.Risk.Rules.OrderNotionalRule>>()));

            rules.AddRange(sp.GetServices<Meridian.Risk.IRiskRule>());
            return new Meridian.Risk.CompositeRiskValidator(
                rules,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Meridian.Risk.CompositeRiskValidator>>(),
                operatorControls,
                sp.GetService<RiskEscalationQueueService>());
        });
        services.TryAddSingleton<StrategyRunReviewPacketService>();
        services.TryAddSingleton<BacktestToLivePromoter>();
        services.TryAddSingleton<PromotionService>();
        services.TryAddSingleton<ISecurityMasterWorkbenchQueryService, SecurityMasterWorkbenchQueryService>();
        // Durable governed-revision lifecycle state is process-wide, so it is a singleton consulted by
        // the scoped command service (publish refuses to run unless the revision is durably Approved).
        services.TryAddSingleton<
            Meridian.Application.SecurityMaster.ISecurityMasterRevisionStore,
            Meridian.Application.SecurityMaster.InMemorySecurityMasterRevisionStore>();
        services.TryAddScoped<
            Meridian.Application.SecurityMaster.ISecurityMasterWorkbenchCommandService,
            Meridian.Application.SecurityMaster.SecurityMasterWorkbenchCommandService>();
        // Bind the source-precedence ladder (and the rest of the workbench options) from configuration so
        // the conflict-authority policy ranks real deployment source systems. BindConfiguration registers a
        // reload-token source, so IOptionsMonitor reflects ladder changes without a restart.
        services
            .AddOptions<Meridian.Application.SecurityMaster.SecurityMasterWorkbenchOptions>()
            .BindConfiguration(Meridian.Application.SecurityMaster.SecurityMasterWorkbenchOptions.SectionName);
        // Phase 3 period-aware propagation: the ledger accounting-period status is the lock
        // authority (default-deny → HardClosed when indeterminate). The restatement resolver routes a
        // closed-period edit into a governed restatement proposal rather than a silent mutation; the
        // canonical reporting governance does not yet expose a security-to-released-run candidate
        // query. Fail closed as indeterminate instead of consulting the retired, empty report-pack
        // workflow authority and incorrectly reporting no soft-closed restatement.
        services.TryAddScoped<
            Meridian.Application.SecurityMaster.ILedgerPeriodLockReader,
            Meridian.Application.SecurityMaster.LedgerPeriodLockReader>();
        services.TryAddScoped<
            Meridian.Application.SecurityMaster.IRestatementCandidateResolver,
            Meridian.Application.SecurityMaster.IndeterminateRestatementCandidateResolver>();
        services.TryAddScoped<
            Meridian.Application.SecurityMaster.IPeriodAwareRestatementResolver,
            Meridian.Application.SecurityMaster.PeriodAwareRestatementResolver>();
        // Publish-time feed for SecurityMasterRevisionPublishedEvent.AffectedLedgerBookIds: maps an
        // impacted fund profile to its durable ledger books so period-aware routing has books to check.
        services.TryAddScoped<
            Meridian.Application.SecurityMaster.IAffectedLedgerBookResolver,
            Meridian.Application.SecurityMaster.LedgerBookAffectedResolver>();
        // Ordered published-revision side-effect fan-out: projection rebuild (Order=10) then coverage
        // invalidation (Order=20). The period-aware restatement decision is resolved separately by the
        // command service (it returns candidates the void handler seam cannot). The coverage read path
        // is currently uncached, so the invalidator defaults to a no-op.
        services.TryAddScoped<
            Meridian.Application.SecurityMaster.IMultiAssetCoverageInvalidator,
            Meridian.Application.SecurityMaster.NullMultiAssetCoverageInvalidator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            Meridian.Application.SecurityMaster.ISecurityMasterRevisionPublishedHandler,
            Meridian.Application.SecurityMaster.Rebuild.SecurityProjectionRebuildHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            Meridian.Application.SecurityMaster.ISecurityMasterRevisionPublishedHandler,
            Meridian.Application.SecurityMaster.CoverageInvalidationHandler>());
        services.TryAddSingleton<NavAttributionService>();
        // Client-grade PDF/XLSX report rendering (QuestPDF/ClosedXML). Registering the concrete
        // renderer for the ILedgerReportBinaryRenderer seam flips governed ledger exports off the
        // dependency-free plain-text fallback so the governed pack is the client deliverable. The
        // shared export service is the single seam both the browser and WPF workstations route
        // through when turning a governed report pack into client deliverables.
        services.AddFinancialReportDocumentRenderer();
        services.TryAddSingleton(sp => new LedgerClientReportExportService(
            sp.GetService<Meridian.Ledger.ILedgerReportBinaryRenderer>()));
        services.TryAddSingleton<ReportGenerationService>();
        services.TryAddSingleton<InvestmentAccountingTransactionLabService>();
        services.TryAddSingleton<ReportPackValidationService>();
        var reportingConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("MERIDIAN_LEDGER_CONNECTION_STRING");
        if (isProductionComposition && string.IsNullOrWhiteSpace(reportingConnectionString))
        {
            throw new InvalidOperationException(
                "Production reporting authority requires MERIDIAN_REPORTING_CONNECTION_STRING " +
                "or MERIDIAN_LEDGER_CONNECTION_STRING. File-backed reporting runs and schedules " +
                "are supported only by non-production local/development composition.");
        }

        if (!string.IsNullOrWhiteSpace(reportingConnectionString))
        {
            services.TryAddSingleton(new ReportingArtifactStoreOptions
            {
                ConnectionString = reportingConnectionString,
                Schema = Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_SCHEMA") ?? "reporting"
            });
            services.TryAddSingleton<ReportingMigrationRunner>();
            services.TryAddSingleton<IReportingMigrationStartup, ReportingMigrationStartup>();
            services.TryAddSingleton<PostgresReportingDeploymentProbe>();
            services.TryAddSingleton<IReportingDeploymentProbe>(sp =>
                sp.GetRequiredService<PostgresReportingDeploymentProbe>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IHostedService,
                    WorkstationReportingMigrationHostedService>());
            services.TryAddSingleton<IReportingArtifactStore>(sp =>
                new PostgresReportingArtifactStore(sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            if (!services.Any(static descriptor =>
                    descriptor.ServiceType
                    == typeof(IStatementReconciliationReportAuthorityStore)))
            {
                services.AddSingleton<IStatementReconciliationReportAuthorityStore>(sp =>
                    new PostgresStatementReconciliationReportAuthorityStore(
                        sp.GetRequiredService<ReportingArtifactStoreOptions>(),
                        sp.GetRequiredService<IReportingArtifactStore>()));
                services.TryAddSingleton<DurableStatementReconciliationAuthorityRegistration>();
            }
            services.TryAddSingleton<IReportingArtifactCatalog>(sp =>
                new PostgresReportingArtifactCatalog(sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<IReportingArtifactAuditStore>(sp =>
                new PostgresReportingArtifactAuditStore(sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<ReportingArtifactVaultService>();
            services.TryAddSingleton<IReportingGovernanceRepository>(sp =>
                new PostgresReportingGovernanceRepository(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<PostgresReportingReleaseConsistencyGate>(sp =>
                new PostgresReportingReleaseConsistencyGate(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<IReportingReleaseConsistencyGate>(sp =>
                sp.GetRequiredService<PostgresReportingReleaseConsistencyGate>());
            services.TryAddSingleton<ReportingGovernanceService>();
            services.TryAddSingleton<PostgresReportingReconciliationEvidenceStore>(sp =>
                new PostgresReportingReconciliationEvidenceStore(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<IReportingReconciliationEvidenceStore>(sp =>
                sp.GetRequiredService<PostgresReportingReconciliationEvidenceStore>());
            services.TryAddSingleton<IReportingReconciliationEvidenceRetentionStore>(sp =>
                sp.GetRequiredService<PostgresReportingReconciliationEvidenceStore>());
            services.TryAddSingleton<IReportingReconciliationEvidenceSource, ReportingReconciliationEvidenceSource>();
            services.TryAddSingleton<ReportingReconciliationEvidenceRetentionService>();
            services.TryAddSingleton<IReportingPrimaryDocumentRenderer>(sp =>
                new DocumentsReportingPrimaryDocumentRenderer(
                    sp.GetRequiredService<LedgerClientReportExportService>()));
            services.TryAddSingleton<IReportingCertifiedArtifactProducer>(sp =>
                new DeterministicReportingCertifiedArtifactProducer(
                    sp.GetRequiredService<IReportingPrimaryDocumentRenderer>(),
                    sp.GetRequiredService<IReportingCertifiedLedgerPresentationSource>()));
            services.TryAddSingleton<IReportingArtifactRetentionAuthorityProvider, ReportingArtifactRetentionAuthorityProvider>();
            services.TryAddSingleton<IReportingRestatementChangedLineResolver, GovernedReportingRestatementChangedLineResolver>();
            services.TryAddSingleton<IReportingRestatementCertificationInputProvider, GovernedReportingRestatementCertificationInputProvider>();
            services.TryAddSingleton<ReportingGovernanceCoordinatorService>();
            services.TryAddSingleton<IReportingGovernanceEndpointCoordinator>(sp =>
                sp.GetRequiredService<ReportingGovernanceCoordinatorService>());
            services.TryAddSingleton<IReportingAccessGrantStore>(sp =>
                new PostgresReportingAccessGrantStore(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<ReportingAccessGrantService>();
            services.TryAddSingleton<IReportingDeliveryStore>(sp =>
                new PostgresReportingDeliveryStore(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<IReportingRunStore>(sp =>
                new PostgresReportingRunStore(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.TryAddSingleton<Meridian.Reporting.IReportingScheduleStore>(sp =>
                new PostgresReportingScheduleStore(
                    sp.GetRequiredService<ReportingArtifactStoreOptions>()));
            services.AddSecureReportingDistribution();
        }
        else
        {
            // Local/development compatibility only. The reporting capability and deployment
            // readiness gates identify these stores as non-authoritative and fail material
            // reporting closed until the complete PostgreSQL graph is configured.
            services.TryAddSingleton<ReportingRunStoreOptions>(sp =>
                new ReportingRunStoreOptions(Path.Combine(
                    ResolveWorkstationDataDirectory(sp),
                    "reporting",
                    "runs")));
            services.TryAddSingleton<IReportingRunStore>(sp =>
                new FileReportingRunStore(
                    sp.GetRequiredService<ReportingRunStoreOptions>(),
                    sp.GetRequiredService<ILogger<FileReportingRunStore>>()));
            services.TryAddSingleton<ReportingScheduleStoreOptions>(sp =>
                new ReportingScheduleStoreOptions(Path.Combine(
                    ResolveWorkstationDataDirectory(sp),
                    "reporting",
                    "reporting-schedules.json")));
            services.TryAddSingleton<Meridian.Reporting.IReportingScheduleStore>(sp =>
                new FileReportingScheduleStore(
                    sp.GetRequiredService<ReportingScheduleStoreOptions>(),
                    sp.GetRequiredService<ILogger<FileReportingScheduleStore>>()));
        }
#pragma warning disable CS0618
        services.TryAddSingleton<IReportingScheduleStore>(sp =>
        {
            var canonical = sp.GetRequiredService<Meridian.Reporting.IReportingScheduleStore>();
            return canonical as IReportingScheduleStore
                ?? new ReportingScheduleStoreCompatibilityAdapter(canonical);
        });
#pragma warning restore CS0618
        // Resolve the durable ledger dependencies only when a certification attempt is made. This
        // keeps lightweight hosts startable while preserving fail-closed behavior: a run cannot be
        // certified from fixtures or synthesized rows when the production ledger graph is absent.
        services.TryAddSingleton<ServiceProviderReportingAuthoritativeSource>();
        services.TryAddSingleton<IReportingAuthoritativeSource>(sp =>
            sp.GetRequiredService<ServiceProviderReportingAuthoritativeSource>());
        services.TryAddSingleton<IReportingCertifiedLedgerPresentationSource>(sp =>
            sp.GetRequiredService<ServiceProviderReportingAuthoritativeSource>());
        // The legacy report-pack workflow and query-token delivery stores are intentionally not
        // registered. Their mutation endpoints are retired; canonical lifecycle state belongs to
        // ReportingGovernanceService and canonical delivery state/receipts belong to
        // IReportingDeliveryStore.
        if (!isProductionComposition)
        {
            services.TryAddSingleton<ReportTemplateGovernanceStoreOptions>(sp =>
                new ReportTemplateGovernanceStoreOptions(Path.Combine(ResolveWorkstationDataDirectory(sp), "reporting", "report-templates.json")));
            services.TryAddSingleton<IReportTemplateGovernanceStore>(sp =>
                new FileReportTemplateGovernanceStore(
                    sp.GetRequiredService<ReportTemplateGovernanceStoreOptions>(),
                    sp.GetRequiredService<ILogger<FileReportTemplateGovernanceStore>>()));
        }
        services.TryAddSingleton<ReportTemplateRegistryService>();
        services.TryAddSingleton<DefaultReportingTemplateCatalog>();
        services.TryAddSingleton<IReportingStarterKitCatalog, DefaultReportingStarterKitCatalog>();
        services.TryAddSingleton(sp =>
            new GovernedReportingTemplateCatalog(
                sp.GetRequiredService<DefaultReportingTemplateCatalog>(),
                sp.GetRequiredService<ReportTemplateRegistryService>()));
        services.TryAddSingleton<IReportingTemplateCatalog>(sp =>
            sp.GetRequiredService<GovernedReportingTemplateCatalog>());
        // Derived security→report-line index backing the report-pack restatement candidate lookup. A
        // singleton so it shares the lifetime of the (singleton) workflow service that keeps it current;
        // it is rebuilt from the persisted workflow records on construction.
        services.TryAddSingleton<IReportPackSecurityLineIndex, InMemoryReportPackSecurityLineIndex>();
        services.TryAddSingleton<ReportPackWorkflowService>();
        services.TryAddSingleton<ReportWriterDatasetSourceService>();
        services.TryAddSingleton<ReportWriterGridArtifactService>();
        // Retained as a compatibility service for callers compiled against the earlier projection
        // seam. Canonical capital-account primary documents use the exact certified ledger pack.
        services.TryAddSingleton<IReportingPartnersCapitalSource>(sp =>
            new LedgerReportingPartnersCapitalSource(sp.GetService<ILedgerJournalStore>()));
        services.TryAddSingleton<ReportingOrchestrationRetentionOptions>();
        services.TryAddSingleton<IReportingOrchestrationService>(sp =>
            new ReportingOrchestrationService(
                sp.GetRequiredService<IReportingTemplateCatalog>(),
                new DeterministicReportingSectionRenderer(),
                () => DateTimeOffset.UtcNow,
                sp.GetRequiredService<IReportingRunStore>(),
                // Null until the report-run stream broadcaster is registered (D1d); the null-object
                // default keeps run execution unaffected in the meantime.
                sp.GetService<IReportingRunNotifier>(),
                partnersCapitalSource: null,
                retentionOptions: sp.GetRequiredService<ReportingOrchestrationRetentionOptions>()));
        if (!isProductionComposition)
        {
            services.TryAddSingleton<ReportingStarterKitStoreOptions>(sp =>
                new ReportingStarterKitStoreOptions(Path.Combine(ResolveWorkstationDataDirectory(sp), "reporting", "reporting-starter-kit.json")));
            services.TryAddSingleton<IReportingStarterKitStore>(sp =>
                new FileReportingStarterKitStore(
                    sp.GetRequiredService<ReportingStarterKitStoreOptions>(),
                    sp.GetRequiredService<ILogger<FileReportingStarterKitStore>>()));
        }
        services.TryAddSingleton<IReportingRunReadinessDependencyEvaluator, ReportingRunReadinessDependencyEvaluator>();
        services.TryAddSingleton<ReportingRunReadinessService>();
        services.TryAddSingleton<ReportingRunCertificationService>();
        services.TryAddSingleton<ReportingRunCommandService>();
        services.TryAddSingleton(sp =>
            new ReportingScheduleService(
                sp.GetRequiredService<IReportingOrchestrationService>(),
                sp.GetService<Meridian.Reporting.IReportingScheduleStore>(),
                deliveryService: null,
                sp.GetService<GovernedReportingTemplateCatalog>(),
                sp.GetService<ReportWriterDatasetSourceService>(),
                sp.GetRequiredService<ReportingRunReadinessService>(),
                sp.GetRequiredService<ReportingRunCertificationService>(),
                sp.GetService<IReportingGovernanceEndpointCoordinator>(),
                sp.GetService<IReportingRecipientDestinationResolver>(),
                sp.GetRequiredService<IReportingDeploymentReadinessService>()));
        services.TryAddSingleton(ReportingScheduleWorkerOptions.Default);
        services.TryAddSingleton<ReportingScheduleWorkerReadinessState>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ReportingScheduleHostedService>());
        services.TryAddSingleton<ReportingStarterKitService>();
        services.TryAddSingleton<ReportPackRunReadService>();
        services.TryAddSingleton<ReportingMigrationReadinessState>();
        services.TryAddSingleton<ReconciliationCaseworkAuthorityReadinessState>();
        services.TryAddSingleton<IReportingDeploymentReadinessService, ReportingDeploymentReadinessService>();
        services.TryAddSingleton<W4AcceptanceFilter>();
        if (!isProductionComposition)
        {
            services.TryAddSingleton<IGovernanceReportPackRepository>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<FileGovernanceReportPackRepository>>();
                return new FileGovernanceReportPackRepository(ResolveWorkstationDataDirectory(sp), logger);
            });
        }
        services.TryAddSingleton<LedgerAmountProvenanceService>();
        services.TryAddSingleton<FundOperationsWorkspaceReadService>();
        services.TryAddSingleton(sp => new FamilyOfficeReadService(
            sp.GetService<IFundStructureService>(),
            sp.GetService<Meridian.PortfolioRecords.FundAccounts.IFundAccountService>(),
            sp.GetService<StrategyRunReadService>()));
        services.TryAddSingleton<IOperationsStatusDerivationService, OperationsStatusDerivationService>();
        services.TryAddSingleton<IOperationsContinuityRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileOperationsContinuityRepository>>();
            return new FileOperationsContinuityRepository(
                ResolveWorkstationDataDirectory(sp),
                sp.GetRequiredService<IOperationsStatusDerivationService>(),
                logger);
        });
        services.TryAddSingleton<IOperationsWorkflowAuditStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileOperationsWorkflowAuditStore>>();
            return new FileOperationsWorkflowAuditStore(ResolveWorkstationDataDirectory(sp), logger);
        });
        services.TryAddSingleton<IOperationsContinuityWorkflowService>(sp =>
            new OperationsContinuityWorkflowService(
                sp.GetRequiredService<IOperationsContinuityRepository>(),
                sp.GetRequiredService<IOperationsWorkflowAuditStore>(),
                sp.GetRequiredService<IOperationsStatusDerivationService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IOperationsContinuityTransactionalCommitStore>(),
                sp.GetService<ContractSecurityMasterQueryService>()));
        services.TryAddSingleton<IOperationsApprovalPolicyMatrixService, OperationsApprovalPolicyMatrixService>();
        services.TryAddSingleton<IOperationsCloseCalendarService, OperationsCloseCalendarService>();
        services.TryAddSingleton<IAccountingCloseManagementService, AccountingCloseManagementService>();
        services.TryAddSingleton<IAccountingReportPackageService, AccountingReportPackageService>();
        services.TryAddSingleton<IAutomatedJournalCapitalAccountReconciliationResolver>(sp =>
            new LedgerCapitalAccountReconciliationResolver(
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IFundProfileTenancyRegistry>()));

        services.TryAddSingleton<IReconciliationRunRepository>(sp =>
            new FileReconciliationRunRepository(
                ResolveWorkstationDataDirectory(sp),
                sp.GetRequiredService<ILogger<FileReconciliationRunRepository>>()));
        services.TryAddSingleton<IStrategyLedgerReconciliationSourceAdapter, StrategyLedgerReconciliationSourceAdapter>();
        services.TryAddSingleton<IStrategyPortfolioReconciliationSourceAdapter, StrategyPortfolioReconciliationSourceAdapter>();
        services.TryAddSingleton<IInternalCashReconciliationSourceAdapter, BankInternalCashReconciliationSourceAdapter>();
        services.TryAddSingleton<IExternalStatementSource, NullExternalStatementSource>();
        services.TryAddSingleton<IExternalStatementReconciliationSourceAdapter, ExternalStatementReconciliationSourceAdapter>();
        services.TryAddSingleton<ISecurityMasterAccountingEventService, SecurityMasterAccountingEventService>();
        services.TryAddSingleton<ISecurityMasterAccountingEventSourceAdapter>(sp =>
            new SecurityMasterAccountingEventSourceAdapter(
                sp.GetService<ContractSecurityMasterQueryService>(),
                sp.GetService<IAssetOperationsQueryService>()));
        services.TryAddSingleton<FileAccountingConfigurationStore>(sp =>
            new FileAccountingConfigurationStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "accounting-configuration.json")));
        services.TryAddSingleton<IAccountingConfigurationStore>(sp => sp.GetRequiredService<FileAccountingConfigurationStore>());
        services.TryAddSingleton<IAccountingActionAuditStore>(sp =>
            sp.GetRequiredService<IAccountingConfigurationStore>() is IAccountingActionAuditStore auditStore
                ? auditStore
                : sp.GetRequiredService<FileAccountingConfigurationStore>());
        services.TryAddSingleton<FileFundProfileTenancyRegistry>(sp =>
            new FileFundProfileTenancyRegistry(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "tenancy", "fund-profile-tenancy.json")));
        services.TryAddSingleton<IFundProfileTenancyRegistry>(sp => sp.GetRequiredService<FileFundProfileTenancyRegistry>());
        services.TryAddSingleton<IFundProfileTenantGuard, RegistryFundProfileTenantGuard>();
        services.TryAddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.TryAddSingleton<IAccountingPostingCandidateService, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingPostingCandidateWriteBuilder, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingPostingCandidateAuthorityBuilder>(sp =>
            sp.GetRequiredService<IAccountingPostingCandidateWriteBuilder>() as IAccountingPostingCandidateAuthorityBuilder
            ?? throw new InvalidOperationException(
                "The configured accounting posting candidate write builder must also implement " +
                $"{nameof(IAccountingPostingCandidateAuthorityBuilder)}."));
        services.TryAddSingleton<IAssetAccountingEventSpineService>(sp =>
            AssetAccountingEventSpineService.TryCreate(
                sp.GetService<IAssetAccountingEventProjectionStore>(),
                sp.GetService<IInstrumentPositionProjectionStore>(),
                sp.GetService<ContractSecurityMasterQueryService>(),
                sp.GetService<ILedgerBookService>(),
                sp.GetService<IAccountingPolicyService>(),
                sp.GetService<IAccountingConfigurationService>(),
                sp.GetService<IAccountingPostingCandidateAuthorityBuilder>(),
                sp.GetService<ILedgerJournalStore>())!);
        services.TryAddSingleton<IAccountingPostingCandidatePostService>(sp =>
            new AccountingPostingCandidatePostService(
                sp.GetRequiredService<IAccountingPostingCandidateWriteBuilder>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IAtomicTaxLotJournalStore>(),
                sp.GetService<IAssetAccountingEventProjectionStore>(),
                sp.GetRequiredService<IAccountingPostingCandidateAuthorityBuilder>()));
        services.TryAddSingleton<IAccountingBasisProjectionSetService, AccountingBasisProjectionSetService>();
        services.TryAddSingleton<IManualJournalEntryDraftStore>(sp =>
            new FileManualJournalEntryDraftStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "manual-journal-drafts.json")));
        services.TryAddSingleton<FileDailyValuationPortfolioSource>(sp =>
            new FileDailyValuationPortfolioSource(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "daily-valuation-schedules.json")));
        services.TryAddSingleton<IDailyValuationPortfolioSource>(sp =>
            sp.GetRequiredService<FileDailyValuationPortfolioSource>());
        services.TryAddSingleton<IDailyValuationScheduleStatusSource>(sp =>
            sp.GetRequiredService<FileDailyValuationPortfolioSource>());
        services.TryAddSingleton(sp =>
            new DailyValuationPositionService(
                sp.GetService<IPositionSnapshotStore>(),
                sp.GetService<ICanonicalSymbolRegistry>(),
                sp.GetService<ContractSecurityMasterQueryService>()));
        services.TryAddSingleton<FileAutomatedJournalScheduleStore>(sp =>
            new FileAutomatedJournalScheduleStore(
                Path.Combine(ResolveWorkstationDataDirectory(sp), "accounting", "monthly-automated-journal-schedules.json")));
        services.TryAddSingleton<IAutomatedJournalScheduleStore>(sp =>
            sp.GetRequiredService<FileAutomatedJournalScheduleStore>());
        services.TryAddSingleton<IAutomatedJournalScheduleStatusSource>(sp =>
            sp.GetRequiredService<FileAutomatedJournalScheduleStore>());
        services.TryAddSingleton<IAccountingPositionSnapshotCaptureService>(sp =>
            new AccountingPositionSnapshotCaptureService(
                sp.GetService<IPositionSnapshotStore>(),
                sp.GetService<IDailyValuationPortfolioSource>(),
                sp.GetService<IAutomatedJournalScheduleStore>(),
                sp.GetService<IAccountQueryService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IFundProfileTenancyRegistry>()));
        services.TryAddSingleton<IAutomatedJournalDividendPositionResolver>(sp =>
            new PositionSnapshotAutomatedJournalDividendPositionResolver(
                sp.GetService<IPositionSnapshotStore>()));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IManualJournalEntryWorkbenchService>(sp =>
            new ManualJournalEntryWorkbenchService(
                sp.GetRequiredService<IManualJournalEntryDraftStore>(),
                sp.GetRequiredService<IAccountingConfigurationService>(),
                sp.GetRequiredService<IAccountingActionAuditStore>(),
                sp.GetService<ContractSecurityMasterQueryService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<ReportPackWorkflowService>(),
                sp.GetService<Meridian.Contracts.Banking.IBankTransactionSource>(),
                sp.GetService<IGovernedLedgerPostingTarget>()));
        services.TryAddSingleton<IManualJournalEntryLifecycleService>(sp =>
            (IManualJournalEntryLifecycleService)sp.GetRequiredService<IManualJournalEntryWorkbenchService>());
        services.TryAddSingleton<DailyValuationBatchLifecycleService>();
        services.TryAddSingleton(AutomatedJournalEvidencePolicy.Default);
        services.TryAddSingleton(sp =>
            new AutomatedJournalDraftIntakeService(
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>(),
                sp.GetRequiredService<IManualJournalEntryDraftStore>(),
                sp.GetRequiredService<IAccountingConfigurationService>()));
        services.TryAddSingleton(sp =>
        {
            var securityMaster = sp.GetService<ContractSecurityMasterQueryService>();
            var providerRegistry = sp.GetService<ProviderRegistry>();
            var journalStore = sp.GetService<ILedgerJournalStore>();
            var positionService = sp.GetRequiredService<DailyValuationPositionService>();
            return new AutomatedJournalIntakeRunner(
                sp.GetRequiredService<AutomatedJournalDraftIntakeService>(),
                new FeeScheduleAccrualEventProducer(),
                securityMaster is null ? null : new CorporateActionDividendEventProducer(securityMaster),
                sp.GetService<Meridian.Contracts.Ledger.ILedgerBookService>(),
                providerRegistry is null || journalStore is null
                    ? null
                    : new DailyMarkToMarketService(
                        new RegisteredHistoricalCloseMarkPriceSource(providerRegistry),
                        new LedgerMarkToMarketCarryingValueSource(journalStore)),
                positionService,
                sp.GetRequiredService<AutomatedJournalEvidencePolicy>(),
                sp.GetService<IAutomatedJournalCapitalAccountReconciliationResolver>(),
                sp.GetRequiredService<TimeProvider>());
        });
        // Durable ledger and reporting-evidence services are present only when persistence-backed
        // storage is configured. Resolve them optionally so lightweight hosts still compose; when
        // present, the bridge must hand hard-close evidence to reporting before close completion is
        // represented as certifiable.
        services.TryAddSingleton<IAccountingClosePostingWorkbench>(sp =>
            new AccountingClosePostingWorkbenchBridge(
                sp.GetRequiredService<AutomatedJournalIntakeRunner>(),
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>(),
                sp.GetRequiredService<IManualJournalEntryLifecycleService>(),
                sp.GetService<Meridian.Contracts.Ledger.ILedgerBookService>(),
                sp.GetService<ReportingReconciliationEvidenceRetentionService>(),
                sp.GetService<IFundProfileTenancyRegistry>(),
                sp.GetService<IReconciliationBreakQueueRepository>(),
                sp.GetService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IReportingReleaseConsistencyGate>()));
        services.TryAddSingleton<DailyValuationScheduledWorker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DailyValuationSchedulerHostedService>());
        services.TryAddSingleton<AutomatedJournalScheduledWorker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomatedJournalSchedulerHostedService>());
        services.TryAddSingleton<ICapitalAccountWorkbenchService>(sp =>
            new CapitalAccountWorkbenchService(
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>(),
                sp.GetService<ReportPackWorkflowService>()));
        services.TryAddSingleton<IPrivateCapitalFundEventCommandCenterService>(sp =>
            new PrivateCapitalFundEventCommandCenterService(
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>()));
        services.TryAddSingleton<IPrivateCapitalCloseCockpitService>(sp =>
            new PrivateCapitalCloseCockpitService(
                sp.GetService<IManualJournalEntryWorkbenchService>(),
                sp.GetService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IDailyValuationScheduleStatusSource>(),
                sp.GetService<IAutomatedJournalScheduleStatusSource>()));
        services.TryAddSingleton<IFinancialOperationsCommandCenterReadService>(sp =>
            new FinancialOperationsCommandCenterReadService(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IOperationsCloseCalendarService>(),
                sp.GetService<IPrivateCapitalCloseCockpitService>()));
        services.TryAddSingleton<SecurityMasterExceptionSlaConfig>();
        services.TryAddSingleton<IReconciliationSlaPolicyProvider>(sp =>
            new SecurityMasterReconciliationSlaPolicyProvider(
                sp.GetService<SecurityMasterExceptionSlaConfig>()));
        services.TryAddSingleton<IReconciliationBreakQueueRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileReconciliationBreakQueueRepository>>();
            return new FileReconciliationBreakQueueRepository(
                ResolveWorkstationDataDirectory(sp),
                logger,
                sp.GetService<IReconciliationSlaPolicyProvider>());
        });
        services.TryAddSingleton<IStatementReconciliationCaseworkHandoffService>(sp =>
            new StatementReconciliationCaseworkHandoffService(
                sp.GetRequiredService<IReconciliationBreakQueueRepository>(),
                sp.GetService<Meridian.Infrastructure.Reconciliation.IReconciliationBreakStore>(),
                sp.GetService<Meridian.Infrastructure.Reconciliation.IReconciliationCaseStore>(),
                sp.GetService<IStatementRunWorkflowService>(),
                sp.GetService<IOperationsContinuityWorkflowService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetRequiredService<Meridian.Infrastructure.Reconciliation.IStatementCaseworkCommitStore>()));
        services.TryAddSingleton<SecurityMasterExceptionCaseworkService>(sp =>
            new SecurityMasterExceptionCaseworkService(
                sp.GetService<IReconciliationBreakQueueRepository>(),
                sp.GetRequiredService<ILogger<SecurityMasterExceptionCaseworkService>>()));
        services.TryAddSingleton<ReconciliationProjectionService>();
        services.TryAddSingleton<IReconciliationRunService, ReconciliationRunService>();
        services.TryAddSingleton<Meridian.Ui.Shared.Contracts.Reconciliation.IReconciliationApiService>(sp =>
            new ReconciliationApiService(
                sp.GetRequiredService<IStatementRunWorkflowService>(),
                sp.GetService<IAccountQueryService>(),
                sp.GetService<IFundProfileTenancyRegistry>()));
        services.TryAddSingleton<IStatementReconciliationIntakeAuthority>(sp =>
            new StatementReconciliationIntakeAuthority(
                sp.GetService<IAccountQueryService>(),
                sp.GetService<IFundProfileTenancyRegistry>(),
                sp.GetService<ILedgerBookService>(),
                sp.GetService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IStatementRunWorkflowService>(),
                sp.GetService<Meridian.Ui.Shared.Contracts.Reconciliation.IReconciliationApiService>(),
                sp.GetService<IReconciliationBreakQueueRepository>()));
        services.TryAddSingleton<IOperationsContinuityReconciliationBridge>(sp =>
            new OperationsContinuityReconciliationBridge(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IReconciliationRunService>(),
                sp.GetService<IReconciliationBreakQueueRepository>(),
                sp.GetService<Meridian.Ui.Shared.Contracts.Reconciliation.IReconciliationApiService>()));
        services.TryAddSingleton<CollateralIngestionBuffer>();
        services.TryAddSingleton<CollateralExposureService>();

        services.TryAddSingleton<Meridian.Core.Contracts.IProviderCredentialStore>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var dataRoot = configStore.GetDataRoot();
            return new ProviderCredentialStore(
                sp.GetRequiredService<Meridian.DataIntegration.Credentials.IProviderCredentialStore>(),
                dataRoot);
        });
        services.TryAddSingleton<IProviderModuleSetupService, ProviderModuleSetupService>();

        services.AddWorkflowLibrary();
        services.TryAddSingleton<IExtensibilityConfigurationStore>(sp =>
            new FileExtensibilityConfigurationStore(
                ResolveWorkstationDataDirectory(sp),
                sp.GetRequiredService<ILogger<FileExtensibilityConfigurationStore>>()));
        services.AddExtensibilityCatalog();
        services.AddEvidenceWorkflowFabric(isProductionComposition);
        services.TryAddSingleton<WorkstationWorkflowSummaryService>();
        services.TryAddSingleton<Meridian.Ui.Shared.Contracts.Integrations.IOmsIntegrationApiHandler, OmsIntegrationService>();
        services.AddCoveredCallBacktestServices();

        // Quote-stream fan-out. The broadcaster is registered as the IQuoteUpdateNotifier so the
        // QuoteCollector wakes it on quote changes; SSE connections subscribe via
        // IQuoteStreamBroadcaster. The endpoint still polls until the endpoint-switch increment, so
        // wiring this in changes no observable behaviour (no subscribers = no fan-out output).
        services.TryAddSingleton(new QuoteStreamOptions());
        services.TryAddSingleton(sp => new StreamConnectionRegistry(
            sp.GetRequiredService<QuoteStreamOptions>().MaxConcurrentStreamsPerSession));
        services.TryAddSingleton(sp => new QuoteStreamBroadcaster(
            sp,
            sp.GetRequiredService<StreamConnectionRegistry>(),
            sp.GetRequiredService<QuoteStreamOptions>()));
        services.TryAddSingleton<IQuoteStreamBroadcaster>(sp => sp.GetRequiredService<QuoteStreamBroadcaster>());
        services.TryAddSingleton<IQuoteUpdateNotifier>(sp => sp.GetRequiredService<QuoteStreamBroadcaster>());

        // Report-run status stream (shadow until the SSE endpoint lands in D2). Shares the quote
        // stream's registry cap and options. Registered as IReportingRunNotifier so run persistence
        // wakes it (ReportingOrchestrationService resolves the notifier). No endpoint => no
        // subscribers => no observable behaviour change yet.
        services.TryAddSingleton(sp => new ReportRunStreamBroadcaster(
            sp,
            sp.GetRequiredService<StreamConnectionRegistry>(),
            sp.GetRequiredService<QuoteStreamOptions>()));
        services.TryAddSingleton<IReportingRunNotifier>(sp => sp.GetRequiredService<ReportRunStreamBroadcaster>());

        return services;
    }

    private static PlaidOptions ResolvePlaidOptions(IServiceProvider sp)
    {
        var configuration = sp.GetService<IConfiguration>();
        var section = configuration?.GetSection("Plaid");
        var environment = ParsePlaidEnvironment(
            section?["Environment"] ??
            Environment.GetEnvironmentVariable("PLAID_ENV") ??
            Environment.GetEnvironmentVariable("PLAID_ENVIRONMENT"));
        var products = ParsePlaidProducts(section?["DefaultProducts"]);
        return new PlaidOptions(
            Environment: environment,
            ClientId: section?["ClientId"] ?? Environment.GetEnvironmentVariable("PLAID_CLIENT_ID"),
            Secret: section?["Secret"] ?? Environment.GetEnvironmentVariable("PLAID_SECRET"),
            WebhookBaseUrl: section?["WebhookBaseUrl"] ?? Environment.GetEnvironmentVariable("PLAID_WEBHOOK_BASE_URL"),
            EnableTransfers: ParseBoolean(section?["EnableTransfers"]) || ParseBoolean(Environment.GetEnvironmentVariable("PLAID_ENABLE_TRANSFERS")),
            EnableInvestments: !ParseBoolean(section?["DisableInvestments"]) && !ParseBoolean(Environment.GetEnvironmentVariable("PLAID_DISABLE_INVESTMENTS")),
            DefaultProducts: products.Length == 0 ? PlaidOptions.Default.DefaultProducts : products,
            EnableLiveTransfers: ParseBoolean(section?["EnableLiveTransfers"]) || ParseBoolean(Environment.GetEnvironmentVariable("PLAID_ENABLE_LIVE_TRANSFERS")));
    }

    private static PlaidEnvironmentDto ParsePlaidEnvironment(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "production" => PlaidEnvironmentDto.Production,
            "development" => PlaidEnvironmentDto.Development,
            _ => PlaidEnvironmentDto.Sandbox
        };

    private static PlaidProductDto[] ParsePlaidProducts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item => Enum.TryParse<PlaidProductDto>(item, ignoreCase: true, out var parsed)
                ? parsed
                : (PlaidProductDto?)null)
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .Distinct()
            .ToArray();
    }

    private static bool ParseBoolean(string? value)
        => bool.TryParse(value, out var parsed) && parsed;

    public static IServiceCollection AddLeanAutoExportHostedService(this IServiceCollection services)
    {
        services.TryAddSingleton<LeanAutoExportService>();
        services.AddHostedService(sp => sp.GetRequiredService<LeanAutoExportService>());
        return services;
    }

    private static void AddCoveredCallBacktestServices(this IServiceCollection services)
    {
        services.AddOptions<CoveredCallBacktestOptions>()
            .BindConfiguration(CoveredCallBacktestOptions.SectionName);

        services.TryAddSingleton<ICoveredCallChainProviderFactory, CoveredCallChainProviderFactory>();
        services.TryAddSingleton<Func<BacktestRequest, BacktestEngine>>(sp =>
        {
            BacktestEngine CreateEngine(BacktestRequest request)
            {
                var storageOptions = new StorageOptions { RootPath = request.DataRoot };
                var catalogService = new StorageCatalogService(request.DataRoot, storageOptions);
                return new BacktestEngine(
                    sp.GetRequiredService<ILogger<BacktestEngine>>(),
                    catalogService,
                    sp.GetService<ContractSecurityMasterQueryService>(),
                    sp.GetService<ICorporateActionAdjustmentService>(),
                    sp.GetService<IBacktestPreflightService>());
            }

            return CreateEngine;
        });

        services.TryAddSingleton<CoveredCallBacktestService>(sp => new CoveredCallBacktestService(
            engineFactory: sp.GetRequiredService<Func<BacktestRequest, BacktestEngine>>(),
            chainFactory: sp.GetRequiredService<ICoveredCallChainProviderFactory>(),
            runRepository: sp.GetRequiredService<IStrategyRepository>(),
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CoveredCallBacktestOptions>>(),
            resultCache: sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            loggerFactory: sp.GetRequiredService<ILoggerFactory>(),
            evidenceArtifactStore: sp.GetRequiredService<IEvidenceArtifactStore>()));
        services.TryAddSingleton<ICoveredCallBacktestService>(sp => sp.GetRequiredService<CoveredCallBacktestService>());
        services.AddHostedService(sp => sp.GetRequiredService<CoveredCallBacktestService>());
    }

    private static string ResolveConfigDataRoot(IServiceProvider services)
    {
        var configStore = services.GetRequiredService<ConfigStore>();
        return configStore.GetDataRoot(configStore.Load());
    }

    private static string ResolveWorkstationDataDirectory(IServiceProvider services)
        => Path.Combine(ResolveConfigDataRoot(services), "workstation");
}

internal sealed class WorkstationScopedAccessMigrationHostedService(
    PostgresScopedAccessAssignmentStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.EnsureMigratedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

/// <summary>
/// Completes the existing checksummed reporting migration workflow before services that query
/// the reporting authority are constructed.
/// </summary>
public interface IReportingMigrationStartup
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent process-local startup gate over <see cref="ReportingMigrationRunner"/>.
/// </summary>
internal sealed class ReportingMigrationStartup(
    ReportingMigrationRunner runner,
    ReportingMigrationReadinessState readiness,
    ReconciliationCaseworkAuthorityReadinessState caseworkReadiness,
    IReconciliationBreakQueueRepository breakQueue) : IReportingMigrationStartup
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (readiness.IsReady && caseworkReadiness.IsReady)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (readiness.IsReady && caseworkReadiness.IsReady)
            {
                return;
            }

            if (!readiness.IsReady)
            {
                readiness.MarkNotReady();
                await runner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
                readiness.MarkReady();
            }

            caseworkReadiness.MarkNotReady();
            if (breakQueue is not IReconciliationBreakQueueAuthorityProbe authorityProbe)
            {
                throw new InvalidOperationException(
                    "The configured reconciliation casework authority cannot verify its durable queue state.");
            }

            await authorityProbe.VerifyAsync(cancellationToken).ConfigureAwait(false);
            caseworkReadiness.MarkReady();
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal sealed class WorkstationReportingMigrationHostedService(
    IReportingMigrationStartup startup,
    ReportingMigrationReadinessState readiness,
    ReconciliationCaseworkAuthorityReadinessState caseworkReadiness) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        startup.EnsureReadyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        readiness.MarkNotReady();
        caseworkReadiness.MarkNotReady();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Defers construction of the production ledger adapter until an actual certification request.
/// Missing host capabilities therefore block the material operation with an explicit reporting
/// availability error instead of either synthesizing data or preventing unrelated read-only hosts
/// from starting.
/// </summary>
internal sealed class ServiceProviderReportingAuthoritativeSource :
    IReportingAuthoritativeSource,
    IReportingCertifiedLedgerPresentationSource
{
    private readonly IServiceProvider _services;

    public ServiceProviderReportingAuthoritativeSource(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    internal bool IsConfigured =>
        _services.GetService<DatabaseMigrationReadinessReceipt>() is
        {
            LedgerReady: true,
            FundAccountsReady: true,
            FundStructureReady: true
        }
        && _services.GetService<ILedgerJournalStore>() is PostgresLedgerJournalStore
        && _services.GetService<IFundProfileTenancyRegistry>() is PostgresFundProfileTenancyRegistry
        && _services.GetService<IFundAccountService>() is PostgresFundAccountService
        && _services.GetService<IFundStructureService>() is PostgresFundStructureService;

    public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveSource();
        return source.CaptureAsync(parameters, accessContext, cancellationToken);
    }

    public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        ReportingAuthoritativeSourceCaptureIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var source = ResolveSource();
        return source.CaptureAsync(parameters, accessContext, intent, cancellationToken);
    }

    public async ValueTask<ReportingCertifiedLedgerPresentationInput?> ResolveExactAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReportingCertifiedLedgerPresentationBinding.IsRequired(manifest))
        {
            return null;
        }

        var parameters = manifest.ResolvedParameters
            ?? throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' has no retained parameters for canonical ledger presentation.");
        var scope = manifest.OperationalScope
            ?? throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' has no retained operational scope for canonical ledger presentation.");
        var sourceCheckpoint = manifest.AuthoritativeSource
            ?? throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' has no retained authoritative checkpoint for canonical ledger presentation.");
        if (string.IsNullOrWhiteSpace(scope.CompanyId))
        {
            throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' has no company scope for canonical ledger presentation.");
        }
        var certifiedPresentationEvidence =
            ReportingCertifiedLedgerPresentationBinding.GetSingleEvidenceId(sourceCheckpoint);
        if (certifiedPresentationEvidence is null)
        {
            throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' has no unique retained signed ledger-presentation checksum for canonical partners-capital resolution.");
        }

        // Re-capture from the durable source rather than an in-memory cache so release/retry after a
        // process restart is still possible. The original checkpoint is immutable as-of; any source,
        // ownership, fund-structure, or report-pack drift therefore fails closed below.
        var recaptured = await ResolveSource()
            .CaptureAsync(
                parameters,
                new ReportAccessQueryContext(
                    ActorPrincipalId: "reporting-artifact-producer",
                    CompanyId: scope.CompanyId,
                    TenantId: scope.TenantId,
                    RequireBoundScope: true),
                new ReportingAuthoritativeSourceCaptureIntent(manifest.TemplateId)
                {
                    RequiresCertifiedLedgerPresentation =
                        ReportingCertifiedLedgerPresentationBinding.IsRequired(manifest)
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                recaptured.Checkpoint.CheckpointId,
                sourceCheckpoint.CheckpointId,
                StringComparison.Ordinal)
            || !string.Equals(
                recaptured.Checkpoint.CheckpointHash,
                sourceCheckpoint.CheckpointHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' canonical ledger presentation no longer matches its exact certified source checkpoint.");
        }

        var presentation = recaptured.CertifiedLedgerPresentation
            ?? throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' canonical partners-capital presentation is unavailable from the exact durable checkpoint.");
        var recapturedPresentationEvidence =
            ReportingCertifiedLedgerPresentationBinding.GetSingleEvidenceId(recaptured.Checkpoint);
        var resolvedPresentationEvidence =
            ReportingCertifiedLedgerPresentationBinding.BuildEvidenceId(presentation.ReportPack);
        if (recapturedPresentationEvidence is null
            || !string.Equals(
                recapturedPresentationEvidence,
                certifiedPresentationEvidence,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                resolvedPresentationEvidence,
                certifiedPresentationEvidence,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' canonical ledger presentation checksum changed after certification.");
        }

        return presentation;
    }

    private LedgerReportingAuthoritativeSource ResolveSource()
    {
        var journalStore = _services.GetService<ILedgerJournalStore>() as PostgresLedgerJournalStore;
        var tenancyRegistry =
            _services.GetService<IFundProfileTenancyRegistry>() as PostgresFundProfileTenancyRegistry;
        var fundStructure = _services.GetService<IFundStructureService>() as PostgresFundStructureService;
        if (journalStore is null || tenancyRegistry is null || fundStructure is null)
        {
            throw new ReportingAuthoritativeSourceUnavailableException(
                "The PostgreSQL ledger journal, fund tenancy registry, and fund structure authorities are required for certified reporting.");
        }

        return new LedgerReportingAuthoritativeSource(
            journalStore,
            tenancyRegistry,
            fundStructure,
            _services.GetService<TimeProvider>(),
            journalStore);
    }
}
