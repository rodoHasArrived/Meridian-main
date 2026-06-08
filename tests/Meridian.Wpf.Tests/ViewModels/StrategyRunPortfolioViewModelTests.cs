#if WINDOWS
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunPortfolioViewModelTests
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
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-portfolio-security-id"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunPortfolioViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-portfolio-security-id";
            await Task.Delay(150);

            viewModel.SelectedPosition.Should().NotBeNull();
            viewModel.SelectedPosition!.Symbol.Should().Be("AAPL");
            viewModel.PositionsTable.Rows.Should().BeSameAs(viewModel.Positions);
            viewModel.SelectedPositionInspector.Title.Should().Be("AAPL");
            viewModel.SelectedPositionInspector.Subtitle.Should().Be("Apple Inc.");
            viewModel.SelectedSecurityTooltip.Should().Contain("Apple Inc.");
            viewModel.RunDrillInTooltip.Should().Contain("run-portfolio-security-id");

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
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-portfolio-symbol"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunPortfolioViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-portfolio-symbol";
            await Task.Delay(150);

            viewModel.SelectedPosition = viewModel.Positions.Single(position => position.Symbol == "TSLA");
            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be("TSLA");
        });
    }

    [Fact]
    public void InitialState_ExposesSelectionGuidanceForDensePortfolioWorkbench()
    {
        var store = new Meridian.Strategies.Storage.StrategyRunStore();
        var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
        var workspaceService = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(lookup),
            new Meridian.Strategies.Services.LedgerReadService(lookup));
        var viewModel = new StrategyRunPortfolioViewModel(workspaceService, NavigationService.Instance);

        viewModel.PositionsTable.Title.Should().Be("Run portfolio positions");
        viewModel.PositionsTable.EmptyTitle.Should().Be("No positions retained");
        viewModel.SelectedPositionInspector.Title.Should().Be("No position selected");
        viewModel.HasSelectedPosition.Should().BeFalse();
        viewModel.SelectedSecurityTooltip.Should().Be("Select a position before opening Security Master.");
        viewModel.RunDrillInTooltip.Should().Be("Select a retained strategy run before opening related run drill-ins.");
        viewModel.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenLedgerCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RunPortfolioPageSource_UsesCompactDenseTableAndSelectionInspector()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunPortfolioPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\StrategyRunPortfolioViewModel.cs"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunPortfolioActionStrip");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("Table=\"{Binding PositionsTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedPosition, Mode=TwoWay}\"");
        xaml.Should().Contain("Inspector=\"{Binding SelectedPositionInspector}\"");
        xaml.Should().Contain("RunPortfolioPositionsGrid");
        xaml.Should().Contain("RunPortfolioSelectionInspector");
        xaml.Should().Contain("RunPortfolioActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("ToolTip=\"{Binding SelectedSecurityTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RunDrillInTooltip}\"");
        viewModel.Should().Contain("public WorkstationTableModel<PortfolioPositionSummary> PositionsTable");
        viewModel.Should().Contain("public InspectorPanelModel SelectedPositionInspector");
    }
}
#endif
