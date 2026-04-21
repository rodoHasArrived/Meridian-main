using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Application.SecurityMaster;
using Meridian.Application.FundAccounts;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
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
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();

            serviceProvider.GetRequiredService<ConfigService>().Should().BeSameAs(ConfigService.Instance);
            serviceProvider.GetRequiredService<WorkspaceService>().Should().BeSameAs(WorkspaceService.Instance);
            serviceProvider.GetRequiredService<ConnectionService>().Should().BeSameAs(ConnectionService.Instance);
            serviceProvider.GetRequiredService<LoggingService>().Should().BeSameAs(LoggingService.Instance);
            serviceProvider.GetRequiredService<StatusService>().Should().BeSameAs(StatusService.Instance);
            serviceProvider.GetRequiredService<StrategyRunWorkspaceService>().Should().NotBeNull();
            ResolveRequired<SymbolsPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<BackfillPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<RunMatPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<FundLedgerPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<OrderBookPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<RunRiskPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<EnvironmentDesignerPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<ResearchWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<TradingWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<DataOperationsWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            ResolveRequired<GovernanceWorkspaceShellPage>(serviceProvider).Should().NotBeNull();
            serviceProvider.GetRequiredService<IFundAccountService>().Should().BeOfType<InMemoryFundAccountService>();
            serviceProvider.GetRequiredService<FundAccountReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<CashFinancingReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<ReconciliationReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<FundOperationsWorkspaceReadService>().Should().NotBeNull();
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
    public void ResearchWorkspaceShellPage_ShouldResolveEvenWithoutWorkspaceServiceRegistration()
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

            var exception = Record.Exception(() => serviceProvider.GetRequiredService<ResearchWorkspaceShellPage>());

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
        configureServices!.Invoke(null, [services]);

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
