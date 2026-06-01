using Meridian.Application.Config.Credentials;
using Meridian.Application.Auth;
using Meridian.Application.Backtesting;
using Meridian.Application.Compliance;
using Meridian.Application.FundStructure;
using Meridian.Application.OperationsContinuity;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Plaid;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Adapters.Plaid;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
using Meridian.Storage.Ledger;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services.Acceptance;
using Meridian.Ui.Shared.Services.CoveredCall;
using Meridian.Ui.Shared.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddSingleton<ConfigStore>(sp =>
        {
            var core = sp.GetRequiredService<Meridian.Application.UI.ConfigStore>();
            return new ConfigStore(core.ConfigPath);
        });

        // The UI coordinator wraps the core coordinator and adds preview support for workstation flows.
        services.AddSingleton<BackfillCoordinator>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var registry = sp.GetService<ProviderRegistry>();
            var factory = sp.GetService<ProviderFactory>();
            return new BackfillCoordinator(configStore, registry, factory);
        });

        services.AddHttpClient();
        services.AddMemoryCache();
        services.TryAddSingleton<IRolePermissionProfileStore, FileRolePermissionProfileStore>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING")))
        {
            services.TryAddSingleton(new ScopedAccessStoreOptions
            {
                ConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING")!,
                Schema = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA") ?? "identity_access"
            });
            services.TryAddSingleton<IScopedAccessAssignmentStore>(sp =>
            {
                var store = new PostgresScopedAccessAssignmentStore(sp.GetRequiredService<ScopedAccessStoreOptions>());
                store.EnsureMigratedAsync(CancellationToken.None).GetAwaiter().GetResult();
                return store;
            });
        }
        else
        {
            services.TryAddSingleton<IScopedAccessAssignmentStore>(sp =>
            {
                var storageOptions = sp.GetRequiredService<StorageOptions>();
                var persistencePath = Path.Combine(storageOptions.RootPath, "governance", "user-access-assignments.json");
                return new FileScopedAccessAssignmentStore(persistencePath);
            });
        }
        services.TryAddSingleton<ScopedAccessService>();
        services.TryAddSingleton<IScopedAccessAssignmentService>(sp => sp.GetRequiredService<ScopedAccessService>());
        services.TryAddSingleton<IScopedAuthorizationService>(sp => sp.GetRequiredService<ScopedAccessService>());
        services.TryAddSingleton<UserProfileRegistry>();
        services.TryAddSingleton<LoginSessionService>();
        services.TryAddSingleton<IOperatorInboxService, InMemoryOperatorInboxService>();
        services.TryAddSingleton<FeatureCapabilitySettingsService>();
        services.TryAddSingleton<SensitiveActionPolicyEngine>();
        services.TryAddSingleton<ImmutableAuditLogService>();
        services.TryAddSingleton<AccessReviewService>();
        services.TryAddSingleton<IFundAccountTraversalQueryService, FundAccountTraversalQueryService>();
        services.TryAddSingleton<FundStructureSetupWorkflowService>();

        services.TryAddSingleton<IStrategyRepository, StrategyRunStore>();
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
        services.TryAddSingleton<LedgerReadService>();
        services.TryAddSingleton<StrategyRunReadService>();
        services.TryAddSingleton<StrategyRunComparisonService>();
        services.TryAddSingleton<AuditTrailExplorerService>();
        services.TryAddSingleton<CashFlowProjectionService>();
        services.TryAddSingleton<StrategyRunContinuityService>();
        services.TryAddSingleton<IBacktestPreflightService, BacktestPreflightService>();

        services.TryAddSingleton(BrokerageConnectionOptions.RobinhoodFromEnvironment());
        services.TryAddSingleton<BrokerageConnectionService>();
        services.TryAddSingleton<AlpacaBrokerageConnectionService>();
        services.TryAddSingleton<ProviderConnectionLifecycleService>();
        services.TryAddSingleton(ResolvePlaidOptions);
        services.TryAddSingleton<IPlaidConnectionRepository>(sp =>
            new FilePlaidConnectionRepository(ResolveWorkstationDataDirectory(sp)));
        services.TryAddSingleton<IPlaidClient>(sp =>
            new PlaidHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlaidHttpClient))));
        services.TryAddSingleton<PlaidWorkstationService>();
        services.TryAddSingleton<IPlaidIngestionService>(sp => sp.GetRequiredService<PlaidWorkstationService>());
        services.TryAddSingleton<IPlaidTransferService>(sp => sp.GetRequiredService<PlaidWorkstationService>());
        services.TryAddSingleton(BrokeragePortfolioSyncOptions.Default);
        services.TryAddSingleton<BrokeragePortfolioSyncService>();
        services.TryAddSingleton<ProviderLedgerReconciliationService>();
        services.TryAddSingleton<FundAccountCloseReadinessService>();

        services.TryAddSingleton<ICashSyncOrchestrationService, CashSyncOrchestrationService>();

        services.TryAddSingleton(Dk1TrustGateReadinessOptions.Default);
        services.TryAddSingleton<Dk1TrustGateReadinessService>();
        services.TryAddSingleton<TradingOperatorReadinessService>();
        services.TryAddSingleton<CollateralExposureService>();
        services.TryAddSingleton<RiskRuleRuntimeService>();
        services.TryAddSingleton<StrategyRunReviewPacketService>();
        services.TryAddSingleton<BacktestToLivePromoter>();
        services.TryAddSingleton<PromotionService>();
        services.TryAddSingleton<ISecurityMasterWorkbenchQueryService, SecurityMasterWorkbenchQueryService>();
        services.TryAddSingleton<NavAttributionService>();
        services.TryAddSingleton<ReportGenerationService>();
        services.TryAddSingleton<InvestmentAccountingTransactionLabService>();
        services.TryAddSingleton<ReportPackValidationService>();
        services.TryAddSingleton<ReportTemplateRegistryService>();
        services.TryAddSingleton<ReportPackWorkflowService>();
        services.TryAddSingleton<W4AcceptanceFilter>();
        services.TryAddSingleton<IGovernanceReportPackRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileGovernanceReportPackRepository>>();
            return new FileGovernanceReportPackRepository(ResolveWorkstationDataDirectory(sp), logger);
        });
        services.TryAddSingleton<LedgerAmountProvenanceService>();
        services.TryAddSingleton<FundOperationsWorkspaceReadService>();
        services.TryAddSingleton(sp => new FamilyOfficeReadService(
            sp.GetService<IFundStructureService>(),
            sp.GetService<Meridian.Application.FundAccounts.IFundAccountService>(),
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

        services.TryAddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        services.TryAddSingleton<IStrategyLedgerReconciliationSourceAdapter, StrategyLedgerReconciliationSourceAdapter>();
        services.TryAddSingleton<IStrategyPortfolioReconciliationSourceAdapter, StrategyPortfolioReconciliationSourceAdapter>();
        services.TryAddSingleton<IInternalCashReconciliationSourceAdapter, BankInternalCashReconciliationSourceAdapter>();
        services.TryAddSingleton<IExternalStatementSource, NullExternalStatementSource>();
        services.TryAddSingleton<IExternalStatementReconciliationSourceAdapter, ExternalStatementReconciliationSourceAdapter>();
        services.TryAddSingleton<ISecurityMasterAccountingEventService, SecurityMasterAccountingEventService>();
        services.TryAddSingleton<ISecurityMasterAccountingEventSourceAdapter>(sp =>
            new SecurityMasterAccountingEventSourceAdapter(sp.GetService<ContractSecurityMasterQueryService>()));
        services.TryAddSingleton<IReconciliationBreakQueueRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileReconciliationBreakQueueRepository>>();
            return new FileReconciliationBreakQueueRepository(ResolveWorkstationDataDirectory(sp), logger);
        });
        services.TryAddSingleton<SecurityMasterExceptionCaseworkService>(sp =>
            new SecurityMasterExceptionCaseworkService(
                sp.GetService<IReconciliationBreakQueueRepository>(),
                sp.GetRequiredService<ILogger<SecurityMasterExceptionCaseworkService>>()));
        services.TryAddSingleton<ReconciliationProjectionService>();
        services.TryAddSingleton<IReconciliationRunService, ReconciliationRunService>();
        services.TryAddSingleton<IOperationsContinuityReconciliationBridge>(sp =>
            new OperationsContinuityReconciliationBridge(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IReconciliationRunService>()));
        services.TryAddSingleton<CollateralIngestionBuffer>();
        services.TryAddSingleton<CollateralExposureService>();

        services.AddWorkflowLibrary();
        services.AddEvidenceWorkflowFabric();
        services.TryAddSingleton<WorkstationWorkflowSummaryService>();
        services.TryAddSingleton<Meridian.Ui.Shared.Contracts.Integrations.IOmsIntegrationApiHandler, OmsIntegrationService>();
        services.AddCoveredCallBacktestServices();

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
            loggerFactory: sp.GetRequiredService<ILoggerFactory>()));
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
