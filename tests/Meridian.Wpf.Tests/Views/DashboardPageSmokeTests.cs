using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class DashboardPageSmokeTests
{
    [Fact]
    public void DashboardPage_ShouldInstantiateWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.SetServiceProvider(services);

            DashboardPage? page = null;
            var exception = Record.Exception(() => page = services.GetRequiredService<DashboardPage>());

            exception.Should().BeNull();
            page.Should().NotBeNull();

            var viewModel = page!.DataContext.Should().BeOfType<DashboardViewModel>().Subject;
            viewModel.OperationsMetrics.Should().HaveCount(8);
            viewModel.HoldingsSnapshotItems.Should().HaveCount(8);
            viewModel.HoldingsSnapshotCountText.Should().Be("8 securities");
            viewModel.PortfolioDataServiceStatuses.Should().Contain(s => s.ServiceName == "Accounting export" && s.State == "ready");
        });
    }
}
