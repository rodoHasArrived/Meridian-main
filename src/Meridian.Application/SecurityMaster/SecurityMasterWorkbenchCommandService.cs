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
public sealed class SecurityMasterWorkbenchCommandService : ISecurityMasterWorkbenchCommandService
{
    private const string OperatorFieldEditEventType = "operator-field-edit";
    private const string OperatorSourceSystem = "operator-workbench";
    private static readonly Regex LogUnsafeControlChars = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F\r\n]+", RegexOptions.Compiled);

    private static string SanitizeForLog(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : LogUnsafeControlChars.Replace(value, " ").Trim();

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

        // Edits addressing the assetSpecificTerms namespace are anchored to the declared
        // per-asset-class schema: the key must be a declared term field and the value must coerce
        // to its declared type. Paths outside that namespace remain the free annotation surface.
        // The returned path is the schema-canonical spelling — persisting the caller's raw alias or
        // casing variant would fork the same term into separate override keys, revisions, and
        // provenance rows, so every write below uses the canonical path.
        var fieldPath = await EnsureFieldEditIsSchemaValidAsync(request, ct).ConfigureAwait(false);

        // Stage the operator value as an override read-model annotation. The override store applies
        // the patch under a serializable, row-locked transaction; it does not advance the economic
        // version, so the returned NewVersion is the unchanged canonical version. A blank value is
        // a CLEAR: it removes the overlay key rather than persisting an empty-string override that
        // would bypass type validation and read as an asserted value downstream.
        var isClear = string.IsNullOrWhiteSpace(request.NewValue);
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
        OperatorOverridesDto stagedOverride;
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
        var revision = await _revisions.CreateDraftAsync(
            request.SecurityId, request.Actor, fieldPath, request.EffectiveFrom, request.Justification,
            request.FundProfileId, ct)
            .ConfigureAwait(false);

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
                        ct).ConfigureAwait(false);
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
                        ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
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

        // Mirror the winner into the durable override read-model annotation for passport display. The
        // authoritative resolution already succeeded above, so a mirror failure is logged rather than
        // surfaced — it cannot lose the recorded winner or re-open the conflict.
        try
        {
            var patch = new OperatorOverridesPatchRequest(
                SetValues: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [$"conflict-resolution:{request.ConflictId:D}"] = request.ChosenWinnerSource ?? string.Empty,
                },
                RemoveKeys: null)
            {
                ReasonCode = request.Reason,
            };
            await _overrides.PatchAsync(request.SecurityId, patch, request.Actor, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Conflict {ConflictId} for {SecurityId} was resolved, but mirroring the winner into the override annotation failed; the authoritative resolution is recorded on the conflict.",
                request.ConflictId, request.SecurityId);
        }

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

            var dto = new OperationsSubmitApprovalRequestDto(
                ExpectedVersion: request.ExpectedWorkflowVersion,
                Actor: request.Actor,
                Reviewer: request.Reviewer!,
                Rationale: rationale,
                ReportPackId: request.ReportPackId ?? string.Empty);

            var result = await _approvalWorkflow.SubmitForApprovalAsync(workflowId, dto, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Submit for approval was blocked ({result.ErrorCode}): {result.ErrorMessage}");
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

        await _revisions.TransitionAsync(
            request.RevisionId,
            SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted,
            request.Actor,
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master revision {RevisionId} for {SecurityId} submitted for approval (no workflow context) by {Actor}",
            request.RevisionId, request.SecurityId, request.Actor);

        return BuildLifecycleResult(request.SecurityId, request.RevisionId, currentVersion,
            SecurityMasterRevisionStateDto.Submitted, request.Actor, rationale, "operator-field-edit-submitted",
            "Revision submitted for approval.");
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
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Revision approval was blocked ({result.ErrorCode}): {result.ErrorMessage}");
        }

        // Advance the durable revision lifecycle only after the gate records the approval.
        await _revisions.TransitionAsync(
            request.RevisionId,
            SecurityMasterRevisionStateDto.Submitted,
            SecurityMasterRevisionStateDto.Approved,
            request.Actor,
            ct: ct).ConfigureAwait(false);

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master revision {RevisionId} for {SecurityId} approved through gate workflow {WorkflowId} by {Actor}",
            request.RevisionId, request.SecurityId, request.WorkflowId, request.Actor);

        return BuildLifecycleResult(request.SecurityId, request.RevisionId, result.NewVersion ?? currentVersion,
            SecurityMasterRevisionStateDto.Approved, request.Actor, request.Rationale, "security-master-field-edit-approved",
            "Revision approved through the operations-continuity gate.");
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

        await _revisions.TransitionAsync(
            request.RevisionId,
            SecurityMasterRevisionStateDto.Approved,
            SecurityMasterRevisionStateDto.Published,
            request.Actor,
            ct: ct).ConfigureAwait(false);

        // Period-aware propagation: after the side-effect handlers run, resolve whether any affected
        // ledger book is in a closed period and therefore needs a governed restatement proposal rather
        // than silent mutation. The authority is the ledger accounting-period status (default-deny).
        var restatement = await _restatementResolver.ResolveAsync(evt, ct).ConfigureAwait(false);

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

        return await EnsureProfileFieldEditMatchesPinnedProfileAsync(request, canonicalFieldPath, ct).ConfigureAwait(false);
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
            || (!editsSchedule && !editsPar && !editsIssueDate && !editsMaturity)
            || string.IsNullOrWhiteSpace(request.NewValue))
        {
            return;
        }

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
        var scheduleJson = editsSchedule ? request.NewValue : TryGetStagedOverrideValue(stagedOverrides, PrincipalSchedulePath);
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
            ? ParseInvariantDecimal(request.NewValue)
            : ParseInvariantDecimal(TryGetStagedOverrideValue(stagedOverrides, ParTermPath)) ?? terms.PrincipalFace;
        var effectiveIssueDate = editsIssueDate
            ? ParseInvariantDate(request.NewValue)
            : ParseInvariantDate(TryGetStagedOverrideValue(stagedOverrides, IssueDateTermPath)) ?? terms.IssueDate;
        var effectiveMaturity = editsMaturity
            ? ParseInvariantDate(request.NewValue)
            : ParseInvariantDate(TryGetStagedOverrideValue(stagedOverrides, MaturityTermPath)) ?? terms.MaturityDate;

        var subject = editsSchedule
            ? "principalSchedule replacement"
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
        // Clears skip VALUE validation but still need key canonicalization: clearing
        // profileFields.CurrentFactor must remove the canonical currentFactor override, not a
        // casing-variant key that leaves the asserted value and its provenance active.
        var isClear = string.IsNullOrWhiteSpace(request.NewValue);
        if ((!isWholeObject && !isNestedField) || (isWholeObject && isClear))
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
            if (!isClear)
            {
                var undeclaredStagedOverrides = await TryGetStagedOverrideValuesAsync(request.SecurityId, ct).ConfigureAwait(false);
                EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
                    currentProjection!, profile, undeclaredStagedOverrides,
                    editedFieldKey: nestedRemainder, proposedValue: request.NewValue, proposedReplacementRoot: null);
            }

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

    /// <summary>
    /// A profile field's EFFECTIVE date: the staged per-field operator override when one exists,
    /// then a field inside a staged WHOLE-OBJECT profileFields replacement (a replacement is what
    /// the record reads after approval, so falling through it to the superseded canonical value
    /// would validate against dates the overlay has already replaced), and only then the canonical
    /// projection value.
    /// </summary>
    private static bool TryResolveEffectiveProfileDate(
        string fieldKey,
        JsonElement? currentProfileFields,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        out DateOnly value)
    {
        value = default;
        if (stagedOverrides is not null)
        {
            var overridePath = ProfileFieldsNestedPrefix + fieldKey;
            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (string.Equals(path, overridePath, StringComparison.OrdinalIgnoreCase)
                    && DateOnly.TryParse(overrideValue.Trim(), System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }

            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (!string.Equals(path, ProfileFieldsRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var replacement = JsonDocument.Parse(overrideValue);
                    if (replacement.RootElement.ValueKind == JsonValueKind.Object
                        && TryReadProfileDate(replacement.RootElement, fieldKey, out value))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // A malformed staged replacement cannot supply the counterpart; fall through
                    // to the canonical value.
                }
            }
        }

        return currentProfileFields is JsonElement retainedFields
            && TryReadProfileDate(retainedFields, fieldKey, out value);
    }

    private static bool TryReadProfileDate(JsonElement profileFields, string key, out DateOnly value)
    {
        value = default;
        foreach (var property in profileFields.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(property.Value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The EFFECTIVE date a whole-object profileFields replacement stages for
    /// <paramref name="fieldKey"/>: a staged per-field override outranks the replacement (the
    /// override layer is applied on top of the object layer when the record is read), so date-order
    /// rules must bind against it — only in its absence does the replacement's own value apply. A
    /// previously staged whole-object replacement is NOT consulted: the replacement being validated
    /// supersedes it.
    /// </summary>
    private static bool TryResolveEffectiveReplacementDate(
        string fieldKey,
        JsonElement replacementRoot,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        out DateOnly value)
    {
        value = default;
        if (stagedOverrides is not null)
        {
            var overridePath = ProfileFieldsNestedPrefix + fieldKey;
            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (string.Equals(path, overridePath, StringComparison.OrdinalIgnoreCase)
                    && DateOnly.TryParse(overrideValue.Trim(), System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }
        }

        return TryReadProfileDate(replacementRoot, fieldKey, out value);
    }

    /// <summary>
    /// A profile-governed field edit on a record whose asset class RESOLVED past CustomAsset
    /// (e.g. a private-fund-interest envelope reclassified to PrivateFundInterest) must satisfy
    /// the resolved class's domain invariants, not just the pinned profile's field rules: the
    /// seeded profile permits <c>commitment = 0</c> while the PrivateFundInterest kind requires a
    /// strictly positive commitment, so a profile-valid edit could stage — and approve — an
    /// overlay the canonical amend seam rejects. Reconstructs the record's EFFECTIVE profileFields
    /// (base object layer, staged per-field overrides, then the proposed edit), parses the
    /// resolved kind from the effective envelope, and runs the canonical kind invariants. Fails
    /// CLOSED when the effective terms cannot be reconstructed or parsed — a reserved-namespace
    /// edit whose effective outcome cannot be validated must not stage.
    /// </summary>
    private static void EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
        SecurityProjectionRecord projection,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        string? editedFieldKey,
        string? proposedValue,
        JsonElement? proposedReplacementRoot)
    {
        if (string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(projection.AssetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase))
        {
            // A CustomAsset overlay is governed by the pinned profile alone; there is no resolved
            // kind whose invariants could tighten the profile's declared field rules.
            return;
        }

        try
        {
            var effectiveFields = BuildEffectiveProfileFields(
                projection, profile, stagedOverrides, editedFieldKey, proposedValue, proposedReplacementRoot);
            var envelope = System.Text.Json.Nodes.JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())?.AsObject()
                ?? throw new InvalidOperationException("The record's assetSpecificTerms envelope is not a JSON object.");
            foreach (var variantKey in envelope
                .Where(static property => string.Equals(property.Key, "profileFields", StringComparison.OrdinalIgnoreCase))
                .Select(static property => property.Key)
                .ToArray())
            {
                envelope.Remove(variantKey);
            }

            envelope["profileFields"] = effectiveFields;
            var effectiveTerms = JsonSerializer.SerializeToElement(envelope);
            var kind = SecurityMasterMapping.ToRecord(projection with { AssetSpecificTerms = effectiveTerms }).Kind;
            var invariantErrors = Meridian.FSharp.SecurityMasterInterop.SecurityMasterCommandFacade.ValidateKindInvariants(kind);
            if (invariantErrors.Length > 0)
            {
                var summary = string.Join("; ", invariantErrors.Select(static e => $"[{e.Code}] {e.Message}"));
                throw new ArgumentException(
                    $"The effective profileFields overlay violates the resolved asset class " +
                    $"'{projection.AssetClass}' domain invariants: {summary}");
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ArgumentException(
                $"The effective profileFields overlay for security '{projection.SecurityId:D}' could not be " +
                $"reconstructed and validated against the resolved asset class '{projection.AssetClass}' " +
                $"({ex.Message}); the namespace only accepts validated writes.", ex);
        }
    }

    /// <summary>
    /// Layers the record's effective profileFields the way the post-approval read applies them:
    /// the base object layer (the proposed whole-object replacement when this edit IS one, else a
    /// previously staged whole-object replacement, else the canonical profileFields), then staged
    /// per-field overrides on top, then the proposed scalar edit (which supersedes any staged
    /// override of the same field).
    /// </summary>
    private static System.Text.Json.Nodes.JsonObject BuildEffectiveProfileFields(
        SecurityProjectionRecord projection,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        string? editedFieldKey,
        string? proposedValue,
        JsonElement? proposedReplacementRoot)
    {
        System.Text.Json.Nodes.JsonObject baseFields;
        if (proposedReplacementRoot is JsonElement replacementRoot)
        {
            baseFields = System.Text.Json.Nodes.JsonNode.Parse(replacementRoot.GetRawText())!.AsObject();
        }
        else
        {
            string? stagedRootReplacement = null;
            if (stagedOverrides is not null)
            {
                foreach (var (path, overrideValue) in stagedOverrides)
                {
                    if (string.Equals(path, ProfileFieldsRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        stagedRootReplacement = overrideValue;
                        break;
                    }
                }
            }

            if (stagedRootReplacement is not null)
            {
                baseFields = System.Text.Json.Nodes.JsonNode.Parse(stagedRootReplacement)!.AsObject();
            }
            else if (projection.AssetSpecificTerms is { ValueKind: JsonValueKind.Object } terms
                && terms.TryGetProperty("profileFields", out var persisted)
                && persisted.ValueKind == JsonValueKind.Object)
            {
                baseFields = System.Text.Json.Nodes.JsonNode.Parse(persisted.GetRawText())!.AsObject();
            }
            else
            {
                baseFields = new System.Text.Json.Nodes.JsonObject();
            }
        }

        if (stagedOverrides is not null)
        {
            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (!path.StartsWith(ProfileFieldsNestedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var overriddenKey = path[ProfileFieldsNestedPrefix.Length..];
                if (overriddenKey.Length == 0
                    || overriddenKey.Contains('.', StringComparison.Ordinal)
                    || (editedFieldKey is not null
                        && string.Equals(overriddenKey, editedFieldKey, StringComparison.OrdinalIgnoreCase)))
                {
                    // Deeper subpaths are rejected as edits for declared fields and stay dynamic
                    // junk for undeclared ones; the edited field's old override is superseded by
                    // the proposed value below.
                    continue;
                }

                SetEffectiveProfileField(baseFields, profile, overriddenKey, overrideValue);
            }
        }

        if (editedFieldKey is not null && proposedValue is not null)
        {
            SetEffectiveProfileField(baseFields, profile, editedFieldKey, proposedValue);
        }

        return baseFields;
    }

    /// <summary>
    /// Writes a string-staged override into the effective profileFields object coerced to the
    /// declared field type (overrides are stored as strings while the kind parser reads typed
    /// JSON), replacing any case-variant spelling of the key so the layered value cannot fork
    /// from the canonical one.
    /// </summary>
    private static void SetEffectiveProfileField(
        System.Text.Json.Nodes.JsonObject fields,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        string key,
        string value)
    {
        var declared = profile.Fields.FirstOrDefault(
            field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
        var canonicalKey = declared?.Key ?? key;
        foreach (var variantKey in fields
            .Where(property => string.Equals(property.Key, canonicalKey, StringComparison.OrdinalIgnoreCase))
            .Select(static property => property.Key)
            .ToArray())
        {
            fields.Remove(variantKey);
        }

        var trimmed = value.Trim();
        System.Text.Json.Nodes.JsonNode? node = declared?.FieldType switch
        {
            SecurityAssetProfileFieldTypeDto.Decimal when decimal.TryParse(
                trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var decimalValue)
                => System.Text.Json.Nodes.JsonValue.Create(decimalValue),
            SecurityAssetProfileFieldTypeDto.Integer when int.TryParse(
                trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integerValue)
                => System.Text.Json.Nodes.JsonValue.Create(integerValue),
            SecurityAssetProfileFieldTypeDto.Boolean when bool.TryParse(trimmed, out var booleanValue)
                => System.Text.Json.Nodes.JsonValue.Create(booleanValue),
            // An UNDECLARED key has no profile type to coerce through, but the resolved kind's
            // parser reads typed JSON: a structured value edited as its JSON text (an array of
            // factor rows, a number) must land as that structure, not as a quoted string the
            // parser cannot read. Values that do not parse as JSON stay plain strings.
            null => TryParseJsonNode(trimmed) ?? System.Text.Json.Nodes.JsonValue.Create(value),
            _ => System.Text.Json.Nodes.JsonValue.Create(value)
        };
        fields[canonicalKey] = node;
    }

    private static System.Text.Json.Nodes.JsonNode? TryParseJsonNode(string value)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ProfileFieldStringIsValid(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldDefinitionDto field,
        string value,
        out string? error)
    {
        error = null;
        var trimmed = value.Trim();
        var typeIsValid = field.FieldType switch
        {
            SecurityAssetProfileFieldTypeDto.Text => true,
            SecurityAssetProfileFieldTypeDto.Decimal =>
                decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Integer =>
                int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Boolean => bool.TryParse(trimmed, out _),
            SecurityAssetProfileFieldTypeDto.Date =>
                DateOnly.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Enum =>
                field.AllowedValues.Any(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)),
            SecurityAssetProfileFieldTypeDto.CurrencyCode =>
                trimmed.Length == 3 && trimmed.All(static character => character is >= 'A' and <= 'Z'),
            SecurityAssetProfileFieldTypeDto.SecurityLink =>
                Guid.TryParse(trimmed, out var link) && link != Guid.Empty,
            _ => true
        };
        if (!typeIsValid)
        {
            error =
                $"Value '{value}' does not satisfy the pinned profile's declared type {field.FieldType} " +
                $"for profile field '{field.Key}'.";
            return false;
        }

        if (field.FieldType is SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer
            && decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var numeric)
            && ((field.MinValue.HasValue && numeric < field.MinValue.Value)
                || (field.MaxValue.HasValue && numeric > field.MaxValue.Value)))
        {
            error =
                $"Value '{value}' is outside the pinned profile's allowed range for field '{field.Key}' " +
                $"({field.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}" +
                $"–{field.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}).";
            return false;
        }

        return true;
    }

    private static bool ProfileFieldElementIsValid(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldDefinitionDto field,
        JsonElement value,
        out string? error)
    {
        error = null;
        var typeIsValid = field.FieldType switch
        {
            // A required Text field must also be nonblank, mirroring the read-side profile
            // validator: an empty required string strips the value while passing the kind check.
            SecurityAssetProfileFieldTypeDto.Text =>
                value.ValueKind == JsonValueKind.String
                && (!field.IsRequired || !string.IsNullOrWhiteSpace(value.GetString())),
            SecurityAssetProfileFieldTypeDto.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetProfileFieldTypeDto.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetProfileFieldTypeDto.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetProfileFieldTypeDto.Date =>
                value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Enum =>
                value.ValueKind == JsonValueKind.String
                && field.AllowedValues.Any(allowed => string.Equals(allowed, value.GetString(), StringComparison.OrdinalIgnoreCase)),
            SecurityAssetProfileFieldTypeDto.CurrencyCode =>
                value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: 3 } currency
                && currency.All(static character => character is >= 'A' and <= 'Z'),
            SecurityAssetProfileFieldTypeDto.SecurityLink =>
                value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var link)
                && link != Guid.Empty,
            _ => true
        };
        if (!typeIsValid)
        {
            error =
                $"profileFields.{field.Key} does not satisfy the pinned profile's declared type {field.FieldType}.";
            return false;
        }

        if (field.FieldType is SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer
            && value.TryGetDecimal(out var numeric)
            && ((field.MinValue.HasValue && numeric < field.MinValue.Value)
                || (field.MaxValue.HasValue && numeric > field.MaxValue.Value)))
        {
            error =
                $"profileFields.{field.Key} is outside the pinned profile's allowed range " +
                $"({field.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}" +
                $"–{field.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}).";
            return false;
        }

        return true;
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
