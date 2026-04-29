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
    public void NewViewModel_DisablesScheduledExportAndExplainsSetup()
    {
        var viewModel = new DataExportViewModel();

        viewModel.IsScheduleEnabled.Should().BeFalse();
        viewModel.CanConfigureScheduledExport.Should().BeFalse();
        viewModel.ConfigureScheduledExportCommand.CanExecute(null).Should().BeFalse();
        viewModel.ScheduleReadinessTitle.Should().Be("Scheduled exports disabled");
        viewModel.ScheduleReadinessDetail.Should().Contain("Enable scheduled exports");
        viewModel.ScheduleScopeText.Should().Be("Disabled");
    }

    [Fact]
    public void EnabledSchedule_RequiresDestinationBeforeSave()
    {
        var viewModel = new DataExportViewModel
        {
            IsScheduleEnabled = true
        };

        viewModel.CanConfigureScheduledExport.Should().BeFalse();
        viewModel.ConfigureScheduledExportCommand.CanExecute(null).Should().BeFalse();
        viewModel.ScheduleReadinessTitle.Should().Be("Schedule setup incomplete");
        viewModel.ScheduleReadinessDetail.Should().Contain("destination path");
        viewModel.ScheduleScopeText.Should().Contain("Daily");
    }

    [Fact]
    public void EnabledSchedule_WithInvalidTime_DisablesSaveAndShowsValidation()
    {
        var viewModel = new DataExportViewModel
        {
            IsScheduleEnabled = true,
            ScheduleDestinationPath = "D:\\Exports",
            ScheduleTimeText = "not-a-time"
        };

        viewModel.CanConfigureScheduledExport.Should().BeFalse();
        viewModel.ConfigureScheduledExportCommand.CanExecute(null).Should().BeFalse();
        viewModel.IsScheduleTimeErrorVisible.Should().BeTrue();
        viewModel.ScheduleTimeError.Should().Be("Enter a valid time (HH:mm).");
        viewModel.ScheduleReadinessTitle.Should().Be("Schedule setup incomplete");
        viewModel.ScheduleReadinessDetail.Should().Contain("HH:mm");
    }

    [Fact]
    public void EnabledSchedule_WithDestinationAndValidTime_CanSave()
    {
        var viewModel = new DataExportViewModel
        {
            IsScheduleEnabled = true,
            ScheduleDestinationPath = "D:\\Exports",
            ScheduleTimeText = "06:30"
        };

        viewModel.CanConfigureScheduledExport.Should().BeTrue();
        viewModel.ConfigureScheduledExportCommand.CanExecute(null).Should().BeTrue();
        viewModel.ScheduleReadinessTitle.Should().Be("Schedule ready");
        viewModel.ScheduleReadinessDetail.Should().Contain("06:30 local");
        viewModel.ScheduleReadinessDetail.Should().Contain("D:\\Exports");
        viewModel.ScheduleScopeText.Should().Be("Daily - 06:30 local");

        viewModel.ConfigureScheduledExportCommand.Execute(null);

        viewModel.IsActionInfoVisible.Should().BeTrue();
        viewModel.IsActionInfoError.Should().BeFalse();
        viewModel.ActionInfoText.Should().Contain("Daily export scheduled");
    }

    [Theory]
    [InlineData(false, "08:00", "D:\\Exports", false)]
    [InlineData(true, "bad", "D:\\Exports", false)]
    [InlineData(true, "08:00", "", false)]
    [InlineData(true, "08:00", "D:\\Exports", true)]
    public void CanConfigureScheduledExportForState_RequiresEnabledValidTimeAndDestination(
        bool enabled,
        string time,
        string destination,
        bool expected)
    {
        DataExportViewModel.CanConfigureScheduledExportForState(enabled, time, destination)
            .Should()
            .Be(expected);
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

    [Fact]
    public void DataExportPageSource_BindsScheduledExportReadinessThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml.cs"));

        xaml.Should().Contain("DataExportScheduleReadinessCard");
        xaml.Should().Contain("{Binding IsScheduleEnabled, Mode=TwoWay}");
        xaml.Should().Contain("{Binding ScheduleReadinessTitle}");
        xaml.Should().Contain("{Binding ScheduleReadinessDetail}");
        xaml.Should().Contain("{Binding ScheduleScopeText}");
        xaml.Should().Contain("{Binding ScheduleDestinationPath, UpdateSourceTrigger=PropertyChanged}");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataExportConfigureScheduleButton\"");
        xaml.Should().Contain("Command=\"{Binding ConfigureScheduledExportCommand}\"");
        xaml.Should().NotContain("EnableScheduledExportsToggle_Changed");

        codeBehind.Should().NotContain("EnableScheduledExportsToggle_Changed");
    }
}
