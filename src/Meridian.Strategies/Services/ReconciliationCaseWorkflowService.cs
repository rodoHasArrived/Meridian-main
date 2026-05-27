using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationCaseWorkflowService
{
    ReconciliationBreakQueueTransitionResult Apply(ReconciliationBreakQueueItem item, ReconciliationCaseTransitionCommand command, DateTimeOffset now);
}

public sealed class ReconciliationCaseWorkflowService : IReconciliationCaseWorkflowService
{
    public ReconciliationBreakQueueTransitionResult Apply(ReconciliationBreakQueueItem item, ReconciliationCaseTransitionCommand command, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(command.Actor)) return Fail("Actor is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor, item);
        if (string.IsNullOrWhiteSpace(command.Reason)) return Fail("Reason is required.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, item);
        if (command.EvidenceReferences is null || command.EvidenceReferences.Count == 0) return Fail("Evidence references are required.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, item);

        var (next, status) = command.Action switch
        {
            ReconciliationCaseTransitionAction.StartReview when item.LifecycleState is ReconciliationCaseLifecycleState.Open or ReconciliationCaseLifecycleState.Reopened
                => (ReconciliationCaseLifecycleState.InReview, ReconciliationBreakQueueStatus.InReview),
            ReconciliationCaseTransitionAction.RequestApproval when item.LifecycleState == ReconciliationCaseLifecycleState.InReview
                => (ReconciliationCaseLifecycleState.AwaitingApproval, ReconciliationBreakQueueStatus.InReview),
            ReconciliationCaseTransitionAction.Approve when item.LifecycleState == ReconciliationCaseLifecycleState.AwaitingApproval
                => (ReconciliationCaseLifecycleState.Approved, ReconciliationBreakQueueStatus.InReview),
            ReconciliationCaseTransitionAction.Post when item.LifecycleState == ReconciliationCaseLifecycleState.Approved
                => (ReconciliationCaseLifecycleState.Posted, ReconciliationBreakQueueStatus.Resolved),
            ReconciliationCaseTransitionAction.Reopen when item.LifecycleState is ReconciliationCaseLifecycleState.Posted or ReconciliationCaseLifecycleState.Approved
                => (ReconciliationCaseLifecycleState.Reopened, ReconciliationBreakQueueStatus.Open),
            ReconciliationCaseTransitionAction.Supersede when item.LifecycleState != ReconciliationCaseLifecycleState.Superseded
                => (ReconciliationCaseLifecycleState.Superseded, ReconciliationBreakQueueStatus.Dismissed),
            _ => throw new InvalidOperationException("illegal")
        };

        if (command.Action == ReconciliationCaseTransitionAction.Approve && string.Equals(item.ReviewedBy, command.Actor, StringComparison.OrdinalIgnoreCase))
            return Fail("Approver must differ from reviewer.", ReconciliationBreakQueueTransitionErrorCode.DualReviewRequired, item);
        if (command.Action == ReconciliationCaseTransitionAction.Reopen && item.LifecycleState == ReconciliationCaseLifecycleState.Approved)
            return Fail("Cannot reopen from approved prior to posting.", ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed, item);

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
