using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Core.Scheduling;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportingScheduleService
{
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExecutionLeaseRenewalInterval = TimeSpan.FromSeconds(30);

    private readonly string _executionLeaseOwner =
        $"reporting-schedule:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly IReportingDeploymentReadinessService? _deploymentReadinessService;

    private void EnsureReportingDeploymentReady(
        string operation,
        bool allowSchedulingWorkerBootstrap = false)
    {
        if (_deploymentReadinessService is null)
        {
            if (_store?.IsDurableAuthority == true)
            {
                throw new InvalidOperationException(
                    $"Cannot {operation}: a durable reporting schedule authority requires the " +
                    "reporting deployment readiness gate.");
            }

            return;
        }

        var blockers = allowSchedulingWorkerBootstrap
            ? _deploymentReadinessService.GetScheduleWorkerCycleBlockingReasons()
            : ReportingDeploymentReadinessService.ResolveCapabilityBlockingReasons(
                _deploymentReadinessService.Evaluate());
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} until the authoritative reporting deployment is ready: " +
                string.Join(" ", blockers));
        }
    }

    private void PersistSchedule(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset? expectedUpdatedAtUtc,
        ReportingScheduleExecutionLease? executionLease = null)
    {
        if (_store is null)
        {
            return;
        }
        if (executionLease is not null)
        {
            if (expectedUpdatedAtUtc is null)
            {
                throw new InvalidOperationException(
                    "A leased reporting schedule update requires the expected retained revision.");
            }

            _store.UpsertClaimedExecution(
                schedule,
                expectedUpdatedAtUtc.Value,
                executionLease);
            return;
        }

        _store.Upsert(schedule, expectedUpdatedAtUtc);
    }

    private void RefreshSchedulesFromStore()
    {
        if (_store is null)
        {
            return;
        }

        lock (_gate)
        {
            var retained = _store.Load();
            var refreshed = new Dictionary<ReportingScheduleIdentity, ReportingScheduleRecordDto>(
                ReportingScheduleIdentityComparer.Instance);
            foreach (var schedule in retained)
            {
                var identity = ReportingScheduleIdentity.From(schedule);
                ValidateRetainedSchedule(schedule, identity);
                if (!refreshed.TryAdd(
                        identity,
                        _schedules.TryGetValue(identity, out var current)
                        && (EqualityComparer<ReportingScheduleRecordDto>.Default.Equals(
                                current,
                                schedule)
                            || _dueRunClaims.Contains(identity))
                            ? current
                            : schedule))
                {
                    throw new InvalidDataException(
                        $"Duplicate reporting schedule identity '{identity.TenantId}/{identity.CompanyId}/{identity.ScheduleId}'.");
                }
            }

            foreach (var identity in _dueRunClaims)
            {
                if (!refreshed.ContainsKey(identity)
                    && _schedules.TryGetValue(identity, out var inFlight))
                {
                    refreshed[identity] = inFlight;
                }
            }

            _schedules.Clear();
            foreach (var pair in refreshed)
            {
                _schedules.Add(pair.Key, pair.Value);
            }
        }
    }

    private static void ValidateRetainedSchedule(
        ReportingScheduleRecordDto schedule,
        ReportingScheduleIdentity identity)
    {
        if (!ReportingScheduleTimeZones.TryResolve(schedule.TimeZoneId, out var timeZone)
            || !CronExpressionParser.TryParse(schedule.CronExpression, out var cronSchedule)
            || cronSchedule.GetNextOccurrenceOrNull(schedule.DueAtUtc, timeZone) is null)
        {
            throw new InvalidDataException(
                $"Reporting schedule '{schedule.ScheduleId}' has an invalid cron expression or time zone, or no future occurrence.");
        }

        if (identity.TenantId.Length > 0
            && (!HasValidAccessPolicySnapshot(schedule)
                || !HasValidScheduledExecutionPrincipal(schedule)))
        {
            throw new InvalidDataException(
                $"Reporting schedule '{schedule.ScheduleId}' has no valid immutable access-policy or execution-principal snapshot.");
        }
    }

    private ReportingScheduleExecutionLease? TryAcquireExecutionLease(
        ReportingScheduleIdentity identity,
        ReportingScheduleRecordDto schedule,
        DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_dueRunClaims.Add(identity))
            {
                return null;
            }
        }

        try
        {
            var lease = _store?.TryClaimExecution(
                    schedule,
                    _executionLeaseOwner,
                    nowUtc.ToUniversalTime(),
                    ExecutionLeaseDuration)
                ?? (_store is null
                    ? new ReportingScheduleExecutionLease(
                        _executionLeaseOwner,
                        nowUtc.ToUniversalTime().Add(ExecutionLeaseDuration),
                        LeaseVersion: 1)
                    : null);
            if (lease is not null)
            {
                return lease;
            }
        }
        catch
        {
            lock (_gate)
            {
                _dueRunClaims.Remove(identity);
            }

            throw;
        }

        lock (_gate)
        {
            _dueRunClaims.Remove(identity);
        }

        return null;
    }

    private void ReleaseExecutionLease(
        ReportingScheduleIdentity identity,
        ReportingScheduleExecutionLease lease)
    {
        try
        {
            _store?.ReleaseExecutionLease(
                identity.TenantId,
                identity.CompanyId,
                identity.ScheduleId,
                lease);
        }
        finally
        {
            lock (_gate)
            {
                _dueRunClaims.Remove(identity);
            }
        }
    }

    private async Task<ReportingScheduleRunResultDto> RunScheduleWithLeaseAsync(
        ReportingScheduleRecordDto schedule,
        string? requestedBy,
        bool isDueRun,
        ReportAccessQueryContext? accessContext,
        ReportingScheduleExecutionLease lease,
        CancellationToken ct)
    {
        if (_store is null)
        {
            return await RunScheduleAsync(
                        schedule,
                        requestedBy,
                        isDueRun,
                        accessContext,
                        ct,
                        lease)
                .ConfigureAwait(false);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaseLost = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var heartbeat = MaintainExecutionLeaseAsync(
            schedule,
            lease,
            linked,
            leaseLost);
        try
        {
            var run = RunScheduleAsync(
                schedule,
                requestedBy,
                isDueRun,
                accessContext,
                linked.Token,
                lease);
            var completed = await Task.WhenAny(run, leaseLost.Task).ConfigureAwait(false);
            if (completed == leaseLost.Task)
            {
                var failure = await leaseLost.Task.ConfigureAwait(false);
                try
                {
                    await run.ConfigureAwait(false);
                }
                catch
                {
                    // The durable lease failure remains authoritative. Draining the in-flight run
                    // prevents an unobserved, non-cancellable continuation from escaping this owner.
                }

                throw failure;
            }

            return await run.ConfigureAwait(false);
        }
        finally
        {
            linked.Cancel();
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
            }
        }
    }

    private async Task MaintainExecutionLeaseAsync(
        ReportingScheduleRecordDto schedule,
        ReportingScheduleExecutionLease initialLease,
        CancellationTokenSource linked,
        TaskCompletionSource<Exception> leaseLost)
    {
        var lease = initialLease;
        try
        {
            while (true)
            {
                await Task.Delay(
                        ExecutionLeaseRenewalInterval,
                        linked.Token)
                    .ConfigureAwait(false);
                lease = _store!.RenewExecutionLease(
                        schedule,
                        lease,
                        DateTimeOffset.UtcNow,
                        ExecutionLeaseDuration)
                    ?? throw new ReportingScheduleExecutionLeaseException(
                        schedule.TenantId ?? string.Empty,
                        schedule.CompanyId ?? string.Empty,
                        schedule.ScheduleId,
                        "The reporting schedule execution lease expired or was superseded.");
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            leaseLost.TrySetResult(exception);
            linked.Cancel();
        }
    }

    private static ReportingScheduleRecordDto AdvanceSchedule(
        ReportingScheduleRecordDto schedule,
        ReportingOutputManifest manifest,
        ReportingRunParametersDto resolvedParameters)
    {
        if (!ReportingScheduleTimeZones.TryResolve(schedule.TimeZoneId, out var timeZone))
        {
            throw new InvalidDataException(
                $"Reporting schedule time zone '{schedule.TimeZoneId}' is not recognized.");
        }

        DateTimeOffset nextDue;
        DateOnly nextAsOfDate;
        if (schedule.PeriodCadence is { } periodCadence)
        {
            // Period-close schedules advance by accounting period (shared with the ledger's
            // report planner) rather than by cron; the cron expression is retained for display.
            var occurrence = ReportingSchedulePeriodPlanner.Next(
                periodCadence,
                schedule.NextAsOfDate,
                schedule.DueAtUtc,
                timeZone);
            nextDue = occurrence.DueAtUtc;
            nextAsOfDate = occurrence.PeriodEnd;
        }
        else
        {
            nextDue = ResolveNextDue(schedule.CronExpression, schedule.DueAtUtc, schedule.TimeZoneId);
            // Count days on the schedule's local calendar. Two consecutive local days can share
            // one UTC date (or sit two apart) across a daylight-saving change, so UTC dates
            // would repeat or skip a report date.
            var localDue = TimeZoneInfo.ConvertTime(schedule.DueAtUtc, timeZone).Date;
            var localNextDue = TimeZoneInfo.ConvertTime(nextDue, timeZone).Date;
            nextAsOfDate = schedule.NextAsOfDate.AddDays((localNextDue - localDue).Days);
        }

        var runAtUtc = DateTimeOffset.UtcNow;
        return schedule with
        {
            DueAtUtc = nextDue,
            NextAsOfDate = nextAsOfDate,
            // A period-close schedule moves to a new accounting period each run, so its period
            // id moves too (yyyy-MM of the period end, the convention the workstation uses when
            // an operator changes the report period). Cron schedules keep their retained id.
            RunParameters = schedule.PeriodCadence is null
                ? resolvedParameters with { AsOfDate = nextAsOfDate }
                : resolvedParameters with
                {
                    AsOfDate = nextAsOfDate,
                    PeriodId = nextAsOfDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                },
            UpdatedAtUtc = NextRevisionTimestamp(schedule.UpdatedAtUtc, runAtUtc),
            LastRunAtUtc = runAtUtc,
            LastRunId = manifest.RunId,
            RunCount = schedule.RunCount + 1
        };
    }

    private static DateTimeOffset ResolveNextDue(
        string cronExpression,
        DateTimeOffset dueAtUtc,
        string? timeZoneId)
    {
        if (!CronExpressionParser.TryParse(cronExpression, out var schedule))
        {
            throw new InvalidDataException(
                $"Reporting schedule cron expression '{cronExpression}' is invalid.");
        }

        if (!ReportingScheduleTimeZones.TryResolve(timeZoneId, out var timeZone))
        {
            throw new InvalidDataException(
                $"Reporting schedule time zone '{timeZoneId}' is not recognized.");
        }

        // The cron expression is evaluated in the schedule's time zone; the due instant is
        // always retained in UTC.
        return (schedule.GetNextOccurrenceOrNull(dueAtUtc.ToUniversalTime(), timeZone)
            ?? throw new InvalidDataException(
                $"Reporting schedule cron expression '{cronExpression}' has no future occurrence."))
            .ToUniversalTime();
    }

    /// <summary>
    /// Permanently removes a schedule. Runs it already produced are retained by the run store;
    /// only the recurring definition is deleted. A schedule that is executing on this host is
    /// refused so an in-flight run cannot resurrect or lose its advance.
    /// </summary>
    public ReportingScheduleRecordDto Delete(
        string scheduleId,
        ReportAccessQueryContext? accessContext)
    {
        EnsureReportingDeploymentReady("delete a reporting schedule");
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            var identity = ResolveExistingIdentity(scheduleId, accessContext);
            var current = _schedules[identity];

            EnsureScheduleInScope(current, accessContext);
            if (_store is not null
                && (identity.TenantId.Length == 0 || identity.CompanyId.Length == 0))
            {
                throw new InvalidDataException(
                    "A legacy reporting schedule without tenant and company scope cannot be deleted; disable it instead.");
            }

            // Deleting takes the same durable execution lease a run takes. The store grants it
            // only when no other host holds an unexpired lease on this revision, so a schedule
            // that is executing anywhere (not just on this host) cannot be deleted mid-run.
            var lease = TryAcquireExecutionLease(identity, current, DateTimeOffset.UtcNow);
            if (lease is null)
            {
                RefreshSchedulesFromStore();
                if (!_schedules.TryGetValue(identity, out var retained))
                {
                    throw new KeyNotFoundException(
                        $"Reporting schedule '{current.ScheduleId}' no longer exists.");
                }

                if (retained.UpdatedAtUtc != current.UpdatedAtUtc)
                {
                    throw ReportingScheduleConcurrencyException.ForConflict(retained, current.UpdatedAtUtc);
                }

                throw new ReportingScheduleExecutionLeaseException(
                    identity.TenantId,
                    identity.CompanyId,
                    current.ScheduleId,
                    "The reporting schedule is executing and cannot be deleted until the run completes.");
            }

            if (_store is not null)
            {
                bool deleted;
                try
                {
                    deleted = _store.Delete(
                        identity.TenantId,
                        identity.CompanyId,
                        current.ScheduleId,
                        current.UpdatedAtUtc);
                }
                catch (Exception ex)
                {
                    // Same recovery as a failed state change: a revision conflict reloads the
                    // retained row so the caller can retry against the current revision.
                    ReleaseExecutionLease(identity, lease);
                    RestoreScheduleAfterPersistenceFailure(identity, current, ex);
                    throw;
                }

                if (!deleted)
                {
                    _dueRunClaims.Remove(identity);
                    _schedules.Remove(identity);
                    throw new KeyNotFoundException(
                        $"Reporting schedule '{current.ScheduleId}' no longer exists.");
                }
            }

            // The retained row, and with it the durable lease, is gone; only the in-process
            // claim remains to clear.
            _dueRunClaims.Remove(identity);
            _schedules.Remove(identity);
            return current;
        }
    }

    private static void ValidateCronExpressionForUpsert(
        string cronExpression,
        DateTimeOffset dueAtUtc,
        string? timeZoneId)
    {
        if (!CronExpressionParser.TryParse(cronExpression, out var schedule))
        {
            throw new ArgumentException(
                $"Invalid reporting schedule cron expression: {cronExpression}",
                nameof(cronExpression));
        }

        if (!ReportingScheduleTimeZones.TryResolve(timeZoneId, out var timeZone))
        {
            throw new ArgumentException(
                $"Unknown reporting schedule time zone: {timeZoneId}",
                nameof(timeZoneId));
        }

        if (schedule.GetNextOccurrenceOrNull(dueAtUtc.ToUniversalTime(), timeZone) is null)
        {
            throw new ArgumentException(
                "The reporting schedule cron expression has no future occurrence within the supported calendar horizon.",
                nameof(cronExpression));
        }
    }

}

/// <summary>
/// Resolves the optional time zone a reporting schedule's cron expression is evaluated in.
/// A missing identifier means UTC, and UTC is always retained as <see langword="null"/>.
/// </summary>
internal static class ReportingScheduleTimeZones
{
    public static bool TryResolve(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Utc;
            return true;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var resolved))
        {
            timeZone = resolved;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    /// <summary>Returns the identifier to retain: null for UTC, the trimmed id otherwise.</summary>
    public static string? Normalize(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        var trimmed = timeZoneId.Trim();
        return trimmed is "UTC" or "Etc/UTC" or "Z" ? null : trimmed;
    }
}
