using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Meridian.Application.EnvironmentDesign;
using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.QuantScript;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Storage;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Meridian.Storage.Store;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Services;
using Meridian.Ui.Services.DataQuality;
using Meridian.Ui.Services.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf;

/// <summary>
/// Meridian WPF Application
/// Provides maximum stability through WPF (.NET 9) for Windows-only deployment.
/// </summary>
public partial class App : System.Windows.Application
{
    private static bool _isFirstRun;
    private static bool _isFixtureMode;
    private static string[] _launchArgs = [];
    private IHost? _host;

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Gets whether the application is running in fixture mode (offline mock data).
    /// Activated via --fixture command-line argument or MDC_FIXTURE_MODE=1 environment variable.
    /// </summary>
    public static bool IsFixtureMode => _isFixtureMode;

    /// <summary>
    /// Gets the main application window.
    /// </summary>
    public static new MainWindow? MainWindow => Current.MainWindow as MainWindow;

    /// <summary>
    /// Gets whether this is the first run of the application.
    /// </summary>
    public static bool IsFirstRun => _isFirstRun;

    /// <summary>
    /// Gets the command-line arguments passed at launch.
    /// Used by <see cref="MainWindow"/> to handle jump-list and deep-link args.
    /// </summary>
    public static string[] GetLaunchArgs() => _launchArgs;

    /// <summary>
    /// Gets the notification service instance.
    /// </summary>
    public static WpfServices.NotificationService Notifications => WpfServices.NotificationService.Instance;

    /// <summary>
    /// Gets the connection service instance.
    /// </summary>
    public static WpfServices.ConnectionService Connection => WpfServices.ConnectionService.Instance;

    /// <summary>
    /// Gets the theme service instance.
    /// </summary>
    public static WpfServices.ThemeService Theme => WpfServices.ThemeService.Instance;

    /// <summary>
    /// Gets the offline tracking persistence service instance.
    /// </summary>
    public static WpfServices.OfflineTrackingPersistenceService OfflineTracking => WpfServices.OfflineTrackingPersistenceService.Instance;

    /// <summary>
    /// Gets the background task scheduler service instance.
    /// </summary>
    public static WpfServices.BackgroundTaskSchedulerService Scheduler => WpfServices.BackgroundTaskSchedulerService.Instance;

    /// <summary>
    /// Gets the pending operations queue service instance.
    /// </summary>
    public static WpfServices.PendingOperationsQueueService OperationsQueue => WpfServices.PendingOperationsQueueService.Instance;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _launchArgs = e.Args;
        var launchRequest = DesktopLaunchArguments.Parse(e.Args);
        if (launchRequest.HasActions && WpfServices.SingleInstanceService.TrySendArgsToPrimary(e.Args, timeoutMs: 5000))
        {
            Shutdown();
            return;
        }

        // Register the AppUserModelID before any window is shown so that the
        // Windows shell (taskbar, JumpList, toast activations) maps all
        // notifications back to this process identity.
        WpfServices.ToastNotificationService.SetAppUserModelId();
        Meridian.Ui.Services.ConfigService.DefaultPathResolver =
            () => WpfServices.FirstRunService.Instance.ConfigFilePath;
        Meridian.Application.UI.ConfigStore.DefaultPathResolver =
            () => WpfServices.FirstRunService.Instance.ConfigFilePath;

        // Enforce single instance: if another Meridian window is already running,
        // forward the launch args to it via named pipe and exit cleanly.
        if (!WpfServices.SingleInstanceService.Instance.TryAcquire())
        {
            WpfServices.SingleInstanceService.SendArgsToPrimary(e.Args);
            Shutdown();
            return;
        }

        // Detect fixture mode from --fixture arg or MDC_FIXTURE_MODE env var
        _isFixtureMode = DetectFixtureMode(e.Args);

        // Configure the host with dependency injection
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();

        Services = _host.Services;
        Services.GetRequiredService<WpfServices.StrategyRunWorkspaceService>();

        // Provide the DI container to NavigationService so it can resolve pages
        WpfServices.NavigationService.Instance.SetServiceProvider(Services);

        // Handle unhandled exceptions gracefully
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Create and show MainWindow from DI (replaces StartupUri)
        var mainWindow = Services.GetRequiredService<MainWindow>();
        Current.MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.ForceStartupWindowRecovery();

