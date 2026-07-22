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
            viewModel.PageTitle.Should().Be("Strategy Operations");
            viewModel.OperationsMetrics.Should().BeEmpty("normal mode must wait for authoritative shared read models");
            viewModel.Actions.Select(action => action.Label).Should().Contain(
                ["Refresh", "Activity Log", "Quality Worklist"]);
            viewModel.HoldingsSnapshotItems.Should().BeEmpty("normal mode must not seed synthetic holdings");
            viewModel.HoldingsSnapshotCountText.Should().Be("0 holdings");
            viewModel.PortfolioDataServiceStatuses.Should().BeEmpty();
            viewModel.GetContextualCommands().Select(command => command.Category).Should().OnlyContain(category => category == "Strategy Operations");
        });
    }
}
