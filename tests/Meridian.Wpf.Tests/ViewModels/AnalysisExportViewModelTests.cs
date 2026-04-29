using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AnalysisExportViewModelTests
{
    [Fact]
    public void NewViewModel_DisablesRunExportAndExplainsMissingSetup()
    {
        var viewModel = new AnalysisExportViewModel();

        viewModel.CanRunExport().Should().BeFalse();
        viewModel.RunExportCommand.CanExecute(null).Should().BeFalse();
        viewModel.ExportReadinessTitle.Should().Be("Export setup incomplete");
        viewModel.ExportReadinessDetail.Should().Contain("Export name is required");
        viewModel.ExportReadinessDetail.Should().Contain("Destination folder is required");
        viewModel.ExportReadinessDetail.Should().Contain("Select at least one metric");
        viewModel.RecentExportsStateText.Should().Be("No exports have been queued in this session yet.");
    }

    [Fact]
    public void RequiredFieldsAndMetric_EnableRunExportAndDescribeScope()
    {
        var viewModel = new AnalysisExportViewModel
        {
            ExportName = "Morning analytics",
            Destination = @"C:\exports",
            SymbolFilter = "aapl, msft"
        };

        viewModel.Metrics[0].IsSelected = true;

        viewModel.CanRunExport().Should().BeTrue();
        viewModel.RunExportCommand.CanExecute(null).Should().BeTrue();
        viewModel.ExportReadinessTitle.Should().Be("Export ready");
        viewModel.ExportReadinessDetail.Should().Contain("CSV export");
        viewModel.ExportReadinessDetail.Should().Contain("1 metric");
        viewModel.ExportReadinessDetail.Should().Contain("2 selected symbols");
    }

    [Fact]
    public void RunExportCommand_WhenReady_QueuesRecentExportAndUpdatesState()
    {
        var viewModel = new AnalysisExportViewModel
        {
            ExportName = "Liquidity close",
            Destination = @"C:\exports",
            SelectedFormat = "Parquet"
        };
        viewModel.Metrics.Single(metric => metric.Name == "Liquidity").IsSelected = true;

        viewModel.RunExportCommand.Execute(null);

        viewModel.RecentExports.Should().ContainSingle();
        viewModel.RecentExports[0].Name.Should().Be("Liquidity close");
        viewModel.RecentExports[0].Format.Should().Be("Parquet");
        viewModel.RecentExports[0].Status.Should().Be("Queued");
        viewModel.StatusMessage.Should().Be("Export \"Liquidity close\" queued successfully.");
        viewModel.RecentExportsStateText.Should().Be("1 export retained for this session.");
    }

    [Fact]
    public void RunExport_WithoutMetric_ReportsMetricValidation()
    {
        var viewModel = new AnalysisExportViewModel
        {
            ExportName = "Volatility pack",
            Destination = @"C:\exports"
        };

        viewModel.RunExport();

        viewModel.ValidationSummary.Should().Be("Select at least one metric for the export.");
        viewModel.StatusMessage.Should().Be("Select metrics to proceed.");
        viewModel.RunExportCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Initialize_SeedsRecentExportsAndUpdatesRetainedState()
    {
        var viewModel = new AnalysisExportViewModel();

        viewModel.Initialize();

        viewModel.RecentExports.Should().HaveCount(2);
        viewModel.RecentExportsStateText.Should().Be("2 exports retained for this session.");
    }

    [Fact]
    public void AnalysisExportPageSource_BindsActionsThroughViewModelCommands()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AnalysisExportPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AnalysisExportPage.xaml.cs"));

        xaml.Should().Contain("AnalysisExportReadinessCard");
        xaml.Should().Contain("{Binding ExportReadinessTitle}");
        xaml.Should().Contain("{Binding ExportReadinessDetail}");
        xaml.Should().Contain("{Binding RecentExportsStateText}");
        xaml.Should().Contain("Command=\"{Binding RunExportCommand}\"");
        xaml.Should().Contain("Command=\"{Binding SavePresetCommand}\"");
        xaml.Should().NotContain("Click=\"RunExport_Click\"");
        xaml.Should().NotContain("Click=\"SavePreset_Click\"");

        codeBehind.Should().NotContain("RunExport_Click");
        codeBehind.Should().NotContain("SavePreset_Click");
    }
}
