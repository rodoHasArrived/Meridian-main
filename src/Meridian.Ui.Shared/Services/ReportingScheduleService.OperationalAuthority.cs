using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportingScheduleService
{
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExecutionLeaseRenewalInterval = TimeSpan.FromSeconds(30);

    private readonly string _executionLeaseOwner =
        $"reporting-schedule:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly IReportingDeploymentReadinessService? _deploymentReadinessService;

    private void EnsureReportingDeploymentReady(string operation)
    {
        if (_deploymentReadinessService is null)
        {
            return;
        }

        var capability = _deploymentReadinessService.Evaluate();
        if (!capability.IsReady)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} until the authoritative reporting deployment is ready: " +
                string.Join(" ", capability.BlockingReasons));
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
            Math.Max(1, (nextDue.Date - schedule.DueAtUtc.Date).Days));
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
        var cron = cronExpression.Trim();
        if (cron.EndsWith(" 1", StringComparison.Ordinal)
            || cron.Contains(" * * 5", StringComparison.Ordinal))
        {
            return dueAtUtc.AddDays(7);
        }

        if (cron.Contains(" 1 * *", StringComparison.Ordinal))
        {
            return dueAtUtc.AddMonths(1);
        }

        var next = dueAtUtc.AddDays(1);
        if (cron.EndsWith("1-5", StringComparison.Ordinal))
        {
            while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                next = next.AddDays(1);
            }
        }

        return next;
    }

}
