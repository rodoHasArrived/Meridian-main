using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

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
            LifecycleState = item.LifecycleState.ToString(),
            QueueStatus = item.Status.ToString(),
            Action = command.Action.ToString(),
            Actor = command.Actor,
            Reason = command.Reason,
            EvidenceReferenceCount = command.EvidenceReferences?.Count ?? 0,
            ReviewedBy = item.ResolvedBy ?? item.ReviewedBy ?? string.Empty
        });

        if (!decision.IsValid)
        {
            if (string.Equals(decision.ErrorCode, nameof(ReconciliationBreakQueueTransitionErrorCode.IllegalTransition), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("illegal");
            }

            return Fail(
                decision.Error,
                Enum.Parse<ReconciliationBreakQueueTransitionErrorCode>(decision.ErrorCode),
                item);
        }

        var next = Enum.Parse<ReconciliationCaseLifecycleState>(decision.NextLifecycleState);
        var status = Enum.Parse<ReconciliationBreakQueueStatus>(decision.NextQueueStatus);

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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
