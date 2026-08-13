using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Operator-override seam of the workbench command service: the security-level override approval
/// decision (gated wrapper + under-gate core with the per-key governance scan), the gate-held
/// generic overrides patch, and the governed legacy decision route.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    /// <summary>
    /// Records the SECURITY-LEVEL operator-override approval decision when — and only when — it
    /// is safe: the override is Pending and no OTHER revision for this security is still staged
    /// (Draft/Submitted), since the decision approves the entire staged Values dictionary and
    /// would otherwise co-approve economics no reviewer has seen; the overlay deliberately stays
    /// Pending until the LAST staged revision is approved. Callers choose the posture per outcome:
    /// the approve seam accepts every outcome (publish converges), while the publish seam treats
    /// <see cref="OverrideDecisionOutcome.Deferred"/> as a retryable publish failure — publishing
    /// through a deferral would mark the revision Published while SM_OVERRIDE_APPROVAL_REQUIRED
    /// still blocks its economics.
    /// </summary>
    private async Task<OverrideDecisionOutcome> TryRecordOverrideApprovalDecisionAsync(
        Guid securityId, Guid revisionId, string reviewer, string? rationale, CancellationToken ct)
    {
        // The staged-revision check and the override decision run under the SAME per-security gate
        // the field-edit route holds across its patch + draft creation. Without it, an edit could
        // commit its Pending value after the revision query below but before the decision, and the
        // security-level approval would silently co-approve the concurrent unreviewed value.
        var fieldEditGate = await FieldEditGates.AcquireAsync(securityId, ct).ConfigureAwait(false);
        try
        {
            return await RecordOverrideApprovalDecisionUnderGateAsync(
                securityId, revisionId, reviewer, rationale, ct).ConfigureAwait(false);
        }
        finally
        {
            fieldEditGate.Dispose();
        }
    }

    /// <summary>
    /// Gate-free core of <see cref="TryRecordOverrideApprovalDecisionAsync"/> for callers that
    /// ALREADY hold this security's field-edit gate — the discard flow converges a published
    /// restore owner's decision from inside its own gate-held region, and the gate is a
    /// non-reentrant semaphore, so re-entering through the gated wrapper would deadlock the
    /// discard. Callers MUST hold the gate; everyone else goes through the wrapper.
    /// </summary>
    private async Task<OverrideDecisionOutcome> RecordOverrideApprovalDecisionUnderGateAsync(
        Guid securityId, Guid revisionId, string reviewer, string? rationale, CancellationToken ct)
    {
        var revisions = await _revisions.ListBySecurityAsync(securityId, ct).ConfigureAwait(false);
        if (revisions.Any(revision => revision.RevisionId != revisionId
            && revision.State is SecurityMasterRevisionStateDto.Draft or SecurityMasterRevisionStateDto.Submitted))
        {
            _logger.LogInformation(
                "Operator-override approval for {SecurityId} deferred: other staged revisions are not yet approved, and the security-level decision would co-approve their unreviewed values.",
                securityId);
            return OverrideDecisionOutcome.Deferred;
        }

        var stagedOverride = await _overrides.GetAsync(securityId, ct).ConfigureAwait(false);
        if (stagedOverride is not { ApprovalStatus: SecurityOverrideApprovalStatusDto.Pending })
        {
            return OverrideDecisionOutcome.NotPending;
        }

        // The security-level decision approves the ENTIRE Values dictionary, and the generic
        // operator-overrides PATCH endpoint can add free-form keys to it (or replace governed
        // ones in place) without creating a revision. A value no revision staged is a value no
        // reviewer's workflow ever saw — recording Approved would silently approve it on the
        // back of an unrelated revision's gate approval. Defer until every overlay key's CURRENT
        // value has revision evidence: the ungoverned value must be withdrawn (or re-staged
        // through the governed field-edit route) before any security-level decision can land.
        // Only the decision's OWN revision being WHOLE-RECORD (no field path) exempts the scan:
        // that revision's reviewer reviewed the record and its overlay as one unit — the legacy
        // pre-field-edit posture. A historical whole-record revision exempts nothing: its
        // reviewer never saw keys patched in after it was decided, and one old Published
        // whole-record row would otherwise waive the scan forever.
        var decidedRevision = revisions.FirstOrDefault(revision => revision.RevisionId == revisionId);
        var wholeRecordRevisionGoverns = decidedRevision is not null
            && string.IsNullOrWhiteSpace(decidedRevision.FieldPath);
        if (!wholeRecordRevisionGoverns)
        {
            var ungovernedKeys = stagedOverride.Values
                .Where(entry => !OverlayKeyHasRevisionEvidence(revisions, entry.Key, entry.Value))
                .Select(static entry => entry.Key)
                .ToArray();
            if (ungovernedKeys.Length > 0)
            {
                _logger.LogWarning(
                    "Operator-override approval for {SecurityId} deferred: overlay key(s) {UngovernedKeys} carry values no revision staged (patched through the generic overrides route); withdraw them or re-stage them through the governed field-edit route.",
                    securityId, SanitizeForLog(string.Join(",", ungovernedKeys)));
                return OverrideDecisionOutcome.Deferred;
            }
        }

        await _overrides.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, reviewer, rationale),
            ct).ConfigureAwait(false);
        return OverrideDecisionOutcome.Recorded;
    }

    /// <summary>
    /// Whether an overlay key's CURRENT value has revision evidence: a non-Rejected revision
    /// staged exactly this value at this path, or a legacy same-path revision predates value
    /// recording (path-only was all the evidence it ever carried). Mere path existence is NOT
    /// enough for value-recording revisions — the generic overrides route can replace a reviewed
    /// value in place, and the replacement must not ride an unrelated approval on the strength of
    /// the old review. A recorded CLEAR (null value) governs the key's absence, never a present
    /// value.
    /// </summary>
    private static bool OverlayKeyHasRevisionEvidence(
        IReadOnlyList<SecurityMasterRevisionRecord> revisions, string key, string value)
        => revisions.Any(revision =>
            revision.State is not SecurityMasterRevisionStateDto.Rejected
            && string.Equals(revision.FieldPath, key, StringComparison.OrdinalIgnoreCase)
            && (!revision.FieldValueRecorded
                || string.Equals(revision.FieldValue, value, StringComparison.Ordinal)));

    private enum OverrideDecisionOutcome
    {
        /// <summary>The Pending overlay's decision was recorded.</summary>
        Recorded,
        /// <summary>No Pending overlay exists to decide (none staged, or already decided).</summary>
        NotPending,
        /// <summary>Other revisions for the security are still staged; deciding now would co-approve their unreviewed values.</summary>
        Deferred
    }

    public async Task<OperatorOverridesDto> PatchOperatorOverridesAsync(
        Guid securityId, OperatorOverridesPatchRequest request, string updatedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The generic overrides route adds or removes free-form overlay keys without staging a
        // revision. It must hold the same per-security gate the field-edit, approve, submit, and
        // discard routes serialize under: without it, a raw patch can land between an approval's
        // ungoverned-key scan and its recorded decision, and the security-level Approved would
        // silently co-approve a value no reviewer ever saw.
        var fieldEditGate = await FieldEditGates.AcquireAsync(securityId, ct).ConfigureAwait(false);
        try
        {
            // While ANY revision is mid-lifecycle, the overlay is under review and free-form
            // mutation is refused outright — the gate alone only serializes calls, it cannot stop
            // a patch from landing BETWEEN a submission and its approval and changing the
            // dictionary the reviewer decides over (a whole-record revision's approval covers the
            // entire overlay, with no per-key scan to catch the insertion), nor from resetting an
            // APPROVED revision's decided overlay to Pending while it awaits publish. The patch
            // becomes legal again once the revisions are published or discarded.
            var revisions = await _revisions.ListBySecurityAsync(securityId, ct).ConfigureAwait(false);
            if (revisions.Any(static revision => revision.State is SecurityMasterRevisionStateDto.Draft
                or SecurityMasterRevisionStateDto.Submitted
                or SecurityMasterRevisionStateDto.Approved))
            {
                throw new InvalidOperationException(
                    $"Operator-override patches for security '{securityId:D}' must wait for the governed revision " +
                    "workflow: revisions are staged or awaiting publish (Draft/Submitted/Approved), and a free-form " +
                    "patch would change the overlay their reviewers decided over. Publish or discard those revisions " +
                    "first.");
            }

            return await _overrides.PatchAsync(securityId, request, updatedBy, ct).ConfigureAwait(false);
        }
        finally
        {
            fieldEditGate.Dispose();
        }
    }

    public async Task<OperatorOverridesDto> RecordOperatorOverrideDecisionAsync(
        Guid securityId, OperatorOverrideDecision decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);

        // The legacy decision route approves or rejects the overlay directly, with none of the
        // revision lifecycle's controls — no bound workflow, no independent-reviewer check, no
        // staged-value deferral. It stays legitimate ONLY for pure legacy overlays no revision
        // governs; the moment revision evidence exists, the decision must come from the governed
        // submit → approve → publish/discard seams, or any ModifySecurityMaster actor (including
        // the editor) could directly approve a value whose revision is still Draft and have
        // governed runs treat it as usable.
        var fieldEditGate = await FieldEditGates.AcquireAsync(securityId, ct).ConfigureAwait(false);
        try
        {
            var revisions = await _revisions.ListBySecurityAsync(securityId, ct).ConfigureAwait(false);
            if (revisions.Any(static revision => revision.State is SecurityMasterRevisionStateDto.Draft
                or SecurityMasterRevisionStateDto.Submitted))
            {
                throw new InvalidOperationException(
                    $"Operator-override decisions for security '{securityId:D}' must go through the governed revision " +
                    "workflow: staged (Draft/Submitted) revisions are pending, and a direct decision would decide their " +
                    "staged values without the bound workflow's review.");
            }

            var overlay = await _overrides.GetAsync(securityId, ct).ConfigureAwait(false);
            if (overlay is { Values.Count: > 0 }
                && overlay.Values.Keys.Any(key => revisions.Any(revision =>
                    revision.State is not SecurityMasterRevisionStateDto.Rejected
                    && !string.IsNullOrWhiteSpace(revision.FieldPath)
                    && string.Equals(revision.FieldPath, key, StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidOperationException(
                    $"Operator-override decisions for security '{securityId:D}' must go through the governed revision " +
                    "workflow: the overlay carries revision-backed values, whose decisions are recorded by the approve, " +
                    "publish, and discard seams.");
            }

            return await _overrides.RecordApprovalDecisionAsync(securityId, decision, ct).ConfigureAwait(false);
        }
        finally
        {
            fieldEditGate.Dispose();
        }
    }
}
