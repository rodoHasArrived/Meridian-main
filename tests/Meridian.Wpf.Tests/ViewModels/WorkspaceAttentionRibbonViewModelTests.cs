using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class WorkspaceAttentionRibbonViewModelTests
{
    [Theory]
    [InlineData("trading", WorkspaceAttentionSeverity.Warning, "Choose Context", "Settings")]
    [InlineData("portfolio", WorkspaceAttentionSeverity.Informational, "Review Imports", "PortfolioImport")]
    [InlineData("accounting", WorkspaceAttentionSeverity.Error, "Open Reconciliation", "FundReconciliation")]
    [InlineData("reporting", WorkspaceAttentionSeverity.Healthy, "Open Reports", "FundReportPack")]
    [InlineData("strategy", WorkspaceAttentionSeverity.Warning, "Review Runs", "StrategyRuns")]
    [InlineData("data", WorkspaceAttentionSeverity.Informational, "Open Quality", "DataQuality")]
    [InlineData("settings", WorkspaceAttentionSeverity.Healthy, "Open Settings", "Settings")]
    public void ForWorkspace_ShouldProvideRootNavigationAttentionState(
        string workspaceId,
        string expectedSeverity,
        string expectedActionLabel,
        string expectedTarget)
    {
        var ribbon = WorkspaceAttentionRibbonViewModel.ForWorkspace(workspaceId, _ => { });

        ribbon.WorkspaceId.Should().Be(workspaceId);
        ribbon.Severity.Should().Be(expectedSeverity);
        ribbon.PrimaryActionLabel.Should().Be(expectedActionLabel);
        ribbon.PrimaryActionTarget.Should().Be(expectedTarget);
        ribbon.SummaryText.Should().NotBeNullOrWhiteSpace();
        ribbon.Timestamp.Should().Be(new DateTimeOffset(2026, 06, 15, 09, 00, 00, TimeSpan.Zero));
        ribbon.PrimaryActionCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData(WorkspaceAttentionSeverity.Healthy, "Success")]
    [InlineData(WorkspaceAttentionSeverity.Warning, "Warning")]
    [InlineData(WorkspaceAttentionSeverity.Error, "Danger")]
    [InlineData(WorkspaceAttentionSeverity.Informational, "Info")]
    public void ForWorkspace_ShouldMapSeverityToSharedToneWithoutWorkspaceStyles(string severity, string expectedTone)
    {
        var ribbon = WorkspaceAttentionRibbonViewModel.ForWorkspace("unknown", _ => { });
        var projected = WorkspaceAttentionRibbonViewModel.ForWorkspace(
            severity == WorkspaceAttentionSeverity.Healthy ? "settings" :
            severity == WorkspaceAttentionSeverity.Warning ? "trading" :
            severity == WorkspaceAttentionSeverity.Error ? "accounting" : "data",
            _ => { });

        projected.Tone.Should().Be(expectedTone);
        ribbon.PanelAutomationId.Should().Be("unknownAttentionRibbon");
    }

    [Fact]
    public void PrimaryActionCommand_ShouldRaiseSelectedWorkspaceTarget()
    {
        var selectedTarget = string.Empty;
        var ribbon = WorkspaceAttentionRibbonViewModel.ForWorkspace("strategy", target => selectedTarget = target);

        ribbon.PrimaryActionCommand.Execute(null);

        selectedTarget.Should().Be("StrategyRuns");
    }
}
