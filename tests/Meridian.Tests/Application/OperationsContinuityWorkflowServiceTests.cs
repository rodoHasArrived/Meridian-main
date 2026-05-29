using System.Text.Json;
using FluentAssertions;
using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application;

public sealed class OperationsContinuityWorkflowServiceTests
{
    [Fact]
    public void OperationsContinuityContractMatrix_ShouldContainAllRequiredStatusesAndCodes_AndBeSerializable()
    {
        OperationsWorkflowContractMatrix.OverallStatuses.Should().BeEquivalentTo(Enum.GetValues<OperationsWorkflowStatusDto>());
        OperationsWorkflowContractMatrix.GateStatuses.Should().BeEquivalentTo(Enum.GetValues<OperationsGateStatusDto>());
        OperationsWorkflowContractMatrix.BrokerSubStates.Should().BeEquivalentTo(Enum.GetValues<OperationsBrokerIntakeStateDto>());
        OperationsWorkflowContractMatrix.SecurityMasterSubStates.Should().BeEquivalentTo(Enum.GetValues<OperationsSecurityMasterStateDto>());
        OperationsWorkflowContractMatrix.LedgerSubStates.Should().BeEquivalentTo(Enum.GetValues<OperationsLedgerPostingStateDto>());
        OperationsWorkflowContractMatrix.ReconciliationSubStates.Should().BeEquivalentTo(Enum.GetValues<OperationsReconciliationStateDto>());
        OperationsWorkflowContractMatrix.ApprovalSubStates.Should().BeEquivalentTo(Enum.GetValues<OperationsApprovalStateDto>());

        OperationsWorkflowContractMatrix.BlockerCodes.Should().OnlyContain(code => !code.StartsWith("UI_", StringComparison.Ordinal));
        OperationsWorkflowContractMatrix.IssueCodes.Should().OnlyContain(code => !code.StartsWith("UI_", StringComparison.Ordinal));
        OperationsWorkflowContractMatrix.IssueCodes.Should().OnlyContain(code => OperationsWorkflowContractMatrix.BlockerCodes.Contains(code));
        OperationsWorkflowContractMatrix.AuditEventTypes.Should().OnlyContain(eventType => !eventType.StartsWith("UI_", StringComparison.Ordinal));
        OperationsWorkflowContractMatrix.AuditEventTypes.Should().Contain([
            "workflow-started",
            "security-master-override-approved",
            "ledger-posted",
            "ledger-posting-blocked",
            "workflow-reopened"
        ]);
        OperationsWorkflowContractMatrix.BlockerCodes.Should().Contain([
            "BROKER_STATEMENT_MISSING",
            "BROKER_TRANSACTION_TYPE_UNKNOWN",
            "SM_FACTOR_SCHEDULE_MISSING",
            "SM_VALUATION_SOURCE_MISSING",
            "LEDGER_JOURNAL_AGGREGATE_ID_MISMATCH",
            "LEDGER_JOURNAL_PERIOD_ID_MISMATCH",
            "LEDGER_SECURITY_MASTER_ACCOUNTING_RULE_MISSING",
            "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED",
            "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISMATCH",
            "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISMATCH",
            "RECONCILIATION_EXTERNAL_EVIDENCE_MISSING_LEDGER_POSTING",
            "APPROVAL_METADATA_REQUIRED",
            "REPORT_PACK_ID_MISMATCH"
        ]);
        OperationsWorkflowContractMatrix.IssueCodes.Should().Contain([
            "ACCRUAL_DAY_COUNT_MISSING",
            "ACCRUAL_LEDGER_POSTING_MISSING",
            "ACCRUAL_EXTERNAL_EVIDENCE_MISSING",
            "FACTOR_STALE",
            "FACTOR_PAYDOWN_LEDGER_MISSING",
            "SECURITY_SCHEDULE_MISSING"
        ]);

        var sampleBlocker = new OperationsWorkflowBlockerDto(
            OperationsWorkflowContractMatrix.BlockerCodes.First(),
            "contract-check",
            OperationsGateKeyDto.BrokerIngest,
            "Error",
            []);
        var sampleResult = new OperationsTransitionResultDto(
            true,
            null,
            null,
            null,
            [sampleBlocker],
            []);

        var workflowStatusJson = JsonSerializer.Serialize(OperationsWorkflowStatusDto.ReadyForClose);
        var gateStatusJson = JsonSerializer.Serialize(OperationsGateStatusDto.ReviewRequired);
        var blockerJson = JsonSerializer.Serialize(sampleBlocker);
        var transitionJson = JsonSerializer.Serialize(sampleResult);

        workflowStatusJson.Should().Contain("ReadyForClose");
        gateStatusJson.Should().Contain("ReviewRequired");
        blockerJson.Should().Contain(sampleBlocker.Code);
        transitionJson.Should().Contain("Blockers");
        JsonSerializer.Deserialize<OperationsTransitionResultDto>(transitionJson).Should().NotBeNull();
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldCreateInitialGatesAndAuditEvent()
    {
        var service = CreateService(out _, out var auditStore);
        var fundAccountId = Guid.NewGuid();

        var result = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        result.Success.Should().BeTrue();
        result.Workflow.Should().NotBeNull();
        result.Workflow!.FundAccountId.Should().Be(fundAccountId);
        result.Workflow.PeriodId.Should().Be("2026-05");
        result.Workflow.Status.Should().Be(OperationsWorkflowStatusDto.CollectingBrokerData);
        result.Workflow.Version.Should().Be(1);
        result.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.BrokerIngest)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);
        result.Workflow.Gates.Where(gate => gate.GateKey != OperationsGateKeyDto.BrokerIngest)
            .Should().OnlyContain(gate => gate.Status == OperationsGateStatusDto.NotStarted);

