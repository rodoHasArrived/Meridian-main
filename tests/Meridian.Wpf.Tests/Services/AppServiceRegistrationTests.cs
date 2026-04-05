using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Application.FundAccounts;
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
}
