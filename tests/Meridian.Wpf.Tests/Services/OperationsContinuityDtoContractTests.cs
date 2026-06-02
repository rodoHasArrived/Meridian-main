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
            CloseChecklist: [],
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
            ],
            CloseReadiness: new OperationsCloseReadinessDto(
                IsReadyToClose: false,
                Severity: "Critical",
                Score: 70,
                Components:
                [
                    new OperationsCloseReadinessComponentDto(
                        Key: "reconciliation",
                        Label: "Reconciliation",
                        Score: 0,
                        Weight: 15,
                        IsReady: false,
                        Severity: "Critical",
                        BlockingReason: "Unresolved reconciliation breaks still require disposition.",
                        Gate: OperationsGateKeyDto.Reconciliation,
                        RouteHint: "/workstation/accounting")
                ],
                Blockers:
                [
                    new OperationsCloseReadinessBlockerDto(
                        Code: "RECONCILIATION_BREAKS_OPEN",
                        Category: "Reconciliation",
                        Severity: "Critical",
                        Message: "Unresolved reconciliation breaks still require disposition.",
                        Gate: OperationsGateKeyDto.Reconciliation,
                        RouteHint: "/workstation/accounting")
                ],
                NextActions:
                [
                    new OperationsNextActionDto(
                        Code: "RECONCILIATION_BREAKS_OPEN",
                        Label: "Unresolved reconciliation breaks still require disposition.",
                        Route: "/workstation/accounting",
                        Gate: OperationsGateKeyDto.Reconciliation)
                ]),
            AccountingRecordSummary: new OperationsAccountingRecordSummaryDto(
                RecordId: "accounting-record-2026-05",
                IsAuditReady: false,
                CompleteCategoryCount: 4,
                RequiredCategoryCount: 6,
                Summary: "Accounting record has 4 of 6 required evidence categories complete.",
                EvidenceCategories:
                [
                    new OperationsAccountingRecordEvidenceCategoryDto(
                        Key: "reconciliation-case-history",
                        Label: "Reconciliation case history",
                        IsComplete: false,
                        Status: "Open critical break must be resolved.",
                        RouteHint: "/workstation/accounting",
                        EvidenceLinks: [],
                        RequiredEvidence: ["reconciliation run", "break-case decision history", "resolved exception evidence"])
                ],
                EvidenceLinks: []));

        var json = JsonSerializer.Serialize(dto, options);
        var actual = JsonSerializer.Deserialize<OperationsContinuityWorkflowDto>(json, options);

        actual.Should().NotBeNull();
        actual!.Status.Should().Be(OperationsWorkflowStatusDto.ReconciliationActive);
        actual.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "ev-close-1");
        actual.ReportPackReadiness.IsReady.Should().BeFalse();
        actual.ReportPackReadiness.ReportPackId.Should().Be("report-pack-2026-05");
        actual.CloseReadiness.Should().NotBeNull();
        actual.CloseReadiness!.Score.Should().Be(70);
        actual.CloseReadiness.Components.Should().ContainSingle(component => component.Key == "reconciliation");
        actual.AccountingRecordSummary.Should().NotBeNull();
        actual.AccountingRecordSummary!.RequiredCategoryCount.Should().Be(6);
        actual.AccountingRecordSummary.EvidenceCategories.Should().ContainSingle(category => category.Key == "reconciliation-case-history");
        actual.AccountingRecordSummary.EvidenceCategories.Single().RequiredEvidence.Should().Contain("break-case decision history");
        actual.Blockers.Should().ContainSingle(blocker => blocker.EvidenceLinks.Any(link => link.EvidenceId == "ev-pack-1"));
        actual.NextActions.Should().ContainSingle(action => action.Code == "prepare-report-pack");
    }

    [Fact]
    public void EvidenceVaultLookupContracts_ShouldRoundTripAccountingRecordLinkage()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var linkage = new EvidenceSubjectLinkageDto(
            EvidenceSubject: "accounting-record/workflow-2026-05",
            RunId: null,
            PeriodId: "2026-05",
            ReportPackId: "report-pack-2026-05",
            ReconciliationCaseId: "case-77",
            AccountingRecordId: "workflow-2026-05");
        var lookup = new EvidenceVaultLookupRequestDto(
            EvidenceSubject: null,
            RunId: null,
            PeriodId: null,
            ReportPackId: null,
            ReconciliationCaseId: null,
            AccountingRecordId: "workflow-2026-05");

        var linkageJson = JsonSerializer.Serialize(linkage, options);
        var lookupJson = JsonSerializer.Serialize(lookup, options);
        var actualLinkage = JsonSerializer.Deserialize<EvidenceSubjectLinkageDto>(linkageJson, options);
        var actualLookup = JsonSerializer.Deserialize<EvidenceVaultLookupRequestDto>(lookupJson, options);

        linkageJson.Should().Contain("\"accountingRecordId\":\"workflow-2026-05\"");
        lookupJson.Should().Contain("\"accountingRecordId\":\"workflow-2026-05\"");
        actualLinkage.Should().NotBeNull();
        actualLinkage!.AccountingRecordId.Should().Be("workflow-2026-05");
        actualLinkage.EvidenceSubject.Should().Be("accounting-record/workflow-2026-05");
        actualLookup.Should().NotBeNull();
        actualLookup!.AccountingRecordId.Should().Be("workflow-2026-05");
    }
}
