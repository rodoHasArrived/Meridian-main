using Meridian.Contracts.Workstation;

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
