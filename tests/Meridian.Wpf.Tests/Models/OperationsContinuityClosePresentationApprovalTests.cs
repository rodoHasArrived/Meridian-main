using Meridian.Contracts.Workstation;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class OperationsContinuityClosePresentationApprovalTests
{
    [Theory]
    [InlineData("checklist-task")]
    [InlineData("checklist-actor")]
    [InlineData("checklist-time")]
    [InlineData("checklist-time-tick")]
    [InlineData("checklist-missing-time")]
    [InlineData("checklist-missing-actor")]
    [InlineData("checklist-blocked")]
    [InlineData("checklist-expired")]
    [InlineData("checklist-blocking-reason")]
    [InlineData("checklist-missing-evidence")]
    [InlineData("approval-task")]
    [InlineData("workflow-approval-state")]
    [InlineData("decision-status")]
    [InlineData("submitter")]
    [InlineData("submission-time")]
    [InlineData("submission-time-tick")]
    [InlineData("submission-missing-time")]
    [InlineData("reviewer")]
    [InlineData("review-time")]
    [InlineData("review-time-tick")]
    [InlineData("review-missing-time")]
    public void Build_RefusesDifferentForwardedApprovalsAtSameVersion_AndMatchingRefreshRepairs(string change)
    {
        var selected = CreateWorkflow();
        var task = selected.CloseChecklist[0];
        var decision = selected.Approvals[0];
        var active = change switch
        {
            "checklist-task" => ReplaceTask(selected, task with { TaskId = "different-task" }),
            "checklist-actor" => ReplaceTask(selected, task with { AcknowledgedBy = "different-operator" }),
            "checklist-time" => ReplaceTask(selected, task with { AcknowledgedAtUtc = Timestamp.AddMinutes(1) }),
            "checklist-time-tick" => ReplaceTask(selected, task with { AcknowledgedAtUtc = Timestamp.AddTicks(1) }),
            "checklist-missing-time" => ReplaceTask(selected, task with { AcknowledgedAtUtc = null }),
            "checklist-missing-actor" => ReplaceTask(selected, task with { AcknowledgedBy = " " }),
            "checklist-blocked" => ReplaceTask(selected, task with { Status = " BLOCKED " }),
            "checklist-expired" => ReplaceTask(selected, task with { Status = "expired" }),
            "checklist-blocking-reason" => ReplaceTask(selected, task with { BlockingReason = "Evidence is stale" }),
            "checklist-missing-evidence" => ReplaceTask(selected, task with { EvidencePointer = " " }),
            "approval-task" => selected with { CloseChecklist = [task, selected.CloseChecklist[1] with { TaskId = "different-approval-task" }] },
            "workflow-approval-state" => selected with { ApprovalState = OperationsApprovalStateDto.Submitted },
            "decision-status" => selected with { Approvals = [decision with { Status = OperationsApprovalStateDto.Submitted }] },
            "submitter" => selected with { Approvals = [decision with { Operator = "different-submitter" }] },
            "submission-time" => selected with { Approvals = [decision with { SubmittedAtUtc = Timestamp.AddMinutes(1) }] },
            "submission-time-tick" => selected with { Approvals = [decision with { SubmittedAtUtc = Timestamp.AddTicks(1) }] },
            "submission-missing-time" => selected with { Approvals = [decision with { SubmittedAtUtc = null }] },
            "reviewer" => selected with { Approvals = [decision with { Reviewer = "different-reviewer" }] },
            "review-time" => selected with { Approvals = [decision with { DecidedAtUtc = Timestamp.AddMinutes(1) }] },
            "review-time-tick" => selected with { Approvals = [decision with { DecidedAtUtc = Timestamp.AddTicks(1) }] },
            "review-missing-time" => selected with { Approvals = [decision with { DecidedAtUtc = null }] },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        AssertMismatchAndRepair(selected, active);
    }

    [Theory]
    [InlineData("submitter")]
    [InlineData("submission-time")]
    [InlineData("submission-time-tick")]
    [InlineData("submission-status")]
    [InlineData("reviewer")]
    [InlineData("review-time-tick")]
    public void Build_RefusesDifferentLegacySubmissionOrDecision_AndMatchingRefreshRepairs(string change)
    {
        var selected = CreateLegacyWorkflow();
        var submission = selected.Approvals[0];
        var decision = selected.Approvals[1];
        var active = change switch
        {
            "submitter" => selected with { Approvals = [submission with { Operator = "different-submitter" }, decision] },
            "submission-time" => selected with { Approvals = [submission with { SubmittedAtUtc = Timestamp.AddMinutes(1) }, decision] },
            "submission-time-tick" => selected with { Approvals = [submission with { SubmittedAtUtc = Timestamp.AddTicks(1) }, decision] },
            "submission-status" => selected with { Approvals = [submission with { Status = OperationsApprovalStateDto.Rejected }, decision] },
            "reviewer" => selected with { Approvals = [submission, decision with { Reviewer = "different-reviewer" }] },
            "review-time-tick" => selected with { Approvals = [submission, decision with { DecidedAtUtc = Timestamp.AddTicks(1) }] },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        AssertMismatchAndRepair(selected, active);
    }

    [Theory]
    [InlineData(OperationsApprovalStateDto.Submitted)]
    [InlineData(OperationsApprovalStateDto.ReviewerAssigned)]
    public void Build_LegacyAndCurrentApprovalStorageMatchWhenTheyForwardTheSameIdentities(OperationsApprovalStateDto pendingState)
    {
        var selected = CreateWorkflow();
        var legacy = CreateLegacyWorkflow();
        legacy = legacy with { Approvals = [legacy.Approvals[0] with { Status = pendingState }, legacy.Approvals[1]] };

        Build(selected, legacy).IsReady.Should().BeTrue();
    }

    [Fact]
    public void Build_MatchesTrimmedCaseInsensitivePairsAndExactInstants_UsingFirstDuplicate()
    {
        var selected = CreateWorkflow();
        var task = selected.CloseChecklist[0];
        var approvalTask = selected.CloseChecklist[1];
        var decision = selected.Approvals[0];
        var sameInstant = Timestamp.ToOffset(TimeSpan.FromHours(-7));
        var active = selected with
        {
            CloseChecklist =
            [
                approvalTask with { TaskId = " APPROVAL-CONTROL " },
                task with { TaskId = " EVIDENCE-CONTROL ", AcknowledgedBy = " CHECKLIST-OPERATOR ", AcknowledgedAtUtc = sameInstant },
                task with { AcknowledgedAtUtc = Timestamp.AddMinutes(5) }
            ],
            Approvals = [decision with { Operator = " SUBMITTER ", Reviewer = " REVIEWER ", SubmittedAtUtc = sameInstant, DecidedAtUtc = sameInstant }]
        };

        Build(selected, active).IsReady.Should().BeTrue("publication retains the first task/actor pair and the shared guard compares instants and case-insensitive identities");
    }

    [Fact]
    public void Build_IgnoresUnforwardedChecklistAndHistoricalApprovalMetadata()
    {
        var selected = CreateWorkflow();
        var task = selected.CloseChecklist[0];
        var decision = selected.Approvals[0];
        var active = selected with
        {
            CloseChecklist =
            [
                task with { Label = "Relabeled", Owner = "New display owner", EvidencePointer = "another-retained-pointer" },
                selected.CloseChecklist[1] with { AcknowledgedBy = "not-forwarded", AcknowledgedAtUtc = Timestamp.AddTicks(1) },
                task with { TaskId = "blocked", Status = "Blocked", AcknowledgedBy = "excluded" },
                task with { TaskId = "expired", Status = "Expired", AcknowledgedBy = "excluded" },
                task with { TaskId = "reason", BlockingReason = "Needs repair", AcknowledgedBy = "excluded" },
                task with { TaskId = "pointer", EvidencePointer = null, AcknowledgedBy = "excluded" },
                task with { TaskId = "timestamp", AcknowledgedAtUtc = null, AcknowledgedBy = "excluded" }
            ],
            Approvals =
            [
                decision with { ApprovalId = "old", Operator = "historical-submitter", SubmittedAtUtc = Timestamp.AddDays(-1) },
                decision with { ApprovalId = "renamed", Rationale = "Reworded" }
            ]
        };

        Build(selected, active).IsReady.Should().BeTrue("only the current forwarded approval payload identifies publication evidence");
    }

    [Fact]
    public void Build_LegacySubmissionUsesOnlyTheImmediatelyPreviousPendingRecord()
    {
        var selected = CreateLegacyWorkflow();
        var active = selected with
        {
            Approvals = [selected.Approvals[0], selected.Approvals[0] with { Status = OperationsApprovalStateDto.Rejected }, selected.Approvals[1]]
        };

        AssertMismatchAndRepair(selected, active);
    }

    [Fact]
    public void Build_UsesTheDefaultApprovalTaskIdentityWhenNoApprovalChecklistTaskExists()
    {
        var selected = CreateWorkflow();
        selected = selected with { CloseChecklist = [selected.CloseChecklist[0]] };
        var active = selected with { CloseChecklist = [selected.CloseChecklist[0], ChecklistTask("close-gate-approval", OperationsGateKeyDto.Approval)] };

        Build(selected, active).IsReady.Should().BeTrue();
    }

    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-10-09T12:00:00.0000001Z");
    private static readonly CloseReadinessScopeDto Scope = new("fund-alpha", Guid.Parse("27a9a9bb-145b-4f57-8fd1-c71ff99dc2c2"),
        Guid.Parse("1eac8907-6d9a-4dcf-8653-52736dc2ac4b"), "entity-alpha", "2026-09");

    private static OperationsContinuityWorkflowDto CreateWorkflow()
        => new(Guid.Parse("1583c1b1-8401-460b-9ce5-92983706c3ae"), Scope.FundAccountId!.Value, Scope.PeriodId!, null, "broker",
            Timestamp, Timestamp, 5, OperationsWorkflowStatusDto.ReadyForClose, OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete, OperationsLedgerPostingStateDto.Posted, OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.Approved, [], [], [], null,
            [new("approval-current", OperationsApprovalStateDto.Approved, "submitter", "reviewer", "Approved", Timestamp, Timestamp, [])],
            new(true, "report-current", null, []),
            [ChecklistTask("evidence-control", OperationsGateKeyDto.Reconciliation), ChecklistTask("approval-control", OperationsGateKeyDto.Approval)],
            [], [], [], LedgerBookId: Scope.LedgerBookId);

    private static OperationsContinuityWorkflowDto CreateLegacyWorkflow()
    {
        var workflow = CreateWorkflow();
        var decision = workflow.Approvals[0];
        return workflow with
        {
            Approvals =
            [
                decision with { ApprovalId = "submission", Status = OperationsApprovalStateDto.Submitted, Reviewer = null, DecidedAtUtc = null },
                decision with { Operator = null, SubmittedAtUtc = null }
            ]
        };
    }

    private static OperationsCloseChecklistTaskDto ChecklistTask(string taskId, OperationsGateKeyDto gate)
        => new(taskId, gate, "Retained control", "Operations", "Retained evidence", 1, null, null,
            "Completed", null, "evidence-pointer", null, false, Timestamp, "checklist-operator");

    private static OperationsContinuityWorkflowDto ReplaceTask(OperationsContinuityWorkflowDto workflow, OperationsCloseChecklistTaskDto task)
        => workflow with { CloseChecklist = [task, workflow.CloseChecklist[1]] };

    private static OperationsContinuityClosePresentation Build(OperationsContinuityWorkflowDto selected, OperationsContinuityWorkflowDto active)
        => OperationsContinuityClosePresentation.Build(selected,
            new(Timestamp, Scope.FundProfileId, Scope.LedgerBookId, Scope.FundAccountId, Scope.PeriodId,
                "Ready", true, "Shared assessment", 0, 0, 0, [], [], ActiveWorkflow: active,
                CloseReadiness: new(Scope, Timestamp, "Ready", true, true, [], [])), Scope);

    private static void AssertMismatchAndRepair(OperationsContinuityWorkflowDto selected, OperationsContinuityWorkflowDto active)
    {
        var blocked = Build(selected, active);
        blocked.IsReady.Should().BeFalse("same workflow identity/version and report links cannot authorize different forwarded approvals");
        blocked.Detail.Should().Contain("different report-pack or publication evidence");
        blocked.Blockers.Should().BeEmpty();
        Build(active, active).IsReady.Should().BeTrue("refreshing the selected detail to the shared snapshot repairs the mismatch");
    }
}
