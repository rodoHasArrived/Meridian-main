using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    private async Task<OperationsTransitionResultDto> ApplyCommandAsync(
        Guid workflowId,
        long expectedVersion,
        string actor,
        string? rationale,
        string? correlationId,
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks,
        string eventType,
        OperationsGateKeyDto? gate,
        Func<OperationsContinuityWorkflow, OperationsWorkflowBlockerDto?>? precondition = null,
        Func<OperationsContinuityWorkflow, IReadOnlyList<OperationsEvidenceLinkDto>, DateTimeOffset, OperationsWorkflowBlockerDto?>? command = null,
        bool allowClosedWorkflow = false,
        bool requireIntactAuditChain = false,
        CancellationToken ct = default)
    {
        if (workflowId == Guid.Empty)
        {
            return Failure("VALIDATION_FAILED", "Workflow id is required.",
            [
                new OperationsWorkflowBlockerDto("WORKFLOW_ID_REQUIRED", "Workflow id is required.", null, "Error", [])
            ]);
        }

        if (string.IsNullOrWhiteSpace(actor))
        {
            return Failure("VALIDATION_FAILED", "Actor is required for workflow transitions.",
            [
                new OperationsWorkflowBlockerDto("ACTOR_REQUIRED", "Actor is required for workflow transitions.", gate, "Error", [])
            ]);
        }

        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return Failure("NOT_FOUND", "Workflow was not found.", []);
        }

        var evidence = OperationsContinuityWorkflowText.NormalizeEvidence(evidenceLinks);

        if (workflow.Version != expectedVersion)
        {
            var blocker = new OperationsWorkflowBlockerDto(
                "WORKFLOW_VERSION_MISMATCH",
                "Refresh the workflow before retrying the command.",
                null,
                "Error",
                []);
            return await PersistBlockedAttemptAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                eventType,
                gate,
                "VERSION_MISMATCH",
                $"Workflow version {workflow.Version} does not match expected version {expectedVersion}.",
                [blocker],
                evidence,
                ct).ConfigureAwait(false);
        }

        if (workflow.IsClosed && !allowClosedWorkflow)
        {
            var blocker = CreateClosedWorkflowBlocker(gate);
            return await PersistBlockedAttemptAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                eventType,
                gate,
                "INVALID_STATE_TRANSITION",
                blocker.Message,
                [blocker],
                evidence,
                ct).ConfigureAwait(false);
        }

        if (precondition?.Invoke(workflow) is { } preconditionBlocker)
        {
            return await PersistBlockedAttemptAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                eventType,
                gate,
                "INVALID_STATE_TRANSITION",
                preconditionBlocker.Message,
                [preconditionBlocker],
                evidence,
                ct).ConfigureAwait(false);
        }

        if (requireIntactAuditChain)
        {
            var auditTimeline = await _auditStore.GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
            if (!OperationsWorkflowAuditHashing.TryValidateChain(auditTimeline, out var auditBlockerCode, out var auditMessage))
            {
                var blocker = new OperationsWorkflowBlockerDto(
                    auditBlockerCode,
                    auditMessage,
                    gate,
                    "Critical",
                    []);
                // Never append to a chain already known to be invalid. The response remains an
                // honest blocked receipt, but recovery must repair the retained chain first.
                return CreateResult(
                    CreateOperationOutcome(
                        workflow.WorkflowId,
                        expectedVersion,
                        eventType,
                        actor,
                        rationale,
                        correlationId,
                        OperationTerminalState.Blocked,
                        [blocker],
                        [],
                        blocker.Message,
                        DateTimeOffset.UtcNow),
                    "INVALID_STATE_TRANSITION",
                    blocker.Message,
                    workflow: null,
                    [blocker],
                    []);
            }
        }

        if (_transitionCommitStore is null)
        {
            var blocker = new OperationsWorkflowBlockerDto(
                "OPERATIONS_ATOMIC_COMMIT_UNAVAILABLE",
                "The workflow transition was withheld because no store can atomically retain state and audit evidence.",
                gate,
                "Critical",
                evidence);
            return await PersistBlockedAttemptAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                eventType,
                gate,
                "ATOMIC_COMMIT_UNAVAILABLE",
                blocker.Message,
                [blocker],
                evidence,
                ct).ConfigureAwait(false);
        }

        var fromStatus = _statusDerivation.Derive(workflow);
        var fromGateStatus = gate.HasValue ? GetGate(workflow, gate.Value).Status : (OperationsGateStatusDto?)null;
        var now = DateTimeOffset.UtcNow;
        var workflowForCommit = CloneWorkflow(workflow);
        if (command?.Invoke(workflowForCommit, evidence, now) is { } commandBlocker)
        {
            return await PersistBlockedAttemptAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                eventType,
                gate,
                "INVALID_STATE_TRANSITION",
                commandBlocker.Message,
                [commandBlocker],
                evidence,
                ct).ConfigureAwait(false);
        }

        var toStatus = _statusDerivation.Derive(workflowForCommit);
        var toGateStatus = gate.HasValue ? GetGate(workflowForCommit, gate.Value).Status : (OperationsGateStatusDto?)null;
        var outcome = CreateOperationOutcome(
            workflowForCommit.WorkflowId,
            expectedVersion,
            eventType,
            actor,
            rationale,
            correlationId,
            OperationTerminalState.Succeeded,
            [],
            [],
            null,
            now);
        var auditDraft = new OperationsWorkflowAuditDraft(
            workflowForCommit.WorkflowId,
            workflowForCommit.FundAccountId,
            workflowForCommit.PeriodId,
            eventType,
            fromStatus,
            toStatus,
            gate,
            fromGateStatus,
            toGateStatus,
            actor.Trim(),
            OperationsContinuityWorkflowText.RedactSensitiveText(rationale),
            outcome.CorrelationId,
            evidence,
            Outcome: outcome);

        try
        {
            var commit = await _transitionCommitStore
                .CommitWorkflowTransitionAsync(workflowForCommit, auditDraft, persistWorkflowState: true, ct)
                .ConfigureAwait(false);
            var dto = await ToDtoAsync(commit.Workflow, ct).ConfigureAwait(false);
            return CreateResult(outcome, null, null, dto, dto.Blockers, dto.NextActions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await HandleTransitionCommitFailureAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                eventType,
                gate,
                evidence,
                ex,
                ct).ConfigureAwait(false);
        }
    }

    private async Task<OperationsTransitionResultDto> PersistBlockedAttemptAsync(
        OperationsContinuityWorkflow workflow,
        long expectedVersion,
        string actor,
        string? rationale,
        string? correlationId,
        string attemptedEventType,
        OperationsGateKeyDto? gate,
        string errorCode,
        string errorMessage,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence,
        CancellationToken ct)
    {
        var nextActions = BuildRecoveryNextActions(blockers);
        var effectiveBlockers = blockers;
        if (_transitionCommitStore is null &&
            !blockers.Any(static blocker => blocker.Code == "OPERATIONS_ATOMIC_COMMIT_UNAVAILABLE"))
        {
            effectiveBlockers = blockers.Append(new OperationsWorkflowBlockerDto(
                "OPERATIONS_ATOMIC_COMMIT_UNAVAILABLE",
                "The blocked attempt could not be durably retained because no atomic Operations Continuity commit store is available.",
                gate,
                "Critical",
                evidence)).ToArray();
            nextActions = BuildRecoveryNextActions(effectiveBlockers);
        }

        var now = DateTimeOffset.UtcNow;
        var outcome = CreateOperationOutcome(
            workflow.WorkflowId,
            expectedVersion,
            attemptedEventType,
            actor,
            rationale,
            correlationId,
            OperationTerminalState.Blocked,
            effectiveBlockers,
            nextActions,
            errorMessage,
            now);

        if (_transitionCommitStore is null)
        {
            var currentDto = await ToDtoAsync(workflow, ct).ConfigureAwait(false);
            return CreateResult(
                outcome,
                errorCode,
                errorMessage,
                currentDto,
                effectiveBlockers,
                nextActions);
        }

        var status = _statusDerivation.Derive(workflow);
        var gateStatus = gate.HasValue ? GetGate(workflow, gate.Value).Status : (OperationsGateStatusDto?)null;
        var auditDraft = new OperationsWorkflowAuditDraft(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            "workflow-transition-blocked",
            status,
            status,
            gate,
            gateStatus,
            gateStatus,
            actor.Trim(),
            BuildAttemptRationale(attemptedEventType, rationale, errorMessage),
            outcome.CorrelationId,
            evidence,
            Outcome: outcome);

        try
        {
            var commit = await _transitionCommitStore
                .CommitWorkflowTransitionAsync(workflow, auditDraft, persistWorkflowState: false, ct: ct)
                .ConfigureAwait(false);
            var currentDto = await ToDtoAsync(commit.Workflow, ct).ConfigureAwait(false);
            return CreateResult(
                outcome,
                errorCode,
                errorMessage,
                currentDto,
                effectiveBlockers,
                nextActions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await HandleTransitionCommitFailureAsync(
                workflow,
                expectedVersion,
                actor,
                rationale,
                correlationId,
                attemptedEventType,
                gate,
                evidence,
                ex,
                ct).ConfigureAwait(false);
        }
    }

    private async Task<OperationsTransitionResultDto> HandleTransitionCommitFailureAsync(
        OperationsContinuityWorkflow workflow,
        long expectedVersion,
        string actor,
        string? rationale,
        string? correlationId,
        string attemptedEventType,
        OperationsGateKeyDto? gate,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence,
        Exception exception,
        CancellationToken ct)
    {
        var blocker = new OperationsWorkflowBlockerDto(
            "OPERATIONS_TRANSITION_COMMIT_FAILED",
            "The workflow transition was not accepted because its state and audit evidence could not be committed together.",
            gate,
            "Critical",
            evidence);
        var nextActions = BuildRecoveryNextActions([blocker]);
        var now = DateTimeOffset.UtcNow;
        var outcome = CreateOperationOutcome(
            workflow.WorkflowId,
            expectedVersion,
            attemptedEventType,
            actor,
            rationale,
            correlationId,
            OperationTerminalState.Failed,
            [blocker],
            nextActions,
            blocker.Message,
            now,
            exception.GetType().Name);

        if (_transitionCommitStore is not null)
        {
            var status = _statusDerivation.Derive(workflow);
            var gateStatus = gate.HasValue ? GetGate(workflow, gate.Value).Status : (OperationsGateStatusDto?)null;
            var failureDraft = new OperationsWorkflowAuditDraft(
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                "workflow-transition-failed",
                status,
                status,
                gate,
                gateStatus,
                gateStatus,
                actor.Trim(),
                BuildAttemptRationale(attemptedEventType, rationale, blocker.Message),
                outcome.CorrelationId,
                evidence,
                Outcome: outcome);
            try
            {
                await _transitionCommitStore
                    .CommitWorkflowTransitionAsync(workflow, failureDraft, persistWorkflowState: false, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (Exception retentionException) when (retentionException is not OperationCanceledException)
            {
                // The returned outcome remains Failed. Never convert a persistence failure to
                // Succeeded merely because the secondary failure receipt could not be retained.
            }
        }

        OperationsContinuityWorkflowDto? currentDto = null;
        try
        {
            currentDto = await ToDtoAsync(workflow, ct).ConfigureAwait(false);
        }
        catch (Exception projectionException) when (projectionException is not OperationCanceledException)
        {
            // The authoritative result remains Failed even when the unavailable persistence
            // dependency also prevents rebuilding the response projection.
        }

        return CreateResult(
            outcome,
            "PERSISTENCE_FAILED",
            blocker.Message,
            currentDto,
            [blocker],
            nextActions);
    }

    private static OperationsTransitionResultDto CreateResult(
        VerifiedOperationOutcome outcome,
        string? errorCode,
        string? errorMessage,
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers,
        IReadOnlyList<OperationsNextActionDto> nextActions) =>
        new(
            outcome,
            errorCode,
            errorMessage,
            workflow,
            blockers,
            nextActions,
            workflow?.Version,
            workflow?.CloseReadiness);

    private static VerifiedOperationOutcome CreateOperationOutcome(
        Guid workflowId,
        long expectedVersion,
        string operationKind,
        string actor,
        string? rationale,
        string? correlationId,
        OperationTerminalState state,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers,
        IReadOnlyList<OperationsNextActionDto> nextActions,
        string? message,
        DateTimeOffset completedAtUtc,
        string? exceptionType = null)
    {
        var operationId = $"operations-continuity-{Guid.NewGuid():N}";
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? operationId
            : OperationsContinuityWorkflowText.RedactSensitiveText(correlationId) ?? operationId;
        var evidenceId = $"{operationId}-receipt";
        var postconditionState = state is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
            ? OperationPostconditionState.Satisfied
            : OperationPostconditionState.NotSatisfied;
        var issues = BuildOutcomeIssues(state, blockers, message, exceptionType, evidenceId);
        var recovery = BuildOutcomeRecovery(state, nextActions, message, evidenceId);
        var attemptNumber = expectedVersion <= 0
            ? 1
            : expectedVersion >= int.MaxValue
                ? int.MaxValue
                : (int)expectedVersion + 1;

        var outcome = new VerifiedOperationOutcome(
            operationId,
            operationKind,
            state,
            completedAtUtc,
            completedAtUtc,
            attemptNumber,
            normalizedCorrelationId,
            ComputeOperationInputHash(
                workflowId,
                expectedVersion,
                operationKind,
                actor,
                rationale,
                normalizedCorrelationId,
                blockers),
            [new OperationPostcondition(
                "workflow-state-and-audit-committed",
                "The Operations Continuity state transition and its audit evidence share one authoritative commit.",
                postconditionState,
                Required: true,
                EvidenceIds: [evidenceId])],
            [new OperationEvidenceReference(
                evidenceId,
                "operations-continuity-transition-receipt",
                "Outcome receipt retained with the Operations Continuity audit attempt.",
                Uri: $"urn:meridian:operations-continuity:{workflowId:N}:{operationId}",
                CapturedAtUtc: completedAtUtc)],
            [],
            issues,
            recovery);
        return VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);
    }

    private static IReadOnlyList<OperationIssue> BuildOutcomeIssues(
        OperationTerminalState state,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers,
        string? message,
        string? exceptionType,
        string evidenceId)
    {
        if (state == OperationTerminalState.Succeeded)
        {
            return [];
        }

        if (state == OperationTerminalState.CompletedWithWarnings)
        {
            return [new OperationIssue(
                "OPERATIONS_TRANSITION_WARNING",
                message ?? "The workflow transition completed with retained warnings.",
                OperationIssueSeverity.Warning,
                EvidenceId: evidenceId)];
        }

        var source = blockers.Count > 0
            ? blockers
            : [new OperationsWorkflowBlockerDto(
                state == OperationTerminalState.Blocked
                    ? "OPERATIONS_TRANSITION_BLOCKED"
                    : "OPERATIONS_TRANSITION_FAILED",
                message ?? "The workflow transition did not satisfy its required postcondition.",
                null,
                "Error",
                [])];
        return source
            .GroupBy(static blocker => blocker.Code, StringComparer.Ordinal)
            .Select(group => new OperationIssue(
                group.Key,
                group.First().Message,
                OperationIssueSeverity.Error,
                exceptionType,
                evidenceId)
            {
                IsBlocking = state == OperationTerminalState.Blocked
            })
            .ToArray();
    }

    private static IReadOnlyList<OperationRecoveryAction> BuildOutcomeRecovery(
        OperationTerminalState state,
        IReadOnlyList<OperationsNextActionDto> nextActions,
        string? message,
        string evidenceId)
    {
        if (state == OperationTerminalState.Succeeded)
        {
            return [];
        }

        var actions = nextActions
            .GroupBy(static action => action.Code, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (actions.Length == 0)
        {
            return [new OperationRecoveryAction(
                "review-and-retry",
                "Review and retry",
                message ?? "Review the retained outcome, resolve the issue, and retry when permitted.",
                Retryable: true,
                RequiresHumanAction: true,
                Route: "/workstation/accounting")
            {
                EvidenceIds = [evidenceId]
            }];
        }

        return actions.Select(action => new OperationRecoveryAction(
            action.Code,
            action.Label,
            message ?? action.Label,
            Retryable: true,
            RequiresHumanAction: true,
            Route: action.Route)
        {
            EvidenceIds = [evidenceId]
        }).ToArray();
    }

    private static IReadOnlyList<OperationsNextActionDto> BuildRecoveryNextActions(
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers) =>
        blockers
            .GroupBy(static blocker => blocker.Code, StringComparer.Ordinal)
            .Select(group =>
            {
                var blocker = group.First();
                return new OperationsNextActionDto(
                    $"RESOLVE_{blocker.Code}",
                    $"Resolve {blocker.Code.Replace('_', ' ').ToLowerInvariant()}",
                    "/workstation/accounting",
                    blocker.Gate);
            })
            .ToArray();

    private static string ComputeOperationInputHash(
        Guid workflowId,
        long expectedVersion,
        string operationKind,
        string actor,
        string? rationale,
        string correlationId,
        IReadOnlyList<OperationsWorkflowBlockerDto> blockers)
    {
        var canonical = string.Join('\n',
        [
            workflowId.ToString("N"),
            expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            operationKind,
            actor.Trim(),
            rationale?.Trim() ?? string.Empty,
            correlationId,
            .. blockers
                .OrderBy(static blocker => blocker.Code, StringComparer.Ordinal)
                .Select(static blocker => $"{blocker.Code}:{blocker.Message}")
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string? BuildAttemptRationale(
        string attemptedEventType,
        string? rationale,
        string outcomeMessage)
    {
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Attempted transition: {attemptedEventType}",
                $"Outcome: {outcomeMessage}",
                string.IsNullOrWhiteSpace(rationale) ? null : $"Operator rationale: {rationale.Trim()}"
            }.Where(static value => value is not null));
        return OperationsContinuityWorkflowText.RedactSensitiveText(text);
    }
}
