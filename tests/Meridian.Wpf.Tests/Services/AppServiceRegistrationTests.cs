using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Features.Settings.Shell;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Services;

public sealed class AppServiceRegistrationTests
{
    [Fact]
    public void ConfigureServices_ShouldResolveSymbolsBackfillAndRunMatPages()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            AppServiceTestHost.InvokeConfigureServices(configureServices!, services);

            using var serviceProvider = services.BuildServiceProvider();

            serviceProvider.GetRequiredService<ConfigService>().Should().BeSameAs(ConfigService.Instance);
            serviceProvider.GetRequiredService<ICanonicalSymbolRegistry>().Should().NotBeNull();
            serviceProvider.GetRequiredService<WorkspaceService>().Should().BeSameAs(WorkspaceService.Instance);
            serviceProvider.GetRequiredService<WorkspaceStateTokenStore>().Should().NotBeNull();
            serviceProvider.GetRequiredService<ConnectionService>().Should().BeSameAs(ConnectionService.Instance);
            var remoteWorkstationClient = serviceProvider.GetRequiredService<WpfRemoteWorkstationClient>();
            serviceProvider.GetRequiredService<IRemoteWorkstationClient>().Should().BeSameAs(remoteWorkstationClient);
            serviceProvider.GetRequiredService<LoggingService>().Should().BeSameAs(LoggingService.Instance);
            serviceProvider.GetRequiredService<StatusService>().Should().BeSameAs(StatusService.Instance);
            serviceProvider.GetRequiredService<StrategyRunWorkspaceService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<UserProfileRegistry>().Should().NotBeNull();
            serviceProvider.GetRequiredService<LoginSessionService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<DesktopAuthenticationSession>().Should().NotBeNull();
            serviceProvider.GetRequiredService<StartupWindowViewModel>().Should().NotBeNull();
            ResolveRequired<StartupWindow>(serviceProvider).Should().NotBeNull();
            ResolveRequired<SymbolsPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<BackfillPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<RunMatPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<FundLedgerPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<OrderBookPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<RunRiskPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<RunCashFlowPage>(serviceProvider).DataContext.Should().BeOfType<CashFlowViewModel>();
            ResolveRequired<EnvironmentDesignerPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<StrategyWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<TradingWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<DataWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<SettingsWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<AccountingWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            serviceProvider.GetRequiredService<IDataWorkspaceShellSnapshotService>().Should().BeOfType<DataWorkspaceShellSnapshotService>();
            serviceProvider.GetRequiredService<IDataWorkspaceShellPresentationService>().Should().BeOfType<DataWorkspaceShellPresentationService>();
            serviceProvider.GetRequiredService<ISettingsWorkspaceShellSnapshotService>().Should().BeOfType<SettingsWorkspaceShellSnapshotService>();
            serviceProvider.GetRequiredService<ISettingsWorkspaceShellPresentationService>().Should().BeOfType<SettingsWorkspaceShellPresentationService>();
            serviceProvider.GetRequiredService<IFundAccountService>().Should().BeOfType<InMemoryFundAccountService>();
            serviceProvider.GetRequiredService<FundAccountReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<CashFinancingReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<ReconciliationReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<ISecurityAssetProfileWorkflowClient>()
                .Should().BeOfType<SecurityAssetProfileWorkflowClient>();
            serviceProvider.GetRequiredService<IOperationsControlCenterClient>()
                .Should().BeOfType<OperationsControlCenterClient>();
            serviceProvider.GetRequiredService<FundOperationsWorkspaceReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<IAccountingProjectionQueryService>().Should().BeOfType<AccountingProjectionQueryService>();
            serviceProvider.GetRequiredService<IPrivateCapitalCloseCockpitService>().Should().BeOfType<PrivateCapitalCloseCockpitService>();
            serviceProvider.GetRequiredService<AccountingCloseViewModel>().Should().NotBeNull();
        });
    }

    [Fact]
    public void ConfigureServices_ShouldRegisterCatalogPagesAndShellInfrastructureExactlyOnce()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            var services = BuildServiceCollection();

            foreach (var pageType in ShellNavigationCatalog.GetRegisteredPageTypes())
            {
                services.Count(descriptor => descriptor.ServiceType == pageType).Should().Be(
                    1,
                    $"catalog-backed page type '{pageType.Name}' should be registered exactly once through AddMeridianWpfShell");
            }

            foreach (var shell in ShellNavigationCatalog.WorkspaceShells)
            {
                shell.StateProviderType.Should().NotBeNull();
                shell.ViewModelType.Should().NotBeNull();

                services.Count(descriptor => descriptor.ServiceType == shell.StateProviderType).Should().Be(
                    1,
                    $"workspace state provider '{shell.StateProviderType!.Name}' should be registered exactly once");
                services.Count(descriptor => descriptor.ServiceType == shell.ViewModelType).Should().Be(
                    1,
                    $"workspace shell view model '{shell.ViewModelType!.Name}' should be registered exactly once");
            }
        });
    }

    [Fact]
    public void ConfigureServices_ShouldKeepPlatformServicesRootedAndWorkspacePresentationServicesScoped()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            var services = BuildServiceCollection();

            services.Single(descriptor => descriptor.ServiceType == typeof(NavigationService))
                .Lifetime.Should().Be(ServiceLifetime.Singleton);
            services.Single(descriptor => descriptor.ServiceType == typeof(ThemeService))
                .Lifetime.Should().Be(ServiceLifetime.Singleton);
            services.Single(descriptor => descriptor.ServiceType == typeof(FundContextService))
                .Lifetime.Should().Be(ServiceLifetime.Singleton);
            services.Single(descriptor => descriptor.ServiceType == typeof(WorkspaceStateTokenStore))
                .Lifetime.Should().Be(ServiceLifetime.Singleton);
            services.Single(descriptor => descriptor.ServiceType == typeof(IWorkspaceShellSlotContributionService))
                .Lifetime.Should().Be(ServiceLifetime.Singleton);

            services.Single(descriptor => descriptor.ServiceType == typeof(TradingWorkspaceShellPresentationService))
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
            services.Single(descriptor => descriptor.ServiceType == typeof(StrategyWorkspaceShellPresentationService))
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
            services.Single(descriptor => descriptor.ServiceType == typeof(IDataWorkspaceShellSnapshotService))
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
            services.Single(descriptor => descriptor.ServiceType == typeof(IDataWorkspaceShellPresentationService))
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
            services.Single(descriptor => descriptor.ServiceType == typeof(ISettingsWorkspaceShellSnapshotService))
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
            services.Single(descriptor => descriptor.ServiceType == typeof(ISettingsWorkspaceShellPresentationService))
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
        });
    }

    [Fact]
    public void ConfigureServices_ShouldResolveEveryCatalogPageType()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            using var serviceProvider = BuildServiceProvider();

            foreach (var pageType in ShellNavigationCatalog.GetRegisteredPageTypes())
            {
                serviceProvider.GetRequiredService(pageType).Should().NotBeNull(
                    $"catalog-backed page '{pageType.Name}' should resolve from DI");
            }
        });
    }

    [Fact]
    public void RunCashFlowPageSource_ShouldResolveThroughConstructorInjection()
    {
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunCashFlowPage.xaml.cs"));

        source.Should().Contain("public RunCashFlowPage(");
        source.Should().Contain("StrategyRunWorkspaceService workspaceService");
        source.Should().Contain("WpfNavigationService navigationService");
        source.Should().Contain("StrategyRunContinuityService? continuityService = null");
        source.Should().Contain("ResolveOptional<StrategyRunContinuityService>()");
        source.Should().Contain("DataContext = new CashFlowViewModel(workspaceService, navigationService, continuityService)");
        source.Should().NotContain("App.Services.GetRequiredService<StrategyRunWorkspaceService>()");
        source.Should().NotContain("App.Services.GetRequiredService<NavigationService>()");
    }

    [Fact]
    public void StrategyWorkspaceShellPage_ShouldResolveEvenWithoutWorkspaceServiceRegistration()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            var services = BuildServiceCollection();
            foreach (var descriptor in services.Where(static descriptor => descriptor.ServiceType == typeof(WorkspaceService)).ToArray())
            {
                services.Remove(descriptor);
            }

            using var serviceProvider = services.BuildServiceProvider();

            var exception = Record.Exception(() => serviceProvider.GetRequiredService<StrategyWorkspaceShellPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void ConfigureServices_WithoutSecurityMasterConnection_ShouldResolveSecurityMasterPageWithNullServices()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null)
                .Set("POLYGON_API_KEY", null);

            var serviceProvider = BuildServiceProvider();

            serviceProvider.GetRequiredService<ISecurityMasterImportService>().Should().BeOfType<NullSecurityMasterImportService>();
            serviceProvider.GetRequiredService<ITradingParametersBackfillService>().Should().BeOfType<NullTradingParametersBackfillService>();
            ResolveRequired<SecurityMasterPage>(serviceProvider).Should().NotBeNull();
        });
    }

    [Fact]
    public void ConfigureServices_WithSecurityMasterConnectionButNoPolygonKey_ShouldResolveSecurityMasterPage()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", "Host=localhost;Database=meridian;Username=test;Password=test")
                .Set("POLYGON_API_KEY", null);

            var serviceProvider = BuildServiceProvider();

            serviceProvider.GetRequiredService<ISecurityMasterImportService>().Should().BeOfType<SecurityMasterImportService>();
            serviceProvider.GetRequiredService<ITradingParametersBackfillService>().Should().BeOfType<TradingParametersBackfillService>();
            ResolveRequired<SecurityMasterPage>(serviceProvider).Should().NotBeNull();
        });
    }

    [Fact]
    public void ConfigureServices_WithSecurityMasterAndPolygonKey_ShouldResolveRealServices()
    {
        WpfTestThread.Run(() =>
        {
            using var env = new EnvironmentVariableScope()
                .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", "Host=localhost;Database=meridian;Username=test;Password=test")
                .Set("POLYGON_API_KEY", "polygon-test-key");

            var serviceProvider = BuildServiceProvider();

            serviceProvider.GetRequiredService<ISecurityMasterImportService>().Should().BeOfType<SecurityMasterImportService>();
            serviceProvider.GetRequiredService<ITradingParametersBackfillService>().Should().BeOfType<TradingParametersBackfillService>();
            ResolveRequired<SecurityMasterPage>(serviceProvider).Should().NotBeNull();
        });
    }

    [Fact]
    public void AppStartup_ShouldStartGenericHostServicesBeforeWpfBackgroundScheduler()
    {
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\App.xaml.cs"));

        source.Should().Contain("await StartHostServicesAsync(ct);");
        source.Should().Contain("await _host.StartAsync(ct).ConfigureAwait(false);");
        source.Should().Contain("WPF hosted services started");
        source.IndexOf("await StartHostServicesAsync(ct);", StringComparison.Ordinal)
            .Should().BeLessThan(
                source.IndexOf("await InitializeBackgroundServicesAsync();", StringComparison.Ordinal),
                "DI-hosted database workers should start before WPF-local scheduler services");
        source.Should().Contain("host.StopAsync(cts.Token)");
    }

    [Fact]
    public void AppStartup_ShouldRunRefusalGuardsBeforeShowingTheMainWindow()
    {
        // Seventh Codex review round on PR #2871. MainWindow.OnWindowLoaded navigates to the
        // fund-profile page, starts the shell view model and loads workspaces as soon as the window
        // is shown, so a guard that runs after Show() leaves the very posture it rejects live and
        // interactive until teardown finishes -- and checking the refusal flag afterwards only
        // suppresses the later visibility recovery, it cannot un-serve what was already on screen.
        //
        // Eighth round narrowed it to the guards alone: host startup stays behind the window, so a
        // slow hosted service can no longer hold the shell back indefinitely. Hence the preflight
        // call, not StartHostServicesAsync, is what has to precede the window here.
        //
        // Asserted over the source, like the host-ordering test above, because OnStartup owns
        // process lifetime and ends in Shutdown(): there is no way to drive it from a test host.
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\App.xaml.cs"));

        var guards = source.IndexOf("await RunStartupRefusalPreflightAsync();", StringComparison.Ordinal);
        var resolveWindow = source.IndexOf("Services.GetRequiredService<MainWindow>();", StringComparison.Ordinal);
        var showWindow = source.IndexOf("mainWindow.Show();", StringComparison.Ordinal);

        guards.Should().BeGreaterThanOrEqualTo(0, "the guard phase must be its own awaited step in OnStartup");
        resolveWindow.Should().BeGreaterThanOrEqualTo(0);
        showWindow.Should().BeGreaterThanOrEqualTo(0);

        guards.Should().BeLessThan(
            resolveWindow, "a composition a guard refuses must not even construct the shell");
        guards.Should().BeLessThan(
            showWindow, "a composition a guard refuses must never be shown to an operator");

        // Only call sites in OnStartup are compared. Callee bodies sit further down the file than
        // OnStartup, so their source offsets say nothing about when they run -- which is why the
        // window is NOT asserted against StartHostServicesAsync here.
    }

    private static ServiceProvider BuildServiceProvider()
    {
        RunMatUiAutomationFacade.EnsureApplicationResources();

        return BuildServiceCollection().BuildServiceProvider();
    }

    private static ServiceCollection BuildServiceCollection()
    {
        RunMatUiAutomationFacade.EnsureApplicationResources();

        var services = new ServiceCollection();
        var configureServices = typeof(Meridian.Wpf.App)
            .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

        configureServices.Should().NotBeNull();
        AppServiceTestHost.InvokeConfigureServices(configureServices!, services);

        return services;
    }

    private static T ResolveRequired<T>(IServiceProvider serviceProvider) where T : notnull
        => serviceProvider.GetRequiredService<T>();

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
