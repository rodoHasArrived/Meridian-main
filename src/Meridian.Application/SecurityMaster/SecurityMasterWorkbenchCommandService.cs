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
///     version is a read-only staleness guard (a stale <c>ExpectedVersion</c> surfaces as
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

    private readonly ISecurityMasterEventStore _eventStore;
    private readonly IOperatorOverridesStore _overrides;
    private readonly ISecurityMasterConflictAuthorityPolicy _conflictPolicy;
    private readonly ISecurityMasterConflictService _conflictService;
    private readonly ISecurityMasterWorkbenchQueryService _queryService;
    private readonly IOperationsContinuityWorkflowService _approvalWorkflow;
    private readonly ISecurityMasterRevisionStore _revisions;
    private readonly IReadOnlyList<ISecurityMasterRevisionPublishedHandler> _handlers;
    private readonly ILogger<SecurityMasterWorkbenchCommandService> _logger;

    public SecurityMasterWorkbenchCommandService(
        ISecurityMasterEventStore eventStore,
        IOperatorOverridesStore overrides,
        ISecurityMasterConflictAuthorityPolicy conflictPolicy,
        ISecurityMasterConflictService conflictService,
        ISecurityMasterWorkbenchQueryService queryService,
        IOperationsContinuityWorkflowService approvalWorkflow,
        ISecurityMasterRevisionStore revisions,
        IEnumerable<ISecurityMasterRevisionPublishedHandler> handlers,
        ILogger<SecurityMasterWorkbenchCommandService> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _conflictPolicy = conflictPolicy ?? throw new ArgumentNullException(nameof(conflictPolicy));
        _conflictService = conflictService ?? throw new ArgumentNullException(nameof(conflictService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _approvalWorkflow = approvalWorkflow ?? throw new ArgumentNullException(nameof(approvalWorkflow));
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
        _handlers = (handlers ?? throw new ArgumentNullException(nameof(handlers)))
            .OrderBy(static h => h.Order)
            .ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // The passport-displayed security version is a read-only staleness guard: a stale view of
        // the canonical security (it was amended/deactivated since the operator loaded it) is
        // rejected before any overlay write. Operator field edits are overlay annotations and must
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

        // Stage the operator value as an override read-model annotation. The override store applies
        // the patch under a serializable, row-locked transaction; it does not advance the economic
        // version, so the returned NewVersion is the unchanged canonical version.
        var patch = new OperatorOverridesPatchRequest(
            SetValues: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [request.FieldPath] = request.NewValue ?? string.Empty,
            },
            RemoveKeys: null)
        {
            ReasonCode = request.Justification,
        };
        await _overrides.PatchAsync(request.SecurityId, patch, request.Actor, ct).ConfigureAwait(false);

        // Open a durable Draft revision so the governed lifecycle (submit → approve → publish) is
        // anchored to a real, server-issued revision id rather than a transient client value.
        var revision = await _revisions.CreateDraftAsync(request.SecurityId, request.Actor, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master field edit staged for {SecurityId} field {FieldPath} as draft revision {RevisionId} at version {Version} by {Actor}",
            request.SecurityId, request.FieldPath, revision.RevisionId, currentVersion, request.Actor);

        var changeEntry = BuildChangeEntry(
            currentVersion,
            eventType: OperatorFieldEditEventType,
            actor: request.Actor,
            effectiveFrom: request.EffectiveFrom,
            sourceRecordId: request.SourceRecordId,
            reason: request.Justification,
            summary: $"Operator edit to {request.FieldPath}.",
            changedFields: [request.FieldPath]);

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
            var dto = new OperationsSubmitApprovalRequestDto(
                ExpectedVersion: request.ExpectedWorkflowVersion,
                Actor: request.Actor,
                Reviewer: string.IsNullOrWhiteSpace(request.Reviewer) ? request.Actor : request.Reviewer!,
                Rationale: rationale,
                ReportPackId: request.ReportPackId ?? string.Empty);

            var result = await _approvalWorkflow.SubmitForApprovalAsync(workflowId, dto, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Submit for approval was blocked ({result.ErrorCode}): {result.ErrorMessage}");
            }

            // Advance the durable revision lifecycle only after the gate accepts the submission.
            await _revisions.TransitionAsync(
                request.RevisionId,
                SecurityMasterRevisionStateDto.Draft,
                SecurityMasterRevisionStateDto.Submitted,
                request.Actor,
                ct).ConfigureAwait(false);

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
            ct).ConfigureAwait(false);

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
        await EnsureRevisionInStateAsync(
            request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Submitted, ct).ConfigureAwait(false);

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
            ct).ConfigureAwait(false);

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
        await EnsureRevisionInStateAsync(
            request.RevisionId, request.SecurityId, SecurityMasterRevisionStateDto.Approved, ct).ConfigureAwait(false);

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);

        var snapshot = await _queryService
            .GetTrustSnapshotAsync(request.SecurityId, fundProfileId: null, ct)
            .ConfigureAwait(false);

        var evt = new SecurityMasterRevisionPublishedEvent(
            SecurityId: request.SecurityId,
            RevisionId: request.RevisionId,
            Version: currentVersion,
            EffectiveFrom: DateTimeOffset.UtcNow,
            ChangedFields: [],
            DownstreamImpact: snapshot?.DownstreamImpact ?? EmptyDownstreamImpact(),
            AffectedLedgerBookIds: [],
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
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Published Security Master revision {RevisionId} for {SecurityId} by {Actor} ({HandlerCount} handlers)",
            request.RevisionId, request.SecurityId, request.Actor, _handlers.Count);

        return new SecurityMasterPublishResultDto(
            request.SecurityId,
            request.RevisionId,
            currentVersion,
            RestatementRequired: false,
            RestatementCandidates: [],
            InvalidatedProjections: invalidated);
    }

    private async Task<long> GetCurrentVersionAsync(Guid securityId, CancellationToken ct)
    {
        var events = await _eventStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        return events.Count == 0 ? 0L : events.Max(static e => e.StreamVersion);
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
