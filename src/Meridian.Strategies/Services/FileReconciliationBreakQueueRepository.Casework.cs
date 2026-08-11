using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

/// <summary>
/// Durable queue-owned obligation proving that statement casework still needs to be synchronized
/// to the source statement stores and retained on the matching Operations Continuity workflow.
/// Completion is represented by an append-only paired marker so a crash cannot erase the pending
/// fact without retaining the governed completion audit that cleared it.
/// </summary>
public static class StatementCaseworkHandoffObligation
{
    public const string CompletionSource = "statement-casework-handoff";
    private const string PendingPrefix = "urn:meridian:statement-casework-handoff:pending:";
    private const string CompletedPrefix = "urn:meridian:statement-casework-handoff:completed:";
    private const string CompletionCommandPrefix = "statement-casework-handoff-complete:";

    public static string CreatePendingMarker(string commandId)
        => PendingPrefix + ComputeCommandKey(commandId);

    public static string CreateCompletedMarker(string commandId)
        => CompletedPrefix + ComputeCommandKey(commandId);

    public static string CreateCompletionCommandId(string commandId)
        => CompletionCommandPrefix + ComputeCommandKey(commandId);

    public static bool HasPending(ReconciliationBreakQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var evidence = item.EvidenceLinks ?? [];
        var completed = evidence
            .Where(IsCompletedMarker)
            .Select(static value => value[CompletedPrefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
        return evidence
            .Where(IsPendingMarker)
            .Select(static value => value[PendingPrefix.Length..])
            .Any(key => !completed.Contains(key));
    }

    public static bool HasPending(
        ReconciliationBreakQueueItem item,
        string commandId)
    {
        ArgumentNullException.ThrowIfNull(item);
        var evidence = item.EvidenceLinks ?? [];
        return evidence.Contains(CreatePendingMarker(commandId), StringComparer.Ordinal)
            && !evidence.Contains(CreateCompletedMarker(commandId), StringComparer.Ordinal);
    }

    public static bool HasCompleted(
        ReconciliationBreakQueueItem item,
        string commandId)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.EvidenceLinks ?? [])
            .Contains(CreateCompletedMarker(commandId), StringComparer.Ordinal);
    }

    public static bool HasCompleted(ReconciliationBreakQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.EvidenceLinks ?? []).Any(IsCompletedMarker);
    }

    public static bool IsControlMarker(string? value)
        => IsPendingMarker(value) || IsCompletedMarker(value);

    internal static ReconciliationBreakQueueItem MarkPending(
        ReconciliationBreakQueueItem before,
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem after)
    {
        if (!string.Equals(before.SourceType, "statement", StringComparison.OrdinalIgnoreCase)
            || command.Action is not (
                ReconciliationCaseworkAction.Resolve
                or ReconciliationCaseworkAction.Waive
                or ReconciliationCaseworkAction.Supersede
                or ReconciliationCaseworkAction.SignOff
                or ReconciliationCaseworkAction.Reopen))
        {
            return after;
        }

        var completedMarker = CreateCompletedMarker(command.CommandId);
        var evidence = (after.EvidenceLinks ?? [])
            .Where(value => !string.Equals(value, completedMarker, StringComparison.Ordinal))
            .Append(CreatePendingMarker(command.CommandId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var blockedOutputs = (after.BlockedOutputs ?? [])
            .Concat(["FinalReport", "PeriodClose"])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return after with
        {
            EvidenceLinks = evidence,
            EvidenceCount = evidence.Length,
            BlockedOutputs = blockedOutputs
        };
    }

    internal static bool IsCompletionCommand(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCommand command)
    {
        if (command.Action != ReconciliationCaseworkAction.LinkEvidence
            || !string.Equals(command.Source, CompletionSource, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(command.CausationId)
            || command.EvidenceLinks is not { Count: 1 }
            || !HasValidCloseScope(command.CloseScope))
        {
            return false;
        }

        var causationId = command.CausationId.Trim();
        return string.Equals(
                   command.CommandId,
                   CreateCompletionCommandId(causationId),
                   StringComparison.Ordinal)
               && string.Equals(
                   command.EvidenceLinks[0],
                   CreateCompletedMarker(causationId),
                   StringComparison.Ordinal)
               && HasPending(item, causationId);
    }

    private static bool HasValidCloseScope(ReconciliationCaseworkCloseScopeDto? scope)
        => scope is not null
           && !string.IsNullOrWhiteSpace(scope.FundProfileId)
           && scope.LedgerBookId != Guid.Empty
           && scope.AccountingPeriodId != Guid.Empty
           && scope.AsOfDate != default;

    private static bool IsPendingMarker(string? value)
        => value?.StartsWith(PendingPrefix, StringComparison.Ordinal) == true
           && value.Length > PendingPrefix.Length;

    private static bool IsCompletedMarker(string? value)
        => value?.StartsWith(CompletedPrefix, StringComparison.Ordinal) == true
           && value.Length > CompletedPrefix.Length;

    private static string ComputeCommandKey(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandId.Trim())))
            .ToLowerInvariant();
    }
}

