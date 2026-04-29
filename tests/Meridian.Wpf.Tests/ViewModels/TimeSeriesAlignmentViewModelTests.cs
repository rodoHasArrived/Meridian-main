using Meridian.Ui.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class TimeSeriesAlignmentViewModelTests
{
    [Fact]
    public void InitialState_DisablesRunAndExplainsMissingSetup()
    {
        var viewModel = CreateSubject();

        viewModel.CanRunAlignment.Should().BeFalse();
        viewModel.RunAlignmentCommand.CanExecute(null).Should().BeFalse();
        viewModel.AlignmentReadinessTitle.Should().Be("Alignment setup incomplete");
        viewModel.AlignmentReadinessDetail.Should().Contain("Add at least two symbols");
        viewModel.AlignmentReadinessDetail.Should().Contain("Choose a from and to date");
        viewModel.IsRecentAlignmentsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddAndRemoveSymbols_NormalizesDeduplicatesAndRefreshesReadiness()
    {
        var viewModel = CreateSubject();

        viewModel.SymbolInput = " spy, qqq;SPY ";
        viewModel.AddSymbolsCommand.Execute(null);

        viewModel.SelectedSymbols.Should().Equal("SPY", "QQQ");
        viewModel.SymbolInput.Should().BeEmpty();
        viewModel.SelectedSymbolsCountText.Should().Be("2 symbols selected");

        viewModel.RemoveSymbolCommand.Execute("spy");

        viewModel.SelectedSymbols.Should().Equal("QQQ");
        viewModel.SelectedSymbolsCountText.Should().Be("1 symbol selected");
    }

    [Fact]
    public void PresetSelection_UpdatesFrequencyGapAndFieldsInViewModel()
    {
        var viewModel = CreateSubject();

        viewModel.SelectedPresetKey = "pairs";

        viewModel.SelectedFrequencyKey.Should().Be("5m");
        viewModel.SelectedGapStrategyKey.Should().Be("drop");
        viewModel.IncludeClose.Should().BeTrue();
        viewModel.IncludeOpen.Should().BeTrue();
        viewModel.IncludeHigh.Should().BeTrue();
        viewModel.IncludeLow.Should().BeTrue();
        viewModel.IncludeVolume.Should().BeTrue();
        viewModel.SelectedFieldsText.Should().Be("Close, Open, High, Low, Volume");
    }

    [Fact]
    public async Task RunAlignmentCommand_WhenReady_BuildsOptionsAndProjectsResults()
    {
        AlignmentOptions? capturedOptions = null;
        var viewModel = CreateSubject((options, _) =>
        {
            capturedOptions = options;
            return Task.FromResult(new AlignmentResult
            {
                Success = true,
                AlignedRecords = 12345,
                GapsFilled = 17,
                Duration = TimeSpan.FromSeconds(2.4),
                OutputPath = @"C:\temp\alignment.csv"
            });
        });

        viewModel.AlignmentName = "Pairs Test";
        viewModel.SymbolInput = "MSFT,AAPL";
        viewModel.AddSymbolsCommand.Execute(null);
        viewModel.FromDate = new DateTime(2026, 1, 1);
        viewModel.ToDate = new DateTime(2026, 1, 31);
        viewModel.SelectedPresetKey = "pairs";
        viewModel.SelectedExportFormatKey = "Csv";

        viewModel.CanRunAlignment.Should().BeTrue();
        await viewModel.RunAlignmentCommand.ExecuteAsync(null);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.Symbols.Should().Equal("MSFT", "AAPL");
        capturedOptions.Interval.Should().Be(TimeSeriesInterval.Minute5);
        capturedOptions.GapStrategy.Should().Be(GapStrategy.Skip);
        capturedOptions.FromDate.Should().Be(new DateOnly(2026, 1, 1));
        capturedOptions.ToDate.Should().Be(new DateOnly(2026, 1, 31));
        capturedOptions.OutputFormat.Should().Be(ExportFormat.Csv);

        viewModel.HasResults.Should().BeTrue();
        viewModel.ResultRowsText.Should().Be("12,345");
        viewModel.ResultSymbolsText.Should().Be("2");
        viewModel.ResultGapsText.Should().Be("17");
        viewModel.ResultFormatText.Should().Be("CSV");
        viewModel.ResultOutputPathText.Should().Contain(@"C:\temp\alignment.csv");
        viewModel.StatusText.Should().Be("Alignment complete in 2.4s.");
        viewModel.RecentAlignments.Should().ContainSingle(entry => entry.Name == "Pairs Test");
        viewModel.IsRecentAlignmentsEmpty.Should().BeFalse();
    }

    [Fact]
    public void InvalidDateRange_DisablesRunAndShowsInlineGuidance()
    {
        var viewModel = CreateSubject();
        viewModel.SymbolInput = "SPY,QQQ";
        viewModel.AddSymbolsCommand.Execute(null);
        viewModel.FromDate = new DateTime(2026, 2, 1);
        viewModel.ToDate = new DateTime(2026, 1, 1);

        viewModel.CanRunAlignment.Should().BeFalse();
        viewModel.RunAlignmentCommand.CanExecute(null).Should().BeFalse();
        viewModel.AlignmentReadinessTitle.Should().Be("Alignment setup incomplete");
        viewModel.AlignmentReadinessDetail.Should().Contain("Start date must be before end date");
    }

    [Fact]
    public void TimeSeriesAlignmentPageSource_UsesViewModelBindingsAndCommands()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\TimeSeriesAlignmentPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\TimeSeriesAlignmentPage.xaml.cs"));

        xaml.Should().Contain("TimeSeriesAlignmentReadinessCard");
        xaml.Should().Contain("{Binding AlignmentReadinessTitle}");
        xaml.Should().Contain("{Binding AlignmentReadinessDetail}");
        xaml.Should().Contain("Command=\"{Binding AddSymbolsCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RunAlignmentCommand}\"");
        xaml.Should().Contain("Command=\"{Binding SavePresetCommand}\"");
        xaml.Should().Contain("{Binding SelectedSymbols}");
        xaml.Should().Contain("{Binding HasResults");
        xaml.Should().Contain("TimeSeriesAlignmentRecentEmptyState");
        xaml.Should().NotContain("Click=\"RunAlignment_Click\"");
        xaml.Should().NotContain("SelectionChanged=\"Preset_SelectionChanged\"");

        codeBehind.Should().Contain("new TimeSeriesAlignmentViewModel()");
        codeBehind.Should().NotContain("TimeSeriesAlignmentService.Instance");
        codeBehind.Should().NotContain("RunAlignment_Click");
        codeBehind.Should().NotContain("Preset_SelectionChanged");
    }

    private static TimeSeriesAlignmentViewModel CreateSubject(
        Func<AlignmentOptions, CancellationToken, Task<AlignmentResult>>? alignDataAsync = null,
        Func<AlignmentOptions, AlignmentValidationResult>? validateOptions = null)
    {
        return new TimeSeriesAlignmentViewModel(
            alignDataAsync ?? ((_, _) => Task.FromResult(new AlignmentResult { Success = true })),
            validateOptions ?? (_ => new AlignmentValidationResult { IsValid = true }));
    }
}
