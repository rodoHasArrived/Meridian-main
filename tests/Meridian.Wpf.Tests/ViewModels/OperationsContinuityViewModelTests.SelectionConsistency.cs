using Meridian.Contracts.Workstation;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed partial class OperationsContinuityViewModelTests
{
    [Fact]
    public async Task SelectedWorkflow_NotInCurrentListCannotLoadDetailOrEstablishReadiness()
    {
        var scope = CreateCloseScope();
        var detail = CreatePublicationDetail(scope);
        var client = new FakeOperationsClient
        {
            Workflows = [], DetailLoader = _ => throw new InvalidOperationException("Unlisted detail must not be requested."),
            CommandCenter = CreateCommandCenter(scope, detail, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.SelectWorkflowAsync(detail.WorkflowId);

        vm.DetailErrorText.Should().Contain("not in the current workflow list");
        vm.StatusText.Should().Contain("failed to load");
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.NextAction.DisabledReason.Should().NotBeNull();
    }

    [Fact]
    public async Task SelectedWorkflow_CancellationLeavesExplicitStatusAndKeepsOtherPanelErrors()
    {
        var scope = CreateCloseScope();
        var detail = CreatePublicationDetail(scope);
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId)], Detail = detail, CommandCenter = CreateCommandCenter(scope, detail, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };
        await vm.RefreshAsync();
        var calendarError = vm.CalendarErrorText;
        var pending = new TaskCompletionSource<OperationsContinuityWorkflowDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DetailLoader = _ => pending.Task;
        using var cancellation = new CancellationTokenSource();

        var selecting = vm.SelectWorkflowAsync(detail.WorkflowId, cancellation.Token);
        vm.StatusText.Should().Be("Loading selected continuity workflow.");
        cancellation.Cancel();
        pending.SetCanceled(cancellation.Token);
        await selecting;

        vm.StatusText.Should().Contain("load cancelled");
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.IsRefreshing.Should().BeFalse();
        vm.HasCalendarError.Should().BeTrue();
        vm.CalendarErrorText.Should().Be(calendarError);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("period")]
    [InlineData("ledger-book")]
    public async Task SelectedWorkflow_RefusesDetailOutsideListedScope_AndFreshListRepairs(string dimension)
    {
        var scope = CreateCloseScope();
        var summary = CreateSummary(scope.LedgerBookId);
        var detail = CreateDetail() with { LedgerBookId = scope.LedgerBookId };
        detail = dimension switch
        {
            "account" => detail with { FundAccountId = Guid.NewGuid() },
            "period" => detail with { PeriodId = "2026-08" },
            _ => detail with { LedgerBookId = Guid.NewGuid() }
        };
        scope = scope with { FundAccountId = detail.FundAccountId, PeriodId = detail.PeriodId, LedgerBookId = detail.LedgerBookId };
        var client = new FakeOperationsClient
        {
            Workflows = [summary], Detail = detail, CommandCenter = CreateCommandCenter(scope, detail, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.RefreshAsync();

        vm.DetailErrorText.Should().Contain("different account, period, or ledger-book scope");
        vm.CloseReadiness.IsReady.Should().BeFalse("detail and decision agreement cannot replace the selected list scope");
        vm.GateRows.Should().BeEmpty();
        vm.CloseReadiness.Blockers.Should().BeEmpty();

        client.Workflows = [summary with { FundAccountId = detail.FundAccountId, PeriodId = detail.PeriodId, LedgerBookId = detail.LedgerBookId }];
        await vm.RefreshAsync();

        vm.HasDetailError.Should().BeFalse();
        vm.CloseReadiness.IsReady.Should().BeTrue();
    }

    [Theory]
    [InlineData("report-pack-id")]
    [InlineData("report-ready")]
    [InlineData("report-evidence")]
    [InlineData("workflow-evidence")]
    [InlineData("package-evidence")]
    [InlineData("approval-evidence")]
    [InlineData("evidence-source")]
    [InlineData("evidence-route")]
    [InlineData("evidence-captured-at")]
    [InlineData("evidence-captured-at-tick")]
    public async Task SharedCloseDecision_RefusesChangedPublicationEvidenceAtSameWorkflowVersion_AndRefreshRepairs(string change)
    {
        var scope = CreateCloseScope();
        var detail = CreatePublicationDetail(scope);
        var report = detail.ReportPackReadiness;
        var reportEvidence = report.EvidenceLinks[0];
        var active = change switch
        {
            "report-pack-id" => detail with { ReportPackReadiness = report with { ReportPackId = "report-pack-new" } },
            "report-ready" => detail with { ReportPackReadiness = report with { IsReady = false } },
            "report-evidence" => detail with { ReportPackReadiness = report with { EvidenceLinks = [reportEvidence with { EvidenceId = "report-new" }] } },
            "workflow-evidence" => detail with { EvidenceLinks = [detail.EvidenceLinks[0] with { EvidenceId = "workflow-new" }] },
            "package-evidence" => detail with { EvidencePackages = [detail.EvidencePackages[0] with { EvidenceLinks = [PublicationLink("package-new")] }] },
            "approval-evidence" => detail with { Approvals = [detail.Approvals[0] with { EvidenceLinks = [PublicationLink("approval-new")] }] },
            "evidence-source" => detail with { ReportPackReadiness = report with { EvidenceLinks = [reportEvidence with { Source = "different-source" }] } },
            "evidence-route" => detail with { ReportPackReadiness = report with { EvidenceLinks = [reportEvidence with { Route = "/reporting/different-record" }] } },
            "evidence-captured-at-tick" => detail with { ReportPackReadiness = report with { EvidenceLinks = [reportEvidence with { CapturedAtUtc = reportEvidence.CapturedAtUtc!.Value.AddTicks(1) }] } },
            _ => detail with { ReportPackReadiness = report with { EvidenceLinks = [reportEvidence with { CapturedAtUtc = reportEvidence.CapturedAtUtc!.Value.AddMinutes(1) }] } }
        };
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId)], Detail = detail,
            CommandCenter = CreateCommandCenter(scope, active, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.RefreshAsync();

        vm.HasDetailError.Should().BeFalse("workflow version and list scope still match");
        vm.CloseReadiness.IsReady.Should().BeFalse();
        vm.CloseReadiness.Detail.Should().Contain("different report-pack or publication evidence");
        vm.NextAction.DisabledReason.Should().NotBeNull();
        vm.CloseReadiness.Blockers.Should().BeEmpty();

        client.Detail = active;
        await vm.RefreshAsync();

        vm.CloseReadiness.IsReady.Should().BeTrue("the shared decision remains authoritative after both reads agree on the repaired evidence");
        vm.NextAction.DisabledReason.Should().BeNull();
    }

    [Fact]
    public async Task SharedCloseDecision_MatchesEvidenceIdentityDespiteLabelOrderDuplicatesAndTimestampOffset()
    {
        var scope = CreateCloseScope();
        var detail = CreatePublicationDetail(scope);
        var reportEvidence = detail.ReportPackReadiness.EvidenceLinks[0];
        var workflowEvidence = detail.EvidenceLinks[0];
        var additionalEvidence = PublicationLink("workflow-additional");
        detail = detail with { EvidenceLinks = [workflowEvidence, additionalEvidence] };
        var active = detail with
        {
            ReportPackReadiness = detail.ReportPackReadiness with
            {
                EvidenceLinks = [reportEvidence with { Label = "Updated label", CapturedAtUtc = reportEvidence.CapturedAtUtc!.Value.ToOffset(TimeSpan.FromHours(-7)) }]
            },
            EvidenceLinks = [additionalEvidence, workflowEvidence with { Label = "Updated workflow label" }, reportEvidence with { Source = "ignored later duplicate" }]
        };
        var client = new FakeOperationsClient
        {
            Workflows = [CreateSummary(scope.LedgerBookId)], Detail = detail, CommandCenter = CreateCommandCenter(scope, active, [])
        };
        using var vm = new OperationsContinuityViewModel(client) { Parameter = scope };

        await vm.RefreshAsync();

        vm.CloseReadiness.IsReady.Should().BeTrue("identity matching uses the first link for each evidence ID, independent of labels, ordering, and timestamp offsets");
        vm.NextAction.DisabledReason.Should().BeNull();
    }

    private static OperationsContinuityWorkflowDto CreatePublicationDetail(CloseReadinessScopeDto scope)
        => CreateDetail() with
        {
            LedgerBookId = scope.LedgerBookId,
            Status = OperationsWorkflowStatusDto.ReadyForClose,
            Gates = [],
            Blockers = [],
            ReportPackReadiness = new(true, "report-pack-current", null, [PublicationLink("report-current")]),
            EvidenceLinks = [PublicationLink("workflow-current")],
            EvidencePackages = [new("package-current", "Close support", EvidenceStatusDto.Ready, true, "Retained package", null, 1, 1, 1, [PublicationLink("package-current")])],
            Approvals = [new("approval-current", OperationsApprovalStateDto.Approved, "operator", "controller", "Approved", null, null, [PublicationLink("approval-current")])],
            NextActions = [new("close-workflow", "Close workflow", "/accounting/operations-continuity", null)]
        };

    private static OperationsEvidenceLinkDto PublicationLink(string id)
        => new(id, id, $"/reporting/{id}", "retained-report", DateTimeOffset.Parse("2026-10-07T12:00:00Z"));
}
