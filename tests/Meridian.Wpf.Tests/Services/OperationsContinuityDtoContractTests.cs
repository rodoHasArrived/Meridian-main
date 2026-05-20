using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Tests.Services;

public sealed class OperationsContinuityDtoContractTests
{
    [Fact]
    public void OperationsContinuityWorkflowDto_ShouldRoundTripSharedContractWithoutClientSideStatusDerivation()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var dto = new OperationsContinuityWorkflowDto(
            WorkflowId: Guid.NewGuid(),
            FundAccountId: Guid.NewGuid(),
            PeriodId: "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "custodian",
            CreatedAtUtc: new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
            Version: 7,
            Status: OperationsWorkflowStatusDto.ReconciliationActive,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Complete,
            ReconciliationState: OperationsReconciliationStateDto.InReview,
            ApprovalState: OperationsApprovalStateDto.Pending,
            Gates:
            [
                new OperationsGateDto(
                    GateKey: OperationsGateKeyDto.Reconciliation,
                    DisplayName: "Reconciliation",
                    Status: OperationsGateStatusDto.ReviewRequired,
                    IsRequired: true,
                    Description: "Breaks require operator review.",
                    Blockers:
                    [
                        new OperationsWorkflowBlockerDto(
                            Code: "reconciliation-break",
                            Message: "Open critical break must be resolved.",
                            Gate: OperationsGateKeyDto.Reconciliation,
                            Severity: "critical",
                            EvidenceLinks:
                            [
                                new OperationsEvidenceLinkDto("ev-break-1", "Break packet", "/evidence/break-1", "reconciliation", DateTimeOffset.UtcNow)
                            ])
                    ],
                    NextActions:
                    [
                        new OperationsNextActionDto("open-break", "Open Break Review", "/wpf/fund-reconciliation", OperationsGateKeyDto.Reconciliation)
                    ],
                    CompletedAtUtc: null,
                    CompletedBy: null)
            ],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                IsReady: false,
                ReportPackId: "report-pack-2026-05",
                BlockingReason: "Awaiting reconciliation sign-off.",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto("ev-pack-1", "Draft report pack", "/evidence/report-pack", "reporting", DateTimeOffset.UtcNow)
                ]),
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto("ev-close-1", "Close checklist", "/evidence/close-checklist", "ops-runbook", DateTimeOffset.UtcNow)
            ],
            Blockers:
            [
                new OperationsWorkflowBlockerDto(
                    Code: "report-pack-blocked",
                    Message: "Report pack cannot be promoted.",
                    Gate: OperationsGateKeyDto.Approval,
                    Severity: "warning",
                    EvidenceLinks:
                    [
                        new OperationsEvidenceLinkDto("ev-pack-1", "Draft report pack", "/evidence/report-pack", "reporting", DateTimeOffset.UtcNow)
                    ])
            ],
            NextActions:
            [
                new OperationsNextActionDto(
                    Code: "prepare-report-pack",
                    Label: "Prepare Report Pack",
                    Route: "/wpf/fund-report-pack",
                    Gate: OperationsGateKeyDto.Approval)
            ]);

        var json = JsonSerializer.Serialize(dto, options);
        var actual = JsonSerializer.Deserialize<OperationsContinuityWorkflowDto>(json, options);

        actual.Should().NotBeNull();
        actual!.Status.Should().Be(OperationsWorkflowStatusDto.ReconciliationActive);
        actual.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "ev-close-1");
        actual.ReportPackReadiness.IsReady.Should().BeFalse();
        actual.ReportPackReadiness.ReportPackId.Should().Be("report-pack-2026-05");
        actual.Blockers.Should().ContainSingle(blocker => blocker.EvidenceLinks.Any(link => link.EvidenceId == "ev-pack-1"));
        actual.NextActions.Should().ContainSingle(action => action.Code == "prepare-report-pack");
    }
}
