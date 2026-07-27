using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards statement casework against missing or conflicting month-end ledger authority so a
/// reviewed case cannot be rebound to another book or accounting period during evidence handoff.
/// </summary>
public sealed class StatementReconciliationCaseworkHandoffTests : IDisposable
{
    private const string DefaultFundProfileId = "fund-profile-statement-authority";
    private static readonly Guid DefaultLedgerBookId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DefaultAccountingPeriodId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DefaultFundStructureNodeId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-casework-handoff-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyAsync_ResolvedStatementCase_ShouldSynchronizeSourceAndAttachOperationsEvidenceOnce()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var import = BuildImport("import-resolved", fundAccountId);
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-resolved");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddHours(-1));
        var item = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Statement support ties to the retained ledger evidence.",
            ResolvedBy = "fund-ops",
            ResolvedAt = now
        };
        var command = BuildCommand(item, ReconciliationCaseworkAction.Resolve);
        var queue = new StaticQueueRepository(item);
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        await operationsRepository.SaveAsync(BuildOperationsWorkflow(fundAccountId, import, now.AddDays(-1)));
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            queue,
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        var first = await service.ApplyAsync(command);
        var replay = await service.ApplyAsync(command);

        first.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        replay.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        (await breakStore.GetAsync(sourceBreak.BreakId))!.Status.Should().Be("Resolved");
        var retainedCase = await caseStore.GetAsync(sourceCase.CaseId);
        retainedCase.Should().NotBeNull();
        retainedCase!.Status.Should().Be("Resolved");
        retainedCase.LastUpdatedBy.Should().Be("fund-ops");
        retainedCase.Disposition.Should().Be("Resolved");
        retainedCase.Resolution.Should().NotBeNull();
        retainedCase.AuditEvents.Count(audit => audit.EventType == "StatementBreakDisposed").Should().Be(1);
        retainedCase.AuditEvents.Single(audit => audit.EventType == "StatementBreakDisposed").Detail
            .Should().Contain($"correlationId={command.CorrelationId}");

        var workflow = (await operations.ListAsync(
                fundAccountId,
                import.AccountingScope!.AccountingPeriodId.ToString("D"),
                ledgerBookId: import.AccountingScope.LedgerBookId))
            .Should().ContainSingle().Which;
        var timeline = await operations.GetTimelineAsync(workflow.WorkflowId);
        timeline.SelectMany(entry => entry.References)
            .Count(reference => reference.Source == "statement-reconciliation-casework")
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_Reopen_ShouldReopenSourceBreakAndRichStatementCase()
    {
        var now = new DateTimeOffset(2026, 7, 26, 13, 0, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var import = BuildImport("import-reopen", fundAccountId);
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-reopen") with { Status = "Resolved" };
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1)) with
        {
            Status = "Resolved",
            Disposition = "Resolved",
            Resolution = new ReconciliationResolutionMetadata(
                "Resolved",
                "Prior resolution.",
                "prior-operator",
                now.AddHours(-2))
        };
        var item = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.Open,
            LifecycleState = ReconciliationCaseLifecycleState.Reopened,
            ReopenedBy = "controller",
            ReopenedAt = now,
            ReopenReason = "New custodian evidence changed the expected amount."
        };
        var command = BuildCommand(item, ReconciliationCaseworkAction.Reopen) with
        {
            Actor = "controller",
            Reason = item.ReopenReason
        };
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        await operationsRepository.SaveAsync(BuildOperationsWorkflow(fundAccountId, import, now.AddDays(-1)));
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            new StaticQueueRepository(item),
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        await service.ApplyAsync(command);

        (await breakStore.GetAsync(sourceBreak.BreakId))!.Status.Should().Be("Open");
        var retainedCase = await caseStore.GetAsync(sourceCase.CaseId);
        retainedCase.Should().NotBeNull();
        retainedCase!.Status.Should().Be("Open");
        retainedCase.Disposition.Should().Be("NeedsInvestigation");
        retainedCase.Resolution.Should().BeNull();
        retainedCase.AuditEvents.Should().ContainSingle(audit =>
            audit.EventType == "StatementBreakReopened" &&
            audit.Actor == "controller");
    }

    [Fact]
    public async Task ApplyAsync_MissingOperationsWorkflow_ShouldFailClosedAndExactReplayShouldResumeHandoff()
    {
        var now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var import = BuildImport("import-retry", fundAccountId);
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-retry");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1));
        var item = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Reviewed statement variance."
        };
        var command = BuildCommand(item, ReconciliationCaseworkAction.Resolve);
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            new StaticQueueRepository(item),
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        var first = () => service.ApplyAsync(command);
        var failure = await first.Should().ThrowAsync<StatementReconciliationCaseworkHandoffException>();
        failure.Which.Code.Should().Be("OPERATIONS_WORKFLOW_REQUIRED");
        journalStore.Invocations.Should().BeEmpty(
            "missing workflow detection must remain ahead of ledger authority resolution");
        (await breakStore.GetAsync(sourceBreak.BreakId))!.Status.Should().Be("Resolved");
        (await caseStore.GetAsync(sourceCase.CaseId))!.AuditEvents
            .Should().ContainSingle(audit => audit.EventType == "StatementBreakDisposed");

        await operationsRepository.SaveAsync(BuildOperationsWorkflow(fundAccountId, import, now.AddDays(-1)));
        var replay = await service.ApplyAsync(command);

        replay.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        (await caseStore.GetAsync(sourceCase.CaseId))!.AuditEvents
            .Should().ContainSingle(audit => audit.EventType == "StatementBreakDisposed");
        var workflow = (await operations.ListAsync(
                fundAccountId,
                import.AccountingScope!.AccountingPeriodId.ToString("D"),
                ledgerBookId: import.AccountingScope.LedgerBookId))
            .Should().ContainSingle().Which;
        (await operations.GetTimelineAsync(workflow.WorkflowId))
            .SelectMany(entry => entry.References)
            .Should().ContainSingle(reference => reference.Source == "statement-reconciliation-casework");
    }

    [Fact]
    public async Task ApplyAsync_GuidAccountingPeriod_ShouldResolveCanonicalPeriodBeforeEvidenceHandoff()
    {
        var now = new DateTimeOffset(2026, 7, 26, 15, 0, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var import = BuildImport(
            "import-guid-period",
            fundAccountId,
            accountingPeriodId: periodId);
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-guid-period");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1));
        var item = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved,
            DispositionReason = "Reviewed against the canonical accounting period."
        };
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        await operationsRepository.SaveAsync(BuildOperationsWorkflow(
            fundAccountId,
            import,
            now.AddDays(-1),
            periodId.ToString()));
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            new StaticQueueRepository(item),
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        await service.ApplyAsync(BuildCommand(item, ReconciliationCaseworkAction.Resolve));

        var workflow = (await operations.ListAsync(
                fundAccountId,
                periodId.ToString("D"),
                ledgerBookId: import.AccountingScope!.LedgerBookId))
            .Should().ContainSingle().Which;
        (await operations.GetTimelineAsync(workflow.WorkflowId))
            .SelectMany(entry => entry.References)
            .Should().ContainSingle(reference => reference.Source == "statement-reconciliation-casework");
        journalStore.Verify(
            store => store.GetLedgerBookAsync(
                import.AccountingScope!.LedgerBookId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        journalStore.Verify(
            store => store.GetPeriodAsync(periodId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_DifferentLedgerBookWorkflow_ShouldNotRebindAlreadyScopedStatementCase()
    {
        var now = new DateTimeOffset(2026, 7, 26, 15, 10, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var import = BuildImport("import-different-book", fundAccountId);
        var scope = import.AccountingScope!;
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-different-book");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1));
        var item = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved
        };
        var queue = new StaticQueueRepository(item);
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        var differentBookWorkflow = BuildOperationsWorkflow(
            fundAccountId,
            import,
            now.AddDays(-1),
            scope.AccountingPeriodId.ToString("D"),
            Guid.NewGuid());
        await operationsRepository.SaveAsync(differentBookWorkflow);
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            queue,
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        var apply = () => service.ApplyAsync(
            BuildCommand(item, ReconciliationCaseworkAction.Resolve));

        var failure = await apply
            .Should()
            .ThrowAsync<StatementReconciliationCaseworkHandoffException>();
        failure.Which.Code.Should().Be("OPERATIONS_WORKFLOW_REQUIRED");
        journalStore.Invocations.Should().BeEmpty(
            "a differently scoped workflow must be rejected before ledger authority resolution");
        var retained = await queue.GetByIdAsync(item.BreakId);
        retained.Should().NotBeNull();
        retained!.FundProfileId.Should().Be(scope.FundProfileId);
        retained.LedgerBookId.Should().Be(scope.LedgerBookId);
        retained.AccountingPeriodId.Should().Be(scope.AccountingPeriodId.ToString("D"));
        retained.AsOfDate.Should().Be(scope.AsOfDate);
        (await operations.GetTimelineAsync(differentBookWorkflow.WorkflowId))
            .SelectMany(entry => entry.References)
            .Should().NotContain(reference =>
                reference.Source == "statement-reconciliation-casework");
    }

    [Fact]
    public async Task ApplyAsync_DifferentAccountingPeriodWorkflow_ShouldNotRebindAlreadyScopedStatementCase()
    {
        var now = new DateTimeOffset(2026, 7, 26, 15, 20, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var import = BuildImport("import-different-period", fundAccountId);
        var scope = import.AccountingScope!;
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-different-period");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1));
        var item = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.Resolved,
            LifecycleState = ReconciliationCaseLifecycleState.Resolved,
            Disposition = ReconciliationBreakDispositionDto.Resolved
        };
        var queue = new StaticQueueRepository(item);
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        var differentPeriodWorkflow = BuildOperationsWorkflow(
            fundAccountId,
            import,
            now.AddDays(-1),
            Guid.NewGuid().ToString("D"),
            scope.LedgerBookId);
        await operationsRepository.SaveAsync(differentPeriodWorkflow);
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            queue,
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        var apply = () => service.ApplyAsync(
            BuildCommand(item, ReconciliationCaseworkAction.Resolve));

        var failure = await apply
            .Should()
            .ThrowAsync<StatementReconciliationCaseworkHandoffException>();
        failure.Which.Code.Should().Be("OPERATIONS_WORKFLOW_REQUIRED");
        journalStore.Invocations.Should().BeEmpty(
            "a differently scoped workflow must be rejected before ledger authority resolution");
        var retained = await queue.GetByIdAsync(item.BreakId);
        retained.Should().NotBeNull();
        retained!.FundProfileId.Should().Be(scope.FundProfileId);
        retained.LedgerBookId.Should().Be(scope.LedgerBookId);
        retained.AccountingPeriodId.Should().Be(scope.AccountingPeriodId.ToString("D"));
        retained.AsOfDate.Should().Be(scope.AsOfDate);
        (await operations.GetTimelineAsync(differentPeriodWorkflow.WorkflowId))
            .SelectMany(entry => entry.References)
            .Should().NotContain(reference =>
                reference.Source == "statement-reconciliation-casework");
    }

    [Fact]
    public async Task ApplyBulkAsync_CompletedHandoff_ShouldProjectCurrentQueueItemAndRetainBulkReceipt()
    {
        var now = new DateTimeOffset(2026, 7, 26, 15, 30, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var import = BuildImport("import-bulk-projection", fundAccountId);
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-bulk-projection");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1));
        var initial = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.InReview,
            LifecycleState = ReconciliationCaseLifecycleState.Investigating,
            FundAccountId = fundAccountId.ToString("D"),
            RootCauseCode = "BrokerCashTiming",
            DispositionEvidenceHash = null,
            BlockedOutputs = ["ClientDelivery"]
        };
        var queue = new FileReconciliationBreakQueueRepository(
            Path.Combine(_root, "bulk-projection-queue"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await queue.CreateIfMissingAsync(initial)).Should().BeTrue();
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        await operationsRepository.SaveAsync(BuildOperationsWorkflow(
            fundAccountId,
            import,
            now.AddDays(-1)));
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            queue,
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);
        var request = new ReconciliationBulkCaseworkRequest(
            BreakIds: [initial.BreakId],
            Action: ReconciliationCaseworkAction.Resolve,
            Actor: "fund-ops",
            CommandId: "bulk-statement-resolution",
            CorrelationId: "bulk-statement-resolution-correlation",
            Source: "workstation-reconciliation-bulk-casework",
            IdempotencyKey: "bulk-statement-resolution-idempotency",
            DryRun: false,
            AllowPartialSuccess: false,
            Reason: "Reviewed statement variance.",
            Note: "Corrected statement row retained.",
            RootCauseCode: "BrokerCashTiming",
            ResolutionCode: "BrokerStatementCorrected",
            EvidenceLinks: ["statement:corrected-cash-row"],
            ActionOrigin: OperationsActionOriginDto.HumanOperator);

        var result = await service.ApplyBulkAsync(request);

        result.Results.Should().ContainSingle();
        var projected = result.Results[0].Item;
        projected.Should().NotBeNull();
        var caseCommandId = $"{request.CommandId}:{initial.BreakId}";
        StatementCaseworkHandoffObligation.HasPending(projected!, caseCommandId).Should().BeFalse();
        projected!.EvidenceLinks.Should().Contain(
            StatementCaseworkHandoffObligation.CreateCompletedMarker(caseCommandId));
        projected.BlockedOutputs.Should().ContainSingle().Which.Should().Be("ClientDelivery");
        var retained = await queue.GetBulkCaseworkResultAsync(result.BulkActionId);
        retained.Should().NotBeNull();
        StatementCaseworkHandoffObligation.HasPending(retained!.Results[0].Item!, caseCommandId)
            .Should().BeTrue("bulk idempotency retains the immutable terminal transition receipt");
        result.Outcome.OperationId.Should().Be(retained.Outcome.OperationId);
        result.InputHashSha256.Should().Be(retained.InputHashSha256);
    }

    [Fact]
    public async Task ApplyAsync_TerminalHandoffFailure_ShouldRemainDurablyCloseBlockingUntilExactReplayCompletes()
    {
        var now = new DateTimeOffset(2026, 7, 26, 16, 0, 0, TimeSpan.Zero);
        var fundAccountId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid();
        var import = BuildImport(
            "import-durable-handoff",
            fundAccountId,
            ledgerBookId,
            accountingPeriodId);
        var sourceBreak = BuildSourceBreak(import.ImportId, "source-break-durable-handoff");
        var sourceCase = BuildSourceCase(import.ImportId, sourceBreak.BreakId, now.AddDays(-1));
        var initial = BuildQueueItem(import, sourceBreak.BreakId, now) with
        {
            Status = ReconciliationBreakQueueStatus.InReview,
            LifecycleState = ReconciliationCaseLifecycleState.Investigating,
            FundAccountId = fundAccountId.ToString("D"),
            LedgerBookId = ledgerBookId,
            AccountingPeriodId = accountingPeriodId.ToString("D"),
            AsOfDate = import.StatementPeriodEnd,
            RootCauseCode = "BrokerCashTiming",
            SourceFingerprint = new string('c', 64),
            DispositionEvidenceHash = null,
            BlockedOutputs = ["ClientDelivery"],
            Measures =
            [
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.Value,
                    Expected: 125m,
                    Actual: 124m,
                    Variance: -1m,
                    Tolerance: 0.50m,
                    Unit: "USD"),
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.Quantity,
                    Expected: null,
                    Actual: null,
                    Variance: null,
                    Tolerance: null,
                    Unit: "units",
                    UnavailableReason: "The cash statement break has no quantity measure."),
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.CostBasis,
                    Expected: null,
                    Actual: null,
                    Variance: null,
                    Tolerance: null,
                    Unit: "USD",
                    UnavailableReason: "The cash statement break has no cost-basis measure.")
            ]
        };
        var queueRoot = Path.Combine(_root, "durable-queue");
        var queue = new FileReconciliationBreakQueueRepository(
            queueRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await queue.CreateIfMissingAsync(initial)).Should().BeTrue();
        var current = await queue.GetByIdAsync(initial.BreakId);
        current.Should().NotBeNull();
        var command = BuildCommand(current!, ReconciliationCaseworkAction.Resolve) with
        {
            ExpectedVersion = current!.Version,
            RootCauseCode = "BrokerCashTiming",
            ResolutionCode = "BrokerStatementCorrected",
            EvidenceLinks = current.EvidenceLinks!
                .Append("statement:corrected-cash-row")
                .ToArray()
        };
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        await breakStore.WriteAsync([sourceBreak]);
        await caseStore.SaveAsync(sourceCase);
        var operations = CreateOperationsService(out var operationsRepository);
        var journalStore = CreateStrictJournalStore(import);
        var service = new StatementReconciliationCaseworkHandoffService(
            queue,
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        var first = () => service.ApplyAsync(command);
        var failure = await first.Should().ThrowAsync<StatementReconciliationCaseworkHandoffException>();

        failure.Which.Code.Should().Be("OPERATIONS_WORKFLOW_REQUIRED");
        journalStore.Invocations.Should().BeEmpty(
            "missing workflow detection must remain ahead of ledger authority resolution");
        var pending = await queue.GetByIdAsync(initial.BreakId);
        pending.Should().NotBeNull();
        pending!.Disposition.Should().Be(ReconciliationBreakDispositionDto.Resolved);
        StatementCaseworkHandoffObligation.HasPending(pending!, command.CommandId).Should().BeTrue();
        pending!.BlockedOutputs.Should().BeEquivalentTo(
            new[] { "ClientDelivery", "FinalReport", "PeriodClose" });
        var signOffWhilePending = await queue.ApplyCaseworkCommandAsync(
            BuildCommand(pending, ReconciliationCaseworkAction.SignOff) with
            {
                Actor = "controller",
                CommandId = "signoff-before-statement-handoff",
                ExpectedVersion = pending.Version,
                Note = "Independent review cannot outrun the retained evidence handoff."
            });
        signOffWhilePending.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        signOffWhilePending.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.MissingEvidence);
        signOffWhilePending.Error.Should().Contain("pending statement-source/Operations evidence handoff");
        var forgedCompletion = await queue.ApplyCaseworkCommandAsync(
            new ReconciliationCaseworkCommand(
                pending.BreakId,
                ReconciliationCaseworkAction.LinkEvidence,
                Actor: "fund-ops",
                CommandId: "forged-statement-handoff-completion",
                CorrelationId: command.CorrelationId,
                Source: "workstation-reconciliation-casework",
                ExpectedVersion: pending.Version,
                Reason: "Attempt to bypass paired completion.",
                EvidenceLinks:
                [
                    StatementCaseworkHandoffObligation.CreateCompletedMarker(command.CommandId)
                ]));
        forgedCompletion.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.ValidationFailed);
        forgedCompletion.ErrorCode.Should().Be(ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        StatementCaseworkHandoffObligation.HasPending(
                (await queue.GetByIdAsync(initial.BreakId))!,
                command.CommandId)
            .Should().BeTrue();
        Action closeWhilePending = () =>
        {
            _ = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
                [pending!],
                import.AccountingScope!.FundProfileId,
                ledgerBookId,
                accountingPeriodId,
                import.StatementPeriodEnd,
                expectedOpenBreakCount: 0);
        };
        closeWhilePending.Should().Throw<InvalidOperationException>()
            .WithMessage("*pending statement-source/Operations evidence handoff*");
        var unscopedPending = pending! with
        {
            FundProfileId = null,
            LedgerBookId = null,
            AccountingPeriodId = null,
            AsOfDate = null
        };
        Action closeWithUnscopedPending = () =>
        {
            _ = AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
                [unscopedPending],
                import.AccountingScope!.FundProfileId,
                ledgerBookId,
                accountingPeriodId,
                import.StatementPeriodEnd,
                expectedOpenBreakCount: 0);
        };
        closeWithUnscopedPending.Should().Throw<InvalidOperationException>()
            .WithMessage("*incomplete close scope*block PeriodClose and FinalReport*");

        await operationsRepository.SaveAsync(BuildOperationsWorkflow(
            fundAccountId,
            import,
            now.AddDays(-1),
            accountingPeriodId.ToString("D"),
            ledgerBookId));
        var restartedQueue = new FileReconciliationBreakQueueRepository(
            queueRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var restartedPending = await restartedQueue.GetByIdAsync(initial.BreakId);
        restartedPending.Should().NotBeNull();
        StatementCaseworkHandoffObligation.HasPending(restartedPending!, command.CommandId)
            .Should().BeTrue("the pending handoff must survive process restart");
        var restartedService = new StatementReconciliationCaseworkHandoffService(
            restartedQueue,
            breakStore,
            caseStore,
            new StaticStatementRunWorkflowService(import),
            operations,
            journalStore.Object);

        var replay = await restartedService.ApplyAsync(command);

        replay.Status.Should().Be(ReconciliationBreakQueueTransitionStatus.Success);
        replay.Item.Should().NotBeNull();
        StatementCaseworkHandoffObligation.HasPending(replay.Item!, command.CommandId).Should().BeFalse();
        replay.Item!.BlockedOutputs.Should().NotContain("FinalReport");
        replay.Item.BlockedOutputs.Should().NotContain("PeriodClose");
        replay.Item.BlockedOutputs.Should().ContainSingle().Which.Should().Be("ClientDelivery");
        var retainedReceipt = await restartedQueue.ApplyCaseworkCommandAsync(command);
        StatementCaseworkHandoffObligation.HasPending(retainedReceipt.Item!, command.CommandId)
            .Should().BeTrue("the immutable transition receipt predates paired handoff completion");
        replay.Outcome.OperationId.Should().Be(retainedReceipt.Outcome.OperationId);
        replay.Outcome.InputHashSha256.Should().Be(retainedReceipt.Outcome.InputHashSha256);
        var cleared = await restartedQueue.GetByIdAsync(initial.BreakId);
        cleared.Should().NotBeNull();
        StatementCaseworkHandoffObligation.HasPending(cleared!).Should().BeFalse();
        cleared!.BlockedOutputs.Should().NotContain("FinalReport");
        cleared.BlockedOutputs.Should().NotContain("PeriodClose");
        cleared.BlockedOutputs.Should().ContainSingle().Which.Should().Be("ClientDelivery");
        cleared!.EvidenceLinks.Should().Contain(
            StatementCaseworkHandoffObligation.CreateCompletedMarker(command.CommandId));
        (await restartedQueue.GetAuditHistoryAsync(initial.BreakId)).Should().Contain(audit =>
            audit.EventType == "EvidenceLinked"
            && audit.Source == StatementCaseworkHandoffObligation.CompletionSource
            && audit.CausationId == command.CommandId);
        AccountingClosePostingWorkbenchBridge.BuildExactReportingBreakEvidence(
                [cleared!],
                import.AccountingScope!.FundProfileId,
                ledgerBookId,
                accountingPeriodId,
                import.StatementPeriodEnd,
                expectedOpenBreakCount: 0)
            .Should().ContainSingle()
            .Which.Disposition.Should().Be(ReconciliationBreakDispositionDto.Resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static OperationsContinuityWorkflowService CreateOperationsService(
        out InMemoryOperationsContinuityRepository repository)
    {
        var derivation = new OperationsStatusDerivationService();
        repository = new InMemoryOperationsContinuityRepository(derivation);
        return new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
    }

    private static OperationsContinuityWorkflow BuildOperationsWorkflow(
        Guid fundAccountId,
        CanonicalStatementImport import,
        DateTimeOffset now,
        string? periodId = null,
        Guid? ledgerBookId = null)
    {
        var scope = import.AccountingScope
            ?? throw new InvalidOperationException("The test statement import must carry accounting scope.");
        return OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            fundAccountId,
            periodId ?? scope.AccountingPeriodId.ToString("D"),
            securityMasterSnapshotId: null,
            brokerSource: import.Broker,
            now,
            ledgerBookId ?? scope.LedgerBookId);
    }

    private static CanonicalStatementImport BuildImport(
        string importId,
        Guid fundAccountId,
        Guid? ledgerBookId = null,
        Guid? accountingPeriodId = null,
        string fundProfileId = DefaultFundProfileId)
    {
        var statementPeriodStart = new DateOnly(2026, 6, 1);
        var statementPeriodEnd = new DateOnly(2026, 6, 30);
        var resolvedLedgerBookId = ledgerBookId ?? DefaultLedgerBookId;
        var resolvedAccountingPeriodId = accountingPeriodId ?? DefaultAccountingPeriodId;
        return new CanonicalStatementImport(
            importId,
            "custodian",
            statementPeriodEnd,
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            "retained://statement.csv",
            new string('A', 64),
            4,
            4)
        {
            FundAccountId = fundAccountId.ToString("D"),
            ExternalAccountId = "CUST-123",
            StatementPeriodStart = statementPeriodStart,
            StatementPeriodEnd = statementPeriodEnd,
            SourceInstitution = "custodian",
            OriginalFileName = "statement.csv",
            ImportedBy = "statement-loader",
            AccountingScope = new StatementAccountingScope(
                fundProfileId,
                resolvedLedgerBookId,
                resolvedAccountingPeriodId,
                statementPeriodEnd)
        };
    }

    private static Mock<ILedgerJournalStore> CreateStrictJournalStore(
        CanonicalStatementImport import)
    {
        var scope = import.AccountingScope
            ?? throw new InvalidOperationException("The test statement import must carry accounting scope.");
        var journalStore = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        journalStore
            .Setup(store => store.GetLedgerBookAsync(
                scope.LedgerBookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerBookRecord(
                scope.LedgerBookId,
                scope.FundProfileId,
                DefaultFundStructureNodeId,
                FundStructureNodeKindDto.Fund,
                "Statement authority book",
                "USD",
                import.ImportedAtUtc.AddYears(-1),
                import.ImportedAtUtc));
        journalStore
            .Setup(store => store.GetPeriodAsync(
                scope.AccountingPeriodId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerAccountingPeriod(
                scope.AccountingPeriodId,
                scope.LedgerBookId,
                2026,
                6,
                "2026-06",
                import.StatementPeriodStart,
                import.StatementPeriodEnd,
                "Open",
                import.ImportedAtUtc.AddMonths(-1),
                ClosedAt: null,
                Version: 1));
        return journalStore;
    }

    private static ReconciliationBreakRecord BuildSourceBreak(string importId, string breakId)
        => new(
            breakId,
            importId,
            importId,
            $"{importId}:1",
            "AMOUNT_MISMATCH",
            "cash",
            125m,
            1m,
            true,
            new DateTimeOffset(2026, 7, 1, 8, 5, 0, TimeSpan.Zero),
            "Open")
        {
            EvidenceLink = $"/api/workstation/reconciliation/statement-runs/{importId}#row-1"
        };

    private static ReconciliationCase BuildSourceCase(
        string importId,
        string breakId,
        DateTimeOffset createdAt)
        => new(
            $"case:{breakId}",
            importId,
            "Open",
            "Statement amount does not match the retained book.",
            0.5m,
            "Manual review required.",
            createdAt,
            [])
        {
            Owner = "fund-ops",
            LastUpdatedAtUtc = createdAt,
            LastUpdatedBy = "statement-loader",
            Disposition = "NeedsInvestigation",
            EvidenceReferences =
            [
                $"/api/workstation/reconciliation/statement-runs/{importId}#row-1"
            ]
        };

    private static ReconciliationBreakQueueItem BuildQueueItem(
        CanonicalStatementImport import,
        string sourceBreakId,
        DateTimeOffset now)
    {
        var scope = import.AccountingScope
            ?? throw new InvalidOperationException("The test statement import must carry accounting scope.");
        return new ReconciliationBreakQueueItem(
            BreakId: $"statement:queue-{sourceBreakId}",
            RunId: import.ImportId,
            StrategyName: "Statement reconciliation",
            Category: ReconciliationBreakCategory.ExternalStatementMismatch,
            Status: ReconciliationBreakQueueStatus.InReview,
            Variance: 125m,
            Reason: "Statement amount does not match the retained book.",
            AssignedTo: "fund-ops",
            DetectedAt: now.AddHours(-1),
            LastUpdatedAt: now,
            Severity: ReconciliationBreakSeverity.High,
            LifecycleState: ReconciliationCaseLifecycleState.InReview,
            Version: 2,
            FundAccountId: import.FundAccountId,
            EvidenceLinks:
            [
                $"/api/workstation/reconciliation/statement-runs/{import.ImportId}#row-1"
            ],
            SourceType: "statement",
            SourceSystem: "statement-reconciliation",
            SourceReference: $"{import.ImportId}:1",
            SourceImportId: import.ImportId,
            SourceBreakId: sourceBreakId,
            LedgerBookId: scope.LedgerBookId,
            DispositionEvidenceHash: new string('b', 64),
            AccountingPeriodId: scope.AccountingPeriodId.ToString("D"),
            AsOfDate: scope.AsOfDate)
        {
            FundProfileId = scope.FundProfileId
        };
    }

    private static ReconciliationCaseworkCommand BuildCommand(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkAction action)
        => new(
            item.BreakId,
            action,
            "fund-ops",
            $"command-{item.SourceBreakId}",
            $"correlation-{item.SourceImportId}",
            "workstation-reconciliation-casework",
            item.Version - 1,
            Reason: "Retain reviewed statement reconciliation evidence.",
            Note: "Statement variance reviewed.",
            ResolutionCode: action == ReconciliationCaseworkAction.Resolve ? "SourceVerified" : null,
            EvidenceLinks: item.EvidenceLinks,
            Privileged: action == ReconciliationCaseworkAction.Reopen,
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            ApprovalActor: "controller",
            ApprovalReference: "approval://controller-review");

    private sealed class StaticQueueRepository(ReconciliationBreakQueueItem item)
        : IReconciliationBreakQueueRepository
    {
        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
            ReconciliationBreakQueueStatus? status = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(
                !status.HasValue || status.Value == item.Status ? [item] : []);

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(
            string breakId,
            CancellationToken ct = default)
            => Task.FromResult<ReconciliationBreakQueueItem?>(
                string.Equals(breakId, item.BreakId, StringComparison.OrdinalIgnoreCase) ? item : null);

        public Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem value, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SaveAsync(ReconciliationBreakQueueItem value, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(
            string breakId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakQueueAuditEvent>>([]);

        public Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(
            ReconciliationCaseworkCommand command,
            CancellationToken ct = default)
            => Task.FromResult(new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.Success,
                item));
    }

    private sealed class StaticStatementRunWorkflowService(CanonicalStatementImport import)
        : IStatementRunWorkflowService
    {
        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([import]);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StatementRunWorkflowResult?>(null);

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }
}
