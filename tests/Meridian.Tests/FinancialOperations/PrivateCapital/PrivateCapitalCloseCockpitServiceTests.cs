using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class PrivateCapitalCloseCockpitServiceTests
{
    private const string FundProfileId = "fund-alpha";
    private const string PeriodId = "2026-06";
    private const string EntityId = "entity-master";
    private static readonly Guid FundAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetCockpitAsync_WhenCloseScopeIsSourceBacked_ProjectsReadyV018CloseLanes()
    {
        var activity = BuildActivity();
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.FundProfileId.Should().Be(FundProfileId);
        cockpit.FundAccountId.Should().Be(FundAccountId);
        cockpit.PeriodId.Should().Be(PeriodId);
        cockpit.EntityId.Should().Be(EntityId);
        cockpit.CockpitRoute.Should().Contain(UiApiRoutes.OperationsPrivateCapitalCloseCockpit);
        cockpit.CockpitRoute.Should().Contain("fundProfileId=fund-alpha");
        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.Ready);
        cockpit.IsReadyToClose.Should().BeTrue();
        cockpit.ReadinessScore.Should().Be(100);
        cockpit.WorkflowCount.Should().Be(1);
        cockpit.FundEventCount.Should().Be(2);
        cockpit.CapitalAccountCount.Should().Be(1);
        cockpit.ReportOutputCount.Should().Be(3);
        cockpit.DeliveredReportOutputCount.Should().Be(3);
        cockpit.ReadyLaneCount.Should().Be(14);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Select(static lane => lane.LaneId).Should().Equal(
            "data-receipt",
            "reconciliation",
            "journal-posting",
            "capital-accounts",
            "partner-capital-tie-outs",
            "expense-fee-allocation",
            "management-company-operations",
            "nav-support",
            "valuation-evidence",
            "reporting",
            "delivery",
            "close-controls",
            "close-package",
            "period-lock");
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "delivery" &&
            lane.IsReady &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/report-pack/published"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "partner-capital-tie-outs" &&
            lane.IsReady &&
            lane.Summary.Contains("1 partner capital account", StringComparison.OrdinalIgnoreCase) &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/report-pack/published"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "expense-fee-allocation" &&
            lane.IsReady &&
            lane.Summary.Contains("1 expense/fee", StringComparison.OrdinalIgnoreCase) &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/allocation-policy"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "management-company-operations" &&
            lane.IsReady &&
            lane.Summary.Contains("management-company operating record", StringComparison.OrdinalIgnoreCase) &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/intercompany-balance") &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/bank-card-statement") &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/cash-plan-snapshot") &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/reimbursement-support"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "period-lock" &&
            lane.Route == UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflow.WorkflowId.ToString("D")) &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/period-lock"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "close-controls" &&
            lane.IsReady &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/reversal-approval") &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/recurring-journals") &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/stale-marks") &&
            lane.EvidenceLinks.Any(link => link.Route == "/evidence/period-lock"));
        cockpit.Workflows.Should().ContainSingle(row =>
            row.WorkflowId == workflow.WorkflowId &&
            row.ClosePackageId == "close-package-fund-alpha-2026-06" &&
            row.IsReadyToClose);
        cockpit.ApprovalHistory.Should().HaveCount(11);
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId == "approval-close-fund-alpha-2026-06" &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Reviewer == "controller" &&
            row.EvidenceLinkCount == 1 &&
            row.EvidenceLinks.Any(link => link.Route == "/evidence/approval-close"));
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId.StartsWith("checklist-control:", StringComparison.OrdinalIgnoreCase) &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Reviewer == "controller" &&
            row.Rationale == "Checklist control approval retained for close-gate-approval.");
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId.Contains("close-control-reversal-approval", StringComparison.OrdinalIgnoreCase) &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Reviewer == "controller" &&
            row.Rationale == "Checklist control approval retained for close-control-reversal-approval.");
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId == "approval:fund-alpha:capital-call" &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Rationale == "Fund-event approval retained for CapitalCall." &&
            row.WorkflowRoute == "/workstation/approvals/approval:fund-alpha:capital-call" &&
            row.EvidenceLinks.Any(link => link.Route == "/evidence/source"));
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId == "report-output-approval:report-output:fund-alpha:lp-1:statement" &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Reviewer == "controller" &&
            row.Rationale == "Report output publication retained for LP 1 statement." &&
            row.EvidenceLinks.Any(link => link.Route == "/evidence/report-pack/published"));
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId == "report-output-approval:report-output:fund-alpha:administrator-nav:20260630" &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Reviewer == "administrator" &&
            row.Rationale == "Report output publication retained for Administrator NAV statement." &&
            row.EvidenceLinks.Any(link => link.Route == "/evidence/administrator-nav"));
        cockpit.EvidencePackages.Should().HaveCount(5);
        cockpit.EvidencePackages.Should().OnlyContain(package => package.Status == EvidenceStatusDto.Ready && package.IsReady);
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:fund-event-accounting" &&
            package.CompleteCategoryCount == 4 &&
            package.RequiredCategoryCount == 4 &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/allocation-policy"));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:nav-support" &&
            package.CompleteCategoryCount == 3 &&
            package.RequiredCategoryCount == 3 &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/shadow-nav-pack") &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/administrator-nav"));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:close-approval-audit" &&
            package.CompleteCategoryCount == 4 &&
            package.RequiredCategoryCount == 4 &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/approval-close") &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/reversal-approval") &&
            package.EvidenceLinks.Any(link => link.Route == "/operations-continuity/close-package/manifest-fund-alpha-2026-06"));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == $"period-lock-reopen:{workflow.FundAccountId:D}:2026-06" &&
            package.CompleteCategoryCount == 2 &&
            package.RequiredCategoryCount == 2 &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/period-lock"));
        var navPackage = cockpit.NavSupportPackages.Should().ContainSingle(package =>
            package.PackageId == $"nav-support:{workflow.FundAccountId:D}:2026-06" &&
            package.Status == EvidenceStatusDto.Ready &&
            package.IsReady &&
            package.ShadowNav == 235m &&
            package.Currency == "USD" &&
            package.Components.Count == 5 &&
            package.Components.All(component => component.IsReady) &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/shadow-nav-pack") &&
            package.EvidenceLinks.Any(link => link.Route == "/evidence/administrator-nav")).Subject;
        navPackage.ShadowNavTieOut.Should().NotBeNull();
        navPackage.ShadowNavTieOut!.Status.Should().Be(EvidenceStatusDto.Ready);
        navPackage.ShadowNavTieOut.IsReady.Should().BeTrue();
        navPackage.ShadowNavTieOut.MeridianShadowNav.Should().Be(235m);
        navPackage.ShadowNavTieOut.AdministratorNav.Should().Be(235m);
        navPackage.ShadowNavTieOut.Variance.Should().Be(0m);
        navPackage.ShadowNavTieOut.Tolerance.Should().Be(0.01m);
        cockpit.Blockers.Should().BeEmpty();
        cockpit.NextActions.Should().BeEmpty();
        cockpit.LiveCapabilities.Should().Contain(item => item.Contains("Fund/book/period", StringComparison.OrdinalIgnoreCase));
        cockpit.LiveCapabilities.Should().Contain(item => item.Contains("Approval history", StringComparison.OrdinalIgnoreCase));
        cockpit.LiveCapabilities.Should().Contain(item => item.Contains("Close-control checklist", StringComparison.OrdinalIgnoreCase));
        cockpit.LiveCapabilities.Should().Contain(item => item.Contains("evidence package rollups", StringComparison.OrdinalIgnoreCase));
        cockpit.LiveCapabilities.Should().Contain(item => item.Contains("administrator-versus-Meridian", StringComparison.OrdinalIgnoreCase));
        cockpit.LiveCapabilities.Should().Contain(item => item.Contains("Management-company operating records", StringComparison.OrdinalIgnoreCase));
        cockpit.PlannedCapabilities.Should().NotContain(item => item.Contains("Administrator-versus-Meridian", StringComparison.OrdinalIgnoreCase));
        cockpit.PlannedCapabilities.Should().Contain("Live payment release");
    }

    [Fact]
    public async Task GetCockpitAsync_WhenGovernedReopenTimelineExists_ProjectsApprovalHistory()
    {
        var activity = BuildActivity();
        var workflow = BuildClosedWorkflow(
            periodLockPackageReady: false,
            timeline: [GovernedReopenTimelineEntry()]);
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId.StartsWith("workflow-reopened:", StringComparison.OrdinalIgnoreCase) &&
            row.WorkflowId == workflow.WorkflowId &&
            row.Status == OperationsApprovalStateDto.Approved &&
            row.Operator == "ops-user" &&
            row.Reviewer == "ops-user" &&
            row.Rationale!.Contains("Approval reference: approval-ref-123", StringComparison.OrdinalIgnoreCase) &&
            row.Rationale.Contains("Impact summary: Reopened reconciliation for incident follow-up.", StringComparison.OrdinalIgnoreCase) &&
            row.EvidenceLinkCount == 2 &&
            row.EvidenceLinks.Any(link => link.EvidenceId == "INC-123" && link.Source == "incident") &&
            row.EvidenceLinks.Any(link => link.EvidenceId == "approval-ref-123" && link.Source == "approval-reference"));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenNoSourcesAreRegistered_FailsClosedWithMissingLanes()
    {
        var service = new PrivateCapitalCloseCockpitService(null, null);

        var cockpit = await service.GetCockpitAsync(fundProfileId: FundProfileId, periodId: PeriodId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.Missing);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.WorkflowCount.Should().Be(0);
        cockpit.FundEventCount.Should().Be(0);
        cockpit.Lanes.Should().OnlyContain(lane => !lane.IsReady);
        cockpit.Lanes.Should().Contain(lane =>
            lane.LaneId == "data-receipt" &&
            lane.Status == EvidenceStatusDto.Missing &&
            lane.RequiredActions.Contains("Retain source intake evidence for the close scope"));
        cockpit.EvidencePackages.Should().HaveCount(4);
        cockpit.EvidencePackages.Should().OnlyContain(package =>
            package.Status == EvidenceStatusDto.Missing &&
            !package.IsReady &&
            package.EvidenceLinkCount == 0);
    }

    [Fact]
    public async Task GetCockpitAsync_WhenPeriodLockReopenPackageNeedsRemediation_KeepsPeriodLockLaneInReview()
    {
        var activity = BuildActivity();
        var workflow = BuildClosedWorkflow(periodLockPackageReady: false);
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(13);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "period-lock" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.Summary.Contains("reopened after close package", StringComparison.OrdinalIgnoreCase) &&
            lane.RequiredActions.Contains("Complete reopened incident remediation and close the period again with retained evidence."));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == $"period-lock-reopen:{workflow.FundAccountId:D}:2026-06" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.CompleteCategoryCount == 1 &&
            package.RequiredCategoryCount == 2 &&
            package.RequiredActions.Contains("Complete reopened incident remediation and close the period again with retained evidence."));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:close-approval-audit" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.RequiredActions.Contains("Complete reopened incident remediation and close the period again with retained evidence."));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenCloseControlEvidencePointersAreMissing_KeepsCloseAuditPackageInReview()
    {
        var activity = BuildActivity();
        var workflow = BuildClosedWorkflow(includeCloseControlEvidence: false);
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(13);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "close-controls" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.Summary.Contains("retained evidence or approval", StringComparison.OrdinalIgnoreCase) &&
            lane.RequiredActions.Contains("Retain approved reversal support before close sign-off."));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:close-approval-audit" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.CompleteCategoryCount == 3 &&
            package.RequiredCategoryCount == 4 &&
            package.RequiredActions.Contains("Retain approved reversal support before close sign-off."));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenFundEventJournalPostingIsIncomplete_KeepsJournalLaneInReview()
    {
        var activity = BuildActivity(includeIncompleteJournal: true);
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(11);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "journal-posting" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.Summary.Contains("1 private-capital fund event", StringComparison.OrdinalIgnoreCase) &&
            lane.RequiredActions.Contains("Post or validate close journals"));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:fund-event-accounting" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.RequiredActions.Contains("Post or validate close journals"));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenInvestorStatementIsUnapproved_KeepsReportingDeliveryAndTieOutInReview()
    {
        var activity = BuildActivity(includeUnapprovedStatement: true);
        activity.ReportOutputs.Should().ContainSingle(output =>
            output.ReportOutputId == "report-output:fund-alpha:lp-1:statement" &&
            output.ApprovalState == ManualJournalEntryStatusDto.Submitted);
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.DeliveredReportOutputCount.Should().Be(2);
        cockpit.ReadyLaneCount.Should().Be(11);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "reporting" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.Summary.Contains("approval", StringComparison.OrdinalIgnoreCase) &&
            lane.RequiredActions.Contains("Generate and approve close report outputs"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "delivery" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.Summary.Contains("approved output", StringComparison.OrdinalIgnoreCase) &&
            lane.RequiredActions.Contains("Retain report-package delivery evidence"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "partner-capital-tie-outs" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.RequiredActions.Contains("Retain partner capital tie-out evidence across subledger, ledger, and statement output"));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:partner-capital-tie-out" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.CompleteCategoryCount == 0 &&
            package.RequiredCategoryCount == 3);
        cockpit.ApprovalHistory.Should().ContainSingle(row =>
            row.ApprovalId == "report-output-approval:report-output:fund-alpha:lp-1:statement" &&
            row.Status == OperationsApprovalStateDto.Submitted &&
            row.DecidedAtUtc == null &&
            row.Rationale == "Report output publication retained for LP 1 statement.");
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:close-approval-audit" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.RequiredActions.Contains("Retain approved close approval history before audit package release."));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenExpenseFeeAllocationEvidenceIsMissing_KeepsLaneInReview()
    {
        var activity = BuildActivity(includeExpenseFeeAllocationEvidence: false);
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(12);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "expense-fee-allocation" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.RequiredActions.Contains("Retain posted expense, fee, and allocation review evidence"));
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "management-company-operations" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.Summary.Contains("missing", StringComparison.OrdinalIgnoreCase) &&
            lane.RequiredActions.Contains("Retain management-company operating evidence for expense allocations, intercompany balances, fees, bank/card support, budget or cash-plan snapshots, and reimbursements"));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenAdministratorNavEvidenceIsMissing_KeepsNavSupportInReview()
    {
        var activity = BuildActivity(includeAdministratorNavEvidence: false);
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.ReviewRequired);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(13);
        cockpit.BlockedLaneCount.Should().Be(0);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "nav-support" &&
            lane.Status == EvidenceStatusDto.ReviewRequired &&
            !lane.IsReady &&
            lane.RequiredActions.Contains("Retain administrator NAV support evidence before close sign-off."));
        var navPackage = cockpit.NavSupportPackages.Should().ContainSingle(package =>
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.RequiredActions.Contains("Retain administrator NAV support evidence before close sign-off."));
        navPackage.Subject.ShadowNavTieOut.Should().NotBeNull();
        navPackage.Subject.ShadowNavTieOut!.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        navPackage.Subject.ShadowNavTieOut.IsReady.Should().BeFalse();
        navPackage.Subject.ShadowNavTieOut.MeridianShadowNav.Should().Be(235m);
        navPackage.Subject.ShadowNavTieOut.AdministratorNav.Should().BeNull();
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:nav-support" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            !package.IsReady &&
            package.CompleteCategoryCount == 1 &&
            package.RequiredCategoryCount == 3 &&
            package.RequiredActions.Contains("Retain administrator NAV support evidence before close sign-off."));
    }

    [Fact]
    public async Task GetCockpitAsync_WhenAdministratorNavVarianceExists_BlocksNavSupportLane()
    {
        var activity = BuildActivity(administratorNav: 240m);
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.Blocked);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(13);
        cockpit.BlockedLaneCount.Should().Be(1);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "nav-support" &&
            lane.Status == EvidenceStatusDto.Blocked &&
            !lane.IsReady &&
            lane.Summary.Contains("above the 0.01 tolerance", StringComparison.OrdinalIgnoreCase));
        var navPackage = cockpit.NavSupportPackages.Should().ContainSingle(package =>
            package.Status == EvidenceStatusDto.Blocked &&
            !package.IsReady).Subject;
        navPackage.ShadowNavTieOut.Should().NotBeNull();
        navPackage.ShadowNavTieOut!.Status.Should().Be(EvidenceStatusDto.Blocked);
        navPackage.ShadowNavTieOut.IsReady.Should().BeFalse();
        navPackage.ShadowNavTieOut.MeridianShadowNav.Should().Be(235m);
        navPackage.ShadowNavTieOut.AdministratorNav.Should().Be(240m);
        navPackage.ShadowNavTieOut.Variance.Should().Be(-5m);
        navPackage.ShadowNavTieOut.Tolerance.Should().Be(0.01m);
    }

    [Fact]
    public async Task GetCockpitAsync_WhenPartnerCapitalTieOutVarianceExists_BlocksLane()
    {
        var activity = BuildActivity(partnerTieOutEndingNetActivity: 236m);
        var workflow = BuildClosedWorkflow();
        var service = new PrivateCapitalCloseCockpitService(
            new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([workflow]));

        var cockpit = await service.GetCockpitAsync(
            FundProfileId,
            activity.LedgerBookId,
            FundAccountId,
            PeriodId,
            EntityId);

        cockpit.OverallStatus.Should().Be(EvidenceStatusDto.Blocked);
        cockpit.IsReadyToClose.Should().BeFalse();
        cockpit.ReadyLaneCount.Should().Be(13);
        cockpit.BlockedLaneCount.Should().Be(1);
        cockpit.Lanes.Should().ContainSingle(lane =>
            lane.LaneId == "partner-capital-tie-outs" &&
            lane.Status == EvidenceStatusDto.Blocked &&
            !lane.IsReady &&
            lane.RequiredActions.Contains("Retain partner capital tie-out evidence across subledger, ledger, and statement output"));
        cockpit.EvidencePackages.Should().ContainSingle(package =>
            package.PackageId == "private-capital:partner-capital-tie-out" &&
            package.Status == EvidenceStatusDto.Blocked &&
            !package.IsReady &&
            package.RequiredActions.Contains("Retain partner capital tie-out evidence across subledger, ledger, and statement output"));
    }

    private static OperationsContinuityWorkflowDto BuildClosedWorkflow(
        bool includeCloseControlEvidence = true,
        bool periodLockPackageReady = true,
        IReadOnlyList<OperationsTimelineEntryDto>? timeline = null)
    {
        var workflowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var now = new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero);
        var readiness = new OperationsCloseReadinessDto(
            IsReadyToClose: true,
            Severity: "Ready",
            Score: 100,
            Components:
            [
                ReadyComponent("security-master", "Security Master", OperationsGateKeyDto.SecurityMaster, "/workstation/data/security-master"),
                ReadyComponent("provider-freshness", "Provider freshness", OperationsGateKeyDto.BrokerIngest, "/workstation/data/providers"),
                ReadyComponent("positions", "Positions", OperationsGateKeyDto.BrokerIngest, "/workstation/portfolio"),
                ReadyComponent("cash", "Cash", OperationsGateKeyDto.BrokerIngest, "/workstation/accounting/cash"),
                ReadyComponent("ledger", "Ledger", OperationsGateKeyDto.LedgerPosting, "/workstation/accounting/ledger"),
                ReadyComponent("pricing", "Pricing", OperationsGateKeyDto.SecurityMaster, "/workstation/data/pricing"),
                ReadyComponent("reconciliation", "Reconciliation", OperationsGateKeyDto.Reconciliation, "/workstation/reconciliation"),
                ReadyComponent("reports", "Reports", OperationsGateKeyDto.Approval, "/workstation/reporting"),
                ReadyComponent("approvals", "Approvals", OperationsGateKeyDto.Approval, "/workstation/approvals")
            ],
            Blockers: [],
            NextActions: []);
        return new OperationsContinuityWorkflowDto(
            workflowId,
            FundAccountId,
            PeriodId,
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: now.AddDays(-2),
            UpdatedAtUtc: now,
            Version: 10,
            OperationsWorkflowStatusDto.Closed,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.Approved,
            Gates:
            [
                Gate(OperationsGateKeyDto.BrokerIngest),
                Gate(OperationsGateKeyDto.SecurityMaster),
                Gate(OperationsGateKeyDto.LedgerPosting),
                Gate(OperationsGateKeyDto.Reconciliation),
                Gate(OperationsGateKeyDto.Approval)
            ],
            Timeline: timeline ?? [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals:
            [
                new OperationsApprovalDto(
                    "approval-close-fund-alpha-2026-06",
                    OperationsApprovalStateDto.Approved,
                    "ops-user",
                    "controller",
                    "Close approved after evidence package review.",
                    now.AddMinutes(-45),
                    now.AddMinutes(-30),
                    [Evidence("approval-close", "Approval close evidence", "/evidence/approval-close")])
            ],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                true,
                "report-pack-fund-alpha-2026-06",
                null,
                [Evidence("report-pack-fund-alpha-2026-06", "Report pack evidence", "/evidence/report-pack/published")]),
            CloseChecklist: CloseControlChecklist(now, includeCloseControlEvidence),
            EvidenceLinks: [Evidence("close-source", "Close source evidence", "/evidence/source")],
            Blockers: [],
            NextActions: [],
            CloseReadiness: readiness,
            ClosePackage: new OperationsClosePackagePublicationDto(
                "close-package-fund-alpha-2026-06",
                "report-pack-fund-alpha-2026-06",
                "manifest-fund-alpha-2026-06",
                "/operations-continuity/close-package/manifest-fund-alpha-2026-06",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                now,
                "controller",
                "Close package approved.",
                [Evidence("close-package", "Close package", "/operations-continuity/close-package/manifest-fund-alpha-2026-06")],
                CloseControlApprovals(now)),
            EvidencePackages: [PeriodLockReopenPackage(workflowId, periodLockPackageReady)]);
    }

    private static OperationsTimelineEntryDto GovernedReopenTimelineEntry()
    {
        var workflowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var occurredAt = new DateTimeOffset(2026, 6, 30, 18, 30, 0, TimeSpan.Zero);
        return new OperationsTimelineEntryDto(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            occurredAt,
            workflowId,
            FundAccountId,
            PeriodId,
            "workflow-reopened",
            OperationsWorkflowStatusDto.Closed,
            OperationsWorkflowStatusDto.ReconciliationActive,
            OperationsGateKeyDto.Reconciliation,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.InProgress,
            "ops-user",
            "Rationale: Incident requires reopened reconciliation | Justification: Controller approved reopening the closed period. | Approval reference: approval-ref-123 | Impact summary: Reopened reconciliation for incident follow-up.",
            null,
            null,
            [
                new OperationsEvidenceLinkDto("INC-123", "Workflow reopen incident", "/workstation/accounting", "incident", occurredAt),
                new OperationsEvidenceLinkDto("approval-ref-123", "Governed reopen approval reference", "/workstation/accounting", "approval-reference", occurredAt)
            ],
            null,
            "reopen-hash");
    }

    private static OperationsEvidencePackageSummaryDto PeriodLockReopenPackage(
        Guid workflowId,
        bool isReady)
        => new(
            $"period-lock-reopen:{FundAccountId:D}:{PeriodId}",
            "Period lock and reopen evidence",
            isReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            isReady,
            isReady
                ? $"Period {PeriodId} is locked by close package close-package-fund-alpha-2026-06; no governed reopen incident is active."
                : $"Workflow was reopened after close package close-package-fund-alpha-2026-06; 1 incident evidence link is retained and the period must be locked again after remediation.",
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId.ToString("D")),
            isReady ? 2 : 1,
            2,
            isReady ? 1 : 2,
            isReady
                ? [Evidence("period-lock-evidence", "Period lock evidence", "/evidence/period-lock")]
                :
                [
                    Evidence("reopen-incident", "Governed reopen incident", "/evidence/reopen-incident"),
                    Evidence("close-package", "Close package", "/operations-continuity/close-package/manifest-fund-alpha-2026-06")
                ],
            isReady ? [] : ["Complete reopened incident remediation and close the period again with retained evidence."]);

    private static IReadOnlyList<OperationsCloseChecklistTaskDto> CloseControlChecklist(
        DateTimeOffset now,
        bool includeEvidence)
    {
        var dueDate = new DateOnly(2026, 6, 30);
        return
        [
            CloseControlTask(
                "close-control-reversal-approval",
                "Reversal approval close control",
                "Approved reversal journal support",
                "reversal-approval-evidence",
                "/evidence/reversal-approval",
                now,
                dueDate,
                includeEvidence),
            CloseControlTask(
                "close-control-recurring-journals",
                "Recurring journal completion close control",
                "Recurring journal and accrual completion evidence",
                "recurring-journals-evidence",
                "/evidence/recurring-journals",
                now,
                dueDate,
                includeEvidence),
            CloseControlTask(
                "close-control-stale-marks",
                "Stale mark resolution close control",
                "Stale mark and pricing freshness resolution evidence",
                "stale-marks-evidence",
                "/evidence/stale-marks",
                now,
                dueDate,
                includeEvidence),
            CloseControlTask(
                "close-control-period-lock",
                "Period lock and reopen close control",
                "Period lock or governed reopen evidence",
                "period-lock-evidence",
                "/evidence/period-lock",
                now,
                dueDate,
                includeEvidence)
        ];
    }

    private static OperationsCloseChecklistTaskDto CloseControlTask(
        string taskId,
        string label,
        string requiredEvidence,
        string evidencePointer,
        string route,
        DateTimeOffset now,
        DateOnly dueDate,
        bool includeEvidence)
        => new(
            taskId,
            OperationsGateKeyDto.Approval,
            label,
            "controller",
            requiredEvidence,
            RequiredApprovalCount: 1,
            ExpiresOn: dueDate.AddDays(2),
            DueDate: dueDate,
            Status: "Complete",
            BlockingReason: null,
            EvidencePointer: includeEvidence ? evidencePointer : null,
            RemediationRoute: route,
            CanAcknowledge: false,
            AcknowledgedAtUtc: now.AddMinutes(-20),
            AcknowledgedBy: "controller");

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> CloseControlApprovals(DateTimeOffset now)
        =>
        [
            new("close-gate-approval", "controller", now.AddMinutes(-5)),
            new("close-control-reversal-approval", "controller", now.AddMinutes(-4)),
            new("close-control-recurring-journals", "controller", now.AddMinutes(-3)),
            new("close-control-stale-marks", "controller", now.AddMinutes(-2)),
            new("close-control-period-lock", "controller", now.AddMinutes(-1))
        ];

    private static PrivateCapitalActivityProjectionDto BuildActivity(
        bool includeExpenseFeeAllocationEvidence = true,
        decimal? partnerTieOutEndingNetActivity = null,
        bool includeAdministratorNavEvidence = true,
        decimal? administratorNav = null,
        bool includeIncompleteJournal = false,
        bool includeUnapprovedStatement = false)
    {
        var ledgerBookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var now = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var partnerCapitalEndingNetActivity = partnerTieOutEndingNetActivity ?? 235m;
        IReadOnlyList<string> feeEvidenceLinks = includeExpenseFeeAllocationEvidence
            ?
            [
                "/evidence/management-fee",
                "/evidence/allocation-policy",
                "/evidence/intercompany-balance",
                "/evidence/bank-card-statement",
                "/evidence/budget-snapshot",
                "/evidence/cash-plan-snapshot",
                "/evidence/reimbursement-support"
            ]
            : [];
        IReadOnlyList<string> subledgerEvidenceLinks = includeExpenseFeeAllocationEvidence
            ?
            [
                "/evidence/source",
                "/evidence/allocation-policy",
                "/evidence/intercompany-balance",
                "/evidence/bank-card-statement",
                "/evidence/budget-snapshot",
                "/evidence/cash-plan-snapshot",
                "/evidence/reimbursement-support"
            ]
            : ["/evidence/source"];
        IReadOnlyList<PrivateCapitalEvidenceCategoryDto> allocationEvidenceCategories = includeExpenseFeeAllocationEvidence
            ?
            [
                new PrivateCapitalEvidenceCategoryDto(
                    "allocation-policy",
                    "Allocation policy",
                    true,
                    "Allocation rule support retained.",
                    1,
                    ["/evidence/allocation-policy"],
                    ["Allocation policy"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "management-company-intercompany",
                    "Intercompany balance",
                    true,
                    "Intercompany balance support retained.",
                    1,
                    ["/evidence/intercompany-balance"],
                    ["Intercompany balance support"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "management-company-bank-card",
                    "Bank/card evidence",
                    true,
                    "Bank and card statement support retained.",
                    1,
                    ["/evidence/bank-card-statement"],
                    ["Bank/card statement support"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "management-company-budget",
                    "Budget snapshot",
                    true,
                    "Budget snapshot support retained.",
                    1,
                    ["/evidence/budget-snapshot"],
                    ["Budget snapshot"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "management-company-cash-plan",
                    "Cash-plan snapshot",
                    true,
                    "Cash-plan snapshot support retained.",
                    1,
                    ["/evidence/cash-plan-snapshot"],
                    ["Cash-plan snapshot"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "management-company-reimbursement",
                    "Reimbursement evidence",
                    true,
                    "Reimbursement support retained.",
                    1,
                    ["/evidence/reimbursement-support"],
                    ["Reimbursement evidence"])
            ]
            :
            [
                new PrivateCapitalEvidenceCategoryDto(
                    "allocation-policy",
                    "Allocation policy",
                    false,
                    "Allocation rule support is missing.",
                    0,
                    [],
                    ["Allocation policy"])
            ];
        var fundEvent = new PrivateCapitalFundEventDto(
            "fund-event:fund-alpha:capital-call:20260630",
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            new DateOnly(2026, 6, 30),
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            250m,
            250m,
            "Capital call",
            null,
            null,
            ["/evidence/source"],
            [],
            now,
            IsPosted: true,
            ApprovalId: "approval:fund-alpha:capital-call");
        var managementFeeEvent = new PrivateCapitalFundEventDto(
            "fund-event:fund-alpha:management-fee:20260630",
            "ManagementFee",
            ManualJournalEntryTypeDto.ManagementFee,
            ManualJournalEntryStatusDto.Approved,
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            new DateOnly(2026, 6, 30),
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            15m,
            -15m,
            "Management fee allocation",
            null,
            null,
            feeEvidenceLinks,
            [],
            now,
            IsPosted: true,
            ApprovalId: "approval:fund-alpha:management-fee");
        var subledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            "subledger-entry:fund-alpha:lp-1",
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.EntryType,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            fundEvent.NetCapitalActivity,
            fundEvent.Memo,
            ["/evidence/source"],
            [],
            now,
            IsPosted: true);
        var managementFeeSubledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            "subledger-entry:fund-alpha:lp-1:management-fee",
            managementFeeEvent.CapitalAccountId,
            managementFeeEvent.InvestorId,
            managementFeeEvent.Currency,
            managementFeeEvent.FundEventId,
            managementFeeEvent.FundEventType,
            managementFeeEvent.EntryType,
            ManualJournalEntryStatusDto.Approved,
            managementFeeEvent.JournalEntryId,
            managementFeeEvent.EffectiveDate,
            managementFeeEvent.GrossAmount,
            managementFeeEvent.NetCapitalActivity,
            235m,
            managementFeeEvent.Memo,
            feeEvidenceLinks,
            [],
            now,
            IsPosted: true);
        var ledgerImpact = new PrivateCapitalLedgerImpactDto(
            "ledger-impact:fund-alpha:lp-1",
            fundEvent.JournalEntryId,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            250m,
            250m,
            0m,
            2,
            true,
            true,
            ["/evidence/source"],
            [
                new PrivateCapitalLedgerLineImpactDto(
                    "line-1",
                    "Assets:Cash",
                    AccountingTemplateLineSideDto.Debit,
                    250m,
                    "USD",
                    EntityId,
                    null,
                    null,
                    "/evidence/source")
            ],
            []);
        var managementFeeLedgerImpact = new PrivateCapitalLedgerImpactDto(
            "ledger-impact:fund-alpha:lp-1:management-fee",
            managementFeeEvent.JournalEntryId,
            managementFeeEvent.FundEventId,
            managementFeeEvent.FundEventType,
            managementFeeEvent.CapitalAccountId,
            managementFeeEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            managementFeeEvent.EffectiveDate,
            managementFeeEvent.Currency,
            15m,
            15m,
            0m,
            2,
            true,
            true,
            feeEvidenceLinks,
            [
                new PrivateCapitalLedgerLineImpactDto(
                    "line-fee-expense",
                    "Expenses:ManagementFees",
                    AccountingTemplateLineSideDto.Debit,
                    15m,
                    "USD",
                    EntityId,
                    null,
                    null,
                    includeExpenseFeeAllocationEvidence ? "/evidence/management-fee" : null),
                new PrivateCapitalLedgerLineImpactDto(
                    "line-fee-allocation",
                    "Capital:LPAllocations",
                    AccountingTemplateLineSideDto.Credit,
                    15m,
                    "USD",
                    EntityId,
                    null,
                    null,
                    includeExpenseFeeAllocationEvidence ? "/evidence/allocation-policy" : null)
            ],
            []);
        var reportOutput = new PrivateCapitalReportOutputDto(
            "report-output:fund-alpha:lp-1:statement",
            "CapitalAccountStatement",
            "LP 1 statement",
            "/workstation/reporting/report-pack-fund-alpha-2026-06",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            includeUnapprovedStatement ? ManualJournalEntryStatusDto.Submitted : ManualJournalEntryStatusDto.Approved,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            250m,
            1,
            ["/evidence/report-pack/published"],
            true,
            [],
            IsPublished: true,
            ReportPackId: "report-pack-fund-alpha-2026-06",
            ReportWorkflowState: "Published",
            PublicationManifestId: "manifest-fund-alpha-2026-06",
            RetainedManifestPath: "/evidence/report-packs/manifest-fund-alpha-2026-06.json",
            PublicationEvidenceHash: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            PublishedAtUtc: now,
            PublishedBy: "controller",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output%3Afund-alpha%3Alp-1%3Astatement",
            EvidenceRoute: "/api/workstation/evidence/subjects/report-pack-delivery/report-pack-fund-alpha-2026-06/packet");
        var shadowNavReportOutput = new PrivateCapitalReportOutputDto(
            "report-output:fund-alpha:shadow-nav:20260630",
            "ShadowNavPack",
            "Shadow NAV support package",
            "/workstation/reporting/shadow-nav-pack",
            fundEvent.FundEventId,
            "ShadowNav",
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            partnerCapitalEndingNetActivity,
            1,
            ["/evidence/shadow-nav-pack"],
            true,
            [],
            IsPublished: true,
            ReportPackId: "shadow-nav-pack-fund-alpha-2026-06",
            ReportWorkflowState: "Published",
            PublicationManifestId: "manifest-shadow-nav-fund-alpha-2026-06",
            RetainedManifestPath: "/evidence/report-packs/manifest-shadow-nav-fund-alpha-2026-06.json",
            PublicationEvidenceHash: "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            PublishedAtUtc: now,
            PublishedBy: "controller",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output%3Afund-alpha%3Ashadow-nav%3A20260630",
            EvidenceRoute: "/api/workstation/evidence/subjects/report-pack-delivery/shadow-nav-pack-fund-alpha-2026-06/packet");
        var administratorNavReportOutput = new PrivateCapitalReportOutputDto(
            "report-output:fund-alpha:administrator-nav:20260630",
            "AdministratorNav",
            "Administrator NAV statement",
            "/workstation/reporting/administrator-nav",
            fundEvent.FundEventId,
            "AdministratorNav",
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            administratorNav ?? partnerCapitalEndingNetActivity,
            1,
            ["/evidence/administrator-nav"],
            true,
            [],
            IsPublished: true,
            ReportPackId: "administrator-nav-fund-alpha-2026-06",
            ReportWorkflowState: "Published",
            PublicationManifestId: "manifest-administrator-nav-fund-alpha-2026-06",
            RetainedManifestPath: "/evidence/report-packs/manifest-administrator-nav-fund-alpha-2026-06.json",
            PublicationEvidenceHash: "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            PublishedAtUtc: now,
            PublishedBy: "administrator",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output%3Afund-alpha%3Aadministrator-nav%3A20260630",
            EvidenceRoute: "/api/workstation/evidence/subjects/report-pack-delivery/administrator-nav-fund-alpha-2026-06/packet");
        IReadOnlyList<PrivateCapitalReportOutputDto> closeReportOutputs = includeAdministratorNavEvidence
            ? [reportOutput, shadowNavReportOutput, administratorNavReportOutput]
            : [reportOutput, shadowNavReportOutput];
        var record = new PrivateCapitalFundEventLedgerRecordDto(
            "fund-event-record:fund-alpha:lp-1",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            0m,
            250m,
            fundEvent.Memo,
            null,
            null,
            "/api/ledger/private-capital/activity?fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630",
            "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet",
            fundEvent.ApprovalId,
            "/workstation/approvals/approval:fund-alpha:capital-call",
            true,
            true,
            true,
            true,
            PrivateCapitalFundEventLedgerReadinessDto.Published,
            "Published",
            "Published report output retained.",
            "Open statement",
            reportOutput.ReportOutputRoute,
            1,
            1,
            1,
            2,
            0,
            reportOutput.ReportOutputId,
            reportOutput.ReportOutputType,
            reportOutput.ReportOutputRoute,
            reportOutput.ReportWorkflowState,
            reportOutput.PublicationManifestId,
            reportOutput.RetainedManifestPath,
            reportOutput.ReportLineProvenanceCount,
            ["/evidence/source"],
            fundEvent,
            [subledgerEntry],
            [ledgerImpact],
            closeReportOutputs,
            [],
            [new PrivateCapitalEvidenceCategoryDto("source-support", "Source support", true, "Source support retained.", 1, ["/evidence/source"], ["Source support"])]);
        var managementFeeRecord = new PrivateCapitalFundEventLedgerRecordDto(
            "fund-event-record:fund-alpha:lp-1:management-fee",
            managementFeeEvent.FundEventId,
            managementFeeEvent.FundEventType,
            managementFeeEvent.CapitalAccountId,
            managementFeeEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            managementFeeEvent.JournalEntryId,
            managementFeeEvent.EffectiveDate,
            managementFeeEvent.Currency,
            managementFeeEvent.GrossAmount,
            managementFeeEvent.NetCapitalActivity,
            250m,
            235m,
            managementFeeEvent.Memo,
            null,
            null,
            "/api/ledger/private-capital/activity?fundEventId=fund-event%3Afund-alpha%3Amanagement-fee%3A20260630",
            "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Amanagement-fee%3A20260630/packet",
            managementFeeEvent.ApprovalId,
            "/workstation/approvals/approval:fund-alpha:management-fee",
            !includeIncompleteJournal,
            !includeIncompleteJournal,
            false,
            false,
            PrivateCapitalFundEventLedgerReadinessDto.Ready,
            includeIncompleteJournal ? "Review required" : "Ready",
            includeIncompleteJournal
                ? "Management fee journal still needs posted ledger impact."
                : includeExpenseFeeAllocationEvidence
                ? "Management fee allocation support retained."
                : "Management fee allocation support is missing.",
            "Open management fee evidence",
            includeExpenseFeeAllocationEvidence ? "/evidence/management-fee" : null,
            feeEvidenceLinks.Count,
            1,
            includeIncompleteJournal ? 0 : 1,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            feeEvidenceLinks,
            managementFeeEvent,
            [managementFeeSubledgerEntry],
            [managementFeeLedgerImpact],
            [],
            [],
            allocationEvidenceCategories);
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            250m,
            0m,
            0m,
            0m,
            15m,
            235m,
            2,
            managementFeeEvent.EffectiveDate,
            managementFeeEvent.FundEventType,
            [fundEvent.FundEventId, managementFeeEvent.FundEventId]);
        var subledger = new PrivateCapitalCapitalAccountSubledgerDto(
            "capital-account-subledger:fund-alpha:lp-1",
            FundProfileId,
            ledgerBookId,
            now,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
            250m,
            0m,
            0m,
            0m,
            15m,
            0m,
            partnerCapitalEndingNetActivity,
            235m,
            2,
            0,
            2,
            2,
            includeExpenseFeeAllocationEvidence ? 2 : 1,
            0,
            fundEvent.EffectiveDate,
            managementFeeEvent.EffectiveDate,
            managementFeeEvent.FundEventType,
            subledgerEvidenceLinks,
            capitalAccount,
            [record, managementFeeRecord],
            [subledgerEntry, managementFeeSubledgerEntry],
            [ledgerImpact, managementFeeLedgerImpact],
            closeReportOutputs,
            [],
            PrivateCapitalFundEventLedgerReadinessDto.Published,
            "Published",
            "Published statement retained.",
            "Open statement",
            reportOutput.ReportOutputRoute,
            allocationEvidenceCategories);

        return new PrivateCapitalActivityProjectionDto(
            FundProfileId,
            ledgerBookId,
            now,
            2,
            1,
            0,
            0,
            2,
            2,
            235m,
            "USD",
            [fundEvent, managementFeeEvent],
            [capitalAccount],
            [subledgerEntry, managementFeeSubledgerEntry],
            [ledgerImpact, managementFeeLedgerImpact],
            closeReportOutputs,
            [],
            [record, managementFeeRecord],
            [subledger],
            []);
    }

    private static OperationsCloseReadinessComponentDto ReadyComponent(
        string key,
        string label,
        OperationsGateKeyDto gate,
        string route)
        => new(
            key,
            label,
            100,
            10,
            true,
            "Ready",
            null,
            gate,
            route);

    private static OperationsGateDto Gate(OperationsGateKeyDto gate)
        => new(
            gate,
            gate.ToString(),
            OperationsGateStatusDto.Passed,
            IsRequired: true,
            Description: gate.ToString(),
            Blockers: [],
            NextActions: [],
            CompletedAtUtc: new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            CompletedBy: "controller");

    private static OperationsEvidenceLinkDto Evidence(string evidenceId, string label, string route)
        => new(
            evidenceId,
            label,
            route,
            "test",
            new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero));

    private sealed class StubManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
    {
        private readonly PrivateCapitalActivityProjectionDto _activity;

        public StubManualJournalEntryWorkbenchService(PrivateCapitalActivityProjectionDto activity)
        {
            _activity = activity;
        }

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([FundProfileId]);

        public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default)
            => Task.FromResult(new ManualJournalEntryWorkbenchDto(FundProfileId, ledgerBookId, DateTimeOffset.UtcNow, [], [], [], [], _activity));

        public Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_activity);
        }

        public Task<ManualJournalEntryDraftDto> SaveDraftAsync(SaveManualJournalEntryDraftRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(ValidateManualJournalEntryDraftRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubOperationsContinuityWorkflowService : IOperationsContinuityWorkflowService
    {
        private readonly IReadOnlyList<OperationsContinuityWorkflowDto> _workflows;

        public StubOperationsContinuityWorkflowService(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        {
            _workflows = workflows;
        }

        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
            Guid? fundAccountId = null,
            string? periodId = null,
            OperationsWorkflowStatusDto? status = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>>(
                _workflows
                    .Where(workflow => !fundAccountId.HasValue || workflow.FundAccountId == fundAccountId.Value)
                    .Where(workflow => string.IsNullOrWhiteSpace(periodId) || string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
                    .Where(workflow => !status.HasValue || workflow.Status == status.Value)
                    .Select(static workflow => new OperationsContinuityWorkflowSummaryDto(
                        workflow.WorkflowId,
                        workflow.FundAccountId,
                        workflow.PeriodId,
                        workflow.SecurityMasterSnapshotId,
                        workflow.BrokerSource,
                        workflow.Status,
                        workflow.Version,
                        workflow.CreatedAtUtc,
                        workflow.UpdatedAtUtc,
                        workflow.Gates,
                        workflow.NextActions))
                    .ToArray());
        }

        public Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_workflows.FirstOrDefault(workflow => workflow.WorkflowId == workflowId));
        }

        public Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationsTimelineEntryDto>>([]);

        public Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationsCloseChecklistTaskDto>>([]);

        public Task<OperationsTransitionResultDto> StartWorkflowAsync(OperationsStartWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ImportBrokerDataAsync(Guid workflowId, OperationsTransitionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> NormalizeBrokerTransactionsAsync(Guid workflowId, OperationsTransitionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RefreshGatePostureAsync(Guid workflowId, OperationsGatePostureRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ResolveSecurityMasterMappingsAsync(Guid workflowId, OperationsSecurityMasterResolveRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ApproveSecurityMasterOverrideAsync(Guid workflowId, string overrideId, OperationsSecurityMasterOverrideApprovalRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> BuildLedgerDraftAsync(Guid workflowId, OperationsLedgerDraftRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ValidateLedgerDraftAsync(Guid workflowId, OperationsLedgerValidationRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> PostLedgerEntriesAsync(Guid workflowId, OperationsLedgerPostRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RunReconciliationAsync(Guid workflowId, OperationsReconciliationRunRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ResolveBreakCaseAsync(Guid workflowId, string breakId, OperationsResolveBreakCaseRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> AssignBreakCaseAsync(Guid workflowId, string breakId, OperationsAssignBreakCaseRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> SubmitForApprovalAsync(Guid workflowId, OperationsSubmitApprovalRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ApproveWorkflowAsync(Guid workflowId, OperationsApprovalDecisionRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> RejectWorkflowAsync(Guid workflowId, OperationsRejectWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> CloseWorkflowAsync(Guid workflowId, OperationsCloseWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> ReopenWorkflowAsync(Guid workflowId, OperationsReopenWorkflowRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(Guid workflowId, string taskId, OperationsChecklistAcknowledgeRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
