using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Discard seam of the workbench command service: the TERMINAL path for staged revisions,
/// including bound-workflow retirement, overlay-ownership resolution, superseded-sibling
/// terminalization, and the compensating re-discard reconciliation for incomplete withdrawals.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    public async Task<SecurityMasterEditResultDto> DiscardRevisionAsync(
        DiscardSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Discard is the TERMINAL path for a staged revision an operator abandons (Draft) or the
        // bound workflow rejects (Submitted). Without it, any staged sibling defers the
        // security-level override decision forever — the overlay stays Pending and no later
        // approved revision can ever converge it, blocking governed runs indefinitely.
        var revision = await _revisions.GetAsync(request.RevisionId, ct).ConfigureAwait(false);
        if (revision is null)
        {
            throw new SecurityMasterRevisionStateException(request.RevisionId, "no such revision.");
        }
        if (revision.SecurityId != request.SecurityId)
        {
            throw new SecurityMasterRevisionStateException(
                request.RevisionId, $"belongs to security '{revision.SecurityId:D}', not '{request.SecurityId:D}'.");
        }
        // A Rejected revision may be RE-discarded: if a prior discard's transition committed but
        // its withdrawal failed or was canceled, the retry skips the transition and reconciles
        // the withdrawal instead of stranding a Pending overlay value behind an unretryable
        // state precondition.
        if (revision.State is not (SecurityMasterRevisionStateDto.Draft
            or SecurityMasterRevisionStateDto.Submitted
            or SecurityMasterRevisionStateDto.Rejected))
        {
            throw new SecurityMasterRevisionStateException(
                request.RevisionId,
                $"only Draft or Submitted revisions can be discarded (or Rejected ones re-discarded to reconcile " +
                $"an incomplete withdrawal); the revision is {revision.State}.");
        }

        // Retire the BOUND approval workflow before the revision transition: a discarded
        // submission must not leave its workflow approvable, or the generic operations-continuity
        // approval endpoint (which does not consult the revision) could later record approval
        // evidence for an abandoned change. Fail closed — a workflow that cannot be retired keeps
        // the revision Submitted. An already-Approved workflow refuses the discard outright: that
        // is the stranded half-approval, and its remedy is the approve-side reconciliation.
        if (revision.State == SecurityMasterRevisionStateDto.Submitted && revision.WorkflowId is Guid boundWorkflowId)
        {
            var boundWorkflow = await _approvalWorkflow.GetAsync(boundWorkflowId, ct).ConfigureAwait(false);
            if (boundWorkflow is { ApprovalState: OperationsApprovalStateDto.Approved })
            {
                throw new SecurityMasterRevisionStateException(
                    request.RevisionId,
                    "its bound approval workflow is already Approved; retry the approval to reconcile the revision " +
                    "instead of discarding it.");
            }

            if (boundWorkflow is { ApprovalState: OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned })
            {
                // Reviewer evidence must name the person who actually decided: when the workflow
                // has an ASSIGNED reviewer, only that reviewer may discard the submission — the
                // rejection record would otherwise attribute the decision to a reviewer who never
                // made it. An unassigned workflow records the discarding actor.
                var assignedReviewer = boundWorkflow.Approvals
                    .Select(static approval => approval.Reviewer)
                    .LastOrDefault(static reviewer => !string.IsNullOrWhiteSpace(reviewer));
                if (assignedReviewer is not null
                    && !string.Equals(assignedReviewer.Trim(), request.Actor?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityMasterRevisionStateException(
                        request.RevisionId,
                        $"its bound approval workflow is assigned to reviewer '{assignedReviewer}'; only the assigned " +
                        "reviewer may discard a submitted revision, so the rejection evidence names the person who " +
                        "actually decided.");
                }

                var rejectResult = await _approvalWorkflow.RejectWorkflowAsync(
                    boundWorkflowId,
                    new OperationsRejectWorkflowRequestDto(
                        ExpectedVersion: boundWorkflow.Version,
                        Actor: request.Actor,
                        Reviewer: request.Actor,
                        Rationale: string.IsNullOrWhiteSpace(request.Reason)
                            ? "Security Master revision discarded."
                            : request.Reason!,
                        ReasonCode: "SecurityMasterRevisionDiscarded"),
                    ct).ConfigureAwait(false);
                if (!rejectResult.Success)
                {
                    throw new InvalidOperationException(
                        $"The bound approval workflow for revision '{request.RevisionId:D}' could not be retired " +
                        $"({rejectResult.ErrorCode}): {rejectResult.ErrorMessage}. The revision remains Submitted.");
                }
            }
            // A missing or already-Rejected workflow needs no retirement.
        }

        // The transition and the override withdrawal run under the same per-security gate the
        // field-edit and approval routes use, so a discard cannot interleave with an in-flight
        // edit's patch→draft window or an approval's staged-revision check.
        var fieldEditGate = FieldEditGates.GetOrAdd(request.SecurityId, static _ => new SemaphoreSlim(1, 1));
        await fieldEditGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Overlay ownership resolves BEFORE any transition so sibling preconditions can fail
            // the discard closed while the revision is still untouched. Ownership follows staging
            // ORDER: the overlay key holds the value of the LATEST-staged live revision for the
            // path. A LATER sibling that is Draft, Submitted, OR Approved means the discarded
            // revision's value was already replaced — the key carries the sibling's value and
            // stays. Otherwise the discarded revision owns the key; OLDER same-path siblings had
            // their values superseded at staging time. When the latest remaining sibling durably RECORDED its
            // staged value, the discard restores that exact value to the overlay so the sibling
            // keeps governing what its reviewer sees. Only legacy siblings without recorded
            // values are unrecoverable — they cannot be left approvable (a sibling that later
            // reached Approved would publish through a NotPending override decision with its
            // approved value present nowhere): older DRAFT siblings are terminalized to Rejected
            // alongside the withdrawal, and an older SUBMITTED or APPROVED sibling refuses the
            // discard outright — its value is under active review (or already decided) and
            // silently invalidating it is not this actor's call.
            var laterSiblingOwnsPath = false;
            SecurityMasterRevisionRecord? restoreOwner = null;
            SecurityMasterRevisionRecord[] supersededDraftSiblings = [];
            if (!string.IsNullOrWhiteSpace(revision.FieldPath))
            {
                var revisions = await _revisions.ListBySecurityAsync(request.SecurityId, ct).ConfigureAwait(false);
                var samePathSiblings = revisions
                    .Where(other => other.RevisionId != request.RevisionId
                        && string.Equals(other.FieldPath, revision.FieldPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                // PUBLISHED siblings are owners too: a re-discard retried after a later sibling
                // published (the original response was lost) must find that sibling owning the
                // key and leave it in place — withdrawing would silently erase the value the
                // sibling already published.
                laterSiblingOwnsPath = samePathSiblings.Any(other =>
                    other.State is SecurityMasterRevisionStateDto.Draft
                        or SecurityMasterRevisionStateDto.Submitted
                        or SecurityMasterRevisionStateDto.Approved
                        or SecurityMasterRevisionStateDto.Published
                    && other.CreatedAt > revision.CreatedAt);
                if (!laterSiblingOwnsPath)
                {
                    // The LATEST remaining live sibling becomes the key's owner. When its exact
                    // staged value was durably recorded on the revision, the discard RESTORES that
                    // value instead of withdrawing the key — the sibling keeps governing precisely
                    // what its reviewer sees (an approved predecessor publishes its reviewed
                    // value; a draft/submitted one continues review over its own; a published one
                    // gets its already-decided value and decision back). Only legacy siblings
                    // predating value persistence fall back to the fail-closed handling below,
                    // because their values are genuinely unrecoverable.
                    var liveSiblings = samePathSiblings
                        .Where(static other => other.State is SecurityMasterRevisionStateDto.Draft
                            or SecurityMasterRevisionStateDto.Submitted
                            or SecurityMasterRevisionStateDto.Approved
                            or SecurityMasterRevisionStateDto.Published)
                        .ToArray();
                    var latestLive = liveSiblings
                        .OrderByDescending(static other => other.CreatedAt)
                        .FirstOrDefault();
                    if (latestLive is { FieldValueRecorded: true })
                    {
                        restoreOwner = latestLive;
                    }
                    else
                    {
                        var blockingSiblings = liveSiblings
                            .Where(static other => other.State is SecurityMasterRevisionStateDto.Submitted
                                or SecurityMasterRevisionStateDto.Approved
                                or SecurityMasterRevisionStateDto.Published)
                            .Select(static other => other.RevisionId)
                            .ToArray();
                        if (blockingSiblings.Length > 0)
                        {
                            throw new SecurityMasterRevisionStateException(
                                request.RevisionId,
                                $"withdrawing its staged override value at '{revision.FieldPath}' would invalidate older " +
                                $"revision(s) {string.Join(", ", blockingSiblings.Select(static id => $"'{id:D}'"))} that are " +
                                "already Submitted, Approved, or Published for the same field with values superseded at " +
                                "staging time. Decide or discard those revisions first.");
                        }

                        supersededDraftSiblings = liveSiblings
                            .Where(static other => other.State == SecurityMasterRevisionStateDto.Draft)
                            .ToArray();
                    }
                }
            }

            if (revision.State is SecurityMasterRevisionStateDto.Draft or SecurityMasterRevisionStateDto.Submitted)
            {
                await _revisions.TransitionAsync(
                    request.RevisionId,
                    revision.State,
                    SecurityMasterRevisionStateDto.Rejected,
                    request.Actor,
                    ct: ct).ConfigureAwait(false);
            }

            // Withdraw the revision's staged override value. Once the transition has committed,
            // the withdrawal runs on CancellationToken.None — a canceled request must not strand
            // a Rejected revision with its Pending value still in the overlay (and if anything
            // here still fails, re-discarding the Rejected revision reconciles it).
            if (!string.IsNullOrWhiteSpace(revision.FieldPath))
            {
                if (laterSiblingOwnsPath)
                {
                    _logger.LogInformation(
                        "Discarded revision {RevisionId} for {SecurityId} left the staged override at {FieldPath} in place: a later staged revision owns the current value.",
                        request.RevisionId, request.SecurityId, SanitizeForLog(revision.FieldPath));
                }
                else if (restoreOwner is not null)
                {
                    // RESTORE the exact value the latest remaining sibling governs (its recorded
                    // clear removes the key). The patch resets the overlay to Pending, which is
                    // the correct approval posture for every owner state: a draft/submitted owner
                    // was Pending anyway, and an approved owner's decision re-records at publish
                    // over precisely the value its reviewer approved.
                    var restoration = new OperatorOverridesPatchRequest(
                        SetValues: restoreOwner.FieldValue is null
                            ? null
                            : new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [revision.FieldPath!] = restoreOwner.FieldValue,
                            },
                        RemoveKeys: restoreOwner.FieldValue is null ? [revision.FieldPath!] : null)
                    {
                        ReasonCode = string.IsNullOrWhiteSpace(request.Reason)
                            ? "staged revision discarded; predecessor revision's staged value restored"
                            : request.Reason,
                    };
                    var restored = await _overrides
                        .PatchAsync(request.SecurityId, restoration, request.Actor, CancellationToken.None)
                        .ConfigureAwait(false);

                    _logger.LogInformation(
                        "Discarded revision {RevisionId} for {SecurityId} restored the staged override at {FieldPath} to the value governed by revision {OwnerRevisionId}.",
                        request.RevisionId, request.SecurityId, SanitizeForLog(revision.FieldPath), restoreOwner.RevisionId);

                    // A PUBLISHED restore owner already carried its decision through the governed
                    // lifecycle — its restored value must not sit Pending behind
                    // SM_OVERRIDE_APPROVAL_REQUIRED with no revision left to approve it. The
                    // decision re-converges through the same deferral-aware seam publish uses
                    // (other staged revisions still defer it), naming the workflow's recorded
                    // reviewer; a draft/submitted/approved owner correctly stays Pending until its
                    // own approval or publish converges.
                    if (restoreOwner.State == SecurityMasterRevisionStateDto.Published)
                    {
                        try
                        {
                            var decisionReviewer = request.Actor;
                            if (restoreOwner.WorkflowId is Guid ownerWorkflowId)
                            {
                                var ownerWorkflow = await _approvalWorkflow
                                    .GetAsync(ownerWorkflowId, CancellationToken.None).ConfigureAwait(false);
                                var workflowReviewer = ownerWorkflow?.Approvals
                                    .Select(static approval => approval.Reviewer)
                                    .LastOrDefault(static reviewer => !string.IsNullOrWhiteSpace(reviewer));
                                if (!string.IsNullOrWhiteSpace(workflowReviewer))
                                {
                                    decisionReviewer = workflowReviewer!;
                                }
                            }

                            await TryRecordOverrideApprovalDecisionAsync(
                                request.SecurityId,
                                restoreOwner.RevisionId,
                                decisionReviewer,
                                rationale: "Published revision's decision restored after a discard re-staged its value.",
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Re-recording the published revision {OwnerRevisionId}'s override decision for {SecurityId} failed after discarding revision {RevisionId}; the overlay stays Pending until reconciled.",
                                restoreOwner.RevisionId, request.SecurityId, request.RevisionId);
                        }
                    }

                    // Lineage follows the restored value best-effort: the operator attribution row
                    // re-points to the owning revision (or is removed when the owner staged a clear).
                    if (_fieldProvenance is not null)
                    {
                        try
                        {
                            if (restoreOwner.FieldValue is null)
                            {
                                await _fieldProvenance.RemoveAsync(
                                    request.SecurityId,
                                    revision.FieldPath!,
                                    SecurityFieldProvenanceOrigins.OperatorFieldEdit,
                                    clearedAt: restored.UpdatedAt,
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                            else
                            {
                                await _fieldProvenance.UpsertAsync(
                                    new SecurityFieldProvenanceRecord(
                                        request.SecurityId,
                                        revision.FieldPath!,
                                        OperatorSourceSystem,
                                        AsOf: restoreOwner.FieldEffectiveFrom,
                                        UpdatedBy: restoreOwner.Actor,
                                        Confidence: null,
                                        Origin: SecurityFieldProvenanceOrigins.OperatorFieldEdit,
                                        OriginReference: restoreOwner.RevisionId.ToString("D"),
                                        RecordedAt: restored.UpdatedAt),
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Re-pointing operator field-edit lineage for {SecurityId} field {FieldPath} to revision {OwnerRevisionId} failed after discarding revision {RevisionId}.",
                                request.SecurityId, SanitizeForLog(revision.FieldPath), restoreOwner.RevisionId, request.RevisionId);
                        }
                    }
                }
                else
                {
                    // Terminalize the superseded DRAFT siblings before the withdrawal: their
                    // unrecoverable values must not stay approvable once the key is gone. The CAS
                    // (expected Draft) fails closed against a concurrent submission — the failure
                    // propagates, the overlay stays intact, and re-discarding this Rejected
                    // revision reconciles the withdrawal after the sibling is decided.
                    foreach (var sibling in supersededDraftSiblings)
                    {
                        await _revisions.TransitionAsync(
                            sibling.RevisionId,
                            SecurityMasterRevisionStateDto.Draft,
                            SecurityMasterRevisionStateDto.Rejected,
                            request.Actor,
                            ct: CancellationToken.None).ConfigureAwait(false);
                    }

                    // The prior APPROVAL state is part of what the withdrawal must preserve for the
                    // SURVIVING overlay, mirroring the draft-creation compensation path: PatchAsync
                    // resets any nonempty overlay to Pending, so re-discarding a stale key out of
                    // an overlay whose remaining values were already decided (e.g. a later
                    // different-path revision approved and published while this revision sat
                    // Rejected with its withdrawal incomplete) would re-gate those published
                    // values behind SM_OVERRIDE_APPROVAL_REQUIRED with no revision left to
                    // re-approve them.
                    var priorOverlay = await _overrides.GetAsync(request.SecurityId, CancellationToken.None).ConfigureAwait(false);
                    var priorApprovalStatus = priorOverlay?.ApprovalStatus ?? SecurityOverrideApprovalStatusDto.NotRequested;
                    var priorReviewer = priorOverlay?.ReviewedBy;

                    var withdrawal = new OperatorOverridesPatchRequest(
                        SetValues: null,
                        RemoveKeys: [revision.FieldPath!])
                    {
                        ReasonCode = string.IsNullOrWhiteSpace(request.Reason)
                            ? "staged revision discarded; override withdrawn"
                            : request.Reason,
                    };
                    var withdrawn = await _overrides
                        .PatchAsync(request.SecurityId, withdrawal, request.Actor, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (withdrawn.Values.Count > 0
                        && priorApprovalStatus is SecurityOverrideApprovalStatusDto.Approved or SecurityOverrideApprovalStatusDto.Rejected
                        && !string.IsNullOrWhiteSpace(priorReviewer))
                    {
                        try
                        {
                            await _overrides.RecordApprovalDecisionAsync(
                                request.SecurityId,
                                new OperatorOverrideDecision(
                                    priorApprovalStatus,
                                    priorReviewer!,
                                    "Prior decision restored after a discard withdrew a stale override key."),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Restoring the prior override decision for {SecurityId} failed after discarding revision {RevisionId}; the surviving overlay stays Pending until reconciled.",
                                request.SecurityId, request.RevisionId);
                        }
                    }

                    if (supersededDraftSiblings.Length > 0)
                    {
                        _logger.LogWarning(
                            "Discarding revision {RevisionId} for {SecurityId} withdrew the overlay value at {FieldPath}; superseded draft revisions ({SupersededRevisionIds}) were rejected because their staged values are unrecoverable and must be re-staged.",
                            request.RevisionId, request.SecurityId, SanitizeForLog(revision.FieldPath),
                            string.Join(",", supersededDraftSiblings.Select(static other => other.RevisionId.ToString("D"))));
                    }

                    // Lineage follows the withdrawn value best-effort, mirroring a CLEAR: the
                    // operator attribution row must not keep reporting an asserted value the
                    // discard removed.
                    if (_fieldProvenance is not null)
                    {
                        try
                        {
                            await _fieldProvenance.RemoveAsync(
                                request.SecurityId,
                                revision.FieldPath!,
                                SecurityFieldProvenanceOrigins.OperatorFieldEdit,
                                clearedAt: withdrawn.UpdatedAt,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Removing operator field-edit lineage for {SecurityId} field {FieldPath} failed after discarding revision {RevisionId}.",
                                request.SecurityId, SanitizeForLog(revision.FieldPath), request.RevisionId);
                        }
                    }
                }
            }
        }
        finally
        {
            fieldEditGate.Release();
        }

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master revision {RevisionId} for {SecurityId} discarded (Rejected) by {Actor}",
            request.RevisionId, request.SecurityId, SanitizeForLog(request.Actor));

        return BuildLifecycleResult(request.SecurityId, request.RevisionId, currentVersion,
            SecurityMasterRevisionStateDto.Rejected, request.Actor, request.Reason, "operator-field-edit-discarded",
            "Staged revision discarded; the staged override value was withdrawn.");
    }
}
