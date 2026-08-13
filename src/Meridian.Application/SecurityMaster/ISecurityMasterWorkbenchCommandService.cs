using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Governed write surface for the Security Master Passport Workbench. Unifies operator-authored
/// field edits, source-conflict resolution, and the draft → submit → approve → publish lifecycle
/// over the existing Security Master write primitives and the conflict-authority policy.
/// </summary>
public interface ISecurityMasterWorkbenchCommandService
{
    /// <summary>
    /// Stages an operator-authored field edit. A non-empty justification is required (operator
    /// origin), and a stale <c>ExpectedVersion</c> surfaces as a concurrency failure (HTTP 409).
    /// Missing provider data stays review-required — this never fabricates completeness.
    /// </summary>
    Task<SecurityMasterEditResultDto> UpdateSecurityFieldAsync(
        UpdateSecurityFieldRequest request, CancellationToken ct = default);

    /// <summary>
    /// Records an operator's resolution of a source conflict. The conflict-authority policy computes
    /// the default winner; if the operator's choice diverges, the deviation must be acknowledged and
    /// is retained as the audited artifact.
    /// </summary>
    Task<SecurityMasterConflictResolutionDto> ResolveSourceConflictAsync(
        ResolveSourceConflictRequest request, CancellationToken ct = default);

    /// <summary>
    /// Governed conflict resolution for LEGACY callers that carry no record version (the shared
    /// conflict queue's browser and WPF actions): resolves the record's CURRENT version and runs
    /// the full governed path — authority policy, rationale requirement, candidate guard, and the
    /// transactional persisted-value check — with that version. The optimistic freshness guard is
    /// vacuous by construction here; correctness against concurrent amendments is carried by the
    /// conflict store's locked persisted-value revalidation.
    /// </summary>
    Task<SecurityMasterConflictResolutionDto> ResolveSourceConflictAtCurrentVersionAsync(
        ResolveSourceConflictRequest request, CancellationToken ct = default);

    /// <summary>
    /// Transitions a draft revision into the governed approval gate. When the request carries a
    /// workflow id, submission is routed through <c>IOperationsContinuityWorkflowService</c>.
    /// </summary>
    Task<SecurityMasterEditResultDto> SubmitForApprovalAsync(
        SubmitSecurityMasterRevisionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Discards a staged (Draft or Submitted) revision: transitions it to Rejected and withdraws
    /// the staged override value it governs, so an abandoned draft or gate-rejected submission
    /// stops deferring the security-level override decision for every later revision.
    /// </summary>
    Task<SecurityMasterEditResultDto> DiscardRevisionAsync(
        DiscardSecurityMasterRevisionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Applies a raw operator-overrides patch (the generic overrides route's free-form sets and
    /// removals) under the SAME per-security serialization the field-edit, approve, submit, and
    /// discard routes hold. Hosts that wire the workbench must route the generic PATCH endpoint
    /// through this seam instead of the bare store: an ungated patch can land between an
    /// approval's ungoverned-key scan and its recorded security-level decision, silently
    /// co-approving a value no reviewer ever saw.
    /// </summary>
    Task<OperatorOverridesDto> PatchOperatorOverridesAsync(
        Guid securityId, OperatorOverridesPatchRequest request, string updatedBy, CancellationToken ct = default);

    /// <summary>
    /// Records a direct operator-override approval decision (the legacy decision route) under the
    /// per-security gate, refusing with <see cref="InvalidOperationException"/> whenever the
    /// decision belongs to the governed revision lifecycle instead: staged (Draft/Submitted)
    /// revisions are pending, or the overlay carries revision-backed values. Hosts that wire the
    /// workbench must route the legacy decision endpoint through this seam so a
    /// ModifySecurityMaster actor cannot directly approve values whose revisions were never
    /// reviewed.
    /// </summary>
    Task<OperatorOverridesDto> RecordOperatorOverrideDecisionAsync(
        Guid securityId, OperatorOverrideDecision decision, CancellationToken ct = default);

    /// <summary>Approves a submitted revision through the operations-continuity approval gate.</summary>
    Task<SecurityMasterEditResultDto> ApproveRevisionAsync(
        ApproveSecurityMasterRevisionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Publishes an approved revision: fans out to the ordered revision-published handlers and
    /// returns any restatement candidates the period-aware handler surfaced.
    /// </summary>
    Task<SecurityMasterPublishResultDto> PublishRevisionAsync(
        PublishSecurityMasterRevisionRequest request, CancellationToken ct = default);
}
