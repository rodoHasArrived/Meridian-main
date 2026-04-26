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
            viewModel.PageTitle.Should().Be("Research Operations");
            viewModel.OperationsMetrics.Should().HaveCount(8);
            viewModel.OperationsMetrics.Select(metric => metric.Label).Should().Contain(
                ["Holdings in Scope", "Quality Exceptions", "Stale Valuations"]);
            viewModel.Actions.Select(action => action.Label).Should().Contain(
                ["Refresh", "Activity Log", "Quality Worklist"]);
            viewModel.HoldingsSnapshotItems.Should().HaveCount(8);
            viewModel.HoldingsSnapshotCountText.Should().Be("8 holdings");
            viewModel.HoldingsSnapshotItems.Select(item => item.DataStatus).Should().Contain(
                ["Current", "Needs review", "Stale price", "Data gap"]);
            viewModel.PortfolioDataServiceStatuses.Should().Contain(s => s.ServiceName == "Ledger export" && s.State == "ready");
            viewModel.GetContextualCommands().Select(command => command.Category).Should().OnlyContain(category => category == "Research Operations");
        });
    }
}
