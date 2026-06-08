#if WINDOWS
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunLedgerViewModelTests
{
    [Fact]
    public void OpenSelectedSecurityCommand_NavigatesToSecurityMasterWithResolvedSecurityId()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-security-id"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-security-id";
            await Task.Delay(150);

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.Single(line => line.Symbol == "AAPL");
            viewModel.TrialBalanceTable.Rows.Should().BeSameAs(viewModel.TrialBalance);
            viewModel.JournalTable.Rows.Should().BeSameAs(viewModel.Journal);
            viewModel.SelectedTrialBalanceInspector.Title.Should().Be("Securities");
            viewModel.SelectedTrialBalanceInspector.Subtitle.Should().Be("Apple Inc.");
            viewModel.SelectedTrialBalanceInspector.Facts.Should().Contain(f => f.Label == "Symbol" && f.Value == "AAPL");
            viewModel.SelectedSecurityTooltip.Should().Contain("Apple Inc.");
            viewModel.RunDrillInTooltip.Should().Contain("run-ledger-security-id");
            viewModel.CanOpenSelectedSecurity.Should().BeTrue();

            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        });
    }

    [Fact]
    public void OpenSelectedSecurityCommand_FallsBackToSymbolWhenCoverageIsMissing()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-symbol"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-symbol";
            await Task.Delay(150);

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.Single(line => line.Symbol == "TSLA");
            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be("TSLA");
        });
    }

    [Fact]
    public void OpenSelectedSecurityCommand_DisablesForNonSecurityTrialBalanceLines()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-no-security"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-no-security";
            await Task.Delay(150);

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.First(line => string.IsNullOrWhiteSpace(line.Symbol));

            viewModel.HasSelectedTrialBalanceLine.Should().BeTrue();
            viewModel.CanOpenSelectedSecurity.Should().BeFalse();
            viewModel.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
            viewModel.SelectedSecurityTooltip.Should().Be("Select a security-linked trial-balance line before opening Security Master.");
        });
    }

    [Fact]
    public void InitialState_ExposesSelectionGuidanceForDenseLedgerWorkbench()
    {
        var store = new Meridian.Strategies.Storage.StrategyRunStore();
        var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
        var workspaceService = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(lookup),
            new Meridian.Strategies.Services.LedgerReadService(lookup));
        var viewModel = new StrategyRunLedgerViewModel(workspaceService, NavigationService.Instance);

        viewModel.TrialBalanceTable.Title.Should().Be("Run trial balance");
        viewModel.TrialBalanceTable.EmptyTitle.Should().Be("No trial-balance lines retained");
        viewModel.JournalTable.Title.Should().Be("Run journal");
        viewModel.SelectedTrialBalanceInspector.Title.Should().Be("No trial-balance line selected");
        viewModel.HasSelectedTrialBalanceLine.Should().BeFalse();
        viewModel.CanOpenSelectedSecurity.Should().BeFalse();
        viewModel.SelectedSecurityTooltip.Should().Be("Select a trial-balance line before opening Security Master.");
        viewModel.RunDrillInTooltip.Should().Be("Select a retained strategy run before opening related run drill-ins.");
        viewModel.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RunLedgerPageSource_UsesCompactDenseTablesAndSelectionInspector()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunLedgerPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\StrategyRunLedgerViewModel.cs"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunLedgerActionStrip");
        xaml.Should().Contain("workstation:DenseDataGridControl");
        xaml.Should().Contain("RunLedgerTrialBalanceGrid");
        xaml.Should().Contain("RunLedgerJournalGrid");
        xaml.Should().Contain("RunLedgerSelectionInspector");
        xaml.Should().Contain("RunLedgerActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("ToolTip=\"{Binding SelectedSecurityTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RunDrillInTooltip}\"");
        viewModel.Should().Contain("public WorkstationTableModel<LedgerTrialBalanceLine> TrialBalanceTable");
        viewModel.Should().Contain("public WorkstationTableModel<LedgerJournalLine> JournalTable");
        viewModel.Should().Contain("public InspectorPanelModel SelectedTrialBalanceInspector");
    }
}
#endif
