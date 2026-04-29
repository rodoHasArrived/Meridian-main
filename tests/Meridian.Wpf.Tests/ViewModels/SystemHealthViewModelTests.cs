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
        emptySnapshot.Title.Should().Be("No recent events retained");
        emptySnapshot.Detail.Should().Contain("Continue monitoring");
    }

    [Fact]
    public void SystemHealthPageSource_ShouldExposeTriageBriefing()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SystemHealthPage.xaml"));

        xaml.Should().Contain("System Health triage briefing");
        xaml.Should().Contain("SystemTriageTitle");
        xaml.Should().Contain("SystemTriageDetail");
        xaml.Should().Contain("SystemTriageActionText");
        xaml.Should().Contain("SystemTriageTargetText");
        xaml.Should().Contain("SystemHealthTriageBriefingCard");
        xaml.Should().Contain("SystemHealthTriageHandoffPanel");
        xaml.Should().Contain("SystemHealthProviderEmptyStatePanel");
        xaml.Should().Contain("SystemHealthProviderEmptyStateTitle");
        xaml.Should().Contain("SystemHealthProviderEmptyStateDetail");
        xaml.Should().Contain("SystemHealthEventEmptyStatePanel");
        xaml.Should().Contain("SystemHealthEventEmptyStateTitle");
        xaml.Should().Contain("SystemHealthEventEmptyStateDetail");
        xaml.Should().Contain("{Binding ProviderEmptyStateTitle}");
        xaml.Should().Contain("{Binding ProviderEmptyStateDetail}");
        xaml.Should().Contain("{Binding EventEmptyStateTitle}");
        xaml.Should().Contain("{Binding EventEmptyStateDetail}");
    }
}