        var timeline = await auditStore.GetTimelineAsync(result.Workflow.WorkflowId);
        timeline.Should().ContainSingle();
        timeline[0].EventType.Should().Be("workflow-started");
        timeline[0].PreviousHash.Should().BeNull();
        timeline[0].CurrentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldRejectDuplicateOpenWorkflowForSameFundAndPeriod()
    {
        var service = CreateService(out _, out _);
        var fundAccountId = Guid.NewGuid();

        var first = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        var second = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Retry duplicate monthly operations close workflow"));

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.ErrorCode.Should().Be("WORKFLOW_ALREADY_EXISTS");
        second.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "OPERATIONS_CONTINUITY_WORKFLOW_ALREADY_EXISTS" &&
            blocker.Gate == null);
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldNotPersistWorkflowWhenAuditAppendFails()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new ThrowingAuditStore("workflow-started");
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);

        var act = () => service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated audit append failure.");

        var workflows = await repository.ListAsync();
        workflows.Should().BeEmpty();
    }

    [Fact]
    public void Derive_ShouldReturnBlockedWhenAnyGateIsBlocked()
    {
        var derivation = new OperationsStatusDerivationService();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            DateTimeOffset.UtcNow);
        workflow.ReplaceGate(workflow.SecurityMasterGate.WithStatus(
            OperationsGateStatusDto.Blocked,
            blockers:
            [
                new OperationsWorkflowBlockerDto(
                    "SM_ACCOUNTING_CLASSIFICATION_MISSING",
                    "Security Master accounting classification is missing.",
                    OperationsGateKeyDto.SecurityMaster,
                    "Critical",
                    [])
            ]));

        derivation.Derive(workflow).Should().Be(OperationsWorkflowStatusDto.Blocked);
    }

    [Fact]
    public void Derive_ShouldReturnReadyForCloseWhenAllGatesPassedAndApprovalApproved()
    {
        var derivation = new OperationsStatusDerivationService();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            DateTimeOffset.UtcNow);
        foreach (var gate in workflow.Gates)
        {
            workflow.ReplaceGate(gate.WithStatus(OperationsGateStatusDto.Passed));
        }

        workflow.SetApprovalState(OperationsApprovalStateDto.Approved, DateTimeOffset.UtcNow);

        derivation.Derive(workflow).Should().Be(OperationsWorkflowStatusDto.ReadyForClose);
    }


    [Fact]
    public void Derive_ShouldUseDeterministicHighestActiveStageOrdering()
    {
        var derivation = new OperationsStatusDerivationService();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            DateTimeOffset.UtcNow);

        workflow.ReplaceGate(workflow.BrokerIngestGate.WithStatus(OperationsGateStatusDto.InProgress));
        workflow.ReplaceGate(workflow.SecurityMasterGate.WithStatus(OperationsGateStatusDto.InProgress));
        workflow.ReplaceGate(workflow.LedgerPostingGate.WithStatus(OperationsGateStatusDto.InProgress));

        derivation.Derive(workflow).Should().Be(OperationsWorkflowStatusDto.LedgerPostingDraft);
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldRejectVersionMismatchWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var result = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                ExpectedVersion: 99,
                Actor: "ops-user",
                Rationale: "Import statement batch"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VERSION_MISMATCH");
        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldAppendAuditWithPreviousHash()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                start.Workflow.Version,
                "ops-user",
                "Imported custodian statement",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto(
                        "statement-2026-05",
                        "May custodian statement",
                        "/workstation/accounting",
                        "custodian-upload",
                        DateTimeOffset.UtcNow)
                ]));

        import.Success.Should().BeTrue();
        import.Workflow!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Imported);
        import.Workflow.Version.Should().Be(2);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().HaveCount(2);
        timeline[1].PreviousHash.Should().Be(timeline[0].CurrentHash);
        timeline[1].References.Should().ContainSingle(link => link.EvidenceId == "statement-2026-05");
    }

    [Fact]
    public async Task StartWorkflowAsync_ShouldPreserveEvidenceIdentityWhileRedactingSensitiveAuditFields()
    {
        var service = CreateService(out _, out var auditStore);

        var result = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user",
            Rationale: "Open close lane with api_key=do-not-store and Bearer raw-token-value",
            CorrelationId: "token=raw-correlation-token",
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    "statement-2026-05",
                    "Uploaded with password:raw-label-secret",
                    "https://operator:raw-route-secret@example.invalid/statement?api_key=raw-query-secret",
                    "credential=raw-source-secret",
                    DateTimeOffset.UtcNow)
            ]));

        result.Success.Should().BeTrue();

        var timeline = await auditStore.GetTimelineAsync(result.Workflow!.WorkflowId);
        var audit = timeline.Single();
        audit.Rationale.Should().Contain("api_key=[redacted]");
        audit.Rationale.Should().Contain("Bearer [redacted]");
        audit.Rationale.Should().NotContain("do-not-store");
        audit.Rationale.Should().NotContain("raw-token-value");
        audit.CorrelationId.Should().Be("token=[redacted]");
        audit.References.Should().ContainSingle();
        audit.References[0].EvidenceId.Should().Be("statement-2026-05");
        audit.References[0].Label.Should().Be("Uploaded with password:[redacted]");
        audit.References[0].Route.Should().Be("https://[redacted]@example.invalid/statement?api_key=[redacted]");
        audit.References[0].Source.Should().Be("credential=[redacted]");

        result.Workflow.EvidenceLinks.Should().ContainSingle(link =>
            link.EvidenceId == "statement-2026-05" &&
            link.Route == "https://[redacted]@example.invalid/statement?api_key=[redacted]");
    }

    [Fact]
    public async Task WorkflowCommands_ShouldAdvanceThroughApprovalAndClose()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await service.ImportBrokerDataAsync(workflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(workflowId, new OperationsTransitionRequestDto(
            import.Workflow!.Version,
            "ops-user",
            "Normalized imported broker activity"));
        var security = await service.ResolveSecurityMasterMappingsAsync(workflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            "Resolved all instruments"));
        var draft = await service.BuildLedgerDraftAsync(workflowId, new OperationsLedgerDraftRequestDto(
            security.Workflow!.Version,
            "ops-user",
            PreviewId: "ledger-preview-1",
            IsBalanced: true,
            Rationale: "Built Security Master-derived journal preview"));
        var validated = await service.ValidateLedgerDraftAsync(workflowId, new OperationsLedgerValidationRequestDto(
            draft.Workflow!.Version,
            "ops-user",
            IsBalanced: true,
            PeriodOpen: true,
            Rationale: "Validated balanced draft"));
        var posted = await service.PostLedgerEntriesAsync(workflowId, new OperationsLedgerPostRequestDto(
            validated.Workflow!.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            Rationale: "Posted validated accounting journals",
            JournalCandidate: CreateJournalCandidate(validated.Workflow!.FundAccountId)));
        var reconciled = await service.RunReconciliationAsync(workflowId, new OperationsReconciliationRunRequestDto(
            posted.Workflow!.Version,
            "ops-user",
            "Ran expected-vs-actual reconciliation",
            BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1",
            Rationale: "Report pack is ready"));
        var submitted = await service.SubmitForApprovalAsync(workflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Submit clean workflow",
            ReportPackId: "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var approved = await service.ApproveWorkflowAsync(workflowId, new OperationsApprovalDecisionRequestDto(
            submitted.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Approved close evidence",
            ReportPackId: "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var closed = await service.CloseWorkflowAsync(workflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            Rationale: "Close accounting period",
            ReportPackId: "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        closed.Success.Should().BeTrue();
        closed.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Closed);
        closed.Workflow.Gates.Should().OnlyContain(gate => gate.Status == OperationsGateStatusDto.Passed);
        closed.Workflow.ReportPackReadiness.IsReady.Should().BeTrue();
        closed.Workflow.Approvals.Should().Contain(approval => approval.Status == OperationsApprovalStateDto.Approved);
        closed.Workflow.CloseReadiness.Should().NotBeNull();
        closed.Workflow.CloseReadiness!.IsReadyToClose.Should().BeTrue();
        closed.Workflow.CloseReadiness.Score.Should().Be(100);
        closed.Workflow.CloseReadiness.Components.Should().HaveCount(9);
        closed.Workflow.CloseReadiness.Components.Select(component => component.Key).Should().BeEquivalentTo(
        [
            "security-master",
            "provider-freshness",
            "positions",
            "cash",
            "ledger",
            "pricing",
            "reconciliation",
            "reports",
            "approvals"
        ], "the W4 close score must stay grounded in the shared evidence domains");
        closed.Workflow.CloseReadiness.Components.Should().OnlyContain(component => component.IsReady);
        closed.Workflow.ClosePackage.Should().NotBeNull();
        closed.Workflow.ClosePackage!.ReportPackId.Should().Be("report-pack-1");
        closed.Workflow.ClosePackage.ClosePackageId.Should().StartWith("close-package-");
        closed.Workflow.ClosePackage.RetainedManifestId.Should().EndWith("-manifest");
        closed.Workflow.ClosePackage.RetainedManifestRoute.Should().Contain($"/operations-continuity/{workflowId:D}/close-package/");
        closed.Workflow.ClosePackage.EvidenceHash.Should().MatchRegex("^[a-f0-9]{64}$");
        closed.Workflow.ClosePackage.PublishedBy.Should().Be("ops-user");
        closed.Workflow.ClosePackage.SignOffRationale.Should().Be("Close accounting period");
        closed.Workflow.ClosePackage.EvidenceLinks.Should().Contain(link => link.EvidenceId == "report-pack-1");
        closed.Workflow.ClosePackage.ChecklistControlApprovals.Should().HaveCount(6);

        var timeline = await auditStore.GetTimelineAsync(workflowId);
        timeline.Select(entry => entry.EventType).Should().ContainInOrder(
            "workflow-started",
            "broker-imported",
            "broker-transactions-normalized",
            "security-master-resolved",
            "ledger-draft-built",
            "ledger-draft-validated",
            "ledger-posted",
            "reconciliation-run",
            "gate-posture-refreshed",
            "approval-submitted",
            "approval-approved",
            "workflow-closed");
    }

    [Fact]
    public async Task RefreshGatePostureAsync_ProviderSyncStale_ShouldReduceCloseReadiness()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "custodian",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        var posture = await service.RefreshGatePostureAsync(start.Workflow!.WorkflowId, new OperationsGatePostureRequestDto(
            start.Workflow.Version,
            "ops-user",
            Rationale: "Provider sync failed freshness policy.",
            ProviderAccountLinked: true,
            ProviderSyncStale: true));

        posture.Success.Should().BeTrue();
        posture.Workflow.Should().NotBeNull();
        posture.Workflow!.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.BrokerIngest)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
        posture.Workflow.CloseReadiness.Should().NotBeNull();
        posture.Workflow.CloseReadiness!.Components.Should().Contain(component =>
            component.Key == "provider-freshness" &&
            component.Weight == 10 &&
            component.IsReady == false &&
            component.BlockingReason!.Contains("Provider sync is stale", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.CloseReadiness.Blockers.Should().Contain(blocker =>
            blocker.Code == "BROKER_SYNC_STALE" &&
            blocker.Message.Contains("Provider sync is stale", StringComparison.OrdinalIgnoreCase) &&
            blocker.Gate == OperationsGateKeyDto.BrokerIngest);
        posture.Workflow.CloseReadiness.Score.Should().BeLessThan(100);
    }

    [Fact]
    public async Task RefreshGatePostureAsync_RequiredProviderCapabilityGap_ShouldBlockBrokerIngest()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "custodian",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        var posture = await service.RefreshGatePostureAsync(start.Workflow!.WorkflowId, new OperationsGatePostureRequestDto(
            start.Workflow.Version,
            "ops-user",
            Rationale: "Provider routing lacks required accounting feeds.",
            ProviderAccountLinked: true,
            ProviderSyncStale: false,
            ProviderRequiredCapabilityGaps:
            [
                "AccountPositions",
                "ReconciliationFeed",
                "AccountPositions"
            ]));

        posture.Success.Should().BeTrue();
        posture.Workflow.Should().NotBeNull();
        var brokerGate = posture.Workflow!.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.BrokerIngest);
        brokerGate.Status.Should().Be(OperationsGateStatusDto.Blocked);
        brokerGate.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "BROKER_PROVIDER_REQUIRED_CAPABILITY_UNROUTABLE" &&
            blocker.Message.Contains("AccountPositions", StringComparison.OrdinalIgnoreCase) &&
            blocker.Message.Contains("ReconciliationFeed", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.CloseReadiness.Should().NotBeNull();
        posture.Workflow.CloseReadiness!.Blockers.Should().Contain(blocker =>
            blocker.Code == "BROKER_PROVIDER_REQUIRED_CAPABILITY_UNROUTABLE" &&
            blocker.Gate == OperationsGateKeyDto.BrokerIngest);
        posture.Workflow.CloseReadiness.Components.Should().Contain(component =>
            component.Key == "provider-freshness" &&
            component.IsReady == false &&
            component.BlockingReason!.Contains("AccountPositions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RefreshGatePostureAsync_DegradedProviderCapabilityGap_ShouldRequireBrokerReview()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "custodian",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        var posture = await service.RefreshGatePostureAsync(start.Workflow!.WorkflowId, new OperationsGatePostureRequestDto(
            start.Workflow.Version,
            "ops-user",
            Rationale: "Provider routing lacks optional but close-relevant feeds.",
            ProviderAccountLinked: true,
            ProviderSyncStale: false,
            ProviderDegradedCapabilityGaps:
            [
                "HistoricalQuotes:Equity",
                "CorporateActions"
            ]));

        posture.Success.Should().BeTrue();
        posture.Workflow.Should().NotBeNull();
        var brokerGate = posture.Workflow!.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.BrokerIngest);
        brokerGate.Status.Should().Be(OperationsGateStatusDto.ReviewRequired);
        brokerGate.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "BROKER_PROVIDER_CAPABILITY_DEGRADED" &&
            blocker.Severity == "Warning" &&
            blocker.Message.Contains("HistoricalQuotes:Equity", StringComparison.OrdinalIgnoreCase) &&
            blocker.Message.Contains("CorporateActions", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.CloseReadiness.Should().NotBeNull();
        posture.Workflow.CloseReadiness!.Components.Should().Contain(component =>
            component.Key == "provider-freshness" &&
            component.IsReady == false &&
            component.Severity == "Warning");
        posture.Workflow.CloseReadiness.Blockers.Should().Contain(blocker =>
            blocker.Code == "BROKER_PROVIDER_CAPABILITY_DEGRADED" &&
            blocker.Severity == "Warning");
        posture.Workflow.CloseReadiness.Score.Should().BeLessThan(100);
    }

    [Fact]
    public async Task CloseReadiness_ShouldOnlyEmitSharedContractBlockerCodes()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "custodian",
            Actor: "ops-user",
            Rationale: "Open monthly operations close workflow"));

        start.Workflow!.CloseReadiness.Should().NotBeNull();
        start.Workflow.CloseReadiness!.Blockers.Select(blocker => blocker.Code)
            .Should().OnlyContain(code => OperationsWorkflowContractMatrix.BlockerCodes.Contains(code),
                "browser and WPF close-readiness consumers depend on shared blocker-code routing");
        start.Workflow.CloseReadiness.Components.Select(component => component.Key).Should().BeEquivalentTo(
        [
            "security-master",
            "provider-freshness",
            "positions",
            "cash",
            "ledger",
            "pricing",
            "reconciliation",
            "reports",
            "approvals"
        ]);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRejectMissingSubmissionMetadataWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran expected-vs-actual reconciliation",
            BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1",
            Rationale: "Report pack is ready"));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "",
            Rationale: "",
            ReportPackId: ""));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "APPROVAL_SUBMISSION_METADATA_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRejectMismatchedReadyReportPackWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran expected-vs-actual reconciliation",
            BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-ready",
            Rationale: "Report pack is ready"));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            "reviewer",
            "Submit approval against a different report pack",
            "report-pack-different"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "REPORT_PACK_ID_MISMATCH" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRejectBeforePrerequisiteGatesPassWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var posture = await service.RefreshGatePostureAsync(start.Workflow!.WorkflowId, new OperationsGatePostureRequestDto(
            start.Workflow.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-early",
            Rationale: "Report pack was uploaded before gates completed"));
        var timelineBefore = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);

        var result = await service.SubmitForApprovalAsync(start.Workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            "reviewer",
            "Submit early approval",
            "report-pack-early"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED" &&
            blocker.Gate == null);
        var timelineAfter = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task BuildLedgerDraftAsync_ShouldBlockBeforeSecurityMasterResolution()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));

        var result = await service.BuildLedgerDraftAsync(start.Workflow!.WorkflowId, new OperationsLedgerDraftRequestDto(
            start.Workflow.Version,
            "ops-user",
            PreviewId: "ledger-preview-1",
            IsBalanced: true));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "SECURITY_MASTER_RESOLUTION_REQUIRED");
    }

    [Fact]
    public async Task NormalizeBrokerTransactionsAsync_ShouldAdvanceBrokerGateTowardSecurityMasterResolution()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));

        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));

        normalized.Success.Should().BeTrue();
        normalized.Workflow!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Normalized);
        normalized.Workflow.SecurityMasterState.Should().Be(OperationsSecurityMasterStateDto.Pending);
        normalized.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("broker-transactions-normalized");
    }

    [Fact]
    public async Task ResolveSecurityMasterMappingsAsync_ShouldRejectBeforeBrokerNormalizationWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var timelineBefore = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);

        var result = await service.ResolveSecurityMasterMappingsAsync(
            start.Workflow.WorkflowId,
            new OperationsSecurityMasterResolveRequestDto(
                import.Workflow!.Version,
                "ops-user",
                "Attempted resolution before normalization"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "BROKER_NORMALIZATION_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.BrokerIngest);
        var timelineAfter = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ApproveSecurityMasterOverrideAsync_ShouldPermitLedgerDraftWithGovernedMetadata()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            OverrideRequestCount: 1));
        security.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ApprovalPending);

        var approved = await service.ApproveSecurityMasterOverrideAsync(start.Workflow.WorkflowId, "override-1", new OperationsSecurityMasterOverrideApprovalRequestDto(
            security.Workflow.Version,
            "ops-user",
            "override-1",
            "Temporary mapping approved for month-end close",
            "policy-sm-override-v1",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))));
        var draft = await service.BuildLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerDraftRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "ledger-preview-override",
            true));

        approved.Success.Should().BeTrue();
        approved.Workflow!.SecurityMasterState.Should().Be(OperationsSecurityMasterStateDto.OverridesApproved);
        draft.Success.Should().BeTrue();

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("security-master-override-approved");
    }

    [Fact]
    public async Task ApproveSecurityMasterOverrideAsync_ShouldRejectMismatchedRouteAndBodyOverrideIds()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            OverrideRequestCount: 1));

        var result = await service.ApproveSecurityMasterOverrideAsync(
            start.Workflow.WorkflowId,
            "override-route",
            new OperationsSecurityMasterOverrideApprovalRequestDto(
                security.Workflow!.Version,
                "ops-user",
                "override-body",
                "Temporary mapping approved for month-end close",
                "policy-sm-override-v1",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "SM_OVERRIDE_ID_MISMATCH");
    }

    [Fact]
    public async Task ApproveSecurityMasterOverrideAsync_ShouldRejectExpiredApprovalMetadataWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user", "Normalized imported rows"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(
            normalized.Workflow!.Version,
            "ops-user",
            OverrideRequestCount: 1));
        var timelineBefore = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);

        var result = await service.ApproveSecurityMasterOverrideAsync(
            start.Workflow.WorkflowId,
            "override-1",
            new OperationsSecurityMasterOverrideApprovalRequestDto(
                security.Workflow!.Version,
                "ops-user",
                "override-1",
                "Temporary mapping approved for month-end close",
                "policy-sm-override-v1",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "SM_OVERRIDE_APPROVAL_EXPIRED" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
        var timelineAfter = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task RunReconciliationAsync_ShouldRequirePostedLedgerEntries()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: []));

        result.Success.Should().BeFalse();
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "LEDGER_POSTING_REQUIRED");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldAppendDurableJournalCandidateBeforeWorkflowPosting()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate(workflow.FundAccountId);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeTrue();
        journalStore.Appended.Should().ContainSingle();
        journalStore.Appended[0].PeriodId.Should().Be(candidate.PeriodId);
        journalStore.Appended[0].AggregateId.Should().Be(workflow.FundAccountId);
        journalStore.Appended[0].Entry.IsBalanced.Should().BeTrue();
        result.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldBlockAndAuditWhenJournalStoreIsMissing()
    {
        var service = CreateService(out _, out var auditStore, registerLedgerJournalStore: false);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId)));

        result.Success.Should().BeTrue();
        result.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        result.Workflow.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Validated);
        result.Workflow.Blockers.Should().ContainSingle(blocker => blocker.Code == "LEDGER_JOURNAL_STORE_UNAVAILABLE");

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posting-blocked");
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectJournalCandidateWhenAggregateOrPeriodMismatchWorkflow()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate(Guid.NewGuid(), Guid.NewGuid());

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_JOURNAL_AGGREGATE_ID_MISMATCH");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectJournalCandidateWithoutIdempotencyOrSecurityMasterProvenance()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate(workflow.FundAccountId) with
        {
            CommandId = null,
            IdempotencyKey = null,
            SecurityMasterProvenance = null,
            Metadata = new OperationsJournalEntryMetadataDto(ActivityType: "operations-continuity")
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-missing-provenance",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_IDEMPOTENCY_KEY_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_JOURNAL_PROVENANCE_MISSING");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectJournalCandidateWhoseProvenanceDoesNotReferenceMetadataSecurityId()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var mismatchedSecurityId = Guid.Parse("A0F5F7BD-B091-4C0F-9EE5-CB2700C10E43");
        var candidate = CreateInstrumentJournalCandidate(workflow.FundAccountId) with
        {
            SecurityMasterProvenance = $"security-master:{mismatchedSecurityId:N};snapshot:test-source-hash;approved:true"
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-journal-provenance-mismatch",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_JOURNAL_SECURITY_MASTER_PROVENANCE_MISMATCH");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentLinesWithoutSecurityMasterGateEvidence()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(
            workflow.FundAccountId,
            includeLineSecurityMasterEvidence: false);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-missing-line-security-master",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ID_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_APPROVAL_REQUIRED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISSING");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentLineWhoseSecurityMasterIdDiffersFromJournalMetadata()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var lineSecurityId = Guid.Parse("C5FC0731-CC8A-4719-97D2-4719C4B9D43D");
        var candidate = CreateInstrumentJournalCandidate(workflow.FundAccountId);
        candidate = candidate with
        {
            Lines =
            [
                candidate.Lines[0] with
                {
                    SecurityId = lineSecurityId,
                    SecurityMasterProvenance = $"security-master:{lineSecurityId:N};snapshot:test-source-hash;approved:true"
                },
                candidate.Lines[1]
            ]
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-line-security-id-mismatch",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ID_MISMATCH");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentLineWhoseSymbolDiffersFromJournalMetadata()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(workflow.FundAccountId);
        candidate = candidate with
        {
            Lines =
            [
                candidate.Lines[0] with { Symbol = "MSFT" },
                candidate.Lines[1]
            ]
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-line-symbol-mismatch",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISMATCH");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentLineWhoseProvenanceDoesNotReferenceLineSecurityId()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var mismatchedSecurityId = Guid.Parse("DA8597C1-BE48-499C-BE0F-0E55E0E75C53");
        var candidate = CreateInstrumentJournalCandidate(workflow.FundAccountId);
        candidate = candidate with
        {
            Lines =
            [
                candidate.Lines[0] with
                {
                    SecurityMasterProvenance = $"security-master:{mismatchedSecurityId:N};snapshot:test-source-hash;approved:true"
                },
                candidate.Lines[1]
            ]
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-line-provenance-mismatch",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISMATCH");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentLineWhoseLedgerMappingDoesNotReferenceInstrument()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(workflow.FundAccountId);
        candidate = candidate with
        {
            Lines =
            [
                candidate.Lines[0] with { LedgerMappingReference = "ledger-map:generic-securities" },
                candidate.Lines[1]
            ]
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-line-mapping-mismatch",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISMATCH");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentAccountLinesWithoutExplicitSecurityMasterLineage()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate(workflow.FundAccountId) with
        {
            Lines =
            [
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Securities",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Cash",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 0m,
                    Credit: 100m)
            ]
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-instrument-account-without-lineage",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ID_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_APPROVAL_REQUIRED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISSING");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISSING");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectApprovedInstrumentLineWithoutApprovalEvidence()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(
            workflow.FundAccountId,
            includeSecurityMasterApprovalReference: false);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-missing-approval-reference",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectInstrumentLineWithoutActiveSecurityMasterStatus()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(
            workflow.FundAccountId,
            securityMasterStatus: SecurityStatusDto.Inactive);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-inactive-security-master",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldPersistInstrumentLineSecurityMasterLineage()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out _, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(workflow.FundAccountId);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-instrument-lineage",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeTrue();
        var journal = journalStore.Appended.Should().ContainSingle().Which.Entry;
        journal.Metadata.Tags.Should().ContainKey("securityMasterLineage");
        journal.Metadata.Tags!["securityMasterLineage"].Should().Contain("AAPL");
        journal.Metadata.Tags!["securityMasterLineage"].Should().Contain("ledger-map:aapl-gaap-securities");
        journal.Metadata.Tags!["securityMasterLineage"].Should().Contain("sm-approval:aapl-controller");
        journal.Metadata.Tags!["securityMasterLineage"].Should().Contain("security-status:Active");
        journal.Metadata.Tags!["securityMasterLineage"].Should().Contain(candidate.Lines[0].SecurityId!.Value.ToString("N"));
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldUseTransactionalCommitStoreWhenRegistered()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var journalStore = new RecordingLedgerJournalStore();
        var commitStore = new RecordingTransactionalCommitStore(repository, auditStore, journalStore);
        var service = new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            ledgerJournalStore: null,
            transactionalCommitStore: commitStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate(workflow.FundAccountId);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeTrue();
        commitStore.CommitCount.Should().Be(1);
        journalStore.Appended.Should().ContainSingle();
        result.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectJournalCandidateThatDoesNotMatchWorkflowContext()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out _, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateJournalCandidate(workflow.FundAccountId) with
        {
            AggregateId = Guid.NewGuid()
        };

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_JOURNAL_AGGREGATE_ID_MISMATCH");
        journalStore.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldLeaveWorkflowUnchangedWhenTransactionalCommitFails()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var journalStore = new RecordingLedgerJournalStore();
        var commitStore = new RecordingTransactionalCommitStore(
            repository,
            auditStore,
            journalStore,
            throwBeforeCommit: true);
        var service = new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            ledgerJournalStore: null,
            transactionalCommitStore: commitStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId)));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("LEDGER_JOURNAL_APPEND_REJECTED");
        journalStore.Appended.Should().BeEmpty();

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Validated);
        persisted.LedgerPostingGate.Status.Should().Be(
            workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting).Status);
        persisted.Version.Should().Be(workflow.Version);
        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldBlockMissingPostingMetadataWithoutAppendingJournalCandidate()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "",
            PostingKind: "",
            PeriodOpen: true,
            Rationale: "Attempt posting without durable posting metadata",
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId)));

        result.Success.Should().BeTrue();
        result.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        result.Workflow.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_BATCH_ID_REQUIRED");
        result.Workflow.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_POSTING_KIND_REQUIRED");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posting-blocked");
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldBlockClosedPeriodPosting()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out _, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-closed-period",
            PostingKind: "period-close",
            PeriodOpen: false));

        result.Success.Should().BeTrue();
        result.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        result.Workflow.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_PERIOD_CLOSED");
        journalStore.Appended.Should().BeEmpty();

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posting-blocked");
        timeline.Select(entry => entry.EventType).Should().NotContain("ledger-posted");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldFailBeforeSecurityMasterResolution()
    {
        var service = CreateService(out _, out _);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));

        var result = await service.PostLedgerEntriesAsync(start.Workflow!.WorkflowId, new OperationsLedgerPostRequestDto(
            start.Workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-before-sm",
            PostingKind: "period-close",
            PeriodOpen: true));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldFailWhenDraftIsUnbalanced()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-unbalanced",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId) with
            {
                Lines =
                [
                    new OperationsLedgerJournalLineDto(null, "Cash", nameof(LedgerAccountType.Asset), 100m, 0m),
                    new OperationsLedgerJournalLineDto(null, "Interest income", nameof(LedgerAccountType.Revenue), 0m, 90m)
                ]
            }));

        result.Success.Should().BeFalse();
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_DRAFT_IMBALANCED");
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldNotAppendAuditOrMutateWorkflowWhenJournalCandidateIsInvalid()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var service = CreateService(out var repository, out var auditStore, journalStore);
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-invalid-candidate",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId) with
            {
                PeriodId = Guid.Empty
            }));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_JOURNAL_PERIOD_ID_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.LedgerPosting);
        journalStore.Appended.Should().BeEmpty();

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        persisted!.Version.Should().Be(workflow.Version);
        persisted.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Validated);
        persisted.LedgerPostingGate.Status.Should().Be(OperationsGateStatusDto.InProgress);

        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectJournalCandidatePeriodThatDoesNotMatchWorkflowPeriod()
    {
        var service = CreateService(out _, out _);
        var workflowPeriodId = Guid.NewGuid();
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            workflowPeriodId.ToString(),
            null,
            "custodian",
            "ops-user"));
        var workflow = await AdvanceToLedgerValidatedStateAsync(service, start.Workflow!);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-period-mismatch",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId, Guid.NewGuid())));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_JOURNAL_PERIOD_ID_MISMATCH" &&
            blocker.Gate == OperationsGateKeyDto.LedgerPosting);
    }

    [Fact]
    public async Task ResolveBreakCaseAsync_ShouldClearCriticalBreakAndAllowApprovalSubmission()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases:
            [
                new OperationsBreakCaseDto(
                    "break-1",
                    "expected-coupon",
                    "Accrual",
                    "Critical",
                    "Open",
                    null,
                    null,
                    "Security Master expected event",
                    "Custodian statement",
                    100m,
                    null,
                    100m,
                    null,
                    "BOND1",
                    "Upload evidence",
                    [],
                    new OperationsContinuityCorrelationKeysDto(
                        RunId: "run-break-1",
                        FundAccountId: workflow.FundAccountId,
                        LedgerBatchId: "ledger-batch-1",
                        ReconciliationCaseId: "break-1"))
            ]));
        reconciled.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);

        var resolved = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-1",
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Uploaded missing custodian evidence"));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            resolved.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1"));
        var submit = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Submit after critical break cleared",
            ReportPackId: "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        resolved.Workflow!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Complete);
        resolved.Workflow.Status.Should().Be(OperationsWorkflowStatusDto.ApprovalPending);
        submit.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RunReconciliationAsync_ShouldFeedSecurityAccountingIssueCountsIntoGatePosture()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran reconciliation with Security Master accounting-event issues",
            BreakCases: [],
            SecurityAccountingIssueCount: 2,
            ExpectedAccountingEventCount: 3,
            ExpectedJournalPreviewCount: 1));

        reconciled.Success.Should().BeTrue();
        reconciled.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        reconciled.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
        reconciled.Workflow.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
    }


    [Fact]
    public async Task RunReconciliationAsync_WithSecurityAccountingIssues_ShouldReturnDeterministicSecurityMasterBlockerCode()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var first = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "security master terms missing",
            BreakCases: [],
            SecurityAccountingIssueCount: 1,
            ExpectedAccountingEventCount: 0,
            ExpectedJournalPreviewCount: 0));

        var second = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            first.Workflow!.Version,
            "ops-user",
            "security master terms still missing",
            BreakCases: [],
            SecurityAccountingIssueCount: 2,
            ExpectedAccountingEventCount: 0,
            ExpectedJournalPreviewCount: 0));

        first.Workflow!.Blockers.Should().ContainSingle(blocker => blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE");
        second.Workflow!.Blockers.Should().ContainSingle(blocker => blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE");
    }

    [Fact]
    public async Task RunReconciliationAsync_ZeroSecurityIssueCounts_ShouldNotAutoPassSecurityMasterGate()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var initiallyBlocked = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran reconciliation with security-accounting issues",
            BreakCases: [],
            SecurityAccountingIssueCount: 1));
        initiallyBlocked.Success.Should().BeTrue();
        initiallyBlocked.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        initiallyBlocked.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
        initiallyBlocked.Workflow.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);

        var stillBlocked = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            initiallyBlocked.Workflow!.Version,
            "ops-user",
            "Attempt to clear Security Master gate with caller-supplied zero counts",
            BreakCases: [],
            SecurityCoverageIssueCount: 0,
            SecurityAccountingIssueCount: 0));

        stillBlocked.Success.Should().BeTrue();
        stillBlocked.Workflow!.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
        stillBlocked.Workflow.Blockers.Should().Contain(blocker =>
            blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
    }

    [Fact]
    public async Task ApproveWorkflowAsync_ShouldRequireApprovalMetadata()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);

        var result = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            Reviewer: "",
            Rationale: "",
            ReportPackId: ""));

        result.Success.Should().BeFalse();
        result.Blockers.Should().ContainSingle(blocker => blocker.Code == "APPROVAL_METADATA_REQUIRED");
    }

    [Fact]
    public async Task ApproveWorkflowAsync_ShouldRejectMismatchedReadyReportPack()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approve against a different report pack",
            "report-pack-different"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "REPORT_PACK_ID_MISMATCH" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ApproveWorkflowAsync_ShouldRejectReviewerThatDoesNotMatchSubmission()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "other-reviewer",
            "Approve with mismatched reviewer",
            "report-pack-1"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "APPROVAL_REVIEWER_MISMATCH" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ApproveWorkflowAsync_ShouldRejectIncompleteChecklistControlApprovals()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approve with missing approval-gate control evidence",
            "report-pack-1",
            ChecklistControlApprovals:
            [
                new("close-gate-brokeringest", "operations-lead", new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)),
                new("close-gate-securitymaster", "security-master-lead", new DateTimeOffset(2026, 5, 31, 12, 1, 0, TimeSpan.Zero)),
                new("close-gate-ledgerposting", "ledger-lead", new DateTimeOffset(2026, 5, 31, 12, 2, 0, TimeSpan.Zero)),
                new("close-gate-reconciliation", "reconciliation-lead", new DateTimeOffset(2026, 5, 31, 12, 3, 0, TimeSpan.Zero)),
                new("close-gate-approval", "controller", new DateTimeOffset(2026, 5, 31, 12, 4, 0, TimeSpan.Zero))
            ]));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "CLOSE_CHECKLIST_CONTROL_APPROVALS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldRejectMissingChecklistControlApprovalsWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close without close-checklist control approvals",
            "report-pack-1"));

        close.Success.Should().BeFalse();
        close.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        close.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "CLOSE_CHECKLIST_CONTROL_APPROVALS_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task RejectWorkflowAsync_ShouldRouteLedgerMismatchBackToLedgerDraft()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);

        var rejected = await service.RejectWorkflowAsync(workflow.WorkflowId, new OperationsRejectWorkflowRequestDto(
            workflow.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Ledger entries need correction before close",
            ReasonCode: "LedgerMismatch"));

        rejected.Success.Should().BeTrue();
        rejected.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.LedgerPostingDraft);
        rejected.Workflow.ApprovalState.Should().Be(OperationsApprovalStateDto.Rejected);
        rejected.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting)
            .Status.Should().Be(OperationsGateStatusDto.InProgress);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("approval-rejected");
    }

    [Fact]
    public async Task RejectWorkflowAsync_ShouldRejectReviewerThatDoesNotMatchSubmission()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.RejectWorkflowAsync(workflow.WorkflowId, new OperationsRejectWorkflowRequestDto(
            workflow.Version,
            "ops-user",
            Reviewer: "other-reviewer",
            Rationale: "Reject with mismatched reviewer",
            ReasonCode: "LedgerMismatch"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "APPROVAL_REVIEWER_MISMATCH" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ReopenWorkflowAsync_ShouldRequireGovernedIncidentMetadata()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var closed = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        var denied = await service.ReopenWorkflowAsync(workflow.WorkflowId, new OperationsReopenWorkflowRequestDto(
            closed.Workflow!.Version,
            "ops-user",
            Rationale: "",
            IncidentId: "",
            IsGovernedAdmin: false));
        var reopened = await service.ReopenWorkflowAsync(workflow.WorkflowId, new OperationsReopenWorkflowRequestDto(
            closed.Workflow.Version,
            "ops-user",
            Rationale: "Incident requires reopened reconciliation",
            IncidentId: "INC-123",
            IsGovernedAdmin: true,
            Justification: "Controller approved reopening the closed period.",
            ApprovalReference: "approval-ref-123",
            ImpactSummary: "Reopens reconciliation gate for incident follow-up."));

        denied.Success.Should().BeFalse();
        denied.Blockers.Should().ContainSingle(blocker => blocker.Code == "REOPEN_GOVERNANCE_METADATA_REQUIRED");
        reopened.Success.Should().BeTrue();
        reopened.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.ReconciliationActive);
        reopened.Workflow.EvidenceLinks.Should().Contain(link => link.EvidenceId == "INC-123");
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldFailWhenCriticalBreakRemainsOpen()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);

        var reconciliation = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases:
            [
                new OperationsBreakCaseDto(
                    "break-critical",
                    "id",
                    "type",
                    "Critical",
                    "Open",
                    null,
                    null,
                    "summary",
                    "source",
                    10m,
                    null,
                    11m,
                    null,
                    "SYM",
                    "action",
                    [],
                    new OperationsContinuityCorrelationKeysDto(
                        RunId: "run-critical",
                        FundAccountId: workflow.FundAccountId,
                        LedgerBatchId: "ledger-batch-1",
                        ReconciliationCaseId: "break-critical"))
            ]));

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            reconciliation.Workflow!.Version,
            "ops-user",
            "Close with open critical break",
            "report-pack-1"));

        close.Success.Should().BeFalse();
        close.CloseReadiness.Should().NotBeNull();
        close.CloseReadiness!.Score.Should().BeLessThan(100);
        close.CloseReadiness.Components.Should().Contain(component =>
            component.Key == "reconciliation" &&
            component.IsReady == false &&
            component.Weight == 15);
        close.Blockers.Should().Contain(blocker => blocker.Code == "RECONCILIATION_BREAKS_OPEN");
        close.Blockers.Should().Contain(blocker => blocker.Code == "APPROVAL_MISSING");
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldRejectMismatchedReadyReportPackWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close using a mismatched report pack",
            "report-pack-different",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        close.Success.Should().BeFalse();
        close.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        close.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "REPORT_PACK_ID_MISMATCH" &&
            blocker.Gate == OperationsGateKeyDto.Approval);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldRejectTamperedAuditChainWithoutAppendingAudit()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new TamperingAuditStore();
        var service = new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            new RecordingLedgerJournalStore());
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var appendCountBefore = auditStore.AppendCount;
        auditStore.TimelineTransform = timeline => timeline
            .Select((entry, index) => index == 1 ? entry with { PreviousHash = "tampered" } : entry)
            .ToArray();

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));

        close.Success.Should().BeFalse();
        close.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        close.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "AUDIT_CHAIN_INVALID" &&
            blocker.Gate == OperationsGateKeyDto.Approval &&
            blocker.Severity == "Critical");
        auditStore.AppendCount.Should().Be(appendCountBefore);
        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted!.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshGatePostureAsync_ShouldRejectClosedWorkflowWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var closed = await CreateClosedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(closed.WorkflowId);

        var result = await service.RefreshGatePostureAsync(closed.WorkflowId, new OperationsGatePostureRequestDto(
            closed.Version,
            "ops-user",
            ReportPackReady: false,
            Rationale: "Attempt to mutate a closed workflow"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "WORKFLOW_CLOSED" &&
            blocker.Gate == null);

        var timelineAfter = await auditStore.GetTimelineAsync(closed.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task PostLedgerEntriesAsync_ShouldRejectClosedWorkflowBeforeLedgerPreconditions()
    {
        var service = CreateService(out _, out var auditStore);
        var closed = await CreateClosedWorkflowAsync(service);
        var timelineBefore = await auditStore.GetTimelineAsync(closed.WorkflowId);

        var result = await service.PostLedgerEntriesAsync(closed.WorkflowId, new OperationsLedgerPostRequestDto(
            closed.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-after-close",
            PostingKind: "period-close",
            PeriodOpen: true,
            Rationale: "Attempt to post after close",
            JournalCandidate: CreateJournalCandidate()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "WORKFLOW_CLOSED" &&
            blocker.Gate == OperationsGateKeyDto.LedgerPosting);

        var timelineAfter = await auditStore.GetTimelineAsync(closed.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldRejectRepeatedImportWithoutAppendingAudit()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var import = await service.ImportBrokerDataAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                start.Workflow.Version,
                "ops-user",
                "Imported custodian statement"));

        var repeatedImport = await service.ImportBrokerDataAsync(
            start.Workflow.WorkflowId,
            new OperationsTransitionRequestDto(
                import.Workflow!.Version,
                "ops-user",
                "Retried broker import"));

        import.Success.Should().BeTrue();
        repeatedImport.Success.Should().BeFalse();
        repeatedImport.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        repeatedImport.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "BROKER_IMPORT_ALREADY_RECORDED" &&
            blocker.Gate == OperationsGateKeyDto.BrokerIngest);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().HaveCount(2);
    }

    [Fact]
    public async Task ImportBrokerDataAsync_ShouldLeaveWorkflowUnchangedWhenAuditAppendFails()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new ThrowingAuditStore("broker-imported");
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));
        start.Workflow.Should().NotBeNull();
        var workflow = start.Workflow!;

        var act = () => service.ImportBrokerDataAsync(
            workflow.WorkflowId,
            new OperationsTransitionRequestDto(
                workflow.Version,
                "ops-user",
                "Imported custodian statement"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated audit append failure.");

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        persisted!.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Pending);
        persisted.BrokerIngestGate.Status.Should().Be(OperationsGateStatusDto.InProgress);
    }

    [Fact]
    public async Task FileAuditStore_ShouldAppendJsonlEventsAndMaintainHashChain()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var store = new FileOperationsWorkflowAuditStore(root, NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var workflowId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();

        var first = await store.AppendAsync(CreateAuditDraft(
            workflowId,
            fundAccountId,
            "workflow-started",
            OperationsWorkflowStatusDto.NotStarted,
            OperationsWorkflowStatusDto.CollectingBrokerData));
        var second = await store.AppendAsync(CreateAuditDraft(
            workflowId,
            fundAccountId,
            "broker-imported",
            OperationsWorkflowStatusDto.CollectingBrokerData,
            OperationsWorkflowStatusDto.CollectingBrokerData));

        var timeline = await store.GetTimelineAsync(workflowId);
        timeline.Should().HaveCount(2);
        timeline[0].CurrentHash.Should().Be(first.CurrentHash);
        timeline[1].CurrentHash.Should().Be(second.CurrentHash);
        timeline[1].PreviousHash.Should().Be(timeline[0].CurrentHash);

        var auditPath = Path.Combine(root, "operations-continuity", "audit", $"{workflowId:N}.jsonl");
        File.ReadAllLines(auditPath).Should().HaveCount(2);
    }

    [Fact]
    public void PostgresAuditAppend_ShouldAcquireWorkflowScopedAdvisoryLockBeforeReadingTailHash()
    {
        var source = ReadRepoFile(
            "src",
            "Meridian.Application",
            "OperationsContinuity",
            "PostgresOperationsContinuityStore.cs");
        var appendMethodStart = source.IndexOf(
            "private async Task<OperationsWorkflowAuditDto> AppendAuditAsync",
            StringComparison.Ordinal);

        appendMethodStart.Should().BeGreaterThanOrEqualTo(0);
        var appendMethod = source[appendMethodStart..];
        appendMethod.IndexOf("AcquireWorkflowAuditLockAsync", StringComparison.Ordinal)
            .Should().BeLessThan(appendMethod.IndexOf("LoadPreviousAuditHashAsync", StringComparison.Ordinal));
        source.Should().Contain("pg_advisory_xact_lock");
        source.Should().Contain("CreateWorkflowAuditLockKey");
    }

    private static OperationsContinuityWorkflowService CreateService(
        out InMemoryOperationsContinuityRepository repository,
        out InMemoryOperationsWorkflowAuditStore auditStore,
        RecordingLedgerJournalStore? ledgerJournalStore = null,
        bool registerLedgerJournalStore = true)
    {
        var derivation = new OperationsStatusDerivationService();
        repository = new InMemoryOperationsContinuityRepository(derivation);
        auditStore = new InMemoryOperationsWorkflowAuditStore();
        return new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            registerLedgerJournalStore ? ledgerJournalStore ?? new RecordingLedgerJournalStore() : null);
    }

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> RequiredChecklistControlApprovals() =>
    [
        new("close-gate-brokeringest", "operations-lead", new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)),
        new("close-gate-securitymaster", "security-master-lead", new DateTimeOffset(2026, 5, 31, 12, 1, 0, TimeSpan.Zero)),
        new("close-gate-ledgerposting", "ledger-lead", new DateTimeOffset(2026, 5, 31, 12, 2, 0, TimeSpan.Zero)),
        new("close-gate-reconciliation", "reconciliation-lead", new DateTimeOffset(2026, 5, 31, 12, 3, 0, TimeSpan.Zero)),
        new("close-gate-approval", "controller", new DateTimeOffset(2026, 5, 31, 12, 4, 0, TimeSpan.Zero)),
        new("close-gate-approval", "fund-admin", new DateTimeOffset(2026, 5, 31, 12, 5, 0, TimeSpan.Zero))
    ];

    private static async Task<OperationsContinuityWorkflowDto> CreateLedgerValidatedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "ops-user"));
        var import = await service.ImportBrokerDataAsync(start.Workflow!.WorkflowId, new OperationsTransitionRequestDto(start.Workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(start.Workflow.WorkflowId, new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user"));
        var security = await service.ResolveSecurityMasterMappingsAsync(start.Workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "ops-user"));
        var draft = await service.BuildLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "ops-user", "ledger-preview-1", true));
        var validated = await service.ValidateLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "ops-user", true, true));
        return validated.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> AdvanceToLedgerValidatedStateAsync(
        OperationsContinuityWorkflowService service,
        OperationsContinuityWorkflowDto workflow)
    {
        var import = await service.ImportBrokerDataAsync(workflow.WorkflowId, new OperationsTransitionRequestDto(workflow.Version, "ops-user"));
        var normalized = await service.NormalizeBrokerTransactionsAsync(workflow.WorkflowId, new OperationsTransitionRequestDto(import.Workflow!.Version, "ops-user"));
        var security = await service.ResolveSecurityMasterMappingsAsync(workflow.WorkflowId, new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "ops-user"));
        var draft = await service.BuildLedgerDraftAsync(workflow.WorkflowId, new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "ops-user", "ledger-preview-1", true));
        var validated = await service.ValidateLedgerDraftAsync(workflow.WorkflowId, new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "ops-user", true, true));
        return validated.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateApprovalSubmittedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(workflow.Version, "ops-user", BreakCases: []));
        var posture = await service.RefreshGatePostureAsync(workflow.WorkflowId, new OperationsGatePostureRequestDto(
            reconciled.Workflow!.Version,
            "ops-user",
            ReportPackReady: true,
            ReportPackId: "report-pack-1"));
        var submitted = await service.SubmitForApprovalAsync(workflow.WorkflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            "reviewer",
            "Submit for approval",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        return submitted.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateClosedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var workflow = await CreateApprovalSubmittedWorkflowAsync(service);
        var approved = await service.ApproveWorkflowAsync(workflow.WorkflowId, new OperationsApprovalDecisionRequestDto(
            workflow.Version,
            "ops-user",
            "reviewer",
            "Approved close",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var closed = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        return closed.Workflow!;
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateLedgerPostedWorkflowAsync(
        OperationsContinuityWorkflowService service)
    {
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var posted = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(workflow.FundAccountId)));
        return posted.Workflow!;
    }

    private static OperationsLedgerJournalCandidateDto CreateJournalCandidate(Guid? aggregateId = null, Guid? periodId = null)
    {
        var securityId = Guid.Parse("27F62228-5183-4C2D-95A3-8619BC93F15E");
        var idempotencyKey = $"{securityId:N}:fund-close:20260531:AccrueInterestIncome:test-source-hash";
        return new OperationsLedgerJournalCandidateDto(
            JournalEntryId: null,
            AggregateId: aggregateId ?? Guid.NewGuid(),
            PeriodId: periodId ?? Guid.NewGuid(),
            Timestamp: DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            Description: "Operations continuity month-end posting",
            Lines:
            [
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Cash",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Interest income",
                    AccountType: nameof(LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 100m)
            ],
            CommandId: Guid.NewGuid(),
            SourceEventId: Guid.NewGuid(),
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: "operations-continuity-accrual",
            RuleVersion: "v1",
            PostingKind: LedgerPostingKindDto.Originating,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "operations-continuity",
                Symbol: "OPS",
                SecurityId: securityId,
                LedgerBook: "fund-close"),
            IdempotencyKey: idempotencyKey,
            SecurityMasterProvenance: $"security-master:{securityId:N};snapshot:test-source-hash");
    }

    private static OperationsLedgerJournalCandidateDto CreateInstrumentJournalCandidate(
        Guid? aggregateId = null,
        Guid? periodId = null,
        bool includeLineSecurityMasterEvidence = true,
        bool includeSecurityMasterApprovalReference = true,
        SecurityStatusDto? securityMasterStatus = SecurityStatusDto.Active)
    {
        var securityId = Guid.Parse("BCE42470-8F6B-4BD3-9FC7-B8763F8B48B1");
        var provenance = $"security-master:{securityId:N};snapshot:test-source-hash;approved:true";
        var approvalReference = "sm-approval:aapl-controller";
        var idempotencyKey = $"{securityId:N}:fund-close:20260531:BuySecurity:test-source-hash";
        return new OperationsLedgerJournalCandidateDto(
            JournalEntryId: null,
            AggregateId: aggregateId ?? Guid.NewGuid(),
            PeriodId: periodId ?? Guid.NewGuid(),
            Timestamp: DateTimeOffset.Parse("2026-05-31T21:15:00Z"),
            Description: "Operations continuity instrument posting",
            Lines:
            [
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Securities",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 100m,
                    Credit: 0m,
                    Symbol: "AAPL",
                    SecurityId: includeLineSecurityMasterEvidence ? securityId : null,
                    SecurityMasterApproved: includeLineSecurityMasterEvidence,
                    SecurityMasterProvenance: includeLineSecurityMasterEvidence ? provenance : null,
                    LedgerMappingReference: includeLineSecurityMasterEvidence ? "ledger-map:aapl-gaap-securities" : null,
                    SecurityMasterApprovalReference: includeLineSecurityMasterEvidence && includeSecurityMasterApprovalReference
                        ? approvalReference
                        : null,
                    SecurityMasterStatus: includeLineSecurityMasterEvidence ? securityMasterStatus : null),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Cash",
                    AccountType: nameof(LedgerAccountType.Asset),
                    Debit: 0m,
                    Credit: 100m)
            ],
            CommandId: Guid.NewGuid(),
            SourceEventId: Guid.NewGuid(),
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: "operations-continuity-instrument-posting",
            RuleVersion: "v1",
            PostingKind: LedgerPostingKindDto.Originating,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "operations-continuity",
                Symbol: "AAPL",
                SecurityId: securityId,
                LedgerBook: "fund-close"),
            IdempotencyKey: idempotencyKey,
            SecurityMasterProvenance: provenance);
    }

    private static OperationsWorkflowAuditDraft CreateAuditDraft(
        Guid workflowId,
        Guid fundAccountId,
        string eventType,
        OperationsWorkflowStatusDto fromState,
        OperationsWorkflowStatusDto toState) =>
        new(
            workflowId,
            fundAccountId,
            "2026-05",
            eventType,
            fromState,
            toState,
            OperationsGateKeyDto.BrokerIngest,
            OperationsGateStatusDto.InProgress,
            OperationsGateStatusDto.InProgress,
            "ops-user",
            "Monthly close evidence",
            "corr-1",
            []);

    private static string ReadRepoFile(params string[] pathParts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(pathParts.Prepend(root).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Application")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }

    private sealed class RecordingLedgerJournalStore : ILedgerJournalStore
    {
        public List<LedgerJournalEntryWrite> Appended { get; } = [];

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
            {
                throw new LedgerValidationException("Journal entry must be balanced.");
            }

            Appended.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(null);
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([]);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>([]);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(book);
        }
    }

    private sealed class RecordingTransactionalCommitStore : IOperationsContinuityTransactionalCommitStore
    {
        private readonly IOperationsContinuityRepository _repository;
        private readonly IOperationsWorkflowAuditStore _auditStore;
        private readonly RecordingLedgerJournalStore _journalStore;
        private readonly bool _throwBeforeCommit;

        public RecordingTransactionalCommitStore(
            IOperationsContinuityRepository repository,
            IOperationsWorkflowAuditStore auditStore,
            RecordingLedgerJournalStore journalStore,
            bool throwBeforeCommit = false)
        {
            _repository = repository;
            _auditStore = auditStore;
            _journalStore = journalStore;
            _throwBeforeCommit = throwBeforeCommit;
        }

        public int CommitCount { get; private set; }

        public async Task<OperationsContinuityTransactionalCommitResult> CommitLedgerPostingAsync(
            OperationsContinuityWorkflow workflow,
            OperationsWorkflowAuditDraft auditDraft,
            LedgerJournalEntryWrite journalEntry,
            CancellationToken ct = default)
        {
            CommitCount++;
            if (_throwBeforeCommit)
            {
                throw new LedgerValidationException("Simulated transactional commit rejection.");
            }

            await _journalStore.AppendAsync(journalEntry, ct).ConfigureAwait(false);
            var audit = await _auditStore.AppendAsync(auditDraft, ct).ConfigureAwait(false);
            workflow.Touch(audit.OccurredAtUtc);
            await _repository.SaveAsync(workflow, ct).ConfigureAwait(false);
            return new OperationsContinuityTransactionalCommitResult(workflow, audit);
        }
    }

    private sealed class ThrowingAuditStore(string failEventType) : IOperationsWorkflowAuditStore
    {
        private readonly InMemoryOperationsWorkflowAuditStore _inner = new();

        public Task<OperationsWorkflowAuditDto> AppendAsync(OperationsWorkflowAuditDraft draft, CancellationToken ct = default)
        {
            if (string.Equals(draft.EventType, failEventType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated audit append failure.");
            }

            return _inner.AppendAsync(draft, ct);
        }

        public Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
            => _inner.GetTimelineAsync(workflowId, ct);
    }

    private sealed class TamperingAuditStore : IOperationsWorkflowAuditStore
    {
        private readonly InMemoryOperationsWorkflowAuditStore _inner = new();

        public int AppendCount { get; private set; }

        public Func<IReadOnlyList<OperationsWorkflowAuditDto>, IReadOnlyList<OperationsWorkflowAuditDto>>? TimelineTransform { get; set; }

        public async Task<OperationsWorkflowAuditDto> AppendAsync(
            OperationsWorkflowAuditDraft draft,
            CancellationToken ct = default)
        {
            var audit = await _inner.AppendAsync(draft, ct).ConfigureAwait(false);
            AppendCount++;
            return audit;
        }

        public async Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(
            Guid workflowId,
            CancellationToken ct = default)
        {
            var timeline = await _inner.GetTimelineAsync(workflowId, ct).ConfigureAwait(false);
            return TimelineTransform?.Invoke(timeline) ?? timeline;
        }
    }

    [Fact]
    public void OperationsContractEnums_ShouldSerializeAsStableStrings()
    {
        JsonSerializer.Serialize(OperationsWorkflowStatusDto.Blocked).Should().Be("\"Blocked\"");
        JsonSerializer.Serialize(OperationsGateStatusDto.ReviewRequired).Should().Be("\"ReviewRequired\"");
        JsonSerializer.Serialize(OperationsGateKeyDto.Reconciliation).Should().Be("\"Reconciliation\"");
        JsonSerializer.Serialize(OperationsIssueCodeDto.ReconciliationCriticalBreaksOpen)
            .Should().Be("\"ReconciliationCriticalBreaksOpen\"");
    }
}