        // Begin listening for args forwarded from secondary instances as soon as
        // the main window exists so automation deep links do not race startup work.
        WpfServices.SingleInstanceService.Instance.StartListening();

        // Register taskbar jump list tasks.
        WpfServices.JumpListService.Instance.Register();

        // Fire-and-forget async initialization with proper exception handling
        await SafeOnStartupAsync();
        EnsureMainWindowVisible(mainWindow);
        _ = RestoreMainWindowVisibilityAsync(mainWindow);
    }

    /// <summary>
    /// Detects whether fixture mode should be activated.
    /// Checks for --fixture command-line argument or MDC_FIXTURE_MODE=1 environment variable.
    /// </summary>
    private static bool DetectFixtureMode(string[] args)
    {
        // Check command-line argument
        if (Array.Exists(args, arg => string.Equals(arg, "--fixture", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check environment variable
        var envValue = Environment.GetEnvironmentVariable("MDC_FIXTURE_MODE");
        return string.Equals(envValue, "1", StringComparison.Ordinal)
            || string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Configures services for dependency injection.
    /// C1: DI-first registration — services registered by interface where possible.
    /// Pages registered as transient for constructor injection via NavigationService.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Register shared desktop HttpClient configurations
        services.AddDesktopHttpClients();

        // ILogger<T> infrastructure — must be first so all services can resolve loggers
        services.AddLogging();

        // Shared API infrastructure
        services.AddSingleton<ApiClientService>(_ => ApiClientService.Instance);

        // ── Fixture mode service (offline mock data) ────────────────────────
        services.AddSingleton<Meridian.Ui.Services.Services.FixtureDataService>(_ => Meridian.Ui.Services.Services.FixtureDataService.Instance);
        services.AddSingleton<Meridian.Ui.Services.Services.FixtureModeDetector>(_ => Meridian.Ui.Services.Services.FixtureModeDetector.Instance);

        // ── Core services (by interface + concrete type) ────────────────────
        services.AddSingleton<IConnectionService>(_ => WpfServices.ConnectionService.Instance);
        services.AddSingleton<WpfServices.ConnectionService>(_ => WpfServices.ConnectionService.Instance);

        services.AddSingleton<INavigationService>(_ => WpfServices.NavigationService.Instance);
        services.AddSingleton<WpfServices.NavigationService>(_ => WpfServices.NavigationService.Instance);

        services.AddSingleton<Meridian.Ui.Services.Contracts.ILoggingService>(_ => WpfServices.LoggingService.Instance);
        services.AddSingleton<WpfServices.LoggingService>(_ => WpfServices.LoggingService.Instance);

        services.AddSingleton<WpfServices.ConfigService>(_ => WpfServices.ConfigService.Instance);
        services.AddSingleton<WpfServices.ThemeService>(_ => WpfServices.ThemeService.Instance);
        services.AddSingleton<WpfServices.NotificationService>(_ => WpfServices.NotificationService.Instance);
        services.AddSingleton<WpfServices.KeyboardShortcutService>(_ => WpfServices.KeyboardShortcutService.Instance);
        services.AddSingleton<WpfServices.MessagingService>(_ => WpfServices.MessagingService.Instance);
        services.AddSingleton<WpfServices.ApiStatusService>();
        services.AddSingleton<IStatusService>(sp => sp.GetRequiredService<WpfServices.ApiStatusService>());
        services.AddSingleton<WpfServices.StatusService>(_ => WpfServices.StatusService.Instance);
        services.AddSingleton<WpfServices.FirstRunService>(_ => WpfServices.FirstRunService.Instance);

        // ── Onboarding / workspace services ──────────────────────────────────
        services.AddSingleton<Meridian.Ui.Services.OnboardingTourService>(_ => Meridian.Ui.Services.OnboardingTourService.Instance);
        services.AddSingleton<WpfServices.WorkspaceService>(_ => WpfServices.WorkspaceService.Instance);
        services.AddSingleton<Meridian.Ui.Services.AlertService>(_ => Meridian.Ui.Services.AlertService.Instance);
        services.AddSingleton<WpfServices.FundContextService>(_ => WpfServices.FundContextService.Instance);
        services.AddSingleton<WpfServices.IFundProfileCatalog>(sp => sp.GetRequiredService<WpfServices.FundContextService>());
        services.AddSingleton(sp => new InMemoryFundAccountService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "fund-accounts.json")));
        services.AddSingleton<IFundAccountService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IFundStructureService>(sp => new InMemoryFundStructureService(
            sp.GetRequiredService<IFundAccountService>(),
            sharedDataAccessService: null,
            securityMasterQueryService: sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
            persistencePath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "fund-structure.json")));
        services.AddSingleton<EnvironmentDesignerService>(_ => new EnvironmentDesignerService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "environment-designer.json")));
        services.AddSingleton<IEnvironmentDesignService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<IEnvironmentValidationService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<IEnvironmentPublishService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<IEnvironmentRuntimeProjectionService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<WpfServices.WorkstationOperatingContextService>();
        services.AddSingleton<WpfServices.WorkspaceShellContextService>();
        services.AddSingleton<Meridian.Application.UI.ConfigStore>();

        // ── Domain / feature services ───────────────────────────────────────
        services.AddSingleton<WpfServices.BackendServiceManager>(_ => WpfServices.BackendServiceManager.Instance);
        services.AddSingleton<WpfServices.WatchlistService>(_ => WpfServices.WatchlistService.Instance);
        services.AddSingleton<WpfServices.IWatchlistReader>(sp => sp.GetRequiredService<WpfServices.WatchlistService>());
        services.AddSingleton<WpfServices.ArchiveHealthService>(_ => WpfServices.ArchiveHealthService.Instance);
        services.AddSingleton<WpfServices.SchemaService>(_ => WpfServices.SchemaService.Instance);
        services.AddSingleton<WpfServices.RunMatService>(_ => WpfServices.RunMatService.Instance);
        services.AddSingleton<ProviderManagementService>(_ => ProviderManagementService.Instance);
        services.AddSingleton<AdminMaintenanceServiceBase>(_ => AdminMaintenanceServiceBase.Instance);
        services.AddSingleton<AdvancedAnalyticsServiceBase>(_ => new AdvancedAnalyticsServiceBase());
        services.AddSingleton<SearchService>(_ => SearchService.Instance);
        services.AddSingleton<WpfServices.FundAccountReadService>();
        services.AddSingleton<WpfServices.FundLedgerReadService>();
        services.AddSingleton<WpfServices.ReconciliationReadService>();
        services.AddSingleton<WpfServices.CashFinancingReadService>();
        services.AddSingleton<WpfServices.IWorkstationReconciliationApiClient, WpfServices.WorkstationReconciliationApiClient>();
        services.AddSingleton<WpfServices.IWorkstationSecurityMasterApiClient, WpfServices.WorkstationSecurityMasterApiClient>();
        services.AddSingleton<WpfServices.IWorkstationResearchBriefingApiClient, WpfServices.WorkstationResearchBriefingApiClient>();
        services.AddSingleton<WpfServices.IWorkstationOperatorInboxApiClient, WpfServices.WorkstationOperatorInboxApiClient>();
        services.AddSingleton<WpfServices.IResearchBriefingWorkspaceService, WpfServices.ResearchBriefingWorkspaceService>();
        services.AddSingleton<WpfServices.IFundReconciliationWorkbenchService, WpfServices.FundReconciliationWorkbenchService>();

        // ── AI Agent service (local Ollama) ──────────────────────────────────
        services.AddSingleton<WpfServices.IAgentLoopService, WpfServices.AgentLoopService>();

        // ── Data quality shared services ─────────────────────────────────────
        services.AddSingleton<IDataQualityApiClient, DataQualityApiClient>();
        services.AddSingleton<IDataQualityPresentationService, DataQualityPresentationService>();
        services.AddTransient<IDataQualityRefreshService, DataQualityRefreshService>();
        RegisterStrategyWorkspaceServices(services);

        // ── Background / infrastructure services ────────────────────────────
        services.AddSingleton<WpfServices.BackgroundTaskSchedulerService>(_ => WpfServices.BackgroundTaskSchedulerService.Instance);
        services.AddSingleton<WpfServices.OfflineTrackingPersistenceService>(_ => WpfServices.OfflineTrackingPersistenceService.Instance);
        services.AddSingleton<WpfServices.PendingOperationsQueueService>(_ => WpfServices.PendingOperationsQueueService.Instance);
        services.AddSingleton<WpfServices.ToastNotificationService>(_ => WpfServices.ToastNotificationService.Instance);
        // C1 fix: register a single SystemTrayService instance under the interface contract;
        //         the concrete type is resolved via the same singleton.
        services.AddSingleton<WpfServices.SystemTrayService>();
        services.AddSingleton<WpfServices.ISystemTrayService>(sp => sp.GetRequiredService<WpfServices.SystemTrayService>());

        // ── MainWindow ──────────────────────────────────────────────────────
        services.AddSingleton<Meridian.Wpf.ViewModels.MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        // ── Catalog-driven WPF shell pages and shell services ───────────────
        services.AddMeridianWpfShell();

        // ── Additional pages not yet catalog-backed ─────────────────────────
        services.AddTransient<FundProfileSelectionPage>();
        services.AddTransient<Meridian.Ui.Services.DataCalendarService>();
        services.AddTransient<Meridian.Wpf.ViewModels.SecurityMasterViewModel>();
        services.AddTransient<PluginManagementPage>();
        services.AddTransient<AgentPage>();
        services.AddTransient<QualityArchivePage>();

        services.AddTransient<ClusterStatusPage>();

        // ── Backtesting service ──────────────────────────────────────────────
        // Registered in RegisterStrategyWorkspaceServices so optional Security Master
        // collaborators can be attached when that feature is enabled.

        // ── Ui.Services singletons accessed via DI (no static .Instance in pages) ──
        services.AddSingleton<BackfillProviderConfigService>(_ => BackfillProviderConfigService.Instance);
        services.AddSingleton<BackfillCheckpointService>(_ => BackfillCheckpointService.Instance);
        services.AddSingleton<BackfillApiService>();
        services.AddSingleton<Meridian.Ui.Services.CollectionSessionService>(_ => Meridian.Ui.Services.CollectionSessionService.Instance);
        services.AddSingleton<Meridian.Ui.Services.ScheduleManagerService>(_ => Meridian.Ui.Services.ScheduleManagerService.Instance);
        services.AddSingleton<WpfServices.StorageService>(_ => WpfServices.StorageService.Instance);
        services.AddSingleton<BatchExportSchedulerService>();
        services.AddSingleton<Meridian.Ui.Services.ActivityFeedService>(_ => Meridian.Ui.Services.ActivityFeedService.Instance);
        services.AddSingleton<Meridian.Ui.Services.CommandPaletteService>(_ => Meridian.Ui.Services.CommandPaletteService.Instance);
        services.AddSingleton<Meridian.Ui.Services.SymbolManagementService>(_ => Meridian.Ui.Services.SymbolManagementService.Instance);
        services.AddSingleton<Meridian.Ui.Services.BackfillService>(_ => Meridian.Ui.Services.BackfillService.Instance);
        services.AddSingleton<WpfServices.TaskbarProgressService>(_ => WpfServices.TaskbarProgressService.Instance);
        services.AddSingleton<WpfServices.TearOffPanelService>(_ => WpfServices.TearOffPanelService.Instance);

        // ── ViewModels (transient — new instance per page navigation) ────────
        services.AddTransient<Meridian.Wpf.ViewModels.BackfillViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.ProviderViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.DataQualityViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.FundProfileSelectionViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.FundAccountsViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.FundLedgerViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.RunMatViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunBrowserViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunDetailViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunPortfolioViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.StrategyRunLedgerViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.RunRiskViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.PluginManagementViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.AgentViewModel>();
        services.AddSingleton<Meridian.Wpf.Services.BacktestDataAvailabilityService>();
        services.AddTransient<IBatchBacktestService>(sp => new BatchBacktestService(
            sp.GetRequiredService<ILogger<BatchBacktestService>>(),
            request =>
            {
                var storageOptions = new StorageOptions { RootPath = request.DataRoot };
                var catalogService = new StorageCatalogService(request.DataRoot, storageOptions);
                return new BacktestEngine(
                    sp.GetRequiredService<ILogger<BacktestEngine>>(),
                    catalogService,
                    sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                    sp.GetService<Meridian.Backtesting.ICorporateActionAdjustmentService>(),
                    sp.GetService<IBacktestPreflightService>());
            }));
        services.AddTransient<Meridian.Wpf.ViewModels.BacktestViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.BatchBacktestViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.ChartingPageViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.TickerStripViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.WatchlistViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.SettingsViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.CollectionSessionViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.WorkflowLibraryViewModel>();

        // ── Credential management ────────────────────────────────────────────
        services.AddSingleton<WpfServices.CredentialService>();
        services.AddTransient<Meridian.Wpf.ViewModels.CredentialManagementViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.AccountPortfolioViewModel>();

        // ── Quality archive ──────────────────────────────────────────────────
        services.AddSingleton<Meridian.Ui.Services.Services.IQualityArchiveStore,
                              Meridian.Ui.Services.Services.QualityArchiveStore>();
        services.AddTransient<Meridian.Wpf.ViewModels.QualityArchiveViewModel>();

        // ── QuantScript services ─────────────────────────────────────────────
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<Meridian.QuantScript.QuantScriptOptions>>(sp =>
        {
            var configService = sp.GetRequiredService<WpfServices.ConfigService>();
            var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
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
            return new Meridian.Storage.Store.JsonlMarketDataStore(options.DefaultDataRoot);
        });
        services.AddSingleton<Meridian.QuantScript.Api.IQuantDataContext,
                              Meridian.QuantScript.Api.QuantDataContext>();
        services.AddSingleton<Meridian.QuantScript.Plotting.PlotQueue>();
        services.AddSingleton<Meridian.QuantScript.Compilation.IQuantScriptCompiler,
                              Meridian.QuantScript.Compilation.RoslynScriptCompiler>();
        services.AddSingleton<Meridian.QuantScript.Compilation.IScriptRunner,
                              Meridian.QuantScript.Compilation.ScriptRunner>();
        services.AddSingleton<Meridian.QuantScript.Documents.IQuantScriptNotebookStore>(sp =>
            new Meridian.QuantScript.Documents.QuantScriptNotebookStore(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Meridian.QuantScript.QuantScriptOptions>>().Value));
        services.AddSingleton<WpfServices.IQuantScriptLayoutService,
                              WpfServices.QuantScriptLayoutService>();
        services.AddSingleton<WpfServices.QuantScriptTemplateCatalogService>();
        services.AddSingleton<WpfServices.QuantScriptExecutionHistoryService>();
        services.AddTransient<Meridian.Wpf.ViewModels.QuantScriptViewModel>();

        // ── Plugin loader service ────────────────────────────────────────────
        services.AddSingleton<Meridian.Infrastructure.DataSources.DataSourceRegistry>();
        services.AddSingleton<Meridian.Application.Services.IPluginLoaderService,
                              Meridian.Application.Services.PluginLoaderService>();
    }

    private static void RegisterStrategyWorkspaceServices(IServiceCollection services)
    {
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

            // Security Master bulk import services
            services.AddSingleton<SecurityMasterCsvParser>();
            services.AddSingleton<ISecurityMasterImportService, SecurityMasterImportService>();

            // Corporate action adjustment for backtesting and live paper trading.
            services.AddSingleton<ISecurityResolver, SecurityResolver>();
            services.AddSingleton<Meridian.Backtesting.CorporateActionAdjustmentService>();
            services.AddSingleton<Meridian.Backtesting.ICorporateActionAdjustmentService>(
                sp => sp.GetRequiredService<Meridian.Backtesting.CorporateActionAdjustmentService>());
            services.AddSingleton<Meridian.Application.SecurityMaster.ILivePositionCorporateActionAdjuster>(
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

        // Wire optional Security Master collaborators into the BacktestService singleton when available.
        services.AddSingleton(sp =>
        {
            var svc = WpfServices.BacktestService.Instance;
            svc.SecurityMasterQueryService = sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
            svc.CorporateActionAdjustmentService = sp.GetService<Meridian.Backtesting.ICorporateActionAdjustmentService>();
            return svc;
        });

        services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        services.AddSingleton(PromotionRecordStoreOptions.Default);
        services.AddSingleton<IPromotionRecordStore, JsonlPromotionRecordStore>();
        services.AddSingleton<PortfolioReadService>();
        services.AddSingleton<LedgerReadService>();
        services.AddSingleton<StrategyRunReadService>();
        services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
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
        services.AddSingleton<Meridian.Strategies.Promotions.BacktestToLivePromoter>();
        services.AddSingleton<PromotionService>();
        services.AddSingleton<NavAttributionService>();
        services.AddSingleton<ReportGenerationService>();
        services.AddSingleton<FundOperationsWorkspaceReadService>();
        services.AddSingleton<ISecurityMasterOperatorWorkflowClient, SecurityMasterOperatorWorkflowClient>();
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

    /// <summary>
    /// Performs async initialization with proper exception handling.
    /// </summary>
    private async Task SafeOnStartupAsync(CancellationToken ct = default)
    {
        try
        {
            // Run first-time setup before showing window
            await InitializeFirstRunAsync();

            // Initialize and validate configuration
            await InitializeConfigurationAsync();

            // Initialize theme service
            if (Current.MainWindow is MainWindow mainWindow)
            {
                WpfServices.ThemeService.Instance.Initialize(mainWindow);

                // Initialize system tray integration
                var systemTrayService = Services.GetRequiredService<WpfServices.ISystemTrayService>();
                systemTrayService.Initialize(mainWindow);

                // Wire notifications to system tray balloons
                WireNotificationsTray(systemTrayService);

                // Wire connection status to tray icon
                WireConnectionStatusTray(systemTrayService);
            }

            // Start connection monitoring
            WpfServices.ConnectionService.Instance.StartMonitoring();

            // Initialize offline tracking persistence (handles recovery from crashes/restarts)
            await InitializeOfflineTrackingAsync();

            // Start background task scheduler
            await InitializeBackgroundServicesAsync();

            // Notify if running in fixture/demo mode
            if (_isFixtureMode)
            {
                WpfServices.LoggingService.Instance.LogInfo("Running in demo data mode using offline sample data");
                await WpfServices.NotificationService.Instance.NotifyAsync(
                    "Demo Data Active",
                    "Application is using sample data for offline review",
                    NotificationType.Info);
            }

            // Log successful startup
            WpfServices.LoggingService.Instance.LogInfo("Application started successfully");
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("Error during application startup", ex);

            try
            {
                await WpfServices.NotificationService.Instance.NotifyErrorAsync(
                    "Startup Error",
                    ex.Message);
            }
            catch (Exception notificationEx)
            {
                WpfServices.LoggingService.Instance.LogError(
                    "Failed to display startup error notification",
                    notificationEx);
            }
        }
    }

    /// <summary>
    /// Initializes offline tracking persistence and performs recovery if needed.
    /// </summary>
    private static async Task InitializeOfflineTrackingAsync(CancellationToken ct = default)
    {
        try
        {
            await WpfServices.OfflineTrackingPersistenceService.Instance.InitializeAsync();
        }
        catch (Exception)
        {
            // Continue - app should still work without persistence
        }
    }

    /// <summary>
    /// Initializes background services for scheduled tasks and offline queue processing.
    /// </summary>
    private static async Task InitializeBackgroundServicesAsync(CancellationToken ct = default)
    {
        try
        {
            // Initialize pending operations queue
            await WpfServices.PendingOperationsQueueService.Instance.InitializeAsync();

            // Start background task scheduler
            await WpfServices.BackgroundTaskSchedulerService.Instance.StartAsync();
        }
        catch (Exception)
        {
            // Continue - app should still work without background services
        }
    }

    /// <summary>
    /// Restores the last workspace session state (active page, window bounds).
    /// </summary>
    private static async Task RestoreWorkspaceSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var workspaceService = WpfServices.WorkspaceService.Instance;
            await workspaceService.LoadWorkspacesAsync();

            var session = workspaceService.GetLastSessionState();
            if (session != null && !string.IsNullOrEmpty(session.ActivePageTag))
            {
                // Navigate to the last active page
                WpfServices.NavigationService.Instance.NavigateTo(session.ActivePageTag);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Saves the current workspace session state before shutdown.
    /// </summary>
    private static async Task SaveWorkspaceSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var workspaceService = WpfServices.WorkspaceService.Instance;
            var navService = WpfServices.NavigationService.Instance;

            var currentPageTag = navService.GetCurrentPageTag() ?? "Dashboard";
            var session = new Meridian.Ui.Services.SessionState
            {
                ActivePageTag = currentPageTag,
                SavedAt = DateTime.UtcNow
            };

            await workspaceService.SaveSessionStateAsync(session, ct: ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static void EnsureMainWindowVisible(Window window)
    {
        if (window is MainWindow mainWindow)
        {
            mainWindow.ForceStartupWindowRecovery();
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.ShowInTaskbar)
        {
            window.ShowInTaskbar = true;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    private static async Task RestoreMainWindowVisibilityAsync(Window window)
    {
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3)
        };

        foreach (var delay in delays)
        {
            await Task.Delay(delay).ConfigureAwait(true);

            if (Current is null || Current.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            await Current.Dispatcher.InvokeAsync(() => EnsureMainWindowVisible(window), DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// Handles app exit for clean shutdown of background services with timeout.
    /// </summary>
    private void OnExit(object sender, ExitEventArgs e)
    {
        SafeOnExitAsync().GetAwaiter().GetResult();
        StopHostSafely();
        WpfServices.SingleInstanceService.Instance.Dispose();
    }

    /// <summary>
    /// Performs async shutdown with proper exception handling.
    /// </summary>
    private static async Task SafeOnExitAsync(CancellationToken ct = default)
    {
        const int ShutdownTimeoutMs = 5000; // 5 second timeout for graceful shutdown

        try
        {

            // Close any floating tear-off quote panels before service shutdown
            WpfServices.TearOffPanelService.Instance.CloseAll();

            using var cts = new CancellationTokenSource(ShutdownTimeoutMs);

            // Save workspace session before shutting down services
            await SaveWorkspaceSessionAsync(cts.Token).ConfigureAwait(false);

            // Shutdown services in parallel with timeout for better performance
            var shutdownTasks = new[]
            {
                ShutdownServiceAsync(() => WpfServices.BackgroundTaskSchedulerService.Instance.StopAsync(), "BackgroundTaskScheduler", cts.Token),
                ShutdownServiceAsync(() => WpfServices.PendingOperationsQueueService.Instance.ShutdownAsync(), "PendingOperationsQueue", cts.Token),
                ShutdownServiceAsync(() => WpfServices.OfflineTrackingPersistenceService.Instance.ShutdownAsync(), "OfflineTrackingPersistence", cts.Token),
                ShutdownServiceAsync(() => WpfServices.ConnectionService.Instance.StopMonitoring(), "ConnectionService", cts.Token),
                ShutdownServiceAsync(() => StopManagedBackendAsync(cts.Token), "BackendServiceManager", cts.Token)
            };

            await Task.WhenAll(shutdownTasks).ConfigureAwait(false);

            // Dispose the NotifyIcon so the system-tray icon is removed cleanly.
            try
            {
                WpfServices.ToastNotificationService.Instance.Dispose();
            }
            catch (Exception ex)
            {
                WpfServices.LoggingService.Instance.LogWarning(
                    "Failed to dispose toast notification service during shutdown",
                    ("Error", ex.Message));
            }

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void StopHostSafely()
    {
        var host = _host;
        _host = null;
        if (host is null)
        {
            return;
        }

        const int HostShutdownTimeoutMs = 5000;
        try
        {
            using var cts = new CancellationTokenSource(HostShutdownTimeoutMs);
            host.StopAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }

        try
        {
            if (host is IAsyncDisposable asyncHost)
            {
                asyncHost.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception)
        {
        }
    }

    private static async Task StopManagedBackendAsync(CancellationToken ct)
    {
        var result = await WpfServices.BackendServiceManager.Instance.StopAsync(ct).ConfigureAwait(false);
        if (!result.Success)
        {
            WpfServices.LoggingService.Instance.LogWarning(
                "Backend service manager reported a shutdown failure",
                ("Message", result.Message));
        }
    }

    /// <summary>
    /// Helper method to shutdown a service with proper error handling.
    /// </summary>
    private static async Task ShutdownServiceAsync(Func<Task> shutdownAction, string serviceName, CancellationToken ct)
    {
        try
        {
            await shutdownAction().WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Helper method to shutdown a synchronous service.
    /// </summary>
    private static async Task ShutdownServiceAsync(Action shutdownAction, string serviceName, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                shutdownAction();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs first-run initialization including config setup.
    /// </summary>
    private static async Task InitializeFirstRunAsync(CancellationToken ct = default)
    {
        try
        {
            var firstRunService = WpfServices.FirstRunService.Instance;
            _isFirstRun = await firstRunService.IsFirstRunAsync();

            if (_isFirstRun)
            {
                await firstRunService.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            // Q2: Log first-run setup failures instead of silently swallowing
            WpfServices.LoggingService.Instance.LogError("First-run setup failed", ex);
        }
    }

    /// <summary>
    /// Initializes and validates the application configuration.
    /// </summary>
    private static async Task InitializeConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            // Initialize the config service
            await WpfServices.ConfigService.Instance.InitializeAsync();

            // Validate configuration
            var validationResult = await WpfServices.ConfigService.Instance.ValidateConfigAsync();

            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    WpfServices.LoggingService.Instance.LogError("Configuration error: " + error);
                }
            }

            foreach (var warning in validationResult.Warnings)
            {
                WpfServices.LoggingService.Instance.LogWarning("Configuration warning: " + warning);
            }

            WpfServices.LoggingService.Instance.LogInfo("Configuration initialized",
                ("isValid", validationResult.IsValid.ToString()),
                ("errors", validationResult.Errors.Length.ToString()),
                ("warnings", validationResult.Warnings.Length.ToString()));
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("Failed to initialize configuration", ex);
            // Continue - app should still work with defaults
        }
    }

    /// <summary>
    /// Handles unhandled exceptions on the UI thread.
    /// E3 fix: only suppress transient/recoverable exceptions; fatal ones are logged and re-raised
    /// so the process can terminate cleanly instead of limping forward in a broken state.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;

        // Determine whether the exception is likely recoverable (transient UI or I/O issues).
        var isRecoverable =
            ex is InvalidOperationException or
                 System.Net.Http.HttpRequestException or
                 TimeoutException or
                 OperationCanceledException or
                 System.IO.IOException;

        // Always log with structured logging so the error is visible in the log file.
        WpfServices.LoggingService.Instance.LogError("Dispatcher unhandled exception", ex);

        if (isRecoverable)
        {
            e.Handled = true;
            try
            {
                _ = WpfServices.NotificationService.Instance.NotifyErrorAsync(
                    "Application Error",
                    ex.Message);
            }
            catch (Exception notifyEx)
            {
                WpfServices.LoggingService.Instance.LogWarning(
                    "Notification failure during error handling",
                    ("Error", notifyEx.Message));
            }
        }
        // Non-recoverable exceptions are not marked Handled so WPF can tear down cleanly.
    }

    /// <summary>
    /// Handles unhandled exceptions from non-UI threads.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError("Domain unhandled exception", ex);
        }
    }

    /// <summary>
    /// Handles unobserved task exceptions.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WpfServices.LoggingService.Instance.LogError("Unobserved task exception", e.Exception);
        e.SetObserved(); // Prevent the process from terminating
    }

    /// <summary>
    /// Wires the NotificationService to the system tray to display important notifications as balloon tips.
    /// </summary>
    private static void WireNotificationsTray(WpfServices.ISystemTrayService systemTrayService)
    {
        var notificationService = WpfServices.NotificationService.Instance;
        notificationService.NotificationReceived += (sender, args) =>
        {
            // Only show high-priority notifications in tray to avoid spam
            if (args.Type == NotificationType.Error || args.Type == NotificationType.Success)
            {
                var icon = args.Type switch
                {
                    NotificationType.Error => System.Windows.Forms.ToolTipIcon.Error,
                    NotificationType.Warning => System.Windows.Forms.ToolTipIcon.Warning,
                    NotificationType.Success => System.Windows.Forms.ToolTipIcon.Info,
                    _ => System.Windows.Forms.ToolTipIcon.Info
                };

                systemTrayService.ShowBalloonTip(args.Title, args.Message, icon, args.DurationMs);
            }
        };
    }

    /// <summary>
    /// Wires the ConnectionService status changes to the system tray icon color and tooltip.
    /// Updates the tray icon to reflect connection health: green (connected), amber (reconnecting), red (disconnected).
    /// </summary>
    private static void WireConnectionStatusTray(WpfServices.ISystemTrayService systemTrayService)
    {
        var connectionService = WpfServices.ConnectionService.Instance;
        connectionService.StateChanged += (sender, args) =>
        {
            // Map ConnectionState to ConnectionStatus
            var status = args.NewState switch
            {
                ConnectionState.Connected => ConnectionStatus.Connected,
                ConnectionState.Reconnecting => ConnectionStatus.Reconnecting,
                ConnectionState.Disconnected => ConnectionStatus.Disconnected,
                _ => ConnectionStatus.Faulted
            };

            systemTrayService.UpdateHealthStatus(status);
        };

        // Set initial status
        var initialStatus = connectionService.State switch
        {
            ConnectionState.Connected => ConnectionStatus.Connected,
            ConnectionState.Reconnecting => ConnectionStatus.Reconnecting,
            ConnectionState.Disconnected => ConnectionStatus.Disconnected,
            _ => ConnectionStatus.Faulted
        };
        systemTrayService.UpdateHealthStatus(initialStatus);
    }

}
