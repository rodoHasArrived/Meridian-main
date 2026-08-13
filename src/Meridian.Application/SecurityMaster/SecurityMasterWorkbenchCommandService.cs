using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Phase 2 governed write surface for the Security Master Passport Workbench.
///
/// <list type="bullet">
///   <item><c>UpdateSecurityFieldAsync</c> — justification-gated operator field edits staged as
///     overlay annotations in <see cref="IOperatorOverridesStore"/>. The passport-displayed security
///     version is an optimistic-concurrency staleness guard (a stale <c>ExpectedVersion</c> surfaces as
///     <see cref="SecurityMasterConcurrencyException"/> → HTTP 409). Overlay edits are deliberately
///     NOT appended to the economic event stream: that stream is replayed verbatim to rebuild the
///     passport, so a partial field-edit payload would corrupt the economic definition.</item>
///   <item><c>ResolveSourceConflictAsync</c> — scored by the conflict-authority policy, enforces
///     acknowledged deviation, and closes the underlying conflict via
///     <see cref="ISecurityMasterConflictService"/> with an atomic open→resolved transition; a
///     concurrent duplicate resolution surfaces as <see cref="SecurityMasterConcurrencyException"/>
///     rather than silently overwriting the first operator's winner.</item>
///   <item><c>SubmitForApprovalAsync</c> / <c>ApproveRevisionAsync</c> — route through the
///     <see cref="IOperationsContinuityWorkflowService"/> approval gate and advance the durable
///     <see cref="ISecurityMasterRevisionStore"/> lifecycle (Draft → Submitted → Approved) with
///     compare-and-set transitions.</item>
///   <item><c>PublishRevisionAsync</c> — refuses to publish unless the revision is durably Approved,
///     fans out to the ordered <see cref="ISecurityMasterRevisionPublishedHandler"/> seam (Phase 3
///     supplies handlers), and surfaces any handler failure (<see cref="SecurityMasterPublishFailedException"/>)
///     leaving the revision Approved for an idempotent retry; only an all-success fan-out transitions
///     the revision to Published.</item>
/// </list>
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService : ISecurityMasterWorkbenchCommandService
{
    private const string OperatorFieldEditEventType = "operator-field-edit";
    private const string OperatorSourceSystem = "operator-workbench";
    private static readonly Regex LogUnsafeControlChars = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F\r\n]+", RegexOptions.Compiled);

    private static string SanitizeForLog(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : LogUnsafeControlChars.Replace(value, " ").Trim();

    // Per-security gates serializing the field-edit validate→patch window (see
    // UpdateSecurityFieldAsync). STATIC so the guarantee holds process-wide regardless of the
    // service's DI lifetime; entries are one SemaphoreSlim per edited security and are never
    // removed — the population is bounded by securities actually edited in-process.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> FieldEditGates = new();

    private readonly ISecurityMasterEventStore _eventStore;
    private readonly IOperatorOverridesStore _overrides;
    private readonly ISecurityMasterConflictAuthorityPolicy _conflictPolicy;
    private readonly ISecurityMasterConflictService _conflictService;
    private readonly ISecurityMasterWorkbenchQueryService _queryService;
    private readonly IOperationsContinuityWorkflowService _approvalWorkflow;
    private readonly ISecurityMasterRevisionStore _revisions;
    private readonly IPeriodAwareRestatementResolver _restatementResolver;
    private readonly IAffectedLedgerBookResolver _affectedLedgerBookResolver;
    private readonly IReadOnlyList<ISecurityMasterRevisionPublishedHandler> _handlers;
    private readonly ILogger<SecurityMasterWorkbenchCommandService> _logger;
    private readonly ISecurityFieldProvenanceStore? _fieldProvenance;
    private readonly ISecurityMasterStore? _projectionStore;
    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog;

    public SecurityMasterWorkbenchCommandService(
        ISecurityMasterEventStore eventStore,
        IOperatorOverridesStore overrides,
        ISecurityMasterConflictAuthorityPolicy conflictPolicy,
        ISecurityMasterConflictService conflictService,
        ISecurityMasterWorkbenchQueryService queryService,
        IOperationsContinuityWorkflowService approvalWorkflow,
        ISecurityMasterRevisionStore revisions,
        IPeriodAwareRestatementResolver restatementResolver,
        IAffectedLedgerBookResolver affectedLedgerBookResolver,
        IEnumerable<ISecurityMasterRevisionPublishedHandler> handlers,
        ILogger<SecurityMasterWorkbenchCommandService> logger,
        ISecurityFieldProvenanceStore? fieldProvenance = null,
        ISecurityMasterStore? projectionStore = null,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _conflictPolicy = conflictPolicy ?? throw new ArgumentNullException(nameof(conflictPolicy));
        _conflictService = conflictService ?? throw new ArgumentNullException(nameof(conflictService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _approvalWorkflow = approvalWorkflow ?? throw new ArgumentNullException(nameof(approvalWorkflow));
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
        _restatementResolver = restatementResolver ?? throw new ArgumentNullException(nameof(restatementResolver));
        _affectedLedgerBookResolver = affectedLedgerBookResolver ?? throw new ArgumentNullException(nameof(affectedLedgerBookResolver));
        _handlers = (handlers ?? throw new ArgumentNullException(nameof(handlers)))
            .OrderBy(static h => h.Order)
            .ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fieldProvenance = fieldProvenance;
        _projectionStore = projectionStore;
        _assetProfileCatalog = assetProfileCatalog;
    }

    public async Task<SecurityMasterEditResultDto> UpdateSecurityFieldAsync(
        UpdateSecurityFieldRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Justification))
        {
            throw new ArgumentException(
                "Operator field edits require a justification.", nameof(request));
        }

        // The passport-displayed security version is an optimistic-concurrency staleness guard: a
        // stale view of the canonical security (it was amended/deactivated since the operator
        // loaded it) is rejected before any overlay write and rechecked atomically by the store.
        // Operator field edits are overlay annotations and must
        // NOT be appended to the economic event stream — that stream is replayed verbatim through
        // SecurityMasterMapping.FromEconomicPayload to rebuild the passport, so a partial
        // field-edit payload would clobber the economic definition on the next reload/replay.
        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (currentVersion == 0)
        {
            // No events means the security was never created in the event-sourced Security Master.
            // Reject the edit before staging an override / opening a draft for a phantom security
            // (the override store upserts by id and would otherwise create overlay state for it).
            throw new InvalidOperationException(
                $"Security '{request.SecurityId:D}' was not found; it cannot be edited.");
        }
        if (request.ExpectedVersion != currentVersion)
        {
            throw new SecurityMasterConcurrencyException(request.SecurityId, request.ExpectedVersion, currentVersion);
        }

        // The validate→patch→draft window runs under a PER-SECURITY gate: validation reads the
        // effective overlay, the patch writes it, and the draft revision anchors it as separate
        // store calls, so two concurrent edits to the same security could otherwise both validate
        // against the same pre-edit overlay and serialize individually valid values that combine
        // into an overlay violating a cross-field invariant (a start and end date, par and
        // principal schedule). The approval path acquires the SAME gate around its staged-revision
        // check and override decision, so a decision can never land between an edit's patch and
        // its draft creation and silently co-approve the unreviewed value. Serializing in-process
        // closes those races for this single write route; a store-level overlay-revision
        // compare-and-set remains the durable answer for multi-node deployments.
        var fieldEditGate = FieldEditGates.GetOrAdd(request.SecurityId, static _ => new SemaphoreSlim(1, 1));
        await fieldEditGate.WaitAsync(ct).ConfigureAwait(false);
        string fieldPath;
        OperatorOverridesDto stagedOverride;
        SecurityMasterRevisionRecord revision;
        string? priorOverrideValue = null;
        var hadPriorOverride = false;
        var isClear = string.IsNullOrWhiteSpace(request.NewValue);
        try
        {
            // Edits addressing the assetSpecificTerms namespace are anchored to the declared
            // per-asset-class schema: the key must be a declared term field and the value must coerce
            // to its declared type. Paths outside that namespace remain the free annotation surface.
            // The returned path is the schema-canonical spelling — persisting the caller's raw alias or
            // casing variant would fork the same term into separate override keys, revisions, and
            // provenance rows, so every write below uses the canonical path.
            fieldPath = await EnsureFieldEditIsSchemaValidAsync(request, ct).ConfigureAwait(false);

            // Stage the operator value as an override read-model annotation. The override store applies
            // the patch under a serializable, row-locked transaction; it does not advance the economic
            // version, so the returned NewVersion is the unchanged canonical version. A blank value is
            // a CLEAR: it removes the overlay key rather than persisting an empty-string override that
            // would bypass type validation and read as an asserted value downstream.
            // Capture the field's PRIOR staged value for compensation: if the draft revision
            // cannot be created after the patch commits, the overlay is reverted so no ungoverned
            // Pending value outlives the failed edit.
            var priorOverlay = await _overrides.GetAsync(request.SecurityId, ct).ConfigureAwait(false);
            if (priorOverlay is not null)
            {
                foreach (var (priorPath, priorValue) in priorOverlay.Values)
                {
                    if (string.Equals(priorPath, fieldPath, StringComparison.OrdinalIgnoreCase))
                    {
                        priorOverrideValue = priorValue;
                        hadPriorOverride = true;
                        break;
                    }
                }
            }

            // The prior APPROVAL state is part of what compensation must restore: PatchAsync
            // resets any nonempty overlay to Pending, so reverting the values alone would leave a
            // previously Approved overlay Pending with no new revision to approve it — blocking
            // governed runs behind SM_OVERRIDE_APPROVAL_REQUIRED for values a reviewer already
            // decided.
            var priorApprovalStatus = priorOverlay?.ApprovalStatus ?? SecurityOverrideApprovalStatusDto.NotRequested;
            var priorReviewer = priorOverlay?.ReviewedBy;
            var priorHadValues = priorOverlay is { Values.Count: > 0 };

            var patch = new OperatorOverridesPatchRequest(
                SetValues: isClear
                    ? null
                    : new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [fieldPath] = request.NewValue!,
                    },
                RemoveKeys: isClear ? [fieldPath] : null)
            {
                ReasonCode = request.Justification,
            };
            try
            {
                stagedOverride = await _overrides
                    .PatchAsync(
                        request.SecurityId,
                        patch,
                        request.Actor,
                        ct,
                        expectedCanonicalVersion: currentVersion)
                    .ConfigureAwait(false);
            }
            catch (OperatorOverrideCanonicalVersionConflictException ex)
            {
                throw new SecurityMasterConcurrencyException(
                    request.SecurityId,
                    request.ExpectedVersion,
                    ex.CurrentVersion);
            }

            // Open a durable Draft revision carrying the field-edit metadata so the governed lifecycle
            // (submit → approve → publish) is anchored to a real, server-issued revision id, and publish
            // can later emit the correct effective-date and changed-field set for downstream impact
            // analysis rather than defaulting to publish time.
            // Persist the edit's optional fund-profile scope on the draft so publish can resolve a SCOPED
            // downstream impact (and therefore real affected ledger books + restatement candidates). Without
            // it, publish falls back to an unscoped impact whose empty affected-book set short-circuits the
            // period-aware restatement path to "no restatement".
            // The draft is created while the gate is STILL HELD: the approval path checks for staged
            // revisions under the same gate, so releasing between the patch and the draft would open a
            // window where an in-flight approval sees the freshly Pending value with no staged revision
            // and co-approves it unreviewed.
            try
            {
                // The draft durably records the exact overlay VALUE it governs (null models a
                // CLEAR): without it, discarding a later same-path revision could never restore
                // this revision's value — an approved predecessor would deadlock unpublishable.
                revision = await _revisions.CreateDraftAsync(
                    request.SecurityId, request.Actor, fieldPath, request.EffectiveFrom, request.Justification,
                    request.FundProfileId,
                    new SecurityMasterRevisionFieldValue(isClear ? null : request.NewValue),
                    ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // COMPENSATE the committed patch: without a draft revision there is no approval
                // workflow that can ever govern the staged value, so leaving it would block governed
                // runs behind SM_OVERRIDE_APPROVAL_REQUIRED until an operator happens to restage it.
                // The overlay is reverted to the field's prior state (previous override restored, or
                // the key removed when none existed) and the edit reports failure for a clean retry.
                // Cancellation is a post-patch failure like any other — the patch is already durable
                // — so the revert runs on CancellationToken.None: the canceled request token that
                // aborted the draft creation must not also abort the compensation.
                try
                {
                    var compensation = new OperatorOverridesPatchRequest(
                        SetValues: hadPriorOverride
                            ? new Dictionary<string, string>(StringComparer.Ordinal) { [fieldPath] = priorOverrideValue! }
                            : null,
                        RemoveKeys: hadPriorOverride ? null : [fieldPath])
                    {
                        ReasonCode = "field-edit draft creation failed; compensating overlay revert",
                    };
                    await _overrides.PatchAsync(request.SecurityId, compensation, request.Actor, CancellationToken.None).ConfigureAwait(false);

                    // The compensating patch reset the surviving values to Pending; when the prior
                    // overlay carried a recorded decision, re-record it so already-reviewed values do
                    // not fall back behind SM_OVERRIDE_APPROVAL_REQUIRED with no revision to approve
                    // them. The restored decision is audit-trailed with an explicit comment.
                    if (priorHadValues
                        && priorApprovalStatus is SecurityOverrideApprovalStatusDto.Approved or SecurityOverrideApprovalStatusDto.Rejected
                        && !string.IsNullOrWhiteSpace(priorReviewer))
                    {
                        await _overrides.RecordApprovalDecisionAsync(
                            request.SecurityId,
                            new OperatorOverrideDecision(
                                priorApprovalStatus,
                                priorReviewer!,
                                "Prior decision restored after a failed field-edit draft creation reverted the overlay."),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception revertEx)
                {
                    _logger.LogError(
                        revertEx,
                        "Compensating overlay revert failed for {SecurityId} field {FieldPath}: a Pending override remains without a governing draft revision until the edit is retried or the value cleared.",
                        request.SecurityId, SanitizeForLog(fieldPath));
                }

                if (ex is OperationCanceledException)
                {
                    throw;
                }

                throw new InvalidOperationException(
                    $"The draft revision for the field edit on security '{request.SecurityId:D}' could not be created; " +
                    "the staged override was reverted. Retry the edit once the revision store is reachable.", ex);
            }
        }
        finally
        {
            fieldEditGate.Release();
        }

        // FieldPath and Actor are operator-supplied text; strip control characters so they cannot
        // forge log entries.
        _logger.LogInformation(
            "Security Master field edit staged for {SecurityId} field {FieldPath} as draft revision {RevisionId} at version {Version} by {Actor}",
            request.SecurityId, SanitizeForLog(fieldPath), revision.RevisionId, currentVersion, SanitizeForLog(request.Actor));

        // Record the edit's field-level attribution (origin OperatorFieldEdit, referenced to the
        // draft revision) so overlay lineage is durable alongside canonical conflict-resolution
        // lineage. A CLEAR withdraws the asserted value, so it removes the operator attribution row
        // instead of upserting one — otherwise lineage would keep reporting an asserted operator
        // value that no longer exists (and a clear after an overlay edit would even repoint that
        // row's revision reference). Best-effort: the staged edit and draft revision are already the
        // authoritative artifacts, so a lineage write failure is logged rather than failing the edit.
        if (_fieldProvenance is not null)
        {
            try
            {
                if (isClear)
                {
                    // The recorded_at guard in the store keeps the ordering invariant: a delayed
                    // clear cannot erase an attribution recorded after the clear's overlay write.
                    await _fieldProvenance.RemoveAsync(
                        request.SecurityId,
                        fieldPath,
                        SecurityFieldProvenanceOrigins.OperatorFieldEdit,
                        clearedAt: stagedOverride.UpdatedAt,
                        ct: CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await _fieldProvenance.UpsertAsync(
                        new SecurityFieldProvenanceRecord(
                            request.SecurityId,
                            fieldPath,
                            OperatorSourceSystem,
                            AsOf: request.EffectiveFrom,
                            UpdatedBy: request.Actor,
                            Confidence: null,
                            Origin: SecurityFieldProvenanceOrigins.OperatorFieldEdit,
                            OriginReference: revision.RevisionId.ToString("D"),
                            // PatchAsync returns the timestamp assigned while the serialized overlay
                            // write holds its row lock. Ordering lineage by that authoritative write
                            // time prevents a delayed older edit from replacing a newer attribution.
                            RecordedAt: stagedOverride.UpdatedAt),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Cancellation is absorbed like any other lineage failure: the overlay patch and
                // draft revision have already COMMITTED, so propagating an
                // OperationCanceledException here would hide the durable draft from the caller —
                // a retry would then stage a second same-field revision whose hidden sibling
                // defers the security-level override decision until separately discovered.
                var safeFieldPathForLog = SanitizeForLog(fieldPath);
                _logger.LogWarning(
                    ex,
                    "Field edit for {SecurityId} field {FieldPath} staged, but recording field provenance failed; the override and draft revision remain authoritative.",
                    request.SecurityId, safeFieldPathForLog);
            }
        }

        var changeEntry = BuildChangeEntry(
            currentVersion,
            eventType: OperatorFieldEditEventType,
            actor: request.Actor,
            effectiveFrom: request.EffectiveFrom,
            sourceRecordId: request.SourceRecordId,
            reason: request.Justification,
            summary: $"Operator edit to {fieldPath}.",
            changedFields: [fieldPath]);

        return new SecurityMasterEditResultDto(
            request.SecurityId,
            revision.RevisionId,
            currentVersion,
            SecurityMasterRevisionStateDto.Draft,
            changeEntry);
    }

    public async Task<SecurityMasterConflictResolutionDto> ResolveSourceConflictAtCurrentVersionAsync(
        ResolveSourceConflictRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);
        return await ResolveSourceConflictAsync(request with { ExpectedVersion = currentVersion }, ct).ConfigureAwait(false);
    }

    public async Task<SecurityMasterConflictResolutionDto> ResolveSourceConflictAsync(
        ResolveSourceConflictRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException(
                "Conflict resolution requires a reason.", nameof(request));
        }

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (request.ExpectedVersion != currentVersion)
        {
            throw new SecurityMasterConcurrencyException(request.SecurityId, request.ExpectedVersion, currentVersion);
        }

        var snapshot = await _queryService
            .GetTrustSnapshotAsync(request.SecurityId, fundProfileId: null, ct)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"Trust snapshot for security '{request.SecurityId}' was not found.");
        }

        var assessment = snapshot.ConflictAssessments?
            .FirstOrDefault(a => a.Conflict.ConflictId == request.ConflictId)
            ?? throw new InvalidOperationException(
                $"Conflict '{request.ConflictId}' was not found for security '{request.SecurityId}'.");

        // The chosen winner must be one of the conflict's two competing sources (the same pair the
        // authority policy decides between). Without this, an acknowledged deviation would let a typo
        // or arbitrary source name close the conflict and record a "winner" that was never in conflict.
        EnsureChosenWinnerIsCandidate(assessment, request.ChosenWinnerSource);

        var providerConfidence = snapshot.InstrumentPassport?.ProviderConfidence
            ?? Array.Empty<InstrumentPassportProviderConfidenceDto>();

        var decision = _conflictPolicy.Evaluate(assessment, providerConfidence);
        var isDeviation = !string.Equals(
            request.ChosenWinnerSource?.Trim() ?? string.Empty,
            decision.PolicyWinnerSource?.Trim() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        if (isDeviation && !request.AcknowledgePolicyDeviation)
        {
            throw new ArgumentException(
                "Choosing a winner that diverges from the policy winner requires acknowledging the deviation.",
                nameof(request));
        }

        // Close the underlying conflict AND record the chosen winner in the SAME atomic transition,
        // so there is no window in which the conflict is resolved but its winner is unrecorded. A
        // null result means another operator already resolved this conflict, so surface it as a
        // concurrency conflict rather than letting the later decision silently win.
        var resolution = assessment.Recommendation == SecurityMasterConflictRecommendationKind.DismissAsEquivalent
            ? "Dismiss"
            : "Resolve";
        var resolved = await _conflictService
            .ResolveAsync(
                new ResolveConflictRequest(
                    request.ConflictId, resolution, request.Actor, request.Reason, request.ChosenWinnerSource),
                ct)
            .ConfigureAwait(false);
        if (resolved is null)
        {
            throw new SecurityMasterConcurrencyException(request.SecurityId, request.ExpectedVersion, currentVersion);
        }

        // The winner is deliberately NOT mirrored into the governed override dictionary: the
        // authoritative resolution (and its display surface) is the durable conflict record
        // itself, and a PatchAsync mirror would reset the entire override row to Pending —
        // minting a display-only value no approval workflow could ever decide, blocking governed
        // runs behind SM_OVERRIDE_APPROVAL_REQUIRED and reopening previously Approved economic
        // overrides on the same row.

        _logger.LogInformation(
            "Resolved Security Master conflict {ConflictId} for {SecurityId}: policy winner {PolicyWinner}, chosen {Chosen}, deviation {IsDeviation}",
            request.ConflictId, request.SecurityId, decision.PolicyWinnerSource, request.ChosenWinnerSource, isDeviation);

        return new SecurityMasterConflictResolutionDto(
            request.ConflictId,
            decision.PolicyWinnerSource,
            request.ChosenWinnerSource,
            isDeviation,
            request.Reason,
            currentVersion);
    }

    public async Task<SecurityMasterEditResultDto> SubmitForApprovalAsync(
        SubmitSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The ENTIRE submission — preflight, gate-workflow mutation, and revision transition —
        // runs under the same per-security gate the field-edit, approval, and discard routes
        // hold. Without it, a discard can transition the revision to Rejected and withdraw its
        // overlay while this method awaits the external workflow submission: the submission then
        // commits a Submitted workflow before the Draft→Submitted CAS fails, leaving an unbound,
        // still-approvable workflow the discard never retired (it observed a plain Draft).
        var fieldEditGate = FieldEditGates.GetOrAdd(request.SecurityId, static _ => new SemaphoreSlim(1, 1));
        await fieldEditGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SubmitForApprovalUnderGateAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            fieldEditGate.Release();
        }
    }

    private async Task<SecurityMasterEditResultDto> SubmitForApprovalUnderGateAsync(
        SubmitSecurityMasterRevisionRequest request, CancellationToken ct)
    {
        // Preflight the revision BEFORE mutating the approval gate: a stale/mistyped revision id must
        // not leave an orphaned submitted workflow that can never be published. The TransitionAsync
        // CAS below remains the authority against concurrent advancement.
        await EnsureRevisionInStateAsync(
            request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Draft, ct).ConfigureAwait(false);

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);
        var rationale = string.IsNullOrWhiteSpace(request.Note)
            ? "Security Master revision submitted for approval."
            : request.Note!;

        if (request.WorkflowId is { } workflowId)
        {
            // A governed submission must name an INDEPENDENT reviewer. Defaulting a blank reviewer to
            // the submitter would create a self-approval path, because the approval gate only checks
            // the approving reviewer matches the assigned reviewer — it does not enforce independence.
            if (string.IsNullOrWhiteSpace(request.Reviewer))
            {
                throw new ArgumentException(
                    "An independent reviewer is required to submit a Security Master revision for approval.",
                    nameof(request));
            }
            if (string.Equals(request.Reviewer.Trim(), request.Actor?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The reviewer must be independent from the submitter; self-approval is not permitted.",
                    nameof(request));
            }

            // The submission rationale carries a durable REVISION-IDENTITY marker into the
            // workflow's approval evidence. Reconciling a stranded half-submission (below) is only
            // safe for the exact interrupted submission: without the marker, ANY draft — even one
            // for another security — could claim an already-submitted workflow as its own
            // interrupted submission and bind to it, letting one gate approval decide multiple
            // unrelated edits through the approve-side reconciliation.
            var revisionMarker = $"[security-master-revision:{request.RevisionId:D}]";
            var dto = new OperationsSubmitApprovalRequestDto(
                ExpectedVersion: request.ExpectedWorkflowVersion,
                Actor: request.Actor,
                Reviewer: request.Reviewer!,
                Rationale: $"{rationale} {revisionMarker}",
                ReportPackId: request.ReportPackId ?? string.Empty);

            var result = await _approvalWorkflow.SubmitForApprovalAsync(workflowId, dto, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                // RECONCILE a stranded half-submission instead of dead-ending. The gate submission
                // is irreversible: if a prior attempt submitted the workflow but failed (or was
                // canceled) before the revision transition below committed, the workflow is
                // Submitted/ReviewerAssigned while the revision is still an unbound Draft — and
                // the gate rejects every retry because the workflow is no longer draft-state, so
                // without this branch the revision could never bind or submit, while the orphaned
                // workflow stayed independently approvable. Only the EXACT interrupted submission
                // reconciles: the workflow's recorded submission evidence must name THIS revision
                // (the rationale marker persisted by the prior attempt); any other gate failure —
                // including a workflow submitted for a different revision — still rejects.
                var submittedWorkflow = await _approvalWorkflow.GetAsync(workflowId, ct).ConfigureAwait(false);
                var recordedSubmission = submittedWorkflow is
                { ApprovalState: OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned }
                    ? submittedWorkflow.Approvals.LastOrDefault(static approval =>
                        approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
                    : null;
                if (recordedSubmission?.Rationale?.Contains(revisionMarker, StringComparison.OrdinalIgnoreCase) != true)
                {
                    throw new InvalidOperationException(
                        $"Submit for approval was blocked ({result.ErrorCode}): {result.ErrorMessage}");
                }

                _logger.LogWarning(
                    "Approval gate workflow {WorkflowId} is already submitted while revision {RevisionId} for {SecurityId} is still an unbound Draft; reconciling the stranded revision transition and binding.",
                    workflowId, request.RevisionId, request.SecurityId);
            }

            // Advance the durable revision lifecycle only after the gate accepts the submission, and
            // bind the submitting workflow so approval can be restricted to this same lane.
            await _revisions.TransitionAsync(
                request.RevisionId,
                SecurityMasterRevisionStateDto.Draft,
                SecurityMasterRevisionStateDto.Submitted,
                request.Actor,
                workflowIdForSubmit: workflowId,
                ct: ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Security Master revision {RevisionId} for {SecurityId} submitted through approval gate workflow {WorkflowId}",
                request.RevisionId, request.SecurityId, workflowId);

            return BuildLifecycleResult(request.SecurityId, request.RevisionId, result.NewVersion ?? currentVersion,
                SecurityMasterRevisionStateDto.Submitted, request.Actor, rationale, "operator-field-edit-submitted",
                "Revision submitted for approval through the operations-continuity gate.");
        }

        // A workflow-less submission is rejected at the service boundary, matching the governed
        // HTTP endpoint: the only approval command routes through the operations-continuity gate
        // and requires the revision's BOUND workflow to match the approving one, so a revision
        // submitted with no workflow binding could never be approved — it would strand permanently
        // in Submitted with no transition back to Draft and no alternate approval path.
        throw new ArgumentException(
            "A Security Master revision submission requires an approval workflow. Supply the WorkflowId of the " +
            "operations-continuity workflow governing this revision; a workflow-less submission cannot be " +
            "approved and would strand the revision in Submitted.",
            nameof(request));
    }

    public async Task<SecurityMasterEditResultDto> ApproveRevisionAsync(
        ApproveSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Preflight the revision BEFORE mutating the approval gate (same orphaned-lane reasoning as
        // SubmitForApprovalAsync); the TransitionAsync CAS below is the authority on concurrency.
        var revision = await EnsureRevisionInStateAsync(
            request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Submitted, ct).ConfigureAwait(false);

        // Approval must run through the SAME workflow the revision was submitted to. Otherwise a
        // caller could approve revision A via an unrelated, already-approvable workflow lane.
        if (revision.WorkflowId != request.WorkflowId)
        {
            throw new SecurityMasterRevisionStateException(
                request.RevisionId,
                $"was submitted through workflow '{revision.WorkflowId:D}' but approval was attempted via workflow '{request.WorkflowId:D}'.");
        }

        var dto = new OperationsApprovalDecisionRequestDto(
            ExpectedVersion: request.ExpectedWorkflowVersion,
            Actor: request.Actor,
            Reviewer: request.Reviewer,
            Rationale: request.Rationale,
            ReportPackId: request.ReportPackId,
            CorrelationId: request.CorrelationId);

        var result = await _approvalWorkflow.ApproveWorkflowAsync(request.WorkflowId, dto, ct).ConfigureAwait(false);
        var decisionReviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? request.Actor : request.Reviewer;
        if (!result.Success)
        {
            // RECONCILE a stranded half-approval instead of dead-ending. The gate approval is
            // irreversible: if a prior attempt approved the workflow but failed (or was canceled)
            // before the revision transition below committed, the revision is still Submitted
            // while the workflow is Approved — and the gate rejects every retry because it only
            // accepts Submitted/ReviewerAssigned workflows, so without this branch the revision
            // could never reach Approved or Published. When the BOUND workflow's decision has
            // already been recorded, the retry completes the revision-side transition instead of
            // failing forever; any other gate failure still rejects the approval.
            var workflow = await _approvalWorkflow.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
            if (workflow is not { ApprovalState: OperationsApprovalStateDto.Approved })
            {
                throw new InvalidOperationException(
                    $"Revision approval was blocked ({result.ErrorCode}): {result.ErrorMessage}");
            }

            // A RECONCILING retry did not decide anything — the workflow's retained approval did.
            // The retrying caller may be a different ModifySecurityMaster user (the endpoint
            // server-binds the request reviewer to the current actor), so recording them would
            // attribute the override decision to someone who never reviewed it; the publish-time
            // lookup cannot repair this later because the overlay is already Approved. Only when
            // the workflow retained no reviewer name does the retrying caller stand in.
            var recordedReviewer = workflow.Approvals
                .Select(static approval => approval.Reviewer)
                .LastOrDefault(static reviewer => !string.IsNullOrWhiteSpace(reviewer));
            if (!string.IsNullOrWhiteSpace(recordedReviewer))
            {
                decisionReviewer = recordedReviewer!;
            }

            _logger.LogWarning(
                "Approval gate workflow {WorkflowId} is already Approved while revision {RevisionId} for {SecurityId} is still Submitted; reconciling the stranded revision transition.",
                request.WorkflowId, request.RevisionId, request.SecurityId);
        }

        // Advance the durable revision lifecycle only after the gate records the approval.
        await _revisions.TransitionAsync(
            request.RevisionId,
            SecurityMasterRevisionStateDto.Submitted,
            SecurityMasterRevisionStateDto.Approved,
            request.Actor,
            ct: ct).ConfigureAwait(false);

        // The browser workflow exposes ONE approval step: approving the revision IS approving the
        // staged override carrying its value, so the override decision lands with the same gate
        // approval. This runs AFTER the (irreversible) gate approval and revision transition, so a
        // failure here never strands the flow: it is logged and PUBLISH — which fails closed and
        // is fully retryable before its own transition — converges the decision. The decision is
        // also deferred while OTHER revisions for this security are still staged: the override
        // decision is security-level and would co-approve their unreviewed values. Everything past
        // the transition runs on CancellationToken.None with a catch-all: the approval is already
        // durable, so a caller's canceled token must neither skip the best-effort convergence nor
        // surface the committed approval as a canceled request.
        try
        {
            await TryRecordOverrideApprovalDecisionAsync(
                request.SecurityId,
                request.RevisionId,
                decisionReviewer,
                request.Rationale,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Revision {RevisionId} for {SecurityId} approved, but recording the operator-override approval decision failed; publish records the decision (fail-closed) before the revision transitions to Published.",
                request.RevisionId, request.SecurityId);
        }

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master revision {RevisionId} for {SecurityId} approved through gate workflow {WorkflowId} by {Actor}",
            request.RevisionId, request.SecurityId, request.WorkflowId, request.Actor);

        return BuildLifecycleResult(request.SecurityId, request.RevisionId, result.NewVersion ?? currentVersion,
            SecurityMasterRevisionStateDto.Approved, request.Actor, request.Rationale, "security-master-field-edit-approved",
            "Revision approved through the operations-continuity gate.");
    }

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
        var fieldEditGate = FieldEditGates.GetOrAdd(securityId, static _ => new SemaphoreSlim(1, 1));
        await fieldEditGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RecordOverrideApprovalDecisionUnderGateAsync(
                securityId, revisionId, reviewer, rationale, ct).ConfigureAwait(false);
        }
        finally
        {
            fieldEditGate.Release();
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
        // operator-overrides PATCH endpoint can add free-form keys to it without creating a
        // revision. A key no revision governs carries a value no reviewer's workflow ever saw —
        // recording Approved would silently approve it on the back of an unrelated revision's
        // gate approval. Defer until every overlay key has revision evidence: the ungoverned
        // value must be withdrawn (or re-staged through the governed field-edit route) before any
        // security-level decision can land. Only the decision's OWN revision being WHOLE-RECORD
        // (no field path) exempts the scan: that revision's reviewer reviewed the record and its
        // overlay as one unit — the legacy pre-field-edit posture. A historical whole-record
        // revision exempts nothing: its reviewer never saw keys patched in after it was decided,
        // and one old Published whole-record row would otherwise waive the scan forever.
        var decidedRevision = revisions.FirstOrDefault(revision => revision.RevisionId == revisionId);
        var wholeRecordRevisionGoverns = decidedRevision is not null
            && string.IsNullOrWhiteSpace(decidedRevision.FieldPath);
        if (!wholeRecordRevisionGoverns)
        {
            var ungovernedKeys = stagedOverride.Values.Keys
                .Where(key => !revisions.Any(revision =>
                    string.Equals(revision.FieldPath, key, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (ungovernedKeys.Length > 0)
            {
                _logger.LogWarning(
                    "Operator-override approval for {SecurityId} deferred: overlay key(s) {UngovernedKeys} have no governing revision (staged through the generic overrides route); withdraw them or re-stage them through the governed field-edit route.",
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
        var fieldEditGate = FieldEditGates.GetOrAdd(securityId, static _ => new SemaphoreSlim(1, 1));
        await fieldEditGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _overrides.PatchAsync(securityId, request, updatedBy, ct).ConfigureAwait(false);
        }
        finally
        {
            fieldEditGate.Release();
        }
    }

    public async Task<SecurityMasterPublishResultDto> PublishRevisionAsync(
        PublishSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A revision may only be published if it was actually approved through the governed gate and
        // belongs to this security. This blocks an arbitrary revision id from triggering downstream
        // publish side effects.
        var revision = await EnsureRevisionInStateAsync(
            request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Approved, ct).ConfigureAwait(false);

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);

        // Scope the downstream-impact computation to the fund captured on the draft revision (the fund
        // the edit was made under, if any) so affected ledger books and report-pack restatement
        // candidates can be resolved. Publish deliberately does NOT accept a caller-supplied fund scope:
        // a publisher must not be able to resolve impact for a fund the operator did not edit under,
        // which would disclose another scope's affected books/candidates. A null/blank scope yields an
        // unscoped impact and the period-aware path reports no restatement — publish-time re-scoping and
        // cross-fund/multi-fund activation are a later slice gated on tenant-scoped holdings discovery.
        var fundScope = string.IsNullOrWhiteSpace(revision.FundProfileId) ? null : revision.FundProfileId.Trim();

        var snapshot = await _queryService
            .GetTrustSnapshotAsync(request.SecurityId, fundProfileId: fundScope, ct)
            .ConfigureAwait(false);

        var downstreamImpact = snapshot?.DownstreamImpact ?? EmptyDownstreamImpact();

        // Resolve the durable accounting books the edit could have touched so the period-aware resolver
        // can route each by its accounting-period lock status. Without this feed the resolver sees an
        // empty affected set and short-circuits to no restatement.
        var affectedLedgerBookIds = await _affectedLedgerBookResolver
            .ResolveAsync(downstreamImpact, ct)
            .ConfigureAwait(false);

        // Carry the edit's effective date and changed field from the durable revision so period-aware
        // / restatement handlers can scope impact analysis to the actual (possibly back-dated) edit
        // rather than to publish time.
        var evt = new SecurityMasterRevisionPublishedEvent(
            SecurityId: request.SecurityId,
            RevisionId: request.RevisionId,
            Version: currentVersion,
            EffectiveFrom: revision.FieldEffectiveFrom ?? DateTimeOffset.UtcNow,
            ChangedFields: revision.FieldPath is { } fieldPath ? [fieldPath] : [],
            DownstreamImpact: downstreamImpact,
            AffectedLedgerBookIds: affectedLedgerBookIds,
            Actor: request.Actor,
            CorrelationId: request.CorrelationId);

        var invalidated = new List<string>();
        var failedHandlers = new List<string>();
        foreach (var handler in _handlers)
        {
            var handlerName = handler.GetType().Name;
            try
            {
                await handler.HandleAsync(evt, ct).ConfigureAwait(false);
                invalidated.Add(handlerName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedHandlers.Add(handlerName);
                _logger.LogError(
                    ex,
                    "Revision-published handler {Handler} failed for {SecurityId} revision {RevisionId}",
                    handlerName, request.SecurityId, request.RevisionId);
            }
        }

        // If any handler failed, a required side effect (projection rebuild, coverage invalidation,
        // restatement proposal) did not run. Leave the revision Approved and surface the failure so
        // the caller can retry; handlers are idempotent, so a retry re-runs the full ordered fan-out.
        if (failedHandlers.Count > 0)
        {
            throw new SecurityMasterPublishFailedException(request.SecurityId, request.RevisionId, failedHandlers);
        }

        // Converge the operator-override approval decision BEFORE the Published transition: the
        // approve seam records it best-effort (a failure there logs and defers here), and a
        // failure HERE fails closed while the revision is still Approved — the publish retry
        // re-runs everything, so a published edit can never leave its override stuck Pending
        // behind SM_OVERRIDE_APPROVAL_REQUIRED.
        // The recorded reviewer resolves from the BOUND APPROVED WORKFLOW, not from the request:
        // the publish body's ApproverActor is caller-supplied text, so trusting it would let a
        // publisher persist any name as the reviewer of governed approval evidence. The gate
        // already recorded who actually decided; only when the revision carries no workflow (or
        // the workflow records no reviewer) does the authenticated publisher stand in.
        var decisionReviewer = request.Actor;
        if (revision.WorkflowId is Guid decisionWorkflowId)
        {
            var decisionWorkflow = await _approvalWorkflow.GetAsync(decisionWorkflowId, ct).ConfigureAwait(false);
            var workflowReviewer = decisionWorkflow?.Approvals
                .Select(static approval => approval.Reviewer)
                .LastOrDefault(static reviewer => !string.IsNullOrWhiteSpace(reviewer));
            if (!string.IsNullOrWhiteSpace(workflowReviewer))
            {
                decisionReviewer = workflowReviewer!;
            }
        }

        var decisionOutcome = await TryRecordOverrideApprovalDecisionAsync(
            request.SecurityId,
            request.RevisionId,
            decisionReviewer,
            rationale: null,
            ct).ConfigureAwait(false);
        if (decisionOutcome == OverrideDecisionOutcome.Deferred)
        {
            // A DEFERRED decision is a retryable publish failure, not a pass-through: other
            // revisions for this security are still staged, so the overlay must stay Pending —
            // publishing now would mark the revision Published while SM_OVERRIDE_APPROVAL_REQUIRED
            // still blocks its economics. The revision stays Approved; handlers are idempotent, so
            // the retry (after the other staged revisions are decided) re-runs the full fan-out.
            throw new InvalidOperationException(
                $"Revision '{request.RevisionId:D}' for security '{request.SecurityId:D}' cannot be published while " +
                "other revisions for the security are still staged (or overlay keys lack a governing revision): the " +
                "security-level override decision would co-approve unreviewed values. Approve or discard the other " +
                "staged revisions — or withdraw ungoverned overlay keys — and retry; the revision remains Approved.");
        }

        // Period-aware propagation resolves BEFORE the revision is marked Published: the
        // closed-period restatement decision is a REQUIRED publish side effect, and resolving it
        // after the transition would make a transient period-lock or report-index outage
        // unretryable — the Approved-state precondition rejects the retry, permanently skipping
        // the restatement decision for a back-dated edit. Resolving here keeps the revision
        // Approved on failure so the caller can retry the whole idempotent publish.
        var restatement = await _restatementResolver.ResolveAsync(evt, ct).ConfigureAwait(false);

        await _revisions.TransitionAsync(
            request.RevisionId,
            SecurityMasterRevisionStateDto.Approved,
            SecurityMasterRevisionStateDto.Published,
            request.Actor,
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Published Security Master revision {RevisionId} for {SecurityId} by {Actor} ({HandlerCount} handlers, restatementRequired={RestatementRequired})",
            request.RevisionId, request.SecurityId, request.Actor, _handlers.Count, restatement.RestatementRequired);

        return new SecurityMasterPublishResultDto(
            request.SecurityId,
            request.RevisionId,
            currentVersion,
            RestatementRequired: restatement.RestatementRequired,
            RestatementCandidates: restatement.Candidates,
            InvalidatedProjections: invalidated);
    }

    /// <summary>
    /// Validates an asset-terms edit against the declared schema and returns the path the edit must
    /// be persisted under: the schema-canonical spelling for <c>assetSpecificTerms.*</c> paths (so
    /// an alias like <c>dayCount</c> and its declared key <c>dayCountConvention</c> share one
    /// override key, revision lineage, and provenance row), or the caller's path unchanged for the
    /// free annotation surface. When the asset class cannot be resolved (passport read model
    /// degraded or unavailable), the edit fails CLOSED — value edits because the namespace only
    /// accepts validated writes, and clears because without the schema the path cannot be
    /// canonicalized, so an alias-spelled clear would remove the wrong key and leave the asserted
    /// value active.
    /// </summary>
    private async Task<string> EnsureFieldEditIsSchemaValidAsync(UpdateSecurityFieldRequest request, CancellationToken ct)
    {
        if (!SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(request.FieldPath))
        {
            return request.FieldPath;
        }

        // FieldPath is operator-supplied free text; strip control characters before logging so a
        // crafted path cannot forge log entries.
        var safeFieldPathForLog = SanitizeForLog(request.FieldPath);
        string? assetClass = null;
        try
        {
            var passport = await _queryService
                .GetInstrumentPassportAsync(request.SecurityId, request.FundProfileId, ct)
                .ConfigureAwait(false);
            assetClass = passport?.EconomicDefinition?.AssetClass;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Resolving the asset class for {SecurityId} failed while validating field edit {FieldPath}.",
                request.SecurityId, safeFieldPathForLog);
        }

        if (string.IsNullOrWhiteSpace(assetClass))
        {
            // The assetSpecificTerms namespace is RESERVED for schema-validated writes (the generic
            // override route hard-rejects it on that basis), so an unresolvable asset class fails
            // the edit closed — otherwise a read-model outage is exactly the window in which
            // undeclared paths and malformed values slip through unvalidated. Clears fail closed
            // too: without the schema the path cannot be canonicalized, so clearing an alias
            // spelling (e.g. dayCount) would remove a nonexistent key while the canonical override
            // (dayCountConvention) and its provenance stay active — a clear that silently no-ops.
            throw new InvalidOperationException(
                $"The asset class for security '{request.SecurityId:D}' could not be resolved, so the " +
                "assetSpecificTerms edit cannot be schema-validated or path-canonicalized. Retry once the " +
                "passport read model is available; the namespace only accepts validated writes.");
        }

        if (!SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, request.FieldPath, request.NewValue, out var canonicalFieldPath, out var error))
        {
            throw new ArgumentException(error, nameof(request));
        }

        await EnsurePrincipalScheduleFitsEffectiveTermsAsync(request, canonicalFieldPath, ct).ConfigureAwait(false);
        await EnsureFirstClassTermEditSatisfiesKindInvariantsAsync(request, canonicalFieldPath, ct).ConfigureAwait(false);

        return await EnsureProfileFieldEditMatchesPinnedProfileAsync(request, canonicalFieldPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A FIRST-CLASS record's asset terms are bound by its resolved kind's F# domain invariants,
    /// not only per-field type coercion: a StructuredCredit <c>currentFactor</c> must stay within
    /// [0, 1] even though "2" is a perfectly typed decimal, so an edit the equivalent canonical
    /// amendment rejects must not stage. Reconstructs the record's EFFECTIVE asset terms (the
    /// canonical document with staged overrides applied and the proposed value overlaid) and runs
    /// the resolved kind's invariants. profileFields paths are owned by the pinned-profile route,
    /// and CustomAsset/OtherSecurity records have no first-class kind to reconstruct.
    /// <para>Reconstruction FAILS CLOSED: a document that cannot round-trip through the strict
    /// kind mapping (e.g. a legacy row storing a Bond's maturity under the <c>maturityDate</c>
    /// alias) cannot have its invariants verified, so the edit is rejected until the record's
    /// terms are migrated to the canonical shape through a governed amendment — legacy shape is
    /// not permission to skip validation.</para>
    /// </summary>
    private async Task EnsureFirstClassTermEditSatisfiesKindInvariantsAsync(
        UpdateSecurityFieldRequest request, string canonicalFieldPath, CancellationToken ct)
    {
        if (_projectionStore is null
            || string.Equals(canonicalFieldPath, ProfileFieldsRootPath, StringComparison.OrdinalIgnoreCase)
            || canonicalFieldPath.StartsWith(ProfileFieldsNestedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SecurityProjectionRecord? projection;
        try
        {
            projection = await _projectionStore.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // FAIL CLOSED on a read FAILURE: the passport already resolved a first-class asset
            // class, so a type-correct but invariant-violating value (StructuredCredit
            // currentFactor = 2) would stage unvalidated for the whole outage if the transient
            // failure were swallowed. A null RESULT (projection read model has no row yet) still
            // skips below — there are no retained terms to reconstruct against.
            throw new InvalidOperationException(
                $"The retained terms for security '{request.SecurityId:D}' could not be loaded, so the edit cannot " +
                "be validated against the resolved asset class's domain invariants. Retry once the projection is " +
                "available; the namespace only accepts validated writes.", ex);
        }

        if (projection is null
            || string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(projection.AssetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase)
            || (projection.AssetSpecificTerms is { ValueKind: JsonValueKind.Object } profileProbe
                && profileProbe.TryGetProperty("customProfileId", out var profileId)
                && profileId.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(profileId.GetString())))
        {
            return;
        }

        var stagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
        Meridian.FSharp.Domain.SecurityKind kind;
        try
        {
            var envelope = System.Text.Json.Nodes.JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())?.AsObject();
            if (envelope is null)
            {
                return;
            }

            // Post-approval read layering: staged overrides first, the proposed edit last (it
            // supersedes any staged override of the same term). A CLEAR removes only the STAGED
            // override — already excluded from the loop below — so the canonical envelope value
            // stays: the overlay removal reveals it on the post-clear read, and deleting it here
            // would validate a state (term absent) the clear never produces, failing legitimate
            // clears of required terms such as a Bond's maturity.
            if (stagedOverrides is not null)
            {
                foreach (var (overridePath, overrideValue) in stagedOverrides)
                {
                    if (string.Equals(overridePath, canonicalFieldPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ApplyFirstClassTermOverlay(envelope, overridePath, overrideValue);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.NewValue))
            {
                ApplyFirstClassTermOverlay(envelope, canonicalFieldPath, request.NewValue!);
            }

            var effectiveTerms = JsonSerializer.SerializeToElement(envelope);
            kind = SecurityMasterMapping.ToRecord(projection with { AssetSpecificTerms = effectiveTerms }).Kind;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // FAIL CLOSED: a first-class record whose effective terms cannot be reconstructed
            // through the strict kind mapping cannot have its invariants verified, and a legacy
            // document shape is not permission to skip validation — an invariant-violating value
            // (StructuredCredit currentFactor = 2) would otherwise stage unvalidated on exactly
            // those records. The remedy is migrating the record's terms to the canonical shape
            // through a governed amendment, then retrying the edit.
            throw new ArgumentException(
                $"The effective asset terms for security '{request.SecurityId:D}' could not be reconstructed as the " +
                $"resolved asset class '{projection.AssetClass}' ({ex.Message}), so the edit cannot be validated " +
                "against the class's domain invariants. Migrate the record's retained terms to the canonical shape " +
                "through a governed amendment and retry; the namespace only accepts validated writes.",
                nameof(request));
        }

        var invariantErrors = Meridian.FSharp.SecurityMasterInterop.SecurityMasterCommandFacade.ValidateKindInvariants(kind);
        if (invariantErrors.Length > 0)
        {
            var summary = string.Join("; ", invariantErrors.Select(static e => $"[{e.Code}] {e.Message}"));
            throw new ArgumentException(
                $"The effective asset terms after this edit violate the resolved asset class " +
                $"'{projection.AssetClass}' domain invariants: {summary}",
                nameof(request));
        }
    }

    /// <summary>Overlays one staged/proposed TOP-LEVEL asset-term value onto the envelope; nested paths are not modeled here.</summary>
    private static void ApplyFirstClassTermOverlay(
        System.Text.Json.Nodes.JsonObject envelope, string overridePath, string overrideValue)
    {
        if (!overridePath.StartsWith(SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(overrideValue))
        {
            return;
        }

        var key = overridePath[SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix.Length..];
        if (key.Contains('.', StringComparison.Ordinal))
        {
            return;
        }

        RemoveEnvelopeKeyVariants(envelope, key);
        var trimmed = overrideValue.Trim();
        envelope[key] = TryParseJsonNode(trimmed) ?? System.Text.Json.Nodes.JsonValue.Create(overrideValue);
    }

    private static void RemoveEnvelopeKeyVariants(System.Text.Json.Nodes.JsonObject envelope, string key)
    {
        foreach (var variantKey in envelope
            .Where(property => string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(static property => property.Key)
            .ToArray())
        {
            envelope.Remove(variantKey);
        }
    }

    private const string ProfileFieldsRootPath = SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix + "profileFields";
    private const string ProfileFieldsNestedPrefix = ProfileFieldsRootPath + ".";
    private const string PrincipalSchedulePath = SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix + "principalSchedule";
    private const string ParTermPath = SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix + "par";
    private const string IssueDateTermPath = SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix + "issueDate";
    private const string MaturityTermPath = SecurityAssetTermsFieldEditValidator.AssetSpecificTermsPrefix + "maturity";

    /// <summary>
    /// A whole principalSchedule replacement passes the static schema validator on ROW-LOCAL shape
    /// alone (parseable payment dates, positive amounts), but the canonical Bond contract also
    /// binds the schedule to the record it overlays: instalments must fall inside the issue/maturity
    /// window and must not sum past the principal face. The window and par are resolved from the
    /// EFFECTIVE overlay — a staged override of par/issueDate/maturity is what the record reads
    /// after approval, so validating against superseded canonical values would let a staged
    /// <c>par=50</c> coexist with a schedule totaling 80. The check is RECIPROCAL: editing par,
    /// issueDate, or maturity revalidates the effective principal schedule against the proposed
    /// value, so neither staging order slips an inconsistent overlay through. Fails CLOSED when
    /// the retained terms cannot be loaded, matching the reserved namespace's
    /// validated-writes-only contract.
    /// </summary>
    private async Task EnsurePrincipalScheduleFitsEffectiveTermsAsync(
        UpdateSecurityFieldRequest request, string canonicalFieldPath, CancellationToken ct)
    {
        var editsSchedule = string.Equals(canonicalFieldPath, PrincipalSchedulePath, StringComparison.OrdinalIgnoreCase);
        var editsPar = string.Equals(canonicalFieldPath, ParTermPath, StringComparison.OrdinalIgnoreCase);
        var editsIssueDate = string.Equals(canonicalFieldPath, IssueDateTermPath, StringComparison.OrdinalIgnoreCase);
        var editsMaturity = string.Equals(canonicalFieldPath, MaturityTermPath, StringComparison.OrdinalIgnoreCase);
        if (_projectionStore is null
            || (!editsSchedule && !editsPar && !editsIssueDate && !editsMaturity))
        {
            return;
        }

        // A CLEAR of a bound term participates too: removing a staged par/issueDate/maturity (or
        // the schedule itself) reverts that term to the remaining staged/canonical layering, and
        // the effective schedule must still fit the reverted value — otherwise staging par=100,
        // staging a schedule totaling 80, then clearing the par override leaves an approvable
        // overlay whose schedule exceeds the canonical par it now reads against. For a clear the
        // edited term resolves by SKIPPING both the proposed value and its staged override.
        var isClear = string.IsNullOrWhiteSpace(request.NewValue);

        SecurityProjectionRecord? projection = null;
        try
        {
            projection = await _projectionStore.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Loading the projection for {SecurityId} failed while validating field edit {FieldPath} against the principal schedule.",
                request.SecurityId, SanitizeForLog(canonicalFieldPath));
        }

        if (projection is null)
        {
            throw new InvalidOperationException(
                $"The retained terms for security '{request.SecurityId:D}' could not be loaded, so the edit cannot be " +
                "validated against the record's principal schedule, issue/maturity window, and principal face. Retry " +
                "once the projection is available; the namespace only accepts validated writes.");
        }

        var terms = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(projection));
        var stagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);

        // The EFFECTIVE schedule: the proposed replacement when this edit IS one, else a staged
        // schedule override, else the canonical schedule. Same layering for each window term —
        // the proposed value wins for the term being edited.
        var rows = new List<(DateOnly PaymentDate, decimal Amount)>();
        var scheduleJson = editsSchedule
            ? (isClear ? null : request.NewValue)
            : TryGetStagedOverrideValue(stagedOverrides, PrincipalSchedulePath);
        if (scheduleJson is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(scheduleJson);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in document.RootElement.EnumerateArray())
                    {
                        if (TryReadScheduleRowDate(row, out var paymentDate) && TryReadScheduleRowAmount(row, out var amount))
                        {
                            rows.Add((paymentDate, amount));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // The proposed replacement was already shape-validated; a malformed STAGED
                // override cannot supply the effective schedule and is left to its own guards.
            }
        }
        else if (terms.HasPrincipalSchedule)
        {
            foreach (var entry in terms.PrincipalSchedule!)
            {
                rows.Add((entry.PaymentDate, entry.Amount));
            }
        }

        if (rows.Count == 0)
        {
            // No effective contractual schedule — there is nothing for the window or par to bind.
            return;
        }

        var effectivePar = editsPar
            ? (isClear ? terms.PrincipalFace : ParseInvariantDecimal(request.NewValue))
            : ParseInvariantDecimal(TryGetStagedOverrideValue(stagedOverrides, ParTermPath)) ?? terms.PrincipalFace;
        var effectiveIssueDate = editsIssueDate
            ? (isClear ? terms.IssueDate : ParseInvariantDate(request.NewValue))
            : ParseInvariantDate(TryGetStagedOverrideValue(stagedOverrides, IssueDateTermPath)) ?? terms.IssueDate;
        var effectiveMaturity = editsMaturity
            ? (isClear ? terms.MaturityDate : ParseInvariantDate(request.NewValue))
            : ParseInvariantDate(TryGetStagedOverrideValue(stagedOverrides, MaturityTermPath)) ?? terms.MaturityDate;

        var subject = editsSchedule && !isClear
            ? "principalSchedule replacement"
            : isClear
                ? "the effective principal schedule after clearing this override"
                : "the effective principal schedule after this edit";
        var scheduledTotal = 0m;
        foreach (var (paymentDate, amount) in rows)
        {
            scheduledTotal += amount;
            if (effectiveIssueDate is DateOnly issueDate && paymentDate < issueDate)
            {
                throw new ArgumentException(
                    $"{subject} pays principal on {paymentDate:yyyy-MM-dd}, before the record's effective " +
                    $"retained issue date {issueDate:yyyy-MM-dd}.",
                    nameof(request));
            }

            if (effectiveMaturity is DateOnly maturityDate && paymentDate > maturityDate)
            {
                throw new ArgumentException(
                    $"{subject} pays principal on {paymentDate:yyyy-MM-dd}, after the record's effective " +
                    $"retained maturity date {maturityDate:yyyy-MM-dd}.",
                    nameof(request));
            }
        }

        if (effectivePar is > 0m && scheduledTotal > effectivePar.Value)
        {
            throw new ArgumentException(
                $"{subject} instalments total {scheduledTotal} and exceed the record's effective retained " +
                $"principal face {effectivePar.Value}.",
                nameof(request));
        }
    }

    /// <summary>The staged override value at <paramref name="path"/>, matched case-insensitively; null when absent or blank.</summary>
    private static string? TryGetStagedOverrideValue(IReadOnlyDictionary<string, string>? stagedOverrides, string path)
    {
        if (stagedOverrides is null)
        {
            return null;
        }

        foreach (var (overridePath, overrideValue) in stagedOverrides)
        {
            if (string.Equals(overridePath, path, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(overrideValue))
            {
                return overrideValue;
            }
        }

        return null;
    }

    private static decimal? ParseInvariantDecimal(string? value)
        => value is not null
           && decimal.TryParse(value.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateOnly? ParseInvariantDate(string? value)
        => value is not null
           && DateOnly.TryParse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool TryReadScheduleRowDate(JsonElement row, out DateOnly value)
    {
        value = default;
        if (row.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, "paymentDate", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(property.Value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadScheduleRowAmount(JsonElement row, out decimal value)
    {
        value = default;
        if (row.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, "amount", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDecimal(out value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The static asset-terms schema cannot type dynamic profile-governed fields — their contract
    /// is the record's PINNED profile definition. Without this check an edit like
    /// <c>profileFields.currentFactor = "garbage"</c> stages an invalid overlay (with a draft
    /// revision and provenance row) that only a later validation read exposes. Resolves the pinned
    /// profile from the persisted envelope and validates the edited field's declared type, range,
    /// and enum constraints. Edits — value-asserting and clears alike — fail CLOSED when the
    /// pinned profile cannot be resolved (projection lag, missing envelope, catalog mismatch,
    /// unwired stores): the reserved namespace only accepts validated writes, and an
    /// uncanonicalized clear would remove the wrong casing's key while the stored override stays
    /// active. Undeclared keys still pass through once the profile resolves, since the profile
    /// owns only its declared fields.
    /// <para>Returns the canonical path: a declared profile field's key is normalized to the
    /// pinned definition's spelling, so <c>profileFields.currentFactor</c> and
    /// <c>profileFields.CurrentFactor</c> address ONE override key, revision lineage, and
    /// provenance row instead of forking per casing.</para>
    /// </summary>
    private async Task<string> EnsureProfileFieldEditMatchesPinnedProfileAsync(
        UpdateSecurityFieldRequest request, string canonicalFieldPath, CancellationToken ct)
    {
        var isWholeObject = string.Equals(canonicalFieldPath, ProfileFieldsRootPath, StringComparison.Ordinal);
        var isNestedField = canonicalFieldPath.StartsWith(ProfileFieldsNestedPrefix, StringComparison.Ordinal);
        // Clears skip the asserted-VALUE type check (there is no asserted value) but still need
        // key canonicalization — clearing profileFields.CurrentFactor must remove the canonical
        // currentFactor override, not a casing-variant key — AND post-clear revalidation: the
        // overlay that remains after removing the override must still satisfy the profile's date
        // ordering and the resolved kind's invariants. That applies to the WHOLE-OBJECT clear too,
        // so it no longer short-circuits here: removing a staged replacement reveals the canonical
        // profileFields object beneath the RETAINED scalar overrides, and that combination can
        // violate rules the replacement satisfied (a scalar start override that ordered correctly
        // against the replacement's end date may read start-after-end against the canonical one).
        var isClear = string.IsNullOrWhiteSpace(request.NewValue);
        if (!isWholeObject && !isNestedField)
        {
            return canonicalFieldPath;
        }

        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto? profile = null;
        JsonElement? currentProfileFields = null;
        SecurityProjectionRecord? currentProjection = null;
        if (_projectionStore is not null && _assetProfileCatalog is not null)
        {
            try
            {
                var projection = await _projectionStore.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
                var terms = projection?.AssetSpecificTerms;
                if (terms is { ValueKind: JsonValueKind.Object }
                    && terms.Value.TryGetProperty("customProfileId", out var profileId)
                    && profileId.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(profileId.GetString())
                    && terms.Value.TryGetProperty("profileVersion", out var versionElement)
                    && versionElement.TryGetInt32(out var profileVersion)
                    && _assetProfileCatalog.TryGetProfile(profileId.GetString()!, profileVersion, out var resolved))
                {
                    profile = resolved;
                    currentProjection = projection;
                    if (terms.Value.TryGetProperty("profileFields", out var persistedFields)
                        && persistedFields.ValueKind == JsonValueKind.Object)
                    {
                        currentProfileFields = persistedFields.Clone();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Resolving the pinned profile for {SecurityId} failed while validating profile-field edit {FieldPath}.",
                    request.SecurityId, SanitizeForLog(canonicalFieldPath));
            }
        }

        if (profile is null)
        {
            // Fail closed for values AND clears: profileFields values are governed by the pinned
            // profile, so a value that cannot be validated against it must not stage — and without
            // the pinned definition a clear's path cannot be canonicalized either, so clearing
            // profileFields.CurrentFactor would remove that noncanonical key while the stored
            // currentFactor override and its provenance stay active, a clear that reports success
            // and changes nothing.
            throw new InvalidOperationException(
                $"The pinned asset profile for security '{request.SecurityId:D}' could not be resolved, so the " +
                "profileFields edit cannot be validated or path-canonicalized. Ensure the record carries a profile " +
                "envelope pinned to a registered profile version and retry; the namespace only accepts validated writes.");
        }

        if (isWholeObject && isClear)
        {
            // Clearing the staged whole-object replacement is an edit to the effective overlay:
            // the canonical profileFields object is revealed beneath every RETAINED scalar
            // override, and that post-clear combination must still satisfy the profile's date
            // ordering and the resolved kind's invariants — otherwise stage a valid replacement,
            // stage a scalar date override that orders correctly against it, then clear the
            // replacement: each edit validates individually, but the clear leaves an approvable
            // overlay violating the pinned profile against the canonical object.
            var replacementClearOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
            IReadOnlyDictionary<string, string>? postClearOverrides = replacementClearOverrides;
            if (replacementClearOverrides is not null)
            {
                var filtered = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (path, value) in replacementClearOverrides)
                {
                    if (!string.Equals(path, ProfileFieldsRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        filtered[path] = value;
                    }
                }

                postClearOverrides = filtered;
            }

            foreach (var rule in profile.DateOrderRules)
            {
                if (TryResolveEffectiveProfileDate(rule.StartFieldKey, currentProfileFields, postClearOverrides, out var start)
                    && TryResolveEffectiveProfileDate(rule.EndFieldKey, currentProfileFields, postClearOverrides, out var end)
                    && start > end)
                {
                    throw new ArgumentException(
                        $"Clearing the profileFields replacement leaves the effective overlay violating the " +
                        $"pinned profile's date ordering [{rule.Code}]: {rule.Message}",
                        nameof(request));
                }
            }

            EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
                currentProjection!, profile, postClearOverrides,
                editedFieldKey: null, proposedValue: null, proposedReplacementRoot: null);

            return canonicalFieldPath;
        }

        if (isWholeObject)
        {
            // The validator already guaranteed the replacement parses as a JSON object. A whole
            // replacement REPLACES the object, so it must satisfy the profile's complete field
            // rules: a missing or blank REQUIRED field is as invalid as a mistyped one — otherwise
            // "{}" stages an overlay that strips every required field and proceeds to approval.
            using var document = JsonDocument.Parse(request.NewValue!);
            foreach (var field in profile.Fields)
            {
                // Declared keys resolve CASE-INSENSITIVELY, matching the scalar edit route and
                // every downstream term reader — an exact-case lookup would let "Maturity":
                // "not-a-date" bypass the declared maturity field's validation entirely. Two
                // casings of the same declared key are ambiguous (readers would pick one
                // arbitrarily) and are rejected outright.
                JsonElement? match = null;
                var casingMatches = 0;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, field.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        match = property.Value;
                        casingMatches++;
                    }
                }

                if (casingMatches > 1)
                {
                    throw new ArgumentException(
                        $"profileFields replacement contains multiple casings of declared profile field " +
                        $"'{field.Key}'; collapse them to the pinned definition's spelling.",
                        nameof(request));
                }

                if (match is null)
                {
                    if (field.IsRequired)
                    {
                        throw new ArgumentException(
                            $"profileFields replacement omits required profile field '{field.Key}' " +
                            $"declared by the pinned profile.",
                            nameof(request));
                    }

                    continue;
                }

                if (!ProfileFieldElementIsValid(field, match.Value, out var objectError))
                {
                    throw new ArgumentException(objectError, nameof(request));
                }
            }

            // The complete profile rule set includes cross-field date ordering, not just per-field
            // shape: individually valid start/end dates in reverse order violate the pinned
            // profile just as a mistyped value does. Each rule date resolves from the EFFECTIVE
            // overlay, not the replacement alone — a staged PER-FIELD override outranks a
            // whole-object replacement when the record is read after approval, so checking only
            // the replacement's own pair would stage an object that reads back as start-after-end
            // once the retained scalar override is applied on top of it.
            var wholeObjectStagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
            foreach (var rule in profile.DateOrderRules)
            {
                if (TryResolveEffectiveReplacementDate(rule.StartFieldKey, document.RootElement, wholeObjectStagedOverrides, out var start)
                    && TryResolveEffectiveReplacementDate(rule.EndFieldKey, document.RootElement, wholeObjectStagedOverrides, out var end)
                    && start > end)
                {
                    throw new ArgumentException(
                        $"profileFields replacement violates the pinned profile's date ordering " +
                        $"against the effective overlay [{rule.Code}]: {rule.Message}",
                        nameof(request));
                }
            }

            EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
                currentProjection!, profile, wholeObjectStagedOverrides,
                editedFieldKey: null, proposedValue: null, proposedReplacementRoot: document.RootElement);

            return canonicalFieldPath;
        }

        var nestedRemainder = canonicalFieldPath[ProfileFieldsNestedPrefix.Length..];
        if (nestedRemainder.Contains('.', StringComparison.Ordinal))
        {
            // Profile field types describe SCALAR values; a deeper path beneath a declared field
            // (profileFields.currentFactor.unit) would bypass its type/range validation and stage
            // an undeclared override. Undeclared roots stay dynamic pass-through; a CLEAR of such
            // a subpath removes junk rather than asserting a value, so it passes with the root
            // segment canonicalized.
            var separatorIndex = nestedRemainder.IndexOf('.', StringComparison.Ordinal);
            var rootSegment = nestedRemainder[..separatorIndex];
            var declaredRoot = profile.Fields.FirstOrDefault(
                field => string.Equals(field.Key, rootSegment, StringComparison.OrdinalIgnoreCase));
            if (declaredRoot is null)
            {
                // A root the profile does not declare may still be OWNED by the resolved kind on a
                // record resolved past CustomAsset (factorScheduleEntries on StructuredCredit): a
                // nested VALUE edit beneath it (…factorScheduleEntries.0.factor = 2) cannot be
                // validated against the kind's schedule-wide invariants, while the equivalent
                // whole-value replacement runs them — so nested value edits are rejected there,
                // mirroring the schema validator's replace-the-whole-array rule for declared
                // schedules. Clears still pass (they remove overlay junk), and genuine CustomAsset
                // records keep dynamic pass-through.
                if (!isClear
                    && !string.Equals(currentProjection!.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(currentProjection.AssetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Path '{canonicalFieldPath}' edits beneath '{rootSegment}', which the pinned profile does not " +
                        $"declare but the resolved asset class '{currentProjection.AssetClass}' may govern; a nested " +
                        "value cannot be validated against the resolved kind's structural invariants — replace the " +
                        $"whole '{ProfileFieldsNestedPrefix}{rootSegment}' value instead.",
                        nameof(request));
                }

                return canonicalFieldPath;
            }

            if (!isClear)
            {
                throw new ArgumentException(
                    $"Profile field '{declaredRoot.Key}' is declared as a scalar {declaredRoot.FieldType} by the " +
                    "pinned profile and has no structured children; edit the field itself.",
                    nameof(request));
            }

            return ProfileFieldsNestedPrefix + declaredRoot.Key + nestedRemainder[separatorIndex..];
        }

        var declared = profile.Fields.FirstOrDefault(
            field => string.Equals(field.Key, nestedRemainder, StringComparison.OrdinalIgnoreCase));
        if (declared is null)
        {
            // A key the pinned profile does not declare is NOT unrestricted on a record whose
            // asset class resolved past CustomAsset: the resolved kind may read it — the seeded
            // structured-credit-io-po profile does not declare factorScheduleEntries while the
            // StructuredCredit kind enforces factors within [0, 1], unique dates, and
            // non-increasing order over exactly that key — so the effective overlay must still
            // satisfy the resolved class's domain invariants before the edit stages. Undeclared
            // keys on CustomAsset records stay dynamic pass-through (the check no-ops there), and
            // clears revert toward the already-validated canonical value.
            // The check runs for CLEARS too: removing one staged override reveals the base-layer
            // value while every OTHER staged override stays applied, and that combination must
            // still satisfy the resolved kind's invariants before it becomes the approvable
            // overlay (proposedValue: null models exactly the post-clear layering).
            var undeclaredStagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
            EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
                currentProjection!, profile, undeclaredStagedOverrides,
                editedFieldKey: nestedRemainder,
                proposedValue: isClear ? null : request.NewValue,
                proposedReplacementRoot: null);

            return canonicalFieldPath;
        }

        if (!isClear && !ProfileFieldStringIsValid(declared, request.NewValue!, out var fieldError))
        {
            throw new ArgumentException(fieldError, nameof(request));
        }

        // A scalar date edit participates in the profile's cross-field date-order rules exactly as
        // a whole-object replacement does: moving startDate after the RETAINED endDate violates
        // the pinned profile even though the value is individually valid, and staging it would put
        // a draft and provenance row behind a contract the equivalent object replacement rejects.
        if (!isClear)
        {
            // The counterpart values must come from the EFFECTIVE overlay, not just the canonical
            // projection: an already staged override is what the record will read after approval,
            // so validating against superseded canonical values would let two individually
            // plausible edits stage an overlay that violates the profile or the resolved kind.
            var stagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
            if (declared.FieldType == SecurityAssetProfileFieldTypeDto.Date)
            {
                EnsureScalarDateEditSatisfiesDateOrderRules(
                    profile, declared.Key, request.NewValue!, currentProfileFields, stagedOverrides);
            }

            EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
                currentProjection!, profile, stagedOverrides,
                editedFieldKey: declared.Key, proposedValue: request.NewValue, proposedReplacementRoot: null);
        }
        else
        {
            // A CLEAR is an edit to the effective overlay too: removing this field's staged
            // override reveals its base-layer value (a staged whole-object replacement, else the
            // canonical projection) while every OTHER staged override stays applied — and that
            // post-clear combination must still satisfy the profile's date ordering and the
            // resolved kind's invariants. Otherwise stage an end date, stage a start date after
            // the canonical end, then clear the end override: each asserted edit validates, but
            // the clear leaves an approvable start-after-end overlay.
            var stagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
            IReadOnlyDictionary<string, string>? postClearOverrides = stagedOverrides;
            if (stagedOverrides is not null)
            {
                var clearedPath = ProfileFieldsNestedPrefix + declared.Key;
                var filtered = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (path, value) in stagedOverrides)
                {
                    if (!string.Equals(path, clearedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        filtered[path] = value;
                    }
                }

                postClearOverrides = filtered;
            }

            if (declared.FieldType == SecurityAssetProfileFieldTypeDto.Date)
            {
                foreach (var rule in profile.DateOrderRules)
                {
                    var involvesClearedField =
                        string.Equals(rule.StartFieldKey, declared.Key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(rule.EndFieldKey, declared.Key, StringComparison.OrdinalIgnoreCase);
                    if (involvesClearedField
                        && TryResolveEffectiveProfileDate(rule.StartFieldKey, currentProfileFields, postClearOverrides, out var start)
                        && TryResolveEffectiveProfileDate(rule.EndFieldKey, currentProfileFields, postClearOverrides, out var end)
                        && start > end)
                    {
                        throw new ArgumentException(
                            $"Clearing profile field '{declared.Key}' leaves the effective overlay violating the " +
                            $"pinned profile's date ordering [{rule.Code}]: {rule.Message}",
                            nameof(request));
                    }
                }
            }

            EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
                currentProjection!, profile, postClearOverrides,
                editedFieldKey: declared.Key, proposedValue: null, proposedReplacementRoot: null);
        }

        // Persist under the pinned definition's key spelling — the case-insensitive lookup above
        // must not let casing variants fork the same profile field into separate overrides.
        return ProfileFieldsNestedPrefix + declared.Key;
    }

    /// <summary>
    /// The current staged operator override values. FAIL-CLOSED: every caller validates the
    /// proposed edit against the EFFECTIVE overlay, so treating an unreadable overlay as absent
    /// would validate against canonical values only — an already staged counterpart (an end date,
    /// a Bond term, a resolved-kind constraint) could be silently contradicted and the stored
    /// overlay left violating the pinned profile. A <c>null</c> return means overrides genuinely
    /// do not exist; a read failure rejects the edit instead.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> TryGetStagedOverrideValuesAsync(
        Guid securityId, CancellationToken ct)
    {
        try
        {
            var overrides = await _overrides.GetAsync(securityId, ct).ConfigureAwait(false);
            return overrides?.Values;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The staged operator overlay for security '{securityId:D}' could not be loaded, so the edit cannot be " +
                "validated against the effective overlay values. Retry the edit once the overlay store is reachable.",
                ex);
        }
    }

    private static void EnsureScalarDateEditSatisfiesDateOrderRules(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        string editedKey,
        string newValue,
        JsonElement? currentProfileFields,
        IReadOnlyDictionary<string, string>? stagedOverrides)
    {
        if (!DateOnly.TryParse(newValue.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var proposed))
        {
            return;
        }

        foreach (var rule in profile.DateOrderRules)
        {
            var editsStart = string.Equals(rule.StartFieldKey, editedKey, StringComparison.OrdinalIgnoreCase);
            var editsEnd = string.Equals(rule.EndFieldKey, editedKey, StringComparison.OrdinalIgnoreCase);
            if (!editsStart && !editsEnd)
            {
                continue;
            }

            var counterpartKey = editsStart ? rule.EndFieldKey : rule.StartFieldKey;
            if (!TryResolveEffectiveProfileDate(counterpartKey, currentProfileFields, stagedOverrides, out var counterpart))
            {
                // No effective counterpart date — there is nothing for the proposed value to
                // violate; the rule binds once both dates exist.
                continue;
            }

            var start = editsStart ? proposed : counterpart;
            var end = editsStart ? counterpart : proposed;
            if (start > end)
            {
                throw new ArgumentException(
                    $"Value '{newValue}' violates the pinned profile's date ordering against the effective " +
                    $"value of field '{counterpartKey}' [{rule.Code}]: {rule.Message}");
            }
        }
    }

    private async Task<long> GetCurrentVersionAsync(Guid securityId, CancellationToken ct)
    {
        var events = await _eventStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        return events.Count == 0 ? 0L : events.Max(static e => e.StreamVersion);
    }

    /// <summary>
    /// Rejects a chosen winner that is not one of the conflict's two competing sources. The authority
    /// policy only ever picks between <c>CurrentWinningSource</c> and <c>ChallengerSource</c>, so a
    /// value outside that pair (a typo or arbitrary source) must never be allowed to close the conflict.
    /// </summary>
    internal static void EnsureChosenWinnerIsCandidate(
        SecurityMasterConflictAssessmentDto assessment, string? chosenWinnerSource)
    {
        var chosen = chosenWinnerSource?.Trim() ?? string.Empty;
        var isCandidate =
            string.Equals(chosen, assessment.CurrentWinningSource?.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(chosen, assessment.ChallengerSource?.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!isCandidate)
        {
            throw new ArgumentException(
                $"Chosen winner source '{chosenWinnerSource}' is not one of the conflict's candidate sources " +
                $"('{assessment.CurrentWinningSource}', '{assessment.ChallengerSource}').",
                nameof(chosenWinnerSource));
        }
    }

    private async Task<SecurityMasterRevisionRecord> EnsureRevisionInStateAsync(
        Guid revisionId, Guid securityId, SecurityMasterRevisionStateDto expected, CancellationToken ct)
    {
        var revision = await _revisions.GetAsync(revisionId, ct).ConfigureAwait(false);
        if (revision is null)
        {
            throw new SecurityMasterRevisionStateException(revisionId, "no such revision.");
        }
        if (revision.SecurityId != securityId)
        {
            throw new SecurityMasterRevisionStateException(
                revisionId, $"belongs to security '{revision.SecurityId:D}', not '{securityId:D}'.");
        }
        if (revision.State != expected)
        {
            throw new SecurityMasterRevisionStateException(
                revisionId, $"expected state {expected} but the revision is {revision.State}.");
        }

        return revision;
    }

    private static SecurityMasterEditResultDto BuildLifecycleResult(
        Guid securityId, Guid revisionId, long version, SecurityMasterRevisionStateDto state,
        string actor, string? reason, string eventType, string summary)
        => new(
            securityId,
            revisionId,
            version,
            state,
            BuildChangeEntry(version, eventType, actor, effectiveFrom: null, sourceRecordId: null,
                reason: reason, summary: summary, changedFields: []));

    private static SecurityMasterChangeHistoryItemDto BuildChangeEntry(
        long streamVersion,
        string eventType,
        string actor,
        DateTimeOffset? effectiveFrom,
        string? sourceRecordId,
        string? reason,
        string summary,
        IReadOnlyList<string> changedFields)
        => new(
            ChangeId: $"{streamVersion}:{eventType}",
            StreamVersion: streamVersion,
            EventType: eventType,
            ChangedAtUtc: DateTimeOffset.UtcNow,
            EffectiveAtUtc: effectiveFrom,
            Actor: actor,
            Origin: "User",
            SourceSystem: OperatorSourceSystem,
            SourceRecordId: sourceRecordId,
            Reason: reason,
            Summary: summary,
            ChangedFields: changedFields,
            ChangedFieldsSummary: changedFields.Count == 0 ? "No structured field diff." : string.Join(", ", changedFields));

    private static SecurityMasterDownstreamImpactDto EmptyDownstreamImpact()
        => new(
            FundProfileId: null,
            IsScoped: false,
            Severity: SecurityMasterImpactSeverity.None,
            Summary: "No downstream impact resolved.",
            PortfolioExposureSummary: string.Empty,
            LedgerExposureSummary: string.Empty,
            ReconciliationExposureSummary: string.Empty,
            ReportPackExposureSummary: string.Empty,
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);
}
