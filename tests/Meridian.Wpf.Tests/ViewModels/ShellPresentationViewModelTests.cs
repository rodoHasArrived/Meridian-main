using System.Windows;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Refresh;
using Meridian.Wpf.Shell.Session;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ShellPresentationViewModelTests
{
    [Fact]
    public void CommandPaletteViewModel_FiltersPagesAndExposesEmptyState()
    {
        var viewModel = new CommandPaletteViewModel();

        viewModel.Show("strategy", ShellNavigationCatalog.GetRegisteredPageTags());
        viewModel.Pages.Should().NotBeEmpty();
        viewModel.SelectedPage.Should().NotBeNull();
        viewModel.CanOpenSelectedPage.Should().BeTrue();

        viewModel.SetQuery("zzzz-unmatched-query", "strategy", ShellNavigationCatalog.GetRegisteredPageTags());

        viewModel.Pages.Should().BeEmpty();
        viewModel.SelectedPage.Should().BeNull();
        viewModel.ResultSummary.Should().Contain("No matches");
        viewModel.EmptyVisibility.Should().Be(Visibility.Visible);
        viewModel.EmptyTitle.Should().Contain("zzzz-unmatched-query");
        viewModel.CanOpenSelectedPage.Should().BeFalse();
    }

    [Fact]
    public void OperatorInboxViewModel_PrioritizesCriticalWorkAndResolvesTarget()
    {
        var viewModel = new OperatorInboxViewModel();
        var warning = new OperatorWorkItemDto(
            WorkItemId: "warning-1",
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: "Review reconciliation break",
            Detail: "Cash mismatch needs review.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            TargetPageTag: "FundReconciliation")
        {
            PriorityScore = 520,
            PriorityExplanation = "warning lane"
        };
        var critical = new OperatorWorkItemDto(
            WorkItemId: "critical-1",
            Kind: OperatorWorkItemKindDto.PaperReplay,
            Label: "Replay verification is stale",
            Detail: "Session changed after audit.",
            Tone: OperatorWorkItemToneDto.Critical,
            CreatedAt: DateTimeOffset.UtcNow,
            TargetPageTag: "TradingShell")
        {
            PriorityScore = 860,
            PriorityExplanation = "critical lane"
        };
        var inbox = new OperatorInboxDto(
            DateTimeOffset.UtcNow,
            [warning, critical],
            CriticalCount: 1,
            WarningCount: 1,
            ReviewCount: 2,
            Summary: "2 work items need review.");

        viewModel.Apply(inbox, error: null, workItem => workItem?.TargetPageTag);

        viewModel.PrimaryWorkItem.Should().BeSameAs(critical);
        viewModel.ButtonText.Should().Be("Queue (2)");
        viewModel.Summary.Should().Be("2 work items need review.");
        viewModel.Tone.Should().Be(WorkspaceTone.Danger);
        viewModel.TargetText.Should().Be("TradingShell");
        viewModel.LaneGroups.Should().HaveCount(2);
        viewModel.LaneGroups.First().Items.First().WorkItemId.Should().Be("critical-1");
    }

    [Fact]
    public void WorkflowSummaryStripViewModel_SelectsCurrentWorkspaceAndTogglesSecondaryActions()
    {
        var viewModel = new WorkflowSummaryStripViewModel();
        var strategy = CreateWorkflowSummary("strategy", "Strategy", WorkspaceTone.Info, isBlocking: false);
        var trading = CreateWorkflowSummary("trading", "Trading", WorkspaceTone.Warning, isBlocking: true);

        viewModel.Apply([strategy, trading], "trading");

        viewModel.PrimarySummary.Should().BeSameAs(trading);
        viewModel.SecondarySummaries.Should().ContainSingle().Which.Should().BeSameAs(strategy);
        viewModel.HasSecondarySummaries.Should().BeTrue();
        viewModel.PrimaryDetailVisibility.Should().Be(Visibility.Visible);
        viewModel.SecondarySummariesVisibility.Should().Be(Visibility.Collapsed);

        viewModel.ToggleSecondarySummaries();

        viewModel.AreSecondarySummariesExpanded.Should().BeTrue();
        viewModel.SecondarySummariesVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task ShellRefreshCoordinator_RequestRefresh_CancelsPreviousRefresh()
    {
        using var coordinator = new ShellRefreshCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        coordinator.RequestRefresh(async ct =>
        {
            firstStarted.SetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                firstCanceled.SetResult();
                throw;
            }
        });

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        coordinator.RequestRefresh(_ => Task.CompletedTask);

        await firstCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WindowStateStore_SaveAndLoad_RoundTripsState()
    {
        var statePath = Path.Combine(Path.GetTempPath(), "meridian-window-state-tests", $"{Guid.NewGuid():N}.json");
        var store = new WindowStateStore(statePath);
        var state = new DesktopWindowState(
            Left: 20,
            Top: 30,
            Width: 1400,
            Height: 900,
            IsMaximized: true,
            SavedAt: DateTime.UtcNow);

        await store.SaveAsync(state);

        store.Load().Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task WindowStateStore_SaveAsync_PropagatesCancellation()
    {
        var statePath = Path.Combine(Path.GetTempPath(), "meridian-window-state-tests", $"{Guid.NewGuid():N}.json");
        var store = new WindowStateStore(statePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => store.SaveAsync(new DesktopWindowState(0, 0, 1200, 800, false, DateTime.UtcNow), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static WorkspaceWorkflowSummary CreateWorkflowSummary(
        string workspaceId,
        string title,
        string tone,
        bool isBlocking)
        => new(
            workspaceId,
            title,
            $"{title} status",
            $"{title} guidance",
            tone,
            new WorkflowNextAction(
                $"Open {title}",
                $"Open {title}",
                $"{title}Shell",
                WorkspaceTone.Primary),
            new WorkflowBlockerSummary(
                $"{workspaceId}-blocker",
                $"{title} blocker",
                $"{title} blocker detail",
                tone,
                isBlocking),
            []);
}
