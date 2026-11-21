using System.Text.Json;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>Internal source observation, never a browser-supplied privileged casework command.</summary>
public sealed record BookReceiptReversalCommand(
    string BreakId, Guid LedgerBookId, string CommandId, long ExpectedVersion,
    IReadOnlyList<string> ReversalIds, IReadOnlyList<string> EvidenceLinks, DateOnly AsOfDate);

public sealed partial class FileReconciliationBreakQueueRepository
{
    public async Task<ReconciliationBreakQueueTransitionResult> ApplyBookReceiptReversalAsync(
        ReconciliationBreakQueueScope scope, BookReceiptReversalCommand request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        if (request.LedgerBookId == Guid.Empty || string.IsNullOrWhiteSpace(request.BreakId)
            || string.IsNullOrWhiteSpace(request.CommandId) || request.AsOfDate == default || request.ExpectedVersion <= 0
            || request.ReversalIds is not { Count: > 0 } || request.ReversalIds.Any(string.IsNullOrWhiteSpace)
            || request.EvidenceLinks is not { Count: > 0 } || request.EvidenceLinks.Any(string.IsNullOrWhiteSpace)
            || request.EvidenceLinks.Any(StatementCaseworkHandoffObligation.IsControlMarker))
            return Invalid(null, "A receipt reversal requires exact book, date, identity and retained source evidence.",
                ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);

        var reversalIds = request.ReversalIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var evidence = request.EvidenceLinks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var command = new ReconciliationCaseworkCommand(
            request.BreakId, ReconciliationCaseworkAction.Reopen, "book-receipt-source", request.CommandId,
            request.CommandId, "book-receipts-v1", request.ExpectedVersion,
            Reason: "Retained receipt or journal reversal reopened the linked receipt obligation.",
            Note: JsonSerializer.Serialize(new
            {
                scope.TenantId, scope.CompanyId, request.LedgerBookId, request.AsOfDate, ReversalIds = reversalIds
            }, _jsonOptions),
            EvidenceLinks: evidence, ActionOrigin: OperationsActionOriginDto.AutomationAssistant);
        var startedAt = DateTimeOffset.UtcNow;
        var inputHash = ComputeCommandInputHash(command);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(request.BreakId, out var item) || !scope.Owns(item)
                || item.LedgerBookId != request.LedgerBookId
                || !string.Equals(item.SourceType, "BookReceipt", StringComparison.Ordinal))
                return ScopeNotFoundResult();

            if (_commandReceipts.TryGetValue(command.CommandId, out var retained))
            {
                if (!AccessScopeEquals(retained.AccessScope, scope))
                    return ScopeNotFoundResult();
                if (retained.LegacyUnverified || retained.Outcome is null || retained.BreakId != request.BreakId
                    || retained.Action != command.Action || retained.InputHashSha256 != inputHash)
                    return new(ReconciliationBreakQueueTransitionStatus.Conflict, item,
                        "The receipt reversal command id is already bound to different evidence.",
                        ReconciliationBreakQueueTransitionErrorCode.CommandIdConflict);
                return new(ReconciliationBreakQueueTransitionStatus.Success, retained.Result) { Outcome = retained.Outcome };
            }

            if (item.Version != request.ExpectedVersion)
                return new(ReconciliationBreakQueueTransitionStatus.Conflict, item,
                    "The receipt exception changed after the source observation.",
                    ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict);
            if (!item.AsOfDate.HasValue || request.AsOfDate < item.AsOfDate.Value)
                return Invalid(item, "A backdated receipt observation cannot reopen later casework.",
                    ReconciliationBreakQueueTransitionErrorCode.InvalidRequest);
            // The casework snapshot can commit even when the subsequent queue-run save fails.
            // Consume source events from that durable audit, so retrying after a later human
            // disposition cannot reopen the same reversal under a different case version.
            var consumed = ReadConsumedBookReceiptReversals(scope, request);
            if (reversalIds.All(consumed.Contains))
                return new(ReconciliationBreakQueueTransitionStatus.Success, item);
            var closeValidation = ValidateCloseScopeMutation(item, command);
            if (closeValidation is not null)
                return closeValidation;
            // Receipt cases can predate a period binding. Missing binding must not let a
            // source observation bypass a retained freeze covering that book and date.
            if (_closeScopeLocks.Values.Any(retained => retained.State != CloseScopeLockState.Reopened
                && retained.Scope.LedgerBookId == request.LedgerBookId
                && retained.Scope.AsOfDate >= item.AsOfDate.Value))
                return Invalid(item, "A retained close freeze covers this receipt exception; governed period reopen is required.",
                    ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed);
            if (item.Status is not (ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed
                or ReconciliationBreakQueueStatus.SignedOff))
                return Invalid(item, "Only a closed receipt exception can be reopened by source reversal.",
                    ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed);

            // The dedicated source path only removes a terminal disposition. It cannot approve,
            // resolve or sign off a case, and never weakens generic operator-only casework guards.
            var state = CaptureState();
            try
            {
                var now = DateTimeOffset.UtcNow;
                var next = ApplyCaseworkMutation(item, command, now);
                var retainedEvidence = (item.EvidenceLinks ?? []).Concat(evidence)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                next = StampComputedFields(next with { EvidenceLinks = retainedEvidence, EvidenceCount = retainedEvidence.Length }, now);
                _items[request.BreakId] = next;
                await AppendAuditAsync(CreateAudit(command, item, next, now) with { EventType = "ReceiptReversalReopened" }, ct)
                    .ConfigureAwait(false);
                var outcome = CreateTransitionOutcome(command, inputHash, OperationTerminalState.Succeeded,
                    startedAt, next, null, _auditEvents[^1]);
                _commandReceipts[command.CommandId] = new CaseworkCommandReceipt(command.CommandId, command.BreakId,
                    command.Action, inputHash, next, outcome, AccessScope: scope);
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
                return new(ReconciliationBreakQueueTransitionStatus.Success, next) { Outcome = outcome };
            }
            catch
            {
                RestoreState(state);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private HashSet<string> ReadConsumedBookReceiptReversals(
        ReconciliationBreakQueueScope scope, BookReceiptReversalCommand request)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var audit in _auditEvents.Where(row => row.BreakId == request.BreakId
            && row.EventType == "ReceiptReversalReopened" && row.Actor == "book-receipt-source"
            && row.Source == "book-receipts-v1" && row.TenantId == scope.TenantId && row.CompanyId == scope.CompanyId))
        {
            if (audit.CommandId is null || !_commandReceipts.TryGetValue(audit.CommandId, out var receipt)
                || receipt.LegacyUnverified || receipt.Outcome is null || !AccessScopeEquals(receipt.AccessScope, scope)
                || receipt.BreakId != request.BreakId || receipt.Action != ReconciliationCaseworkAction.Reopen)
                throw new InvalidDataException("A retained receipt reversal is missing its verified command receipt.");
            var observation = JsonSerializer.Deserialize<BookReceiptReversalObservation>(audit.Note ?? "null", _jsonOptions);
            if (observation is null || observation.TenantId != scope.TenantId || observation.CompanyId != scope.CompanyId
                || observation.LedgerBookId != request.LedgerBookId || observation.ReversalIds is not { Length: > 0 }
                || observation.ReversalIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("A retained receipt reversal observation failed identity verification.");
            result.UnionWith(observation.ReversalIds);
        }
        return result;
    }

    private sealed record BookReceiptReversalObservation(
        string TenantId, string CompanyId, Guid LedgerBookId, DateOnly AsOfDate, string[] ReversalIds);
}
