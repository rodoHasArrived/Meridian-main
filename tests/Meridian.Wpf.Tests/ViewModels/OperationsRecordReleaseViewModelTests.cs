using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OperationsRecordReleaseViewModelTests
{
    [Fact]
    public void CombineTones_NeutralOutranksReady()
    {
        OperationsRecordReleaseMapper.CombineTones(
                [WorkstationReadinessTone.EvidenceLinked, WorkstationReadinessTone.Neutral])
            .Should().Be(
                WorkstationReadinessTone.Neutral,
                "an unknown input must keep a composed step out of ready");

        OperationsRecordReleaseMapper.CombineTones(
                [WorkstationReadinessTone.SignoffRequired, WorkstationReadinessTone.Blocked])
            .Should().Be(WorkstationReadinessTone.Blocked, "any blocked input blocks the composition");
    }

    [Fact]
    public void BuildReleaseSteps_MapsGatesAndBlocksOnBlockedGate()
    {
        var steps = OperationsRecordReleaseMapper.BuildReleaseSteps(CreateDetail());

        steps.Should().HaveCount(6);
        steps.Select(static step => step.StepId).Should().Equal(
            "source-data", "broker-intake", "ledger", "reconcile", "approve", "report");
        steps.Single(static step => step.StepId == "reconcile").ReadinessTone.Should().Be(
            WorkstationReadinessTone.Blocked, "the reconciliation gate is blocked");
        steps.Single(static step => step.StepId == "broker-intake").ReadinessTone.Should().Be(
            WorkstationReadinessTone.EvidenceLinked, "the broker-ingest gate passed");
        steps.Single(static step => step.StepId == "source-data").ReadinessTone.Should().Be(
            WorkstationReadinessTone.Neutral, "the desktop has no source-data seam wired yet");
    }

    [Fact]
    public void BuildReleaseSteps_MissingWorkflow_KeepsEveryStepOutOfReady()
    {
        var steps = OperationsRecordReleaseMapper.BuildReleaseSteps(null);

        steps.Should().OnlyContain(
            static step => step.ReadinessTone != WorkstationReadinessTone.EvidenceLinked,
            "no step may fabricate green without a loaded workflow");
        OperationsRecordReleaseMapper.BuildSummary(steps).StatusLabel.Should().NotBe("Release ready");
    }

    [Fact]
    public void BuildSummary_RollsUpBlockedThenReviewThenReady()
    {
        var blocked = OperationsRecordReleaseMapper.BuildSummary(OperationsRecordReleaseMapper.BuildReleaseSteps(CreateDetail()));
        blocked.StatusLabel.Should().Be("Release blocked", "a blocked step blocks the release");
        blocked.ReadinessTone.Should().Be(WorkstationReadinessTone.Blocked);

        var readySteps = OperationsRecordReleaseMapper
            .BuildReleaseSteps(CreateDetail() with
            {
                Gates = CreateDetail().Gates
                    .Select(static gate => gate with { Status = OperationsGateStatusDto.Passed, Blockers = [] })
                    .ToArray(),
                ClosePackage = CreateClosePackage()
            });
        var summary = OperationsRecordReleaseMapper.BuildSummary(readySteps);
        summary.StatusLabel.Should().Be(
            "Release review", "the unwired source-data step keeps the release in review even when every gate passed");
    }

    [Fact]
    public void AccountingSummaryTone_FailsClosedWithoutARecordId()
    {
        OperationsRecordReleaseMapper.AccountingSummaryTone(null).Should().Be(WorkstationReadinessTone.Blocked);

        var pending = CreateAccountingSummary() with { RecordId = "No accounting record yet" };
        OperationsRecordReleaseMapper.AccountingSummaryTone(pending).Should().Be(
            WorkstationReadinessTone.Blocked, "a placeholder record id is missing data, not a ready record");

        OperationsRecordReleaseMapper.AccountingSummaryTone(CreateAccountingSummary()).Should().Be(
            WorkstationReadinessTone.EvidenceLinked, "an audit-ready record with a real id is ready");
    }

    [Fact]
    public void BuildReleaseSteps_PublishedClosePackage_MarksReportStepReady()
    {
        var steps = OperationsRecordReleaseMapper.BuildReleaseSteps(CreateDetail() with
        {
            ClosePackage = CreateClosePackage()
        });

        var report = steps.Single(static step => step.StepId == "report");
        report.StatusText.Should().Be("Publication ready");
        report.ReadinessTone.Should().Be(WorkstationReadinessTone.EvidenceLinked);
    }

    private static OperationsContinuityWorkflowDto CreateDetail()
        => new(
            WorkflowId: Guid.Parse("7d3c2f10-6a5b-4c8d-9e1f-0a2b3c4d5e6f"),
            FundAccountId: Guid.Parse("0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b"),
            PeriodId: "2026-07",
            SecurityMasterSnapshotId: null,
            BrokerSource: "alpaca",
            CreatedAtUtc: DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            UpdatedAtUtc: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Version: 4,
            Status: OperationsWorkflowStatusDto.ReconciliationActive,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Posted,
            ReconciliationState: OperationsReconciliationStateDto.ExceptionsOpen,
            ApprovalState: OperationsApprovalStateDto.Pending,
            Gates:
            [
                new OperationsGateDto(
                    OperationsGateKeyDto.BrokerIngest,
                    "Broker ingest",
                    OperationsGateStatusDto.Passed,
                    IsRequired: true,
                    "Broker files imported.",
                    Blockers: [],
                    NextActions: [],
                    CompletedAtUtc: DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
                    CompletedBy: "ops"),
                new OperationsGateDto(
                    OperationsGateKeyDto.LedgerPosting,
                    "Ledger posting",
                    OperationsGateStatusDto.Passed,
                    IsRequired: true,
                    "Ledger posted.",
                    Blockers: [],
                    NextActions: [],
                    CompletedAtUtc: DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
                    CompletedBy: "ops"),
                new OperationsGateDto(
                    OperationsGateKeyDto.Reconciliation,
                    "Reconciliation",
                    OperationsGateStatusDto.Blocked,
                    IsRequired: true,
                    "Open breaks must be cleared.",
                    Blockers:
                    [
                        new OperationsWorkflowBlockerDto(
                            Code: "recon-open-breaks",
                            Message: "One reconciliation break is open.",
                            Gate: OperationsGateKeyDto.Reconciliation,
                            Severity: "Critical",
                            EvidenceLinks: [])
                    ],
                    NextActions: [],
                    CompletedAtUtc: null,
                    CompletedBy: null),
                new OperationsGateDto(
                    OperationsGateKeyDto.Approval,
                    "Approval",
                    OperationsGateStatusDto.ReviewRequired,
                    IsRequired: true,
                    "Approval pending.",
                    Blockers: [],
                    NextActions: [],
                    CompletedAtUtc: null,
                    CompletedBy: null)
            ],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                IsReady: false,
                ReportPackId: null,
                BlockingReason: "Report pack has not been generated.",
                EvidenceLinks: []),
            CloseChecklist: [],
            EvidenceLinks: [],
            Blockers: [],
            NextActions: [],
            AccountingRecordSummary: CreateAccountingSummary());

    private static OperationsAccountingRecordSummaryDto CreateAccountingSummary()
        => new(
            RecordId: "rec-2026-07",
            IsAuditReady: true,
            CompleteCategoryCount: 4,
            RequiredCategoryCount: 4,
            Summary: "Accounting record is audit ready.",
            EvidenceCategories: [],
            EvidenceLinks: []);

    private static OperationsClosePackagePublicationDto CreateClosePackage()
        => new(
            ClosePackageId: "close-2026-07",
            ReportPackId: "pack-9",
            RetainedManifestId: "manifest-1",
            RetainedManifestRoute: "/reporting/evidence?subjectKind=close-package&subjectId=close-2026-07",
            EvidenceHash: "abc123",
            PublishedAtUtc: DateTimeOffset.Parse("2026-08-05T06:30:00Z"),
            PublishedBy: "controller",
            SignOffRationale: "All gates passed.",
            EvidenceLinks: [],
            ChecklistControlApprovals: []);
}
