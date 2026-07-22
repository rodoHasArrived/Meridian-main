using Meridian.Wpf.Models;
using System.IO;
using System.Threading.Tasks;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Instruments.AssetOperations;
using Meridian.Reporting;
using Meridian.Storage;
using Meridian.Storage.Operations;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Meridian.Storage.Store;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Wpf.Features.Strategy;

public sealed class StrategyFeatureModule : IDesktopFeatureModule
{
    private static readonly WorkspaceCapabilityDescriptor Capability = ShellNavigationCatalog.BuildStrategyCapability();

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        RegisterStrategyWorkspaceServices(services);
        services.AddSingleton<AdvancedAnalyticsServiceBase>(_ => new AdvancedAnalyticsServiceBase());
        services.AddSingleton<WpfServices.RunMatService>(_ => WpfServices.RunMatService.Instance);
        services.AddTransient<RunMatViewModel>();
        services.AddTransient(sp => new StrategyRunBrowserViewModel(
            sp.GetRequiredService<WpfServices.StrategyRunWorkspaceService>(),
            sp.GetRequiredService<WpfServices.NavigationService>(),
            sp.GetRequiredService<WpfServices.WorkspaceService>()));
        services.AddTransient(sp => new StrategyRunDetailViewModel(
            sp.GetRequiredService<WpfServices.StrategyRunWorkspaceService>(),
            sp.GetRequiredService<WpfServices.NavigationService>()));
        services.AddTransient(sp => new StrategyRunPortfolioViewModel(
            sp.GetRequiredService<WpfServices.StrategyRunWorkspaceService>(),
            sp.GetRequiredService<WpfServices.NavigationService>()));
        services.AddTransient(sp => new StrategyRunLedgerViewModel(
            sp.GetRequiredService<WpfServices.StrategyRunWorkspaceService>(),
            sp.GetRequiredService<WpfServices.NavigationService>()));
        services.AddTransient<CashFlowViewModel>();
        services.AddSingleton<WpfServices.BacktestDataAvailabilityService>();
        services.TryAddSingleton<Func<BacktestRequest, BacktestEngine>>(sp =>
        {
            BacktestEngine CreateEngine(BacktestRequest request)
            {
                var storageOptions = new StorageOptions { RootPath = request.DataRoot };
                var catalogService = new StorageCatalogService(request.DataRoot, storageOptions);
                return new BacktestEngine(
                    sp.GetRequiredService<ILogger<BacktestEngine>>(),
                    catalogService,
                    sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                    sp.GetService<Meridian.Backtesting.ICorporateActionAdjustmentService>(),
                    sp.GetService<IBacktestPreflightService>());
            }

            return CreateEngine;
        });
        services.AddTransient<IBatchBacktestService>(sp => new BatchBacktestService(
            sp.GetRequiredService<ILogger<BatchBacktestService>>(),
            sp.GetRequiredService<Func<BacktestRequest, BacktestEngine>>()));
        services.AddTransient<Meridian.Backtesting.WalkForward.IWalkForwardService>(sp =>
            new Meridian.Backtesting.WalkForward.WalkForwardService(
                sp.GetRequiredService<ILogger<Meridian.Backtesting.WalkForward.WalkForwardService>>(),
                sp.GetRequiredService<IBatchBacktestService>(),
                sp.GetRequiredService<Func<BacktestRequest, BacktestEngine>>()));
        services.AddTransient<BacktestViewModel>();
        services.AddTransient<BatchBacktestViewModel>();
        services.AddTransient<ChartingPageViewModel>();
        services.AddTransient<TickerStripViewModel>();
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<Meridian.QuantScript.QuantScriptOptions>>(sp =>
        {
            var configService = sp.GetRequiredService<WpfServices.ConfigService>();
            var config = Task.Run(() => configService.LoadConfigAsync()).GetAwaiter().GetResult();
            var resolvedDataRoot = configService.ResolveDataRoot(config);
            return Microsoft.Extensions.Options.Options.Create(new Meridian.QuantScript.QuantScriptOptions
            {
                DefaultDataRoot = resolvedDataRoot,
                ScriptsDirectory = Path.Combine(AppContext.BaseDirectory, "scripts")
            });
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Meridian.QuantScript.QuantScriptOptions>>().Value;
            return new JsonlMarketDataStore(options.DefaultDataRoot);
        });
        services.AddSingleton<Meridian.QuantScript.Api.IQuantDataContext, Meridian.QuantScript.Api.QuantDataContext>();
        services.AddSingleton<Meridian.QuantScript.Plotting.PlotQueue>();
        services.AddSingleton<Meridian.QuantScript.Compilation.IQuantScriptCompiler, Meridian.QuantScript.Compilation.RoslynScriptCompiler>();
        services.AddSingleton<Meridian.QuantScript.Compilation.IScriptRunner, Meridian.QuantScript.Compilation.ScriptRunner>();
        services.AddSingleton<Meridian.QuantScript.Documents.IQuantScriptNotebookStore>(sp =>
            new Meridian.QuantScript.Documents.QuantScriptNotebookStore(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Meridian.QuantScript.QuantScriptOptions>>().Value));
        services.AddSingleton<WpfServices.IQuantScriptLayoutService, WpfServices.QuantScriptLayoutService>();
        services.AddSingleton<WpfServices.QuantScriptTemplateCatalogService>();
        services.AddSingleton<WpfServices.QuantScriptExecutionHistoryService>();
        services.AddTransient<QuantScriptViewModel>();
    }

    private static void RegisterStrategyWorkspaceServices(IServiceCollection services)
    {
        services.TryAddSingleton<IFactorPaydownProjectionService, FactorPaydownProjectionService>();
        var securityMasterConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(securityMasterConnectionString))
        {
            services.AddSingleton(sp => new SecurityMasterOptions
            {
                ConnectionString = securityMasterConnectionString,
                Schema = Environment.GetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_SCHEMA") ?? "security_master",
                SnapshotIntervalVersions = ParseInt("MERIDIAN_SECURITY_MASTER_SNAPSHOT_INTERVAL", 50),
                ProjectionReplayBatchSize = ParseInt("MERIDIAN_SECURITY_MASTER_REPLAY_BATCH_SIZE", 500),
                PreloadProjectionCache = ParseBool("MERIDIAN_SECURITY_MASTER_PRELOAD_CACHE", true),
                ResolveInactiveByDefault = ParseBool("MERIDIAN_SECURITY_MASTER_RESOLVE_INACTIVE", true)
            });
            services.AddSingleton<ISecurityMasterEventStore, PostgresSecurityMasterEventStore>();
            services.AddSingleton<ISecurityMasterSnapshotStore, PostgresSecurityMasterSnapshotStore>();
            services.AddSingleton<ISecurityMasterStore, PostgresSecurityMasterStore>();
            services.AddSingleton<SecurityMasterAggregateRebuilder>();
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterService, SecurityMasterService>();
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterAmender>(sp =>
                (Meridian.Contracts.SecurityMaster.ISecurityMasterAmender)sp.GetRequiredService<Meridian.Contracts.SecurityMaster.ISecurityMasterService>());
            services.AddSingleton<SecurityMasterQueryService>();
            services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(sp => sp.GetRequiredService<SecurityMasterQueryService>());
            services.AddSingleton<ISecurityMasterRuntimeStatus>(_ => new WpfServices.SecurityMasterRuntimeStatusService(
                isAvailable: true,
                availabilityDescription: "Security Master runtime is configured for workstation workflows."));
            services.AddSingleton<SecurityMasterCsvParser>();
            services.AddSingleton<ISecurityMasterImportService, SecurityMasterImportService>();
            services.AddSingleton<ISecurityResolver, SecurityResolver>();
            services.AddSingleton<Meridian.Backtesting.CorporateActionAdjustmentService>();
            services.AddSingleton<Meridian.Backtesting.ICorporateActionAdjustmentService>(
                sp => sp.GetRequiredService<Meridian.Backtesting.CorporateActionAdjustmentService>());
            services.AddSingleton<Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster>(
                sp => sp.GetRequiredService<Meridian.Backtesting.CorporateActionAdjustmentService>());
            services.AddSingleton<ITradingParametersBackfillService, TradingParametersBackfillService>();
        }
        else
        {
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterService, NullSecurityMasterService>();
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterAmender>(sp =>
                (Meridian.Contracts.SecurityMaster.ISecurityMasterAmender)sp.GetRequiredService<Meridian.Contracts.SecurityMaster.ISecurityMasterService>());
            services.AddSingleton<NullSecurityMasterQueryService>();
            services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(sp =>
                sp.GetRequiredService<NullSecurityMasterQueryService>());
            services.AddSingleton<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(sp =>
                sp.GetRequiredService<NullSecurityMasterQueryService>());
            services.AddSingleton<ISecurityMasterRuntimeStatus>(sp =>
                sp.GetRequiredService<NullSecurityMasterQueryService>());
            services.AddSingleton<ISecurityMasterImportService, NullSecurityMasterImportService>();
            services.AddSingleton<ITradingParametersBackfillService, NullTradingParametersBackfillService>();
        }

        services.AddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        services.AddSingleton<IBacktestPreflightService, BacktestPreflightService>();
        services.AddSingleton(sp =>
        {
            var svc = WpfServices.BacktestService.Instance;
            svc.SecurityMasterQueryService = sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
            svc.CorporateActionAdjustmentService = sp.GetService<Meridian.Backtesting.ICorporateActionAdjustmentService>();
            svc.BacktestPreflightService = sp.GetService<IBacktestPreflightService>();
            return svc;
        });
        services.TryAddSingleton<IOperationalCaseHistoryStore>(sp =>
        {
            var configService = sp.GetRequiredService<WpfServices.ConfigService>();
            var config = Task.Run(() => configService.LoadConfigAsync()).GetAwaiter().GetResult();
            return new FileOperationalCaseHistoryStore(configService.ResolveDataRoot(config));
        });
        services.AddSingleton<IStrategyRepository>(sp =>
            new StrategyRunStore(sp.GetRequiredService<IOperationalCaseHistoryStore>()));
        services.AddSingleton<PromotionRecordStoreOptions>(sp =>
        {
            var configService = sp.GetRequiredService<WpfServices.ConfigService>();
            var config = Task.Run(() => configService.LoadConfigAsync()).GetAwaiter().GetResult();
            var resolvedDataRoot = configService.ResolveDataRoot(config);
            return new PromotionRecordStoreOptions(Path.Combine(resolvedDataRoot, "strategies", "promotions"));
        });
        services.AddSingleton<IPromotionRecordStore, JsonlPromotionRecordStore>();
        services.AddSingleton<PortfolioReadService>();
        services.AddSingleton<LedgerReadService>();
        services.AddSingleton<StrategyRunReadService>();
        services.AddSingleton<IReconciliationRunRepository>(sp =>
        {
            var configService = sp.GetRequiredService<WpfServices.ConfigService>();
            var config = Task.Run(() => configService.LoadConfigAsync()).GetAwaiter().GetResult();
            var resolvedDataRoot = configService.ResolveDataRoot(config);
            return new FileReconciliationRunRepository(
                Path.Combine(resolvedDataRoot, "workstation"),
                sp.GetRequiredService<ILogger<FileReconciliationRunRepository>>());
        });
        services.AddSingleton<ReconciliationProjectionService>();
        services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        services.AddSingleton<CashFlowProjectionService>();
        services.AddSingleton<StrategyRunContinuityService>();
        services.AddSingleton(BrokeragePortfolioSyncOptions.Default);
        services.AddSingleton<BrokeragePortfolioSyncService>();
        services.AddSingleton(Dk1TrustGateReadinessOptions.Default);
        services.AddSingleton<Dk1TrustGateReadinessService>();
        services.AddSingleton<TradingOperatorReadinessService>();
        services.AddSingleton<StrategyRunReviewPacketService>();
        services.AddWorkflowLibrary();
        services.AddSingleton<WorkstationWorkflowSummaryService>();
        services.AddSingleton<WpfServices.IWorkstationStrategyBriefingApiClient, WpfServices.WorkstationStrategyBriefingApiClient>();
        services.AddSingleton<WpfServices.IWorkstationOperatorInboxApiClient, WpfServices.WorkstationOperatorInboxApiClient>();
        services.AddSingleton<WpfServices.IStrategyBriefingWorkspaceService, WpfServices.StrategyBriefingWorkspaceService>();
        services.AddSingleton<Meridian.Strategies.Promotions.BacktestToLivePromoter>();
        services.AddSingleton<PromotionService>();
        services.AddSingleton<NavAttributionService>();
        services.AddSingleton<ReportGenerationService>();
        services.AddSingleton<FundOperationsWorkspaceReadService>();
        services.AddSingleton<WpfServices.ISecurityMasterOperatorWorkflowClient, WpfServices.SecurityMasterOperatorWorkflowClient>();
        services.AddSingleton<WpfServices.StrategyRunWorkspaceService>(sp =>
        {
            var service = new WpfServices.StrategyRunWorkspaceService(
                sp.GetRequiredService<IStrategyRepository>(),
                sp.GetRequiredService<StrategyRunReadService>(),
                sp.GetService<BrokerageConfiguration>());
            WpfServices.StrategyRunWorkspaceService.SetInstance(service);
            return service;
        });
    }

    private static int ParseInt(string name, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;

    private static bool ParseBool(string name, bool defaultValue)
        => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Capability.Pages;

    public WorkspaceCapabilityDescriptor DescribeWorkspace() => Capability;
}
