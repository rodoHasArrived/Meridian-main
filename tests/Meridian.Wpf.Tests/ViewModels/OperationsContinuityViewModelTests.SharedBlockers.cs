using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed partial class OperationsContinuityViewModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SharedBlocker_MissingOwnerRemainsExplicitWithoutInventingOwnership(string owner)
    {
        var blocker = new CloseReadinessBlockerDto("LANE_MISSING", "cash", "Unavailable", 1, "Error", owner,
            "Cash evidence is unavailable.", []);
        var row = new OperationsContinuityCloseBlockerPresentation(blocker);

        row.Blocker.Should().BeSameAs(blocker);
        row.OwnerLabel.Should().Be("Owner unavailable");
        row.RepairInstruction.Should().Contain("identify the owning lane");
    }

    [Fact]
    public async Task SharedBlockers_RetainServerFields_AndRefreshCurrentWorkflowAfterRepair()
    {
        var scope = CreateCloseScope();
        var detail = CreateDetail() with { LedgerBookId = scope.LedgerBookId, Status = OperationsWorkflowStatusDto.ReadyForClose };
        var blockers = new CloseReadinessBlockerDto[]
        {
            new("REPORT_STALE", "report-evidence", "Stale", 3, "Critical", "Fund Controller",
                "Retained reports require current support.", ["report-package-7", "report-manifest-7"]),
            new("LANE_UNREGISTERED", "cash", "Unavailable", 1, "Error", "Cash Operations",
                "The required cash contributor is not registered.", [])
        };
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId)], Detail = detail,
            CommandCenter = CreateCommandCenter(scope, detail, blockers)
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.RefreshAsync();

        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.CloseReadiness.Blockers.Should().BeEquivalentTo(blockers, options => options.WithStrictOrdering());
        vm.CloseReadiness.Blockers[0].Count.Should().Be(3, "the server count is not inferred from the record ID count");

        var currentDetail = detail with { Version = detail.Version + 1 };
        client.Workflows = [CreateSummary(scope.LedgerBookId) with { Version = currentDetail.Version }];
        client.CommandCenter = CreateCommandCenter(scope, currentDetail, []);
        await vm.RefreshAsync();
        vm.CloseReadiness.IsReady.Should().BeFalse("old detail must not borrow repaired evidence for a newer workflow version");
        vm.CloseReadiness.Blockers.Should().BeEmpty("a mismatched decision cannot contribute records to the selection");

        client.Detail = currentDetail;
        await vm.RefreshAsync();

        vm.SelectedWorkflowId.Should().Be(detail.WorkflowId);
        vm.CloseReadiness.IsReady.Should().BeTrue();
        vm.CloseReadiness.Blockers.Should().BeEmpty();
        vm.EntityInput = "different-entity";
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.CloseReadiness.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectedWorkflow_RejectsAnotherWorkflowEvenWhenItsSharedDecisionMatches()
    {
        var scope = CreateCloseScope();
        var otherDetail = CreateDetail() with { WorkflowId = Guid.NewGuid(), LedgerBookId = scope.LedgerBookId };
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId)], Detail = otherDetail,
            CommandCenter = CreateCommandCenter(scope, otherDetail, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.RefreshAsync();

        vm.SelectedWorkflowId.Should().Be(CreateSummary().WorkflowId);
        vm.HasDetailError.Should().BeTrue();
        vm.DetailErrorText.Should().Contain("does not match the selected workflow");
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.CloseReadiness.Blockers.Should().BeEmpty();
        vm.GateRows.Should().BeEmpty();
        vm.NextAction.DisabledReason.Should().NotBeNull();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task SelectedWorkflow_RequiresListDetailAndDecisionVersionsToMatch(long detailVersion)
    {
        var scope = CreateCloseScope();
        var detail = CreateDetail() with { LedgerBookId = scope.LedgerBookId, Version = detailVersion };
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId)], Detail = detail,
            CommandCenter = CreateCommandCenter(scope, detail, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.RefreshAsync();

        vm.CloseReadiness.IsReady.Should().BeFalse("agreement between detail and decision cannot override a different listed workflow version");
        vm.DetailErrorText.Should().Contain("different versions");
        vm.CloseReadiness.Blockers.Should().BeEmpty();

        client.Workflows = [CreateSummary(scope.LedgerBookId) with { Version = detailVersion }];
        await vm.RefreshAsync();

        vm.HasDetailError.Should().BeFalse();
        vm.CloseReadiness.IsReady.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SelectedWorkflow_DelayedPreviousReadCannotReplaceCurrentBlockers(bool selectedDetailFails, bool previousRefreshCancelled)
    {
        var scope = CreateCloseScope();
        var previous = CreateDetail() with { LedgerBookId = scope.LedgerBookId };
        var selected = previous with { WorkflowId = Guid.NewGuid(), Version = 12 };
        var delayed = new TaskCompletionSource<FinancialOperationsCommandCenterDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        var blockers = new CloseReadinessBlockerDto[]
        {
            new("CURRENT_STALE", "report-evidence", "Stale", 1, "Error", "Controller", "Repair selected report.", ["selected-report-12"])
        };
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId), CreateSummary(scope.LedgerBookId) with { WorkflowId = selected.WorkflowId, Version = 12 }],
            DetailLoader = id => Task.FromResult<OperationsContinuityWorkflowDto?>(id == selected.WorkflowId
                ? selectedDetailFails ? null : selected
                : previous),
            CloseReadinessLoader = () => ++requestCount == 1 ? delayed.Task : Task.FromResult<FinancialOperationsCommandCenterDto?>(CreateCommandCenter(scope, selected, blockers))
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };
        using var previousCancellation = new CancellationTokenSource();
        var oldRefresh = vm.RefreshAsync(previousCancellation.Token);

        await vm.SelectWorkflowAsync(selected.WorkflowId);
        var selectedStatus = vm.StatusText;
        if (previousRefreshCancelled)
        {
            previousCancellation.Cancel();
            delayed.SetCanceled(previousCancellation.Token);
        }
        else
        {
            delayed.SetResult(CreateCommandCenter(scope, previous, []));
        }
        await oldRefresh;

        vm.SelectedWorkflowId.Should().Be(selected.WorkflowId);
        vm.CloseReadiness.IsReady.Should().BeFalse();
        if (selectedDetailFails)
        {
            vm.CloseReadiness.Blockers.Should().BeEmpty();
            vm.StatusText.Should().Contain("failed to load");
        }
        else
        {
            vm.CloseReadiness.Blockers.Should().BeEquivalentTo(blockers);
            vm.StatusText.Should().StartWith("Selected continuity workflow refreshed ");
        }
        vm.StatusText.Should().Be(selectedStatus, "a superseded refresh cannot restore its loading status");
        vm.StatusText.Should().NotContain("Loading");
        vm.IsRefreshing.Should().BeFalse("a superseded refresh must not leave repair refresh disabled");
        vm.EvaluateCloseScopeCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SharedBlockers_RenderAllFieldsAndRepairRefresh_ThenClearAfterRepair()
    {
        WpfTestThread.Run(async () =>
        {
            var scope = CreateCloseScope();
            var detail = CreateDetail() with { LedgerBookId = scope.LedgerBookId };
            var blockers = new CloseReadinessBlockerDto[]
            {
                new("REPORT_STALE", "report-evidence", "Stale", 3, "Critical", "Fund Controller",
                    "Retained reports require current support.", ["report-package-7", "report-manifest-7"]),
                new("LANE_MISSING", "cash", "Unavailable", 1, "Error", "", "Cash evidence is unavailable.", [])
            };
            var client = new FakeOperationsClient
            {
                Workflows = [CreateSummary(scope.LedgerBookId)], Detail = detail,
                CommandCenter = CreateCommandCenter(scope, detail, blockers)
            };
            using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };
            await vm.RefreshAsync();
            var page = new OperationsContinuityPage(vm);
            var host = new Window { Width = 1280, Height = 900, Content = new Frame { Content = page } };
            try
            {
                host.Show();
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var list = Descendants(page).OfType<ItemsControl>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "OperationsContinuitySharedCloseBlockerList");
                list.Items.Cast<object>().Should().HaveCount(2);
                var text = Descendants(list).OfType<TextBlock>().Where(block => block.IsVisible).Select(block => block.Text).ToArray();
                text.Should().Contain(["Type: Stale", "Count: 3", "Severity: Critical", "Owner: Fund Controller",
                    "Contributor: report-evidence · Code: REPORT_STALE", "report-package-7", "report-manifest-7",
                    "Owner: Owner unavailable", "No causing record IDs supplied by the shared service."]);
                text.Should().Contain(value => value.Contains("Repair: ask Fund Controller") && value.Contains("Evaluate close"));
                var refresh = Descendants(page).OfType<Button>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "OperationsContinuityRefreshAfterRepairButton");
                refresh.Command.Should().BeSameAs(vm.EvaluateCloseScopeCommand);

                var repaired = detail with { Version = 5 };
                client.Workflows = [CreateSummary(scope.LedgerBookId) with { Version = repaired.Version }];
                client.Detail = repaired;
                client.CommandCenter = CreateCommandCenter(scope, repaired, []);
                await vm.EvaluateCloseScopeCommand.ExecuteAsync(null);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                list.Items.Cast<object>().Should().BeEmpty();
                vm.CloseReadiness.IsReady.Should().BeTrue();
            }
            finally
            {
                host.Close();
            }
        });
    }

    private static CloseReadinessScopeDto CreateCloseScope()
        => new("fund-alpha", Guid.NewGuid(), CreateSummary().FundAccountId, "entity-alpha", "2026-07");

    private static FinancialOperationsCommandCenterDto CreateCommandCenter(CloseReadinessScopeDto scope,
        OperationsContinuityWorkflowDto detail, IReadOnlyList<CloseReadinessBlockerDto> blockers)
        => new(DateTimeOffset.UtcNow, scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId, scope.PeriodId,
            blockers.Count == 0 ? "Ready" : "Blocked", blockers.Count == 0, "Shared close assessment", 0, blockers.Count, 0, [], [],
            ActiveWorkflow: detail,
            CloseReadiness: new(scope, DateTimeOffset.UtcNow, blockers.Count == 0 ? "Ready" : "Blocked", true, blockers.Count == 0, [], blockers));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
