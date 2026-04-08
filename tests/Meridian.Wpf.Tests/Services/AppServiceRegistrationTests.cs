using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Application.SecurityMaster;
using Meridian.Application.FundAccounts;
using Meridian.Infrastructure.Adapters.Polygon;
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
            var configuration = new ConfigurationBuilder().Build();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services, configuration]);

            using var serviceProvider = services.BuildServiceProvider();

            serviceProvider.GetRequiredService<ConfigService>().Should().BeSameAs(ConfigService.Instance);
            serviceProvider.GetRequiredService<WorkspaceService>().Should().BeSameAs(WorkspaceService.Instance);
            serviceProvider.GetRequiredService<SymbolsPage>().Should().NotBeNull();
            serviceProvider.GetRequiredService<BackfillPage>().Should().NotBeNull();
            serviceProvider.GetRequiredService<RunMatPage>().Should().NotBeNull();
            serviceProvider.GetRequiredService<FundLedgerPage>().Should().NotBeNull();
            serviceProvider.GetRequiredService<IFundAccountService>().Should().BeOfType<InMemoryFundAccountService>();
            serviceProvider.GetRequiredService<FundAccountReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<CashFinancingReadService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<ReconciliationReadService>().Should().NotBeNull();
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
            serviceProvider.GetRequiredService<SecurityMasterPage>().Should().NotBeNull();
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
            serviceProvider.GetRequiredService<ITradingParametersBackfillService>().Should().BeOfType<NullTradingParametersBackfillService>();
            serviceProvider.GetRequiredService<SecurityMasterPage>().Should().NotBeNull();
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
            serviceProvider.GetRequiredService<SecurityMasterPage>().Should().NotBeNull();
        });
    }

    private static ServiceProvider BuildServiceProvider()
    {
        RunMatUiAutomationFacade.EnsureApplicationResources();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var configureServices = typeof(Meridian.Wpf.App)
            .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

        configureServices.Should().NotBeNull();
        configureServices!.Invoke(null, [services, configuration]);

        return services.BuildServiceProvider();
    }

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
