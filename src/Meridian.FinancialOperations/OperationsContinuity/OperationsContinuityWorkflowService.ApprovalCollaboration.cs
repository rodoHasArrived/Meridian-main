using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    public async Task<OperationsApprovalCollaborationDto?> GetApprovalCollaborationAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
            return null;
        var approval = workflow.Approvals.LastOrDefault();
        var active = approval is not null && workflow.ApprovalCollaborationBlocker(approval.ApprovalId) is null;
        return new(workflowId, workflow.Version, workflow.ApprovalState, approval?.ApprovalId, approval?.Operator, approval?.Reviewer,
            workflow.ApprovalComments.ToArray(), workflow.Approvals.ToArray(), active, active,
            active ? null : "Discussion changes require an open, currently submitted approval. Retained history remains available.");
    }

    public Task<OperationsTransitionResultDto> AddApprovalCommentAsync(Guid workflowId, OperationsApprovalCommentRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mentions = (request.Mentions ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 4000 || mentions.Length > 50)
            return Task.FromResult(Failure("VALIDATION_FAILED", "A comment of up to 4,000 characters and at most 50 mentioned operators is required.", []));
        return ApplyApprovalCollaborationAsync(workflowId, request.ExpectedVersion, request.ApprovalId, request.CommandId, request.Actor,
            request.Body.Trim(), "approval-comment-added", new { request.ApprovalId, Body = request.Body.Trim(), Mentions = mentions },
            (workflow, now) =>
            {
                workflow.ApprovalComments.Add(new(Guid.NewGuid().ToString("N"), request.ApprovalId, request.Actor.Trim(),
                    OperationsContinuityWorkflowText.RedactSensitiveText(request.Body.Trim()) ?? string.Empty, mentions, now, request.CommandId));
                return null;
            }, ct);
    }

    public Task<OperationsTransitionResultDto> ReassignApprovalAsync(Guid workflowId, OperationsApprovalReassignRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reviewer) || request.Reviewer.Length > 128 || string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Length > 4000)
            return Task.FromResult(Failure("VALIDATION_FAILED", "Select an independent reviewer and supply a reason of up to 4,000 characters.", []));
        return ApplyApprovalCollaborationAsync(workflowId, request.ExpectedVersion, request.ApprovalId, request.CommandId, request.Actor,
            request.Rationale.Trim(), "approval-reviewer-reassigned", new { request.ApprovalId, Reviewer = request.Reviewer.Trim(), Rationale = request.Rationale.Trim() },
            (workflow, _) => workflow.ReassignApprovalReviewer(request.ApprovalId, request.Reviewer,
                OperationsContinuityWorkflowText.RedactSensitiveText(request.Rationale) ?? string.Empty), ct);
    }

    private async Task<OperationsTransitionResultDto> ApplyApprovalCollaborationAsync(Guid workflowId, long expectedVersion,
        string approvalId, string commandId, string actor, string rationale, string eventType, object payload,
        Func<OperationsContinuityWorkflow, DateTimeOffset, OperationsWorkflowBlockerDto?> mutation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 128 || string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(approvalId))
            return Failure("VALIDATION_FAILED", "Approval identity, authenticated actor and a bounded command identity are required.", []);
        var fingerprint = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(new { EventType = eventType, Payload = payload }));
        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (workflow is null)
            return Failure("NOT_FOUND", "Workflow was not found.", []);
        var receipt = workflow.ApprovalCollaborationReceipts.FirstOrDefault(entry => entry.Actor == actor && entry.CommandId == commandId);
        if (receipt is not null)
            return receipt.Fingerprint == fingerprint
            ? Success(await ToDtoAsync(workflow, ct).ConfigureAwait(false))
            : Failure("COMMAND_ID_REUSE", "This command identity already names a different approval change.", []);
        return await ApplyCommandAsync(workflowId, expectedVersion, actor, rationale, commandId, [], eventType,
            // Discussion does not invalidate checklist evidence or alter the approval gate.
            gate: null, requireIntactAuditChain: true,
            precondition: current => current.ApprovalCollaborationBlocker(approvalId),
            command: (current, _, now) =>
            {
                if (mutation(current, now) is { } blocker)
                    return blocker;
                current.ApprovalCollaborationReceipts.Add(new(actor, commandId, fingerprint));
                return null;
            }, ct: ct).ConfigureAwait(false);
    }
}
