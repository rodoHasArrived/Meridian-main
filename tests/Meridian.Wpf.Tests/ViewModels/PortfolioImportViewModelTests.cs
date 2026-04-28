using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class PortfolioImportViewModelTests
{
    [Fact]
    public void NewViewModel_DisablesInvalidImportActionsAndShowsGuidance()
    {
        var viewModel = new PortfolioImportViewModel();

        viewModel.CanImportFile.Should().BeFalse();
        viewModel.ImportFileCommand.CanExecute(null).Should().BeFalse();
        viewModel.FileImportGuidanceTitle.Should().Be("Select an import file");
        viewModel.FileImportGuidanceText.Should().Contain("Choose a CSV");

        viewModel.ManualSymbolCount.Should().Be(0);
        viewModel.CanAddManualSymbols.Should().BeFalse();
        viewModel.AddManualSymbolsCommand.CanExecute(null).Should().BeFalse();
        viewModel.ManualSymbolCountText.Should().Be("No symbols queued");
        viewModel.ManualEntryGuidanceText.Should().Contain("Paste one or more symbols");
    }

    [Fact]
    public void FilePath_EnablesImportActionAndUpdatesReadinessCopy()
    {
        var viewModel = new PortfolioImportViewModel
        {
            SelectedFileFormat = "csv-header",
            FilePath = @"C:\imports\desk-book.csv"
        };

        viewModel.CanImportFile.Should().BeTrue();
        viewModel.ImportFileCommand.CanExecute(null).Should().BeTrue();
        viewModel.FileImportGuidanceTitle.Should().Be("File ready to import");
        viewModel.FileImportGuidanceText.Should().Contain("desk-book.csv");
        viewModel.FileImportGuidanceText.Should().Contain("CSV with headers");
    }

    [Fact]
    public void ManualSymbols_DeduplicatesInputAndEnablesAddAction()
    {
        var viewModel = new PortfolioImportViewModel
        {
            ManualSymbols = "aapl, msft MSFT; spy\nAAPL"
        };

        viewModel.ManualSymbolCount.Should().Be(3);
        viewModel.CanAddManualSymbols.Should().BeTrue();
        viewModel.AddManualSymbolsCommand.CanExecute(null).Should().BeTrue();
        viewModel.ManualSymbolCountText.Should().Be("3 unique symbols queued");
        viewModel.ManualEntryGuidanceText.Should().Contain("Ready to add");

        PortfolioImportViewModel.ExtractManualSymbols(viewModel.ManualSymbols)
            .Should().Equal("AAPL", "MSFT", "SPY");
    }

    [Fact]
    public async Task AddManualSymbolsAsync_WithoutSymbols_ReportsValidationWithoutServiceCall()
    {
        var viewModel = new PortfolioImportViewModel();

        await viewModel.AddManualSymbolsAsync();

        viewModel.ManualImportStatus.Should().Be("Enter symbols first.");
        viewModel.IsManualImporting.Should().BeFalse();
        viewModel.CanAddManualSymbols.Should().BeFalse();
    }

    [Fact]
    public async Task AddManualSymbolsAsync_WithOnlySeparators_ReportsValidationWithoutServiceCall()
    {
        var viewModel = new PortfolioImportViewModel
        {
            ManualSymbols = ", ; \r\n"
        };

        await viewModel.AddManualSymbolsAsync();

        viewModel.ManualImportStatus.Should().Be("Enter symbols first.");
        viewModel.ManualSymbolCount.Should().Be(0);
        viewModel.AddManualSymbolsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PortfolioImportPageSource_BindsActionsThroughViewModelCommands()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\PortfolioImportPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\PortfolioImportPage.xaml.cs"));

        xaml.Should().Contain("PortfolioImportFileReadiness");
        xaml.Should().Contain("PortfolioImportManualReadiness");
        xaml.Should().Contain("Command=\"{Binding BrowseFileCommand}\"");
        xaml.Should().Contain("Command=\"{Binding ImportFileCommand}\"");
        xaml.Should().Contain("Command=\"{Binding ImportIndexCommand}\"");
        xaml.Should().Contain("Command=\"{Binding AddManualSymbolsCommand}\"");
        xaml.Should().Contain("{Binding FileImportGuidanceTitle}");
        xaml.Should().Contain("{Binding FileImportGuidanceText}");
        xaml.Should().Contain("{Binding ManualSymbolCountText}");
        xaml.Should().Contain("{Binding ManualEntryGuidanceText}");
        xaml.Should().NotContain("Click=\"ImportFile_Click\"");
        xaml.Should().NotContain("Click=\"ImportIndex_Click\"");
        xaml.Should().NotContain("Click=\"AddManualSymbols_Click\"");

        codeBehind.Should().NotContain("ImportFile_Click");
        codeBehind.Should().NotContain("ImportIndex_Click");
        codeBehind.Should().NotContain("AddManualSymbols_Click");
    }
}
