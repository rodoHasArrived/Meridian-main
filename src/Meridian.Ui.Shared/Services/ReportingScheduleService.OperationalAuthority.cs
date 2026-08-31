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
        if (!CronExpressionParser.TryParse(schedule.CronExpression, out var cronSchedule)
            || cronSchedule.GetNextOccurrenceOrNull(schedule.DueAtUtc, TimeZoneInfo.Utc) is null)
        {
            throw new InvalidDataException(
                $"Reporting schedule '{schedule.ScheduleId}' has an invalid cron expression or no future UTC occurrence.");
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
        var nextDue = ResolveNextDue(schedule.CronExpression, schedule.DueAtUtc);
        var nextAsOfDate = schedule.NextAsOfDate.AddDays(
            (nextDue.Date - schedule.DueAtUtc.Date).Days);
        var runAtUtc = DateTimeOffset.UtcNow;
        return schedule with
        {
            DueAtUtc = nextDue,
            NextAsOfDate = nextAsOfDate,
            RunParameters = resolvedParameters with { AsOfDate = nextAsOfDate },
            UpdatedAtUtc = NextRevisionTimestamp(schedule.UpdatedAtUtc, runAtUtc),
            LastRunAtUtc = runAtUtc,
            LastRunId = manifest.RunId,
            RunCount = schedule.RunCount + 1
        };
    }

    private static DateTimeOffset ResolveNextDue(
        string cronExpression,
        DateTimeOffset dueAtUtc)
    {
        if (!CronExpressionParser.TryParse(cronExpression, out var schedule))
        {
            throw new InvalidDataException(
                $"Reporting schedule cron expression '{cronExpression}' is invalid.");
        }

        return schedule.GetNextOccurrenceOrNull(dueAtUtc.ToUniversalTime(), TimeZoneInfo.Utc)
            ?? throw new InvalidDataException(
                $"Reporting schedule cron expression '{cronExpression}' has no future UTC occurrence.");
    }

    private static void ValidateCronExpressionForUpsert(
        string cronExpression,
        DateTimeOffset dueAtUtc)
    {
        if (!CronExpressionParser.TryParse(cronExpression, out var schedule))
        {
            throw new ArgumentException(
                $"Invalid reporting schedule cron expression: {cronExpression}",
                nameof(cronExpression));
        }

        if (schedule.GetNextOccurrenceOrNull(dueAtUtc.ToUniversalTime(), TimeZoneInfo.Utc) is null)
        {
            throw new ArgumentException(
                "The reporting schedule cron expression has no future UTC occurrence within the supported calendar horizon.",
                nameof(cronExpression));
        }
    }

}
