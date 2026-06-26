using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Phase 2 governed write surface for the Security Master Passport Workbench.
///
/// <para>This slice implements the two operator write primitives end-to-end:</para>
/// <list type="bullet">
///   <item><c>UpdateSecurityFieldAsync</c> — justification-gated, version-guarded operator field
///     edits staged through the existing <see cref="IOperatorOverridesStore"/>.</item>
///   <item><c>ResolveSourceConflictAsync</c> — conflict resolution scored by the Phase 1
///     <see cref="ISecurityMasterConflictAuthorityPolicy"/>, with acknowledged-deviation enforcement.</item>
/// </list>
/// <para><c>PublishRevisionAsync</c> fans out to the ordered
/// <see cref="ISecurityMasterRevisionPublishedHandler"/> seam (Phase 3 supplies the handlers). The
/// full OperationsContinuity approval-gate bridge (workflow scoping + reviewer routing + durable
/// submitted/approved state) is the subsequent Phase 2 slice; <c>SubmitForApprovalAsync</c> records
/// the logical transition until then.</para>
/// </summary>
public sealed class SecurityMasterWorkbenchCommandService : ISecurityMasterWorkbenchCommandService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly IOperatorOverridesStore _overrides;
    private readonly ISecurityMasterConflictAuthorityPolicy _conflictPolicy;
    private readonly ISecurityMasterWorkbenchQueryService _queryService;
    private readonly IReadOnlyList<ISecurityMasterRevisionPublishedHandler> _handlers;
    private readonly ILogger<SecurityMasterWorkbenchCommandService> _logger;

    public SecurityMasterWorkbenchCommandService(
        ISecurityMasterEventStore eventStore,
        IOperatorOverridesStore overrides,
        ISecurityMasterConflictAuthorityPolicy conflictPolicy,
        ISecurityMasterWorkbenchQueryService queryService,
        IEnumerable<ISecurityMasterRevisionPublishedHandler> handlers,
        ILogger<SecurityMasterWorkbenchCommandService> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _conflictPolicy = conflictPolicy ?? throw new ArgumentNullException(nameof(conflictPolicy));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
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

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (request.ExpectedVersion != currentVersion)
        {
            throw new SecurityMasterConcurrencyException(request.SecurityId, request.ExpectedVersion, currentVersion);
        }

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

        _logger.LogInformation(
            "Security Master field edit staged for {SecurityId} field {FieldPath} effective {EffectiveFrom:o} by {Actor}",
            request.SecurityId, request.FieldPath, request.EffectiveFrom, request.Actor);

        var changeEntry = BuildChangeEntry(
            currentVersion,
            eventType: "operator-field-edit",
            actor: request.Actor,
            effectiveFrom: request.EffectiveFrom,
            sourceRecordId: request.SourceRecordId,
            reason: request.Justification,
            summary: $"Operator edit to {request.FieldPath}.",
            changedFields: [request.FieldPath]);

        return new SecurityMasterEditResultDto(
            request.SecurityId,
            Guid.NewGuid(),
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

        var assessment = snapshot?.ConflictAssessments
            .FirstOrDefault(a => a.Conflict.ConflictId == request.ConflictId)
            ?? throw new InvalidOperationException(
                $"Conflict '{request.ConflictId}' was not found for security '{request.SecurityId}'.");

        var providerConfidence = snapshot?.InstrumentPassport?.ProviderConfidence
            ?? Array.Empty<InstrumentPassportProviderConfidenceDto>();

        var decision = _conflictPolicy.Evaluate(assessment, providerConfidence);
        var isDeviation = !string.Equals(
            request.ChosenWinnerSource?.Trim(),
            decision.PolicyWinnerSource?.Trim(),
            StringComparison.OrdinalIgnoreCase);

        if (isDeviation && !request.AcknowledgePolicyDeviation)
        {
            throw new ArgumentException(
                "Choosing a winner that diverges from the policy winner requires acknowledging the deviation.",
                nameof(request));
        }

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

        var currentVersion = await GetCurrentVersionAsync(request.SecurityId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master revision {RevisionId} for {SecurityId} submitted for approval by {Actor}",
            request.RevisionId, request.SecurityId, request.Actor);

        var changeEntry = BuildChangeEntry(
            currentVersion,
            eventType: "operator-field-edit-submitted",
            actor: request.Actor,
            effectiveFrom: null,
            sourceRecordId: null,
            reason: request.Note,
            summary: "Revision submitted for approval.",
            changedFields: []);

        return new SecurityMasterEditResultDto(
            request.SecurityId,
            request.RevisionId,
            currentVersion,
            SecurityMasterRevisionStateDto.Submitted,
            changeEntry);
    }

    public async Task<SecurityMasterPublishResultDto> PublishRevisionAsync(
        PublishSecurityMasterRevisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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
        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(evt, ct).ConfigureAwait(false);
                invalidated.Add(handler.GetType().Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The revision is already durable; handlers are idempotent so publish is retry-safe.
                _logger.LogError(
                    ex,
                    "Revision-published handler {Handler} failed for {SecurityId} revision {RevisionId}",
                    handler.GetType().Name, request.SecurityId, request.RevisionId);
            }
        }

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
            SourceSystem: "operator-workbench",
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
