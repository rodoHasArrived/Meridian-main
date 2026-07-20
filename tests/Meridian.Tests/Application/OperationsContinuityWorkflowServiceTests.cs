using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
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
        OperationsWorkflowContractMatrix.ReconciliationLaneStatuses.Should().BeEquivalentTo(Enum.GetValues<OperationsReconciliationLaneStatusDto>());
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
            "workflow-transition-blocked",
            "workflow-transition-failed",
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
            "REPORT_PACK_ID_MISMATCH",
            "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED"
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
        var actionOriginJson = JsonSerializer.Serialize(OperationsActionOriginDto.AssistantDraft);
        var blockerJson = JsonSerializer.Serialize(sampleBlocker);
        var transitionJson = JsonSerializer.Serialize(sampleResult);

        workflowStatusJson.Should().Contain("ReadyForClose");
        gateStatusJson.Should().Contain("ReviewRequired");
        actionOriginJson.Should().Contain("AssistantDraft");
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
        result.Workflow.ReviewedAutomation.Should().NotBeNull();
        result.Workflow.ReviewedAutomation!.Stage.Should().Be("Suggestions only");
        result.Workflow.ReviewedAutomation.RequiredActions.Should().Contain(
            "Keep automation output in the review queue before approval, posting, publication, payment, or evidence-retention actions.");

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
    public async Task StartWorkflowAsync_ShouldAllowDistinctLedgerBooksForSameFundAndPeriod()
    {
        var service = CreateService(out _, out _);
        var fundAccountId = Guid.NewGuid();
        var primaryBookId = Guid.NewGuid();
        var taxBookId = Guid.NewGuid();

        var primary = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Open primary-book monthly operations close workflow",
            LedgerBookId: primaryBookId));

        var tax = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Open tax-book monthly operations close workflow",
            LedgerBookId: taxBookId));

        var duplicatePrimary = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "ibkr",
            Actor: "ops-user",
            Rationale: "Retry duplicate primary-book close workflow",
            LedgerBookId: primaryBookId));

        primary.Success.Should().BeTrue();
        tax.Success.Should().BeTrue();
        duplicatePrimary.Success.Should().BeFalse();
        duplicatePrimary.ErrorCode.Should().Be("WORKFLOW_ALREADY_EXISTS");

        var allSummaries = await service.ListAsync(fundAccountId, "2026-05");
        allSummaries.Select(static summary => summary.LedgerBookId)
            .Should().BeEquivalentTo([primaryBookId, taxBookId]);

        var primarySummaries = await service.ListAsync(fundAccountId, "2026-05", ledgerBookId: primaryBookId);
        primarySummaries.Should().ContainSingle();
        primarySummaries[0].LedgerBookId.Should().Be(primaryBookId);
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
    public async Task RefreshGatePostureAsync_ShouldCommitSucceededOutcomeWithWorkflowAndAudit()
    {
        var service = CreateService(out var repository, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var result = await service.RefreshGatePostureAsync(
            start.Workflow!.WorkflowId,
            new OperationsGatePostureRequestDto(
                start.Workflow.Version,
                "ops-user",
                Rationale: "Retain current provider posture.",
                CorrelationId: "refresh-posture-1",
                ProviderAccountLinked: true));

        result.Success.Should().BeTrue();
        result.Outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        var persisted = await repository.GetAsync(start.Workflow.WorkflowId);
        persisted!.Version.Should().Be(result.Workflow!.Version);

        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        var audit = timeline.Should().ContainSingle(entry => entry.EventType == "gate-posture-refreshed").Subject;
        audit.Outcome.Should().BeEquivalentTo(result.Outcome);
        OperationsWorkflowAuditHashing.TryValidateChain(timeline, out _, out _).Should().BeTrue();
    }

    [Fact]
    public async Task AuditHashing_SourceGeneratedMetadata_ShouldPreserveCanonicalHashCompatibility()
    {
        var service = CreateService(out _, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var refreshed = await service.RefreshGatePostureAsync(
            start.Workflow!.WorkflowId,
            new OperationsGatePostureRequestDto(
                start.Workflow.Version,
                "ops-user",
                Rationale: "Retain current provider posture.",
                CorrelationId: "sourcegen-hash-compatibility",
                ProviderAccountLinked: true));

        refreshed.Success.Should().BeTrue();
        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().HaveCount(2);
        timeline[0].Outcome.Should().BeNull("the genesis event exercises the legacy hash payload");
        timeline[1].Outcome.Should().NotBeNull("terminal receipts exercise the outcome hash payload");
        timeline.Should().OnlyContain(entry =>
            entry.CurrentHash == ComputeReflectionCanonicalHash(entry));
    }

    [Fact]
    public void AuditHashing_SourceGeneratedMetadata_ShouldNotRequireReflectionFallback()
    {
        var hashingSource = ReadRepoFile(
            "src",
            "Meridian.FinancialOperations",
            "OperationsContinuity",
            "OperationsWorkflowAuditHashing.cs");
        var contextSource = ReadRepoFile(
            "src",
            "Meridian.FinancialOperations",
            "OperationsContinuity",
            "OperationsWorkflowAuditHashJsonContext.cs");

        hashingSource.Should().NotContain("JsonSerializerOptions");
        hashingSource.Should().Contain(
            "OperationsWorkflowAuditHashJsonContext.Default.LegacyAuditHashInput");
        hashingSource.Should().Contain(
            "OperationsWorkflowAuditHashJsonContext.Default.OutcomeAuditHashInput");
        contextSource.Should().Contain("JsonSourceGenerationOptions(JsonSerializerDefaults.Web");
        contextSource.Should().Contain("TypeInfoPropertyName = \"LegacyAuditHashInput\"");
        contextSource.Should().Contain("TypeInfoPropertyName = \"OutcomeAuditHashInput\"");
        contextSource.Should().Contain("JsonSerializerContext");
    }

    [Fact]
    public async Task NormalizeBrokerTransactionsAsync_ShouldPersistBlockedOutcomeAndRecoveryWithoutChangingState()
    {
        var service = CreateService(out var repository, out var auditStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var result = await service.NormalizeBrokerTransactionsAsync(
            start.Workflow!.WorkflowId,
            new OperationsTransitionRequestDto(
                start.Workflow.Version,
                "ops-user",
                "Normalize before a statement is available.",
                "normalize-blocked-1"));

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        result.Outcome.Issues.Should().Contain(issue =>
            issue.Code == "BROKER_IMPORT_REQUIRED" && issue.IsBlocking);
        result.Outcome.Recovery.Should().NotBeEmpty();
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();

        var persisted = await repository.GetAsync(start.Workflow.WorkflowId);
        persisted!.Version.Should().Be(start.Workflow.Version);
        persisted.BrokerIntakeState.Should().Be(OperationsBrokerIntakeStateDto.Pending);
        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        var blockedAudit = timeline.Should()
            .ContainSingle(entry => entry.EventType == "workflow-transition-blocked")
            .Subject;
        blockedAudit.Outcome.Should().BeEquivalentTo(result.Outcome);
        blockedAudit.Rationale.Should().Contain("broker-transactions-normalized");
        OperationsWorkflowAuditHashing.TryValidateChain(timeline, out _, out _).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshGatePostureAsync_WhenAtomicPersistenceFails_ShouldRetainNoSucceededStateOrAudit()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var commitStore = new FailingWorkflowTransitionCommitStore(auditStore);
        var service = new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            transactionalCommitStore: commitStore);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var result = await service.RefreshGatePostureAsync(
            start.Workflow!.WorkflowId,
            new OperationsGatePostureRequestDto(
                start.Workflow.Version,
                "ops-user",
                Rationale: "This commit will fail.",
                CorrelationId: "refresh-failure-1",
                ProviderAccountLinked: true));

        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Failed);
        result.ErrorCode.Should().Be("PERSISTENCE_FAILED");
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();

        var persisted = await repository.GetAsync(start.Workflow.WorkflowId);
        persisted!.Version.Should().Be(start.Workflow.Version);
        var timeline = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timeline.Should().NotContain(entry => entry.EventType == "gate-posture-refreshed");
        timeline.Should().ContainSingle(entry =>
            entry.EventType == "workflow-transition-failed" &&
            entry.Outcome!.State == OperationTerminalState.Failed);
        timeline.Select(static entry => entry.Outcome)
            .Where(static outcome => outcome is not null)
            .Cast<VerifiedOperationOutcome>()
            .Should().NotContain(outcome => outcome.State == OperationTerminalState.Succeeded);
        commitStore.AcceptedCommitAttempts.Should().Be(1);
        commitStore.FailureReceiptCommits.Should().Be(1);
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
        draft.Workflow!.ReviewedAutomation.Should().NotBeNull();
        draft.Workflow.ReviewedAutomation!.Stage.Should().Be("Journal draft review");
        draft.Workflow.ReviewedAutomation.RequiresHumanReview.Should().BeTrue();
        draft.Workflow.ReviewedAutomation.ProhibitedActions.Should().Contain("Post material journals without approval");
        draft.Workflow.ReviewedAutomation.RequiredActions.Should().Contain("Review the ledger draft and retained source evidence before posting.");
        var validated = await service.ValidateLedgerDraftAsync(workflowId, new OperationsLedgerValidationRequestDto(
            draft.Workflow.Version,
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
        posture.Workflow!.AccountingRecordSummary.Should().NotBeNull();
        posture.Workflow.AccountingRecordSummary!.CompleteCategoryCount.Should().Be(5);
        posture.Workflow.AccountingRecordSummary.RequiredCategoryCount.Should().Be(8);
        posture.Workflow.AccountingRecordSummary.IsAuditReady.Should().BeFalse();
        posture.Workflow.AccountingRecordSummary.EvidenceCategories.Should().Contain(category =>
            category.Key == "exports" &&
            !category.IsComplete &&
            category.Status.Contains("close-package publication", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.DashboardSummary.Should().NotBeNull();
        posture.Workflow.DashboardSummary!.Stage.Should().Be("Approve Results");
        posture.Workflow.DashboardSummary.Status.Should().Be(EvidenceStatusDto.Blocked);
        var approveMetric = posture.Workflow.DashboardSummary.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "approve-results")
            .Subject;
        approveMetric.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        approveMetric.RequiredActions.Should().Contain("Submit workflow approval for report pack report-pack-1 with reviewer and rationale.");
        approveMetric.RequiredActions.Should().Contain("Retain 1 checklist-control approval for Broker, custodian, and bank intake close gate (close-gate-brokeringest) before approval submission.");
        approveMetric.RequiredActions.Should().Contain("Retain 1 checklist-control approval for Reconciliation close gate (close-gate-reconciliation) before approval submission.");
        approveMetric.RequiredActions.Should().NotContain("Complete workflow approval and checklist-control approvals.");
        var produceEvidenceMetric = posture.Workflow.DashboardSummary.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "produce-evidence")
            .Subject;
        produceEvidenceMetric.Value.Should().Be("Report pack ready");
        produceEvidenceMetric.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        produceEvidenceMetric.RequiredActions.Should().Contain("Publish the close package manifest and retain the evidence hash.");
        produceEvidenceMetric.RequiredActions.Should().Contain("Close the workflow and retain the period-lock package before evidence release.");
        produceEvidenceMetric.RequiredActions.Should()
            .NotContain("Publish and retain the evidence package before period close.");
        posture.Workflow.DashboardSummary.RequiredActions.Should()
            .Contain("Publish the close package manifest and retain the evidence hash.");
        posture.Workflow.ReviewedAutomation.Should().NotBeNull();
        posture.Workflow.ReviewedAutomation!.Stage.Should().Be("Report commentary and audit request list draft review");
        posture.Workflow.ReviewedAutomation.Summary.Should().Contain("draft report commentary");
        posture.Workflow.ReviewedAutomation.Summary.Should().Contain("audit request lists");
        posture.Workflow.ReviewedAutomation.AllowedUseCases.Should().Contain("Draft report commentary");
        posture.Workflow.ReviewedAutomation.AllowedUseCases.Should().Contain("Draft audit request lists");
        posture.Workflow.ReviewedAutomation.Artifacts.Should().Contain(artifact =>
            artifact.ArtifactKind == "Report commentary" &&
            artifact.Title == "Report commentary draft" &&
            artifact.RequiresHumanReview &&
            artifact.BlockedMaterialAction == "Cannot publish reports or release support packages.");
        posture.Workflow.ReviewedAutomation.Artifacts.Should().Contain(artifact =>
            artifact.ArtifactKind == "Audit request list" &&
            artifact.Title == "Audit request list draft" &&
            artifact.SuggestedOperatorAction.Contains("assign an owner", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.ReviewedAutomation.Artifacts.Should().Contain(artifact =>
            artifact.ArtifactKind == "Missing support" &&
            artifact.BlockedMaterialAction == "Cannot approve its own missing-support disposition.");
        posture.Workflow.ReviewedAutomation.RequiredActions.Should().Contain(
            "Review drafted report commentary and audit request lists against retained evidence before submission.");
        posture.Workflow.EvidencePackages.Should().HaveCount(8);
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.PackageId == posture.Workflow.AccountingRecordSummary!.RecordId &&
            package.Label == "Accounting record evidence" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            package.CompleteCategoryCount == 5 &&
            package.RequiredCategoryCount == 8);
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Reconciliation coverage evidence" &&
            package.Status == EvidenceStatusDto.Ready &&
            package.CompleteCategoryCount == 7 &&
            package.RequiredCategoryCount == 7 &&
            package.Summary.Contains("cash, position, trade, income, MBS factor, bank, and GL", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Exception management evidence" &&
            package.Status == EvidenceStatusDto.Ready &&
            package.CompleteCategoryCount == 3 &&
            package.RequiredCategoryCount == 3 &&
            package.Summary.Contains("no open exception casework", StringComparison.OrdinalIgnoreCase));
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Report pack evidence" &&
            package.Status == EvidenceStatusDto.Ready &&
            package.IsReady);
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Close package manifest" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            package.RequiredActions.Contains("Publish the close package manifest and retain the evidence hash."));
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Approval history evidence" &&
            package.Status == EvidenceStatusDto.Missing &&
            package.CompleteCategoryCount == 0 &&
            package.RequiredCategoryCount == 3 &&
            package.RequiredActions.Contains("Submit workflow approval with reviewer, rationale, and report-pack evidence."));
        posture.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Period lock and reopen evidence" &&
            package.Status == EvidenceStatusDto.Missing &&
            package.CompleteCategoryCount == 1 &&
            package.RequiredCategoryCount == 2 &&
            package.RequiredActions.Contains("Close the workflow and retain the period-lock package before evidence release."));
        var submitted = await service.SubmitForApprovalAsync(workflowId, new OperationsSubmitApprovalRequestDto(
            posture.Workflow!.Version,
            "ops-user",
            Reviewer: "reviewer",
            Rationale: "Submit clean workflow",
            ReportPackId: "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        submitted.Workflow!.ReviewedAutomation.Should().NotBeNull();
        submitted.Workflow.ReviewedAutomation!.Stage.Should().Be("Reviewer approval required");
        submitted.Workflow.ReviewedAutomation.RequiredActions.Should().Contain("Complete reviewer approval before close evidence can be released.");
        submitted.Workflow.DashboardSummary.Should().NotBeNull();
        var submittedApproveMetric = submitted.Workflow.DashboardSummary!.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "approve-results")
            .Subject;
        submittedApproveMetric.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        submittedApproveMetric.RequiredActions.Should().Contain("Record approval decision from reviewer with retained rationale for report pack report-pack-1.");
        submittedApproveMetric.RequiredActions.Should().Contain("Retain 2 checklist-control approvals for Approval and close readiness close gate (close-gate-approval) before approval decision.");
        submittedApproveMetric.RequiredActions.Should().NotContain("Complete workflow approval and checklist-control approvals.");
        var approved = await service.ApproveWorkflowAsync(workflowId, new OperationsApprovalDecisionRequestDto(
            submitted.Workflow.Version,
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
        closed.Workflow.ClosePackage.DocumentSnapshots.Should().BeEmpty();
        closed.Workflow.ClosePackage.ChecklistControlApprovals.Should().HaveCount(6);
        closed.Workflow.AccountingRecordSummary.Should().NotBeNull();
        closed.Workflow.AccountingRecordSummary!.IsAuditReady.Should().BeTrue();
        closed.Workflow.AccountingRecordSummary.CompleteCategoryCount.Should().Be(8);
        closed.Workflow.AccountingRecordSummary.RequiredCategoryCount.Should().Be(8);
        closed.Workflow.AccountingRecordSummary.AuditPackReadiness.Should().NotBeNull();
        closed.Workflow.AccountingRecordSummary.AuditPackReadiness!.IsComplete.Should().BeTrue();
        closed.Workflow.AccountingRecordSummary.EvidenceCategories.Should().Contain(category =>
            category.Key == "exports" &&
            category.IsComplete &&
            category.Status.Contains("evidence hash", StringComparison.OrdinalIgnoreCase));
        closed.Workflow.DashboardSummary.Should().NotBeNull();
        closed.Workflow.DashboardSummary!.Stage.Should().Be("Produce Evidence");
        closed.Workflow.DashboardSummary.Status.Should().Be(EvidenceStatusDto.Ready);
        closed.Workflow.DashboardSummary.IsReady.Should().BeTrue();
        closed.Workflow.DashboardSummary.ReadyMetricCount.Should().Be(6);
        closed.Workflow.DashboardSummary.TotalMetricCount.Should().Be(6);
        closed.Workflow.DashboardSummary.Metrics.Should().OnlyContain(metric => metric.Status == EvidenceStatusDto.Ready);
        closed.Workflow.DashboardSummary.Metrics.Should().Contain(metric =>
            metric.MetricId == "close-support" &&
            metric.Value == "Ready to close");
        closed.Workflow.EvidencePackages.Should().HaveCount(8);
        closed.Workflow.EvidencePackages.Should().OnlyContain(package => package.Status == EvidenceStatusDto.Ready);
        closed.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Audit support package" &&
            package.IsReady &&
            package.CompleteCategoryCount == package.RequiredCategoryCount);
        closed.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Reconciliation coverage evidence" &&
            package.IsReady &&
            package.CompleteCategoryCount == 7 &&
            package.RequiredCategoryCount == 7);
        closed.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Exception management evidence" &&
            package.IsReady &&
            package.CompleteCategoryCount == package.RequiredCategoryCount);
        closed.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Approval history evidence" &&
            package.IsReady &&
            package.CompleteCategoryCount == 3 &&
            package.RequiredCategoryCount == 3 &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "report-pack-1") &&
            package.Summary.Contains("retained checklist-control approval", StringComparison.OrdinalIgnoreCase));
        closed.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Period lock and reopen evidence" &&
            package.IsReady &&
            package.CompleteCategoryCount == package.RequiredCategoryCount &&
            package.Summary.Contains("no governed reopen incident is active", StringComparison.OrdinalIgnoreCase));
        closed.Workflow.ReviewedAutomation.Should().NotBeNull();
        closed.Workflow.ReviewedAutomation!.Status.Should().Be(EvidenceStatusDto.Ready);
        closed.Workflow.ReviewedAutomation.RequiresHumanReview.Should().BeFalse();
        closed.Workflow.ReviewedAutomation.Stage.Should().Be("Reviewed evidence retained");
        closed.Workflow.ReviewedAutomation.ProhibitedActions.Should().Contain("Erase evidence");

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
        var closeSupportMetric = posture.Workflow.DashboardSummary!.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "close-support")
            .Subject;
        closeSupportMetric.RequiredActions.Should().Contain(action =>
            action.Contains("Resolve Provider data freshness", StringComparison.OrdinalIgnoreCase) &&
            action.Contains("Provider sync is stale", StringComparison.OrdinalIgnoreCase));
        closeSupportMetric.RequiredActions.Should()
            .NotContain("Clear close readiness blockers and retain period-lock or reopen evidence.");
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
    public async Task SubmitForApprovalAsync_ShouldRetainBlockedMissingSubmissionMetadataAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRetainBlockedMismatchedReadyReportPackAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_ShouldRetainBlockedPrerequisiteAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
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
    public async Task BuildLedgerDraftAsync_ShouldRequireSecurityMasterApprovalAndLedgerMappingEvidence()
    {
        var service = CreateService(out _, out _);
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
        var security = await service.ResolveSecurityMasterMappingsAsync(
            start.Workflow.WorkflowId,
            new OperationsSecurityMasterResolveRequestDto(
                normalized.Workflow!.Version,
                "ops-user",
                "Resolved Security Master identities"));

        var result = await service.BuildLedgerDraftAsync(start.Workflow.WorkflowId, new OperationsLedgerDraftRequestDto(
            security.Workflow!.Version,
            "ops-user",
            PreviewId: "ledger-preview-missing-security-master-proof",
            IsBalanced: true,
            HasSecurityMasterProvenance: false,
            HasSecurityMasterApproval: false,
            HasLedgerMappings: false));

        result.Success.Should().BeTrue();
        var ledgerGate = result.Workflow!.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting);
        ledgerGate.Status.Should().Be(OperationsGateStatusDto.Blocked);
        result.Workflow.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Drafted);
        ledgerGate.Blockers.Should().Contain(blocker =>
            blocker.Code == "LEDGER_SECURITY_MASTER_PROVENANCE_MISSING");
        ledgerGate.Blockers.Should().Contain(blocker =>
            blocker.Code == "LEDGER_SECURITY_MASTER_APPROVAL_MISSING");
        ledgerGate.Blockers.Should().Contain(blocker =>
            blocker.Code == "LEDGER_SECURITY_MASTER_MAPPING_MISSING");
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
    public async Task ResolveSecurityMasterMappingsAsync_ShouldRetainBlockedBrokerNormalizationAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
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
    public async Task ApproveSecurityMasterOverrideAsync_ShouldRejectReviewedAutomationOriginBeforeApprovalAudit()
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
                "reviewed-automation",
                "override-1",
                "Assistant draft approval",
                "policy-sm-override-v1",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("REVIEWED_AUTOMATION_REVIEW_REQUIRED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
        var timelineAfter = await auditStore.GetTimelineAsync(start.Workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
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
    public async Task ApproveSecurityMasterOverrideAsync_ShouldRetainBlockedExpiredApprovalAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
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
        journalStore.Appended[0].Entry.Lines.Should().OnlyContain(line => line.Dimensions != null);
        var cashLine = journalStore.Appended[0].Entry.Lines.Single(static line => line.Account.Name == "Cash");
        cashLine.Dimensions!.FundId.Should().Be("fund-alpha");
        cashLine.Dimensions.EntityId.Should().Be("entity-master");
        cashLine.Dimensions.CostCenterId.Should().Be("cash-ops");
        cashLine.Dimensions.ExternalGlDimensions["Department"].Should().Be("Treasury");
        var revenueLine = journalStore.Appended[0].Entry.Lines.Single(static line => line.Account.Name == "Interest income");
        revenueLine.Dimensions!.FundId.Should().Be("fund-alpha");
        revenueLine.Dimensions.EntityId.Should().Be("entity-master");
        revenueLine.Dimensions.CostCenterId.Should().Be("income-review");
        revenueLine.Dimensions.ExternalGlDimensions["Department"].Should().Be("Accounting");
        result.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);

        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(entry => entry.EventType).Should().Contain("ledger-posted");
    }

    [Fact]
    public async Task ReviewedAutomationMaterialCommands_ShouldRejectAssistantOriginBeforeMutation()
    {
        var service = CreateService(out _, out var auditStore);
        var ledgerReady = await CreateLedgerValidatedWorkflowAsync(service);
        var ledgerTimelineBefore = await auditStore.GetTimelineAsync(ledgerReady.WorkflowId);

        var ledgerPost = await service.PostLedgerEntriesAsync(ledgerReady.WorkflowId, new OperationsLedgerPostRequestDto(
            ledgerReady.Version,
            "assistant-agent",
            LedgerBatchId: "ledger-batch-1",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: CreateJournalCandidate(ledgerReady.FundAccountId),
            ActionOrigin: OperationsActionOriginDto.AutomationAssistant));

        AssertAutomationMaterialActionRejected(ledgerPost, OperationsGateKeyDto.LedgerPosting);
        var ledgerTimelineAfter = await auditStore.GetTimelineAsync(ledgerReady.WorkflowId);
        ledgerTimelineAfter.Should().HaveCount(ledgerTimelineBefore.Count);

        var approvalReady = await CreateApprovalSubmittedWorkflowAsync(service);
        var approvalDecision = await service.ApproveWorkflowAsync(approvalReady.WorkflowId, new OperationsApprovalDecisionRequestDto(
            approvalReady.Version,
            "assistant-agent",
            "reviewer",
            "Assistant attempted to approve close evidence",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        AssertAutomationMaterialActionRejected(approvalDecision, OperationsGateKeyDto.Approval);

        var approved = await service.ApproveWorkflowAsync(approvalReady.WorkflowId, new OperationsApprovalDecisionRequestDto(
            approvalReady.Version,
            "ops-user",
            "reviewer",
            "Human approval",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals()));
        var close = await service.CloseWorkflowAsync(approvalReady.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "assistant-agent",
            "Assistant attempted close-package publication",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            ActionOrigin: OperationsActionOriginDto.AutomationSuggestion));

        AssertAutomationMaterialActionRejected(close, OperationsGateKeyDto.Approval);

        var closed = await CreateClosedWorkflowAsync(service);
        var reopen = await service.ReopenWorkflowAsync(closed.WorkflowId, new OperationsReopenWorkflowRequestDto(
            closed.Version,
            "assistant-agent",
            Rationale: "Assistant attempted period reopen",
            IncidentId: "INC-AI-1",
            IsGovernedAdmin: true,
            Justification: "Synthetic assistant reopen",
            ApprovalReference: "assistant-draft",
            ImpactSummary: "Would override period-lock posture",
            ActionOrigin: OperationsActionOriginDto.AutomationAssistant));

        AssertAutomationMaterialActionRejected(reopen, OperationsGateKeyDto.Reconciliation);
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
        var securityId = Guid.Parse("BCE42470-8F6B-4BD3-9FC7-B8763F8B48B1");
        var service = CreateService(
            out _,
            out var auditStore,
            journalStore,
            securityStatuses: new Dictionary<Guid, SecurityStatusDto>
            {
                [securityId] = SecurityStatusDto.Inactive
            });
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
    public async Task PostLedgerEntriesAsync_ShouldRejectClientSuppliedActiveStatusWhenSecurityMasterIsInactive()
    {
        var journalStore = new RecordingLedgerJournalStore();
        var securityId = Guid.Parse("BCE42470-8F6B-4BD3-9FC7-B8763F8B48B1");
        var service = CreateService(
            out _,
            out _,
            journalStore,
            securityStatuses: new Dictionary<Guid, SecurityStatusDto>
            {
                [securityId] = SecurityStatusDto.Inactive
            });
        var workflow = await CreateLedgerValidatedWorkflowAsync(service);
        var candidate = CreateInstrumentJournalCandidate(
            workflow.FundAccountId,
            securityMasterStatus: SecurityStatusDto.Active);

        var result = await service.PostLedgerEntriesAsync(workflow.WorkflowId, new OperationsLedgerPostRequestDto(
            workflow.Version,
            "ops-user",
            LedgerBatchId: "ledger-batch-client-spoofed-active-security-master",
            PostingKind: "period-close",
            PeriodOpen: true,
            JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED");
        journalStore.Appended.Should().BeEmpty();
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
                Rationale: "Uploaded missing custodian evidence",
                EvidenceLinks: [CreateEvidenceLink("break-1-resolution-evidence", "Custodian resolution evidence")]));
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
        var incomeLane = resolved.Workflow.ReconciliationLanes.Single(static lane => lane.LaneId == "income-reconciliation");
        incomeLane.Status.Should().Be(OperationsReconciliationLaneStatusDto.Ready);
        incomeLane.IsReady.Should().BeTrue();
        incomeLane.BreakCount.Should().Be(0);
        incomeLane.RequiredActions.Should().BeEmpty();
        incomeLane.EvidenceLinks.Should().Contain(link =>
            link.EvidenceId == "break-1-resolution-evidence" &&
            link.Label == "Custodian resolution evidence");
        submit.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveBreakCaseAsync_WaiveAndSupersedeRetainTerminalDispositionLineage()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var measures = new[]
        {
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Value, 100m, 112m, 12m, 1m, "USD"),
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Quantity, 10m, 11m, 1m, 0m, "units"),
            new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.CostBasis, 80m, 84m, 4m, 1m, "USD")
        };
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases:
            [
                CreateOpenCriticalBreak(workflow, "break-waive") with
                {
                    Measures = measures,
                    BlockedOutputs = ["FinalReport", "PeriodClose"]
                },
                CreateOpenCriticalBreak(workflow, "break-supersede") with
                {
                    Measures = measures,
                    BlockedOutputs = ["FinalReport"]
                },
                CreateOpenCriticalBreak(workflow, "break-replacement-1") with
                {
                    Status = "Resolved",
                    Measures = measures,
                    Disposition = ReconciliationBreakDispositionDto.Resolved,
                    DispositionReason = "Corrected replacement case already cleared.",
                    DisposedAtUtc = DateTimeOffset.UtcNow,
                    BlockedOutputs = []
                }
            ]));

        var waived = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-waive",
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "controller-a",
                ResolutionStatus: "Waived",
                Rationale: "Approved policy exception.",
                EvidenceLinks: [CreateEvidenceLink("waiver-evidence-1", "Waiver evidence")],
                ApprovalActor: "controller-b",
                ApprovalReference: "approval:waiver-1"));
        waived.Success.Should().BeTrue();

        var superseded = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-supersede",
            new OperationsResolveBreakCaseRequestDto(
                waived.Workflow!.Version,
                "controller-a",
                ResolutionStatus: "Superseded",
                Rationale: "Replacement case created from corrected source.",
                EvidenceLinks: [CreateEvidenceLink("supersede-evidence-1", "Supersede evidence")],
                ApprovalActor: "controller-b",
                ApprovalReference: "approval:supersede-1",
                SupersedingBreakId: "break-replacement-1"));

        superseded.Success.Should().BeTrue();
        superseded.Workflow!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Complete);
        var waivedCase = superseded.Workflow.BreakCases.Single(item => item.BreakId == "break-waive");
        waivedCase.Disposition.Should().Be(ReconciliationBreakDispositionDto.Waived);
        waivedCase.DispositionEvidenceHash.Should().MatchRegex("^[0-9a-f]{64}$");
        waivedCase.Measures.Should().HaveCount(3);
        var supersededCase = superseded.Workflow.BreakCases.Single(item => item.BreakId == "break-supersede");
        supersededCase.Disposition.Should().Be(ReconciliationBreakDispositionDto.Superseded);
        supersededCase.SupersedingBreakId.Should().Be("break-replacement-1");
        supersededCase.DispositionApprovedBy.Should().Be("controller-b");
        supersededCase.DispositionEvidenceHash.Should().MatchRegex("^[0-9a-f]{64}$");

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted!.BreakCases.Where(item =>
                item.BreakId == "break-waive" || item.BreakId == "break-supersede")
            .Should().OnlyContain(item =>
                item.Status == "Waived" || item.Status == "Superseded");
        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Should().Contain(entry => entry.EventType == "reconciliation-break-waived");
        timeline.Should().Contain(entry => entry.EventType == "reconciliation-break-superseded");
    }

    [Fact]
    public async Task ResolveBreakCaseAsync_ShouldRejectCriticalBreakWithoutResolutionEvidence()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-missing-evidence")]));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-missing-evidence",
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Controller reviewed the break but did not attach evidence."));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "RECONCILIATION_EVIDENCE_MISSING" &&
            blocker.Gate == OperationsGateKeyDto.Reconciliation);
        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted!.BreakCases.Single(static item => item.BreakId == "break-missing-evidence")
            .Status.Should().Be("Open");
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
    }

    [Fact]
    public async Task ResolveBreakCaseAsync_ShouldRetainBlockedAlreadyClosedBreakAttempt()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-duplicate-resolution")]));
        var resolved = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-duplicate-resolution",
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Controller retained the first resolution evidence.",
                EvidenceLinks: [CreateEvidenceLink("break-duplicate-resolution-evidence", "First resolution evidence")]));
        var timelineBeforeDuplicate = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var duplicate = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-duplicate-resolution",
            new OperationsResolveBreakCaseRequestDto(
                resolved.Workflow!.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Controller attempted to resolve the already closed break again.",
                EvidenceLinks: [CreateEvidenceLink("break-duplicate-resolution-second-evidence", "Duplicate resolution evidence")]));

        duplicate.Success.Should().BeFalse();
        duplicate.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        duplicate.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "RECONCILIATION_BREAK_ALREADY_CLOSED" &&
            blocker.Gate == OperationsGateKeyDto.Reconciliation);
        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        persisted!.Version.Should().Be(resolved.Workflow.Version);
        var breakCase = persisted.BreakCases.Single(static item => item.BreakId == "break-duplicate-resolution");
        breakCase.Status.Should().Be("Resolved");
        breakCase.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "break-duplicate-resolution-evidence");
        breakCase.EvidenceLinks.Should().NotContain(link => link.EvidenceId == "break-duplicate-resolution-second-evidence");
        var timelineAfterDuplicate = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        AssertBlockedAttemptPersisted(timelineBeforeDuplicate, timelineAfterDuplicate, duplicate);
    }

    [Fact]
    public async Task ResolveBreakCaseAsync_ShouldRejectAssistantOriginBeforeMutation()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-assistant-origin")]));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var request = new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "assistant-agent",
                ResolutionStatus: "Resolved",
                Rationale: "Assistant attempted to clear the break.",
                EvidenceLinks: [CreateEvidenceLink("break-assistant-resolution-evidence", "Assistant draft evidence")],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft);

        var result = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-assistant-origin",
            request);

        AssertAutomationMaterialActionRejected(result, OperationsGateKeyDto.Reconciliation);
        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task AssignBreakCaseAsync_ShouldRetainOwnerEscalationAndAuditEvidence()
    {
        var service = CreateService(out _, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-assign")]));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var assigned = await service.AssignBreakCaseAsync(
            workflow.WorkflowId,
            "break-assign",
            new OperationsAssignBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                Owner: "fund-controller",
                Rationale: "Cash break aged beyond SLA",
                EscalationLevel: "Level 2",
                EscalationReason: "Cash break aged beyond SLA",
                DueDate: new DateOnly(2026, 6, 2),
                CorrelationId: "corr-break-assign",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto(
                        "assignment-note",
                        "Controller assignment note",
                        "/evidence/assignment-note",
                        "operator-note",
                        new DateTimeOffset(2026, 5, 31, 13, 0, 0, TimeSpan.Zero))
                ]));

        assigned.Success.Should().BeTrue();
        var breakCase = assigned.Workflow!.BreakCases.Single(item => item.BreakId == "break-assign");
        breakCase.Owner.Should().Be("fund-controller");
        breakCase.Status.Should().Be("InReview");
        breakCase.DueDate.Should().Be(new DateOnly(2026, 6, 2));
        breakCase.EscalationLevel.Should().Be("Level 2");
        breakCase.EscalationReason.Should().Be("Cash break aged beyond SLA");
        breakCase.EscalatedAtUtc.Should().NotBeNull();
        breakCase.EscalatedAtUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        breakCase.EvidenceLinks.Should().Contain(link => link.EvidenceId == "assignment-note");
        var cashLane = assigned.Workflow.ReconciliationLanes.Single(static lane => lane.LaneId == "cash-reconciliation");
        cashLane.Status.Should().Be(OperationsReconciliationLaneStatusDto.Blocked);
        cashLane.IsReady.Should().BeFalse();
        cashLane.BreakCount.Should().Be(1);
        cashLane.RequiredActions.Should().Contain(action =>
            action.Contains("Review Level 2 escalation", StringComparison.OrdinalIgnoreCase) &&
            action.Contains("Cash break aged beyond SLA", StringComparison.OrdinalIgnoreCase));
        cashLane.RequiredActions.Should().NotContain(action =>
            action.Contains("Assign 1 cash reconciliation break", StringComparison.OrdinalIgnoreCase));
        cashLane.EvidenceLinks.Should().Contain(link =>
            link.EvidenceId == "assignment-note" &&
            link.Label == "Controller assignment note");
        assigned.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Exception management evidence" &&
            package.Status == EvidenceStatusDto.Blocked &&
            package.CompleteCategoryCount == 2 &&
            package.RequiredCategoryCount == 3 &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "assignment-note") &&
            package.RequiredActions.Any(action =>
                action.Contains("Review Level 2 escalation", StringComparison.OrdinalIgnoreCase) &&
                action.Contains("Cash break aged beyond SLA", StringComparison.OrdinalIgnoreCase)));
        assigned.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Reconciliation coverage evidence" &&
            package.Status == EvidenceStatusDto.Blocked &&
            package.CompleteCategoryCount == 5 &&
            package.RequiredCategoryCount == 7 &&
            package.Summary.Contains("2 reconciliation lane", StringComparison.OrdinalIgnoreCase) &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "assignment-note") &&
            package.RequiredActions.Any(action =>
                action.Contains("Review Level 2 escalation", StringComparison.OrdinalIgnoreCase) &&
                action.Contains("Cash break aged beyond SLA", StringComparison.OrdinalIgnoreCase)));

        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count + 1);
        var audit = timelineAfter.Last();
        audit.EventType.Should().Be("reconciliation-break-escalated");
        audit.Actor.Should().Be("ops-user");
        audit.Rationale.Should().Be("Cash break aged beyond SLA");
        audit.CorrelationId.Should().Be("corr-break-assign");
        audit.References.Should().ContainSingle(link => link.EvidenceId == "assignment-note");
    }

    [Fact]
    public async Task AssignBreakCaseAsync_ShouldRetainBlockedAlreadyClosedBreakAttempt()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-closed-assignment")]));
        var resolved = await service.ResolveBreakCaseAsync(
            workflow.WorkflowId,
            "break-closed-assignment",
            new OperationsResolveBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                ResolutionStatus: "Resolved",
                Rationale: "Controller retained resolution evidence before assignment could continue.",
                EvidenceLinks: [CreateEvidenceLink("break-closed-assignment-resolution", "Resolution evidence")]));
        var timelineBeforeDuplicate = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var duplicate = await service.AssignBreakCaseAsync(
            workflow.WorkflowId,
            "break-closed-assignment",
            new OperationsAssignBreakCaseRequestDto(
                resolved.Workflow!.Version,
                "ops-user",
                Owner: "fund-controller",
                Rationale: "Controller attempted to reassign the closed break.",
                EscalationLevel: "Level 2",
                EscalationReason: "Duplicate assignment attempt.",
                EvidenceLinks: [CreateEvidenceLink("break-closed-assignment-duplicate", "Duplicate assignment evidence")]));

        duplicate.Success.Should().BeFalse();
        duplicate.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        duplicate.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "RECONCILIATION_BREAK_ALREADY_CLOSED" &&
            blocker.Gate == OperationsGateKeyDto.Reconciliation);
        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        persisted!.Version.Should().Be(resolved.Workflow.Version);
        var breakCase = persisted.BreakCases.Single(static item => item.BreakId == "break-closed-assignment");
        breakCase.Status.Should().Be("Resolved");
        breakCase.Owner.Should().Be("ops-user");
        breakCase.Owner.Should().NotBe("fund-controller");
        breakCase.EscalationLevel.Should().BeNull();
        breakCase.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "break-closed-assignment-resolution");
        breakCase.EvidenceLinks.Should().NotContain(link => link.EvidenceId == "break-closed-assignment-duplicate");
        var timelineAfterDuplicate = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        AssertBlockedAttemptPersisted(timelineBeforeDuplicate, timelineAfterDuplicate, duplicate);
    }

    [Fact]
    public async Task AssignBreakCaseAsync_ShouldRejectAssistantOriginBeforeMutation()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-assistant-assign")]));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var result = await service.AssignBreakCaseAsync(
            workflow.WorkflowId,
            "break-assistant-assign",
            new OperationsAssignBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "assistant-agent",
                Owner: "fund-controller",
                Rationale: "Assistant attempted break assignment.",
                EscalationLevel: "Level 2",
                EscalationReason: "Assistant attempted escalation.",
                DueDate: new DateOnly(2026, 6, 2),
                EvidenceLinks: [CreateEvidenceLink("break-assistant-assignment-evidence", "Assistant draft assignment evidence")],
                ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        AssertAutomationMaterialActionRejected(result, OperationsGateKeyDto.Reconciliation);
        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        var breakCase = persisted!.BreakCases.Single(static item => item.BreakId == "break-assistant-assign");
        breakCase.Owner.Should().BeNull();
        breakCase.Status.Should().Be("Open");
        breakCase.EscalationLevel.Should().BeNull();
        breakCase.EscalationReason.Should().BeNull();
        breakCase.DueDate.Should().BeNull();

        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        timelineAfter.Should().HaveCount(timelineBefore.Count);
    }

    [Fact]
    public async Task AssignBreakCaseAsync_ShouldRetainBlockedMissingOwnerAttempt()
    {
        var service = CreateService(out var repository, out var auditStore);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            BreakCases: [CreateOpenCriticalBreak(workflow, "break-owner-required")]));
        var timelineBefore = await auditStore.GetTimelineAsync(workflow.WorkflowId);

        var rejected = await service.AssignBreakCaseAsync(
            workflow.WorkflowId,
            "break-owner-required",
            new OperationsAssignBreakCaseRequestDto(
                reconciled.Workflow!.Version,
                "ops-user",
                Owner: " ",
                Rationale: "Assignment must name the accountable owner"));

        rejected.Success.Should().BeFalse();
        rejected.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        rejected.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "RECONCILIATION_BREAK_OWNER_REQUIRED" &&
            blocker.Gate == OperationsGateKeyDto.Reconciliation);

        var persisted = await repository.GetAsync(workflow.WorkflowId);
        persisted.Should().NotBeNull();
        persisted!.Version.Should().Be(reconciled.Workflow!.Version);
        var breakCase = persisted.BreakCases.Single(item => item.BreakId == "break-owner-required");
        breakCase.Owner.Should().BeNull();
        breakCase.Status.Should().Be("Open");

        var timelineAfter = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, rejected);
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
    public async Task RunReconciliationAsync_ShouldRetainFinancialOperationsReconciliationLaneCoverage()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);
        var runEvidence = new OperationsEvidenceLinkDto(
            "reconciliation-run:finops-lanes",
            "Financial operations reconciliation run",
            "/workstation/accounting/reconciliation/runs/finops-lanes",
            "reconciliation-run",
            DateTimeOffset.UtcNow);
        var laneExpectations = new Dictionary<string, (string EvidenceId, OperationsReconciliationLaneStatusDto Status)>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["cash-reconciliation"] = ("evidence:cash", OperationsReconciliationLaneStatusDto.ReviewRequired),
            ["position-reconciliation"] = ("evidence:position", OperationsReconciliationLaneStatusDto.ReviewRequired),
            ["trade-reconciliation"] = ("evidence:trade", OperationsReconciliationLaneStatusDto.ReviewRequired),
            ["income-reconciliation"] = ("evidence:income", OperationsReconciliationLaneStatusDto.ReviewRequired),
            ["mbs-factor-reconciliation"] = ("evidence:mbs-factor", OperationsReconciliationLaneStatusDto.Blocked),
            ["bank-reconciliation"] = ("evidence:bank", OperationsReconciliationLaneStatusDto.ReviewRequired),
            ["gl-reconciliation"] = ("evidence:gl", OperationsReconciliationLaneStatusDto.ReviewRequired)
        };

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "ops-user",
            "Ran Financial Operations lane reconciliation",
            BreakCases:
            [
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "cash-reconciliation",
                    "CASH_BALANCE_VARIANCE",
                    "Cash",
                    "High",
                    "ledger cash activity",
                    "custodian cash activity",
                    "CASH",
                    LaneEvidence("cash", "Cash reconciliation evidence"),
                    "Assign cash reconciliation variance to fund operations."),
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "position-reconciliation",
                    "POSITION_QUANTITY_VARIANCE",
                    "Position",
                    "Medium",
                    "portfolio positions",
                    "custodian positions",
                    "BOND1",
                    LaneEvidence("position", "Position reconciliation evidence"),
                    "Review position quantity variance."),
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "trade-reconciliation",
                    "TRADE_FILL_MISMATCH",
                    "Trade",
                    "Medium",
                    "expected trade allocations",
                    "broker fills",
                    "EQTY1",
                    LaneEvidence("trade", "Trade reconciliation evidence"),
                    "Match trade allocation to broker fill evidence."),
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "income-reconciliation",
                    "ACCRUAL_AMOUNT_MISMATCH",
                    "Income",
                    "High",
                    "expected coupon accrual",
                    "custodian income activity",
                    "BONDINC",
                    LaneEvidence("income", "Income reconciliation evidence"),
                    "Review income accrual support before close.",
                    rootCauseCode: "coupon-accrual-break",
                    blockedOutputs: ["income statement support"]),
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "mbs-factor-reconciliation",
                    "FACTOR_STALE",
                    "MBS factor",
                    "Critical",
                    "trustee factor schedule",
                    "custodian factor feed",
                    "MBS1",
                    LaneEvidence("mbs-factor", "MBS factor reconciliation evidence"),
                    "Resolve MBS factor schedule before close.",
                    rootCauseCode: "factor-schedule-stale",
                    blockedOutputs: ["MBS factor roll-forward"]),
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "bank-reconciliation",
                    "EXTERNAL_STATEMENT_VARIANCE",
                    "Bank",
                    "High",
                    "external statement",
                    "bank record",
                    "BANKUSD",
                    LaneEvidence("bank", "Bank reconciliation evidence"),
                    "Tie external statement evidence to operating record."),
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "gl-reconciliation",
                    "GL_JOURNAL_CONTROL_VARIANCE",
                    "General ledger",
                    "High",
                    "general ledger control total",
                    "journal summary",
                    "GL",
                    LaneEvidence("gl", "GL reconciliation evidence"),
                    "Tie GL control total to retained journal summary.")
            ],
            EvidenceLinks: [runEvidence]));

        reconciled.Success.Should().BeTrue();
        reconciled.Workflow!.ReconciliationLanes.Should().HaveCount(7);
        foreach (var (laneId, expectation) in laneExpectations)
        {
            var lane = reconciled.Workflow.ReconciliationLanes.Should()
                .ContainSingle(item => item.LaneId == laneId)
                .Subject;
            lane.Status.Should().Be(expectation.Status);
            lane.IsReady.Should().BeFalse();
            lane.BreakCount.Should().Be(1);
            lane.EvidenceLinks.Should().Contain(link => link.EvidenceId == expectation.EvidenceId);
            lane.RequiredActions.Should().NotBeEmpty();
        }

        var financialCashLane = reconciled.Workflow.ReconciliationLanes.Should()
            .ContainSingle(lane => lane.LaneId == "cash-reconciliation")
            .Subject;
        financialCashLane.RequiredActions.Should().Contain("Assign cash reconciliation variance to fund operations.");
        financialCashLane.RequiredActions.Should().Contain("Assign 1 cash reconciliation break to an accountable owner.");

        var financialIncomeLane = reconciled.Workflow.ReconciliationLanes.Should()
            .ContainSingle(lane => lane.LaneId == "income-reconciliation")
            .Subject;
        financialIncomeLane.RequiredActions.Should().Contain("Review income accrual support before close.");
        financialIncomeLane.RequiredActions.Should().Contain(action =>
            action.Contains("income statement support", StringComparison.OrdinalIgnoreCase));

        var financialMbsLane = reconciled.Workflow.ReconciliationLanes.Should()
            .ContainSingle(lane => lane.LaneId == "mbs-factor-reconciliation")
            .Subject;
        financialMbsLane.RequiredActions.Should().Contain("Resolve MBS factor schedule before close.");
        financialMbsLane.RequiredActions.Should().Contain(action =>
            action.Contains("MBS factor roll-forward", StringComparison.OrdinalIgnoreCase));

        reconciled.Workflow.ReconciliationLanes.SelectMany(static lane => lane.EvidenceLinks)
            .Should()
            .Contain(link => link.EvidenceId == "evidence:income" && link.Label == "Income reconciliation evidence")
            .And
            .Contain(link => link.EvidenceId == "evidence:mbs-factor" && link.Label == "MBS factor reconciliation evidence");
        reconciled.Workflow.EvidencePackages.Should().Contain(package =>
            package.PackageId == $"reconciliation-coverage:{workflow.FundAccountId:D}:{workflow.PeriodId}" &&
            package.Label == "Reconciliation coverage evidence" &&
            package.Status == EvidenceStatusDto.Blocked &&
            package.CompleteCategoryCount == 0 &&
            package.RequiredCategoryCount == 7 &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "reconciliation-run:finops-lanes") &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "evidence:cash") &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "evidence:income") &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "evidence:mbs-factor") &&
            package.RequiredActions.Contains("Resolve MBS factor schedule before close."));

        static OperationsEvidenceLinkDto LaneEvidence(string laneId, string label) =>
            new(
                $"evidence:{laneId}",
                label,
                $"/workstation/accounting/reconciliation/{laneId}",
                "reconciliation-run",
                DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RunReconciliationAsync_ShouldClassifyPaymentConfirmationEvidenceBreaksAsBankReconciliation()
    {
        var service = CreateService(out _, out _);
        var workflow = await CreateLedgerPostedWorkflowAsync(service);

        var reconciled = await service.RunReconciliationAsync(workflow.WorkflowId, new OperationsReconciliationRunRequestDto(
            workflow.Version,
            "cash-ops",
            "Reviewed retained payment confirmation evidence for bank reconciliation",
            BreakCases:
            [
                CreateFinancialOperationsLaneBreak(
                    workflow,
                    "payment-confirmation",
                    "PAYMENT_CONFIRMATION_MISSING",
                    "Payment confirmation",
                    "High",
                    "approved payment intent",
                    "retained confirmation record missing",
                    "PAYMENT",
                    CreateEvidenceLink("evidence:payment-confirmation", "Payment confirmation evidence"),
                    "Retain payment confirmation and return evidence for the approved payment intent.",
                    rootCauseCode: "payment-confirmation-evidence-missing",
                    blockedOutputs: ["Bank reconciliation support"])
            ]));

        reconciled.Success.Should().BeTrue();
        var bankLane = reconciled.Workflow!.ReconciliationLanes.Should()
            .ContainSingle(lane => lane.LaneId == "bank-reconciliation")
            .Subject;
        bankLane.Status.Should().Be(OperationsReconciliationLaneStatusDto.ReviewRequired);
        bankLane.IsReady.Should().BeFalse();
        bankLane.BreakCount.Should().Be(1);
        bankLane.EvidenceLinks.Should().Contain(link =>
            link.EvidenceId == "evidence:payment-confirmation" &&
            link.Label == "Payment confirmation evidence");
        bankLane.RequiredActions.Should().Contain(action =>
            action.Contains("Retain payment confirmation and return evidence", StringComparison.OrdinalIgnoreCase));
        bankLane.RequiredActions.Should().Contain(action =>
            action.Contains("Bank reconciliation support", StringComparison.OrdinalIgnoreCase));

        var matchRecordsMetric = reconciled.Workflow.DashboardSummary!.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "match-records")
            .Subject;
        matchRecordsMetric.RequiredActions.Should().Contain(action =>
            action.Contains("Retain payment confirmation and return evidence", StringComparison.OrdinalIgnoreCase));
        matchRecordsMetric.RequiredActions.Should().Contain(action =>
            action.Contains("Bank reconciliation support", StringComparison.OrdinalIgnoreCase));
        matchRecordsMetric.RequiredActions.Should()
            .NotContain("Complete source-backed reconciliation lanes before approval.");
        reconciled.Workflow.DashboardSummary.RequiredActions.Should().Contain(action =>
            action.Contains("Retain payment confirmation and return evidence", StringComparison.OrdinalIgnoreCase));

        var resolveExceptionsMetric = reconciled.Workflow.DashboardSummary.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "resolve-exceptions")
            .Subject;
        resolveExceptionsMetric.RequiredActions.Should().Contain(action =>
            action.Contains("Retain payment confirmation and return evidence", StringComparison.OrdinalIgnoreCase));
        resolveExceptionsMetric.RequiredActions.Should().Contain(action =>
            action.Contains("Bank reconciliation support", StringComparison.OrdinalIgnoreCase));
        resolveExceptionsMetric.RequiredActions.Should()
            .NotContain("Assign, escalate, or resolve open exceptions and retain resolution evidence.");

        reconciled.Workflow.ReconciliationLanes.Should()
            .ContainSingle(lane => lane.LaneId == "cash-reconciliation" && lane.BreakCount == 0);
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldRetainBlockedMissingChecklistControlApprovalsAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, close);
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldComputeClosePackageEvidenceHashServerSide()
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
        var callerSuppliedHash = new string('a', 64);

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow with caller-supplied hash",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            ClosePackageEvidenceHash: callerSuppliedHash));

        close.Success.Should().BeTrue();
        close.Workflow!.ClosePackage.Should().NotBeNull();
        close.Workflow.ClosePackage!.EvidenceHash.Should().MatchRegex("^[a-f0-9]{64}$");
        close.Workflow.ClosePackage.EvidenceHash.Should().NotBe(callerSuppliedHash);
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldFreezeVaultDocumentSnapshotsInClosePackageManifest()
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
        var closeDocument = CreateCloseVaultDocument(
            workflow.PeriodId,
            workflow.FundAccountId.ToString("D"),
            "evidence-doc-close-binder-1",
            "0e5751c026e543b2e8ab2eb06099daa1a8e2e3566cf9ca71972c1d0a12d8df43");
        var manifestSnapshot = CreateCloseManifestSnapshot(workflow, closeDocument);

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow with vault binder support",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            DocumentSnapshots: [closeDocument],
            ManifestSnapshot: manifestSnapshot));

        close.Success.Should().BeTrue();
        close.Workflow!.ClosePackage.Should().NotBeNull();
        close.Workflow.ClosePackage!.DocumentSnapshots.Should().ContainSingle(document =>
            document.DocumentId == closeDocument.DocumentId &&
            document.SourceHashSha256 == closeDocument.SourceHashSha256 &&
            document.Classification == EvidenceDocumentClassificationDto.BankEvidence &&
            document.ObjectLinks.Any(link =>
                link.LinkKind == EvidenceDocumentLinkKindDto.CloseTask &&
                link.ObjectId == "close-gate-approval"));
        close.Workflow.ClosePackage.EvidenceLinks.Should().Contain(link =>
            link.EvidenceId == closeDocument.DocumentId &&
            link.Route == closeDocument.ManifestRoute &&
            link.Source == "upload");
        close.Workflow.ClosePackage.ManifestSnapshot.Should().NotBeNull();
        close.Workflow.ClosePackage.ManifestSnapshot!.ManifestId.Should().Be(manifestSnapshot.ManifestId);
        close.Workflow.ClosePackage.ManifestSnapshot.ContentHashSha256.Should().Be(manifestSnapshot.ContentHashSha256);
        close.Workflow.ClosePackage.ManifestSnapshot.Documents.Should().ContainSingle(document =>
            document.DocumentId == closeDocument.DocumentId &&
            document.SourceHashSha256 == closeDocument.SourceHashSha256);
        close.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Close package manifest" &&
            package.EvidenceLinkCount >= 2 &&
            package.EvidenceLinks.Any(link => link.EvidenceId == closeDocument.DocumentId));
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldIncludeFrozenVaultDocumentHashInServerEvidenceHash()
    {
        var firstHash = await CloseWorkflowWithDocumentHashAsync(
            "0e5751c026e543b2e8ab2eb06099daa1a8e2e3566cf9ca71972c1d0a12d8df43");
        var secondHash = await CloseWorkflowWithDocumentHashAsync(
            "a1124d8d32d04453b273bf2ea3cb49fbef5d0e49f7a8e11dfd5eb5eb1738171d");

        firstHash.Should().MatchRegex("^[a-f0-9]{64}$");
        secondHash.Should().MatchRegex("^[a-f0-9]{64}$");
        secondHash.Should().NotBe(firstHash);
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldIncludeFrozenManifestHashInServerEvidenceHash()
    {
        var firstHash = await CloseWorkflowWithManifestHashAsync(new string('c', 64));
        var secondHash = await CloseWorkflowWithManifestHashAsync(new string('d', 64));

        firstHash.Should().MatchRegex("^[a-f0-9]{64}$");
        secondHash.Should().MatchRegex("^[a-f0-9]{64}$");
        secondHash.Should().NotBe(firstHash);
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
    }

    [Fact]
    public async Task ReopenWorkflowAsync_ShouldRequireGovernedIncidentMetadata()
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
        reopened.Workflow.EvidencePackages.Should().Contain(package =>
            package.Label == "Period lock and reopen evidence" &&
            package.Status == EvidenceStatusDto.ReviewRequired &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "INC-123") &&
            package.EvidenceLinks.Any(link => link.EvidenceId == "approval-ref-123") &&
            package.RequiredActions.Contains("Complete reopened incident remediation and close the period again with retained evidence."));
        var reopenedProduceEvidenceMetric = reopened.Workflow.DashboardSummary!.Metrics.Should()
            .ContainSingle(metric => metric.MetricId == "produce-evidence")
            .Subject;
        reopenedProduceEvidenceMetric.Value.Should().Be("Reopened period lock pending");
        reopenedProduceEvidenceMetric.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        reopenedProduceEvidenceMetric.RequiredActions.Should()
            .Contain("Complete reopened incident remediation and close the period again with retained evidence.");
        reopenedProduceEvidenceMetric.RequiredActions.Should()
            .NotBeEmpty("a reopened workflow retains the prior close package but still needs a new period-lock package before release");
        var timeline = await auditStore.GetTimelineAsync(workflow.WorkflowId);
        var reopenAudit = timeline.Should().ContainSingle(entry => entry.EventType == "workflow-reopened").Subject;
        reopenAudit.Rationale.Should().Contain("Rationale: Incident requires reopened reconciliation");
        reopenAudit.Rationale.Should().Contain("Justification: Controller approved reopening the closed period.");
        reopenAudit.Rationale.Should().Contain("Approval reference: approval-ref-123");
        reopenAudit.Rationale.Should().Contain("Impact summary: Reopens reconciliation gate for incident follow-up.");
        reopenAudit.References.Should().Contain(link =>
            link.EvidenceId == "INC-123" &&
            link.Source == "incident");
        reopenAudit.References.Should().Contain(link =>
            link.EvidenceId == "approval-ref-123" &&
            link.Source == "approval-reference");
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
        close.Blockers.Should().Contain(blocker => blocker.Code == "RECONCILIATION_CRITICAL_BREAKS_OPEN");
        close.Blockers.Should().Contain(blocker => blocker.Code == "APPROVAL_REQUIRED");
    }

    [Fact]
    public async Task CloseWorkflowAsync_ShouldRetainBlockedMismatchedReadyReportPackAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, close);
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
    public async Task RefreshGatePostureAsync_ShouldRetainBlockedClosedWorkflowAttempt()
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
        AssertBlockedAttemptPersisted(timelineBefore, timelineAfter, result);
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
    public async Task FileWorkflowStart_ShouldRestoreStateAndGenesisAuditFromOneAtomicEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var derivation = new OperationsStatusDerivationService();
        var repository = new FileOperationsContinuityRepository(
            root,
            derivation,
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var auditStore = new FileOperationsWorkflowAuditStore(
            root,
            NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);

        var started = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        started.Success.Should().BeTrue();
        var workflowId = started.Workflow!.WorkflowId;
        var envelopePath = Path.Combine(
            root,
            "operations-continuity",
            "transition-commits",
            $"{workflowId:N}.json");
        File.Exists(envelopePath).Should().BeTrue();
        File.Exists(Path.Combine(root, "operations-continuity", "workflows", $"{workflowId:N}.json"))
            .Should().BeFalse("the start snapshot is committed with its genesis audit event");
        File.Exists(Path.Combine(root, "operations-continuity", "audit", $"{workflowId:N}.jsonl"))
            .Should().BeFalse("the genesis audit event is committed with its start snapshot");

        var restartedRepository = new FileOperationsContinuityRepository(
            root,
            derivation,
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var restartedAuditStore = new FileOperationsWorkflowAuditStore(
            root,
            NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var restored = await restartedRepository.GetAsync(workflowId);
        var timeline = await restartedAuditStore.GetTimelineAsync(workflowId);

        restored.Should().NotBeNull();
        restored!.Version.Should().Be(started.Workflow.Version);
        timeline.Should().ContainSingle(entry => entry.EventType == "workflow-started");
        OperationsWorkflowAuditHashing.TryValidateChain(timeline, out _, out _).Should().BeTrue();
    }

    [Fact]
    public async Task FileEnvelopeReads_ShouldFailClosedWhenFilenameAndWorkflowIdentityDoNotMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var derivation = new OperationsStatusDerivationService();
        var repository = new FileOperationsContinuityRepository(
            root,
            derivation,
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var auditStore = new FileOperationsWorkflowAuditStore(
            root,
            NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var started = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));
        var originalWorkflowId = started.Workflow!.WorkflowId;
        var wrongWorkflowId = Guid.NewGuid();
        var envelopeDirectory = Path.Combine(root, "operations-continuity", "transition-commits");
        var originalPath = Path.Combine(envelopeDirectory, $"{originalWorkflowId:N}.json");
        var wrongPath = Path.Combine(envelopeDirectory, $"{wrongWorkflowId:N}.json");
        File.Move(originalPath, wrongPath);

        var getAct = () => repository.GetAsync(wrongWorkflowId);
        var listAct = () => repository.ListAsync();
        var timelineAct = () => auditStore.GetTimelineAsync(wrongWorkflowId);

        await getAct.Should().ThrowAsync<InvalidDataException>()
            .WithMessage($"*{wrongPath}*does not match workflow*");
        await listAct.Should().ThrowAsync<InvalidDataException>()
            .WithMessage($"*{wrongPath}*does not match workflow*");
        await timelineAct.Should().ThrowAsync<InvalidDataException>()
            .WithMessage($"*{wrongPath}*does not match workflow*");
    }

    [Fact]
    public async Task FileWorkflowStart_WhenAtomicEnvelopeWriteFails_ShouldLeaveNoOrphanStateOrAudit()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var derivation = new OperationsStatusDerivationService();
        var repository = new FileOperationsContinuityRepository(
            root,
            derivation,
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var auditStore = new FileOperationsWorkflowAuditStore(
            root,
            NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var transitionCommitPath = Path.Combine(root, "operations-continuity", "transition-commits");
        await File.WriteAllTextAsync(transitionCommitPath, "blocks-directory-creation");

        var act = () => service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        await act.Should().ThrowAsync<IOException>();
        Directory.EnumerateFiles(Path.Combine(root, "operations-continuity", "workflows"))
            .Should().BeEmpty();
        Directory.EnumerateFiles(Path.Combine(root, "operations-continuity", "audit"))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task FileRepository_ListAsync_ShouldFailClosedOnCorruptLegacyWorkflowSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var repository = new FileOperationsContinuityRepository(
            root,
            new OperationsStatusDerivationService(),
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var workflowId = Guid.NewGuid();
        var snapshotPath = Path.Combine(
            root,
            "operations-continuity",
            "workflows",
            $"{workflowId:N}.json");
        await File.WriteAllTextAsync(snapshotPath, "{ not-valid-json");

        var act = () => repository.ListAsync();

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage($"*{snapshotPath}*corrupt*");
    }

    [Fact]
    public async Task FileTransitionCommit_ShouldRestoreWorkflowAndOutcomeAuditFromOneAtomicEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "operations-continuity", Guid.NewGuid().ToString("N"));
        var derivation = new OperationsStatusDerivationService();
        var repository = new FileOperationsContinuityRepository(
            root,
            derivation,
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var auditStore = new FileOperationsWorkflowAuditStore(
            root,
            NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var service = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var start = await service.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "bank",
            "ops-user"));

        var committed = await service.RefreshGatePostureAsync(
            start.Workflow!.WorkflowId,
            new OperationsGatePostureRequestDto(
                start.Workflow.Version,
                "ops-user",
                CorrelationId: "file-transition-commit-1",
                ProviderAccountLinked: true));

        committed.Success.Should().BeTrue();
        var envelopePath = Path.Combine(
            root,
            "operations-continuity",
            "transition-commits",
            $"{start.Workflow.WorkflowId:N}.json");
        File.Exists(envelopePath).Should().BeTrue();

        var restartedRepository = new FileOperationsContinuityRepository(
            root,
            derivation,
            NullLogger<FileOperationsContinuityRepository>.Instance);
        var restartedAuditStore = new FileOperationsWorkflowAuditStore(
            root,
            NullLogger<FileOperationsWorkflowAuditStore>.Instance);
        var restored = await restartedRepository.GetAsync(start.Workflow.WorkflowId);
        var restoredTimeline = await restartedAuditStore.GetTimelineAsync(start.Workflow.WorkflowId);

        restored!.Version.Should().Be(committed.Workflow!.Version);
        restoredTimeline.Should().ContainSingle(entry =>
            entry.EventType == "gate-posture-refreshed" &&
            entry.Outcome!.State == OperationTerminalState.Succeeded);
        OperationsWorkflowAuditHashing.TryValidateChain(restoredTimeline, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void PostgresAuditAppend_ShouldAcquireWorkflowScopedAdvisoryLockBeforeReadingTailHash()
    {
        var source = ReadRepoFile(
            "src",
            "Meridian.FinancialOperations",
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

    [Fact]
    public void PostgresTimelineRead_ShouldValidateAuditHashChainBeforeReturning()
    {
        var source = ReadRepoFile(
            "src",
            "Meridian.FinancialOperations",
            "OperationsContinuity",
            "PostgresOperationsContinuityStore.cs");
        var methodStart = source.IndexOf(
            "public async Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync",
            StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf(
            "public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowStartAsync",
            methodStart,
            StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        nextMethodStart.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..nextMethodStart];
        method.Should().Contain("OperationsWorkflowAuditHashing.TryValidateChain");
        method.Should().Contain("throw new InvalidDataException");
        method.IndexOf("TryValidateChain", StringComparison.Ordinal)
            .Should().BeLessThan(method.LastIndexOf("return results;", StringComparison.Ordinal));
    }

    private static OperationsContinuityWorkflowService CreateService(
        out InMemoryOperationsContinuityRepository repository,
        out InMemoryOperationsWorkflowAuditStore auditStore,
        RecordingLedgerJournalStore? ledgerJournalStore = null,
        bool registerLedgerJournalStore = true,
        IReadOnlyDictionary<Guid, SecurityStatusDto>? securityStatuses = null)
    {
        var derivation = new OperationsStatusDerivationService();
        repository = new InMemoryOperationsContinuityRepository(derivation);
        auditStore = new InMemoryOperationsWorkflowAuditStore();
        return new OperationsContinuityWorkflowService(
            repository,
            auditStore,
            derivation,
            registerLedgerJournalStore ? ledgerJournalStore ?? new RecordingLedgerJournalStore() : null,
            securityMasterQueryService: new StaticSecurityMasterQueryService(securityStatuses ?? DefaultAuthoritativeSecurityStatuses()));
    }

    private static IReadOnlyDictionary<Guid, SecurityStatusDto> DefaultAuthoritativeSecurityStatuses() =>
        new Dictionary<Guid, SecurityStatusDto>
        {
            [Guid.Parse("BCE42470-8F6B-4BD3-9FC7-B8763F8B48B1")] = SecurityStatusDto.Active
        };

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> RequiredChecklistControlApprovals() =>
    [
        new("close-gate-brokeringest", "operations-lead", new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)),
        new("close-gate-securitymaster", "security-master-lead", new DateTimeOffset(2026, 5, 31, 12, 1, 0, TimeSpan.Zero)),
        new("close-gate-ledgerposting", "ledger-lead", new DateTimeOffset(2026, 5, 31, 12, 2, 0, TimeSpan.Zero)),
        new("close-gate-reconciliation", "reconciliation-lead", new DateTimeOffset(2026, 5, 31, 12, 3, 0, TimeSpan.Zero)),
        new("close-gate-approval", "controller", new DateTimeOffset(2026, 5, 31, 12, 4, 0, TimeSpan.Zero)),
        new("close-gate-approval", "fund-admin", new DateTimeOffset(2026, 5, 31, 12, 5, 0, TimeSpan.Zero))
    ];

    private static OperationsBreakCaseDto CreateOpenCriticalBreak(OperationsContinuityWorkflowDto workflow, string breakId) =>
        new(
            breakId,
            "cash-position-match",
            "Cash",
            "Critical",
            "Open",
            null,
            null,
            "Ledger cash",
            "Custodian cash",
            100m,
            null,
            100m,
            null,
            "CASH",
            "Assign an accountable owner",
            [],
            new OperationsContinuityCorrelationKeysDto(
                RunId: $"run-{breakId}",
                FundAccountId: workflow.FundAccountId,
                LedgerBatchId: "ledger-batch-1",
                ReconciliationCaseId: breakId));

    private static OperationsEvidenceLinkDto CreateEvidenceLink(string evidenceId, string label) =>
        new(
            evidenceId,
            label,
            Route: $"/evidence/{evidenceId}",
            Source: "operations-test",
            CapturedAtUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

    private static EvidenceDocumentDto CreateCloseVaultDocument(
        string periodId,
        string fundAccountId,
        string documentId,
        string sourceHashSha256) =>
        new(
            documentId,
            "close-binder-bank-support.pdf",
            EvidenceDocumentClassificationDto.BankEvidence,
            sourceHashSha256,
            new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
            "upload",
            "ops-user",
            "tenant-alpha",
            "fund-alpha",
            EvidenceExtractionStatusDto.Accepted,
            [
                new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Period, periodId, "Close period"),
                new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Portfolio, fundAccountId, "Fund account"),
                new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.CloseTask, "close-gate-approval", "Approval close task"),
                new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Journal, "ledger-batch-1", "Ledger batch")
            ],
            new EvidenceDocumentReviewStateDto(
                EvidenceDocumentReviewStatusDto.Accepted,
                "controller",
                new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                "Accepted for close binder."),
            [
                new EvidenceDocumentAuditEventDto(
                    new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
                    "ops-user",
                    "accepted",
                    "Vault document accepted for close binder.",
                    "corr-close-doc")
            ])
        {
            ContentType = "application/pdf",
            VaultId = "vault-close-2026-05",
            ArtifactId = "artifact-close-binder-bank-support",
            ManifestRoute = "/api/workstation/evidence/vault/vault-close-2026-05/manifest",
            ExtractorId = "manual-metadata-v1"
        };

    private static EvidenceManifestDto CreateCloseManifestSnapshot(
        OperationsContinuityWorkflowDto workflow,
        EvidenceDocumentDto closeDocument,
        string? contentHashSha256 = null) =>
        new(
            ManifestId: $"manifest:{workflow.WorkflowId:D}:close",
            FrozenAt: new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero),
            PackageKind: "close-package",
            PackageId: $"close-package:{workflow.WorkflowId:D}",
            ContentHashSha256: contentHashSha256 ?? new string('c', 64),
            Documents: [closeDocument],
            Requests: [],
            ObjectLinks: closeDocument.ObjectLinks);

    private static async Task<string> CloseWorkflowWithDocumentHashAsync(string sourceHashSha256)
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
        var closeDocument = CreateCloseVaultDocument(
            workflow.PeriodId,
            workflow.FundAccountId.ToString("D"),
            "evidence-doc-close-binder-1",
            sourceHashSha256);

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow with vault binder support",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            ClosePackageEvidenceHash: new string('a', 64),
            DocumentSnapshots: [closeDocument]));

        close.Success.Should().BeTrue();
        return close.Workflow!.ClosePackage!.EvidenceHash;
    }

    private static async Task<string> CloseWorkflowWithManifestHashAsync(string manifestHashSha256)
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
        var closeDocument = CreateCloseVaultDocument(
            workflow.PeriodId,
            workflow.FundAccountId.ToString("D"),
            "evidence-doc-close-binder-1",
            "0e5751c026e543b2e8ab2eb06099daa1a8e2e3566cf9ca71972c1d0a12d8df43");
        var manifestSnapshot = CreateCloseManifestSnapshot(workflow, closeDocument, manifestHashSha256);

        var close = await service.CloseWorkflowAsync(workflow.WorkflowId, new OperationsCloseWorkflowRequestDto(
            approved.Workflow!.Version,
            "ops-user",
            "Close workflow with vault manifest support",
            "report-pack-1",
            ChecklistControlApprovals: RequiredChecklistControlApprovals(),
            ClosePackageEvidenceHash: new string('a', 64),
            DocumentSnapshots: [closeDocument],
            ManifestSnapshot: manifestSnapshot));

        close.Success.Should().BeTrue();
        return close.Workflow!.ClosePackage!.EvidenceHash;
    }

    private static OperationsBreakCaseDto CreateFinancialOperationsLaneBreak(
        OperationsContinuityWorkflowDto workflow,
        string laneId,
        string checkId,
        string category,
        string severity,
        string expectedSource,
        string actualSource,
        string symbol,
        OperationsEvidenceLinkDto evidenceLink,
        string suggestedAction,
        string? rootCauseCode = null,
        IReadOnlyList<string>? blockedOutputs = null) =>
        new(
            $"break:{laneId}",
            checkId,
            category,
            severity,
            "Open",
            Owner: null,
            DueDate: null,
            ExpectedSource: expectedSource,
            ActualSource: actualSource,
            ExpectedAmount: 100m,
            ActualAmount: 90m,
            Variance: -10m,
            SecurityId: null,
            Symbol: symbol,
            SuggestedAction: suggestedAction,
            EvidenceLinks: [evidenceLink],
            CorrelationKeys: new OperationsContinuityCorrelationKeysDto(
                RunId: "finops-lanes",
                FundAccountId: workflow.FundAccountId,
                LedgerBatchId: "ledger-batch-1",
                ReconciliationCaseId: $"case:{laneId}"),
            RootCauseCode: rootCauseCode,
            BlockedOutputs: blockedOutputs);

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

    private static void AssertAutomationMaterialActionRejected(
        OperationsTransitionResultDto result,
        OperationsGateKeyDto expectedGate)
    {
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("REVIEWED_AUTOMATION_REVIEW_REQUIRED");
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Code == "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED" &&
            blocker.Gate == expectedGate &&
            blocker.Message.Contains("requires a human operator origin", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertBlockedAttemptPersisted(
        IReadOnlyList<OperationsWorkflowAuditDto> timelineBefore,
        IReadOnlyList<OperationsWorkflowAuditDto> timelineAfter,
        OperationsTransitionResultDto result)
    {
        timelineAfter.Should().HaveCount(timelineBefore.Count + 1);
        var blockedAudit = timelineAfter.Last();
        blockedAudit.EventType.Should().Be("workflow-transition-blocked");
        blockedAudit.Outcome.Should().NotBeNull();
        blockedAudit.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        blockedAudit.Outcome.Should().BeEquivalentTo(result.Outcome);
        result.Success.Should().BeFalse();
        result.Outcome.State.Should().Be(OperationTerminalState.Blocked);
        result.Outcome.Recovery.Should().NotBeEmpty();
        VerifiedOperationOutcomeValidator.Validate(result.Outcome).Should().BeEmpty();
        OperationsWorkflowAuditHashing.TryValidateChain(timelineAfter, out _, out _).Should().BeTrue();
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
                    Credit: 0m,
                    Dimensions: new LedgerDimensionSetDto(
                        FundId: "fund-alpha",
                        EntityId: "entity-master",
                        CostCenterId: "cash-ops",
                        ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Department"] = "Treasury"
                        })),
                new OperationsLedgerJournalLineDto(
                    EntryId: null,
                    AccountName: "Interest income",
                    AccountType: nameof(LedgerAccountType.Revenue),
                    Debit: 0m,
                    Credit: 100m,
                    Dimensions: new LedgerDimensionSetDto(
                        FundId: "fund-alpha",
                        EntityId: "entity-master",
                        CostCenterId: "income-review",
                        ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Department"] = "Accounting"
                        }))
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
            SecurityMasterProvenance: $"security-master:{securityId:N};snapshot:test-source-hash",
            ExpectedLedgerVersion: 1);
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
            SecurityMasterProvenance: provenance,
            ExpectedLedgerVersion: 1);
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

    private static string ComputeReflectionCanonicalHash(OperationsWorkflowAuditDto audit)
    {
        object hashInput = audit.Outcome is null
            ? new
            {
                audit.AuditId,
                audit.OccurredAtUtc,
                audit.WorkflowId,
                audit.FundAccountId,
                audit.PeriodId,
                audit.EventType,
                audit.FromState,
                audit.ToState,
                audit.Gate,
                audit.FromGateStatus,
                audit.ToGateStatus,
                audit.Actor,
                audit.Rationale,
                audit.CorrelationId,
                audit.CorrelationKeys,
                audit.References,
                audit.PreviousHash
            }
            : new
            {
                audit.AuditId,
                audit.OccurredAtUtc,
                audit.WorkflowId,
                audit.FundAccountId,
                audit.PeriodId,
                audit.EventType,
                audit.FromState,
                audit.ToState,
                audit.Gate,
                audit.FromGateStatus,
                audit.ToGateStatus,
                audit.Actor,
                audit.Rationale,
                audit.CorrelationId,
                audit.CorrelationKeys,
                audit.References,
                Outcome = audit.Outcome,
                audit.PreviousHash
            };
        var canonicalJson = JsonSerializer.Serialize(
            hashInput,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
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


    private sealed class StaticSecurityMasterQueryService(IReadOnlyDictionary<Guid, SecurityStatusDto> statuses)
        : ISecurityMasterQueryService
    {
        private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!statuses.TryGetValue(securityId, out var status))
            {
                return Task.FromResult<SecurityDetailDto?>(null);
            }

            return Task.FromResult<SecurityDetailDto?>(new SecurityDetailDto(
                securityId,
                "Equity",
                status,
                "Apple Inc.",
                "USD",
                EmptyObject,
                EmptyObject,
                [],
                [],
                1,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                null));
        }

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default) =>
            GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null) =>
            Task.FromResult<SecurityDetailDto?>(null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default) =>
            Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<ConvertibleEquityTermsDto?>(null);
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

        public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowTransitionAsync(
            OperationsContinuityWorkflow workflow,
            OperationsWorkflowAuditDraft auditDraft,
            bool persistWorkflowState,
            CancellationToken ct = default)
        {
            var audit = await _auditStore.AppendAsync(auditDraft, ct).ConfigureAwait(false);
            if (persistWorkflowState)
            {
                workflow.Touch(audit.OccurredAtUtc);
                await _repository.SaveAsync(workflow, ct).ConfigureAwait(false);
            }

            return new OperationsContinuityTransactionalCommitResult(workflow, audit);
        }

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

    private sealed class FailingWorkflowTransitionCommitStore(IOperationsWorkflowAuditStore auditStore)
        : IOperationsContinuityTransactionalCommitStore
    {
        public int AcceptedCommitAttempts { get; private set; }

        public int FailureReceiptCommits { get; private set; }

        public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowTransitionAsync(
            OperationsContinuityWorkflow workflow,
            OperationsWorkflowAuditDraft auditDraft,
            bool persistWorkflowState,
            CancellationToken ct = default)
        {
            if (persistWorkflowState)
            {
                AcceptedCommitAttempts++;
                throw new IOException("Simulated atomic workflow persistence failure.");
            }

            FailureReceiptCommits++;
            var audit = await auditStore.AppendAsync(auditDraft, ct).ConfigureAwait(false);
            return new OperationsContinuityTransactionalCommitResult(workflow, audit);
        }

        public Task<OperationsContinuityTransactionalCommitResult> CommitLedgerPostingAsync(
            OperationsContinuityWorkflow workflow,
            OperationsWorkflowAuditDraft auditDraft,
            LedgerJournalEntryWrite journalEntry,
            CancellationToken ct = default) =>
            throw new IOException("Simulated atomic ledger persistence failure.");
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
