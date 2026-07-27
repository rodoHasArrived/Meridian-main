using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public interface IStatementReconciliationCaseworkHandoffService
{
    Task<ReconciliationBreakQueueTransitionResult> ApplyAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationCaseworkCommand command,
        CancellationToken ct = default);

    Task<ReconciliationBulkCaseworkResult> ApplyBulkAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationBulkCaseworkRequest request,
        CancellationToken ct = default);
}

public sealed class StatementReconciliationCaseworkHandoffException(
    string code,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Synchronizes terminal governed queue casework back to its statement-owned break/case and then
/// attaches the same evidence to the matching Operations Continuity workflow. It deliberately does
/// not post, approve, close, release, or otherwise advance an accounting gate.
/// </summary>
public sealed class StatementReconciliationCaseworkHandoffService : IStatementReconciliationCaseworkHandoffService
{
    private readonly IReconciliationBreakQueueRepository _queueRepository;
    private readonly IReconciliationBreakStore? _statementBreakStore;
    private readonly IReconciliationCaseStore? _statementCaseStore;
    private readonly IStatementRunWorkflowService? _statementRuns;
    private readonly IOperationsContinuityWorkflowService? _operationsContinuity;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);

    public StatementReconciliationCaseworkHandoffService(
        IReconciliationBreakQueueRepository queueRepository,
        IReconciliationBreakStore? statementBreakStore = null,
        IReconciliationCaseStore? statementCaseStore = null,
        IStatementRunWorkflowService? statementRuns = null,
        IOperationsContinuityWorkflowService? operationsContinuity = null,
        ILedgerJournalStore? ledgerJournalStore = null)
    {
        _queueRepository = queueRepository ?? throw new ArgumentNullException(nameof(queueRepository));
        _statementBreakStore = statementBreakStore;
        _statementCaseStore = statementCaseStore;
        _statementRuns = statementRuns;
        _operationsContinuity = operationsContinuity;
        _ledgerJournalStore = ledgerJournalStore;
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ApplyAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationCaseworkCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);
        var transition = await _queueRepository
            .ApplyCaseworkCommandAsync(scope, command, ct)
            .ConfigureAwait(false);
        if (transition.Status != ReconciliationBreakQueueTransitionStatus.Success ||
            transition.Item is null ||
            !RequiresStatementSynchronization(command, transition.Item))
        {
            return transition;
        }

        await _synchronizationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SynchronizeAsync(scope, command, transition.Item, ct).ConfigureAwait(false);
        }
        finally
        {
            _synchronizationGate.Release();
        }

        var current = await RequireCurrentQueueItemAsync(scope, command.BreakId, ct).ConfigureAwait(false);
        return transition with { Item = current };
    }

    public async Task<ReconciliationBulkCaseworkResult> ApplyBulkAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationBulkCaseworkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        var result = await _queueRepository
            .ApplyBulkCaseworkAsync(scope, request, ct)
            .ConfigureAwait(false);
        if (request.DryRun || result.Results.All(static item => !item.Succeeded || item.Item is null))
        {
            return result;
        }

        await _synchronizationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var projected = new List<ReconciliationBulkCaseworkCaseResult>(result.Results.Count);
            foreach (var caseResult in result.Results)
            {
                if (!caseResult.Succeeded || caseResult.Item is null)
                {
                    projected.Add(caseResult);
                    continue;
                }

                var item = caseResult.Item!;
                var command = new ReconciliationCaseworkCommand(
                    BreakId: item.BreakId,
                    Action: request.Action,
                    Actor: request.Actor,
                    CommandId: $"{request.CommandId}:{item.BreakId}",
                    CorrelationId: request.CorrelationId,
                    Source: request.Source,
                    ExpectedVersion: item.Version,
                    Reason: request.Reason,
                    Assignee: request.Assignee,
                    Priority: request.Priority,
                    Status: request.Status,
                    Note: request.Note,
                    RootCauseCode: request.RootCauseCode,
                    ResolutionCode: request.ResolutionCode,
                    EvidenceLinks: request.EvidenceLinks,
                    ActionOrigin: request.ActionOrigin,
                    ApprovalActor: request.ApprovalActor,
                    ApprovalReference: request.ApprovalReference,
                    SupersedingBreakId: request.SupersedingBreakId);
                if (RequiresStatementSynchronization(command, item))
                {
                    await SynchronizeAsync(scope, command, item, ct).ConfigureAwait(false);
                    var current = await RequireCurrentQueueItemAsync(scope, item.BreakId, ct).ConfigureAwait(false);
                    projected.Add(caseResult with { Item = current });
                    continue;
                }

                projected.Add(caseResult);
            }

            return result with { Results = projected };
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    private async Task<ReconciliationBreakQueueItem> RequireCurrentQueueItemAsync(
        ReconciliationBreakQueueScope scope,
        string breakId,
        CancellationToken ct)
        => await _queueRepository.GetByIdAsync(scope, breakId, ct).ConfigureAwait(false)
           ?? throw Failure(
               "STATEMENT_CASEWORK_NOT_FOUND",
               $"Statement case '{breakId}' disappeared after its evidence handoff completed.");

    private async Task SynchronizeAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem retainedItem,
        CancellationToken ct)
    {
        EnsureStatementDependencies();
        var currentItem = await _queueRepository.GetByIdAsync(scope, retainedItem.BreakId, ct).ConfigureAwait(false);
        if (currentItem is null)
        {
            throw Failure(
                "STATEMENT_CASEWORK_NOT_FOUND",
                $"Statement case '{retainedItem.BreakId}' disappeared after its governed casework transition was retained.");
        }

        var hasDurableObligation =
            StatementCaseworkHandoffObligation.HasPending(retainedItem, command.CommandId);
        if (hasDurableObligation
            && !StatementCaseworkHandoffObligation.HasPending(currentItem, command.CommandId))
        {
            // The queue retains immutable command receipts, so exact replay returns the item as it
            // existed at the terminal transition. The current item is authoritative for whether
            // the paired completion audit already cleared that retained obligation.
            if (HasVerifiedCompletion(currentItem, command.CommandId))
            {
                return;
            }

            throw Failure(
                "STATEMENT_HANDOFF_COMPLETION_EVIDENCE_MISSING",
                $"Statement case '{retainedItem.BreakId}' no longer exposes its pending handoff, but the paired completion marker is not durably retained.");
        }

        var sourceImportId = Require(retainedItem.SourceImportId, "STATEMENT_IMPORT_ID_REQUIRED",
            "Statement-origin casework is missing its source import id.");
        var sourceBreakId = Require(retainedItem.SourceBreakId, "STATEMENT_BREAK_ID_REQUIRED",
            "Statement-origin casework is missing its source break id.");
        var disposition = ResolveDisposition(retainedItem, command);
        var sourceStatus = ResolveSourceStatus(retainedItem, command, disposition);
        var evidence = BuildEvidence(command, retainedItem);
        ReconciliationBreakRecord sourceBreak;
        try
        {
            sourceBreak = await _statementBreakStore!
                .ApplyCaseworkAsync(
                    new StatementBreakCaseworkUpdate(
                        BreakId: sourceBreakId,
                        ImportId: sourceImportId,
                        Status: sourceStatus,
                        Actor: command.Actor,
                        Action: command.Action.ToString(),
                        CommandId: command.CommandId,
                        CorrelationId: command.CorrelationId,
                        Reason: FirstNonBlank(command.Reason, command.Note, retainedItem.DispositionReason, retainedItem.ResolutionNote),
                        Disposition: disposition,
                        ApprovalActor: FirstNonBlank(retainedItem.DispositionApprovedBy, command.ApprovalActor),
                        ApprovalReference: FirstNonBlank(retainedItem.DispositionApprovalReference, command.ApprovalReference),
                        SupersedingBreakId: FirstNonBlank(retainedItem.SupersedingBreakId, command.SupersedingBreakId),
                        EvidenceLinks: evidence,
                        OccurredAtUtc: retainedItem.LastUpdatedAt),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not StatementReconciliationCaseworkHandoffException)
        {
            throw Failure(
                "STATEMENT_BREAK_SYNCHRONIZATION_FAILED",
                $"The governed casework transition was retained, but source statement break '{sourceBreakId}' could not be synchronized.",
                exception);
        }

        await SynchronizeSourceCaseAsync(
                command,
                retainedItem,
                sourceBreak,
                sourceStatus,
                disposition,
                evidence,
                ct)
            .ConfigureAwait(false);
        var closeScope = await AttachOperationsContinuityEvidenceAsync(
                command,
                retainedItem,
                sourceBreak,
                disposition,
                evidence,
                ct)
            .ConfigureAwait(false);
        if (hasDurableObligation)
        {
            await CompleteDurableHandoffObligationAsync(scope, command, retainedItem.BreakId, closeScope, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task CompleteDurableHandoffObligationAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationCaseworkCommand command,
        string breakId,
        ReconciliationCaseworkCloseScopeDto closeScope,
        CancellationToken ct)
    {
        const int maximumAttempts = 5;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            var current = await _queueRepository.GetByIdAsync(scope, breakId, ct).ConfigureAwait(false)
                ?? throw Failure(
                    "STATEMENT_CASEWORK_NOT_FOUND",
                    $"Statement case '{breakId}' disappeared before its evidence-handoff obligation could be cleared.");
            if (!StatementCaseworkHandoffObligation.HasPending(current, command.CommandId))
            {
                return;
            }

            var completion = new ReconciliationCaseworkCommand(
                BreakId: current.BreakId,
                Action: ReconciliationCaseworkAction.LinkEvidence,
                Actor: command.Actor,
                CommandId: StatementCaseworkHandoffObligation.CreateCompletionCommandId(command.CommandId),
                CorrelationId: command.CorrelationId,
                Source: StatementCaseworkHandoffObligation.CompletionSource,
                ExpectedVersion: current.Version,
                Reason: "Source statement casework and Operations Continuity evidence handoff completed.",
                Note: "Clear the durable statement casework evidence-handoff obligation.",
                CausationId: command.CommandId,
                EvidenceLinks:
                [
                    StatementCaseworkHandoffObligation.CreateCompletedMarker(command.CommandId)
                ],
                ActionOrigin: OperationsActionOriginDto.HumanOperator)
            {
                CloseScope = closeScope
            };
            var cleared = await _queueRepository
                .ApplyCaseworkCommandAsync(scope, completion, ct)
                .ConfigureAwait(false);
            if (cleared.Status == ReconciliationBreakQueueTransitionStatus.Success)
            {
                var verified = await _queueRepository.GetByIdAsync(scope, breakId, ct).ConfigureAwait(false)
                    ?? throw Failure(
                        "STATEMENT_CASEWORK_NOT_FOUND",
                        $"Statement case '{breakId}' disappeared after its evidence-handoff completion command succeeded.");
                if (!HasVerifiedCompletion(verified, command.CommandId))
                {
                    throw Failure(
                        "STATEMENT_HANDOFF_COMPLETION_NOT_RETAINED",
                        $"Statement case '{breakId}' reported successful evidence-handoff completion without retaining the paired completion marker and clearing its pending marker.");
                }

                return;
            }

            if (cleared.Status == ReconciliationBreakQueueTransitionStatus.Conflict
                && cleared.ErrorCode == ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict)
            {
                continue;
            }

            throw Failure(
                "STATEMENT_HANDOFF_COMPLETION_NOT_RETAINED",
                $"Statement case '{breakId}' completed source and Operations Continuity synchronization, but its durable pending obligation could not be cleared: {FirstNonBlank(cleared.Error, cleared.ErrorCode.ToString())}.");
        }

        throw Failure(
            "STATEMENT_HANDOFF_COMPLETION_CONFLICT",
            $"Statement case '{breakId}' completed source and Operations Continuity synchronization, but concurrent casework prevented its durable pending obligation from being cleared after {maximumAttempts} attempts.");
    }

    private static bool HasVerifiedCompletion(
        ReconciliationBreakQueueItem item,
        string commandId)
        => StatementCaseworkHandoffObligation.HasCompleted(item, commandId)
           && !(item.EvidenceLinks ?? [])
               .Contains(
                   StatementCaseworkHandoffObligation.CreatePendingMarker(commandId),
                   StringComparer.Ordinal);

    private async Task SynchronizeSourceCaseAsync(
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem item,
        ReconciliationBreakRecord sourceBreak,
        string sourceStatus,
        string disposition,
        IReadOnlyList<string> evidence,
        CancellationToken ct)
    {
        var caseId = $"case:{sourceBreak.BreakId}";
        ReconciliationCase? current;
        try
        {
            current = await _statementCaseStore!.GetAsync(caseId, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "STATEMENT_CASE_READ_FAILED",
                $"The source statement break was synchronized, but reconciliation case '{caseId}' could not be read.",
                exception);
        }

        // Older retained statement imports can predate the corresponding rich case record. The
        // break-owned audit remains authoritative in that compatibility posture.
        if (current is null)
        {
            return;
        }

        var eventId = BuildStableId("statement-casework", command.CommandId, sourceBreak.BreakId);
        if (current.AuditEvents.Any(audit => string.Equals(audit.EventId, eventId, StringComparison.Ordinal)))
        {
            return;
        }

        var occurredAt = item.LastUpdatedAt.ToUniversalTime();
        var actor = command.Actor.Trim();
        var isReopen = IsReopen(command, item);
        var nextCaseStatus = isReopen
            ? "Open"
            : sourceStatus switch
            {
                "SignedOff" => "SignedOff",
                "Waived" or "Superseded" => "Dismissed",
                _ => "Resolved"
            };
        var reason = FirstNonBlank(
            command.Reason,
            command.Note,
            item.DispositionReason,
            item.ResolutionNote,
            $"Governed queue casework applied {command.Action}.")!;
        var retainedEvidence = current.EvidenceReferences
            .Concat(evidence)
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static link => link, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var history = current.History.Concat(
        [
            new ReconciliationCaseHistoryEntry(
                occurredAt,
                current.Status,
                nextCaseStatus,
                reason)
            {
                Actor = actor,
                EvidenceId = retainedEvidence.FirstOrDefault()
            }
        ]).ToArray();
        var auditDetail =
            $"action={command.Action}; commandId={command.CommandId}; correlationId={command.CorrelationId}; " +
            $"sourceBreakId={sourceBreak.BreakId}; disposition={disposition}; reason={reason}";
        var auditEvents = current.AuditEvents.Concat(
        [
            new ReconciliationCaseAuditEvent(
                eventId,
                isReopen ? "StatementBreakReopened" : "StatementBreakDisposed",
                occurredAt,
                actor,
                auditDetail)
        ]).ToArray();
        var decisionNotes = isReopen
            ? current.DecisionNotes
            : current.DecisionNotes.Concat(
            [
                new ReconciliationCaseDecisionNote(
                    BuildStableId("statement-decision", command.CommandId, sourceBreak.BreakId),
                    actor,
                    occurredAt,
                    reason,
                    retainedEvidence)
            ]).ToArray();
        var resolution = isReopen
            ? null
            : new ReconciliationResolutionMetadata(
                disposition,
                reason,
                actor,
                occurredAt,
                nextCaseStatus == "SignedOff" ? FirstNonBlank(item.SignedOffBy, actor) : null,
                nextCaseStatus == "SignedOff" ? item.SignedOffAt ?? occurredAt : null);
        var updated = current with
        {
            Status = nextCaseStatus,
            Owner = FirstNonBlank(item.AssigneeId, item.AssignedTo, item.ResolvedBy, actor) ?? actor,
            LastUpdatedAtUtc = occurredAt,
            LastUpdatedBy = actor,
            Disposition = isReopen ? "NeedsInvestigation" : disposition,
            History = history,
            AuditEvents = auditEvents,
            DecisionNotes = decisionNotes,
            EvidenceReferences = retainedEvidence,
            Resolution = resolution
        };

        try
        {
            await _statementCaseStore.SaveAsync(updated, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "STATEMENT_CASE_SYNCHRONIZATION_FAILED",
                $"Source statement break '{sourceBreak.BreakId}' was synchronized, but reconciliation case '{caseId}' could not be retained.",
                exception);
        }
    }

    private async Task<ReconciliationCaseworkCloseScopeDto> AttachOperationsContinuityEvidenceAsync(
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem item,
        ReconciliationBreakRecord sourceBreak,
        string disposition,
        IReadOnlyList<string> evidence,
        CancellationToken ct)
    {
        CanonicalStatementImport import;
        try
        {
            var imports = await _statementRuns!.ListImportsAsync(ct).ConfigureAwait(false);
            import = imports.FirstOrDefault(candidate =>
                    string.Equals(candidate.ImportId, sourceBreak.ImportId, StringComparison.OrdinalIgnoreCase))
                ?? throw Failure(
                    "STATEMENT_IMPORT_NOT_FOUND",
                    $"Statement import '{sourceBreak.ImportId}' was not found for Operations Continuity handoff.");
        }
        catch (StatementReconciliationCaseworkHandoffException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "STATEMENT_IMPORT_READ_FAILED",
                $"Statement import '{sourceBreak.ImportId}' could not be read for Operations Continuity handoff.",
                exception);
        }

        if (!Guid.TryParse(import.FundAccountId, out var fundAccountId) || fundAccountId == Guid.Empty)
        {
            throw Failure(
                "OPERATIONS_FUND_ACCOUNT_ID_REQUIRED",
                $"Statement import '{import.ImportId}' does not carry a canonical fund-account id for Operations Continuity.");
        }

        IReadOnlyList<OperationsContinuityWorkflowSummaryDto> workflows;
        try
        {
            var expectedLedgerBookId =
                item.LedgerBookId
                ?? import.AccountingScope?.LedgerBookId;
            workflows = await _operationsContinuity!
                .ListAsync(
                    fundAccountId,
                    periodId: null,
                    status: null,
                    ct: ct,
                    ledgerBookId: expectedLedgerBookId)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "OPERATIONS_WORKFLOW_LOOKUP_FAILED",
                $"Operations Continuity could not resolve the workflow for statement import '{import.ImportId}'.",
                exception);
        }

        var matching = await ResolveMatchingWorkflowsAsync(workflows, item, import, ct)
            .ConfigureAwait(false);
        if (matching.Count != 1)
        {
            throw Failure(
                matching.Count == 0
                    ? "OPERATIONS_WORKFLOW_REQUIRED"
                    : "OPERATIONS_WORKFLOW_AMBIGUOUS",
                matching.Count == 0
                    ? $"No Operations Continuity workflow matches fund account '{fundAccountId}' and statement period '{FormatPeriod(import)}'."
                    : $"More than one Operations Continuity workflow matches fund account '{fundAccountId}' and statement period '{FormatPeriod(import)}'.");
        }

        var workflow = await _operationsContinuity.GetAsync(matching[0].WorkflowId, ct).ConfigureAwait(false)
            ?? throw Failure(
                "OPERATIONS_WORKFLOW_NOT_FOUND",
                $"Operations Continuity workflow '{matching[0].WorkflowId}' was not found during statement evidence handoff.");
        if (workflow.Status == OperationsWorkflowStatusDto.Closed)
        {
            throw Failure(
                "OPERATIONS_WORKFLOW_CLOSED",
                $"Operations Continuity workflow '{workflow.WorkflowId}' is closed; reopen it through governed close controls before attaching reopened statement casework.");
        }
        if (workflow.FundAccountId != fundAccountId)
        {
            throw Failure(
                "OPERATIONS_WORKFLOW_SCOPE_MISMATCH",
                $"Operations Continuity workflow '{workflow.WorkflowId}' does not match statement fund account '{fundAccountId:D}'.");
        }

        var closeScope = await ResolveAccountingCloseScopeAsync(workflow, import, item, ct)
            .ConfigureAwait(false);

        var evidenceId = BuildStableId("statement-casework-evidence", command.CommandId, sourceBreak.BreakId);
        var timeline = await _operationsContinuity.GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
        if (timeline.Any(entry => entry.References.Any(reference =>
                string.Equals(reference.EvidenceId, evidenceId, StringComparison.Ordinal))))
        {
            return closeScope;
        }

        var route = evidence.FirstOrDefault(static link =>
                link.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                link.StartsWith("/workstation/", StringComparison.OrdinalIgnoreCase))
            ?? sourceBreak.EvidenceLink
            ?? $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(import.ImportId)}";
        var operationEvidence = new OperationsEvidenceLinkDto(
            EvidenceId: evidenceId,
            Label: IsReopen(command, item)
                ? "Statement reconciliation case reopened"
                : $"Statement reconciliation case {disposition.ToLowerInvariant()}",
            Route: route,
            Source: "statement-reconciliation-casework",
            CapturedAtUtc: item.LastUpdatedAt.ToUniversalTime());
        OperationsTransitionResultDto handoff;
        try
        {
            handoff = await _operationsContinuity
                .RefreshGatePostureAsync(
                    workflow.WorkflowId,
                    new OperationsGatePostureRequestDto(
                        ExpectedVersion: workflow.Version,
                        Actor: command.Actor,
                        Rationale: FirstNonBlank(
                            command.Reason,
                            command.Note,
                            item.DispositionReason,
                            $"Retain statement reconciliation casework {command.Action}."),
                        CorrelationId: command.CorrelationId,
                        EvidenceLinks: [operationEvidence]),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "OPERATIONS_HANDOFF_FAILED",
                $"Statement source casework was synchronized, but Operations Continuity workflow '{workflow.WorkflowId}' rejected the evidence handoff.",
                exception);
        }

        if (!handoff.Success)
        {
            throw Failure(
                FirstNonBlank(handoff.ErrorCode, "OPERATIONS_HANDOFF_BLOCKED")!,
                FirstNonBlank(
                    handoff.ErrorMessage,
                    handoff.Blockers.FirstOrDefault()?.Message,
                    $"Operations Continuity workflow '{workflow.WorkflowId}' did not retain the statement evidence handoff.")!);
        }

        return closeScope;
    }

    private void EnsureStatementDependencies()
    {
        if (_statementBreakStore is null)
        {
            throw Failure(
                "STATEMENT_BREAK_STORE_REQUIRED",
                "The durable source statement break store is not registered.");
        }

        if (_statementCaseStore is null)
        {
            throw Failure(
                "STATEMENT_CASE_STORE_REQUIRED",
                "The durable source statement reconciliation case store is not registered.");
        }

        if (_statementRuns is null)
        {
            throw Failure(
                "STATEMENT_RUN_SERVICE_REQUIRED",
                "The statement run workflow service is not registered.");
        }

        if (_operationsContinuity is null)
        {
            throw Failure(
                "OPERATIONS_CONTINUITY_REQUIRED",
                "The Operations Continuity workflow service is not registered.");
        }

    }

    private async Task<ReconciliationCaseworkCloseScopeDto> ResolveAccountingCloseScopeAsync(
        OperationsContinuityWorkflowDto workflow,
        CanonicalStatementImport import,
        ReconciliationBreakQueueItem item,
        CancellationToken ct)
    {
        if (_ledgerJournalStore is null)
        {
            throw Failure(
                "LEDGER_JOURNAL_STORE_REQUIRED",
                "The durable ledger journal store is required to bind statement casework to an exact fund, ledger-book, accounting-period, and as-of scope.");
        }

        if (!workflow.LedgerBookId.HasValue || workflow.LedgerBookId.Value == Guid.Empty)
        {
            throw Failure(
                "OPERATIONS_LEDGER_BOOK_REQUIRED",
                $"Operations Continuity workflow '{workflow.WorkflowId}' has no exact ledger-book scope.");
        }

        var ledgerBookId = workflow.LedgerBookId.Value;
        LedgerBookRecord ledgerBook;
        try
        {
            ledgerBook = await _ledgerJournalStore!
                .GetLedgerBookAsync(ledgerBookId, ct)
                .ConfigureAwait(false)
                ?? throw Failure(
                    "LEDGER_BOOK_NOT_FOUND",
                    $"Ledger book '{ledgerBookId:D}' was not found for Operations Continuity workflow '{workflow.WorkflowId}'.");
        }
        catch (StatementReconciliationCaseworkHandoffException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "LEDGER_BOOK_LOOKUP_FAILED",
                $"Ledger book '{ledgerBookId:D}' could not be resolved for statement casework handoff.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(ledgerBook.FundProfileId))
        {
            throw Failure(
                "FUND_PROFILE_SCOPE_REQUIRED",
                $"Ledger book '{ledgerBookId:D}' has no authoritative fund-profile scope.");
        }

        IReadOnlyList<LedgerAccountingPeriod> candidates;
        try
        {
            if (Guid.TryParse(workflow.PeriodId, out var exactPeriodId) && exactPeriodId != Guid.Empty)
            {
                var exact = await _ledgerJournalStore!
                    .GetPeriodAsync(exactPeriodId, ct)
                    .ConfigureAwait(false);
                candidates = exact is null ? [] : [exact];
            }
            else
            {
                candidates = (await _ledgerJournalStore!
                        .ListPeriodsAsync(ledgerBookId, ct: ct)
                        .ConfigureAwait(false))
                    .Where(period =>
                        string.Equals(period.Label, workflow.PeriodId, StringComparison.OrdinalIgnoreCase)
                        || (period.StartDate == import.StatementPeriodStart
                            && period.EndDate == import.StatementPeriodEnd))
                    .ToArray();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(
                "ACCOUNTING_PERIOD_LOOKUP_FAILED",
                $"Accounting period '{workflow.PeriodId}' could not be resolved for statement casework handoff.",
                exception);
        }

        var exactCandidates = candidates
            .Where(period =>
                period.PeriodId != Guid.Empty
                && period.LedgerBookId == ledgerBookId
                && period.StartDate == import.StatementPeriodStart
                && period.EndDate == import.StatementPeriodEnd)
            .DistinctBy(static period => period.PeriodId)
            .ToArray();
        if (exactCandidates.Length != 1)
        {
            throw Failure(
                exactCandidates.Length == 0
                    ? "ACCOUNTING_PERIOD_SCOPE_REQUIRED"
                    : "ACCOUNTING_PERIOD_SCOPE_AMBIGUOUS",
                exactCandidates.Length == 0
                    ? $"Operations Continuity workflow '{workflow.WorkflowId}' does not resolve to the exact statement accounting period '{FormatPeriod(import)}' on ledger book '{ledgerBookId:D}'."
                    : $"Operations Continuity workflow '{workflow.WorkflowId}' resolves to more than one statement accounting period on ledger book '{ledgerBookId:D}'.");
        }

        var period = exactCandidates[0];
        var closeScope = new ReconciliationCaseworkCloseScopeDto(
            ledgerBook.FundProfileId.Trim(),
            ledgerBookId,
            period.PeriodId,
            period.EndDate);
        EnsureAuthorityScopeMatches(import, item, closeScope);
        return closeScope;
    }

    private static void EnsureAuthorityScopeMatches(
        CanonicalStatementImport import,
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCloseScopeDto resolved)
    {
        var imported = import.AccountingScope;
        if (imported is not null
            && (!string.Equals(
                    imported.FundProfileId,
                    resolved.FundProfileId,
                    StringComparison.OrdinalIgnoreCase)
                || imported.LedgerBookId != resolved.LedgerBookId
                || imported.AccountingPeriodId != resolved.AccountingPeriodId
                || imported.AsOfDate != resolved.AsOfDate))
        {
            throw Failure(
                "STATEMENT_ACCOUNTING_SCOPE_MISMATCH",
                $"Statement import '{import.ImportId}' is bound to a different fund, ledger-book, accounting-period, or as-of authority than Operations Continuity workflow '{resolved.AccountingPeriodId:D}'.");
        }

        var retainedPeriodId = Guid.TryParse(item.AccountingPeriodId, out var parsedPeriodId)
            ? parsedPeriodId
            : (Guid?)null;
        if ((!string.IsNullOrWhiteSpace(item.FundProfileId)
             && !string.Equals(
                 item.FundProfileId,
                 resolved.FundProfileId,
                 StringComparison.OrdinalIgnoreCase))
            || (item.LedgerBookId.HasValue
                && item.LedgerBookId.Value != resolved.LedgerBookId)
            || (retainedPeriodId.HasValue
                && retainedPeriodId.Value != resolved.AccountingPeriodId)
            || (item.AsOfDate.HasValue
                && item.AsOfDate.Value != resolved.AsOfDate))
        {
            throw Failure(
                "STATEMENT_CASEWORK_SCOPE_MISMATCH",
                $"Reconciliation case '{item.BreakId}' is already bound to a different fund, ledger-book, accounting-period, or as-of authority and cannot be rebound by Operations Continuity.");
        }
    }

    private static bool RequiresStatementSynchronization(
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem item)
    {
        if (!string.Equals(item.SourceType, "statement", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsReopen(command, item) ||
               item.Disposition.HasValue ||
               item.Status is ReconciliationBreakQueueStatus.Resolved
                   or ReconciliationBreakQueueStatus.Dismissed
                   or ReconciliationBreakQueueStatus.SignedOff ||
               item.LifecycleState is ReconciliationCaseLifecycleState.Resolved
                   or ReconciliationCaseLifecycleState.SignedOff
                   or ReconciliationCaseLifecycleState.Superseded;
    }

    private static bool IsReopen(
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem item)
        => command.Action == ReconciliationCaseworkAction.Reopen ||
           item.LifecycleState == ReconciliationCaseLifecycleState.Reopened;

    private static string ResolveDisposition(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCommand command)
    {
        if (IsReopen(command, item))
        {
            return "Reopened";
        }

        return item.Disposition?.ToString() ??
               command.Action switch
               {
                   ReconciliationCaseworkAction.Waive => "Waived",
                   ReconciliationCaseworkAction.Supersede => "Superseded",
                   _ when item.Status == ReconciliationBreakQueueStatus.Dismissed => "Waived",
                   _ => "Resolved"
               };
    }

    private static string ResolveSourceStatus(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCommand command,
        string disposition)
    {
        if (IsReopen(command, item))
        {
            return "Open";
        }

        if (item.Status == ReconciliationBreakQueueStatus.SignedOff ||
            item.LifecycleState == ReconciliationCaseLifecycleState.SignedOff)
        {
            return "SignedOff";
        }

        return disposition;
    }

    private static IReadOnlyList<string> BuildEvidence(
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem item)
    {
        var evidence = (command.EvidenceLinks ?? [])
            .Concat(item.EvidenceLinks ?? [])
            .Where(static link => !StatementCaseworkHandoffObligation.IsControlMarker(link))
            .ToList();
        if (!string.IsNullOrWhiteSpace(item.DispositionEvidenceHash))
        {
            evidence.Add($"urn:sha256:{item.DispositionEvidenceHash.Trim()}");
        }

        evidence.Add($"/api/workstation/reconciliation/break-queue/{Uri.EscapeDataString(item.BreakId)}");
        return evidence
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static link => link, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool PeriodMatches(string periodId, CanonicalStatementImport import)
    {
        if (string.IsNullOrWhiteSpace(periodId))
        {
            return false;
        }

        var normalized = periodId.Trim();
        var start = import.StatementPeriodStart;
        var end = import.StatementPeriodEnd;
        var exactKeys = new[]
        {
            end.ToString("yyyy-MM"),
            end.ToString("yyyy-MM-dd"),
            $"{start:yyyy-MM-dd}:{end:yyyy-MM-dd}",
            $"{start:yyyy-MM-dd}/{end:yyyy-MM-dd}",
            $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}"
        };
        return exactKeys.Any(key => string.Equals(normalized, key, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ResolveMatchingWorkflowsAsync(
        IReadOnlyList<OperationsContinuityWorkflowSummaryDto> workflows,
        ReconciliationBreakQueueItem item,
        CanonicalStatementImport import,
        CancellationToken ct)
    {
        var matching = new List<OperationsContinuityWorkflowSummaryDto>();
        var expectedLedgerBookId =
            item.LedgerBookId
            ?? import.AccountingScope?.LedgerBookId;
        var expectedAccountingPeriodId =
            Guid.TryParse(item.AccountingPeriodId, out var retainedPeriodId)
                && retainedPeriodId != Guid.Empty
                ? retainedPeriodId
                : import.AccountingScope?.AccountingPeriodId;
        foreach (var workflow in workflows)
        {
            if (expectedLedgerBookId.HasValue
                && workflow.LedgerBookId != expectedLedgerBookId)
            {
                continue;
            }

            if (expectedAccountingPeriodId.HasValue
                && Guid.TryParse(workflow.PeriodId, out var workflowPeriodId)
                && workflowPeriodId != Guid.Empty
                && workflowPeriodId != expectedAccountingPeriodId.Value)
            {
                continue;
            }

            if ((!string.IsNullOrWhiteSpace(item.AccountingPeriodId) &&
                 string.Equals(
                     workflow.PeriodId,
                     item.AccountingPeriodId,
                     StringComparison.OrdinalIgnoreCase)) ||
                PeriodMatches(workflow.PeriodId, import))
            {
                matching.Add(workflow);
                continue;
            }

            if (_ledgerJournalStore is null ||
                !Guid.TryParse(workflow.PeriodId, out var periodId) ||
                periodId == Guid.Empty)
            {
                continue;
            }

            LedgerAccountingPeriod? period;
            try
            {
                period = await _ledgerJournalStore.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Failure(
                    "ACCOUNTING_PERIOD_LOOKUP_FAILED",
                    $"Accounting period '{workflow.PeriodId}' could not be resolved for the statement handoff.",
                    exception);
            }

            if (period is not null &&
                (!expectedLedgerBookId.HasValue
                 || period.LedgerBookId == expectedLedgerBookId.Value) &&
                (!expectedAccountingPeriodId.HasValue
                 || period.PeriodId == expectedAccountingPeriodId.Value) &&
                period.StartDate == import.StatementPeriodStart &&
                period.EndDate == import.StatementPeriodEnd)
            {
                matching.Add(workflow);
            }
        }

        return matching;
    }

    private static string FormatPeriod(CanonicalStatementImport import)
        => $"{import.StatementPeriodStart:yyyy-MM-dd}:{import.StatementPeriodEnd:yyyy-MM-dd}";

    private static string BuildStableId(string prefix, string commandId, string breakId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{prefix}|{commandId.Trim()}|{breakId.Trim()}"))).ToLowerInvariant();
        return $"{prefix}:{hash[..24]}";
    }

    private static string Require(string? value, string code, string message)
        => !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw Failure(code, message);

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static StatementReconciliationCaseworkHandoffException Failure(
        string code,
        string message,
        Exception? exception = null)
        => new(code, message, exception);
}
