using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Workflow.Workflows;

public sealed class FundWorkflowCommandHandler
{
    private const string CaseType = "fund-workflow";
    private const string ExpectedPreviousCaseSequenceDataKey = "expectedPreviousCaseSequence";
    private const string ExpectedPreviousCaseRecordHashDataKey = "expectedPreviousCaseRecordHashSha256";
    private static readonly string[] AuthorizedApprovalRoles =
        ["Administrator", "OperationsManager", "Controller", "Compliance"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IOperationalCaseHistoryStore _caseHistoryStore;
    private readonly Dictionary<Guid, WorkflowSnapshot> _workflows = new();
    private readonly Dictionary<Guid, Dictionary<string, RequestIdentity>> _requestIdentities = new();
    private readonly Dictionary<Guid, CaseHistoryHead> _historyHeads = new();
    private readonly Dictionary<Guid, string?> _historyAssignees = new();
    private readonly HashSet<Guid> _hydratedWorkflowIds = [];

    public FundWorkflowCommandHandler(IOperationalCaseHistoryStore caseHistoryStore)
    {
        _caseHistoryStore = caseHistoryStore ?? throw new ArgumentNullException(nameof(caseHistoryStore));
    }

    public Task<FundWorkflowState> HandleAsync(StartWorkflow command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(StartWorkflow), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ImportBrokerData command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ImportBrokerData), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(NormalizeBrokerTransactions command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(NormalizeBrokerTransactions), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ResolveSecurityMasterMappings command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ResolveSecurityMasterMappings), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ApproveSecurityMasterOverrides command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ApproveSecurityMasterOverrides), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(BuildLedgerDraft command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(BuildLedgerDraft), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ValidateLedgerDraft command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ValidateLedgerDraft), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(PostLedgerEntries command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(PostLedgerEntries), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(RunReconciliation command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(RunReconciliation), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ResolveBreakCase command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ResolveBreakCase), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(SubmitForApproval command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(SubmitForApproval), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ApproveWorkflow command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ApproveWorkflow), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(RejectWorkflow command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(RejectWorkflow), command.Metadata, command.ReasonCode.ToString(), ct);

    public Task<FundWorkflowState> HandleAsync(CloseWorkflow command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(CloseWorkflow), command.Metadata, null, ct);

    public Task<FundWorkflowState> HandleAsync(ReopenWorkflow command, CancellationToken ct = default) =>
        ApplyAsync(command.WorkflowId, nameof(ReopenWorkflow), command.Metadata, command.Reason, ct);

    private async Task<FundWorkflowState> ApplyAsync(
        Guid workflowId,
        string operation,
        FundWorkflowCommandMetadata metadata,
        string? operationReason,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var inputHash = ComputeInputHash(workflowId, operation, metadata, operationReason);
            var eventId = $"fund-workflow:{workflowId:N}:{Guid.NewGuid():N}";
            var occurredAtUtc = DateTimeOffset.UtcNow;
            try
            {
                await EnsureHydratedAsync(workflowId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsCaseHistoryAccessFailure(ex))
            {
                var visibleSnapshot = SnapshotForFailure(workflowId);
                InvalidateHydratedState(workflowId);
                return ToState(
                    visibleSnapshot,
                    CreateCaseHistoryFailureOutcome(
                        eventId,
                        operation,
                        workflowId,
                        metadata,
                        inputHash,
                        occurredAtUtc,
                        OperationTerminalState.Blocked,
                        "read",
                        ex));
            }

            var current = GetOrCreate(workflowId);
            var requestIdentities = GetRequestIdentities(workflowId);

            try
            {
                if (!string.IsNullOrWhiteSpace(metadata.RequestId) &&
                    requestIdentities.TryGetValue(metadata.RequestId, out var existingIdentity))
                {
                    if (!string.Equals(existingIdentity.Operation, operation, StringComparison.Ordinal) ||
                        !string.Equals(existingIdentity.InputHashSha256, inputHash, StringComparison.OrdinalIgnoreCase))
                    {
                        var conflict = new InvalidOperationException(
                            $"Request id '{metadata.RequestId}' was already used for a different fund workflow operation or input.");
                        var blockedOutcome = CreateBlockedOutcome(
                            eventId,
                            operation,
                            workflowId,
                            metadata,
                            inputHash,
                            occurredAtUtc,
                            conflict);
                        await AppendHistoryAsync(
                            workflowId,
                            operation,
                            "idempotency-conflict",
                            metadata,
                            inputHash,
                            conflict.Message,
                            current.OverallStatus,
                            current.OverallStatus,
                            blockedOutcome,
                            eventId,
                            occurredAtUtc,
                            ct,
                            exception: conflict,
                            operationReason: operationReason).ConfigureAwait(false);
                        return ToState(current, blockedOutcome);
                    }

                    return ToState(current, existingIdentity.TerminalOutcome);
                }

                var candidate = current.Clone();
                var previousStatus = candidate.OverallStatus;
                try
                {
                    ValidateApprovalCommand(operation, metadata);
                    ApplyOperation(candidate, operation, operationReason, metadata);
                }
                catch (InvalidOperationException ex)
                {
                    var outcome = CreateBlockedOutcome(
                        eventId,
                        operation,
                        workflowId,
                        metadata,
                        inputHash,
                        occurredAtUtc,
                        ex);
                    await AppendHistoryAsync(
                        workflowId,
                        operation,
                        "rejected",
                        metadata,
                        inputHash,
                        ex.Message,
                        previousStatus,
                        previousStatus,
                        outcome,
                        eventId,
                        occurredAtUtc,
                        ct,
                        exception: ex,
                        operationReason: operationReason).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(metadata.RequestId))
                    {
                        requestIdentities[metadata.RequestId] = new RequestIdentity(operation, inputHash, outcome);
                    }
                    return ToState(current, outcome);
                }

                candidate.OverallStatus = DeriveOverallStatus(candidate);
                candidate.LastActor = metadata.Actor;
                candidate.UpdatedAtUtc = occurredAtUtc;
                var acceptedReason = DescribeAcceptedReason(operation, operationReason);
                var acceptedOutcome = CreateSucceededOutcome(
                    eventId,
                    operation,
                    workflowId,
                    metadata,
                    inputHash,
                    occurredAtUtc,
                    "transition-retained",
                    $"The {operation} transition was accepted and retained in operational case history.");

                await AppendHistoryAsync(
                    workflowId,
                    operation,
                    "accepted",
                    metadata,
                    inputHash,
                    acceptedReason,
                    previousStatus,
                    candidate.OverallStatus,
                    acceptedOutcome,
                    eventId,
                    occurredAtUtc,
                    ct,
                    recoveryAttempt: operation == nameof(ReopenWorkflow),
                    operationReason: operationReason).ConfigureAwait(false);

                _workflows[workflowId] = candidate;
                if (!string.IsNullOrWhiteSpace(metadata.RequestId))
                    requestIdentities[metadata.RequestId] = new RequestIdentity(operation, inputHash, acceptedOutcome);
                return ToState(candidate, acceptedOutcome);
            }
            catch (InvalidOperationException ex) when (IsCaseHistoryConcurrencyConflict(ex))
            {
                try
                {
                    var expectedHead = GetHistoryHead(workflowId);
                    InvalidateHydratedState(workflowId);
                    await EnsureHydratedAsync(workflowId, ct).ConfigureAwait(false);
                    var refreshed = GetOrCreate(workflowId);
                    var retainedHead = GetHistoryHead(workflowId);
                    var completedAtUtc = DateTimeOffset.UtcNow;
                    var concurrencyOutcome = CreateConcurrencyBlockedOutcome(
                        eventId,
                        operation,
                        workflowId,
                        metadata,
                        inputHash,
                        occurredAtUtc,
                        completedAtUtc,
                        expectedHead,
                        retainedHead,
                        ex);
                    await AppendHistoryAsync(
                        workflowId,
                        operation,
                        "concurrency-conflict",
                        metadata,
                        inputHash,
                        ex.Message,
                        refreshed.OverallStatus,
                        refreshed.OverallStatus,
                        concurrencyOutcome,
                        eventId,
                        occurredAtUtc,
                        ct,
                        exception: ex,
                        operationReason: operationReason).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(metadata.RequestId))
                    {
                        GetRequestIdentities(workflowId)[metadata.RequestId] =
                            new RequestIdentity(operation, inputHash, concurrencyOutcome);
                    }
                    return ToState(refreshed, concurrencyOutcome);
                }
                catch (Exception storageException) when (IsCaseHistoryAccessFailure(storageException))
                {
                    return CreateCaseHistoryFailureState(
                        workflowId,
                        operation,
                        metadata,
                        inputHash,
                        eventId,
                        occurredAtUtc,
                        storageException);
                }
            }
            catch (Exception ex) when (IsCaseHistoryAccessFailure(ex))
            {
                return CreateCaseHistoryFailureState(
                    workflowId,
                    operation,
                    metadata,
                    inputHash,
                    eventId,
                    occurredAtUtc,
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask EnsureHydratedAsync(Guid workflowId, CancellationToken ct)
    {
        if (_hydratedWorkflowIds.Contains(workflowId))
            return;

        var records = await _caseHistoryStore.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = workflowId.ToString("D"),
            CaseType = CaseType
        }, ct).ConfigureAwait(false);
        var snapshot = CreateSnapshot(workflowId);
        var requestIdentities = GetRequestIdentities(workflowId);

        foreach (var record in records.OrderBy(item => item.Sequence))
        {
            var requestId = ReadRequestId(record);
            if (!string.IsNullOrWhiteSpace(requestId) &&
                record.Data.TryGetValue("operation", out var requestOperation) &&
                !string.IsNullOrWhiteSpace(requestOperation) &&
                record.TerminalOutcome is not null)
            {
                requestIdentities.TryAdd(
                    requestId,
                    new RequestIdentity(requestOperation, record.InputHashSha256, record.TerminalOutcome));
            }

            if (!TryReadAcceptedOperation(record.EventType, out var operation))
                continue;

            ApplyOperation(snapshot, operation, ReadOperationReason(record), ReplayMetadata(record));
            snapshot.OverallStatus = DeriveOverallStatus(snapshot);
            snapshot.LastActor = record.ActorId;
            snapshot.UpdatedAtUtc = record.OccurredAtUtc;
        }

        var retainedHead = records.OrderBy(item => item.Sequence).LastOrDefault();
        _historyHeads[workflowId] = retainedHead is null
            ? CaseHistoryHead.None
            : new CaseHistoryHead(retainedHead.Sequence, retainedHead.RecordHashSha256);
        _historyAssignees[workflowId] = records
            .Where(record => record.Assignment is not null)
            .OrderBy(record => record.Sequence)
            .LastOrDefault()
            ?.Assignment
            ?.AssigneeId;
        _workflows[workflowId] = snapshot;
        _hydratedWorkflowIds.Add(workflowId);
    }

    private async ValueTask AppendHistoryAsync(
        Guid workflowId,
        string operation,
        string disposition,
        FundWorkflowCommandMetadata metadata,
        string inputHash,
        string reason,
        FundWorkflowOverallStatus previousStatus,
        FundWorkflowOverallStatus currentStatus,
        VerifiedOperationOutcome outcome,
        string eventId,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct,
        Exception? exception = null,
        bool recoveryAttempt = false,
        string? operationReason = null)
    {
        var predecessor = GetHistoryHead(workflowId);
        if (predecessor.Sequence > 0 && string.IsNullOrWhiteSpace(predecessor.RecordHashSha256))
        {
            throw new InvalidDataException(
                $"Fund workflow '{workflowId:D}' retained predecessor sequence {predecessor.Sequence} without a record hash.");
        }

        var transitionEvidence = CreateEvidence(eventId, workflowId, metadata, occurredAtUtc);
        var evidence = transitionEvidence
            .Concat(outcome.Evidence ?? [])
            .DistinctBy(item => item.EvidenceId, StringComparer.Ordinal)
            .ToArray();
        var evidenceId = transitionEvidence[0].EvidenceId;
        var actorId = string.IsNullOrWhiteSpace(metadata.Actor) ? "unknown-actor" : metadata.Actor;
        var approvals = disposition == "accepted" && IsApprovalOperation(operation)
            ? (IReadOnlyList<OperationalCaseApproval>)[new OperationalCaseApproval
            {
                ApprovalId = metadata.ApprovalReference!,
                Decision = "Approved",
                DecidedBy = actorId,
                DecidedAtUtc = occurredAtUtc,
                Reason = reason,
                EvidenceIds = [evidenceId]
            }]
            : [];
        var exceptions = exception is null
            ? []
            : (IReadOnlyList<OperationalCaseException>)[new OperationalCaseException
            {
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                Message = exception.Message,
                OccurredAtUtc = occurredAtUtc,
                EvidenceIds = [evidenceId]
            }];
        var recoveryAttempts = recoveryAttempt
            ? (IReadOnlyList<OperationalCaseRecoveryAttempt>)[new OperationalCaseRecoveryAttempt
            {
                RecoveryActionId = metadata.IncidentTicketId ?? eventId,
                Attempt = 1,
                StartedAtUtc = occurredAtUtc,
                CompletedAtUtc = occurredAtUtc,
                Result = outcome.State.ToString(),
                EvidenceIds = [evidenceId]
            }]
            : [];

        var data = new Dictionary<string, string>(
            CreateHistoryData(operation, metadata, operationReason),
            StringComparer.Ordinal)
        {
            [ExpectedPreviousCaseSequenceDataKey] = predecessor.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };
        if (predecessor.Sequence > 0)
        {
            data[ExpectedPreviousCaseRecordHashDataKey] = predecessor.RecordHashSha256!;
        }

        _historyAssignees.TryGetValue(workflowId, out var previousAssigneeId);
        var retained = await _caseHistoryStore.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = workflowId.ToString("D"),
            CaseType = CaseType,
            HistoryEventId = eventId,
            EventType = EventType(operation, disposition),
            OccurredAtUtc = occurredAtUtc,
            ActorId = actorId,
            Reason = reason,
            CorrelationId = metadata.CorrelationId ?? metadata.RequestId ?? workflowId.ToString("D"),
            InputHashSha256 = inputHash,
            Data = data,
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = predecessor.Sequence == 0 ? null : previousStatus.ToString(),
                CurrentState = currentStatus.ToString(),
                TransitionedAtUtc = occurredAtUtc
            },
            Assignment = string.IsNullOrWhiteSpace(metadata.AssigneeId)
                ? null
                : new OperationalCaseAssignment
                {
                    PreviousAssigneeId = previousAssigneeId,
                    AssigneeId = metadata.AssigneeId,
                    AssignedBy = actorId,
                    AssignedAtUtc = occurredAtUtc
                },
            Exceptions = exceptions,
            Approvals = approvals,
            Evidence = evidence,
            RecoveryAttempts = recoveryAttempts,
            TerminalOutcome = outcome
        }, ct).ConfigureAwait(false);

        _historyHeads[workflowId] = new CaseHistoryHead(retained.Sequence, retained.RecordHashSha256);
        if (retained.Assignment is not null)
        {
            _historyAssignees[workflowId] = retained.Assignment.AssigneeId;
        }
    }

    private static IReadOnlyList<OperationEvidenceReference> CreateEvidence(
        string eventId,
        Guid workflowId,
        FundWorkflowCommandMetadata metadata,
        DateTimeOffset occurredAtUtc)
    {
        var evidence = new List<OperationEvidenceReference>
        {
            new(
                $"{eventId}:transition",
                "fund-workflow-transition",
                $"Fund workflow {workflowId:D} transition receipt.",
                Uri: $"operations://fund-workflows/{workflowId:D}/events/{Uri.EscapeDataString(eventId)}",
                CapturedAtUtc: occurredAtUtc)
        };
        if (!string.IsNullOrWhiteSpace(metadata.RequestId))
        {
            evidence.Add(new OperationEvidenceReference(
                $"{eventId}:request-id",
                "fund-workflow-request-id",
                "The caller-supplied idempotency request id for this transition.",
                Uri: $"urn:meridian:request-id:{Uri.EscapeDataString(metadata.RequestId)}",
                CapturedAtUtc: occurredAtUtc));
        }

        return evidence;
    }

    private static VerifiedOperationOutcome CreateSucceededOutcome(
        string operationId,
        string operation,
        Guid workflowId,
        FundWorkflowCommandMetadata metadata,
        string inputHash,
        DateTimeOffset occurredAtUtc,
        string postconditionCode,
        string description)
    {
        var evidence = CreateEvidence(operationId, workflowId, metadata, occurredAtUtc);
        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            $"fund-workflow.{operation}",
            OperationTerminalState.Succeeded,
            occurredAtUtc,
            occurredAtUtc,
            1,
            metadata.CorrelationId ?? metadata.RequestId ?? workflowId.ToString("D"),
            inputHash,
            [new OperationPostcondition(
                postconditionCode,
                description,
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: evidence.Select(item => item.EvidenceId).ToArray())],
            evidence,
            [],
            [],
            []));
    }

    private static VerifiedOperationOutcome CreateBlockedOutcome(
        string operationId,
        string operation,
        Guid workflowId,
        FundWorkflowCommandMetadata metadata,
        string inputHash,
        DateTimeOffset occurredAtUtc,
        InvalidOperationException exception)
    {
        var evidence = CreateEvidence(operationId, workflowId, metadata, occurredAtUtc);
        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            $"fund-workflow.{operation}",
            OperationTerminalState.Blocked,
            occurredAtUtc,
            occurredAtUtc,
            1,
            metadata.CorrelationId ?? metadata.RequestId ?? workflowId.ToString("D"),
            inputHash,
            [new OperationPostcondition(
                "workflow-preconditions",
                "The workflow transition preconditions were satisfied.",
                OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: evidence.Select(item => item.EvidenceId).ToArray())],
            evidence,
            [],
            [new OperationIssue(
                "workflow-transition-blocked",
                exception.Message,
                OperationIssueSeverity.Error,
                exception.GetType().FullName,
                evidence[0].EvidenceId)
            {
                IsBlocking = true
            }],
            [new OperationRecoveryAction(
                "satisfy-workflow-preconditions",
                "Satisfy workflow preconditions",
                $"Resolve the blocking condition ({exception.Message}) and retry {operation} with a new request id.",
                Retryable: true,
                RequiresHumanAction: true,
                Route: $"workflow://fund/{workflowId:D}")
            {
                EvidenceIds = evidence.Select(item => item.EvidenceId).ToArray()
            }]));
    }

    private static VerifiedOperationOutcome CreateConcurrencyBlockedOutcome(
        string operationId,
        string operation,
        Guid workflowId,
        FundWorkflowCommandMetadata metadata,
        string inputHash,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CaseHistoryHead expectedHead,
        CaseHistoryHead retainedHead,
        InvalidOperationException exception)
    {
        var expectedEvidence = new OperationEvidenceReference(
            $"{operationId}:expected-predecessor",
            "fund-workflow-expected-predecessor",
            $"The command was based on fund workflow case-history sequence {expectedHead.Sequence}.",
            Uri: $"operations://fund-workflows/{workflowId:D}/history/{expectedHead.Sequence}",
            ContentHashSha256: expectedHead.RecordHashSha256,
            CapturedAtUtc: completedAtUtc);
        var retainedEvidence = new OperationEvidenceReference(
            $"{operationId}:retained-predecessor",
            "fund-workflow-retained-predecessor",
            $"The authoritative fund workflow case-history head is sequence {retainedHead.Sequence}.",
            Uri: $"operations://fund-workflows/{workflowId:D}/history/{retainedHead.Sequence}",
            ContentHashSha256: retainedHead.RecordHashSha256,
            CapturedAtUtc: completedAtUtc);
        var evidence = new[] { expectedEvidence, retainedEvidence };

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            $"fund-workflow.{operation}",
            OperationTerminalState.Blocked,
            startedAtUtc,
            completedAtUtc,
            1,
            metadata.CorrelationId ?? metadata.RequestId ?? workflowId.ToString("D"),
            inputHash,
            [new OperationPostcondition(
                "transition-retained",
                "The requested workflow transition was retained against the expected case-history predecessor.",
                OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: evidence.Select(item => item.EvidenceId).ToArray())],
            evidence,
            [],
            [new OperationIssue(
                "workflow-history-concurrency-conflict",
                exception.Message,
                OperationIssueSeverity.Error,
                exception.GetType().FullName,
                retainedEvidence.EvidenceId)
            {
                IsBlocking = true
            }],
            [new OperationRecoveryAction(
                "reconcile-retained-workflow-state",
                "Review the retained workflow state",
                "Review the newly retained case-history transition, reconcile the intended action against that state, and submit a new command only if the work is still required.",
                Retryable: true,
                RequiresHumanAction: true,
                Route: $"workflow://fund/{workflowId:D}")
            {
                EvidenceIds = [retainedEvidence.EvidenceId]
            }]));
    }

    private static VerifiedOperationOutcome CreateCaseHistoryFailureOutcome(
        string operationId,
        string operation,
        Guid workflowId,
        FundWorkflowCommandMetadata metadata,
        string inputHash,
        DateTimeOffset startedAtUtc,
        OperationTerminalState state,
        string phase,
        Exception exception)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var evidence = new OperationEvidenceReference(
            $"{operationId}:case-history-{phase}-failure",
            "fund-workflow-case-history-failure",
            $"The fund workflow case-history {phase} boundary failed for {workflowId:D}.",
            Uri: $"operations://fund-workflows/{workflowId:D}/case-history",
            CapturedAtUtc: completedAtUtc);
        var blocked = state == OperationTerminalState.Blocked;
        var postconditionCode = phase == "read" ? "case-history-loaded" : "transition-retained";
        var guidance = phase == "read"
            ? "Restore access to the operational case-history store, verify its hash chain and checkpoint, then retry with the same request id."
            : "Read the retained case history before retrying because the append may already be committed. Repair storage or its chain-head checkpoint, then retry with the same request id.";

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            $"fund-workflow.{operation}",
            state,
            startedAtUtc,
            completedAtUtc,
            1,
            metadata.CorrelationId ?? metadata.RequestId ?? workflowId.ToString("D"),
            inputHash,
            [new OperationPostcondition(
                postconditionCode,
                phase == "read"
                    ? "The authoritative fund workflow case history was loaded before evaluating the command."
                    : "The requested fund workflow transition was durably retained.",
                OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: [evidence.EvidenceId])],
            [evidence],
            [],
            [new OperationIssue(
                $"workflow-case-history-{phase}-failed",
                exception.Message,
                OperationIssueSeverity.Error,
                exception.GetType().FullName,
                evidence.EvidenceId)
            {
                IsBlocking = blocked
            }],
            [new OperationRecoveryAction(
                $"recover-workflow-case-history-{phase}",
                "Recover operational case history",
                guidance,
                Retryable: true,
                RequiresHumanAction: true,
                Route: $"workflow://fund/{workflowId:D}")
            {
                EvidenceIds = [evidence.EvidenceId]
            }]));
    }

    private static void ApplyOperation(
        WorkflowSnapshot snapshot,
        string operation,
        string? operationReason,
        FundWorkflowCommandMetadata metadata)
    {
        switch (operation)
        {
            case nameof(StartWorkflow):
                EnsureStage(snapshot, FundWorkflowStage.BrokerImport, FundWorkflowSubStatus.InProgress);
                break;
            case nameof(ImportBrokerData):
                RequireStage(snapshot, FundWorkflowStage.BrokerImport, FundWorkflowSubStatus.InProgress);
                break;
            case nameof(NormalizeBrokerTransactions):
                RequireStage(snapshot, FundWorkflowStage.BrokerImport, FundWorkflowSubStatus.InProgress);
                snapshot.StageStatus[FundWorkflowStage.BrokerImport] = FundWorkflowSubStatus.Completed;
                snapshot.StageStatus[FundWorkflowStage.SecurityMaster] = FundWorkflowSubStatus.InProgress;
                break;
            case nameof(ResolveSecurityMasterMappings):
                RequireStage(snapshot, FundWorkflowStage.SecurityMaster, FundWorkflowSubStatus.InProgress);
                break;
            case nameof(ApproveSecurityMasterOverrides):
                RequireStage(snapshot, FundWorkflowStage.SecurityMaster, FundWorkflowSubStatus.InProgress);
                snapshot.StageStatus[FundWorkflowStage.SecurityMaster] = FundWorkflowSubStatus.Completed;
                snapshot.StageStatus[FundWorkflowStage.Ledger] = FundWorkflowSubStatus.InProgress;
                break;
            case nameof(BuildLedgerDraft):
                RequireStage(snapshot, FundWorkflowStage.Ledger, FundWorkflowSubStatus.InProgress);
                break;
            case nameof(ValidateLedgerDraft):
                RequireStage(snapshot, FundWorkflowStage.Ledger, FundWorkflowSubStatus.InProgress);
                snapshot.PostingGateOpen = true;
                break;
            case nameof(PostLedgerEntries):
                Require(snapshot.PostingGateOpen, "Posting gate must be opened by ValidateLedgerDraft.");
                snapshot.StageStatus[FundWorkflowStage.Ledger] = FundWorkflowSubStatus.Completed;
                snapshot.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.InProgress;
                break;
            case nameof(RunReconciliation):
                RequireStage(snapshot, FundWorkflowStage.Reconciliation, FundWorkflowSubStatus.InProgress);
                snapshot.ReconciliationGateOpen = true;
                break;
            case nameof(ResolveBreakCase):
                Require(snapshot.ReconciliationGateOpen, "RunReconciliation must complete before resolving breaks.");
                snapshot.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.Completed;
                snapshot.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.InProgress;
                snapshot.ApprovalGateOpen = true;
                break;
            case nameof(SubmitForApproval):
                Require(snapshot.ApprovalGateOpen, "Approval gate is closed.");
                break;
            case nameof(ApproveWorkflow):
                Require(snapshot.ApprovalGateOpen, "SubmitForApproval or ResolveBreakCase is required first.");
                snapshot.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.Completed;
                break;
            case nameof(RejectWorkflow):
                Require(snapshot.ApprovalGateOpen, "Workflow must be in approval to reject.");
                if (!Enum.TryParse<FundWorkflowRejectionReasonCode>(operationReason, out var rejectionReason))
                    throw new InvalidOperationException("A recognized rejection reason is required.");
                RouteRejection(snapshot, rejectionReason);
                break;
            case nameof(CloseWorkflow):
                Require(DeriveOverallStatus(snapshot) == FundWorkflowOverallStatus.Approved, "Only approved workflows can be closed.");
                snapshot.IsClosed = true;
                break;
            case nameof(ReopenWorkflow):
                Require(!string.IsNullOrWhiteSpace(operationReason), "Reopen reason is required.");
                Require(string.Equals(metadata.Role, "Administrator", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(metadata.Role, "OperationsManager", StringComparison.OrdinalIgnoreCase),
                    "Reopen requires elevated role (Administrator or OperationsManager).");
                Require(!string.IsNullOrWhiteSpace(metadata.IncidentTicketId), "Incident/ticket ID is required for reopen.");
                Require(snapshot.IsClosed || DeriveOverallStatus(snapshot) == FundWorkflowOverallStatus.Rejected, "Only closed or rejected workflows can be reopened.");
                snapshot.IsClosed = false;
                snapshot.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.InProgress;
                snapshot.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.NotStarted;
                snapshot.ApprovalGateOpen = false;
                break;
            default:
                throw new InvalidOperationException($"Unsupported fund workflow transition '{operation}'.");
        }
    }

    private static void ValidateApprovalCommand(
        string operation,
        FundWorkflowCommandMetadata metadata)
    {
        if (!IsApprovalOperation(operation))
            return;

        Require(!string.IsNullOrWhiteSpace(metadata.Actor), "Approval actor is required.");
        Require(
            AuthorizedApprovalRoles.Any(role =>
                string.Equals(role, metadata.Role, StringComparison.OrdinalIgnoreCase)),
            $"Approval requires an authorized role ({string.Join(", ", AuthorizedApprovalRoles)}).");
        Require(!string.IsNullOrWhiteSpace(metadata.ApprovalReference), "Approval reference is required.");
    }

    private WorkflowSnapshot GetOrCreate(Guid id)
    {
        if (_workflows.TryGetValue(id, out var found))
            return found;

        var created = CreateSnapshot(id);
        _workflows[id] = created;
        return created;
    }

    private CaseHistoryHead GetHistoryHead(Guid workflowId) =>
        _historyHeads.TryGetValue(workflowId, out var head) ? head : CaseHistoryHead.None;

    private void InvalidateHydratedState(Guid workflowId)
    {
        _hydratedWorkflowIds.Remove(workflowId);
        _workflows.Remove(workflowId);
        _requestIdentities.Remove(workflowId);
        _historyHeads.Remove(workflowId);
        _historyAssignees.Remove(workflowId);
    }

    private WorkflowSnapshot SnapshotForFailure(Guid workflowId) =>
        _workflows.TryGetValue(workflowId, out var snapshot)
            ? snapshot.Clone()
            : CreateSnapshot(workflowId);

    private FundWorkflowState CreateCaseHistoryFailureState(
        Guid workflowId,
        string operation,
        FundWorkflowCommandMetadata metadata,
        string inputHash,
        string eventId,
        DateTimeOffset startedAtUtc,
        Exception exception)
    {
        var visibleSnapshot = SnapshotForFailure(workflowId);
        InvalidateHydratedState(workflowId);
        return ToState(
            visibleSnapshot,
            CreateCaseHistoryFailureOutcome(
                eventId,
                operation,
                workflowId,
                metadata,
                inputHash,
                startedAtUtc,
                OperationTerminalState.Failed,
                "persistence",
                exception));
    }

    private static bool IsCaseHistoryConcurrencyConflict(InvalidOperationException exception) =>
        exception.Message.Contains("Operational case '", StringComparison.Ordinal) &&
        exception.Message.Contains("Refresh case history", StringComparison.OrdinalIgnoreCase);

    private static bool IsCaseHistoryAccessFailure(Exception exception) =>
        exception is not OperationCanceledException and not OutOfMemoryException;

    private Dictionary<string, RequestIdentity> GetRequestIdentities(Guid id)
    {
        if (_requestIdentities.TryGetValue(id, out var found))
            return found;

        found = new Dictionary<string, RequestIdentity>(StringComparer.OrdinalIgnoreCase);
        _requestIdentities[id] = found;
        return found;
    }

    private static WorkflowSnapshot CreateSnapshot(Guid id)
    {
        var stages = Enum.GetValues<FundWorkflowStage>()
            .ToDictionary(stage => stage, _ => FundWorkflowSubStatus.NotStarted);
        return new WorkflowSnapshot(
            id,
            FundWorkflowOverallStatus.Draft,
            stages,
            false,
            false,
            false,
            null,
            DateTimeOffset.UtcNow,
            false);
    }

    private static void EnsureStage(WorkflowSnapshot snapshot, FundWorkflowStage stage, FundWorkflowSubStatus status) =>
        snapshot.StageStatus[stage] = status;

    private static void RequireStage(WorkflowSnapshot snapshot, FundWorkflowStage stage, FundWorkflowSubStatus status) =>
        Require(snapshot.StageStatus[stage] == status, $"Expected {stage} to be {status}.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void RouteRejection(WorkflowSnapshot snapshot, FundWorkflowRejectionReasonCode reason)
    {
        snapshot.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.Blocked;
        if (reason is FundWorkflowRejectionReasonCode.LedgerMismatch or FundWorkflowRejectionReasonCode.GovernanceFailure)
        {
            snapshot.StageStatus[FundWorkflowStage.Ledger] = FundWorkflowSubStatus.InProgress;
            snapshot.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.NotStarted;
        }
        else
        {
            snapshot.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.InProgress;
        }
    }

    private static FundWorkflowOverallStatus DeriveOverallStatus(WorkflowSnapshot snapshot)
    {
        if (snapshot.IsClosed)
            return FundWorkflowOverallStatus.Closed;
        if (snapshot.StageStatus[FundWorkflowStage.Approval] == FundWorkflowSubStatus.Blocked)
            return FundWorkflowOverallStatus.Rejected;
        if (snapshot.StageStatus.Values.All(status => status == FundWorkflowSubStatus.Completed))
            return FundWorkflowOverallStatus.Approved;
        if (snapshot.StageStatus[FundWorkflowStage.Approval] == FundWorkflowSubStatus.InProgress)
            return FundWorkflowOverallStatus.AwaitingApproval;
        if (snapshot.StageStatus.Values.Any(status => status is FundWorkflowSubStatus.InProgress or FundWorkflowSubStatus.Completed))
            return FundWorkflowOverallStatus.InProgress;
        return FundWorkflowOverallStatus.Draft;
    }

    private static FundWorkflowState ToState(WorkflowSnapshot snapshot, VerifiedOperationOutcome outcome) =>
        new(
            snapshot.WorkflowId,
            snapshot.OverallStatus,
            new Dictionary<FundWorkflowStage, FundWorkflowSubStatus>(snapshot.StageStatus),
            snapshot.ApprovalGateOpen,
            snapshot.PostingGateOpen,
            snapshot.ReconciliationGateOpen,
            snapshot.LastActor,
            snapshot.UpdatedAtUtc,
            outcome);

    private static string ComputeInputHash(
        Guid workflowId,
        string operation,
        FundWorkflowCommandMetadata metadata,
        string? operationReason)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendCanonicalHashField(hash, "schema", "meridian.fund-workflow.command-input.v1");
        AppendCanonicalHashField(hash, "workflowId", workflowId.ToString("D"));
        AppendCanonicalHashField(hash, "operation", operation);
        AppendCanonicalHashField(hash, "actor", metadata.Actor);
        AppendCanonicalHashField(hash, "role", metadata.Role);
        AppendCanonicalHashField(hash, "requestId", metadata.RequestId);
        AppendCanonicalHashField(hash, "correlationId", metadata.CorrelationId);
        AppendCanonicalHashField(hash, "incidentTicketId", metadata.IncidentTicketId);
        AppendCanonicalHashField(hash, "approvalReference", metadata.ApprovalReference);
        AppendCanonicalHashField(hash, "assigneeId", metadata.AssigneeId);
        AppendCanonicalHashField(hash, "operationReason", operationReason);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendCanonicalHashField(
        IncrementalHash hash,
        string fieldName,
        string? value)
    {
        AppendCanonicalHashValue(hash, fieldName);
        Span<byte> presence = stackalloc byte[1];
        presence[0] = value is null ? (byte)0 : (byte)1;
        hash.AppendData(presence);
        if (value is not null)
        {
            AppendCanonicalHashValue(hash, value);
        }
    }

    private static void AppendCanonicalHashValue(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string EventType(string operation, string disposition) =>
        $"fund-workflow.{operation}.{disposition}";

    private static bool TryReadAcceptedOperation(string eventType, out string operation)
    {
        const string prefix = "fund-workflow.";
        const string suffix = ".accepted";
        if (eventType.StartsWith(prefix, StringComparison.Ordinal) &&
            eventType.EndsWith(suffix, StringComparison.Ordinal))
        {
            operation = eventType[prefix.Length..^suffix.Length];
            return !string.IsNullOrWhiteSpace(operation);
        }

        operation = string.Empty;
        return false;
    }

    private static string? ReadOperationReason(OperationalCaseHistoryRecord record)
    {
        if (record.Data.TryGetValue("operationReason", out var retainedReason) &&
            !string.IsNullOrWhiteSpace(retainedReason))
        {
            return retainedReason;
        }

        if (!TryReadAcceptedOperation(record.EventType, out var operation))
            return null;
        if (operation == nameof(RejectWorkflow))
        {
            var separator = record.Reason.LastIndexOf(':');
            return separator >= 0 ? record.Reason[(separator + 1)..].TrimEnd('.') : record.Reason;
        }
        if (operation == nameof(ReopenWorkflow))
            return string.IsNullOrWhiteSpace(record.Reason) ? "retained recovery" : record.Reason;
        return null;
    }

    private static FundWorkflowCommandMetadata ReplayMetadata(OperationalCaseHistoryRecord record) =>
        new(
            record.ActorId,
            record.Data.TryGetValue("role", out var role) && !string.IsNullOrWhiteSpace(role)
                ? role
                : RoleForReplay(record),
            RequestId: ReadRequestId(record),
            CorrelationId: record.CorrelationId,
            IncidentTicketId: record.Data.GetValueOrDefault("incidentTicketId") ?? record.RecoveryAttempts.FirstOrDefault()?.RecoveryActionId,
            ApprovalReference: record.Data.GetValueOrDefault("approvalReference") ?? record.Approvals.FirstOrDefault()?.ApprovalId,
            AssigneeId: record.Data.GetValueOrDefault("assigneeId") ?? record.Assignment?.AssigneeId);

    private static string RoleForReplay(OperationalCaseHistoryRecord record) =>
        record.EventType.Contains($".{nameof(ReopenWorkflow)}.", StringComparison.Ordinal)
            ? "OperationsManager"
            : "Replay";

    private static string? ReadRequestId(OperationalCaseHistoryRecord record)
    {
        if (record.Data.TryGetValue("requestId", out var retainedRequestId) &&
            !string.IsNullOrWhiteSpace(retainedRequestId))
        {
            return retainedRequestId;
        }

        const string prefix = "urn:meridian:request-id:";
        var uri = record.Evidence.FirstOrDefault(item => item.Kind == "fund-workflow-request-id")?.Uri;
        return uri is not null && uri.StartsWith(prefix, StringComparison.Ordinal)
            ? Uri.UnescapeDataString(uri[prefix.Length..])
            : null;
    }

    private static IReadOnlyDictionary<string, string> CreateHistoryData(
        string operation,
        FundWorkflowCommandMetadata metadata,
        string? operationReason)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["role"] = metadata.Role
        };
        AddIfPresent(data, "requestId", metadata.RequestId);
        AddIfPresent(data, "incidentTicketId", metadata.IncidentTicketId);
        AddIfPresent(data, "approvalReference", metadata.ApprovalReference);
        AddIfPresent(data, "assigneeId", metadata.AssigneeId);
        AddIfPresent(data, "operationReason", operationReason);
        return data;
    }

    private static void AddIfPresent(IDictionary<string, string> data, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            data[key] = value;
    }

    private static string DescribeAcceptedReason(string operation, string? operationReason) => operation switch
    {
        nameof(RejectWorkflow) => $"Accepted rejection transition: {operationReason}.",
        nameof(ReopenWorkflow) => $"Accepted governed recovery transition: {operationReason}",
        _ => $"Accepted {operation} transition."
    };

    private static bool IsApprovalOperation(string operation) =>
        operation is nameof(ApproveSecurityMasterOverrides) or nameof(ApproveWorkflow);

    private sealed class WorkflowSnapshot
    {
        public WorkflowSnapshot(
            Guid workflowId,
            FundWorkflowOverallStatus overallStatus,
            Dictionary<FundWorkflowStage, FundWorkflowSubStatus> stageStatus,
            bool approvalGateOpen,
            bool postingGateOpen,
            bool reconciliationGateOpen,
            string? lastActor,
            DateTimeOffset updatedAtUtc,
            bool isClosed)
        {
            WorkflowId = workflowId;
            OverallStatus = overallStatus;
            StageStatus = stageStatus;
            ApprovalGateOpen = approvalGateOpen;
            PostingGateOpen = postingGateOpen;
            ReconciliationGateOpen = reconciliationGateOpen;
            LastActor = lastActor;
            UpdatedAtUtc = updatedAtUtc;
            IsClosed = isClosed;
        }

        public Guid WorkflowId { get; }
        public FundWorkflowOverallStatus OverallStatus { get; set; }
        public Dictionary<FundWorkflowStage, FundWorkflowSubStatus> StageStatus { get; }
        public bool ApprovalGateOpen { get; set; }
        public bool PostingGateOpen { get; set; }
        public bool ReconciliationGateOpen { get; set; }
        public string? LastActor { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public bool IsClosed { get; set; }

        public WorkflowSnapshot Clone() =>
            new(
                WorkflowId,
                OverallStatus,
                new Dictionary<FundWorkflowStage, FundWorkflowSubStatus>(StageStatus),
                ApprovalGateOpen,
                PostingGateOpen,
                ReconciliationGateOpen,
                LastActor,
                UpdatedAtUtc,
                IsClosed);
    }

    private sealed record RequestIdentity(
        string Operation,
        string InputHashSha256,
        VerifiedOperationOutcome TerminalOutcome);

    private sealed record CaseHistoryHead(long Sequence, string? RecordHashSha256)
    {
        public static CaseHistoryHead None { get; } = new(0, null);
    }
}
