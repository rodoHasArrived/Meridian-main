using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.Contracts.Integrity;

namespace Meridian.Strategies.Services;

public interface IReconciliationCaseWorkflowService
{
    ReconciliationBreakQueueTransitionResult Apply(ReconciliationBreakQueueItem item, ReconciliationCaseTransitionCommand command, DateTimeOffset now);
}

public sealed class ReconciliationCaseWorkflowService : IReconciliationCaseWorkflowService
{
    public ReconciliationBreakQueueTransitionResult Apply(ReconciliationBreakQueueItem item, ReconciliationCaseTransitionCommand command, DateTimeOffset now)
    {
        var decision = ReconciliationCaseWorkflowInterop.Apply(new ReconciliationCaseTransitionInput
        {
            LifecycleState = ReconciliationCaseWorkflowVocabulary.ToToken(item.LifecycleState),
            QueueStatus = ReconciliationCaseWorkflowVocabulary.ToToken(item.Status),
            Action = ReconciliationCaseWorkflowVocabulary.ToToken(command.Action),
            Actor = command.Actor,
            Reason = command.Reason,
            EvidenceReferenceCount = command.EvidenceReferences?.Count ?? 0,
            ReviewedBy = item.ResolvedBy ?? item.ReviewedBy ?? string.Empty
        });

        if (!decision.IsValid)
        {
            var errorCode = ReconciliationCaseWorkflowVocabulary.ParseErrorCode(decision.ErrorCode);
            if (errorCode == ReconciliationBreakQueueTransitionErrorCode.IllegalTransition)
            {
                throw new InvalidOperationException("illegal");
            }

            return Fail(decision.Error, errorCode, item);
        }

        var next = ReconciliationCaseWorkflowVocabulary.ParseLifecycleState(decision.NextLifecycleState);
        var status = ReconciliationCaseWorkflowVocabulary.ParseQueueStatus(decision.NextQueueStatus);

        var previousHash = item.StateTransitions?.LastOrDefault()?.EntryHash;
        var transition = new ReconciliationCaseStateTransition(Guid.NewGuid().ToString("N"), item.LifecycleState, next, command.Actor, command.Reason, now, command.EvidenceReferences, previousHash, ComputeHash(item.BreakId, item.LifecycleState, next, command, now, previousHash));
        var updated = item with
        {
            LifecycleState = next,
            LifecycleRationale = command.Reason,
            LastUpdatedAt = now,
            Status = status,
            StateTransitions = (item.StateTransitions ?? []).Concat([transition]).ToArray()
        };
        return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated);
    }

    private static ReconciliationBreakQueueTransitionResult Fail(string error, ReconciliationBreakQueueTransitionErrorCode code, ReconciliationBreakQueueItem item)
        => new(ReconciliationBreakQueueTransitionStatus.InvalidTransition, item, error, code);

    private static string ComputeHash(string breakId, ReconciliationCaseLifecycleState from, ReconciliationCaseLifecycleState to, ReconciliationCaseTransitionCommand command, DateTimeOffset now, string? previousHash)
    {
        var payload = JsonSerializer.Serialize(new { breakId, from, to, command.Actor, command.Reason, command.EvidenceReferences, now, previousHash });
        var bytes = Sha256Digest.ComputeBytesUtf8(payload);
        return Convert.ToHexString(bytes);
    }
}
