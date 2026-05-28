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
    public void NewViewModel_ExposesSelectorOptionsAndDefaultSelections()
    {
        var viewModel = new DataExportViewModel();

        viewModel.ExportFormatOptions.Select(option => option.Key).Should().Contain(new[] { "csv", "parquet", "jsonl" });
        viewModel.CompressionOptions.Select(option => option.Key).Should().Contain(new[] { "none", "gzip", "zstd" });
        viewModel.DatabaseTypeOptions.Select(option => option.Key).Should().Contain(new[] { "postgresql", "clickhouse", "sqlite" });
        viewModel.ScheduleFrequencyOptions.Select(option => option.Key).Should().Contain(new[] { "hourly", "daily", "weekly" });
        viewModel.WebhookFormatOptions.Select(option => option.Key).Should().Contain(new[] { "json", "msgpack", "protobuf" });
        viewModel.WebhookBatchOptions.Select(option => option.Key).Should().Contain(new[] { "realtime", "microbatch", "bulk" });
        viewModel.LeanResolutionOptions.Select(option => option.Key).Should().Contain(new[] { "tick", "minute", "daily" });

        viewModel.SelectedExportFormat.Should().Be("csv");
        viewModel.SelectedCompression.Should().Be("gzip");
        viewModel.SelectedDatabaseType.Should().Be("postgresql");
        viewModel.SelectedScheduleFrequency.Should().Be("daily");
        viewModel.SelectedWebhookFormat.Should().Be("json");
        viewModel.SelectedWebhookBatch.Should().Be("microbatch");
        viewModel.SelectedLeanResolution.Should().Be("minute");
    }

    [Fact]
    public void SelectorChanges_UpdateReadinessAndDerivedDefaultsInViewModel()
    {
        var viewModel = new DataExportViewModel();

        viewModel.SelectedExportFormat = "parquet";
        viewModel.SelectedCompression = "zstd";
        viewModel.SelectedDatabaseType = "clickhouse";
        viewModel.SelectedScheduleFrequency = "weekly";

        viewModel.ExportReadinessDetail.Should().Contain("Parquet export");
        viewModel.ExportReadinessDetail.Should().Contain("Zstd compression");
        viewModel.DatabasePort.Should().Be("8123");

        viewModel.IsScheduleEnabled = true;
        viewModel.ScheduleDestinationPath = "D:\\Exports";
        viewModel.ScheduleReadinessDetail.Should().Contain("Weekly export");
        viewModel.ScheduleScopeText.Should().Be("Weekly - 08:00 local");

        viewModel.SelectedDatabaseType = "sqlite";

        viewModel.DatabasePort.Should().Be("0");
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
        xaml.Should().Contain("ItemsSource=\"{Binding ExportFormatOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedExportFormat");
        xaml.Should().Contain("ItemsSource=\"{Binding CompressionOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedCompression");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataExportRunButton\"");
        xaml.Should().Contain("Command=\"{Binding ExportDataCommand}\"");
        xaml.Should().Contain("Value=\"{Binding ExportProgressValue, Mode=OneWay}\"");
        xaml.Should().NotContain("Click=\"ExportData_Click\"");
        xaml.Should().NotContain("ExportFormatCombo_SelectionChanged");
        xaml.Should().NotContain("CompressionCombo_SelectionChanged");

        codeBehind.Should().NotContain("ExportData_Click");
        codeBehind.Should().NotContain("GetComboTag");
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
        xaml.Should().Contain("ItemsSource=\"{Binding ScheduleFrequencyOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedScheduleFrequency");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataExportConfigureScheduleButton\"");
        xaml.Should().Contain("Command=\"{Binding ConfigureScheduledExportCommand}\"");
        xaml.Should().NotContain("EnableScheduledExportsToggle_Changed");
        xaml.Should().NotContain("ScheduleFrequency_SelectionChanged");

        codeBehind.Should().NotContain("EnableScheduledExportsToggle_Changed");
        codeBehind.Should().NotContain("ScheduleFrequency_SelectionChanged");
    }

    [Fact]
    public void DataExportPageSource_BindsIntegrationSelectorsThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml.cs"));

        xaml.Should().Contain("ItemsSource=\"{Binding DatabaseTypeOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedDatabaseType");
        xaml.Should().Contain("ItemsSource=\"{Binding WebhookFormatOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedWebhookFormat");
        xaml.Should().Contain("ItemsSource=\"{Binding WebhookBatchOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedWebhookBatch");
        xaml.Should().Contain("ItemsSource=\"{Binding LeanResolutionOptions}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedLeanResolution");
        xaml.Should().NotContain("SelectionChanged=");
        xaml.Should().NotContain("<ComboBoxItem");

        codeBehind.Should().NotContain("SelectionChangedEventArgs");
        codeBehind.Should().NotContain("SelectedIndex");
        codeBehind.Should().NotContain("GetComboTag");
    }
}
