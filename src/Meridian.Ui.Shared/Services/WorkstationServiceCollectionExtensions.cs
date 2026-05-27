using Meridian.Application.Config.Credentials;
using Meridian.Application.Backtesting;
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
using Meridian.Contracts.Workstation;
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
        services.TryAddSingleton<UserProfileRegistry>();
        services.TryAddSingleton<LoginSessionService>();
        services.TryAddSingleton<IOperatorInboxService, InMemoryOperatorInboxService>();
        services.TryAddSingleton<FeatureCapabilitySettingsService>();
        services.TryAddSingleton<IFundAccountTraversalQueryService, FundAccountTraversalQueryService>();

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
        services.TryAddSingleton<CashFlowProjectionService>();
        services.TryAddSingleton<StrategyRunContinuityService>();
        services.TryAddSingleton<IBacktestPreflightService, BacktestPreflightService>();

        services.TryAddSingleton(BrokerageConnectionOptions.RobinhoodFromEnvironment());
        services.TryAddSingleton<BrokerageConnectionService>();
        services.TryAddSingleton<AlpacaBrokerageConnectionService>();
        services.TryAddSingleton<ProviderConnectionLifecycleService>();
        services.TryAddSingleton(BrokeragePortfolioSyncOptions.Default);
        services.TryAddSingleton<BrokeragePortfolioSyncService>();

        services.TryAddSingleton(Dk1TrustGateReadinessOptions.Default);
        services.TryAddSingleton<Dk1TrustGateReadinessService>();
        services.TryAddSingleton<TradingOperatorReadinessService>();
        services.TryAddSingleton<RiskRuleRuntimeService>();
        services.TryAddSingleton<StrategyRunReviewPacketService>();
        services.TryAddSingleton<BacktestToLivePromoter>();
        services.TryAddSingleton<PromotionService>();
        services.TryAddSingleton<ISecurityMasterWorkbenchQueryService, SecurityMasterWorkbenchQueryService>();
        services.TryAddSingleton<NavAttributionService>();
        services.TryAddSingleton<ReportGenerationService>();
        services.TryAddSingleton<ReportPackValidationService>();
        services.TryAddSingleton<IGovernanceReportPackRepository>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileGovernanceReportPackRepository>>();
            return new FileGovernanceReportPackRepository(ResolveWorkstationDataDirectory(sp), logger);
        });
        services.TryAddSingleton<FundOperationsWorkspaceReadService>();
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
                sp.GetService<IOperationsContinuityTransactionalCommitStore>()));

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
        services.TryAddSingleton<ReconciliationProjectionService>();
        services.TryAddSingleton<IReconciliationRunService, ReconciliationRunService>();
        services.TryAddSingleton<IOperationsContinuityReconciliationBridge>(sp =>
            new OperationsContinuityReconciliationBridge(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IReconciliationRunService>()));

        services.AddWorkflowLibrary();
        services.AddEvidenceWorkflowFabric();
        services.TryAddSingleton<WorkstationWorkflowSummaryService>();
        services.AddCoveredCallBacktestServices();

        return services;
    }

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
