using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class DataExportViewModelTests
{
    [Fact]
    public void NewViewModel_EnablesQuickExportAndDescribesReadyScope()
    {
        var viewModel = new DataExportViewModel();

        viewModel.CanRunExportData().Should().BeTrue();
        viewModel.CanExportData.Should().BeTrue();
        viewModel.ExportDataCommand.CanExecute(null).Should().BeTrue();
        viewModel.ExportReadinessTitle.Should().Be("Export ready");
        viewModel.ExportReadinessDetail.Should().Contain("3 symbols selected");
        viewModel.ExportReadinessDetail.Should().Contain("CSV export");
        viewModel.SelectedSymbolCountText.Should().Be("3 symbols selected");
        viewModel.ExportScopeText.Should().Contain("3 symbols selected");
    }

    [Fact]
    public void MissingSymbols_DisablesQuickExportAndExplainsRequiredSelection()
    {
        var viewModel = new DataExportViewModel();

        viewModel.SelectedSymbols.Clear();

        viewModel.CanRunExportData().Should().BeFalse();
        viewModel.CanExportData.Should().BeFalse();
        viewModel.ExportDataCommand.CanExecute(null).Should().BeFalse();
        viewModel.ExportReadinessTitle.Should().Be("Export setup incomplete");
        viewModel.ExportReadinessDetail.Should().Contain("Add at least one symbol");
        viewModel.SelectedSymbolCountText.Should().Be("No symbols selected");
    }

    [Fact]
    public void InvalidDateRange_DisablesQuickExportAndUpdatesGuidance()
    {
        var viewModel = new DataExportViewModel
        {
            ExportFromDate = DateTime.Today,
            ExportToDate = DateTime.Today.AddDays(-1)
        };

        viewModel.CanRunExportData().Should().BeFalse();
        viewModel.ExportDataCommand.CanExecute(null).Should().BeFalse();
        viewModel.ExportReadinessTitle.Should().Be("Export setup incomplete");
        viewModel.ExportReadinessDetail.Should().Contain("Start date must be before end date");

        viewModel.ExportToDate = DateTime.Today;

        viewModel.CanRunExportData().Should().BeTrue();
        viewModel.ExportReadinessTitle.Should().Be("Export ready");
    }

    [Fact]
    public void AddSymbolCommand_NormalizesDeduplicatesAndRefreshesReadiness()
    {
        var viewModel = new DataExportViewModel();
        viewModel.SelectedSymbols.Clear();

        viewModel.SymbolSearchText = " spy ";
        viewModel.AddSymbolCommand.Execute(null);
        viewModel.SymbolSearchText = "SPY";
        viewModel.AddSymbolCommand.Execute(null);

        viewModel.SelectedSymbols.Should().Equal("SPY");
        viewModel.SelectedSymbolCountText.Should().Be("1 symbol selected");
        viewModel.CanRunExportData().Should().BeTrue();
        viewModel.ExportDataCommand.CanExecute(null).Should().BeTrue();
        viewModel.ExportReadinessDetail.Should().Contain("1 symbol selected");
    }

    [Fact]
    public void DataExportPageSource_BindsQuickExportReadinessThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml.cs"));

        xaml.Should().Contain("DataExportReadinessCard");
        xaml.Should().Contain("{Binding ExportReadinessTitle}");
        xaml.Should().Contain("{Binding ExportReadinessDetail}");
        xaml.Should().Contain("{Binding ExportScopeText}");
        xaml.Should().Contain("{Binding SelectedSymbolCountText}");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataExportRunButton\"");
        xaml.Should().Contain("Command=\"{Binding ExportDataCommand}\"");
        xaml.Should().Contain("Value=\"{Binding ExportProgressValue, Mode=OneWay}\"");
        xaml.Should().NotContain("Click=\"ExportData_Click\"");

        codeBehind.Should().NotContain("ExportData_Click");
    }
}