public sealed partial class FileReconciliationBreakQueueRepository
{
    private async Task AppendMaterialActionDeniedAuditAsync(
        ReconciliationBreakQueueItem item,
        string actor,
        string? note,
        string? commandId,
        string? correlationId,
        string source,
        string reason,
        CancellationToken ct)
    {
        var auditCount = _auditEvents.Count;
        await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: item.BreakId,
            EventType: "MaterialActionDenied",
            PreviousStatus: item.Status,
            NewStatus: item.Status,
            PreviousLifecycleState: item.LifecycleState,
            NewLifecycleState: item.LifecycleState,
            OccurredAt: DateTimeOffset.UtcNow,
            AssignedTo: item.AssignedTo,
            ReviewedBy: item.ReviewedBy,
            ResolvedBy: item.ResolvedBy,
            Note: note,
            ExceptionRoute: item.ExceptionRoute,
            ToleranceBand: item.ToleranceBand,
            RequiredSignoffRole: item.RequiredSignoffRole,
            SignoffStatus: item.SignoffStatus,
            ExternalAccountId: item.ExternalAccountId,
            CustodianId: item.CustodianId,
            UpstreamSyncCursor: item.UpstreamSyncCursor,
            Actor: actor,
            BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
            AfterPayload: JsonSerializer.Serialize(item, _jsonOptions),
            CorrelationId: correlationId,
            CommandId: commandId,
            Source: source,
            Reason: reason), ct).ConfigureAwait(false);
        try
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _auditEvents.RemoveRange(auditCount, _auditEvents.Count - auditCount);
            throw;
        }
    }

    private async Task AppendCaseworkRejectedAuditAsync(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueTransitionResult validation,
        CancellationToken ct)
    {
        var auditCount = _auditEvents.Count;
        await AppendAuditAsync(CreateAudit(command, item, item, DateTimeOffset.UtcNow) with
        {
            EventType = validation.ErrorCode == ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator
                ? "MaterialActionDenied"
                : "CaseworkRejected",
            Reason = validation.Error ?? "Reconciliation casework prerequisites were not satisfied."
        }, ct).ConfigureAwait(false);
        try
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _auditEvents.RemoveRange(auditCount, _auditEvents.Count - auditCount);
            throw;
        }
    }

    private long NextAuditSequence() => _auditEvents.Count == 0 ? 1 : _auditEvents.Max(static item => item.Sequence) + 1;

    private static string? HashPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private string ComputeCommandInputHash(ReconciliationCaseworkCommand command)
        => HashPayload($"meridian.reconciliation-casework-command.v1\n{JsonSerializer.Serialize(command, _jsonOptions)}")!;

    private static ReconciliationCaseworkCommand CreateLegacyStartReviewCommand(
        ReviewReconciliationBreakRequest request,
        string inputHash)
        => new(
            request.BreakId,
            ReconciliationCaseworkAction.TransitionStatus,
            request.ReviewedBy,
            $"legacy-start-review:{inputHash}",
            $"legacy-start-review:{inputHash}",
            "legacy-reconciliation-start-review",
            ExpectedVersion: 0,
            Reason: request.ReviewNote ?? "Review started.",
            Assignee: request.AssignedTo,
            Status: ReconciliationCaseLifecycleState.Investigating,
            Note: request.ReviewNote,
            StatusTransition: ReconciliationCaseLifecycleState.Investigating);

    private static ReconciliationCaseworkCommand CreateLegacyResolveCommand(
        ResolveReconciliationBreakRequest request,
        string inputHash)
        => new(
            request.BreakId,
            ReconciliationCaseworkAction.Resolve,
            request.ResolvedBy,
            $"legacy-resolve:{inputHash}",
            $"legacy-resolve:{inputHash}",
            "legacy-reconciliation-resolve",
            ExpectedVersion: 0,
            Reason: request.OperatorRationale,
            Status: ReconciliationCaseLifecycleState.Resolved,
            Note: request.ResolutionNote,
            ResolutionCode: request.Status == ReconciliationBreakQueueStatus.Dismissed
                ? "DismissedFalsePositive"
                : "LegacyResolved",
            StatusTransition: ReconciliationCaseLifecycleState.Resolved,
            ActionOrigin: request.ActionOrigin,
            ApprovalActor: request.ResolvedBy);

    private async Task<ReconciliationBreakQueueTransitionResult?> TryReplayRetainedLegacyCommandAsync(
        ReconciliationCaseworkCommand command,
        string inputHash,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        if (!_commandReceipts.TryGetValue(command.CommandId, out var receipt))
            return null;

        var exactReplay = !receipt.LegacyUnverified &&
                          receipt.Outcome is not null &&
                          string.Equals(receipt.BreakId, command.BreakId, StringComparison.OrdinalIgnoreCase) &&
                          receipt.Action == command.Action &&
                          string.Equals(receipt.InputHashSha256, inputHash, StringComparison.Ordinal);
        var auditCount = _auditEvents.Count;
        var reason = exactReplay
            ? "Exact legacy request replay returned the retained terminal receipt without reapplying the transition."
            : "The derived legacy command id was already bound to different casework input.";
        await AppendCaseworkReplayAuditAsync(
            command,
            _items!.GetValueOrDefault(command.BreakId),
            exactReplay ? "CaseworkReplayAccepted" : "CaseworkReplayConflict",
            reason,
            ct).ConfigureAwait(false);
        try
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _auditEvents.RemoveRange(auditCount, _auditEvents.Count - auditCount);
            return CreatePersistenceFailure(command, inputHash, startedAt, receipt.Result, ex);
        }

        if (exactReplay)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.Success,
                receipt.Result)
            {
                Outcome = receipt.Outcome!
            };
        }

        return BindTransitionOutcome(
            new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.Conflict,
                _items!.GetValueOrDefault(command.BreakId),
                reason,
                ReconciliationBreakQueueTransitionErrorCode.CommandIdConflict),
            command,
            inputHash,
            startedAt);
    }

    private string ComputeBulkInputHash(ReconciliationBulkCaseworkRequest request)
        => HashPayload($"meridian.reconciliation-bulk-casework-request.v1\n{JsonSerializer.Serialize(request, _jsonOptions)}")!;

    private string ComputeCreateInputHash(ReconciliationBreakQueueItem item)
    {
        var materialInput = new
        {
            item.BreakId,
            item.StrategyName,
            item.Category,
            item.Variance,
            item.Reason,
            item.SourceType,
            item.SourceSystem,
            item.SourceReference,
            item.SourceImportId,
            item.SourceBreakId,
            item.SourceFingerprint,
            item.FundAccountId,
            item.FundProfileId,
            item.ExternalAccountId,
            item.CustodianId,
            item.LedgerBookId,
            item.AccountingPeriodId,
            item.AsOfDate,
            item.ToleranceProfileId,
            item.ToleranceBand,
            Measures = (item.Measures ?? [])
                .OrderBy(static measure => measure.Kind)
                .ThenBy(static measure => measure.Unit, StringComparer.Ordinal)
                .ToArray(),
            BlockedOutputs = (item.BlockedOutputs ?? [])
                .OrderBy(static output => output, StringComparer.Ordinal)
                .ToArray()
        };
        return HashPayload($"meridian.reconciliation-break-create.v1\n{JsonSerializer.Serialize(materialInput, _jsonOptions)}")!;
    }

    private string ComputeRetainedCreateInputHash(
        string breakId,
        ReconciliationBreakQueueItem currentProjection)
    {
        var retainedCreation = _auditEvents
            .Where(entry => string.Equals(entry.BreakId, breakId, StringComparison.OrdinalIgnoreCase)
                && entry.EventType is "CaseCreated" or "BreakIdMigrated"
                && !string.IsNullOrWhiteSpace(entry.AfterPayload))
            .OrderBy(static entry => entry.Sequence)
            .FirstOrDefault();
        if (retainedCreation is null)
        {
            // Legacy snapshots can predate durable creation evidence. Preserve compatibility for
            // those cases while all newly created/migrated cases use their immutable audit payload.
            return ComputeCreateInputHash(currentProjection);
        }

        var retainedInput = JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(
                retainedCreation.AfterPayload!,
                _jsonOptions)
            ?? throw new InvalidDataException(
                $"Reconciliation creation evidence '{retainedCreation.EventId}' retained a null case payload.");
        return ComputeCreateInputHash(retainedInput);
    }

    private static BulkRequestProblem? ValidateBulkRequest(ReconciliationBulkCaseworkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CommandId))
            return new BulkRequestProblem("Command id is required.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return new BulkRequestProblem("Idempotency key is required.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.Actor))
            return new BulkRequestProblem("Actor is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor);
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
            return new BulkRequestProblem("Correlation id is required.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.Source))
            return new BulkRequestProblem("Source is required.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (request.BreakIds is null || request.BreakIds.Count == 0)
            return new BulkRequestProblem("At least one break id is required.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (request.BreakIds.Any(string.IsNullOrWhiteSpace))
            return new BulkRequestProblem("Break ids cannot be empty.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (request.BreakIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.BreakIds.Count)
            return new BulkRequestProblem("Break ids must be unique within a bulk request.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (request.MaxCaseCount <= 0 || request.MaxCaseCount > MaximumBulkCaseCount)
            return new BulkRequestProblem($"MaxCaseCount must be between 1 and {MaximumBulkCaseCount}.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
        if (request.BreakIds.Count > request.MaxCaseCount || request.BreakIds.Count > MaximumBulkCaseCount)
            return new BulkRequestProblem($"Bulk request contains {request.BreakIds.Count} cases, exceeding the admitted limit of {Math.Min(request.MaxCaseCount, MaximumBulkCaseCount)}.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);

        return null;
    }

    private static ReconciliationBreakQueueTransitionResult BindTransitionOutcome(
        ReconciliationBreakQueueTransitionResult result,
        ReconciliationCaseworkCommand command,
        string inputHash,
        DateTimeOffset startedAt)
    {
        var state = result.Status switch
        {
            ReconciliationBreakQueueTransitionStatus.Success => OperationTerminalState.Succeeded,
            ReconciliationBreakQueueTransitionStatus.Failed => OperationTerminalState.Failed,
            _ => OperationTerminalState.Blocked
        };
        return result with
        {
            Outcome = CreateTransitionOutcome(
                command,
                inputHash,
                state,
                startedAt,
                result.Item,
                result.Error,
                auditEvent: null,
                errorCode: result.ErrorCode)
        };
    }

    private static ReconciliationBreakQueueTransitionResult CreatePersistenceFailure(
        ReconciliationCaseworkCommand command,
        string inputHash,
        DateTimeOffset startedAt,
        ReconciliationBreakQueueItem? original,
        Exception exception)
    {
        var error = $"The reconciliation transition was rolled back because durable persistence failed: {exception.Message}";
        return new ReconciliationBreakQueueTransitionResult(
            ReconciliationBreakQueueTransitionStatus.Failed,
            original,
            error,
            ReconciliationBreakQueueTransitionErrorCode.PersistenceFailed)
        {
            Outcome = CreateTransitionOutcome(
                command,
                inputHash,
                OperationTerminalState.Failed,
                startedAt,
                original,
                error,
                auditEvent: null,
                errorCode: ReconciliationBreakQueueTransitionErrorCode.PersistenceFailed,
                exception: exception)
        };
    }

    private static ReconciliationBreakQueueTransitionResult CreateLegacyPersistenceFailure(
        string operationKind,
        string breakId,
        string inputHash,
        DateTimeOffset startedAt,
        ReconciliationBreakQueueItem? original,
        Exception exception)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var evidenceId = "casework-input-hash";
        var error = $"The reconciliation transition was rolled back because durable persistence failed: {exception.Message}";
        var outcome = VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: $"{operationKind}:{breakId}:{inputHash[..12]}",
            OperationKind: operationKind,
            State: OperationTerminalState.Failed,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt,
            AttemptNumber: 1,
            CorrelationId: breakId,
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "transition-retained",
                    "The reconciliation transition and audit evidence were durably retained.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    evidenceId,
                    "casework-input",
                    "Canonical hash of the attempted reconciliation transition input.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: completedAt)
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    ReconciliationBreakQueueTransitionErrorCode.PersistenceFailed.ToString(),
                    error,
                    OperationIssueSeverity.Error,
                    ExceptionType: exception.GetType().FullName,
                    EvidenceId: evidenceId)
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "repair-persistence-and-retry",
                    "Repair persistence and retry",
                    "Inspect storage health, repair the durable queue, and retry the transition with the exact input.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidenceId]
                }
            ]));
        return new ReconciliationBreakQueueTransitionResult(
            ReconciliationBreakQueueTransitionStatus.Failed,
            original,
            error,
            ReconciliationBreakQueueTransitionErrorCode.PersistenceFailed)
        {
            Outcome = outcome
        };
    }

    private static VerifiedOperationOutcome CreateTransitionOutcome(
        ReconciliationCaseworkCommand command,
        string inputHash,
        OperationTerminalState state,
        DateTimeOffset startedAt,
        ReconciliationBreakQueueItem? item,
        string? error,
        ReconciliationBreakQueueAuditEvent? auditEvent,
        ReconciliationBreakQueueTransitionErrorCode errorCode = ReconciliationBreakQueueTransitionErrorCode.None,
        Exception? exception = null)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var inputEvidenceId = "casework-input-hash";
        var evidence = new List<OperationEvidenceReference>
        {
            new(
                inputEvidenceId,
                "casework-input",
                "Canonical hash of the complete reconciliation casework request.",
                Uri: $"urn:sha256:{inputHash}",
                ContentHashSha256: inputHash,
                CapturedAtUtc: completedAt)
        };
        var evidenceIds = new List<string> { inputEvidenceId };
        if (auditEvent is not null)
        {
            var auditEvidenceId = $"audit:{auditEvent.EventId}";
            evidence.Add(new OperationEvidenceReference(
                auditEvidenceId,
                "reconciliation-audit-event",
                $"Retained reconciliation audit event {auditEvent.EventId} at sequence {auditEvent.Sequence}.",
                Uri: $"urn:meridian:reconciliation-audit:{auditEvent.EventId}",
                ContentHashSha256: auditEvent.AfterPayloadHash ?? auditEvent.BeforePayloadHash ?? inputHash,
                CapturedAtUtc: auditEvent.OccurredAt));
            evidenceIds.Add(auditEvidenceId);
        }

        var succeeded = state == OperationTerminalState.Succeeded;
        var blocked = state == OperationTerminalState.Blocked;
        var issues = succeeded
            ? Array.Empty<OperationIssue>()
            :
            [
                new OperationIssue(
                    errorCode == ReconciliationBreakQueueTransitionErrorCode.None
                        ? "reconciliation-transition-failed"
                        : errorCode.ToString(),
                    error ?? "The reconciliation transition did not satisfy its required postcondition.",
                    OperationIssueSeverity.Error,
                    ExceptionType: exception?.GetType().FullName,
                    EvidenceId: inputEvidenceId)
                {
                    IsBlocking = blocked
                }
            ];
        var recovery = succeeded
            ? Array.Empty<OperationRecoveryAction>()
            :
            [
                new OperationRecoveryAction(
                    blocked ? "correct-and-retry-casework" : "repair-persistence-and-retry",
                    blocked ? "Correct and retry" : "Repair persistence and retry",
                    blocked
                        ? "Satisfy the reported casework prerequisite, then retry with a new command id."
                        : "Inspect storage health and retained logs, repair the durable queue, then retry the exact command with the same command id.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [inputEvidenceId]
                }
            ];

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: $"reconciliation-casework:{command.CommandId}",
            OperationKind: "reconciliation.casework.transition",
            State: state,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt,
            AttemptNumber: 1,
            CorrelationId: FirstNonBlank(command.CorrelationId, command.BreakId, command.CommandId, "reconciliation-casework"),
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "casework-transition-applied",
                    item is null
                        ? "The requested reconciliation case exists and its transition is durably retained."
                        : $"The requested reconciliation transition for case {item.BreakId} is durably retained.",
                    succeeded ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: evidenceIds)
            ],
            Evidence: evidence,
            Artifacts: [],
            Issues: issues,
            Recovery: recovery));
    }

    private static ReconciliationBulkCaseworkResult CreateRejectedBulkResult(
        ReconciliationBulkCaseworkRequest request,
        string inputHash,
        DateTimeOffset startedAt,
        BulkRequestProblem problem)
        => CreateRejectedBulkResult(request, inputHash, startedAt, problem.Message, problem.Code);

    private static ReconciliationBulkCaseworkResult CreateRejectedBulkResult(
        ReconciliationBulkCaseworkRequest request,
        string inputHash,
        DateTimeOffset startedAt,
        string error,
        ReconciliationBreakQueueTransitionErrorCode errorCode)
    {
        var requestedIds = request.BreakIds ?? [];
        var results = requestedIds
            .Select((breakId, index) => new ReconciliationBulkCaseworkCaseResult(
                string.IsNullOrWhiteSpace(breakId) ? $"<invalid-break-id:{index}>" : breakId,
                Succeeded: false,
                WouldSucceed: false,
                Error: error,
                Item: null))
            .ToArray();
        var outcome = CreateBulkOutcome(
            request,
            inputHash,
            startedAt,
            results,
            persistenceFailure: null,
            receiptRetained: false,
            explicitError: error,
            errorCode: errorCode);
        return new ReconciliationBulkCaseworkResult(
            string.IsNullOrWhiteSpace(request.CommandId) ? "invalid-command" : request.CommandId,
            request.IdempotencyKey ?? string.Empty,
            request.DryRun,
            requestedIds.Count,
            0,
            requestedIds.Count,
            results)
        {
            InputHashSha256 = inputHash,
            Outcome = outcome
        };
    }

    private static ReconciliationBulkCaseworkResult CreatePersistenceFailedBulkResult(
        ReconciliationBulkCaseworkRequest request,
        string inputHash,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<string, ReconciliationBreakQueueItem> originalItems,
        Exception exception)
    {
        var error = $"The bulk reconciliation action was rolled back because durable persistence failed: {exception.Message}";
        var results = request.BreakIds.Select(breakId => new ReconciliationBulkCaseworkCaseResult(
            breakId,
            Succeeded: false,
            WouldSucceed: false,
            Error: error,
            Item: originalItems.GetValueOrDefault(breakId))).ToArray();
        var outcome = CreateBulkOutcome(
            request,
            inputHash,
            startedAt,
            results,
            exception,
            receiptRetained: false,
            explicitError: error,
            errorCode: ReconciliationBreakQueueTransitionErrorCode.PersistenceFailed);
        return new ReconciliationBulkCaseworkResult(
            request.CommandId,
            request.IdempotencyKey,
            request.DryRun,
            request.BreakIds.Count,
            0,
            request.BreakIds.Count,
            results)
        {
            InputHashSha256 = inputHash,
            Outcome = outcome
        };
    }

    private static VerifiedOperationOutcome CreateBulkOutcome(
        ReconciliationBulkCaseworkRequest request,
        string inputHash,
        DateTimeOffset startedAt,
        IReadOnlyList<ReconciliationBulkCaseworkCaseResult> results,
        Exception? persistenceFailure,
        bool receiptRetained,
        string? explicitError = null,
        ReconciliationBreakQueueTransitionErrorCode errorCode = ReconciliationBreakQueueTransitionErrorCode.None)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var successful = results.Count(result => request.DryRun ? result.WouldSucceed && result.Error is null : result.Succeeded);
        var failed = results.Count - successful;
        var state = persistenceFailure is not null
            ? OperationTerminalState.Failed
            : failed == 0 && results.Count > 0
                ? OperationTerminalState.Succeeded
                : successful > 0 && request.AllowPartialSuccess
                    ? OperationTerminalState.CompletedWithWarnings
                    : OperationTerminalState.Blocked;
        var evidenceId = "bulk-casework-input-hash";
        var issues = state switch
        {
            OperationTerminalState.Succeeded => Array.Empty<OperationIssue>(),
            OperationTerminalState.CompletedWithWarnings =>
            [
                new OperationIssue(
                    "partial-casework",
                    explicitError ?? $"{failed} reconciliation cases did not satisfy the requested bulk action.",
                    OperationIssueSeverity.Warning,
                    EvidenceId: evidenceId)
            ],
            OperationTerminalState.Blocked =>
            [
                new OperationIssue(
                    errorCode == ReconciliationBreakQueueTransitionErrorCode.None ? "bulk-casework-blocked" : errorCode.ToString(),
                    explicitError ?? "The reconciliation bulk action did not satisfy its required casework prerequisites.",
                    OperationIssueSeverity.Error,
                    EvidenceId: evidenceId)
                {
                    IsBlocking = true
                }
            ],
            _ =>
            [
                new OperationIssue(
                    errorCode == ReconciliationBreakQueueTransitionErrorCode.None ? "bulk-casework-failed" : errorCode.ToString(),
                    explicitError ?? "The reconciliation bulk action failed before its receipt could be durably retained.",
                    OperationIssueSeverity.Error,
                    ExceptionType: persistenceFailure?.GetType().FullName,
                    EvidenceId: evidenceId)
            ]
        };
        var recovery = state == OperationTerminalState.Succeeded
            ? Array.Empty<OperationRecoveryAction>()
            :
            [
                new OperationRecoveryAction(
                    state == OperationTerminalState.Failed ? "repair-queue-and-retry" : "review-failed-cases",
                    state == OperationTerminalState.Failed ? "Repair queue and retry" : "Review failed cases",
                    state == OperationTerminalState.Failed
                        ? "Inspect storage health, repair the durable queue, and retry the exact request with the same command id and idempotency key."
                        : "Review each failed case, satisfy its prerequisites, and retry with a new command id and idempotency key.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidenceId]
                }
            ];

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: $"reconciliation-bulk:{FirstNonBlank(request.CommandId, "invalid-command")}",
            OperationKind: request.DryRun ? "reconciliation.casework.bulk-dry-run" : "reconciliation.casework.bulk-execute",
            State: state,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt,
            AttemptNumber: 1,
            CorrelationId: FirstNonBlank(request.CorrelationId, request.CommandId, "reconciliation-bulk"),
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    persistenceFailure is not null
                        ? "bulk-receipt-retained"
                        : !receiptRetained
                            ? "bulk-request-admitted"
                            : request.DryRun
                                ? "bulk-casework-validated"
                                : "bulk-casework-applied",
                    persistenceFailure is not null
                        ? "The terminal bulk receipt and idempotency binding were durably retained."
                        : !receiptRetained
                            ? "The bulk request satisfied admission requirements and obtained a durable idempotency binding."
                            : request.DryRun
                                ? "Every admitted reconciliation case was evaluated without changing case state."
                                : "Every admitted reconciliation case reached the terminal result permitted by the request's partial-success policy.",
                    state is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
                        ? OperationPostconditionState.Satisfied
                        : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    evidenceId,
                    "bulk-casework-input",
                    receiptRetained
                        ? "Canonical hash of the complete reconciliation bulk request and its retained idempotency binding."
                        : "Canonical hash of the complete reconciliation bulk request; no durable idempotency binding was committed for this rejected or failed attempt.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: completedAt)
            ],
            Artifacts: [],
            Issues: issues,
            Recovery: recovery));
    }

    private async Task AppendBulkFailureAuditAsync(
        ReconciliationBulkCaseworkRequest request,
        PreparedBulkCasework entry,
        string bulkActionId,
        ReconciliationBreakQueueScope? scope,
        CancellationToken ct)
    {
        if (entry.Item is not null && entry.Command is not null)
        {
            await AppendAuditAsync(CreateAudit(entry.Command, entry.Item, entry.Item, DateTimeOffset.UtcNow) with
            {
                EventType = "BulkActionCaseFailed",
                Reason = entry.Validation?.Error
            }, ct).ConfigureAwait(false);
            return;
        }

        await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: entry.BreakId,
            EventType: "BulkActionCaseFailed",
            PreviousStatus: null,
            NewStatus: ReconciliationBreakQueueStatus.Open,
            PreviousLifecycleState: null,
            NewLifecycleState: ReconciliationCaseLifecycleState.Open,
            OccurredAt: DateTimeOffset.UtcNow,
            AssignedTo: request.Assignee,
            ReviewedBy: null,
            ResolvedBy: null,
            Note: request.Note,
            Actor: request.Actor,
            CorrelationId: request.CorrelationId,
            CommandId: $"{bulkActionId}:{entry.BreakId}",
            Source: request.Source,
            Reason: entry.Validation?.Error ?? "Break was not found.")
        {
            TenantId = scope?.TenantId,
            CompanyId = scope?.CompanyId
        }, ct).ConfigureAwait(false);
    }

    private async Task<ReconciliationBulkCaseworkResult> RetainBulkReplayConflictAsync(
        ReconciliationBulkCaseworkRequest request,
        string inputHash,
        DateTimeOffset startedAt,
        string reason,
        ReconciliationBreakQueueScope? scope,
        CancellationToken ct)
    {
        var auditCount = _auditEvents.Count;
        await AppendBulkReplayAuditAsync(request, "BulkActionReplayConflict", reason, scope, ct).ConfigureAwait(false);
        try
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _auditEvents.RemoveRange(auditCount, _auditEvents.Count - auditCount);
            return CreatePersistenceFailedBulkResult(request, inputHash, startedAt, _items!, ex);
        }

        return CreateRejectedBulkResult(
            request,
            inputHash,
            startedAt,
            reason,
            ReconciliationBreakQueueTransitionErrorCode.IdempotencyConflict);
    }

    private Task AppendBulkReplayAuditAsync(
        ReconciliationBulkCaseworkRequest request,
        string eventType,
        string reason,
        ReconciliationBreakQueueScope? scope,
        CancellationToken ct)
        => AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: request.CommandId,
            EventType: eventType,
            PreviousStatus: null,
            NewStatus: ReconciliationBreakQueueStatus.Open,
            PreviousLifecycleState: null,
            NewLifecycleState: ReconciliationCaseLifecycleState.Open,
            OccurredAt: DateTimeOffset.UtcNow,
            AssignedTo: request.Assignee,
            ReviewedBy: null,
            ResolvedBy: null,
            Note: request.Note,
            Actor: request.Actor,
            BeforePayload: JsonSerializer.Serialize(request, _jsonOptions),
            CorrelationId: request.CorrelationId,
            CommandId: request.CommandId,
            Source: request.Source,
            Reason: reason)
        {
            TenantId = scope?.TenantId,
            CompanyId = scope?.CompanyId
        }, ct);

    private Task AppendCaseworkReplayAuditAsync(
        ReconciliationCaseworkCommand command,
        ReconciliationBreakQueueItem? item,
        string eventType,
        string reason,
        CancellationToken ct)
        => AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: command.BreakId,
            EventType: eventType,
            PreviousStatus: item?.Status,
            NewStatus: item?.Status ?? ReconciliationBreakQueueStatus.Open,
            PreviousLifecycleState: item?.LifecycleState,
            NewLifecycleState: item?.LifecycleState ?? ReconciliationCaseLifecycleState.Open,
            OccurredAt: DateTimeOffset.UtcNow,
            AssignedTo: item?.AssignedTo,
            ReviewedBy: item?.ReviewedBy,
            ResolvedBy: item?.ResolvedBy,
            Note: command.Note,
            Actor: command.Actor,
            BeforePayload: JsonSerializer.Serialize(command, _jsonOptions),
            CorrelationId: command.CorrelationId,
            CommandId: command.CommandId,
            Source: command.Source,
            Reason: reason), ct);

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))!;

    private static ReconciliationBreakQueueTransitionResult? ValidateCaseworkCommand(ReconciliationBreakQueueItem item, ReconciliationCaseworkCommand command)
    {
        if (!Enum.IsDefined(command.Action))
        {
            return Invalid(
                item,
                $"Unsupported reconciliation casework action value '{(byte)command.Action}'.",
                ReconciliationBreakQueueTransitionErrorCode.InvalidRequest,
                ["action"]);
        }

        if (command.ExpectedVersion != item.Version)
        {
            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Conflict, item, "Case version conflict.", ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict);
        }

        var exactComment = string.IsNullOrWhiteSpace(command.CommentId)
            ? null
            : item.Comments?.FirstOrDefault(comment =>
                string.Equals(comment.CommentId, command.CommentId, StringComparison.Ordinal));

        var genericGovernedTarget = command.Action switch
        {
            ReconciliationCaseworkAction.TransitionStatus => command.Status,
            ReconciliationCaseworkAction.AddComment => command.StatusTransition,
            _ => null
        };
        if (genericGovernedTarget.HasValue && RequiresGovernedCaseworkAction(genericGovernedTarget.Value))
        {
            return Invalid(
                item,
                $"Lifecycle target {genericGovernedTarget.Value} requires its dedicated governed casework action.",
                ReconciliationBreakQueueTransitionErrorCode.InvalidRequest,
                ["action"],
                genericGovernedTarget);
        }

        if (StatementCaseworkHandoffObligation.HasPending(item)
            && RequiresHumanOrigin(command)
            && !StatementCaseworkHandoffObligation.IsCompletionCommand(item, command))
        {
            return Invalid(
                item,
                $"Reconciliation case {item.BreakId} has a pending statement-source/Operations evidence handoff. Replay the exact retained casework command before applying another material transition.",
                ReconciliationBreakQueueTransitionErrorCode.MissingEvidence,
                ["statementCaseworkHandoff"],
                RequestedLifecycle(command));
        }

        if ((command.EvidenceLinks ?? []).Any(StatementCaseworkHandoffObligation.IsControlMarker)
            && !StatementCaseworkHandoffObligation.IsCompletionCommand(item, command))
        {
            return Invalid(
                item,
                "Statement casework handoff control markers can only be written by the paired governed completion command.",
                ReconciliationBreakQueueTransitionErrorCode.InvalidRequest,
                ["evidenceLinks"],
                RequestedLifecycle(command));
        }

        if (IsImmutableTerminalMutation(item.LifecycleState, command.Action)
            && !StatementCaseworkHandoffObligation.IsCompletionCommand(item, command))
        {
            return Invalid(
                item,
                $"Reconciliation case {item.BreakId} is immutable in lifecycle state {item.LifecycleState}. Use the dedicated governed transition before applying additional casework.",
                ReconciliationBreakQueueTransitionErrorCode.IllegalTransition,
                requestedState: RequestedLifecycle(command));
        }

        return command.Action switch
        {
            _ when RequiresHumanOrigin(command) && !OperationsOriginGuard.IsHumanOperator(command.ActionOrigin)
                => Invalid(item, "Reviewed automation cannot resolve, sign off, or reopen reconciliation cases; a human operator approval is required.", ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator, ["actionOrigin"], RequestedLifecycle(command)),
            ReconciliationCaseworkAction.Assign when string.IsNullOrWhiteSpace(command.Assignee)
                => Invalid(item, "Assignee is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
            ReconciliationCaseworkAction.TransitionStatus when command.Status is null
                => Invalid(item, "Target lifecycle status is required.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Investigating && string.IsNullOrWhiteSpace(item.AssignedTo) && string.IsNullOrWhiteSpace(command.Assignee)
                => Invalid(item, "Assignment is required before investigation.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.AwaitingEvidence && string.IsNullOrWhiteSpace(command.Note)
                => Invalid(item, "Evidence request note is required before awaiting evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence),
            ReconciliationCaseworkAction.TransitionStatus when !IsLegalLifecycleTransition(item.LifecycleState, command.Status.Value)
                => Invalid(item, $"Cannot transition case from {item.LifecycleState} to {command.Status}.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.SetRootCause when !IsKnownRootCause(command.RootCauseCode)
                => Invalid(item, "Root cause code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.SetResolution when !IsKnownResolution(command.ResolutionCode)
                => Invalid(item, "Resolution code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.AddComment when command.StatusTransition.HasValue && !IsLegalLifecycleTransition(item.LifecycleState, command.StatusTransition.Value)
                => Invalid(item, $"Cannot transition case from {item.LifecycleState} to {command.StatusTransition} from comment.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition, requestedState: command.StatusTransition),
            ReconciliationCaseworkAction.EditComment when string.IsNullOrWhiteSpace(command.CommentId)
                => Invalid(item, "Comment id is required for comment edit.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["commentId"]),
            ReconciliationCaseworkAction.EditComment when exactComment is null || exactComment.DeletedAt.HasValue
                => Invalid(item, "An active comment with the exact supplied comment id is required for comment edit.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest, ["commentId"]),
            ReconciliationCaseworkAction.EditComment when !command.Privileged && !string.Equals(exactComment.AuthorId, command.Actor, StringComparison.OrdinalIgnoreCase)
                => Invalid(item, "Only the comment author or a privileged operator can edit this comment.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest, ["actor"]),
            ReconciliationCaseworkAction.DeleteComment when string.IsNullOrWhiteSpace(command.CommentId) || string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Comment id and deletion reason are required for comment deletion.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, ["commentId", "reason"]),
            ReconciliationCaseworkAction.DeleteComment when exactComment is null || exactComment.DeletedAt.HasValue
                => Invalid(item, "An active comment with the exact supplied comment id is required for comment deletion.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest, ["commentId"]),
            ReconciliationCaseworkAction.DeleteComment when !command.Privileged && !string.Equals(exactComment.AuthorId, command.Actor, StringComparison.OrdinalIgnoreCase)
                => Invalid(item, "Only the comment author or a privileged operator can delete this comment.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest, ["actor"]),
            ReconciliationCaseworkAction.Resolve when item.LifecycleState is not (ReconciliationCaseLifecycleState.Investigating or ReconciliationCaseLifecycleState.AwaitingEvidence or ReconciliationCaseLifecycleState.Reopened)
                => Invalid(item, $"Cannot resolve case from {item.LifecycleState}.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(command.Note) && string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Resolving a reconciliation break requires a disposition reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, ["reason"]),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(item.RootCauseCode) && string.IsNullOrWhiteSpace(command.RootCauseCode)
                => Invalid(item, "Root cause code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingRootCause),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(item.ResolutionCode) && string.IsNullOrWhiteSpace(command.ResolutionCode)
                => Invalid(item, "Resolution code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingResolutionCode),
            ReconciliationCaseworkAction.Resolve when !IsKnownRootCause(command.RootCauseCode ?? item.RootCauseCode)
                => Invalid(item, "Root cause code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.Resolve when !IsKnownResolution(command.ResolutionCode ?? item.ResolutionCode)
                => Invalid(item, "Resolution code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.Resolve when !HasRequiredResolutionEvidence(command.ResolutionCode ?? item.ResolutionCode, command.EvidenceLinks, item.EvidenceLinks, command.Note)
                => Invalid(item, "Resolution-specific evidence is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["evidenceLinks"]),
            ReconciliationCaseworkAction.Waive when !IsActiveDispositionState(item.LifecycleState)
                => Invalid(item, $"Cannot waive case from {item.LifecycleState}.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.Waive when string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Waiving a reconciliation break requires a disposition reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, ["reason"]),
            ReconciliationCaseworkAction.Waive when command.EvidenceLinks is not { Count: > 0 }
                => Invalid(item, "Waiving a reconciliation break requires retained evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["evidenceLinks"]),
            ReconciliationCaseworkAction.Waive when string.IsNullOrWhiteSpace(command.ApprovalActor) || string.IsNullOrWhiteSpace(command.ApprovalReference)
                => Invalid(item, "Waiving a reconciliation break requires independent approval and retained approval evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingApproval, ["approvalActor", "approvalReference"]),
            ReconciliationCaseworkAction.Waive when string.Equals(command.ApprovalActor, command.Actor, StringComparison.OrdinalIgnoreCase)
                => Invalid(item, "The operator waiving a reconciliation break cannot approve the same disposition.", ReconciliationBreakQueueTransitionErrorCode.SelfApprovalNotAllowed, ["approvalActor"]),
            ReconciliationCaseworkAction.Supersede when !IsActiveDispositionState(item.LifecycleState)
                => Invalid(item, $"Cannot supersede case from {item.LifecycleState}.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.Supersede when string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Superseding a reconciliation break requires a disposition reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, ["reason"]),
            ReconciliationCaseworkAction.Supersede when command.EvidenceLinks is not { Count: > 0 }
                => Invalid(item, "Superseding a reconciliation break requires retained evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["evidenceLinks"]),
            ReconciliationCaseworkAction.Supersede when string.IsNullOrWhiteSpace(command.SupersedingBreakId) || string.Equals(command.SupersedingBreakId, item.BreakId, StringComparison.OrdinalIgnoreCase)
                => Invalid(item, "A distinct successor break id is required when superseding a reconciliation break.", ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor, ["supersedingBreakId"]),
            ReconciliationCaseworkAction.Supersede when string.IsNullOrWhiteSpace(command.ApprovalActor) || string.IsNullOrWhiteSpace(command.ApprovalReference)
                => Invalid(item, "Superseding a reconciliation break requires independent approval and retained approval evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingApproval, ["approvalActor", "approvalReference"]),
            ReconciliationCaseworkAction.Supersede when string.Equals(command.ApprovalActor, command.Actor, StringComparison.OrdinalIgnoreCase)
                => Invalid(item, "The operator superseding a reconciliation break cannot approve the same disposition.", ReconciliationBreakQueueTransitionErrorCode.SelfApprovalNotAllowed, ["approvalActor"]),
            ReconciliationCaseworkAction.SignOff when item.LifecycleState != ReconciliationCaseLifecycleState.Resolved
                => Invalid(item, "Only resolved cases can be signed off.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.SignOff when string.Equals(item.ResolvedBy, command.Actor, StringComparison.OrdinalIgnoreCase) && (!command.Privileged || string.IsNullOrWhiteSpace(command.Reason))
                => Invalid(item, "Resolver and signer must be different operators unless privileged override and reason are supplied.", ReconciliationBreakQueueTransitionErrorCode.ResolverSignerConflict, ["reason"]),
            ReconciliationCaseworkAction.Reopen when item.LifecycleState != ReconciliationCaseLifecycleState.SignedOff
                => Invalid(item, "Only signed-off cases can be reopened through privileged reopen.", ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed),
            ReconciliationCaseworkAction.Reopen when !command.Privileged || string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Privileged reopen requires a reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason),
            _ => null
        };
    }

    private static bool RequiresHumanOrigin(ReconciliationCaseworkCommand command)
        => command.Action is ReconciliationCaseworkAction.Resolve or ReconciliationCaseworkAction.Waive or ReconciliationCaseworkAction.Supersede or ReconciliationCaseworkAction.SignOff or ReconciliationCaseworkAction.Reopen ||
           command is { Action: ReconciliationCaseworkAction.TransitionStatus, Status: ReconciliationCaseLifecycleState.Resolved or ReconciliationCaseLifecycleState.SignedOff or ReconciliationCaseLifecycleState.Reopened };

    private static bool RequiresGovernedCaseworkAction(ReconciliationCaseLifecycleState state) =>
        state is ReconciliationCaseLifecycleState.Resolved
            or ReconciliationCaseLifecycleState.SignedOff
            or ReconciliationCaseLifecycleState.Reopened
            or ReconciliationCaseLifecycleState.Superseded;

    private static bool IsImmutableTerminalMutation(
        ReconciliationCaseLifecycleState state,
        ReconciliationCaseworkAction action)
        => state switch
        {
            ReconciliationCaseLifecycleState.Resolved => action != ReconciliationCaseworkAction.SignOff,
            ReconciliationCaseLifecycleState.SignedOff => action != ReconciliationCaseworkAction.Reopen,
            ReconciliationCaseLifecycleState.Superseded => true,
            _ => false
        };

    private static ReconciliationCaseLifecycleState? RequestedLifecycle(ReconciliationCaseworkCommand command)
        => command.Action switch
        {
            ReconciliationCaseworkAction.Resolve => ReconciliationCaseLifecycleState.Resolved,
            ReconciliationCaseworkAction.Waive => ReconciliationCaseLifecycleState.Resolved,
            ReconciliationCaseworkAction.Supersede => ReconciliationCaseLifecycleState.Superseded,
            ReconciliationCaseworkAction.SignOff => ReconciliationCaseLifecycleState.SignedOff,
            ReconciliationCaseworkAction.Reopen => ReconciliationCaseLifecycleState.Reopened,
            ReconciliationCaseworkAction.TransitionStatus => command.Status,
            _ => null
        };

    private static ReconciliationBreakQueueTransitionResult Invalid(
        ReconciliationBreakQueueItem? item,
        string error,
        ReconciliationBreakQueueTransitionErrorCode code,
        IReadOnlyList<string>? missingFields = null,
        ReconciliationCaseLifecycleState? requestedState = null)
        => new(
            ReconciliationBreakQueueTransitionStatus.ValidationFailed,
            item,
            error,
            code,
            new ReconciliationCaseValidationProblem(
                item?.LifecycleState.ToString() ?? "Unknown",
                requestedState?.ToString() ?? "Unknown",
                missingFields ?? [],
                error));

    private static bool IsKnownRootCause(string? code)
        => !string.IsNullOrWhiteSpace(code) && RootCauseCodes.Contains(code);

    private static bool IsKnownResolution(string? code)
        => !string.IsNullOrWhiteSpace(code) && ResolutionCodes.Contains(code);

    private static bool HasRequiredResolutionEvidence(
        string? resolutionCode,
        IReadOnlyList<string>? commandEvidence,
        IReadOnlyList<string>? existingEvidence,
        string? note)
    {
        if (string.IsNullOrWhiteSpace(resolutionCode) || !ResolutionEvidencePrefixes.TryGetValue(resolutionCode, out var prefixes))
        {
            return true;
        }

        var evidence = (commandEvidence ?? [])
            .Concat(existingEvidence ?? [])
            .Append(note ?? string.Empty);
        return evidence.Any(value => prefixes.Any(prefix => value.Contains(prefix, StringComparison.OrdinalIgnoreCase)));
    }


    private static readonly HashSet<string> RootCauseCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BrokerCashTiming",
        "CustodianPositionLag",
        "SecurityMasterMapping",
        "LedgerClassification",
        "AccrualTiming",
        "CorporateActionTiming",
        "DismissedFalsePositive"
    };

    private static readonly HashSet<string> ResolutionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LedgerAdjusted",
        "BrokerStatementCorrected",
        "SecurityMasterUpdated",
        "AccrualAdjusted",
        "EvidenceAccepted",
        "DismissedFalsePositive",
        "LegacyResolved"
    };

    private static readonly Dictionary<string, IReadOnlyList<string>> ResolutionEvidencePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LedgerAdjusted"] = ["ledger-event:", "journal:"],
        ["BrokerStatementCorrected"] = ["provider-record:", "statement:"],
        ["SecurityMasterUpdated"] = ["security-master:"],
        ["AccrualAdjusted"] = ["ledger-event:", "journal:"],
        ["EvidenceAccepted"] = ["report-pack:", "evidence:"]
    };

    public static ReconciliationTaxonomySnapshot Taxonomy { get; } = new(
        Version: 1,
        RootCauses: RootCauseCodes.Select(code => new ReconciliationTaxonomyValue(code, code, 1, true)).ToArray(),
        ResolutionCodes: ResolutionCodes.Select(code => new ReconciliationTaxonomyValue(
            code,
            code,
            1,
            true,
            ResolutionEvidencePrefixes.TryGetValue(code, out var prefixes) ? prefixes : null)).ToArray());

    private static bool IsLegalLifecycleTransition(ReconciliationCaseLifecycleState current, ReconciliationCaseLifecycleState next)
        => current == next || (current, next) switch
        {
            (ReconciliationCaseLifecycleState.Open, ReconciliationCaseLifecycleState.Investigating) => true,
            (ReconciliationCaseLifecycleState.Reopened, ReconciliationCaseLifecycleState.Investigating) => true,
            (ReconciliationCaseLifecycleState.Investigating, ReconciliationCaseLifecycleState.AwaitingEvidence) => true,
            (ReconciliationCaseLifecycleState.AwaitingEvidence, ReconciliationCaseLifecycleState.Investigating) => true,
            (ReconciliationCaseLifecycleState.Investigating, ReconciliationCaseLifecycleState.Resolved) => true,
            (ReconciliationCaseLifecycleState.AwaitingEvidence, ReconciliationCaseLifecycleState.Resolved) => true,
            (ReconciliationCaseLifecycleState.Reopened, ReconciliationCaseLifecycleState.Resolved) => true,
            (ReconciliationCaseLifecycleState.Resolved, ReconciliationCaseLifecycleState.SignedOff) => true,
            (ReconciliationCaseLifecycleState.SignedOff, ReconciliationCaseLifecycleState.Reopened) => true,
            _ => false
        };

    private static bool IsActiveDispositionState(ReconciliationCaseLifecycleState state) =>
        state is ReconciliationCaseLifecycleState.Open
            or ReconciliationCaseLifecycleState.InReview
            or ReconciliationCaseLifecycleState.Investigating
            or ReconciliationCaseLifecycleState.AwaitingEvidence
            or ReconciliationCaseLifecycleState.Reopened;

    private static ReconciliationBreakQueueTransitionResult? ValidateSupersessionSuccessor(
        ReconciliationBreakQueueItem source,
        ReconciliationCaseworkCommand command,
        IReadOnlyDictionary<string, ReconciliationBreakQueueItem> items,
        IReadOnlyDictionary<string, string>? pendingSupersessionEdges = null)
    {
        if (command.Action != ReconciliationCaseworkAction.Supersede ||
            string.IsNullOrWhiteSpace(command.SupersedingBreakId))
        {
            return null;
        }

        var successorId = command.SupersedingBreakId.Trim();
        if (string.Equals(successorId, source.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                source,
                "A distinct successor break id is required when superseding a reconciliation break.",
                ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor,
                ["supersedingBreakId"]);
        }

        if (!items.TryGetValue(successorId, out var successor))
        {
            return Invalid(
                source,
                "The successor reconciliation break does not exist.",
                ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor,
                ["supersedingBreakId"]);
        }

        if (!HasMatchingReportingScope(source, successor))
        {
            return Invalid(
                source,
                "The successor reconciliation break does not match the source break's complete reporting scope.",
                ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor,
                ["supersedingBreakId"]);
        }

        if (WouldCreateSupersessionCycle(source.BreakId, successorId, items, pendingSupersessionEdges))
        {
            return Invalid(
                source,
                "The successor reconciliation break would create a supersession cycle.",
                ReconciliationBreakQueueTransitionErrorCode.InvalidRequest,
                ["supersedingBreakId"]);
        }

        if (!IsEligibleSupersessionSuccessor(successor) ||
            pendingSupersessionEdges?.ContainsKey(successorId) == true)
        {
            return Invalid(
                source,
                "The successor reconciliation break must be active and must not already be disposed or scheduled for disposition.",
                ReconciliationBreakQueueTransitionErrorCode.MissingSuccessor,
                ["supersedingBreakId"]);
        }

        return null;
    }

    private static bool IsEligibleSupersessionSuccessor(ReconciliationBreakQueueItem item) =>
        IsActiveDispositionState(item.LifecycleState)
        && item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview
        && item.Disposition is null
        && item.DisposedAt is null
        && string.IsNullOrWhiteSpace(item.SupersedingBreakId);

    private static bool WouldCreateSupersessionCycle(
        string sourceBreakId,
        string successorBreakId,
        IReadOnlyDictionary<string, ReconciliationBreakQueueItem> items,
        IReadOnlyDictionary<string, string>? pendingSupersessionEdges)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentBreakId = successorBreakId;
        while (!string.IsNullOrWhiteSpace(currentBreakId))
        {
            if (string.Equals(currentBreakId, sourceBreakId, StringComparison.OrdinalIgnoreCase) ||
                !visited.Add(currentBreakId))
            {
                return true;
            }

            if (pendingSupersessionEdges?.TryGetValue(currentBreakId, out var pendingSuccessorId) == true)
            {
                currentBreakId = pendingSuccessorId;
                continue;
            }

            if (!items.TryGetValue(currentBreakId, out var current) ||
                string.IsNullOrWhiteSpace(current.SupersedingBreakId))
            {
                return false;
            }

            currentBreakId = current.SupersedingBreakId;
        }

        return false;
    }

    private static bool HasMatchingReportingScope(
        ReconciliationBreakQueueItem source,
        ReconciliationBreakQueueItem successor) =>
        ScopeEquals(source.TenantId, successor.TenantId)
        && ScopeEquals(source.CompanyId, successor.CompanyId)
        && ScopeEquals(source.FundAccountId, successor.FundAccountId)
        && ScopeEquals(source.ExternalAccountId, successor.ExternalAccountId)
        && source.LedgerBookId == successor.LedgerBookId
        && ScopeEquals(source.AccountingPeriodId, successor.AccountingPeriodId)
        && source.AsOfDate == successor.AsOfDate;

    private static bool ScopeEquals(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static ReconciliationBreakQueueItem NormalizeLegacyCaseState(ReconciliationBreakQueueItem item)
    {
        var lifecycle = item.LifecycleState == ReconciliationCaseLifecycleState.InReview
            ? ReconciliationCaseLifecycleState.Investigating
            : item.LifecycleState;
        var status = item.Status;
        var resolutionCode = item.ResolutionCode;
        var rootCauseCode = item.RootCauseCode;
        var signoffStatus = item.SignoffStatus;

        if (item.Status == ReconciliationBreakQueueStatus.Dismissed && item.Disposition is null)
        {
            lifecycle = ReconciliationCaseLifecycleState.Resolved;
            status = ReconciliationBreakQueueStatus.Resolved;
            resolutionCode ??= "DismissedFalsePositive";
            rootCauseCode ??= "DismissedFalsePositive";
            signoffStatus ??= "dismissed-false-positive";
        }

        return item with
        {
            LifecycleState = lifecycle,
            Status = status,
            ResolutionCode = resolutionCode,
            RootCauseCode = rootCauseCode,
            SignoffStatus = signoffStatus,
            TenantId = string.IsNullOrWhiteSpace(item.TenantId) ? null : item.TenantId.Trim(),
            CompanyId = string.IsNullOrWhiteSpace(item.CompanyId) ? null : item.CompanyId.Trim()
        };
    }

    private static ReconciliationBreakQueueItem ApplyCaseworkMutation(ReconciliationBreakQueueItem item, ReconciliationCaseworkCommand command, DateTimeOffset now)
    {
        var comments = (item.Comments ?? []).ToList();
        var evidence = (item.EvidenceLinks ?? []).ToList();
        switch (command.Action)
        {
            case ReconciliationCaseworkAction.Assign:
                return item with { AssignedTo = command.Assignee, AssigneeId = command.Assignee, AssigneeDisplayName = command.Assignee, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.ChangePriority:
                return item with { Priority = command.Priority ?? item.Priority, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.TransitionStatus:
                var lifecycle = command.Status ?? item.LifecycleState;
                return item with
                {
                    LifecycleState = lifecycle,
                    Status = MapQueueStatus(lifecycle, item.Status),
                    AssignedTo = command.Assignee ?? item.AssignedTo,
                    AssigneeId = command.Assignee ?? item.AssigneeId,
                    AssigneeDisplayName = command.Assignee ?? item.AssigneeDisplayName,
                    RootCauseCode = command.RootCauseCode ?? item.RootCauseCode,
                    ResolutionCode = command.ResolutionCode ?? item.ResolutionCode,
                    EvidenceLinks = lifecycle == ReconciliationCaseLifecycleState.Resolved && command.EvidenceLinks is not null
                        ? evidence.Concat(command.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : item.EvidenceLinks,
                    EvidenceCount = lifecycle == ReconciliationCaseLifecycleState.Resolved && command.EvidenceLinks is not null
                        ? evidence.Concat(command.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        : item.EvidenceCount,
                    ResolvedBy = lifecycle == ReconciliationCaseLifecycleState.Resolved ? command.Actor : item.ResolvedBy,
                    ResolvedAt = lifecycle == ReconciliationCaseLifecycleState.Resolved ? now : item.ResolvedAt,
                    LifecycleRationale = command.Note,
                    LastUpdatedAt = now
                };
            case ReconciliationCaseworkAction.AddComment:
                comments.Add(new ReconciliationCaseComment(
                    command.CommentId ?? Guid.NewGuid().ToString("N"),
                    command.ParentCommentId,
                    command.Actor,
                    command.Actor,
                    command.Visibility,
                    command.Note ?? string.Empty,
                    command.EvidenceLinks ?? [],
                    now,
                    Mentions: command.Mentions,
                    LinkedEvidenceIds: command.EvidenceLinks,
                    StatusTransition: command.StatusTransition));
                evidence.AddRange(command.EvidenceLinks ?? []);
                var commentLifecycle = command.StatusTransition ?? item.LifecycleState;
                return item with { LifecycleState = commentLifecycle, Status = MapQueueStatus(commentLifecycle, item.Status), Comments = comments.ToArray(), EvidenceLinks = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), CommentCount = comments.Count(c => c.DeletedAt is null), EvidenceCount = evidence.Distinct(StringComparer.OrdinalIgnoreCase).Count(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.EditComment:
                comments = comments.Select(c => c.CommentId == command.CommentId ? c with { Body = command.Note ?? c.Body, EditedAt = now, PreviousTextHash = HashPayload(c.Body), EditReason = command.Reason } : c).ToList();
                return item with { Comments = comments.ToArray(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.DeleteComment:
                comments = comments.Select(c => c.CommentId == command.CommentId ? c with { DeletedAt = now, DeletedBy = command.Actor, PreviousTextHash = HashPayload(c.Body), DeleteReason = command.Reason } : c).ToList();
                return item with { Comments = comments.ToArray(), CommentCount = comments.Count(c => c.DeletedAt is null), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SetRootCause:
                return item with { RootCauseCode = command.RootCauseCode, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SetResolution:
                return item with { ResolutionCode = command.ResolutionCode, ResolutionNote = command.Note ?? item.ResolutionNote, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.LinkEvidence:
                if (StatementCaseworkHandoffObligation.IsCompletionCommand(item, command))
                {
                    var pendingMarker = StatementCaseworkHandoffObligation.CreatePendingMarker(command.CausationId!);
                    evidence.RemoveAll(value => string.Equals(value, pendingMarker, StringComparison.Ordinal));
                    var remainingBlockedOutputs = (item.BlockedOutputs ?? [])
                        .Where(output =>
                            !string.Equals(output, "FinalReport", StringComparison.Ordinal)
                            && !string.Equals(output, "PeriodClose", StringComparison.Ordinal))
                        .ToArray();
                    evidence.AddRange(command.EvidenceLinks ?? []);
                    var completedEvidence = evidence
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return item with
                    {
                        EvidenceLinks = completedEvidence,
                        EvidenceCount = completedEvidence.Length,
                        BlockedOutputs = remainingBlockedOutputs,
                        FundProfileId = command.CloseScope!.FundProfileId.Trim(),
                        LedgerBookId = command.CloseScope.LedgerBookId,
                        AccountingPeriodId = command.CloseScope.AccountingPeriodId.ToString("D"),
                        AsOfDate = command.CloseScope.AsOfDate,
                        LastUpdatedAt = now
                    };
                }
                evidence.AddRange(command.EvidenceLinks ?? []);
                return item with { EvidenceLinks = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), EvidenceCount = evidence.Distinct(StringComparer.OrdinalIgnoreCase).Count(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Resolve:
                evidence.AddRange(command.EvidenceLinks ?? []);
                var distinctEvidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return item with { LifecycleState = ReconciliationCaseLifecycleState.Resolved, Status = ReconciliationBreakQueueStatus.Resolved, RootCauseCode = command.RootCauseCode ?? item.RootCauseCode, ResolutionCode = command.ResolutionCode ?? item.ResolutionCode, ResolvedBy = command.Actor, ResolvedAt = now, ResolutionNote = FirstNonBlank(command.Note, command.Reason), EvidenceLinks = distinctEvidence, EvidenceCount = distinctEvidence.Length, SignoffStatus = "ready-for-signoff", Disposition = ReconciliationBreakDispositionDto.Resolved, DispositionReason = FirstNonBlank(command.Note, command.Reason), DispositionApprovedBy = command.ApprovalActor, DispositionApprovalReference = command.ApprovalReference, DispositionEvidenceHash = ComputeDispositionEvidenceHash(command, distinctEvidence), DisposedAt = now, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Waive:
                evidence.AddRange(command.EvidenceLinks ?? []);
                var waiverEvidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
                return item with { LifecycleState = ReconciliationCaseLifecycleState.Resolved, Status = ReconciliationBreakQueueStatus.Resolved, ResolvedBy = command.Actor, ResolvedAt = now, ResolutionNote = command.Reason, EvidenceLinks = waiverEvidence, EvidenceCount = waiverEvidence.Length, SignoffStatus = "waived", Disposition = ReconciliationBreakDispositionDto.Waived, DispositionReason = command.Reason, DispositionApprovedBy = command.ApprovalActor, DispositionApprovalReference = command.ApprovalReference, DispositionEvidenceHash = ComputeDispositionEvidenceHash(command, waiverEvidence), DisposedAt = now, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Supersede:
                evidence.AddRange(command.EvidenceLinks ?? []);
                var supersedeEvidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
                return item with { LifecycleState = ReconciliationCaseLifecycleState.Superseded, Status = ReconciliationBreakQueueStatus.Dismissed, ResolvedBy = command.Actor, ResolvedAt = now, ResolutionNote = command.Reason, EvidenceLinks = supersedeEvidence, EvidenceCount = supersedeEvidence.Length, SignoffStatus = "superseded", Disposition = ReconciliationBreakDispositionDto.Superseded, DispositionReason = command.Reason, SupersedingBreakId = command.SupersedingBreakId, DispositionApprovedBy = command.ApprovalActor, DispositionApprovalReference = command.ApprovalReference, DispositionEvidenceHash = ComputeDispositionEvidenceHash(command, supersedeEvidence), DisposedAt = now, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SignOff:
                return item with { LifecycleState = ReconciliationCaseLifecycleState.SignedOff, Status = ReconciliationBreakQueueStatus.SignedOff, SignedOffBy = command.Actor, SignedOffAt = now, SignOffNote = command.Note, SignoffStatus = "signed-off", LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Reopen:
                return item with
                {
                    LifecycleState = ReconciliationCaseLifecycleState.Reopened,
                    Status = ReconciliationBreakQueueStatus.Open,
                    ResolvedBy = null,
                    ResolvedAt = null,
                    ResolutionNote = null,
                    ResolutionCode = null,
                    SignedOffBy = null,
                    SignedOffAt = null,
                    SignOffNote = null,
                    SignoffStatus = null,
                    Disposition = null,
                    DispositionReason = null,
                    SupersedingBreakId = null,
                    DispositionApprovedBy = null,
                    DispositionApprovalReference = null,
                    DispositionEvidenceHash = null,
                    DisposedAt = null,
                    BlockedOutputs = item.BlockedOutputs is { Count: > 0 }
                        ? item.BlockedOutputs
                        : ["FinalReport", "PeriodClose"],
                    ReopenedBy = command.Actor,
                    ReopenedAt = now,
                    ReopenReason = command.Reason,
                    LifecycleRationale = command.Reason,
                    LastUpdatedAt = now
                };
            default:
                return item;
        }
    }

    private static ReconciliationBreakQueueStatus MapQueueStatus(ReconciliationCaseLifecycleState lifecycle, ReconciliationBreakQueueStatus current)
    {
        if (lifecycle is ReconciliationCaseLifecycleState.Open or ReconciliationCaseLifecycleState.Reopened)
        {
            return ReconciliationBreakQueueStatus.Open;
        }

        if (lifecycle == ReconciliationCaseLifecycleState.InReview || lifecycle == ReconciliationCaseLifecycleState.Investigating || lifecycle == ReconciliationCaseLifecycleState.AwaitingEvidence)
        {
            return ReconciliationBreakQueueStatus.InReview;
        }

        if (lifecycle == ReconciliationCaseLifecycleState.Resolved)
        {
            return ReconciliationBreakQueueStatus.Resolved;
        }

        if (lifecycle == ReconciliationCaseLifecycleState.Superseded)
        {
            return ReconciliationBreakQueueStatus.Dismissed;
        }

        return lifecycle == ReconciliationCaseLifecycleState.SignedOff ? ReconciliationBreakQueueStatus.SignedOff : current;
    }

    private ReconciliationBreakQueueItem StampComputedFields(ReconciliationBreakQueueItem item, DateTimeOffset now)
    {
        var policy = _slaPolicyProvider?.ResolvePolicy(item) ?? ReconciliationSlaCalculator.DefaultPolicyFor(item);
        var sla = ReconciliationSlaCalculator.Compute(item, policy, now);
        return item with
        {
            Version = item.Version + 1,
            SlaPolicyId = sla.PolicyId,
            SlaDueAt = sla.DueAt,
            SlaWarningAt = sla.WarningAt,
            SlaBreachedAt = sla.BreachedAt,
            SlaBreached = sla.State == ReconciliationCaseSlaState.Breached,
            SlaState = sla.State,
            AgeBand = sla.AgeBand,
            BusinessAgeHours = sla.BusinessAgeHours,
            LastActivityAt = now,
            Score = ComputeScore(item, now)
        };
    }


    private static bool HasSlaChanged(ReconciliationBreakQueueItem before, ReconciliationBreakQueueItem after)
        => before.SlaState != after.SlaState ||
           before.SlaBreached != after.SlaBreached ||
           before.SlaDueAt != after.SlaDueAt ||
           before.SlaWarningAt != after.SlaWarningAt ||
           before.SlaBreachedAt != after.SlaBreachedAt ||
           !string.Equals(before.SlaPolicyId, after.SlaPolicyId, StringComparison.OrdinalIgnoreCase);

    private ReconciliationBreakQueueAuditEvent CreateAudit(ReconciliationCaseworkCommand command, ReconciliationBreakQueueItem before, ReconciliationBreakQueueItem after, DateTimeOffset now)
        => new(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: command.BreakId,
            EventType: ToAuditEventType(command.Action),
            PreviousStatus: before.Status,
            NewStatus: after.Status,
            PreviousLifecycleState: before.LifecycleState,
            NewLifecycleState: after.LifecycleState,
            OccurredAt: now,
            AssignedTo: after.AssignedTo,
            ReviewedBy: after.ReviewedBy,
            ResolvedBy: after.ResolvedBy,
            Note: command.Note,
            ExceptionRoute: after.ExceptionRoute,
            ToleranceBand: after.ToleranceBand,
            RequiredSignoffRole: after.RequiredSignoffRole,
            SignoffStatus: after.SignoffStatus,
            ExternalAccountId: after.ExternalAccountId,
            CustodianId: after.CustodianId,
            UpstreamSyncCursor: after.UpstreamSyncCursor,
            Actor: command.Actor,
            BeforePayload: JsonSerializer.Serialize(before, _jsonOptions),
            AfterPayload: JsonSerializer.Serialize(after, _jsonOptions),
            CorrelationId: command.CorrelationId,
            CommandId: command.CommandId,
            Source: command.Source,
            Reason: command.Reason,
            CausationId: command.CausationId);

    private static string ToAuditEventType(ReconciliationCaseworkAction action)
        => action switch
        {
            ReconciliationCaseworkAction.Assign => "Assigned",
            ReconciliationCaseworkAction.ChangePriority => "PriorityChanged",
            ReconciliationCaseworkAction.TransitionStatus => "StatusChanged",
            ReconciliationCaseworkAction.AddComment => "CommentAdded",
            ReconciliationCaseworkAction.EditComment => "CommentEdited",
            ReconciliationCaseworkAction.DeleteComment => "CommentDeleted",
            ReconciliationCaseworkAction.SetRootCause => "RootCauseSet",
            ReconciliationCaseworkAction.SetResolution => "ResolutionSet",
            ReconciliationCaseworkAction.LinkEvidence => "EvidenceLinked",
            ReconciliationCaseworkAction.SignOff => "SignedOff",
            ReconciliationCaseworkAction.Reopen => "Reopen",
            ReconciliationCaseworkAction.Resolve => "ResolutionSet",
            ReconciliationCaseworkAction.Waive => "Waived",
            ReconciliationCaseworkAction.Supersede => "Superseded",
            _ => action.ToString()
        };

    private static string ComputeDispositionEvidenceHash(
        ReconciliationCaseworkCommand command,
        IReadOnlyList<string> evidence)
    {
        var payload = string.Join('\n', new[]
        {
            command.Action.ToString(),
            command.Actor.Trim(),
            command.Reason?.Trim() ?? string.Empty,
            command.ApprovalActor?.Trim() ?? string.Empty,
            command.ApprovalReference?.Trim() ?? string.Empty,
            command.SupersedingBreakId?.Trim() ?? string.Empty
        }.Concat(evidence.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
        return HashPayload(payload)!;
    }

    private static ReconciliationBreakScore ComputeScore(ReconciliationBreakQueueItem item, DateTimeOffset now)
    {
        var materiality = Math.Min(50m, Math.Abs(item.Variance));
        var ageHours = Math.Max(0d, (now - item.DetectedAt).TotalHours);
        var ageComponent = Math.Min(25, (int)Math.Round(ageHours / 4d, MidpointRounding.AwayFromZero));
        var counterparty = string.IsNullOrWhiteSpace(item.Counterparty) ? 0 : 15;
        var recurring = item.StateTransitions?.Count(t => t.To == ReconciliationCaseLifecycleState.InReview) > 1 ? 10 : 0;
        var severityScore = (int)Math.Min(100, materiality + ageComponent + counterparty + recurring);
        var priorityScore = Math.Min(100, severityScore + (item.Severity == ReconciliationBreakSeverity.Critical ? 20 : item.Severity == ReconciliationBreakSeverity.High ? 10 : 0));
        return new ReconciliationBreakScore(severityScore, priorityScore, materiality, ageHours, counterparty, recurring, priorityScore >= 70, ComputeSlaDueAt(item), now > ComputeSlaDueAt(item) ? now : null);
    }

    private static DateTimeOffset ComputeSlaDueAt(ReconciliationBreakQueueItem item)
    {
        var hours = item.Severity switch
        {
            ReconciliationBreakSeverity.Critical => 4,
            ReconciliationBreakSeverity.High => 8,
            ReconciliationBreakSeverity.Medium => 24,
            _ => 48
        };
        return item.DetectedAt.AddHours(hours);
    }

    private static bool IsSlaBreached(ReconciliationBreakQueueItem item)
        => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview && DateTimeOffset.UtcNow > ComputeSlaDueAt(item);

    private sealed record CaseworkCommandReceipt(
        string CommandId,
        string BreakId,
        ReconciliationCaseworkAction Action,
        string InputHashSha256,
        ReconciliationBreakQueueItem Result,
        VerifiedOperationOutcome? Outcome = null,
        bool LegacyUnverified = false,
        ReconciliationBreakQueueScope? AccessScope = null);

    private sealed record BulkCaseworkReceipt(
        string BulkActionId,
        string CommandId,
        string IdempotencyKey,
        string InputHashSha256,
        ReconciliationBulkCaseworkResult Result,
        bool LegacyUnverified = false,
        ReconciliationBreakQueueScope? AccessScope = null);

    private sealed record PreparedBulkCasework(
        string BreakId,
        ReconciliationBreakQueueItem? Item,
        ReconciliationCaseworkCommand? Command,
        ReconciliationBreakQueueTransitionResult? Validation);

    private sealed record BulkRequestProblem(
        string Message,
        ReconciliationBreakQueueTransitionErrorCode Code);

    private sealed record BreakQueueSnapshot(
        IReadOnlyList<ReconciliationBreakQueueItem> Items,
        IReadOnlyList<ReconciliationBreakQueueAuditEvent>? AuditEvents = null,
        IReadOnlyList<ReconciliationBulkCaseworkResult>? BulkResults = null,
        IReadOnlyDictionary<string, string>? BulkResultIdsByIdempotencyKey = null,
        IReadOnlyList<CaseworkCommandReceipt>? CommandReceipts = null,
        IReadOnlyList<BulkCaseworkReceipt>? BulkReceipts = null,
        IReadOnlyList<CloseScopeLockRecord>? CloseScopeLocks = null)
    {
        public int SchemaVersion { get; init; } = 1;
        public string? ContentHashSha256 { get; init; }
    }

    private sealed record RepositoryState(
        Dictionary<string, ReconciliationBreakQueueItem> Items,
        List<ReconciliationBreakQueueAuditEvent> AuditEvents,
        Dictionary<string, ReconciliationBulkCaseworkResult> BulkResults,
        Dictionary<string, string> BulkResultIdsByIdempotencyKey,
        Dictionary<string, BulkCaseworkReceipt> BulkReceipts,
        Dictionary<string, CaseworkCommandReceipt> CommandReceipts,
        Dictionary<string, CloseScopeLockRecord> CloseScopeLocks,
        SnapshotStamp? LoadedSnapshotStamp);

    private sealed record SnapshotStamp(long Length, long LastWriteUtcTicks);
}
