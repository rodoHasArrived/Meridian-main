using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Core.Scheduling;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Manages scoped reporting schedules and coordinates their governed execution state.
/// </summary>
public sealed partial class ReportingScheduleService
{
    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly ReportWriterDatasetSourceService? _datasetSourceService;
    private readonly ReportingRunReadinessService? _readinessService;
    private readonly ReportingRunCertificationService? _certificationService;
    private readonly IReportingGovernanceEndpointCoordinator? _governanceCoordinator;
    private readonly IReportingRecipientDestinationResolver _destinationResolver;
    private readonly Meridian.Reporting.IReportingScheduleStore? _store;
    private readonly Dictionary<ReportingScheduleIdentity, ReportingScheduleRecordDto> _schedules =
        new(ReportingScheduleIdentityComparer.Instance);
    private readonly HashSet<ReportingScheduleIdentity> _dueRunClaims =
        new(ReportingScheduleIdentityComparer.Instance);
    private readonly object _gate = new();

#pragma warning disable CS0618
    [Obsolete(
        "Use the constructor overload accepting Meridian.Reporting.IReportingScheduleStore.")]
    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        IReportingScheduleStore? store = null,
        ReportPackDeliveryService? deliveryService = null,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        ReportWriterDatasetSourceService? datasetSourceService = null,
        ReportingRunReadinessService? readinessService = null,
        ReportingRunCertificationService? certificationService = null,
        IReportingGovernanceEndpointCoordinator? governanceCoordinator = null,
        IReportingRecipientDestinationResolver? destinationResolver = null)
        : this(
            orchestrationService,
            store,
            deliveryService,
            governedTemplateCatalog,
            datasetSourceService,
            readinessService,
            certificationService,
            governanceCoordinator,
            destinationResolver,
            deploymentReadinessService: null)
    {
    }

    [Obsolete(
        "Use the constructor overload accepting Meridian.Reporting.IReportingScheduleStore.")]
    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        IReportingScheduleStore? store,
        ReportPackDeliveryService? deliveryService,
        GovernedReportingTemplateCatalog? governedTemplateCatalog,
        ReportWriterDatasetSourceService? datasetSourceService,
        ReportingRunReadinessService? readinessService,
        ReportingRunCertificationService? certificationService,
        IReportingGovernanceEndpointCoordinator? governanceCoordinator,
        IReportingRecipientDestinationResolver? destinationResolver,
        IReportingDeploymentReadinessService? deploymentReadinessService)
        : this(
            orchestrationService,
            (Meridian.Reporting.IReportingScheduleStore?)store,
            deliveryService,
            governedTemplateCatalog,
            datasetSourceService,
            readinessService,
            certificationService,
            governanceCoordinator,
            destinationResolver,
            deploymentReadinessService)
    {
    }
