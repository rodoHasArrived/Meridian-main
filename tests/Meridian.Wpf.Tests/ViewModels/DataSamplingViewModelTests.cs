using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class DataSamplingViewModelTests
{
    [Fact]
    public void NewViewModel_DisablesGenerateAndExplainsMissingSetup()
    {
        var viewModel = new DataSamplingViewModel();

        viewModel.CanGenerateSample().Should().BeFalse();
        viewModel.GenerateSampleCommand.CanExecute(null).Should().BeFalse();
        viewModel.SamplingReadinessTitle.Should().Be("Sample setup incomplete");
        viewModel.SamplingReadinessDetail.Should().Contain("Sample name is required");
        viewModel.SamplingReadinessDetail.Should().Contain("At least one symbol is required");
        viewModel.SymbolScopeText.Should().Be("No symbols added");
        viewModel.RecentSamplesStateText.Should().Be("No sample runs retained in this session yet.");
    }

    [Fact]
    public void AddSymbolCommand_NormalizesDeduplicatesAndRefreshesReadiness()
    {
        var viewModel = new DataSamplingViewModel
        {
            SampleName = "Regression pack",
            SymbolInput = " aapl "
        };

        viewModel.AddSymbolCommand.CanExecute(null).Should().BeTrue();
        viewModel.AddSymbolCommand.Execute(null);

        viewModel.Symbols.Should().Equal("AAPL");
        viewModel.SymbolInput.Should().BeEmpty();
        viewModel.SymbolScopeText.Should().Be("1 symbol: AAPL");
        viewModel.StatusMessage.Should().Be("Added AAPL to the sample scope.");

        viewModel.SymbolInput = "AAPL";

        viewModel.AddSymbolCommand.CanExecute(null).Should().BeFalse();
        viewModel.Symbols.Should().ContainSingle();
    }

    [Fact]
    public void ValidSetup_EnablesGenerateAndQueuesRecentSample()
    {
        var viewModel = new DataSamplingViewModel
        {
            SampleName = "Opening auction replay",
            FromDate = new DateTime(2026, 04, 01),
            ToDate = new DateTime(2026, 04, 03)
        };
        viewModel.SymbolInput = "spy";
        viewModel.AddSymbolCommand.Execute(null);

        viewModel.CanGenerateSample().Should().BeTrue();
        viewModel.GenerateSampleCommand.CanExecute(null).Should().BeTrue();
        viewModel.SamplingReadinessTitle.Should().Be("Sample ready");
        viewModel.SamplingReadinessDetail.Should().Contain("500 rows");
        viewModel.SamplingReadinessDetail.Should().Contain("1 symbol: SPY");

        viewModel.GenerateSampleCommand.Execute(null);

        viewModel.RecentSamples.Should().ContainSingle();
        viewModel.RecentSamples[0].Name.Should().Be("Opening auction replay");
        viewModel.RecentSamples[0].SymbolCount.Should().Be(1);
        viewModel.StatusMessage.Should().Be("Sample \"Opening auction replay\" queued with 1 symbols.");
        viewModel.RecentSamplesStateText.Should().Be("1 sample run retained in this session.");
    }

    [Fact]
    public void InvalidDateOrDataTypeScope_DisablesGenerateAndUpdatesGuidance()
    {
        var viewModel = new DataSamplingViewModel
        {
            SampleName = "Bad scope",
            FromDate = new DateTime(2026, 04, 03),
            ToDate = new DateTime(2026, 04, 01),
            IncludeTrades = false,
            IncludeQuotes = false
        };
        viewModel.SymbolInput = "msft";
        viewModel.AddSymbolCommand.Execute(null);

        viewModel.CanGenerateSample().Should().BeFalse();
        viewModel.GenerateSampleCommand.CanExecute(null).Should().BeFalse();
        viewModel.SamplingReadinessTitle.Should().Be("Sample setup incomplete");
        viewModel.SamplingReadinessDetail.Should().Contain("Start date must be before the end date");
        viewModel.SamplingReadinessDetail.Should().Contain("Select at least one data type");
    }

    [Fact]
    public void SavePresetCommand_RequiresUniqueSampleName()
    {
        var viewModel = new DataSamplingViewModel
        {
            SampleName = "Exploratory"
        };

        viewModel.CanSavePreset().Should().BeFalse();
        viewModel.SavePresetCommand.CanExecute(null).Should().BeFalse();

        viewModel.SampleName = "Opening Auction";

        viewModel.SavePresetCommand.CanExecute(null).Should().BeTrue();
        viewModel.SavePresetCommand.Execute(null);

        viewModel.Presets.Should().Contain("Opening Auction");
        viewModel.SelectedPreset.Should().Be("Opening Auction");
        viewModel.StatusMessage.Should().Be("Preset \"Opening Auction\" saved.");
        viewModel.SavePresetCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DataSamplingPageSource_BindsActionsThroughViewModelCommands()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataSamplingPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataSamplingPage.xaml.cs"));

        xaml.Should().Contain("DataSamplingReadinessCard");
        xaml.Should().Contain("{Binding SamplingReadinessTitle}");
        xaml.Should().Contain("{Binding SamplingReadinessDetail}");
        xaml.Should().Contain("{Binding SymbolScopeText}");
        xaml.Should().Contain("{Binding RecentSamplesStateText}");
        xaml.Should().Contain("Command=\"{Binding AddSymbolCommand}\"");
        xaml.Should().Contain("Command=\"{Binding GenerateSampleCommand}\"");
        xaml.Should().Contain("Command=\"{Binding SavePresetCommand}\"");
        xaml.Should().NotContain("Click=\"AddSymbol_Click\"");
        xaml.Should().NotContain("Click=\"GenerateSample_Click\"");
        xaml.Should().NotContain("Click=\"SavePreset_Click\"");

        codeBehind.Should().NotContain("AddSymbol_Click");
        codeBehind.Should().NotContain("GenerateSample_Click");
        codeBehind.Should().NotContain("SavePreset_Click");
    }
}
