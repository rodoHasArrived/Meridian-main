using FluentAssertions;
using Moq;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.Tests.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadServiceTests
{
    [Fact]
    public async Task GetCommandCenterAsync_WhenEvidencePackageMissing_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow(evidencePackages:
        [
            new OperationsEvidencePackageSummaryDto(
                "accounting-record",
                "Accounting record evidence",
                EvidenceStatusDto.Missing,
                false,
                "Retained source support is missing.",
                "/workstation/reporting/evidence",
                2,
                4,
                0,
                [])
        ]);
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.IsReadyToComplete.Should().BeFalse();
        var queueRow = commandCenter.QueueRows.Should().ContainSingle(row => row.SourceKind == "evidence-package").Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.Title.Should().Be("Accounting record evidence");
        queueRow.SeverityLabel.Should().Be("Missing");
        queueRow.SlaLabel.Should().Be("2/4 categories");
        queueRow.BlockerType.Should().Be("Evidence package");
        queueRow.CloseReportImpact.Should().Be("Review retained support.");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Status.Should().Be("Blocked");
        commandCenter.CloseSupportDecision.RetainedEvidenceGapCount.Should().Be(1);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Retained evidence gaps"
            && row.IsBlocking
            && row.Label == "Accounting record evidence");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenBlockedQueueRowIsOutsideDecisionDisplayLimit_ShouldBlockCloseSupport()
    {
        var workflow = CreateWorkflow(
            closeChecklist: Enumerable.Range(1, 12)
                .Select(index => new OperationsCloseChecklistTaskDto(
                    $"review-task-{index}",
                    OperationsGateKeyDto.BrokerIngest,
                    $"Review task {index}",
                    "controller",
                    "Review retained evidence.",
                    0,
                    null,
                    DateOnly.Parse("2026-06-04"),
                    "In progress",
                    null,
                    $"evidence:review-task-{index}",
                    "/workstation/accounting/operations-continuity",
                    true,
                    null,
                    null))
                .ToArray(),
            evidencePackages:
            [
                new OperationsEvidencePackageSummaryDto(
                    "retained-support",
                    "Retained support evidence",
                    EvidenceStatusDto.Missing,
                    false,
                    "Retained source support is missing.",
                    "/workstation/reporting/evidence",
                    0,
                    1,
                    0,
                    [])
            ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId);

        commandCenter.QueueRows.Should().HaveCount(13);
        commandCenter.QueueRows.Last().QueueId.Should().Be("evidence-package:retained-support");
        commandCenter.QueueRows.Last().IsBlocked.Should().BeTrue();
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions
            .Where(d => !d.DecisionId.StartsWith("close.", StringComparison.Ordinal)).Should().HaveCount(12);
        commandCenter.CloseSupportDecision.IsReady.Should().BeFalse();
        commandCenter.CloseSupportDecision.Status.Should().Be("Blocked");
        commandCenter.CloseSupportDecision.Summary.Should().Contain("block close");
        commandCenter.CloseReadiness!.Blockers.Should().Contain(b => b.Code == "evidence-package:retained-support");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenApprovalHistoryAndPeriodLockEvidenceMissing_ShouldPreserveEvidencePackageCapabilities()
    {
        var workflow = CreateWorkflow(evidencePackages:
        [
            new OperationsEvidencePackageSummaryDto(
                "approval-history:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb:2026-06",
                "Approval history evidence",
                EvidenceStatusDto.ReviewRequired,
                false,
                "Approval history has 1 of 3 required evidence categories complete.",
                "/workstation/accounting/approvals",
                1,
                3,
                1,
                [
                    new OperationsEvidenceLinkDto(
                        "approval-history-evidence",
                        "Approval submission evidence",
                        "/workstation/accounting/approvals",
                        "controller",
                        DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
                ],
                ["Publish close package with retained checklist-control approvals before audit release."]),
            new OperationsEvidencePackageSummaryDto(
                "period-lock-reopen:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb:2026-06",
                "Period lock and reopen evidence",
                EvidenceStatusDto.Missing,
                false,
                "Period has not been closed and no reopen evidence is retained.",
                "/workstation/accounting/operations-continuity",
                0,
                2,
                0,
                [],
                ["Close the workflow and retain the period-lock package before evidence release."])
        ]);
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId.StartsWith("evidence-package:approval-history:", StringComparison.Ordinal) &&
            row.SourceKind == "approval-history-evidence-package" &&
            row.BlockerType == "Approval history" &&
            row.Detail.Contains("Capability: Approval history.", StringComparison.Ordinal) &&
            row.CloseReportImpact.Contains("Approval history, reviewer decision", StringComparison.Ordinal));
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId.StartsWith("evidence-package:period-lock-reopen:", StringComparison.Ordinal) &&
            row.SourceKind == "period-lock-evidence-package" &&
            row.BlockerType == "Period close locks and reopen evidence" &&
            row.Detail.Contains("Capability: Period close locks and reopen evidence.", StringComparison.Ordinal) &&
            row.CloseReportImpact.Contains("Period close lock, governed reopen posture", StringComparison.Ordinal));
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(row =>
            row.Category == "Approval history" &&
            row.Label == "Approval history evidence" &&
            !row.IsBlocking);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Lock/reopen posture" &&
            row.Label == "Period lock and reopen evidence" &&
            row.IsBlocking);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenApprovalPending_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow(approvals:
        [
            new OperationsApprovalDto(
                "approval-close",
                OperationsApprovalStateDto.Submitted,
                "preparer",
                null,
                "Controller sign-off pending.",
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                null,
                [])
        ]);
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        var queueRow = commandCenter.QueueRows.Should().ContainSingle(row => row.SourceKind == "approval").Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.ActionLabel.Should().Be("Complete workflow approval.");
        queueRow.SeverityLabel.Should().Be("Pending approval");
        queueRow.SlaLabel.Should().Be("2026-06-01 00:00");
        queueRow.BlockerType.Should().Be("Approval");
        queueRow.CloseReportImpact.Should().Be("Close/report sign-off");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.PendingApprovalCount.Should().Be(1);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Approvals"
            && row.IsBlocking
            && row.RequiredAction == "Complete workflow approval.");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenCloseChecklistHasBlockedTasks_ShouldSurfaceChecklistMetricAndDecisionRows()
    {
        var workflow = CreateWorkflow(closeChecklist:
        [
            new OperationsCloseChecklistTaskDto(
                "receive-activity",
                OperationsGateKeyDto.BrokerIngest,
                "Receive Activity",
                "controller",
                "Retain broker activity file and intake evidence.",
                0,
                null,
                DateOnly.Parse("2026-06-04"),
                "Complete",
                null,
                "evidence:receive-activity",
                "/workstation/accounting/operations-continuity",
                true,
                DateTimeOffset.Parse("2026-06-04T16:00:00Z"),
                "controller"),
            new OperationsCloseChecklistTaskDto(
                "approve-results",
                OperationsGateKeyDto.Approval,
                "Approve Results",
                "controller",
                "Retain reviewer sign-off and report-pack evidence.",
                1,
                null,
                DateOnly.Parse("2026-06-05"),
                "Blocked",
                "Controller approval evidence is missing.",
                null,
                "/workstation/accounting/approvals",
                false,
                null,
                null)
        ]);
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "close-checklist" &&
            metric.Value == "1/2" &&
            metric.Status == "Blocked" &&
            metric.Detail.Contains("1 close checklist task", StringComparison.Ordinal));
        var queueRow = commandCenter.QueueRows.Should()
            .ContainSingle(row => row.QueueId == "checklist:approve-results")
            .Subject;
        queueRow.SourceKind.Should().Be("close-checklist");
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.BlockerType.Should().Be(nameof(OperationsGateKeyDto.Approval));
        queueRow.CloseReportImpact.Should().Be("Close checklist evidence and approval readiness");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(row =>
            row.Category == "Close checklists" &&
            row.Label == "Approve Results" &&
            row.IsBlocking);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenCoreFlowGateBlocked_ShouldSurfaceCoreFlowMetric()
    {
        var workflow = CreateWorkflow(gates:
        [
            Gate(OperationsGateKeyDto.BrokerIngest, OperationsGateStatusDto.Passed),
            Gate(OperationsGateKeyDto.SecurityMaster, OperationsGateStatusDto.Passed),
            Gate(OperationsGateKeyDto.Reconciliation, OperationsGateStatusDto.Blocked),
            Gate(OperationsGateKeyDto.Approval, OperationsGateStatusDto.NotStarted),
            Gate(OperationsGateKeyDto.LedgerPosting, OperationsGateStatusDto.NotStarted)
        ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "core-flow" &&
            metric.Label == "Core flow" &&
            metric.Value == "2/5" &&
            metric.Status == "Blocked" &&
            metric.Detail.Contains("Resolve Exceptions", StringComparison.Ordinal) &&
            metric.RouteHint == "/workstation/accounting/operations-continuity");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenCoreFlowGatesMissing_ShouldFallbackToWorkflowStates()
    {
        var workflow = CreateWorkflow();
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "core-flow" &&
            metric.Value == "5/5" &&
            metric.Status == "Ready" &&
            metric.Detail.Contains("Receive Activity -> Match Records -> Resolve Exceptions -> Approve Results -> Produce Evidence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenBreakCaseOpen_ShouldProjectDeterministicQueueMetadata()
    {
        var workflow = CreateWorkflow(breakCases:
        [
            new OperationsBreakCaseDto(
                "cash-break",
                "cash-tie-out",
                "Cash",
                "Critical",
                "Open",
                "controller",
                new DateOnly(2026, 6, 2),
                "Custodian",
                "Meridian ledger",
                100m,
                80m,
                20m,
                null,
                null,
                "Resolve cash break before close report release.",
                [
                    new OperationsEvidenceLinkDto(
                        "cash-statement",
                        "Cash statement",
                        "/workstation/accounting/reconciliation",
                        "custodian",
                        DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
                ],
                SlaState: "Warning",
                SlaDueAtUtc: DateTimeOffset.Parse("2026-06-02T18:00:00Z"),
                EscalationLevel: "Controller",
                EscalationReason: "Critical break blocks close-report and NAV outputs.",
                BlockedOutputs: ["close-report", "nav"])
        ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        var row = commandCenter.QueueRows.Should().ContainSingle(item => item.QueueId == "break:cash-break").Subject;
        row.StatusLabel.Should().Be("Open");
        row.OwnerLabel.Should().Be("controller");
        row.DueLabel.Should().Be("Warning 2026-06-02 18:00Z");
        row.SeverityLabel.Should().Be("Critical");
        row.SlaLabel.Should().Be("Warning 2026-06-02 18:00Z");
        row.BlockerType.Should().Be("close-report, nav");
        row.CloseReportImpact.Should().Be("Blocks close-report, nav");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "exception-management" &&
            metric.Label == "Exception management" &&
            metric.Value == "1" &&
            metric.Status == "Blocked" &&
            metric.Detail.Contains("block NAV, close-report, or evidence outputs", StringComparison.Ordinal) &&
            metric.RouteHint == "/workstation/accounting/exceptions");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenBreakCaseIsUnassignedAndSlaBreached_ShouldRequireAssignmentAndEscalation()
    {
        var workflow = CreateWorkflow(breakCases:
        [
            new OperationsBreakCaseDto(
                "income-break",
                "income-accrual",
                "Income",
                "High",
                "Open",
                null,
                new DateOnly(2026, 6, 1),
                "Custodian income file",
                "Meridian accrual ledger",
                1_000m,
                940m,
                60m,
                "security-1",
                "ABC",
                null,
                [],
                SlaState: "Breached",
                SlaDueAtUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"))
        ]);
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.IsReadyToComplete.Should().BeFalse();
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "exception-management" &&
            metric.Value == "1" &&
            metric.Status == "Blocked" &&
            metric.Detail.Contains("require break assignment or escalation", StringComparison.Ordinal));
        var row = commandCenter.QueueRows.Should().ContainSingle(item => item.QueueId == "break:income-break").Subject;
        row.OwnerLabel.Should().Be("Unassigned");
        row.IsBlocked.Should().BeTrue();
        row.ActionLabel.Should().Be("Assign owner and escalate break before close sign-off.");
        row.SeverityLabel.Should().Be("High");
        row.SlaLabel.Should().Be("Breached 2026-06-01 12:00Z");
        row.BlockerType.Should().Be("Assignment/escalation");
        row.CloseReportImpact.Should().Be("Break assignment/escalation required");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(decision =>
            decision.Category == "Unresolved exceptions"
            && decision.Label == "income-break"
            && decision.IsBlocking
            && decision.RequiredAction == "Assign owner and escalate break before close sign-off.");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenCloseReadinessHasWorkflowBlockers_ShouldSurfaceCoreFlowControlRows()
    {
        var workflow = CreateWorkflow(closeReadiness: new OperationsCloseReadinessDto(
            false,
            "Blocked",
            42,
            [],
            [
                new OperationsCloseReadinessBlockerDto(
                    "broker-intake-missing",
                    "Broker intake",
                    "Blocked",
                    "Custodian cash activity has not been received for the close scope.",
                    OperationsGateKeyDto.BrokerIngest,
                    "/workstation/accounting/operations-continuity")
            ],
            [
                new OperationsNextActionDto(
                    "review-reconciliation",
                    "Review unmatched position and cash exceptions.",
                    "/workstation/accounting/reconciliation",
                    OperationsGateKeyDto.Reconciliation)
            ]));
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "workflow-control" &&
            metric.Value == "2" &&
            metric.Status == "Blocked");
        var blocker = commandCenter.QueueRows.Should()
            .ContainSingle(item => item.QueueId == "workflow-blocker:broker-intake-missing")
            .Subject;
        blocker.KindLabel.Should().Be("Workflow control");
        blocker.Title.Should().Be("Broker intake");
        blocker.StatusLabel.Should().Be("Blocked");
        blocker.IsBlocked.Should().BeTrue();
        blocker.SlaLabel.Should().Be("Receive Activity");
        blocker.ActionLabel.Should().Be("Resolve receive activity blocker.");
        blocker.BlockerType.Should().Be("Workflow blocker");
        blocker.CloseReportImpact.Should().Be("Activity intake must complete before matching and close evidence.");

        var nextAction = commandCenter.QueueRows.Should()
            .ContainSingle(item => item.QueueId == "workflow-action:review-reconciliation")
            .Subject;
        nextAction.IsBlocked.Should().BeFalse();
        nextAction.Title.Should().Be("Resolve Exceptions");
        nextAction.ActionLabel.Should().Be("Review unmatched position and cash exceptions.");
        nextAction.CloseReportImpact.Should().Be("Exceptions must be resolved before approval and evidence release.");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(decision =>
            decision.Category == "Workflow control"
            && decision.Label == "Broker intake"
            && decision.IsBlocking);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenAccountingRecordEvidenceIsIncomplete_ShouldSurfaceAuditEvidenceRows()
    {
        var sourceEvidence = new OperationsEvidenceLinkDto(
            "source-data",
            "Source data",
            "/workstation/accounting",
            "custodian",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var workflow = CreateWorkflow(accountingRecordSummary: new OperationsAccountingRecordSummaryDto(
            "accounting-record-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb-2026-06",
            false,
            1,
            3,
            "Accounting record has 1 of 3 required evidence categories complete.",
            [
                new OperationsAccountingRecordEvidenceCategoryDto(
                    "source-records",
                    "Retained source data",
                    true,
                    "Provider and account source data is retained for the close lane.",
                    "/workstation/accounting",
                    [sourceEvidence],
                    ["provider statement"]),
                new OperationsAccountingRecordEvidenceCategoryDto(
                    "ledger-evidence",
                    "Journal and ledger evidence",
                    false,
                    "Ledger preview or posting evidence has not been linked.",
                    "/workstation/accounting/ledger",
                    [],
                    ["journal preview", "posted ledger batch"]),
                new OperationsAccountingRecordEvidenceCategoryDto(
                    "exports",
                    "Exports and retained evidence",
                    false,
                    "Export manifest and retained evidence hash still need close-package publication.",
                    "/workstation/reporting/report-packs",
                    [],
                    ["export manifest", "retained evidence hash"])
            ],
            [sourceEvidence],
            new FundAuditPackReadinessDto(
                false,
                75,
                60,
                false,
                [FundAuditEvidenceCategoryKeyDto.LedgerEvidence, FundAuditEvidenceCategoryKeyDto.Exports],
                ["Audit pack SLA missed."],
                [])));
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "evidence" &&
            metric.Value == "3" &&
            metric.Status == "Blocked");
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId.EndsWith(":ledger-evidence", StringComparison.OrdinalIgnoreCase) &&
            row.SourceKind == "accounting-record-evidence" &&
            row.IsBlocked &&
            row.ActionLabel == "Retain journal preview, posted ledger batch before close sign-off." &&
            row.CloseReportImpact == "Journal and ledger evidence must be retained before accounting close support.");
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId.EndsWith(":audit-pack", StringComparison.OrdinalIgnoreCase) &&
            row.BlockerType == "Audit evidence package" &&
            row.SeverityLabel == "Blocked" &&
            row.ActionLabel.Contains(nameof(FundAuditEvidenceCategoryKeyDto.LedgerEvidence), StringComparison.Ordinal));
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.RetainedEvidenceGapCount.Should().Be(3);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Retained evidence gaps" &&
            row.Label == "Journal and ledger evidence" &&
            row.IsBlocking);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenReviewedAutomationRequiresHumanReview_ShouldSurfaceWorkflowControlRows()
    {
        var evidence = new OperationsEvidenceLinkDto(
            "reconciliation-match-evidence",
            "Suggested match evidence",
            "/workstation/accounting/reconciliation",
            "reviewed-automation",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var workflow = CreateWorkflow(reviewedAutomation: new OperationsReviewedAutomationSummaryDto(
            "reviewed-automation",
            "Suggested matches require review",
            EvidenceStatusDto.ReviewRequired,
            true,
            "Automation may suggest reconciliation matches, but match promotion remains operator-reviewed.",
            ["Suggest reconciliation matches"],
            ["Resolve reconciliation breaks", "Approve exceptions"],
            [evidence],
            ["Review suggested matches before resolving breaks."],
            [
                new OperationsReviewedAutomationArtifactDto(
                    "reviewed-automation:reconciliation-match-suggestion",
                    "Suggested match",
                    "Reconciliation match candidate",
                    EvidenceStatusDto.ReviewRequired,
                    true,
                    88m,
                    "Suggested matches and variance explanations are retained for operator review.",
                    "Review match rationale and variance evidence before resolving breaks.",
                    "Cannot resolve reconciliation breaks or approve exceptions.",
                    [evidence],
                    ["Confirm match support."])
            ]));
        var service = CreateService(workflow);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        var summary = commandCenter.QueueRows.Should()
            .ContainSingle(row => row.QueueId == "reviewed-automation:reviewed-automation")
            .Subject;
        summary.IsBlocked.Should().BeFalse();
        summary.KindLabel.Should().Be("Reviewed automation");
        summary.SeverityLabel.Should().Be("Human review");
        summary.BlockerType.Should().Be("Reviewed automation");
        summary.CloseReportImpact.Should().Be("Material Financial Operations actions remain human-gated.");

        var artifact = commandCenter.QueueRows.Should()
            .ContainSingle(row => row.QueueId == "reviewed-automation-artifact:reviewed-automation-reconciliation-match-suggestion")
            .Subject;
        artifact.IsBlocked.Should().BeFalse();
        artifact.KindLabel.Should().Be("Automation artifact");
        artifact.ActionLabel.Should().Be("Review match rationale and variance evidence before resolving breaks.");
        artifact.CloseReportImpact.Should().Be("Cannot resolve reconciliation breaks or approve exceptions.");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "workflow-control" &&
            metric.Value == "2" &&
            metric.Status == "Review");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(decision =>
            decision.Category == "Workflow control" &&
            decision.Label == "Suggested matches require review" &&
            !decision.IsBlocking);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenOperationalDashboardBlocked_ShouldSurfaceDashboardQueueAndDecisionRows()
    {
        var dashboardEvidence = new OperationsEvidenceLinkDto(
            "dashboard-reconciliation",
            "Reconciliation dashboard evidence",
            "/workstation/accounting/reconciliation",
            "operations-dashboard",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var workflow = CreateWorkflow(dashboardSummary: new OperationsDashboardSummaryDto(
            "operations-dashboard:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb:2026-06",
            "Resolve Exceptions",
            EvidenceStatusDto.Blocked,
            false,
            1,
            2,
            "Financial Operations dashboard has unresolved exceptions.",
            [
                new OperationsDashboardMetricDto(
                    "match-records",
                    "Match Records",
                    "1/2 lanes ready",
                    EvidenceStatusDto.Blocked,
                    "Position reconciliation lane is blocked.",
                    "/workstation/accounting/reconciliation",
                    [dashboardEvidence],
                    ["Resolve blocked position reconciliation lane."])
            ],
            [dashboardEvidence],
            ["Escalate dashboard blocker to fund operations."]));
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.IsReadyToComplete.Should().BeFalse();
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "dashboard" &&
            metric.Value == "2" &&
            metric.Status == "Blocked");
        commandCenter.QueueRows.Should().Contain(row =>
            row.SourceKind == "dashboard-metric" &&
            row.Title == "Match Records" &&
            row.IsBlocked &&
            row.BlockerType == "Operational dashboard" &&
            row.ActionLabel == "Resolve blocked position reconciliation lane.");
        commandCenter.QueueRows.Should().Contain(row =>
            row.SourceKind == "dashboard-action" &&
            row.Title == "Resolve Exceptions" &&
            row.ActionLabel == "Escalate dashboard blocker to fund operations.");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(row =>
            row.Category == "Operational dashboards" &&
            row.IsBlocking &&
            row.Label == "Match Records");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenNamedReconciliationLanesNeedReview_ShouldPreserveCapabilitySpecificImpact()
    {
        var workflow = CreateWorkflow(reconciliationLanes:
        [
            ReconciliationLane(
                "mbs-factor-reconciliation",
                "MBS factor reconciliation",
                OperationsReconciliationLaneStatusDto.Blocked,
                "MBS factor paydown schedule variance requires review.",
                "Retain MBS factor schedule and approve paydown variance."),
            ReconciliationLane(
                "bank-reconciliation",
                "Bank reconciliation",
                OperationsReconciliationLaneStatusDto.ReviewRequired,
                "Bank statement cash movement support is incomplete.",
                "Attach bank statement evidence before close approval."),
            ReconciliationLane(
                "gl-reconciliation",
                "GL reconciliation support",
                OperationsReconciliationLaneStatusDto.ReviewRequired,
                "External GL tie-out has unresolved review rows.",
                "Resolve external GL tie-out review rows.")
        ]);
        var service = CreateService(workflow);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "reconciliation" &&
            metric.Value == "0/3" &&
            metric.Status == "Blocked" &&
            metric.Detail.Contains("cash, position, trade, income, MBS factor, bank, or GL reconciliation", StringComparison.Ordinal));
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId == "reconciliation-lane:mbs-factor-reconciliation" &&
            row.BlockerType == "MBS factor reconciliation" &&
            row.Detail.Contains("Capability: MBS factor reconciliation.", StringComparison.Ordinal) &&
            row.CloseReportImpact.Contains("MBS factor, paydown, principal schedule", StringComparison.Ordinal) &&
            row.ActionLabel == "Retain MBS factor schedule and approve paydown variance.");
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId == "reconciliation-lane:bank-reconciliation" &&
            row.BlockerType == "Bank reconciliation" &&
            row.CloseReportImpact.Contains("Bank statement, payment, cash movement", StringComparison.Ordinal));
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId == "reconciliation-lane:gl-reconciliation" &&
            row.BlockerType == "GL reconciliation support" &&
            row.CloseReportImpact.Contains("GL tie-out, external GL reconciliation", StringComparison.Ordinal));
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.UnresolvedExceptionCount.Should().Be(3);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenPeriodLockSupportMissing_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow();
        var calendar = new OperationsCloseCalendarDto(
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            [
                new OperationsCloseCalendarItemDto(
                    workflow.WorkflowId,
                    workflow.FundAccountId,
                    workflow.PeriodId,
                    workflow.Status,
                    workflow.Version,
                    DateOnly.FromDateTime(DateTime.Parse("2026-06-05")),
                    "period-lock",
                    "Retain period-lock evidence",
                    "Controller",
                    "Blocked",
                    62,
                    false,
                    1,
                    1,
                    1,
                    0,
                    "/workstation/accounting/approvals")
            ]);
        var service = CreateService(workflow, calendar);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        var queueRow = commandCenter.QueueRows.Should().ContainSingle(row => row.SourceKind == "close-calendar").Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.Title.Should().Be("Retain period-lock evidence");
        queueRow.SeverityLabel.Should().Be("Blocked");
        queueRow.SlaLabel.Should().Be("2026-06-05");
        queueRow.BlockerType.Should().Be("period-lock");
        queueRow.CloseReportImpact.Should().Be("Period lock/reopen readiness");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.LockReopenPosture.Should().Contain("Period lock");
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Lock/reopen posture"
            && row.IsBlocking
            && row.Label == "Period lock calendar");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenNavCloseSupportMissing_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow();
        var cockpit = CreateCockpit(workflow, navReady: false);
        var service = CreateService(workflow, cockpit: cockpit);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.QueueRows.Should().Contain(row =>
            row.SourceKind == "nav-support"
            && row.IsBlocked
            && row.Title == "NAV support package");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.NavReportDependencyPosture.Should().Contain("0/1 private-capital close lane");
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "NAV/report dependencies"
            && row.IsBlocking
            && row.Label == "NAV and report dependency posture");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenPrivateCapitalAccountingGapsRemain_ShouldPreserveNamedFinopsCapabilities()
    {
        var workflow = CreateWorkflow();
        var evidence = new OperationsEvidenceLinkDto(
            "partner-capital-evidence",
            "Partner capital tie-out evidence",
            "/workstation/accounting/capital-accounts",
            "private-capital",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var cockpit = CreateCockpit(
            workflow,
            navReady: true,
            lanes:
            [
                new PrivateCapitalCloseCockpitLaneDto(
                    "partner-capital-tie-outs",
                    "Partner capital account tie-outs",
                    EvidenceStatusDto.Blocked,
                    false,
                    "Partner capital account tie-out support is missing retained statement evidence.",
                    "/workstation/accounting/capital-accounts",
                    1,
                    [evidence],
                    ["Retain partner capital tie-out evidence across subledger, ledger, and statement output"]),
                new PrivateCapitalCloseCockpitLaneDto(
                    "expense-fee-allocation",
                    "Expense, fee, and allocation review",
                    EvidenceStatusDto.Blocked,
                    false,
                    "Management fee allocation support is missing.",
                    "/workstation/accounting/capital-accounts",
                    1,
                    [evidence],
                    ["Retain posted expense, fee, and allocation review evidence"])
            ],
            evidencePackages:
            [
                new OperationsEvidencePackageSummaryDto(
                    "private-capital:fund-event-accounting",
                    "Fund-event accounting evidence",
                    EvidenceStatusDto.Missing,
                    false,
                    "Fund-event journal posting and capital-account roll-forward evidence is incomplete.",
                    "/workstation/accounting/capital-accounts",
                    1,
                    4,
                    1,
                    [evidence],
                    ["Retain fund-event accounting evidence before close sign-off."])
            ]);
        var service = CreateService(workflow, cockpit: cockpit);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha",
            fundAccountId: workflow.FundAccountId,
            periodId: workflow.PeriodId,
            ct: cts.Token);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId == "private-capital-lane:partner-capital-tie-outs" &&
            row.SourceKind == "private-capital-partner-capital-tie-out" &&
            row.BlockerType == "Partner capital account tie-outs" &&
            row.Detail.Contains("Capability: Partner capital account tie-outs.", StringComparison.Ordinal) &&
            row.CloseReportImpact.Contains("Partner capital account statement tie-out", StringComparison.Ordinal));
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId == "private-capital-lane:expense-fee-allocation" &&
            row.SourceKind == "private-capital-expense-fee-allocation" &&
            row.BlockerType == "Expense, fee, and allocation review" &&
            row.CloseReportImpact.Contains("management-fee allocation", StringComparison.Ordinal));
        commandCenter.QueueRows.Should().Contain(row =>
            row.QueueId == "private-capital-package:private-capital:fund-event-accounting" &&
            row.SourceKind == "private-capital-fund-event-accounting" &&
            row.BlockerType == "Fund-event accounting support" &&
            row.CloseReportImpact.Contains("Fund-event accounting", StringComparison.Ordinal));
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.Decisions.Should().Contain(row =>
            row.Category == "Partner capital account tie-outs" &&
            row.Label == "Partner capital account tie-outs" &&
            row.IsBlocking);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Expense, fee, and allocation review" &&
            row.Label == "Expense, fee, and allocation review" &&
            row.IsBlocking);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Fund-event accounting support" &&
            row.Label == "Fund-event accounting evidence" &&
            row.IsBlocking);
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenPrivateCapitalApprovalPending_ShouldBlockCompletion()
    {
        var workflow = CreateWorkflow();
        var approvalEvidence = new OperationsEvidenceLinkDto(
            "private-capital-approval-evidence",
            "Private-capital approval evidence",
            "/workstation/accounting/capital-accounts/approvals",
            "controller",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var cockpit = CreateCockpit(
            workflow,
            navReady: true,
            approvals:
            [
                new PrivateCapitalCloseCockpitApprovalDto(
                    "approval-private-capital-close",
                    workflow.WorkflowId,
                    workflow.FundAccountId,
                    workflow.PeriodId,
                    OperationsApprovalStateDto.Submitted,
                    "preparer",
                    "controller",
                    "Controller private-capital close approval pending.",
                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    null,
                    "/workstation/accounting/capital-accounts/approvals",
                    1,
                    [approvalEvidence])
            ]);
        var service = CreateService(workflow, cockpit: cockpit);

        var commandCenter = await service.GetCommandCenterAsync(fundProfileId: "fund-alpha", ledgerBookId: BookId, entityId: "entity-alpha", fundAccountId: workflow.FundAccountId, periodId: workflow.PeriodId);

        commandCenter.Status.Should().Be("Blocked");
        commandCenter.Metrics.Should().Contain(metric =>
            metric.MetricId == "approvals" &&
            metric.Value == "1" &&
            metric.Status == "Blocked");
        var queueRow = commandCenter.QueueRows.Should()
            .ContainSingle(row => row.QueueId == "private-capital-approval:approval-private-capital-close")
            .Subject;
        queueRow.IsBlocked.Should().BeTrue();
        queueRow.SeverityLabel.Should().Be("Pending approval");
        queueRow.ActionLabel.Should().Be("Complete private-capital approval.");
        queueRow.CloseReportImpact.Should().Be("Private-capital close sign-off");
        commandCenter.CloseSupportDecision.Should().NotBeNull();
        commandCenter.CloseSupportDecision!.PendingApprovalCount.Should().Be(1);
        commandCenter.CloseSupportDecision.Decisions.Should().Contain(row =>
            row.Category == "Approvals" &&
            row.IsBlocking &&
            row.Label == "approval-private-capital-close");
    }

    private static readonly Guid BookId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static ILedgerBookService CreateBookService()
    {
        var mock = new Mock<ILedgerBookService>();
        mock.Setup(x => x.GetBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookDto(BookId, "fund-alpha", Guid.NewGuid(), default, "Book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return mock.Object;
    }

    private static FinancialOperationsCommandCenterReadService CreateService(
        OperationsContinuityWorkflowDto workflow,
        OperationsCloseCalendarDto? calendar = null,
        PrivateCapitalCloseCockpitDto? cockpit = null)
        => new(
            new StubOperationsContinuityWorkflowService(workflow),
            calendar is null ? null : new StubCloseCalendarService(calendar),
            cockpit is null ? null : new StubPrivateCapitalCloseCockpitService(cockpit), CreateBookService(), CreateClosePlanService(workflow));

    private static OperationsContinuityWorkflowDto CreateWorkflow(
        IReadOnlyList<OperationsApprovalDto>? approvals = null,
        IReadOnlyList<OperationsEvidencePackageSummaryDto>? evidencePackages = null,
        IReadOnlyList<OperationsBreakCaseDto>? breakCases = null,
        OperationsDashboardSummaryDto? dashboardSummary = null,
        IReadOnlyList<OperationsReconciliationLaneSummaryDto>? reconciliationLanes = null,
        OperationsCloseReadinessDto? closeReadiness = null,
        OperationsAccountingRecordSummaryDto? accountingRecordSummary = null,
        OperationsReviewedAutomationSummaryDto? reviewedAutomation = null,
        IReadOnlyList<OperationsCloseChecklistTaskDto>? closeChecklist = null,
        IReadOnlyList<OperationsGateDto>? gates = null)
    {
        var workflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var fundAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var createdAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        return new OperationsContinuityWorkflowDto(
            workflowId,
            fundAccountId,
            "2026-06",
            null,
            "fixture",
            createdAt,
            createdAt,
            7,
            OperationsWorkflowStatusDto.ReadyForClose,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.Approved,
            gates ?? [],
            [],
            breakCases ?? [],
            null,
            approvals ?? [],
            new OperationsReportPackReadinessDto(true, "report-pack-2026-06", null, []),
            closeChecklist ?? [],
            [],
            [],
            [],
            closeReadiness ?? new OperationsCloseReadinessDto(true, "Ready", 100, [], [], []),
            AccountingRecordSummary: accountingRecordSummary,
            ReconciliationLanes: reconciliationLanes,
            DashboardSummary: dashboardSummary,
            EvidencePackages: evidencePackages,
            ReviewedAutomation: reviewedAutomation, LedgerBookId: BookId);
    }

    private static OperationsGateDto Gate(OperationsGateKeyDto gate, OperationsGateStatusDto status)
        => new(
            gate,
            gate switch
            {
                OperationsGateKeyDto.BrokerIngest => "Receive Activity",
                OperationsGateKeyDto.SecurityMaster => "Match Records",
                OperationsGateKeyDto.Reconciliation => "Resolve Exceptions",
                OperationsGateKeyDto.Approval => "Approve Results",
                _ => "Produce Evidence"
            },
            status,
            true,
            "Test gate",
            [],
            [],
            status is OperationsGateStatusDto.Passed ? DateTimeOffset.Parse("2026-06-01T00:00:00Z") : null,
            status is OperationsGateStatusDto.Passed ? "controller" : null);

    private static OperationsReconciliationLaneSummaryDto ReconciliationLane(
        string laneId,
        string label,
        OperationsReconciliationLaneStatusDto status,
        string summary,
        string requiredAction)
        => new(
            laneId,
            label,
            status,
            false,
            1,
            summary,
            "/workstation/accounting/reconciliation",
            [
                new OperationsEvidenceLinkDto(
                    $"evidence:{laneId}",
                    $"{label} evidence",
                    "/workstation/accounting/reconciliation",
                    "fixture",
                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
            ],
            [requiredAction]);

    private static PrivateCapitalCloseCockpitDto CreateCockpit(
        OperationsContinuityWorkflowDto workflow,
        bool navReady,
        IReadOnlyList<PrivateCapitalCloseCockpitApprovalDto>? approvals = null,
        IReadOnlyList<PrivateCapitalCloseCockpitLaneDto>? lanes = null,
        IReadOnlyList<OperationsEvidencePackageSummaryDto>? evidencePackages = null)
    {
        var cockpitLanes = lanes ?? [];
        var cockpitEvidencePackages = evidencePackages ?? [];
        var cockpitApprovals = approvals ?? [];
        var isReadyToClose = navReady
            && cockpitLanes.All(static lane => lane.IsReady)
            && cockpitEvidencePackages.All(static package => package.IsReady)
            && cockpitApprovals.All(static approval => approval.Status is OperationsApprovalStateDto.Approved);
        var readyLaneCount = cockpitLanes.Count(static lane => lane.IsReady);
        var blockedLaneCount = cockpitLanes.Count(static lane => !lane.IsReady);

        return new(
            "fund-alpha",
            BookId,
            workflow.FundAccountId,
            workflow.PeriodId,
            "entity-alpha",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            "/workstation/accounting/capital-accounts",
            isReadyToClose ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked,
            isReadyToClose,
            isReadyToClose ? 100 : 60,
            1,
            0,
            0,
            0,
            0,
            readyLaneCount,
            blockedLaneCount,
            cockpitLanes,
            [new(workflow.WorkflowId, workflow.FundAccountId, workflow.PeriodId, workflow.Status,
                100, true, "/workstation/accounting", null, null, 0, 0, workflow.UpdatedAtUtc)],
            [],
            [],
            [],
            [],
            ApprovalHistory: cockpitApprovals,
            NavSupportPackages:
            [
                new PrivateCapitalNavSupportPackageDto(
                    "nav-support",
                    "NAV support package",
                    navReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked,
                    navReady,
                    navReady ? "NAV support retained." : "NAV support package still needs positions and pricing evidence.",
                    "/workstation/accounting/capital-accounts/nav",
                    1_000_000m,
                    "USD",
                    0,
                    [],
                    [],
                    ["Retain NAV support before close sign-off."])
            ],
            EvidencePackages: cockpitEvidencePackages);
    }

    private sealed class StubOperationsContinuityWorkflowService(OperationsContinuityWorkflowDto workflow)
        : IOperationsContinuityWorkflowService
    {
        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
            Guid? fundAccountId = null,
            string? periodId = null,
            OperationsWorkflowStatusDto? status = null,
            CancellationToken ct = default,
            Guid? ledgerBookId = null)
            => Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>>(
                [
                    new OperationsContinuityWorkflowSummaryDto(
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
                        workflow.NextActions,
                        workflow.LedgerBookId)
                ]);

        public Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult<OperationsContinuityWorkflowDto?>(workflowId == workflow.WorkflowId ? workflow : null);

        public Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OperationsTimelineEntryDto>>([]);
        public Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OperationsCloseChecklistTaskDto>>([]);
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

    private sealed class StubCloseCalendarService(OperationsCloseCalendarDto calendar) : IOperationsCloseCalendarService
    {
        public Task<OperationsCloseCalendarDto> GetCalendarAsync(Guid? fundAccountId = null, string? periodId = null, CancellationToken ct = default)
            => Task.FromResult(calendar);

        public Task<OperationsCloseCalendarItemUpsertResultDto> UpsertItemAsync(OperationsCloseCalendarItemUpsertRequestDto request, string actor, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubPrivateCapitalCloseCockpitService(PrivateCapitalCloseCockpitDto cockpit) : IPrivateCapitalCloseCockpitService
    {
        public Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(string? fundProfileId = null, Guid? ledgerBookId = null, Guid? fundAccountId = null, string? periodId = null, string? entityId = null, CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => Task.FromResult(cockpit);
    }
}