#pragma warning restore CS0618

    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        Meridian.Reporting.IReportingScheduleStore? store,
        ReportPackDeliveryService? deliveryService = null,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        ReportWriterDatasetSourceService? datasetSourceService = null,
        ReportingRunReadinessService? readinessService = null,
        ReportingRunCertificationService? certificationService = null,
        IReportingGovernanceEndpointCoordinator? governanceCoordinator = null,
        IReportingRecipientDestinationResolver? destinationResolver = null)
        : this(
            orchestrationService,
            store,
            deliveryService,
            governedTemplateCatalog,
            datasetSourceService,
            readinessService,
            certificationService,
            governanceCoordinator,
            destinationResolver,
            deploymentReadinessService: null)
    {
    }

    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        Meridian.Reporting.IReportingScheduleStore? store,
        ReportPackDeliveryService? deliveryService,
        GovernedReportingTemplateCatalog? governedTemplateCatalog,
        ReportWriterDatasetSourceService? datasetSourceService,
        ReportingRunReadinessService? readinessService,
        ReportingRunCertificationService? certificationService,
        IReportingGovernanceEndpointCoordinator? governanceCoordinator,
        IReportingRecipientDestinationResolver? destinationResolver,
        IReportingDeploymentReadinessService? deploymentReadinessService)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        // Kept in the signature for source compatibility while scheduled delivery is migrated to
        // the canonical post-release secure queue. It must never be invoked inline from generation.
        _ = deliveryService;
        _governedTemplateCatalog = governedTemplateCatalog;
        _datasetSourceService = datasetSourceService;
        _readinessService = readinessService;
        _certificationService = certificationService;
        _governanceCoordinator = governanceCoordinator;
        _destinationResolver = destinationResolver ?? new RejectingReportingRecipientDestinationResolver();
        _deploymentReadinessService = deploymentReadinessService;
        _store = store;
        foreach (var schedule in _store?.Load() ?? [])
        {
            var identity = ReportingScheduleIdentity.From(schedule);
            ValidateRetainedSchedule(schedule, identity);
            if (!_schedules.TryAdd(identity, schedule))
            {
                throw new InvalidDataException(
                    $"Duplicate reporting schedule identity '{identity.TenantId}/{identity.CompanyId}/{identity.ScheduleId}'.");
            }
        }
    }

    public IReadOnlyList<ReportingScheduleRecordDto> ListSchedules(int limit = 100)
        => ListSchedules(accessContext: null, limit);

    public IReadOnlyList<ReportingScheduleRecordDto> ListSchedules(
        ReportAccessQueryContext? accessContext,
        int limit = 100)
    {
        lock (_gate)
        {
            return _schedules.Values
                .Where(schedule => IsScheduleInScope(schedule, accessContext))
                .Where(schedule => accessContext?.RequireBoundScope != true
                    || HasValidAccessPolicySnapshot(schedule))
                .Where(schedule => accessContext is null
                    || ReportAccessPolicyEvaluator.Evaluate(schedule.AccessPolicySnapshot, accessContext).IsAccessible)
                .OrderBy(static schedule => schedule.DueAtUtc)
                .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
        }
    }

    public ReportingScheduleRecordDto Upsert(ReportingScheduleUpsertRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Upsert(request, new ReportAccessQueryContext(request.RequestedBy));
    }

    public ReportingScheduleRecordDto Upsert(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext)
    {
        EnsureReportingDeploymentReady("upsert reporting schedules");
        var prepared = PrepareUpsert(
            request,
            accessContext,
            requireExplicitDeliveryRecipient: accessContext?.RequireBoundScope == true
                                              && request.State == ReportingScheduleStateDto.Active,
            allowLegacyRecipientInference: true);
        EnsureSynchronousUpsertCanCommit(prepared.Candidate, accessContext);
        return CommitPreparedUpsert(prepared);
    }

    public Task<ReportingScheduleRecordDto> UpsertAsync(
        ReportingScheduleUpsertRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return UpsertAsync(
            request,
            new ReportAccessQueryContext(request.RequestedBy),
            cancellationToken);
    }

    public async Task<ReportingScheduleRecordDto> UpsertAsync(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext,
        CancellationToken cancellationToken = default)
    {
        EnsureReportingDeploymentReady("upsert reporting schedules");
        var prepared = PrepareUpsert(
            request,
            accessContext,
            requireExplicitDeliveryRecipient: accessContext?.RequireBoundScope == true,
            allowLegacyRecipientInference: true);
        await ValidateExternalDeliveryTargetsAsync(prepared.Candidate, cancellationToken)
            .ConfigureAwait(false);
        return CommitPreparedUpsert(prepared);
    }

    private PreparedScheduleUpsert PrepareUpsert(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext,
        bool requireExplicitDeliveryRecipient,
        bool allowLegacyRecipientInference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        if (request.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "maxRetries must be zero or greater.");
        }

        // A null request zone keeps the retained zone; the effective zone is resolved again
        // under the lock once the existing record is known.
        ValidateCronExpressionForUpsert(request.CronExpression, request.DueAtUtc, request.TimeZoneId);

        EnsureTemplateAccess(request.TemplateId, accessContext);
        EnsureBoundContext(accessContext);
        if (accessContext?.RequireBoundScope == true
            && !string.Equals(request.RequestedBy?.Trim(), accessContext.ActorPrincipalId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule requester must match the authenticated actor.");
        }
        if (accessContext?.RequireBoundScope == true && request.DatasetRows is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "Governed reporting schedules cannot retain caller-supplied rows; select a server-owned dataset source.");
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var scheduleId = request.ScheduleId.Trim();
            var identity = BuildLookupIdentity(scheduleId, accessContext);
            var existing = _schedules.GetValueOrDefault(identity);
            if (existing is not null)
            {
                EnsureScheduleInScope(existing, accessContext);
            }
            var deliveryTargets = NormalizeDeliveryTargets(
                request.DeliveryTargets ?? existing?.DeliveryTargets);
            var datasetRows = request.DatasetRows is null
                ? existing?.DatasetRows
                : request.DatasetRows;
            var datasetSourceId = request.DatasetSourceId is null
                ? existing?.DatasetSourceId
                : NormalizeDatasetSourceId(request.DatasetSourceId);
            var brandingThemeOverride = request.BrandingThemeOverride ?? existing?.BrandingThemeOverride;
            var brandingThemeId = request.BrandingThemeOverride is not null
                ? NormalizeDatasetSourceId(request.BrandingThemeOverride.ThemeId)
                : request.BrandingThemeId is null
                    ? existing?.BrandingThemeId
                    : NormalizeDatasetSourceId(request.BrandingThemeId);
            var templateSnapshot = ResolveScheduleTemplateSnapshot(
                request.TemplateId,
                request.Template ?? existing?.Template,
                accessContext);
            var timeZoneId = request.TimeZoneId is null
                ? existing?.TimeZoneId
                : ReportingScheduleTimeZones.Normalize(request.TimeZoneId);
            if (request.TimeZoneId is null && timeZoneId is not null)
            {
                ValidateCronExpressionForUpsert(request.CronExpression, request.DueAtUtc, timeZoneId);
            }
            if (request.PeriodCadence is { } requestedCadence
                && !ReportingSchedulePeriodPlanner.IsValid(requestedCadence))
            {
                throw new ArgumentException(
                    $"The reporting schedule period cadence must be Monthly, Quarterly, or Annually with 0 to {ReportingSchedulePeriodPlanner.MaximumDueDaysAfterPeriodEnd} due days after period end.",
                    nameof(request));
            }
            var runParameters = request.RunParameters ?? existing?.RunParameters;
            if (accessContext?.RequireBoundScope == true
                && request.State == ReportingScheduleStateDto.Active
                && runParameters is null)
            {
                throw new InvalidDataException(
                    "An active governed reporting schedule requires an immutable run-parameter snapshot.");
            }
            if (accessContext?.RequireBoundScope == true && runParameters is not null)
            {
                ValidateScheduledDeliveryFormatAvailability(deliveryTargets, runParameters);
            }
            var accessPolicySnapshot = CaptureTemplateAccessPolicy(
                request.TemplateId,
                templateSnapshot,
                accessContext);
            deliveryTargets = ResolveAndValidateDeliveryRecipients(
                deliveryTargets,
                accessPolicySnapshot,
                accessContext,
                requireExplicitDeliveryRecipient,
                allowLegacyRecipientInference);
            var deliveryTargetsSnapshotHash = ComputeDeliveryTargetsSnapshotHash(deliveryTargets);
            var retainedReleaseDeliveryHandoffs = RetainReleaseDeliveryHandoffsForTargetSnapshot(
                existing,
                deliveryTargetsSnapshotHash);
            var record = new ReportingScheduleRecordDto(
                scheduleId,
                request.TemplateId.Trim(),
                request.CronExpression.Trim(),
                request.NextAsOfDate,
                request.DueAtUtc,
                request.MaxRetries,
                request.RequestedBy!.Trim(),
                request.State,
                existing?.CreatedAtUtc ?? now,
                existing is null
                    ? now
                    : NextRevisionTimestamp(existing.UpdatedAtUtc, now),
                existing?.LastRunAtUtc,
                existing?.LastRunId,
                existing?.RunCount ?? 0,
                string.IsNullOrWhiteSpace(request.Description) ? existing?.Description : request.Description.Trim(),
                deliveryTargets,
                datasetRows,
                datasetSourceId,
                brandingThemeId,
                brandingThemeOverride,
                Template: templateSnapshot,
                RunParameters: runParameters,
                TenantId: existing?.TenantId ?? EmptyToNull(identity.TenantId),
                CompanyId: existing?.CompanyId ?? EmptyToNull(identity.CompanyId),
                AccessPolicySnapshot: accessPolicySnapshot,
                LastReadiness: existing?.LastReadiness,
                ReleaseDeliveryHandoffs: retainedReleaseDeliveryHandoffs,
                AccessPolicySnapshotHash: ComputeAccessPolicySnapshotHash(accessPolicySnapshot),
                DeliveryTargetsSnapshotHash: deliveryTargetsSnapshotHash,
                TimeZoneId: timeZoneId,
                PeriodCadence: request.PeriodCadence,
                UnresolvedPeriodCloseAfter: request.PeriodCadence is not null && existing?.NextAsOfDate == request.NextAsOfDate
                    ? existing.UnresolvedPeriodCloseAfter : null);
            return new PreparedScheduleUpsert(identity, existing, record);
        }
    }

    private static IReadOnlyList<ReportingScheduledReleaseHandoffDto>? RetainReleaseDeliveryHandoffsForTargetSnapshot(
        ReportingScheduleRecordDto? existing,
        string? candidateSnapshotHash)
    {
        if (existing?.ReleaseDeliveryHandoffs is not { Count: > 0 } handoffs)
        {
            return existing?.ReleaseDeliveryHandoffs;
        }
        if (string.Equals(
                existing.DeliveryTargetsSnapshotHash,
                candidateSnapshotHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return handoffs;
        }

        // A target edit is also a delivery-authority edit. Pending declarations from the old
        // snapshot must become inert immediately; only already-enqueued immutable audit history
        // survives until an explicit Cancelled handoff state is introduced.
        return handoffs
            .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.Enqueued)
            .ToArray();
    }

    private ReportingScheduleRecordDto CommitPreparedUpsert(PreparedScheduleUpsert prepared)
    {
        lock (_gate)
        {
            var current = _schedules.GetValueOrDefault(prepared.Identity);
            if (!ReferenceEquals(current, prepared.ExpectedExisting))
            {
                // A racing writer landed while delivery targets were being validated. Typed so
                // endpoints map it to the canonical 409 version-conflict body instead of the 400
                // a plain InvalidOperationException falls through to.
                throw current is not null
                    ? ReportingScheduleConcurrencyException.ForConflict(
                        current,
                        prepared.ExpectedExisting?.UpdatedAtUtc)
                    : ReportingScheduleConcurrencyException.ForMissing(
                        prepared.Candidate,
                        prepared.ExpectedExisting!.UpdatedAtUtc);
            }

            _schedules[prepared.Identity] = prepared.Candidate;
            try
            {
                PersistSchedule(
                    prepared.Candidate,
                    prepared.ExpectedExisting?.UpdatedAtUtc);
            }
            catch (Exception ex)
            {
                RestoreScheduleAfterPersistenceFailure(
                    prepared.Identity,
                    prepared.ExpectedExisting,
                    ex);
                throw;
            }
            return prepared.Candidate;
        }
    }

    private sealed record PreparedScheduleUpsert(
        ReportingScheduleIdentity Identity,
        ReportingScheduleRecordDto? ExpectedExisting,
        ReportingScheduleRecordDto Candidate);

    public ReportingScheduleRecordDto SetState(string scheduleId, ReportingScheduleStateDto state)
        => SetState(scheduleId, state, accessContext: null);

    public ReportingScheduleRecordDto SetState(
        string scheduleId,
        ReportingScheduleStateDto state,
        ReportAccessQueryContext? accessContext)
    {
        EnsureReportingDeploymentReady("change reporting schedule state");
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            var identity = ResolveExistingIdentity(scheduleId, accessContext);
            var current = _schedules[identity];

            EnsureScheduleInScope(current, accessContext);
            EnsureScheduleAccess(current, accessContext);
            if (accessContext?.RequireBoundScope == true
                && state == ReportingScheduleStateDto.Active
                && current.RunParameters is null)
            {
                throw new InvalidDataException(
                    "An active governed reporting schedule requires an immutable run-parameter snapshot.");
            }
            if (accessContext?.RequireBoundScope == true
                && state == ReportingScheduleStateDto.Active)
            {
                if (!HasValidDeliveryTargetsSnapshot(current))
                {
                    throw new InvalidDataException(
                        "The reporting schedule delivery-target snapshot is missing or invalid; re-upsert it before activation.");
                }
                foreach (var target in current.DeliveryTargets ?? [])
                {
                    if (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                        || target.RecipientPrincipalKind is null
                        || !Enum.IsDefined(target.RecipientPrincipalKind.Value))
                    {
                        throw new InvalidDataException(
                            $"Scheduled distribution '{target.DistributionId}' must be re-upserted with an explicit typed recipient before activation.");
                    }
                }
            }

            var updated = current with
            {
                State = state,
                UpdatedAtUtc = NextRevisionTimestamp(current.UpdatedAtUtc, DateTimeOffset.UtcNow)
            };
            _schedules[identity] = updated;
            try
            {
                PersistSchedule(updated, current.UpdatedAtUtc);
            }
            catch (Exception ex)
            {
                RestoreScheduleAfterPersistenceFailure(identity, current, ex);
                throw;
            }
            return updated;
        }
    }

    public Task<ReportingScheduleRunResultDto> RunNowAsync(string scheduleId, string? requestedBy, CancellationToken ct = default) =>
        RunNowAsync(
            scheduleId,
            requestedBy,
            string.IsNullOrWhiteSpace(requestedBy) ? null : new ReportAccessQueryContext(requestedBy),
            ct);

    public async Task<ReportingScheduleRunResultDto> RunNowAsync(
        string scheduleId,
        string? requestedBy,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        EnsureReportingDeploymentReady("run a reporting schedule");
        RefreshSchedulesFromStore();
        EnsureBoundContext(accessContext);
        if (accessContext?.RequireBoundScope == true
            && !string.IsNullOrWhiteSpace(requestedBy)
            && !string.Equals(requestedBy.Trim(), accessContext.ActorPrincipalId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule run actor must match the authenticated actor.");
        }

        ReportingScheduleRecordDto schedule;
        lock (_gate)
        {
            schedule = _schedules[ResolveExistingIdentity(scheduleId, accessContext)];
        }

        EnsureScheduleAccess(schedule, accessContext);
        EnsureScheduleInScope(schedule, accessContext);
        var identity = ReportingScheduleIdentity.From(schedule);
        var lease = TryAcquireExecutionLease(identity, schedule, DateTimeOffset.UtcNow);
        if (lease is null)
        {
            throw new ReportingScheduleExecutionLeaseException(
                schedule.TenantId ?? string.Empty,
                schedule.CompanyId ?? string.Empty,
                schedule.ScheduleId,
                $"Reporting schedule '{schedule.ScheduleId}' is already executing under another durable owner.");
        }

        try
        {
            schedule = await RealignPeriodCloseToLedgerAsync(identity, schedule, lease, ct).ConfigureAwait(false);
            return await RunScheduleWithLeaseAsync(
                    schedule,
                    requestedBy,
                    isDueRun: false,
                    accessContext,
                    lease,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseExecutionLease(identity, lease);
        }
    }

    public async Task<ReportingDueScheduleRunResultDto> RunDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
        => (await RunDueCoreAsync(
                nowUtc,
            accessContext: null,
            processAllTenants: true,
            continueAfterScheduleFailure: true,
            allowSchedulingWorkerBootstrap: false,
            ct)
            .ConfigureAwait(false)).Result;

    internal Task<ReportingScheduleWorkerBatchResult> RunDueForWorkerAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct = default) =>
        RunDueCoreAsync(
            nowUtc,
            accessContext: null,
            processAllTenants: true,
            continueAfterScheduleFailure: true,
            allowSchedulingWorkerBootstrap: true,
            ct);

    public async Task<ReportingDueScheduleRunResultDto> RunDueAsync(
        DateTimeOffset nowUtc,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
        => (await RunDueCoreAsync(
                nowUtc,
                accessContext,
                processAllTenants: false,
                continueAfterScheduleFailure: false,
                allowSchedulingWorkerBootstrap: false,
                ct)
            .ConfigureAwait(false)).Result;

    private async Task<ReportingScheduleWorkerBatchResult> RunDueCoreAsync(
        DateTimeOffset nowUtc,
        ReportAccessQueryContext? accessContext,
        bool processAllTenants,
        bool continueAfterScheduleFailure,
        bool allowSchedulingWorkerBootstrap,
        CancellationToken ct)
    {
        EnsureReportingDeploymentReady(
            "execute due reporting schedules",
            allowSchedulingWorkerBootstrap);
        RefreshSchedulesFromStore();
        KeyValuePair<ReportingScheduleIdentity, ReportingScheduleRecordDto>[] dueSchedules;
        lock (_gate)
        {
            dueSchedules = _schedules
                .Where(static pair => pair.Value.State == ReportingScheduleStateDto.Active)
                .Where(pair => processAllTenants || IsScheduleInScope(pair.Value, accessContext))
                .Where(pair => pair.Value.DueAtUtc <= nowUtc)
                .OrderBy(static pair => pair.Value.DueAtUtc)
                .ThenBy(static pair => pair.Value.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var results = new List<ReportingScheduleRunResultDto>(dueSchedules.Length);
        var failures = new List<ReportingScheduleWorkerFailure>();
        foreach (var pair in dueSchedules)
        {
            ct.ThrowIfCancellationRequested();
            var lease = TryAcquireExecutionLease(pair.Key, pair.Value, nowUtc);
            if (lease is null)
            {
                continue;
            }

            try
            {
                var due = await RealignPeriodCloseToLedgerAsync(pair.Key, pair.Value, lease, ct).ConfigureAwait(false);
                if (due.DueAtUtc > nowUtc)
                {
                    continue;
                }

                results.Add(await RunScheduleWithLeaseAsync(
                        due,
                        requestedBy: null,
                        isDueRun: true,
                        processAllTenants ? null : accessContext,
                        lease,
                        ct)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (continueAfterScheduleFailure)
            {
                string? failureRecordingErrorType = null;
                try
                {
                    RecordDueScheduleFailure(pair.Key, exception, nowUtc, lease);
                }
                catch (Exception failureRecordingException)
                {
                    failureRecordingErrorType = failureRecordingException.GetType().Name;
                }
                failures.Add(new ReportingScheduleWorkerFailure(
                    pair.Key.TenantId,
                    pair.Key.CompanyId,
                    pair.Key.ScheduleId,
                    exception.GetType().Name,
                    failureRecordingErrorType));
            }
            finally
            {
                ReleaseExecutionLease(pair.Key, lease);
            }
        }

        return new ReportingScheduleWorkerBatchResult(
            new ReportingDueScheduleRunResultDto(nowUtc, results),
            failures);
    }

    private void RecordDueScheduleFailure(
        ReportingScheduleIdentity identity,
        Exception exception,
        DateTimeOffset evaluatedAtUtc,
        ReportingScheduleExecutionLease lease)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(identity, out var current))
            {
                return;
            }

            var updated = current with
            {
                UpdatedAtUtc = NextRevisionTimestamp(current.UpdatedAtUtc, evaluatedAtUtc),
                LastReadiness = exception is ReportingRunReadinessBlockedException blocked
                    ? blocked.Readiness
                    : current.LastReadiness
            };
            _schedules[identity] = updated;
            try
            {
                PersistSchedule(updated, current.UpdatedAtUtc, lease);
            }
            catch (Exception ex)
            {
                RestoreScheduleAfterPersistenceFailure(identity, current, ex);
                throw;
            }
        }
    }

    public IReadOnlyList<ReportingScheduledReleaseHandoffDto> GetPendingReleaseHandoffs(
        string runId,
        ReportAccessQueryContext? accessContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        EnsureBoundContext(accessContext);
        if (accessContext?.RequireBoundScope != true)
        {
            throw new UnauthorizedAccessException(
                "Pending scheduled delivery handoffs require a bound actor, tenant, and company scope.");
        }

        lock (_gate)
        {
            return _schedules.Values
                .Where(schedule => IsScheduleInScope(schedule, accessContext))
                .Where(schedule => ReportAccessPolicyEvaluator
                    .Evaluate(schedule.AccessPolicySnapshot, accessContext)
                    .IsAccessible)
                .SelectMany(schedule => (schedule.ReleaseDeliveryHandoffs ?? [])
                    .Where(handoff => IsPendingReleaseHandoffBoundToCurrentTargets(schedule, handoff)))
                .Where(handoff => string.Equals(handoff.RunId, runId.Trim(), StringComparison.Ordinal)
                    && handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
                .OrderBy(static handoff => handoff.CreatedAtUtc)
                .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Infrastructure-only discovery for the release-triggered secure-distribution consumer. The
    /// consumer must reconstruct a server-owned delivery authority from each retained tenant and
    /// company scope; this method is never mapped to an HTTP endpoint.
    /// </summary>
    internal IReadOnlyList<ReportingScheduledReleaseHandoffDto> ListPendingReleaseHandoffsForWorker(
        int limit = 100) =>
        ListPendingReleaseHandoffPageForWorker(cursor: null, limit).Handoffs;

    /// <summary>
    /// Returns a stable forward-only page. Failed records remain pending, so the hosted bridge
    /// advances this cursor between polls instead of repeatedly selecting the oldest poison page.
    /// </summary>
    internal ReportingScheduledHandoffPage ListPendingReleaseHandoffPageForWorker(
        ReportingScheduledHandoffCursor? cursor,
        int limit = 100)
    {
        lock (_gate)
        {
            var handoffs = _schedules.Values
                .SelectMany(schedule => (schedule.ReleaseDeliveryHandoffs ?? [])
                    .Where(handoff => IsPendingReleaseHandoffBoundToCurrentTargets(schedule, handoff)))
                .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
                .Where(handoff => cursor is null
                    || handoff.CreatedAtUtc > cursor.CreatedAtUtc
                    || handoff.CreatedAtUtc == cursor.CreatedAtUtc
                    && string.CompareOrdinal(handoff.HandoffId, cursor.HandoffId) > 0)
                .OrderBy(static handoff => handoff.CreatedAtUtc)
                .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
            var nextCursor = handoffs.Length == 0
                ? cursor
                : new ReportingScheduledHandoffCursor(
                    handoffs[^1].CreatedAtUtc,
                    handoffs[^1].HandoffId);
            return new ReportingScheduledHandoffPage(handoffs, nextCursor);
        }
    }

    internal ReportingScheduledReleaseHandoffDto MarkReleaseHandoffEnqueuedForWorker(
        string handoffId,
        string tenantId,
        string companyId,
        string deliveryJobId,
        DateTimeOffset enqueuedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryJobId);

        lock (_gate)
        {
            foreach (var pair in _schedules)
            {
                var schedule = pair.Value;
                if (!string.Equals(schedule.TenantId, tenantId.Trim(), StringComparison.Ordinal)
                    || !string.Equals(schedule.CompanyId, companyId.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                var handoffs = schedule.ReleaseDeliveryHandoffs?.ToArray() ?? [];
                var index = Array.FindIndex(handoffs, handoff =>
                    string.Equals(handoff.HandoffId, handoffId.Trim(), StringComparison.Ordinal)
                    && string.Equals(handoff.TenantId, tenantId.Trim(), StringComparison.Ordinal)
                    && string.Equals(handoff.CompanyId, companyId.Trim(), StringComparison.Ordinal));
                if (index < 0)
                {
                    continue;
                }
                if (handoffs[index].State == ReportingScheduledReleaseHandoffStateDto.PendingRelease
                    && !IsPendingReleaseHandoffBoundToCurrentTargets(schedule, handoffs[index]))
                {
                    throw new InvalidOperationException(
                        "The scheduled release handoff is no longer bound to the current immutable delivery-target snapshot.");
                }

                return CompleteReleaseHandoff(
                    pair.Key,
                    schedule,
                    handoffs,
                    index,
                    deliveryJobId,
                    enqueuedAtUtc);
            }
        }

        throw new KeyNotFoundException("Scheduled release handoff was not found.");
    }

    private ReportingScheduledReleaseHandoffDto CompleteReleaseHandoff(
        ReportingScheduleIdentity identity,
        ReportingScheduleRecordDto schedule,
        ReportingScheduledReleaseHandoffDto[] handoffs,
        int index,
        string deliveryJobId,
        DateTimeOffset enqueuedAtUtc)
    {
        var current = handoffs[index];
        if (current.State == ReportingScheduledReleaseHandoffStateDto.Enqueued)
        {
            if (!string.Equals(
                    current.EnqueuedDeliveryJobId,
                    deliveryJobId.Trim(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The scheduled release handoff was already completed by another delivery job.");
            }

            return current;
        }

        var enqueuedUtc = enqueuedAtUtc.ToUniversalTime();
        var updatedAtUtc = NextRevisionTimestamp(schedule.UpdatedAtUtc, enqueuedUtc);
        var completed = current with
        {
            State = ReportingScheduledReleaseHandoffStateDto.Enqueued,
            EnqueuedDeliveryJobId = deliveryJobId.Trim(),
            EnqueuedAtUtc = enqueuedUtc
        };
        handoffs[index] = completed;
        var updated = schedule with
        {
            UpdatedAtUtc = updatedAtUtc,
            ReleaseDeliveryHandoffs = handoffs
        };
        _schedules[identity] = updated;
        try
        {
            PersistSchedule(updated, schedule.UpdatedAtUtc);
        }
        catch (Exception ex)
        {
            RestoreScheduleAfterPersistenceFailure(identity, schedule, ex);
            throw;
        }
        return completed;
    }

    private async Task<ReportingScheduleRunResultDto> RunScheduleAsync(
        ReportingScheduleRecordDto schedule,
        string? requestedBy,
        bool isDueRun,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct,
        ReportingScheduleExecutionLease executionLease)
    {
        if (schedule.State != ReportingScheduleStateDto.Active)
        {
            throw new InvalidOperationException("Only active reporting schedules can be run.");
        }

        var triggerActor = string.IsNullOrWhiteSpace(requestedBy)
            ? schedule.RequestedBy.Trim()
            : requestedBy.Trim();
        var actor = string.IsNullOrWhiteSpace(schedule.TenantId)
            ? triggerActor
            : schedule.RequestedBy.Trim();
        var effectiveAccessContext = BuildScheduledExecutionAccessContext(schedule, actor, accessContext);
        EnsureScheduleInScope(schedule, effectiveAccessContext);
        EnsureScheduleAccess(schedule, effectiveAccessContext);
        if (effectiveAccessContext.RequireBoundScope && schedule.DatasetRows is { Count: > 0 })
        {
            throw new InvalidDataException(
                "A governed reporting schedule contains caller-supplied dataset rows and cannot execute; select a server-owned dataset source.");
        }
        if (effectiveAccessContext.RequireBoundScope && schedule.RunParameters is null)
        {
            throw new InvalidDataException(
                "A governed reporting schedule has no immutable run-parameter snapshot and cannot execute.");
        }

        var scheduledParameters = await WithLedgerPeriodAsync(
            schedule,
            schedule.RunParameters is null ? null : schedule.RunParameters with { AsOfDate = schedule.NextAsOfDate },
            schedule.NextAsOfDate,
            ct).ConfigureAwait(false);
        var runRequest = new ReportingRunRequestDto(
            schedule.TemplateId,
            schedule.NextAsOfDate,
            schedule.MaxRetries,
            schedule.ScheduleId,
            actor,
            schedule.DatasetRows,
            schedule.DatasetSourceId,
            RetryReason: isDueRun
                ? $"scheduled due run for {schedule.CronExpression}"
                : $"manual schedule run requested by {triggerActor}",
            AllowRestatement: false,
            Template: schedule.Template,
            Parameters: scheduledParameters);
        var readiness = _readinessService is null
            ? throw new InvalidOperationException("Server reporting readiness is unavailable; scheduled execution is blocked.")
            : await _readinessService.AssessAsync(runRequest, effectiveAccessContext, ct).ConfigureAwait(false);
        var accessPolicy = schedule.AccessPolicySnapshot;
        CertifiedReportingRunContext? certified = null;
        if (effectiveAccessContext.RequireBoundScope)
        {
            var template = (_governedTemplateCatalog
                            ?? throw new InvalidOperationException("The governed reporting template catalog is unavailable."))
                .Get(readiness.ResolvedTemplate, effectiveAccessContext);
            template = template with { AccessPolicy = schedule.AccessPolicySnapshot };
            certified = await (_certificationService
                               ?? throw new InvalidOperationException("Reporting snapshot certification is unavailable; scheduled execution is blocked."))
                .CertifyAsync(template, readiness, effectiveAccessContext, ct)
                .ConfigureAwait(false);
        }

        var effectiveReadiness = certified?.Readiness ?? readiness;
        var isFinal = effectiveReadiness.ResolvedParameters.Finality == ReportingFinalityDto.Final;
        if (!effectiveReadiness.CanGenerateDraft || isFinal && !effectiveReadiness.CanGenerateFinal)
        {
            throw new ReportingRunReadinessBlockedException(effectiveReadiness);
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>>? datasetRows = certified is null
            ? ResolveDatasetRows(schedule, effectiveAccessContext)
            : certified.DatasetRows;
        var datasetSourceEvidence = certified is null
            ? ResolveDatasetSourceEvidence(schedule, effectiveAccessContext)
            : new DatasetSourceEvidence(
                certified.AuthoritativeSource.SourceId,
                certified.AuthoritativeSource.SourceKind);
        var contract = new ReportingJobContract(
            effectiveAccessContext.RequireBoundScope
                ? $"{effectiveAccessContext.TenantId}-{effectiveAccessContext.CompanyId}-{schedule.ScheduleId}"
                : schedule.ScheduleId,
            schedule.TemplateId,
            schedule.NextAsOfDate,
            ReportingRunTrigger.Scheduled,
            schedule.MaxRetries,
            actor,
            DateTimeOffset.UtcNow,
            schedule.CronExpression,
            schedule.ScheduleId,
            datasetRows,
            datasetSourceEvidence.SourceId,
            datasetSourceEvidence.Label,
            BrandingThemeId: ResolveBrandingThemeId(schedule),
            BrandingTheme: schedule.BrandingThemeOverride,
            AccessPolicy: accessPolicy,
            RetryReason: isDueRun
                ? $"scheduled due run for {schedule.CronExpression}"
                : $"manual schedule run requested by {triggerActor}",
            ResolvedTemplate: effectiveReadiness.ResolvedTemplate,
            ResolvedParameters: effectiveReadiness.ResolvedParameters,
            Readiness: effectiveReadiness,
            OperationalScope: certified?.OperationalScope,
            ImmutableAccessScope: certified?.AccessScope,
            CertifiedSnapshot: certified?.Snapshot,
            AuthoritativeSource: certified?.AuthoritativeSource);
        var manifest = certified is null
            ? await _orchestrationService.ExecuteAsync(contract, ct).ConfigureAwait(false)
            : ResolveReusableScheduledManifest(contract, certified)
              ?? await _orchestrationService.ExecuteAsync(contract, ct).ConfigureAwait(false);
        if (certified is not null)
        {
            var governanceCaller = BuildScheduledGovernanceCaller(
                schedule,
                certified,
                manifest.RunId);
            var coordinator = _governanceCoordinator
                ?? throw new InvalidOperationException(
                    "Canonical reporting governance is unavailable; scheduled delivery handoff creation is blocked.");
            GovernedReportingRun governedRun;
            var recoveredExistingGovernance = false;
            try
            {
                governedRun = await coordinator
                    .GetAsync(manifest.RunId, governanceCaller, ct)
                    .ConfigureAwait(false);
                recoveredExistingGovernance = true;
            }
            catch (ReportingGovernanceNotFoundException)
            {
                governedRun = await coordinator
                    .CreateFromCompletedCertifiedManifestAsync(manifest.RunId, governanceCaller, ct)
                    .ConfigureAwait(false);
            }

            ValidateScheduledGovernedRun(
                manifest,
                certified,
                governedRun,
                requireInitialDraft: !recoveredExistingGovernance);
        }

        var run = ProjectRun(
            manifest,
            manifest.OperationalScope is { } scope
                ? _orchestrationService.GetAudit(scope.TenantId, manifest.RunId)
                : _orchestrationService.GetAudit(manifest.RunId));
        var handoffs = BuildReleaseDeliveryHandoffs(schedule, manifest);
        var (nextParameters, periodClose) = await ResolvePeriodCloseAdvanceAsync(
            schedule, effectiveReadiness.ResolvedParameters, ct).ConfigureAwait(false);
        var advanced = AdvanceSchedule(schedule, manifest, nextParameters, periodClose) with
        {
            LastReadiness = effectiveReadiness,
            ReleaseDeliveryHandoffs = handoffs
        };
        var warnings = handoffs
            .Where(handoff => string.Equals(handoff.RunId, manifest.RunId, StringComparison.Ordinal)
                && handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
            .Select(handoff =>
                $"Delivery target '{handoff.TargetDistributionId}' is awaiting governed release of run '{manifest.RunId}'.")
            .ToArray();
        lock (_gate)
        {
            var identity = ReportingScheduleIdentity.From(advanced);
            var prior = _schedules.GetValueOrDefault(identity);
            if (!ReferenceEquals(prior, schedule))
            {
                throw prior is null
                    ? ReportingScheduleConcurrencyException.ForMissing(
                        advanced,
                        schedule.UpdatedAtUtc)
                    : ReportingScheduleConcurrencyException.ForConflict(
                        prior,
                        schedule.UpdatedAtUtc);
            }

            _schedules[identity] = advanced;
            try
            {
                PersistSchedule(
                    advanced,
                    schedule.UpdatedAtUtc,
                    executionLease);
            }
            catch (Exception ex)
            {
                RestoreScheduleAfterPersistenceFailure(identity, prior, ex);
                throw;
            }
        }

        return new ReportingScheduleRunResultDto(advanced, run, [], warnings);
    }

    private ReportingOutputManifest? ResolveReusableScheduledManifest(
        ReportingJobContract contract,
        CertifiedReportingRunContext certified)
    {
        var runSeriesId = $"{contract.JobId}-{contract.AsOfDate:yyyyMMdd}";
        ReportingOutputManifest? reusable = null;
        const int maximumProbeCount = 10_000;
        for (var ordinal = 1; ordinal <= maximumProbeCount; ordinal++)
        {
            var runId = ordinal == 1 ? runSeriesId : $"{runSeriesId}-v{ordinal}";
            var existing = _orchestrationService.GetManifest(runId);
            if (existing is null)
            {
                return reusable;
            }

            if (IsReusableScheduledManifest(existing, contract, certified))
            {
                reusable = existing;
            }
        }

        throw new InvalidDataException(
            $"Scheduled reporting series '{runSeriesId}' exceeds the safe restart-idempotency probe limit.");
    }

    private static bool IsReusableScheduledManifest(
        ReportingOutputManifest existing,
        ReportingJobContract contract,
        CertifiedReportingRunContext certified) =>
        existing.Status == ReportingRunStatus.Draft
            && existing.Trigger == ReportingRunTrigger.Scheduled
            && string.Equals(existing.ScheduleId, contract.ScheduleId, StringComparison.Ordinal)
            && existing.OperationalScope is not null
            && existing.ImmutableAccessScope is not null
            && existing.CertifiedSnapshot is not null
            && existing.AuthoritativeSource is not null
            && string.Equals(existing.OperationalScope.TenantId, certified.OperationalScope.TenantId, StringComparison.Ordinal)
            && string.Equals(existing.OperationalScope.CompanyId, certified.OperationalScope.CompanyId, StringComparison.Ordinal)
            && string.Equals(existing.ImmutableAccessScope.PolicyHash, certified.AccessScope.PolicyHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.CertifiedSnapshot.SnapshotId, certified.Snapshot.SnapshotId, StringComparison.Ordinal)
            && string.Equals(existing.CertifiedSnapshot.SnapshotHash, certified.Snapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.AuthoritativeSource.CheckpointId, certified.AuthoritativeSource.CheckpointId, StringComparison.Ordinal)
            && string.Equals(existing.AuthoritativeSource.CheckpointHash, certified.AuthoritativeSource.CheckpointHash, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<ReportingScheduledReleaseHandoffDto> BuildReleaseDeliveryHandoffs(
        ReportingScheduleRecordDto schedule,
        ReportingOutputManifest manifest)
    {
        var targets = schedule.DeliveryTargets ?? [];
        var existingForRun = (schedule.ReleaseDeliveryHandoffs ?? [])
            .Where(handoff => string.Equals(handoff.RunId, manifest.RunId, StringComparison.Ordinal))
            .ToDictionary(static handoff => handoff.HandoffId, StringComparer.Ordinal);
        var retained = (schedule.ReleaseDeliveryHandoffs ?? [])
            .Where(handoff => !string.Equals(handoff.RunId, manifest.RunId, StringComparison.Ordinal))
            .ToList();
        if (targets.Count == 0)
        {
            return retained;
        }
        if (!HasValidDeliveryTargetsSnapshot(schedule))
        {
            throw new InvalidDataException(
                "Scheduled delivery handoff requires a valid immutable delivery-target snapshot.");
        }

        if (manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || !string.Equals(manifest.OperationalScope.TenantId, schedule.TenantId, StringComparison.Ordinal)
            || !string.Equals(manifest.OperationalScope.CompanyId, schedule.CompanyId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Scheduled delivery handoff requires a certified run bound to the schedule tenant and company.");
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        foreach (var target in targets)
        {
            var artifactSelection = ResolveScheduledArtifactSelection(target, manifest);
            var deliveryMode = target.DeliveryMode
                ?? throw new InvalidDataException(
                    $"Scheduled distribution '{target.DistributionId}' has no immutable effective delivery mode.");
            var transportId = deliveryMode == ReportPackDeliveryModeDto.EmailLink
                ? "http-relay"
                : "secure-portal";
            var recipient = ResolveScheduledRecipientPrincipal(
                target,
                manifest.ImmutableAccessScope);
            var handoffId = BuildReleaseHandoffId(
                schedule.TenantId,
                schedule.CompanyId,
                schedule.ScheduleId,
                manifest.RunId,
                target.DistributionId,
                recipient.Kind,
                recipient.PrincipalId);
            var distributionId = $"scheduled:{handoffId}";
            var declaration = new ReportingScheduledReleaseHandoffDto(
                handoffId,
                schedule.TenantId,
                schedule.CompanyId,
                schedule.ScheduleId,
                manifest.RunId,
                manifest.TemplateId,
                distributionId,
                target.DistributionId,
                transportId,
                recipient.PrincipalId,
                string.Empty,
                $"Released Meridian report {manifest.TemplateId}",
                $"Governed scheduled report run {manifest.RunId} is available through Meridian secure reporting.",
                artifactSelection.Formats,
                artifactSelection.ArtifactIds,
                GrantLifetimeSeconds: transportId == "http-relay" ? 1_800 : null,
                GrantMaxUses: transportId == "http-relay"
                    ? artifactSelection.ArtifactIds.Count
                    : null,
                MaxAttempts: Math.Max(1, schedule.MaxRetries + 1),
                CreatedAtUtc: createdAtUtc,
                RecipientPrincipalKind: recipient.Kind.ToString(),
                DeliveryTargetsSnapshotHash: schedule.DeliveryTargetsSnapshotHash);
            if (existingForRun.TryGetValue(handoffId, out var existing))
            {
                if (!SameReleaseHandoffDeclaration(existing, declaration))
                {
                    throw new InvalidDataException(
                        $"Scheduled release handoff '{handoffId}' conflicts with its retained immutable declaration.");
                }

                retained.Add(existing);
            }
            else
            {
                retained.Add(declaration);
            }
        }

        var pending = retained
            .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease);
        var completed = retained
            .Where(static handoff => handoff.State == ReportingScheduledReleaseHandoffStateDto.Enqueued)
            .OrderByDescending(static handoff => handoff.EnqueuedAtUtc)
            .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
            .Take(200);
        return pending
            .Concat(completed)
            .OrderByDescending(static handoff => handoff.CreatedAtUtc)
            .ThenBy(static handoff => handoff.HandoffId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string>>? ResolveDatasetRows(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (schedule.DatasetRows is { Count: > 0 })
        {
            return schedule.DatasetRows;
        }

        var template = _governedTemplateCatalog?.Get(schedule.TemplateId, accessContext);
        if (_datasetSourceService is null || template?.ReportWriterGrids is not { Count: > 0 })
        {
            return schedule.DatasetRows;
        }

        var resolvedRows = _datasetSourceService.BuildDatasetRowsForSource(
            schedule.DatasetSourceId,
            accessContext);
        return resolvedRows.Count == 0 ? schedule.DatasetRows : resolvedRows;
    }

    private WorkstationReportingRunPayload ProjectRun(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            ResolveFamily(manifest.TemplateId),
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            manifest.AttemptCount,
            manifest.Sections.Length,
            manifest.Sections.Count(static section => !string.IsNullOrWhiteSpace(section.DatasetSnapshotId) && !string.IsNullOrWhiteSpace(section.ReconciliationCheckpointId)),
            manifest.Artifacts.ToArray(),
            auditTrail.Select(static audit => audit.Action).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            manifest.FailureReason,
            DrilldownLinks: [],
            NextActions: [],
            GeneratedReportWriterGrids: BuildGeneratedReportWriterGrids(manifest).ToArray(),
            ReportWriterDatasetSourceId: manifest.ReportWriterDatasetSourceId,
            ReportWriterDatasetSourceLabel: manifest.ReportWriterDatasetSourceLabel,
            ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount,
            RunSeriesId: ResolveRunSeriesId(manifest),
            RunAttemptOrdinal: ResolveRunAttemptOrdinal(manifest),
            PriorRunId: manifest.PriorRunId,
            RetryReason: manifest.RetryReason,
            LatestGeneratedRunId: manifest.RunId,
            IsLatestGenerated: true,
            ComparisonSummary: BuildRunComparisonSummary(manifest),
            ChangedLineCount: CountChangedLines(manifest),
            AddedLineCount: CountAddedLines(manifest),
            RemovedLineCount: CountRemovedLines(manifest));

    private static string ResolveRunSeriesId(ReportingOutputManifest manifest) =>
        string.IsNullOrWhiteSpace(manifest.RunSeriesId) ? manifest.RunId : manifest.RunSeriesId.Trim();

    private static int ResolveRunAttemptOrdinal(ReportingOutputManifest manifest) =>
        manifest.RunAttemptOrdinal is > 0 ? manifest.RunAttemptOrdinal.Value : 1;

    private static string? BuildRunComparisonSummary(ReportingOutputManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.PriorRunId))
        {
            return "First generated attempt in this run series.";
        }

        var changed = CountChangedLines(manifest);
        var added = CountAddedLines(manifest);
        var removed = CountRemovedLines(manifest);
        return manifest.ReportWriterGridDiffs.IsDefaultOrEmpty
            ? $"Compared with {manifest.PriorRunId}; no report-writer line comparison was retained."
            : $"{changed} changed, {added} added, {removed} removed lines compared with {manifest.PriorRunId}.";
    }

    private static int CountChangedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount);

    private static int CountAddedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.AddedRowCount);

    private static int CountRemovedLines(ReportingOutputManifest manifest) =>
        manifest.ReportWriterGridDiffs.IsDefaultOrEmpty ? 0 : manifest.ReportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount);

    private DatasetSourceEvidence ResolveDatasetSourceEvidence(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        var template = _governedTemplateCatalog?.Get(schedule.TemplateId, accessContext);
        if (template?.ReportWriterGrids is not { Count: > 0 })
        {
            return new DatasetSourceEvidence(null, null);
        }

        if (schedule.DatasetRows is { Count: > 0 })
        {
            return new DatasetSourceEvidence("custom-schedule-dataset", "Custom schedule dataset");
        }

        if (_datasetSourceService is null)
        {
            return new DatasetSourceEvidence(null, null);
        }

        var sourceId = string.IsNullOrWhiteSpace(schedule.DatasetSourceId)
            ? "retained-reporting-rows"
            : schedule.DatasetSourceId.Trim();
        return new DatasetSourceEvidence(
            sourceId,
            ReportWriterDatasetSourceService.GetKnownDatasetSourceLabel(sourceId) ?? sourceId);
    }

    private sealed record DatasetSourceEvidence(string? SourceId, string? Label);

    private IEnumerable<WorkstationGeneratedReportWriterGridPayload> BuildGeneratedReportWriterGrids(
        ReportingOutputManifest manifest)
    {
        if (!manifest.ReportWriterGrids.IsDefaultOrEmpty)
        {
            foreach (var grid in manifest.ReportWriterGrids)
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    grid.Artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
            }

            yield break;
        }

        IReadOnlyDictionary<string, ReportWriterGridDefinitionDto> templateGrids = _governedTemplateCatalog?
            .Get(manifest.TemplateId, manifest.OperationalScope)
            .ReportWriterGrids?
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ReportWriterGridDefinitionDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in manifest.Artifacts.Where(static artifact => artifact.StartsWith("report-writer://", StringComparison.OrdinalIgnoreCase)))
        {
            var gridId = ExtractReportWriterGridId(artifact);
            if (gridId is null)
            {
                continue;
            }

            if (templateGrids.TryGetValue(gridId, out var grid))
            {
                var dimensionCount = (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0);
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId.Trim(),
                    string.IsNullOrWhiteSpace(grid.Title) ? grid.GridId.Trim() : grid.Title.Trim(),
                    grid.Kind.ToString(),
                    artifact,
                    dimensionCount,
                    grid.Metrics?.Count ?? 0,
                    grid.Formulas?.Count ?? 0,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
                continue;
            }

            var generatedValidation = BuildGeneratedGridValidationSummary(gridId, manifest.RenderedReportWriterGrids);
            yield return new WorkstationGeneratedReportWriterGridPayload(
                gridId,
                gridId,
                "Generated",
                artifact,
                0,
                0,
                0,
                generatedValidation.Summary,
                generatedValidation.Passed,
                generatedValidation.Warning,
                generatedValidation.Failed);
        }
    }

    private static (string? Summary, int? Passed, int? Warning, int? Failed) BuildGeneratedGridValidationSummary(
        string gridId,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (renderedGrids.IsDefaultOrEmpty)
        {
            return (null, null, null, null);
        }

        var renderedGrid = renderedGrids.FirstOrDefault(grid =>
            string.Equals(grid.GridId, gridId, StringComparison.OrdinalIgnoreCase));
        if (renderedGrid is null)
        {
            return (null, null, null, null);
        }

        var checks = ResolveReportWriterGridValidationChecks(renderedGrid);
        if (checks.Count == 0)
        {
            return (null, null, null, null);
        }

        var passed = checks.Count(static check => string.Equals(check.Status, "Passed", StringComparison.OrdinalIgnoreCase));
        var warning = checks.Count(static check => string.Equals(check.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        var failed = checks.Count - passed - warning;
        var summary = failed > 0
            ? $"{passed} passed / {checks.Count} checks, {failed} failed"
            : warning > 0
                ? $"{passed} passed / {checks.Count} checks, {warning} review"
                : $"{passed} passed / {checks.Count} checks";
        return (summary, passed, warning, failed);
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> ResolveReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid) =>
        grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            :
            [
                new ReportWriterGridValidationCheckDto(
                    "row-count",
                    grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                    grid.Lineage is null
                        ? $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                        : $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}."),
                new ReportWriterGridValidationCheckDto(
                    "column-dictionary",
                    "Passed",
                    $"Dictionary covers {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rendered column(s)."),
                new ReportWriterGridValidationCheckDto(
                    "source-field-lineage",
                    grid.Lineage is { SourceFields.Count: > 0 } ? "Passed" : "Warning",
                    grid.Lineage is { SourceFields.Count: > 0 }
                        ? $"Lineage retains {grid.Lineage.SourceFields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} source field(s)."
                        : "No source field lineage was retained with this grid."),
                new ReportWriterGridValidationCheckDto(
                    "warnings",
                    grid.Warnings.Count == 0 ? "Passed" : "Warning",
                    grid.Warnings.Count == 0
                        ? "No render warnings were retained."
                        : $"{grid.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", grid.Warnings)}")
            ];

    private static string? ExtractReportWriterGridId(string artifact)
    {
        const string marker = "/grids/";
        var index = artifact.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var gridId = artifact[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(gridId) ? null : gridId;
    }

    private static string ResolveFamily(string templateId)
    {
        if (templateId.StartsWith("investor", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.InvestorStatement.ToString();
        if (templateId.StartsWith("sec", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.SecFilingPacket.ToString();
        if (templateId.StartsWith("shadow", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.ShadowNavPack.ToString();
        if (templateId.StartsWith("performance", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.PerformanceReport.ToString();
        if (templateId.StartsWith("holdings", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.HoldingsReport.ToString();
        if (templateId.StartsWith("capital", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.CapitalAccountStatement.ToString();
        if (templateId.StartsWith("board", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.BoardPacket.ToString();
        if (templateId.StartsWith("audit", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.AuditPackage.ToString();
        if (templateId.StartsWith("certified", StringComparison.OrdinalIgnoreCase))
            return ReportingTemplateFamily.CertifiedDataset.ToString();
        return ReportingTemplateFamily.CustomReport.ToString();
    }

    private static string? NormalizeDatasetSourceId(string? sourceId)
    {
        var normalized = sourceId?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? ResolveBrandingThemeId(ReportingScheduleRecordDto schedule) =>
        NormalizeDatasetSourceId(schedule.BrandingThemeOverride?.ThemeId) ?? NormalizeDatasetSourceId(schedule.BrandingThemeId);

    private VersionedReportTemplateIdDto? ResolveScheduleTemplateSnapshot(
        string templateId,
        VersionedReportTemplateIdDto? requestedSnapshot,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope != true)
        {
            return requestedSnapshot;
        }

        var catalog = _governedTemplateCatalog
            ?? throw new InvalidOperationException(
                "The governed reporting template catalog is unavailable; schedule version pinning is blocked.");
        if (requestedSnapshot is not null
            && !string.Equals(requestedSnapshot.Name, templateId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The reporting schedule template snapshot must match templateId.",
                nameof(requestedSnapshot));
        }

        var resolved = requestedSnapshot is null
            ? catalog.Get(templateId.Trim(), accessContext)
            : catalog.Get(requestedSnapshot, accessContext);
        var majorVersion = resolved.Version.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        if (!int.TryParse(
                majorVersion,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var version)
            || version <= 0)
        {
            throw new InvalidDataException(
                $"Approved reporting template '{resolved.TemplateId}' has no valid major version snapshot.");
        }

        return new VersionedReportTemplateIdDto(resolved.TemplateId, version);
    }

    private ReportAccessPolicyDto? ResolveTemplateAccessPolicy(
        string templateId,
        VersionedReportTemplateIdDto? templateSnapshot,
        ReportAccessQueryContext? accessContext)
    {
        if (_governedTemplateCatalog is null)
        {
            return null;
        }

        try
        {
            return templateSnapshot is null
                ? _governedTemplateCatalog.Get(templateId, accessContext).AccessPolicy
                : _governedTemplateCatalog.Get(templateSnapshot, accessContext).AccessPolicy;
        }
        catch (KeyNotFoundException) when (accessContext?.RequireBoundScope != true)
        {
            return null;
        }
    }

    private ReportAccessPolicyDto CaptureTemplateAccessPolicy(
        string templateId,
        VersionedReportTemplateIdDto? templateSnapshot,
        ReportAccessQueryContext? accessContext)
    {
        var policy = ReportAccessPolicyEvaluator.Normalize(
            ResolveTemplateAccessPolicy(templateId, templateSnapshot, accessContext));
        if (accessContext?.RequireBoundScope == true
            && !string.IsNullOrWhiteSpace(policy.CompanyId)
            && !string.Equals(policy.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule template belongs to another company scope.");
        }

        policy = policy.Mode == ReportAccessModeDto.CompanyWide
            && string.IsNullOrWhiteSpace(policy.CompanyId)
            && !string.IsNullOrWhiteSpace(accessContext?.CompanyId)
                ? policy with { CompanyId = accessContext.CompanyId.Trim() }
                : policy;
        if (accessContext?.RequireBoundScope != true)
        {
            return policy;
        }

        var actor = accessContext.ActorPrincipalId!.Trim();
        var withoutOverride = accessContext with { HasGlobalOverride = false };
        var directEvaluation = ReportAccessPolicyEvaluator.Evaluate(policy, withoutOverride);
        if (!directEvaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(
                "Reporting administrator override cannot create a scheduled execution delegation outside the immutable report audience.");
        }

        if (policy.Mode == ReportAccessModeDto.Private)
        {
            if (policy.AllowOwnerAccess
                && !string.IsNullOrWhiteSpace(policy.OwnerPrincipalId)
                && !string.Equals(policy.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "A private reporting schedule must be authored by its immutable owner.");
            }

            if (string.IsNullOrWhiteSpace(policy.OwnerPrincipalId))
            {
                policy = policy with { OwnerPrincipalId = actor };
            }
        }
        else if (policy.Mode == ReportAccessModeDto.Restricted)
        {
            var principals = (policy.Principals ?? [])
                .Append(new ReportAccessPrincipalDto(
                    ReportAccessPrincipalKindDto.User,
                    actor,
                    "Scheduled execution principal"))
                .ToArray();
            policy = policy with { Principals = principals };
        }

        policy = ReportAccessPolicyEvaluator.Normalize(policy);
        var issues = ReportAccessPolicyEvaluator.Validate(policy);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                $"The reporting schedule access snapshot is invalid: {string.Join("; ", issues)}");
        }

        return policy;
    }

    private static IReadOnlyList<ReportingScheduleDeliveryTargetDto>? NormalizeDeliveryTargets(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets)
    {
        if (targets is not { Count: > 0 })
        {
            return [];
        }

        return targets
            .Where(static target => !string.IsNullOrWhiteSpace(target.DistributionId))
            .GroupBy(static target => target.DistributionId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var target = group.First();
                ReportPackRunReadService.ReportPackDistributionPolicy policy;
                try
                {
                    policy = ReportPackRunReadService.ResolveDistributionPolicy(target.DistributionId);
                }
                catch (KeyNotFoundException ex)
                {
                    throw new ArgumentException(ex.Message, nameof(targets), ex);
                }
                if (target.DeliveryMode is { } deliveryMode && !Enum.IsDefined(deliveryMode))
                {
                    throw new ArgumentException(
                        "A reporting schedule delivery target contains an unknown delivery mode.",
                        nameof(targets));
                }
                if (target.RecipientPrincipalKind is { } principalKind && !Enum.IsDefined(principalKind))
                {
                    throw new ArgumentException(
                        "A reporting schedule delivery target contains an unknown recipient principal kind.",
                        nameof(targets));
                }

                return new ReportingScheduleDeliveryTargetDto(
                    policy.DistributionId,
                    NormalizeDeliveryFormats(target.Formats),
                    target.DeliveryMode ?? ResolveScheduledDeliveryMode(policy.Channel),
                    string.IsNullOrWhiteSpace(target.Note) ? null : target.Note.Trim(),
                    string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                        ? null
                        : target.RecipientPrincipalId.Trim(),
                    target.RecipientPrincipalKind);
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportingScheduleDeliveryTargetDto>? ResolveAndValidateDeliveryRecipients(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets,
        ReportAccessPolicyDto accessPolicy,
        ReportAccessQueryContext? accessContext,
        bool requireExplicitRecipient,
        bool allowLegacyInference)
    {
        if (targets is not { Count: > 0 })
        {
            return targets;
        }

        var policy = ReportAccessPolicyEvaluator.Normalize(accessPolicy);
        var allowed = ResolveNamedDeliveryPrincipals(policy);
        return targets
            .Select(target =>
            {
                var principalId = string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                    ? null
                    : target.RecipientPrincipalId.Trim();
                var principalKind = target.RecipientPrincipalKind;
                if (principalKind is { } kind && !Enum.IsDefined(kind))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(targets),
                        "Scheduled delivery recipient principal kind is invalid.");
                }

                ReportAccessPrincipalDto? recipient = null;
                if (principalId is not null && principalKind is not null)
                {
                    recipient = new ReportAccessPrincipalDto(principalKind.Value, principalId);
                }
                else if (principalId is not null
                         && principalKind is null
                         && allowLegacyInference
                         && policy.Mode != ReportAccessModeDto.CompanyWide)
                {
                    var matches = allowed
                        .Where(candidate => string.Equals(
                            candidate.PrincipalId,
                            principalId,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    recipient = matches.Length switch
                    {
                        1 => matches[0],
                        > 1 => throw new ArgumentException(
                            $"Scheduled distribution '{target.DistributionId}' recipient kind is required because the identifier appears in multiple principal namespaces.",
                            nameof(targets)),
                        _ => throw new UnauthorizedAccessException(
                            $"Scheduled distribution '{target.DistributionId}' recipient is outside the immutable access policy.")
                    };
                }
                else if (principalId is null && principalKind is null)
                {
                    if (!requireExplicitRecipient)
                    {
                        return target with
                        {
                            RecipientPrincipalId = null,
                            RecipientPrincipalKind = null
                        };
                    }
                    if (policy.Mode == ReportAccessModeDto.CompanyWide)
                    {
                        throw new ArgumentException(
                            $"Company-wide scheduled distribution '{target.DistributionId}' requires an explicit typed recipient.",
                            nameof(targets));
                    }
                    if (!allowLegacyInference || allowed.Count != 1)
                    {
                        throw new ArgumentException(
                            $"Scheduled distribution '{target.DistributionId}' requires an explicit typed recipient because its immutable access policy is ambiguous.",
                            nameof(targets));
                    }

                    recipient = allowed[0];
                }
                else
                {
                    throw new ArgumentException(
                        $"Scheduled distribution '{target.DistributionId}' requires both recipient principal id and kind.",
                        nameof(targets));
                }

                ValidateDeliveryRecipient(policy, accessContext, recipient!, target.DistributionId);
                return target with
                {
                    RecipientPrincipalId = recipient!.PrincipalId,
                    RecipientPrincipalKind = recipient.Kind
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportAccessPrincipalDto> ResolveNamedDeliveryPrincipals(
        ReportAccessPolicyDto policy) =>
        (policy.Principals ?? [])
        .Concat(policy.AllowOwnerAccess && !string.IsNullOrWhiteSpace(policy.OwnerPrincipalId)
            ? [new ReportAccessPrincipalDto(
                ReportAccessPrincipalKindDto.User,
                policy.OwnerPrincipalId.Trim())]
            : [])
        .GroupBy(
            static principal => $"{(int)principal.Kind}:{principal.PrincipalId.Trim()}",
            StringComparer.OrdinalIgnoreCase)
        .Select(static group =>
        {
            var principal = group.First();
            return principal with { PrincipalId = principal.PrincipalId.Trim() };
        })
        .OrderBy(static principal => principal.Kind)
        .ThenBy(static principal => principal.PrincipalId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void ValidateDeliveryRecipient(
        ReportAccessPolicyDto policy,
        ReportAccessQueryContext? accessContext,
        ReportAccessPrincipalDto recipient,
        string distributionId)
    {
        if (policy.Mode != ReportAccessModeDto.CompanyWide)
        {
            var allowed = ResolveNamedDeliveryPrincipals(policy);
            if (!allowed.Any(candidate =>
                    candidate.Kind == recipient.Kind
                    && string.Equals(
                        candidate.PrincipalId,
                        recipient.PrincipalId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException(
                    $"Scheduled distribution '{distributionId}' recipient is outside the immutable access policy.");
            }

            return;
        }

        var authorityMatches = recipient.Kind switch
        {
            ReportAccessPrincipalKindDto.User =>
                string.Equals(
                    accessContext?.ActorPrincipalId,
                    recipient.PrincipalId,
                    StringComparison.OrdinalIgnoreCase),
            ReportAccessPrincipalKindDto.Group =>
                (accessContext?.GroupPrincipalIds ?? []).Contains(
                    recipient.PrincipalId,
                    StringComparer.OrdinalIgnoreCase),
            ReportAccessPrincipalKindDto.Company =>
                string.Equals(
                    accessContext?.CompanyId,
                    recipient.PrincipalId,
                    StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(policy.CompanyId)
                    || string.Equals(
                        policy.CompanyId,
                        recipient.PrincipalId,
                        StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        if (!authorityMatches)
        {
            throw new UnauthorizedAccessException(
                $"Company-wide scheduled distribution '{distributionId}' recipient must be in the authenticated principal scope.");
        }
    }

    private void EnsureSynchronousUpsertCanCommit(
        ReportingScheduleRecordDto candidate,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope != true)
        {
            return;
        }

        var externalTargets = (candidate.DeliveryTargets ?? [])
            .Where(IsExternalDeliveryTarget)
            .ToArray();
        if (externalTargets.Length == 0)
        {
            return;
        }
        if (candidate.State == ReportingScheduleStateDto.Active
            || externalTargets.Any(static target =>
                !string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                || target.RecipientPrincipalKind is not null))
        {
            throw new InvalidOperationException(
                "External scheduled delivery must use the asynchronous upsert path so its exact recipient directory binding is verified before persistence.");
        }
    }

    private async Task ValidateExternalDeliveryTargetsAsync(
        ReportingScheduleRecordDto candidate,
        CancellationToken cancellationToken)
    {
        var externalTargets = (candidate.DeliveryTargets ?? [])
            .Where(IsExternalDeliveryTarget)
            .ToArray();
        if (externalTargets.Length == 0)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(candidate.TenantId)
            || string.IsNullOrWhiteSpace(candidate.CompanyId))
        {
            throw new InvalidDataException(
                "External scheduled delivery requires immutable tenant and company scope.");
        }
        if (!_destinationResolver.IsConfigured)
        {
            throw new InvalidOperationException(
                "External scheduled delivery is disabled until a tenant-bound recipient destination directory is configured.");
        }

        foreach (var target in externalTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                || target.RecipientPrincipalKind is null)
            {
                throw new InvalidDataException(
                    $"External scheduled distribution '{target.DistributionId}' requires an explicit typed recipient.");
            }

            var destination = await _destinationResolver.ResolveDestinationAsync(
                    new ReportingRecipientDestinationRequest(
                        candidate.TenantId,
                        candidate.CompanyId,
                        target.RecipientPrincipalId,
                        "http-relay",
                        ToReportingPrincipalKind(target.RecipientPrincipalKind.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(destination))
            {
                throw new UnauthorizedAccessException(
                    $"Scheduled distribution '{target.DistributionId}' recipient has no exact server-owned destination binding for http-relay.");
            }
        }
    }

    private static bool IsExternalDeliveryTarget(ReportingScheduleDeliveryTargetDto target) =>
        target.DeliveryMode switch
        {
            ReportPackDeliveryModeDto.EmailLink => true,
            ReportPackDeliveryModeDto.SecurePortal
                or ReportPackDeliveryModeDto.EvidenceVault
                or ReportPackDeliveryModeDto.InternalRoute => false,
            _ => throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' has no immutable effective delivery mode.")
        };

    private static ReportingAccessPrincipalKind ToReportingPrincipalKind(
        ReportAccessPrincipalKindDto kind) =>
        kind switch
        {
            ReportAccessPrincipalKindDto.User => ReportingAccessPrincipalKind.User,
            ReportAccessPrincipalKindDto.Group => ReportingAccessPrincipalKind.Group,
            ReportAccessPrincipalKindDto.Company => ReportingAccessPrincipalKind.Company,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static IReadOnlyList<GovernanceReportArtifactFormatDto>? NormalizeDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? formats)
    {
        if (formats is not { Count: > 0 })
        {
            return null;
        }
        if (formats.Any(static format => !Enum.IsDefined(format)))
        {
            throw new ArgumentException(
                "A reporting schedule delivery target contains an unknown artifact format.",
                nameof(formats));
        }

        return formats
            .Distinct()
            .ToArray();
    }

    private static void ValidateScheduledDeliveryFormatAvailability(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets,
        ReportingRunParametersDto parameters)
    {
        GovernanceReportArtifactFormatDto[] primaryFormats = parameters.OutputFormat switch
        {
            ReportingOutputFormatDto.Pdf => [GovernanceReportArtifactFormatDto.Pdf],
            ReportingOutputFormatDto.Xlsx => [GovernanceReportArtifactFormatDto.Xlsx],
            ReportingOutputFormatDto.Csv => [GovernanceReportArtifactFormatDto.Csv],
            ReportingOutputFormatDto.EvidenceVault => [GovernanceReportArtifactFormatDto.Json],
            ReportingOutputFormatDto.ClientPackage =>
                [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx],
            _ => throw new InvalidDataException(
                $"Reporting output format '{parameters.OutputFormat}' is unavailable for scheduled delivery.")
        };
        foreach (var target in targets ?? [])
        {
            var requested = (target.Formats ?? [])
                .Distinct()
                .ToArray();
            if (requested.Length > 0
                && (requested.Length != primaryFormats.Length
                    || requested.Except(primaryFormats).Any()))
            {
                throw new InvalidDataException(
                    $"Scheduled distribution '{target.DistributionId}' requests unavailable output format(s); the exact primary outputs are {string.Join(", ", primaryFormats)}.");
            }
        }
    }

    private void EnsureTemplateAccess(string templateId, ReportAccessQueryContext? accessContext)
    {
        var accessEvaluation = _governedTemplateCatalog?.EvaluateAccess(templateId, accessContext)
            ?? ReportAccessPolicyEvaluator.Evaluate((ReportAccessPolicyDto?)null, accessContext);
        if (!accessEvaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(accessEvaluation.Reason);
        }
    }

    private static void EnsureScheduleAccess(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope == true && !HasValidAccessPolicySnapshot(schedule))
        {
            throw new UnauthorizedAccessException(
                "The reporting schedule has no valid immutable access-policy snapshot.");
        }

        var evaluation = ReportAccessPolicyEvaluator.Evaluate(schedule.AccessPolicySnapshot, accessContext);
        if (!evaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(evaluation.Reason);
        }
    }

    private static bool IsScheduleInScope(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            // Compatibility callers without an authority scope may only see legacy unscoped
            // schedules. Returning every tenant's schedules here would turn an internal overload
            // into a cross-tenant list operation.
            return string.IsNullOrWhiteSpace(schedule.TenantId)
                && string.IsNullOrWhiteSpace(schedule.CompanyId);
        }

        var hasTenant = !string.IsNullOrWhiteSpace(accessContext.TenantId);
        var hasCompany = !string.IsNullOrWhiteSpace(accessContext.CompanyId);
        if (accessContext.RequireBoundScope
            && (string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId) || !hasTenant || !hasCompany))
        {
            return false;
        }

        if (hasTenant && hasCompany)
        {
            if (string.IsNullOrWhiteSpace(schedule.TenantId) && string.IsNullOrWhiteSpace(schedule.CompanyId))
            {
                return !accessContext.RequireBoundScope;
            }

            return string.Equals(schedule.TenantId, accessContext.TenantId, StringComparison.Ordinal)
                && string.Equals(schedule.CompanyId, accessContext.CompanyId, StringComparison.Ordinal);
        }

        return !accessContext.RequireBoundScope
            && string.IsNullOrWhiteSpace(schedule.TenantId)
            && string.IsNullOrWhiteSpace(schedule.CompanyId);
    }

    private static void EnsureScheduleInScope(
        ReportingScheduleRecordDto schedule,
        ReportAccessQueryContext? accessContext)
    {
        if (!IsScheduleInScope(schedule, accessContext))
        {
            throw new UnauthorizedAccessException("The reporting schedule is outside the current tenant and company scope.");
        }
    }

    private static void EnsureBoundContext(ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null || !accessContext.RequireBoundScope)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(accessContext.TenantId)
            || string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            throw new UnauthorizedAccessException("A bound actor, tenant, and company scope is required for reporting schedules.");
        }
    }

    private static ReportingScheduleIdentity BuildLookupIdentity(
        string scheduleId,
        ReportAccessQueryContext? accessContext)
    {
        var hasTenant = !string.IsNullOrWhiteSpace(accessContext?.TenantId);
        var hasCompany = !string.IsNullOrWhiteSpace(accessContext?.CompanyId);
        if (hasTenant != hasCompany)
        {
            throw new UnauthorizedAccessException(
                "A reporting schedule lookup requires tenant and company scope together.");
        }

        return ReportingScheduleIdentity.Create(
            hasTenant ? accessContext!.TenantId : null,
            hasCompany ? accessContext!.CompanyId : null,
            scheduleId);
    }

    private ReportingScheduleIdentity ResolveExistingIdentity(
        string scheduleId,
        ReportAccessQueryContext? accessContext)
    {
        EnsureBoundContext(accessContext);
        var identity = BuildLookupIdentity(scheduleId.Trim(), accessContext);
        if (_schedules.ContainsKey(identity))
        {
            return identity;
        }

        throw new KeyNotFoundException("reporting schedule not found");
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static DateTimeOffset NextRevisionTimestamp(
        DateTimeOffset retainedRevision,
        DateTimeOffset candidateRevision)
    {
        var retainedUtc = retainedRevision.ToUniversalTime();
        var candidateUtc = candidateRevision.ToUniversalTime();
        if (candidateUtc > retainedUtc)
        {
            if (candidateUtc == DateTimeOffset.MaxValue)
            {
                throw new InvalidDataException(
                    "The reporting schedule revision timestamp cannot advance to DateTimeOffset.MaxValue.");
            }

            return candidateUtc;
        }
        if (retainedUtc == DateTimeOffset.MaxValue)
        {
            throw new InvalidDataException(
                "The reporting schedule revision timestamp cannot advance beyond DateTimeOffset.MaxValue.");
        }

        var nextRevision = retainedUtc.AddTicks(1);
        if (nextRevision == DateTimeOffset.MaxValue)
        {
            throw new InvalidDataException(
                "The reporting schedule revision timestamp cannot advance to DateTimeOffset.MaxValue.");
        }

        return nextRevision;
    }

    internal static bool HasValidAccessPolicySnapshot(ReportingScheduleRecordDto schedule)
    {
        if (schedule.AccessPolicySnapshot is null
            || string.IsNullOrWhiteSpace(schedule.AccessPolicySnapshotHash))
        {
            return false;
        }

        byte[] retained;
        try
        {
            retained = Convert.FromHexString(schedule.AccessPolicySnapshotHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = Sha256Digest.ComputeBytesUtf8(JsonSerializer.Serialize(ReportAccessPolicyEvaluator.Normalize(schedule.AccessPolicySnapshot)));
        return retained.Length == computed.Length
            && CryptographicOperations.FixedTimeEquals(retained, computed);
    }

    internal static bool HasValidScheduledExecutionPrincipal(ReportingScheduleRecordDto schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            && string.IsNullOrWhiteSpace(schedule.CompanyId))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || string.IsNullOrWhiteSpace(schedule.RequestedBy)
            || schedule.AccessPolicySnapshot is null)
        {
            return false;
        }

        var actor = schedule.RequestedBy.Trim();
        var policy = ReportAccessPolicyEvaluator.Normalize(schedule.AccessPolicySnapshot);
        return policy.Mode switch
        {
            ReportAccessModeDto.Private =>
                (policy.AllowOwnerAccess
                    && string.Equals(policy.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
                || (policy.Principals ?? []).Any(principal =>
                    principal.Kind == ReportAccessPrincipalKindDto.User
                    && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase)),
            ReportAccessModeDto.Restricted =>
                (policy.AllowOwnerAccess
                    && string.Equals(policy.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
                || (policy.Principals ?? []).Any(principal =>
                    principal.Kind == ReportAccessPrincipalKindDto.User
                    && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase)),
            ReportAccessModeDto.CompanyWide =>
                string.Equals(policy.CompanyId, schedule.CompanyId, StringComparison.Ordinal),
            _ => false
        };
    }

    internal static string ComputeAccessPolicySnapshotHash(ReportAccessPolicyDto policy) =>
        Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(ReportAccessPolicyEvaluator.Normalize(policy)));

    internal static string? ComputeDeliveryTargetsSnapshotHash(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets)
    {
        if (targets is not { Count: > 0 })
        {
            return null;
        }

        var canonical = targets
            .Select(static target => target with
            {
                DistributionId = target.DistributionId.Trim(),
                Formats = (target.Formats ?? [])
                    .Distinct()
                    .OrderBy(static format => format)
                    .ToArray(),
                Note = string.IsNullOrWhiteSpace(target.Note) ? null : target.Note.Trim(),
                RecipientPrincipalId = string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                    ? null
                    : target.RecipientPrincipalId.Trim()
            })
            .OrderBy(static target => target.DistributionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(canonical));
    }

    internal static bool HasValidDeliveryTargetsSnapshot(ReportingScheduleRecordDto schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.DeliveryTargets is not { Count: > 0 })
        {
            return string.IsNullOrWhiteSpace(schedule.DeliveryTargetsSnapshotHash);
        }
        if (string.IsNullOrWhiteSpace(schedule.DeliveryTargetsSnapshotHash))
        {
            return false;
        }

        byte[] retained;
        try
        {
            retained = Convert.FromHexString(schedule.DeliveryTargetsSnapshotHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = Convert.FromHexString(ComputeDeliveryTargetsSnapshotHash(schedule.DeliveryTargets)!);
        return retained.Length == computed.Length
               && CryptographicOperations.FixedTimeEquals(retained, computed);
    }

    private void RestoreScheduleAfterPersistenceFailure(
        ReportingScheduleIdentity identity,
        ReportingScheduleRecordDto? fallback,
        Exception exception)
    {
        if (exception is ReportingScheduleConcurrencyException && _store is not null)
        {
            try
            {
                var retained = _store.Load().SingleOrDefault(schedule =>
                    ReportingScheduleIdentityComparer.Instance.Equals(
                        ReportingScheduleIdentity.From(schedule),
                        identity));
                if (retained is null)
                {
                    _schedules.Remove(identity);
                }
                else
                {
                    _schedules[identity] = retained;
                }

                return;
            }
            catch
            {
                // Preserve the original concurrency failure and use the last known local value.
            }
        }

        if (fallback is null)
        {
            _schedules.Remove(identity);
        }
        else
        {
            _schedules[identity] = fallback;
        }
    }
}
