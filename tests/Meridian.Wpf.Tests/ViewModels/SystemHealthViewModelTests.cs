using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SystemHealthViewModelTests
{
    [Fact]
    public void BuildSystemTriage_WithUnhealthyProviders_PrioritizesProviderReview()
    {
        var state = SystemHealthViewModel.BuildSystemTriage(
            providerCount: 3,
            unhealthyProviderCount: 1,
            hasProviderSnapshot: true,
            hasStorageSnapshot: true,
            diskUsagePercent: 41.2,
            storageIssueCount: 0,
            storageCorruptedFiles: 0,
            storageOrphanedFiles: 0,
            eventCount: 2,
            errorEventCount: 0,
            warningEventCount: 0,
            hasEventSnapshot: true);

        state.Tone.Should().Be(SystemHealthTriageTone.Critical);
        state.Title.Should().Be("Provider health needs attention");
        state.ActionText.Should().Be("Inspect unhealthy providers");
        state.TargetText.Should().Be("Provider health list");
        state.EvidenceText.Should().Contain("2/3 providers healthy");
    }

    [Fact]
    public void BuildSystemTriage_WithStoragePressure_PrioritizesDiagnostics()
    {
        var state = SystemHealthViewModel.BuildSystemTriage(
            providerCount: 2,
            unhealthyProviderCount: 0,
            hasProviderSnapshot: true,
            hasStorageSnapshot: true,
            diskUsagePercent: 91.4,
            storageIssueCount: 0,
            storageCorruptedFiles: 0,
            storageOrphanedFiles: 0,
            eventCount: 0,
            errorEventCount: 0,
            warningEventCount: 0,
            hasEventSnapshot: true);

        state.Tone.Should().Be(SystemHealthTriageTone.Critical);
        state.Title.Should().Be("Storage posture needs review");
        state.Detail.Should().Contain("91.4%");
        state.ActionText.Should().Be("Generate diagnostics bundle");
        state.TargetText.Should().Be("Storage health panel");
    }

    [Fact]
    public void BuildSystemTriage_WithRecentErrors_TargetsEventReview()
    {
        var state = SystemHealthViewModel.BuildSystemTriage(
            providerCount: 2,
            unhealthyProviderCount: 0,
            hasProviderSnapshot: true,
            hasStorageSnapshot: true,
            diskUsagePercent: 48,
            storageIssueCount: 0,
            storageCorruptedFiles: 0,
            storageOrphanedFiles: 0,
            eventCount: 5,
            errorEventCount: 1,
            warningEventCount: 2,
            hasEventSnapshot: true);

        state.Tone.Should().Be(SystemHealthTriageTone.Warning);
        state.Title.Should().Be("Recent errors need triage");
        state.ActionText.Should().Be("Review recent events");
        state.EventSummaryText.Should().Be("5 recent events; 1 errors; 2 warnings");
    }

    [Fact]
    public void BuildSystemTriage_WithCleanSignals_IsReady()
    {
        var state = SystemHealthViewModel.BuildSystemTriage(
            providerCount: 2,
            unhealthyProviderCount: 0,
            hasProviderSnapshot: true,
            hasStorageSnapshot: true,
            diskUsagePercent: 42,
            storageIssueCount: 0,
            storageCorruptedFiles: 0,
            storageOrphanedFiles: 0,
            eventCount: 0,
            errorEventCount: 0,
            warningEventCount: 0,
            hasEventSnapshot: true);

        state.Tone.Should().Be(SystemHealthTriageTone.Ready);
        state.Title.Should().Be("System posture ready");
        state.ActionText.Should().Be("Continue monitoring");
        state.ProviderSummaryText.Should().Be("2/2 providers healthy");
    }

    [Fact]
    public void BuildSystemTriage_NormalizesInvalidInputs()
    {
        var state = SystemHealthViewModel.BuildSystemTriage(
            providerCount: -1,
            unhealthyProviderCount: 99,
            hasProviderSnapshot: false,
            hasStorageSnapshot: false,
            diskUsagePercent: double.NaN,
            storageIssueCount: -2,
            storageCorruptedFiles: -3,
            storageOrphanedFiles: -4,
            eventCount: -5,
            errorEventCount: 9,
            warningEventCount: 9,
            hasEventSnapshot: false);

        state.Tone.Should().Be(SystemHealthTriageTone.Waiting);
        state.EvidenceText.Should().Be("Providers waiting; Storage waiting; Events waiting");
    }

    [Fact]
    public void BuildProviderEmptyState_DistinguishesPendingScanFromEmptySnapshot()
    {
        var pending = SystemHealthViewModel.BuildProviderEmptyState(hasProviderSnapshot: false);
        var emptySnapshot = SystemHealthViewModel.BuildProviderEmptyState(hasProviderSnapshot: true);

        pending.Title.Should().Be("Provider scan pending");
        pending.Detail.Should().Contain("Refresh health data");
        emptySnapshot.Title.Should().Be("No providers reported");
        emptySnapshot.Detail.Should().Contain("Connect or enable a provider");
    }

    [Fact]
    public void BuildEventEmptyState_DistinguishesPendingScanFromEmptySnapshot()
    {
        var pending = SystemHealthViewModel.BuildEventEmptyState(hasEventSnapshot: false);
        var emptySnapshot = SystemHealthViewModel.BuildEventEmptyState(hasEventSnapshot: true);

        pending.Title.Should().Be("Event scan pending");
        pending.Detail.Should().Contain("Refresh health data");
        emptySnapshot.Title.Should().Be("No recent desktop events");
        emptySnapshot.Detail.Should().Contain("Continue monitoring");
    }

    [Fact]
    public void BuildInspectors_ShouldExposeTriageProviderAndEventFacts()
    {
        var triage = SystemHealthViewModel.BuildSystemTriage(
            providerCount: 2,
            unhealthyProviderCount: 1,
            hasProviderSnapshot: true,
            hasStorageSnapshot: true,
            diskUsagePercent: 44,
            storageIssueCount: 0,
            storageCorruptedFiles: 0,
            storageOrphanedFiles: 0,
            eventCount: 1,
            errorEventCount: 0,
            warningEventCount: 0,
            hasEventSnapshot: true);

        var triageInspector = SystemHealthViewModel.BuildTriageInspector(triage);
        var providerInspector = SystemHealthViewModel.BuildProviderInspector(new SystemHealthViewModel.ProviderHealthItem
        {
            Name = "Market Data",
            Status = "Disconnected",
            LatencyText = "250ms",
            EventsText = "0.0/s"
        });
        var eventInspector = SystemHealthViewModel.BuildEventInspector(new SystemHealthViewModel.SystemEventItem
        {
            Source = "Collector",
            Severity = "warning",
            Message = "Provider latency above threshold",
            TimeText = "2m ago"
        });

        triageInspector.Title.Should().Be("Provider health needs attention");
        triageInspector.Facts.Should().Contain(fact => fact.Label == "Action" && fact.Value == "Inspect unhealthy providers");
        providerInspector.Title.Should().Be("Market Data");
        providerInspector.Facts.Should().Contain(fact => fact.Label == "Latency" && fact.Value == "250ms");
        eventInspector.Title.Should().Be("Collector");
        eventInspector.Facts.Should().Contain(fact => fact.Label == "Severity" && fact.Value == "warning");
    }

    [Fact]
    public void SystemHealthPageSource_ShouldUseDenseTablesInspectorsAndActionTooltips()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SystemHealthPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SystemHealthViewModel.cs"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<ListView");
        xaml.Should().Contain("SystemHealthActionStrip");
        xaml.Should().Contain("SystemHealthWorkbench");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("InspectorPanelControl");
        xaml.Should().Contain("SystemTriageInspector");
        xaml.Should().Contain("HealthActionStateTitle");
        xaml.Should().Contain("HealthActionStateDetail");
        xaml.Should().Contain("SystemTriageActionText");
        xaml.Should().Contain("SystemTriageTargetText");
        xaml.Should().Contain("SystemHealthTriageBriefingCard");
        xaml.Should().Contain("SystemHealthTriageHandoffPanel");
        xaml.Should().Contain("SystemHealthProviderHealthGrid");
        xaml.Should().Contain("SystemHealthEventsGrid");
        xaml.Should().Contain("Table=\"{Binding ProvidersTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedProvider, Mode=TwoWay}\"");
        xaml.Should().Contain("Table=\"{Binding EventsTable}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedEvent, Mode=TwoWay}\"");
        xaml.Should().Contain("SystemHealthProviderInspector");
        xaml.Should().Contain("SystemHealthEventInspector");
        xaml.Should().Contain("SystemHealthProviderEmptyStatePanel");
        xaml.Should().Contain("SystemHealthProviderEmptyStateTitle");
        xaml.Should().Contain("SystemHealthProviderEmptyStateDetail");
        xaml.Should().Contain("SystemHealthEventEmptyStatePanel");
        xaml.Should().Contain("SystemHealthEventEmptyStateTitle");
        xaml.Should().Contain("SystemHealthEventEmptyStateDetail");
        xaml.Should().Contain("Command=\"{Binding RefreshCommand}\"");
        xaml.Should().Contain("Command=\"{Binding GenerateDiagnosticsCommand}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RefreshTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding DiagnosticsTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().NotContain("Refresh_Click");
        xaml.Should().NotContain("GenerateDiagnostics_Click");
        xaml.Should().Contain("{Binding ProviderEmptyStateTitle}");
        xaml.Should().Contain("{Binding ProviderEmptyStateDetail}");
        xaml.Should().Contain("{Binding EventEmptyStateTitle}");
        xaml.Should().Contain("{Binding EventEmptyStateDetail}");
        viewModel.Should().Contain("public WorkstationTableModel<ProviderHealthItem> ProvidersTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<SystemEventItem> EventsTable { get; }");
        viewModel.Should().Contain("public InspectorPanelModel SystemTriageInspector");
        viewModel.Should().Contain("public InspectorPanelModel SelectedProviderInspector");
        viewModel.Should().Contain("public InspectorPanelModel SelectedEventInspector");
    }
}
