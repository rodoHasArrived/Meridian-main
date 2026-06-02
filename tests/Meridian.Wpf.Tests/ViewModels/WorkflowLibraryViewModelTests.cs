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
            viewModel.Workflows.Should().Contain(workflow => workflow.WorkflowId == "portfolio-position-review");
            viewModel.SummaryText.Should().Contain("workflows");
            viewModel.ResultScopeText.Should().StartWith("Showing");
            viewModel.HasActiveSearch.Should().BeFalse();
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeFalse();

            viewModel.SearchQuery = "reconciliation";

            viewModel.HasActiveSearch.Should().BeTrue();
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeTrue();
            viewModel.ResultScopeText.Should().Contain("match \"reconciliation\"");
            viewModel.Workflows.Should().Contain(workflow => workflow.WorkflowId == "accounting-reconciliation-review");
            viewModel.Workflows.Should().OnlyContain(workflow => workflow.Matches("reconciliation"));
        });
    }

    [Fact]
    public void Load_ShouldExposeAccountingRecordsEvidenceReviewWorkflow()
    {
        WpfTestThread.Run(() =>
        {
            var navigationService = NavigationService.Instance;
            navigationService.ResetForTests();
            navigationService.Initialize(new Frame());

            var viewModel = new WorkflowLibraryViewModel(
                new WorkflowLibraryService(WorkflowRegistry.CreateDefault()),
                navigationService);

            viewModel.SearchQuery = "accounting records";

            var workflow = viewModel.Workflows.Should()
                .ContainSingle(item => item.WorkflowId == "accounting-records-evidence-review")
                .Subject;

            workflow.Title.Should().Be("Accounting Records Evidence Review");
            workflow.Summary.Should().Contain("v0.15 operational record");
            workflow.WorkspaceTitle.Should().Be("Accounting");
            workflow.EntryPageTag.Should().Be("AccountingShell");
            workflow.EvidenceText.Should().Contain("source records");
            workflow.EvidenceText.Should().Contain("document attachments");
            workflow.EvidenceText.Should().Contain("export manifests");
            workflow.EvidenceText.Should().Contain("report lineage");
            workflow.MarketPatternText.Should().Contain("close package");
            workflow.MarketPatternText.Should().Contain("export manifests");
            workflow.Actions.Select(action => action.Label).Should().Equal(
                "Review Source Records",
                "Review Normalized Activity",
                "Review Reconciliation Cases",
                "Review Ledger Evidence",
                "Review Approval History",
                "Review Report Lineage");
            workflow.Actions.Select(action => action.TargetPageTag).Should().Equal(
                "DataShell",
                "PortfolioShell",
                "FundReconciliation",
                "FundTrialBalance",
                "OperationsContinuity",
                "FundReportPack");
        });
    }

    [Fact]
    public void FilterRecoveryState_ShouldExplainNoMatchesAndClearSearch()
    {
        WpfTestThread.Run(() =>
        {
            var navigationService = NavigationService.Instance;
            navigationService.ResetForTests();
            navigationService.Initialize(new Frame());

            var viewModel = new WorkflowLibraryViewModel(
                new WorkflowLibraryService(WorkflowRegistry.CreateDefault()),
                navigationService);

            viewModel.SearchQuery = "no-such-workflow";

            viewModel.Workflows.Should().BeEmpty();
            viewModel.HasWorkflows.Should().BeFalse();
            viewModel.HasActiveSearch.Should().BeTrue();
            viewModel.ResultScopeText.Should().Contain("0 of");
            viewModel.EmptyStateTitle.Should().Be("No workflows match the current filter");
            viewModel.EmptyStateDetail.Should().Contain("Clear the search");
            viewModel.EmptyStateActionText.Should().Be("Clear search");

            viewModel.ClearSearchCommand.Execute(null);

            viewModel.SearchQuery.Should().BeEmpty();
            viewModel.HasActiveSearch.Should().BeFalse();
            viewModel.HasWorkflows.Should().BeTrue();
            viewModel.ResultScopeText.Should().StartWith("Showing");
        });
    }

    [Fact]
    public void BuildFilterPresentation_WhenLibraryIsEmpty_RecommendsRefresh()
    {
        var presentation = WorkflowLibraryViewModel.BuildFilterPresentation(
            totalWorkflowCount: 0,
            visibleWorkflowCount: 0,
            totalActionCount: 0,
            searchQuery: string.Empty);

        presentation.ResultScopeText.Should().Be("0 workflows / 0 actions");
        presentation.EmptyStateTitle.Should().Be("No workflows are registered");
        presentation.EmptyStateDetail.Should().Contain("Refresh the library");
        presentation.EmptyStateActionText.Should().Be("Refresh library");
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

    [Fact]
    public void WorkflowLibraryPageSource_BindsFilterRecoveryThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\WorkflowLibraryPage.xaml"));

        xaml.Should().Contain("WorkflowLibraryResultScopeText");
        xaml.Should().Contain("{Binding ResultScopeText}");
        xaml.Should().Contain("WorkflowLibraryClearSearchButton");
        xaml.Should().Contain("{Binding ClearSearchCommand}");
        xaml.Should().Contain("WorkflowLibraryEmptyStatePanel");
        xaml.Should().Contain("{Binding EmptyStateTitle}");
        xaml.Should().Contain("{Binding EmptyStateDetail}");
        xaml.Should().Contain("{Binding EmptyStateActionText}");
        xaml.Should().Contain("{Binding RefreshCommand}");
        xaml.Should().NotContain("{Binding EmptyStateText}");
    }
}
