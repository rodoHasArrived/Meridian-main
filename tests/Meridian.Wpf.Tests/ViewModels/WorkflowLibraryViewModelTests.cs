using System.Windows.Controls;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class WorkflowLibraryViewModelTests
{
    [Fact]
    public void Load_ShouldExposeBuiltInWorkflowsAndFilterActions()
    {
        WpfTestThread.Run(() =>
        {
            var navigationService = NavigationService.Instance;
            navigationService.ResetForTests();
            navigationService.Initialize(new Frame());

            var viewModel = new WorkflowLibraryViewModel(
                new WorkflowLibraryService(WorkflowRegistry.CreateDefault()),
                navigationService);

            viewModel.Workflows.Should().Contain(workflow => workflow.WorkflowId == "data-provider-recovery");
            viewModel.SummaryText.Should().Contain("workflows");

            viewModel.SearchQuery = "reconciliation";

            viewModel.Workflows.Should().Contain(workflow => workflow.WorkflowId == "accounting-reconciliation-review");
            viewModel.Workflows.Should().OnlyContain(workflow => workflow.Matches("reconciliation"));
        });
    }

    [Fact]
    public void OpenWorkflowCommand_ShouldNavigateToRegisteredTarget()
    {
        WpfTestThread.Run(() =>
        {
            var navigationService = NavigationService.Instance;
            navigationService.ResetForTests();
            navigationService.Initialize(new Frame());

            var viewModel = new WorkflowLibraryViewModel(
                new WorkflowLibraryService(WorkflowRegistry.CreateDefault()),
                navigationService);

            viewModel.OpenWorkflowCommand.Execute("WorkflowLibrary");

            navigationService.GetCurrentPageTag().Should().Be("WorkflowLibrary");
        });
    }
}
