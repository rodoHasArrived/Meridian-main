using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Contracts.Api;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BackfillViewModelTests
{
    [Fact]
    public void BuildStartSetupState_WithoutSymbols_DisablesStartAndExplainsRecovery()
    {
        var state = BackfillViewModel.BuildStartSetupState(
            symbolsText: "",
            fromDate: new DateTime(2026, 4, 1),
            toDate: new DateTime(2026, 4, 28),
            providerDisplay: "Multi-Source (Auto-Failover)",
            provider: "composite",
            granularity: "Daily");

        state.CanStart.Should().BeFalse();
        state.ReadinessTitle.Should().Be("Backfill setup incomplete");
        state.ReadinessDetail.Should().Contain("Add at least one symbol");
        state.SymbolCountText.Should().Be("0 symbols");
        state.SymbolsValidationError.Should().Be("Please enter at least one symbol");
        state.IsSymbolsValidationErrorVisible.Should().BeTrue();
    }

    [Fact]
    public void BuildStartSetupState_WithInvalidDateRange_DisablesStartAndSurfacesBoundValidation()
    {
        var state = BackfillViewModel.BuildStartSetupState(
            symbolsText: "SPY, QQQ",
            fromDate: new DateTime(2026, 4, 28),
            toDate: new DateTime(2026, 4, 1),
            providerDisplay: "Yahoo Finance (Free)",
            provider: "yahoo",
            granularity: "5Min");

        state.CanStart.Should().BeFalse();
        state.ReadinessDetail.Should().Contain("Fix the date range");
        state.FromDateValidationError.Should().Be("From date must be earlier than To date");
        state.ToDateValidationError.Should().Be("To date must be on or after From date");
        state.IsFromDateValidationErrorVisible.Should().BeTrue();
        state.IsToDateValidationErrorVisible.Should().BeTrue();
    }

    [Fact]
    public void BuildStartSetupState_WithValidRequest_DeduplicatesSymbolsAndDescribesScope()
    {
        var state = BackfillViewModel.BuildStartSetupState(
            symbolsText: " spy, SPY, qqq ",
            fromDate: new DateTime(2026, 4, 1),
            toDate: new DateTime(2026, 4, 28),
            providerDisplay: "Multi-Source (Auto-Failover)",
            provider: "composite",
            granularity: "Hourly");

        state.CanStart.Should().BeTrue();
        state.ReadinessTitle.Should().Be("Backfill ready");
        state.ReadinessDetail.Should().Contain("hourly history");
        state.ReadinessDetail.Should().Contain("SPY, QQQ");
        state.ScopeText.Should().Contain("SPY, QQQ");
        state.ScopeText.Should().Contain("Apr 1, 2026 to Apr 28, 2026");
        state.SymbolCountText.Should().Be("2 symbols");
        state.IsSymbolsValidationErrorVisible.Should().BeFalse();
    }

    [Fact]
    public void BackfillPageSource_BindsStartReadinessThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\BackfillViewModel.cs"));

        xaml.Should().Contain("BackfillActionStrip");
        xaml.Should().Contain("BackfillStartReadinessCard");
        xaml.Should().Contain("{Binding StartReadinessTitle}");
        xaml.Should().Contain("{Binding StartReadinessDetail}");
        xaml.Should().Contain("{Binding StartReadinessScopeText}");
        xaml.Should().Contain("{Binding SymbolsValidationErrorText}");
        xaml.Should().Contain("{Binding FromDateValidationErrorText}");
        xaml.Should().Contain("{Binding ToDateValidationErrorText}");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"BackfillStartButton\"");
        xaml.Should().Contain("IsEnabled=\"{Binding CanStartBackfill}\"");
        xaml.Should().Contain("ToolTip=\"{Binding StartReadinessDetail}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");

        codeBehind.Should().Contain("RefreshStartSetupState();");
        codeBehind.Should().NotContain("SymbolsValidationError.Text =");
        codeBehind.Should().NotContain("FromDateValidationError.Text =");
        codeBehind.Should().NotContain("ToDateValidationError.Text =");
        codeBehind.Should().Contain("await _viewModel.ActivateAsync()");
        codeBehind.Should().Contain("_viewModel.Deactivate()");
        codeBehind.Should().NotContain("_viewModel.StartAsync()");
        codeBehind.Should().NotContain("_viewModel.Stop()");
        viewModel.Should().Contain("IPageActivationLifetime");
        viewModel.Should().Contain("public bool IsActive");
        viewModel.Should().Contain("public CancellationToken ActivationToken");
    }

    [Fact]
    public void BuildBackfillStats_WithSuccessfulStatus_ProjectsRealRunSummary()
    {
        var status = new BackfillResultDto
        {
            Success = true,
            BarsWritten = 12345,
            Symbols = ["SPY", "QQQ", "SPY"],
            StartedUtc = new DateTimeOffset(2026, 5, 25, 16, 0, 0, TimeSpan.Zero),
            CompletedUtc = new DateTimeOffset(2026, 5, 25, 16, 17, 0, TimeSpan.Zero)
        };

        var stats = BackfillViewModel.BuildBackfillStats(status);

        stats.TotalBarsText.Should().Be("12,345");
        stats.SymbolsProcessedText.Should().Be("2 symbols");
        stats.RunWindowText.Should().Be($"{status.StartedUtc.Value.LocalDateTime:g} to {status.CompletedUtc.Value.LocalDateTime:g}");
        stats.LastSuccessfulRunText.Should().Be(status.CompletedUtc.Value.LocalDateTime.ToString("g"));
    }

    [Fact]
    public void BuildBackfillStats_WithFailedStatus_DoesNotClaimSuccessfulRun()
    {
        var status = new BackfillResultDto
        {
            Success = false,
            BarsWritten = 0,
            SymbolResults =
            [
                new SymbolBackfillResult { Symbol = "AAPL" },
                new SymbolBackfillResult { Symbol = "MSFT" }
            ],
            StartedUtc = new DateTimeOffset(2026, 5, 25, 15, 30, 0, TimeSpan.Zero)
        };

        var stats = BackfillViewModel.BuildBackfillStats(status);

        stats.TotalBarsText.Should().Be("0");
        stats.SymbolsProcessedText.Should().Be("2 symbols");
        stats.RunWindowText.Should().Be($"Started {status.StartedUtc.Value.LocalDateTime:g}");
        stats.LastSuccessfulRunText.Should().Be("Latest run failed");
    }

    [Fact]
    public void BackfillPageSource_BindsStatsCardThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml"));

        xaml.Should().Contain("{Binding BackfillStatsTotalBarsText}");
        xaml.Should().Contain("{Binding BackfillStatsSymbolsProcessedText}");
        xaml.Should().Contain("{Binding BackfillStatsRunWindowText}");
        xaml.Should().Contain("{Binding BackfillStatsLastSuccessfulRunText}");
        xaml.Should().Contain("Text=\"Total Bars Written\"");
        xaml.Should().Contain("Text=\"Symbols Processed\"");
        xaml.Should().Contain("Text=\"Latest Run Window\"");
        xaml.Should().NotContain("Text=\"1,234,567\"");
        xaml.Should().NotContain("Text=\"45\"");
        xaml.Should().NotContain("Text=\"5 years\"");
        xaml.Should().NotContain("Text=\"2 hours ago\"");
    }

    [Fact]
    public void BackfillWorkbenchSection_ShouldOwnProgressJobsAndGapPresentationWithAdapterBindings()
    {
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\BackfillViewModel.Sections.cs"));
        var viewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\BackfillViewModel.cs"));

        sectionSource.Should().Contain("public ObservableCollection<SymbolProgressInfo> SymbolProgress");
        sectionSource.Should().Contain("public WorkstationTableModel<SymbolProgressInfo> SymbolProgressTable");
        sectionSource.Should().Contain("public ObservableCollection<ScheduledJobInfo> ScheduledJobs");
        sectionSource.Should().Contain("public ObservableCollection<ResumableJobInfo> ResumableJobs");
        sectionSource.Should().Contain("public ObservableCollection<GapAnalysisItem> GapItems");
        sectionSource.Should().Contain("public ObservableCollection<BackfillRemediationQueuePresentation> RemediationQueue");
        sectionSource.Should().Contain("public WorkstationTableModel<GapAnalysisItem> GapItemsTable");
        sectionSource.Should().Contain("public string BackfillStatusText");
        sectionSource.Should().Contain("public string GapAnalysisSummaryText");
        sectionSource.Should().Contain("public BackfillWorkbenchSectionViewModel WorkbenchSection");
        viewModelSource.Should().Contain("public ObservableCollection<SymbolProgressInfo> SymbolProgress => WorkbenchSection.SymbolProgress");
        viewModelSource.Should().Contain("public WorkstationTableModel<SymbolProgressInfo> SymbolProgressTable => WorkbenchSection.SymbolProgressTable");
        viewModelSource.Should().Contain("public WorkstationTableModel<GapAnalysisItem> GapItemsTable => WorkbenchSection.GapItemsTable");
        viewModelSource.Should().Contain("get => WorkbenchSection.BackfillStatusText");
        viewModelSource.Should().Contain("get => WorkbenchSection.GapAnalysisSummaryText");
        viewModelSource.Should().NotContain("_backfillStatusText");
        viewModelSource.Should().NotContain("_overallProgressText");
        viewModelSource.Should().NotContain("_hasNoScheduledJobs");
        viewModelSource.Should().NotContain("_gapAnalysisSummaryText");
    }

    [Fact]
    public void BackfillWorkbenchSection_ShouldExposeDenseTerminalTables()
    {
        var section = new BackfillWorkbenchSectionViewModel();

        section.SymbolProgressTable.Title.Should().Be("Backfill per-symbol progress");
        section.SymbolProgressTable.Rows.Should().BeSameAs(section.SymbolProgress);
        section.SymbolProgressTable.Columns.Select(static column => column.Header).Should().ContainInOrder(
            "Symbol",
            "Range",
            "Provider",
            "Fallback Attempt",
            "Retry",
            "Progress",
            "Bars",
            "Live State",
            "Updated",
            "Error");

        section.GapItemsTable.Title.Should().Be("Backfill gap analysis");
        section.GapItemsTable.Rows.Should().BeSameAs(section.GapItems);
        section.GapItemsTable.Columns.Select(static column => column.Header).Should().ContainInOrder(
            "Symbol",
            "Coverage",
            "Gap Days",
            "Coverage %");
    }

    [Fact]
    public void BackfillPageSource_UsesSharedDenseTablesForTerminalLists()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml"));

        xaml.Should().Contain("workstation:DenseDataGridControl");
        xaml.Should().Contain("Table=\"{Binding GapItemsTable}\"");
        xaml.Should().Contain("GridAutomationId=\"BackfillGapAnalysisGrid\"");
        xaml.Should().Contain("EmptyAutomationId=\"BackfillGapAnalysisSharedEmptyState\"");
        xaml.Should().Contain("Table=\"{Binding SymbolProgressTable}\"");
        xaml.Should().Contain("GridAutomationId=\"BackfillSymbolProgressGrid\"");
        xaml.Should().Contain("EmptyAutomationId=\"BackfillSymbolProgressSharedEmptyState\"");
        xaml.Should().Contain("ItemsSource=\"{Binding RemediationQueue}\"");
        xaml.Should().Contain("SortMemberPath=\"SlaTierSort\"");
        xaml.Should().Contain("SortMemberPath=\"DueAtUtc\"");
        xaml.Should().Contain("Text=\"{Binding RemediationDefaultProviderText}\"");
    }

    [Fact]
    public void BackfillStatusSection_ShouldOwnApiStatusAndStatsPresentationWithAdapterBindings()
    {
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\BackfillViewModel.Sections.cs"));
        var viewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\BackfillViewModel.cs"));

        sectionSource.Should().Contain("public sealed class BackfillStatusSectionViewModel");
        sectionSource.Should().Contain("public BackfillResultDto? LastApiStatus");
        sectionSource.Should().Contain("public Visibility LastStatusVisibility");
        sectionSource.Should().Contain("public string LastRunStatusText");
        sectionSource.Should().Contain("public string BackfillStatsTotalBarsText");
        sectionSource.Should().Contain("public BackfillStatusSectionViewModel StatusSection");
        viewModelSource.Should().Contain("get => StatusSection.LastApiStatus");
        viewModelSource.Should().Contain("get => StatusSection.LastRunStatusText");
        viewModelSource.Should().Contain("get => StatusSection.BackfillStatsTotalBarsText");
        viewModelSource.Should().NotContain("_lastApiStatus");
        viewModelSource.Should().NotContain("_lastStatusVisibility");
        viewModelSource.Should().NotContain("_lastRunStatusText");
        viewModelSource.Should().NotContain("_backfillStatsTotalBarsText");
    }
}
