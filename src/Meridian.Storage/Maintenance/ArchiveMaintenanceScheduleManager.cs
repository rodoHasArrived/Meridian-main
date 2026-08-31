using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Core.Scheduling;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Maintenance;

/// <summary>
/// Manages archive maintenance schedules with file-based persistence.
/// Thread-safe implementation for concurrent access.
/// </summary>
public sealed class ArchiveMaintenanceScheduleManager : IArchiveMaintenanceScheduleManager
{
    private readonly ILogger<ArchiveMaintenanceScheduleManager> _logger;
    private readonly string _schedulesPath;
    private readonly string _schedulesLockPath;
    private volatile ConcurrentDictionary<string, ArchiveMaintenanceSchedule> _schedules =
        new(StringComparer.Ordinal);
    private readonly MaintenanceExecutionHistory _executionHistory;
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private static readonly TimeSpan s_crossProcessLockRetryDelay = TimeSpan.FromMilliseconds(25);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public event EventHandler<ArchiveMaintenanceSchedule>? ScheduleCreated;
    public event EventHandler<ArchiveMaintenanceSchedule>? ScheduleUpdated;
    public event EventHandler<string>? ScheduleDeleted;

    public MaintenanceExecutionHistory ExecutionHistory => _executionHistory;

    public ArchiveMaintenanceScheduleManager(
        ILogger<ArchiveMaintenanceScheduleManager> logger,
        string dataRoot,
        MaintenanceExecutionHistory? executionHistory = null)
    {
        _logger = logger;
        _schedulesPath = Path.Combine(dataRoot, ".maintenance", "schedules.json");
        _schedulesLockPath = _schedulesPath + ".lock";
        _executionHistory = executionHistory ?? new MaintenanceExecutionHistory(dataRoot);

        Directory.CreateDirectory(Path.GetDirectoryName(_schedulesPath)!);
        LoadSchedules();
    }

    public IReadOnlyList<ArchiveMaintenanceSchedule> GetAllSchedules()
    {
        return _schedules.Values
            .OrderBy(s => s.Name)
            .Select(CloneSchedule)
            .ToList();
    }

    public ArchiveMaintenanceSchedule? GetSchedule(string scheduleId)
    {
        return _schedules.TryGetValue(scheduleId, out var schedule)
            ? CloneSchedule(schedule)
            : null;
    }

    public async Task<ArchiveMaintenanceSchedule> CreateScheduleAsync(
        ArchiveMaintenanceSchedule schedule,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var candidate = CloneSchedule(schedule);
        ValidateScheduleIdentity(candidate);
        candidate.NextExecutionAt = ValidateAndCalculateNextExecution(candidate);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);

            if (latest.ContainsKey(candidate.ScheduleId))
                throw new InvalidOperationException($"Schedule with ID '{candidate.ScheduleId}' already exists");

            candidate.Revision = 1;
            var snapshot = CopySnapshot(latest);
            snapshot[candidate.ScheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _schedules = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "Created maintenance schedule '{Name}' (ID: {ScheduleId}) with cron '{Cron}', next execution: {NextExecution}",
            candidate.Name, candidate.ScheduleId, candidate.CronExpression, candidate.NextExecutionAt);

        ScheduleCreated?.Invoke(this, CloneSchedule(candidate));
        return CloneSchedule(candidate);
    }

    public async Task<ArchiveMaintenanceSchedule> CreateFromPresetAsync(
        string presetName,
        string name,
        CancellationToken ct = default)
    {
        var schedule = presetName.ToLowerInvariant() switch
        {
            "daily-health" or "health" => MaintenanceSchedulePresets.DailyHealthCheck(name),
            "weekly-full" or "full" => MaintenanceSchedulePresets.WeeklyFullMaintenance(name),
            "daily-tier" or "tier" => MaintenanceSchedulePresets.DailyTierMigration(name),
            "monthly-compression" or "compression" => MaintenanceSchedulePresets.MonthlyCompression(name),
            "daily-retention" or "retention" => MaintenanceSchedulePresets.DailyRetentionEnforcement(name),
            _ => throw new ArgumentException($"Unknown preset: {presetName}", nameof(presetName))
        };

        return await CreateScheduleAsync(schedule, ct);
    }

    public async Task<ArchiveMaintenanceSchedule> UpdateScheduleAsync(
        ArchiveMaintenanceSchedule schedule,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var candidate = CloneSchedule(schedule);
        ValidateScheduleIdentity(candidate);
        candidate.ModifiedAt = DateTimeOffset.UtcNow;
        candidate.NextExecutionAt = ValidateAndCalculateNextExecution(candidate);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);

            if (!latest.TryGetValue(candidate.ScheduleId, out var retained))
                throw new KeyNotFoundException($"Schedule '{candidate.ScheduleId}' not found");
            if (candidate.Revision != 0 && candidate.Revision != retained.Revision)
            {
                throw new ArchiveMaintenanceScheduleConcurrencyException(
                    candidate.ScheduleId,
                    candidate.Revision,
                    retained.Revision);
            }

            PreserveRuntimeState(candidate, retained);
            candidate.Revision = NextRevision(retained.Revision);
            var snapshot = CopySnapshot(latest);
            snapshot[candidate.ScheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _schedules = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "Updated maintenance schedule '{Name}' (ID: {ScheduleId})",
            candidate.Name, candidate.ScheduleId);

        ScheduleUpdated?.Invoke(this, CloneSchedule(candidate));
        return CloneSchedule(candidate);
    }

    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        ArchiveMaintenanceSchedule? schedule;

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);

            if (!latest.TryGetValue(scheduleId, out schedule))
                return false;

            var snapshot = CopySnapshot(latest);
            snapshot.TryRemove(scheduleId, out _);
            await PersistSnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _schedules = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "Deleted maintenance schedule '{Name}' (ID: {ScheduleId})",
            schedule!.Name, scheduleId);

        ScheduleDeleted?.Invoke(this, scheduleId);
        return true;
    }

    public async Task<bool> SetScheduleEnabledAsync(string scheduleId, bool enabled, CancellationToken ct = default)
    {
        ArchiveMaintenanceSchedule candidate = null!;

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);

            if (!latest.TryGetValue(scheduleId, out var schedule))
                return false;

            candidate = CloneSchedule(schedule);
            candidate.Enabled = enabled;
            candidate.ModifiedAt = DateTimeOffset.UtcNow;
            candidate.NextExecutionAt = enabled
                ? ValidateAndCalculateNextExecution(candidate, requireFutureOccurrence: true)
                : null;
            candidate.Revision = NextRevision(schedule.Revision);

            var snapshot = CopySnapshot(latest);
            snapshot[scheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _schedules = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }

        _logger.LogInformation(
            "Maintenance schedule '{Name}' (ID: {ScheduleId}) {Action}",
            candidate.Name, scheduleId, enabled ? "enabled" : "disabled");

        ScheduleUpdated?.Invoke(this, CloneSchedule(candidate));
        return true;
    }

    public IReadOnlyList<ArchiveMaintenanceSchedule> GetDueSchedules(DateTimeOffset asOf)
    {
        return _schedules.Values
            .Where(s => s.Enabled &&
                        s.NextExecutionAt.HasValue &&
                        s.NextExecutionAt.Value <= asOf)
            .OrderBy(s => s.Priority)
            .ThenBy(s => s.NextExecutionAt)
            .Select(CloneSchedule)
            .ToList();
    }

    internal IReadOnlyList<string> GetPendingExecutionScheduleIds()
    {
        return _schedules.Values
            .Where(schedule => schedule.PendingExecution is not null)
            .OrderBy(schedule => schedule.PendingExecution!.CreatedAt)
            .Select(schedule => schedule.ScheduleId)
            .ToList();
    }

    /// <summary>
    /// Atomically advances one due occurrence and creates its durable outbox entry while holding
    /// both the in-process gate and the cross-process schedule-file lease.
    /// </summary>
    internal async Task<ArchiveMaintenanceClaim?> TryClaimDueScheduleAsync(
        string scheduleId,
        DateTimeOffset asOf,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ValidateLease(leaseOwner, leaseDuration);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            if (!latest.TryGetValue(scheduleId, out var retained)
                || retained.PendingExecution is not null
                || !retained.Enabled
                || !retained.NextExecutionAt.HasValue
                || retained.NextExecutionAt.Value > asOf)
            {
                _schedules = latest;
                return null;
            }

            var claimed = CloneSchedule(retained);
            var occurrenceAt = retained.NextExecutionAt.Value;
            claimed.PendingExecution = CreateExecutionClaim(
                claimed,
                occurrenceAt,
                manualTrigger: false,
                leaseOwner,
                asOf + leaseDuration);

            if (TryCalculateNextExecution(claimed, asOf, out var nextExecution))
            {
                claimed.NextExecutionAt = nextExecution;
            }
            else
            {
                claimed.Enabled = false;
                claimed.NextExecutionAt = null;
                MarkScheduleRepair(
                    claimed,
                    "Disabled the schedule while claiming its final due occurrence because it has no valid future occurrence.");
                _logger.LogWarning(
                    "Disabled maintenance schedule {ScheduleId} while claiming a due occurrence because it has no valid future occurrence",
                    claimed.ScheduleId);
            }

            claimed.Revision = NextRevision(retained.Revision);
            latest[scheduleId] = claimed;
            await PersistSnapshotUnderLockAsync(latest, ct).ConfigureAwait(false);
            _schedules = latest;
            return CreateClaimResult(claimed);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    internal async Task<ArchiveMaintenanceClaim?> TryClaimManualScheduleAsync(
        string scheduleId,
        DateTimeOffset asOf,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ValidateLease(leaseOwner, leaseDuration);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            if (!latest.TryGetValue(scheduleId, out var retained))
                throw new KeyNotFoundException($"Schedule '{scheduleId}' not found");
            if (retained.PendingExecution is not null)
            {
                _schedules = latest;
                return null;
            }

            var claimed = CloneSchedule(retained);
            claimed.PendingExecution = CreateExecutionClaim(
                claimed,
                asOf,
                manualTrigger: true,
                leaseOwner,
                asOf + leaseDuration);
            claimed.Revision = NextRevision(retained.Revision);
            latest[scheduleId] = claimed;
            await PersistSnapshotUnderLockAsync(latest, ct).ConfigureAwait(false);
            _schedules = latest;
            return CreateClaimResult(claimed);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    internal async Task<ArchiveMaintenanceClaim?> TryLeasePendingExecutionAsync(
        string scheduleId,
        DateTimeOffset asOf,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ValidateLease(leaseOwner, leaseDuration);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            if (!latest.TryGetValue(scheduleId, out var retained)
                || retained.PendingExecution is null)
            {
                _schedules = latest;
                return null;
            }

            var candidate = CloneSchedule(retained);
            var pending = candidate.PendingExecution!;
            var activeForeignLease = pending.LeaseExpiresAt > asOf
                && !string.Equals(pending.LeaseOwner, leaseOwner, StringComparison.Ordinal);
            if (activeForeignLease)
            {
                _schedules = latest;
                return null;
            }

            if (pending.State == ArchiveMaintenanceClaimState.Running)
            {
                pending.State = ArchiveMaintenanceClaimState.Interrupted;
                pending.LastError =
                    "The prior process stopped or lost its execution lease after marking this occurrence running; the outcome is ambiguous and the occurrence will not be replayed.";
            }
            else if (pending.State == ArchiveMaintenanceClaimState.Pending)
            {
                pending.State = ArchiveMaintenanceClaimState.Dispatched;
            }

            pending.LeaseOwner = leaseOwner;
            pending.LeaseExpiresAt = asOf + leaseDuration;
            candidate.Revision = NextRevision(retained.Revision);
            latest[scheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(latest, ct).ConfigureAwait(false);
            _schedules = latest;
            return CreateClaimResult(candidate);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    internal async Task RenewExecutionLeasesAsync(
        IReadOnlyDictionary<string, string> outstandingExecutions,
        DateTimeOffset asOf,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ValidateLease(leaseOwner, leaseDuration);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            var changed = false;
            foreach (var (scheduleId, executionId) in outstandingExecutions)
            {
                if (!latest.TryGetValue(scheduleId, out var retained)
                    || retained.PendingExecution is not { } pending
                    || !string.Equals(pending.ExecutionId, executionId, StringComparison.Ordinal)
                    || !string.Equals(pending.LeaseOwner, leaseOwner, StringComparison.Ordinal)
                    || pending.State == ArchiveMaintenanceClaimState.Interrupted)
                {
                    continue;
                }

                var candidate = CloneSchedule(retained);
                candidate.PendingExecution!.LeaseExpiresAt = asOf + leaseDuration;
                candidate.Revision = NextRevision(retained.Revision);
                latest[scheduleId] = candidate;
                changed = true;
            }

            if (changed)
                await PersistSnapshotUnderLockAsync(latest, ct).ConfigureAwait(false);
            _schedules = latest;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    internal async Task<bool> MarkExecutionRunningAsync(
        string scheduleId,
        string executionId,
        DateTimeOffset asOf,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ValidateLease(leaseOwner, leaseDuration);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            if (!latest.TryGetValue(scheduleId, out var retained)
                || retained.PendingExecution is not { } pending
                || !string.Equals(pending.ExecutionId, executionId, StringComparison.Ordinal)
                || !string.Equals(pending.LeaseOwner, leaseOwner, StringComparison.Ordinal)
                || pending.State is ArchiveMaintenanceClaimState.Running or ArchiveMaintenanceClaimState.Interrupted)
            {
                _schedules = latest;
                return false;
            }

            var candidate = CloneSchedule(retained);
            candidate.PendingExecution!.State = ArchiveMaintenanceClaimState.Running;
            candidate.PendingExecution.RunningAt = asOf;
            candidate.PendingExecution.LeaseExpiresAt = asOf + leaseDuration;
            candidate.Revision = NextRevision(retained.Revision);
            latest[scheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(latest, ct).ConfigureAwait(false);
            _schedules = latest;
            return true;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    internal async Task ReleaseExecutionForRetryAsync(
        string scheduleId,
        string executionId,
        string leaseOwner,
        string reason,
        CancellationToken ct = default)
    {
        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            if (!latest.TryGetValue(scheduleId, out var retained)
                || retained.PendingExecution is not { } pending
                || !string.Equals(pending.ExecutionId, executionId, StringComparison.Ordinal)
                || !string.Equals(pending.LeaseOwner, leaseOwner, StringComparison.Ordinal)
                || pending.State == ArchiveMaintenanceClaimState.Running)
            {
                _schedules = latest;
                return;
            }

            var candidate = CloneSchedule(retained);
            candidate.PendingExecution!.State = ArchiveMaintenanceClaimState.Pending;
            candidate.PendingExecution.LeaseOwner = null;
            candidate.PendingExecution.LeaseExpiresAt = null;
            candidate.PendingExecution.LastError = reason;
            candidate.Revision = NextRevision(retained.Revision);
            latest[scheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(latest, ct).ConfigureAwait(false);
            _schedules = latest;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    public MaintenanceScheduleSummary GetStatusSummary()
    {
        var schedules = _schedules.Values.ToList();
        var byTaskType = schedules
            .GroupBy(s => s.TaskType)
            .ToDictionary(g => g.Key, g => g.Count());

        var nextDue = schedules
            .Where(s => s.Enabled && s.NextExecutionAt.HasValue)
            .OrderBy(s => s.NextExecutionAt)
            .FirstOrDefault();

        return new MaintenanceScheduleSummary(
            TotalSchedules: schedules.Count,
            EnabledSchedules: schedules.Count(s => s.Enabled),
            DisabledSchedules: schedules.Count(s => !s.Enabled),
            ByTaskType: byTaskType,
            NextDueSchedule: nextDue?.NextExecutionAt,
            NextDueScheduleName: nextDue?.Name
        );
    }

    public async Task UpdateScheduleAfterExecutionAsync(
        string scheduleId,
        MaintenanceExecution execution,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var latest = ReadSchedulesSnapshotUnderLock(out _);
            if (!latest.TryGetValue(scheduleId, out var retained))
                return;

            if (string.Equals(retained.LastExecutionId, execution.ExecutionId, StringComparison.Ordinal)
                && (retained.PendingExecution is null
                    || !string.Equals(
                        retained.PendingExecution.ExecutionId,
                        execution.ExecutionId,
                        StringComparison.Ordinal)))
            {
                _schedules = latest;
                return;
            }

            var candidate = CloneSchedule(retained);
            candidate.LastExecutedAt = execution.StartedAt;
            candidate.LastExecutionId = execution.ExecutionId;
            candidate.LastExecutionStatus = execution.Status;
            candidate.ExecutionCount++;

            if (execution.Status == MaintenanceExecutionStatus.Completed ||
                execution.Status == MaintenanceExecutionStatus.CompletedWithWarnings)
            {
                candidate.SuccessfulExecutions++;
            }
            else if (execution.Status == MaintenanceExecutionStatus.Failed ||
                     execution.Status == MaintenanceExecutionStatus.TimedOut)
            {
                candidate.FailedExecutions++;
            }

            // A due claim already advanced NextExecutionAt before enqueue. Only advance again when
            // a long-running execution has crossed that retained occurrence.
            var completedAt = execution.CompletedAt ?? DateTimeOffset.UtcNow;
            if (!execution.ManualTrigger && candidate.Enabled &&
                (!candidate.NextExecutionAt.HasValue || candidate.NextExecutionAt.Value <= completedAt))
            {
                if (TryCalculateNextExecution(candidate, completedAt, out var nextExecution))
                {
                    candidate.NextExecutionAt = nextExecution;
                }
                else
                {
                    candidate.Enabled = false;
                    candidate.NextExecutionAt = null;
                    MarkScheduleRepair(
                        candidate,
                        "Disabled the schedule after execution because it has no valid future occurrence.");
                    _logger.LogWarning(
                        "Disabled maintenance schedule {ScheduleId} after execution because it has no valid future occurrence",
                        candidate.ScheduleId);
                }
            }
            else if (!candidate.Enabled)
            {
                candidate.NextExecutionAt = null;
            }

            if (string.Equals(
                    candidate.PendingExecution?.ExecutionId,
                    execution.ExecutionId,
                    StringComparison.Ordinal))
            {
                candidate.PendingExecution = null;
            }
            candidate.Revision = NextRevision(retained.Revision);

            var snapshot = CopySnapshot(latest);
            snapshot[scheduleId] = candidate;
            await PersistSnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _schedules = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private void LoadSchedules()
    {
        _persistLock.Wait();
        try
        {
            using var processLock = AcquireCrossProcessLockAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var loaded = ReadSchedulesSnapshotUnderLock(
                out var requiresPersistence,
                repairPastDueOccurrences: false);
            if (requiresPersistence)
            {
                try
                {
                    PersistSnapshotUnderLockAsync(loaded, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist repaired maintenance schedules from {Path}", _schedulesPath);
                    throw;
                }
            }

            _schedules = loaded;
            _logger.LogInformation("Loaded {Count} maintenance schedules from {Path}", loaded.Count, _schedulesPath);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private ConcurrentDictionary<string, ArchiveMaintenanceSchedule> ReadSchedulesSnapshotUnderLock(
        out bool requiresPersistence,
        bool repairPastDueOccurrences = false)
    {
        requiresPersistence = false;
        if (!File.Exists(_schedulesPath))
        {
            _logger.LogDebug("No existing maintenance schedules found at {Path}", _schedulesPath);
            return new ConcurrentDictionary<string, ArchiveMaintenanceSchedule>(StringComparer.Ordinal);
        }

        List<ArchiveMaintenanceSchedule>? schedules;
        try
        {
            var json = File.ReadAllText(_schedulesPath);
            schedules = JsonSerializer.Deserialize<List<ArchiveMaintenanceSchedule>>(json, s_jsonOptions);
            if (schedules is null)
                throw new JsonException("The retained archive-maintenance schedule document was null.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            var quarantinePath = QuarantineUnreadableScheduleFile();
            _logger.LogError(
                ex,
                "Quarantined unreadable maintenance schedules from {Path} to {QuarantinePath}",
                _schedulesPath,
                quarantinePath);
            requiresPersistence = true;
            return new ConcurrentDictionary<string, ArchiveMaintenanceSchedule>(StringComparer.Ordinal);
        }

        var now = DateTimeOffset.UtcNow;
        var loaded = new ConcurrentDictionary<string, ArchiveMaintenanceSchedule>(StringComparer.Ordinal);

        foreach (var schedule in schedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.ScheduleId))
            {
                _logger.LogWarning("Skipped loaded maintenance schedule without an identifier");
                requiresPersistence = true;
                continue;
            }

            if (loaded.ContainsKey(schedule.ScheduleId))
            {
                _logger.LogWarning(
                    "Skipped duplicate loaded maintenance schedule identifier {ScheduleId}",
                    schedule.ScheduleId);
                requiresPersistence = true;
                continue;
            }

            if (schedule.Revision <= 0)
            {
                schedule.Revision = 1;
                requiresPersistence = true;
            }

            string? repairReason = null;

            if (MaintenanceSchedulePresets.TryMigrateLegacyMonthlyCompression(schedule))
            {
                repairReason =
                    "Migrated the retained monthly-compression preset to explicit first-Sunday cron semantics.";
                requiresPersistence = true;
                _logger.LogInformation(
                    "Migrated loaded maintenance schedule {ScheduleId} to explicit first-Sunday cron semantics",
                    schedule.ScheduleId);
            }

            if (!TryValidateRetainedSettings(schedule, out var settingsFailure))
            {
                var quarantineReason =
                    $"Disabled the retained schedule because its configuration is invalid: {settingsFailure}.";
                var requiresNormalization =
                    schedule.Options is null || schedule.TargetPaths is null || schedule.Tags is null;
                var requiresQuarantine =
                    schedule.Enabled ||
                    schedule.NextExecutionAt.HasValue ||
                    !string.Equals(schedule.LastRepairReason, quarantineReason, StringComparison.Ordinal);

                schedule.Enabled = false;
                schedule.NextExecutionAt = null;
                schedule.Options ??= new MaintenanceTaskOptions();
                schedule.TargetPaths ??= new List<string>();
                schedule.Tags ??= new List<string>();
                if (string.Equals(
                        settingsFailure,
                        "the durable execution claim is invalid",
                        StringComparison.Ordinal))
                {
                    schedule.PendingExecution = null;
                }

                if (requiresQuarantine || requiresNormalization || repairReason is not null)
                {
                    repairReason = quarantineReason;
                    requiresPersistence = true;
                    _logger.LogWarning(
                        "Disabled loaded maintenance schedule {ScheduleId} because {Reason}",
                        schedule.ScheduleId,
                        settingsFailure);
                }
            }
            else if (schedule.Enabled && schedule.PendingExecution is null)
            {
                if (TryCalculateNextExecution(schedule, now, out var nextExecution))
                {
                    if (repairReason is not null ||
                        !schedule.NextExecutionAt.HasValue ||
                        (repairPastDueOccurrences && schedule.NextExecutionAt.Value <= now))
                    {
                        if (schedule.NextExecutionAt != nextExecution)
                            requiresPersistence = true;
                        schedule.NextExecutionAt = nextExecution;
                    }
                }
                else
                {
                    schedule.Enabled = false;
                    schedule.NextExecutionAt = null;
                    repairReason =
                        "Disabled the retained schedule because its cron expression or time zone has no valid future occurrence.";
                    requiresPersistence = true;
                    _logger.LogWarning(
                        "Disabled loaded maintenance schedule {ScheduleId} because its cron expression or time zone has no valid future occurrence",
                        schedule.ScheduleId);
                }
            }
            else if (!schedule.Enabled && schedule.NextExecutionAt.HasValue)
            {
                schedule.NextExecutionAt = null;
                requiresPersistence = true;
            }

            if (repairReason is not null)
                MarkScheduleRepair(schedule, repairReason);

            loaded[schedule.ScheduleId] = schedule;
        }
        return loaded;
    }

    private async Task PersistSnapshotUnderLockAsync(
        ConcurrentDictionary<string, ArchiveMaintenanceSchedule> snapshot,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var schedules = snapshot.Values
            .OrderBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
            .ToList();
        var json = JsonSerializer.Serialize(schedules, s_jsonOptions);

        try
        {
            await AtomicFileWriter.WriteAsync(_schedulesPath, json, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Persisted {Count} maintenance schedules to {Path}",
                schedules.Count,
                _schedulesPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist maintenance schedules");
            throw;
        }
    }

    private async Task<FileStream> AcquireCrossProcessLockAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var lockDirectory = Path.GetDirectoryName(_schedulesLockPath)!;
            if (!Directory.Exists(lockDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Maintenance schedule directory '{lockDirectory}' does not exist.");
            }
            try
            {
                return new FileStream(
                    _schedulesLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(s_crossProcessLockRetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private string QuarantineUnreadableScheduleFile()
    {
        var quarantineDirectory = Path.Combine(
            Path.GetDirectoryName(_schedulesPath)!,
            "quarantine");
        Directory.CreateDirectory(quarantineDirectory);
        var quarantinePath = Path.Combine(
            quarantineDirectory,
            $"schedules-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.invalid.json");
        File.Copy(_schedulesPath, quarantinePath, overwrite: false);
        return quarantinePath;
    }

    private static void PreserveRuntimeState(
        ArchiveMaintenanceSchedule candidate,
        ArchiveMaintenanceSchedule retained)
    {
        candidate.CreatedAt = retained.CreatedAt;
        candidate.LastExecutedAt = retained.LastExecutedAt;
        candidate.LastExecutionId = retained.LastExecutionId;
        candidate.LastExecutionStatus = retained.LastExecutionStatus;
        candidate.ExecutionCount = retained.ExecutionCount;
        candidate.SuccessfulExecutions = retained.SuccessfulExecutions;
        candidate.FailedExecutions = retained.FailedExecutions;
        candidate.PendingExecution = retained.PendingExecution is null
            ? null
            : CloneExecutionClaim(retained.PendingExecution);
        candidate.LastRepairReason = retained.LastRepairReason;
        candidate.LastRepairedAt = retained.LastRepairedAt;
    }

    private static ArchiveMaintenanceExecutionClaim CreateExecutionClaim(
        ArchiveMaintenanceSchedule schedule,
        DateTimeOffset occurrenceAt,
        bool manualTrigger,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt)
    {
        return new ArchiveMaintenanceExecutionClaim
        {
            OccurrenceAt = occurrenceAt,
            CreatedAt = DateTimeOffset.UtcNow,
            ManualTrigger = manualTrigger,
            ScheduleName = schedule.Name,
            TaskType = schedule.TaskType,
            Options = CloneTaskOptions(schedule.Options),
            TargetPaths = schedule.TargetPaths.ToList(),
            MaxDuration = schedule.MaxDuration,
            State = ArchiveMaintenanceClaimState.Dispatched,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt
        };
    }

    private static ArchiveMaintenanceClaim CreateClaimResult(ArchiveMaintenanceSchedule schedule)
    {
        return new ArchiveMaintenanceClaim(
            CloneSchedule(schedule),
            CloneExecutionClaim(schedule.PendingExecution
                ?? throw new InvalidOperationException("A claimed schedule is missing its durable execution entry.")));
    }

    private static ArchiveMaintenanceExecutionClaim CloneExecutionClaim(
        ArchiveMaintenanceExecutionClaim claim)
    {
        var json = JsonSerializer.Serialize(claim, s_jsonOptions);
        return JsonSerializer.Deserialize<ArchiveMaintenanceExecutionClaim>(json, s_jsonOptions)
            ?? throw new JsonException("Archive maintenance execution claim serialization produced a null value.");
    }

    private static MaintenanceTaskOptions CloneTaskOptions(MaintenanceTaskOptions options)
    {
        var json = JsonSerializer.Serialize(options, s_jsonOptions);
        return JsonSerializer.Deserialize<MaintenanceTaskOptions>(json, s_jsonOptions)
            ?? throw new JsonException("Archive maintenance task option serialization produced a null value.");
    }

    private static long NextRevision(long revision)
    {
        return revision <= 0 ? 1 : checked(revision + 1);
    }

    private static void ValidateLease(string leaseOwner, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
            throw new ArgumentException("Execution lease owner is required", nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    private static ConcurrentDictionary<string, ArchiveMaintenanceSchedule> CopySnapshot(
        ConcurrentDictionary<string, ArchiveMaintenanceSchedule> source)
    {
        return new ConcurrentDictionary<string, ArchiveMaintenanceSchedule>(
            source,
            StringComparer.Ordinal);
    }

    private static ArchiveMaintenanceSchedule CloneSchedule(ArchiveMaintenanceSchedule schedule)
    {
        var json = JsonSerializer.Serialize(schedule, s_jsonOptions);
        return JsonSerializer.Deserialize<ArchiveMaintenanceSchedule>(json, s_jsonOptions)
            ?? throw new JsonException("Archive maintenance schedule serialization produced a null value.");
    }

    private static void ValidateScheduleIdentity(ArchiveMaintenanceSchedule schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule.ScheduleId))
            throw new ArgumentException("Schedule ID is required", nameof(schedule));
        if (string.IsNullOrWhiteSpace(schedule.Name))
            throw new ArgumentException("Schedule name is required", nameof(schedule));
        if (schedule.MaxDuration <= TimeSpan.Zero)
            throw new ArgumentException("Schedule maximum duration must be positive", nameof(schedule));
        if (schedule.MaxRetries < 0)
            throw new ArgumentException("Schedule maximum retries cannot be negative", nameof(schedule));
        if (schedule.TargetPaths is null)
            throw new ArgumentException("Schedule target paths cannot be null", nameof(schedule));
        if (schedule.Tags is null)
            throw new ArgumentException("Schedule tags cannot be null", nameof(schedule));
        if (!Enum.IsDefined(schedule.TaskType))
            throw new ArgumentException("Schedule task type is unknown", nameof(schedule));
        if (!Enum.IsDefined(schedule.Priority))
            throw new ArgumentException("Schedule priority is unknown", nameof(schedule));

        schedule.Options ??= new MaintenanceTaskOptions();
        if (schedule.Options.ParallelOperations <= 0)
            throw new ArgumentException("Schedule parallel-operation count must be positive", nameof(schedule));
    }

    private static bool TryValidateRetainedSettings(
        ArchiveMaintenanceSchedule schedule,
        out string failure)
    {
        if (string.IsNullOrWhiteSpace(schedule.Name))
        {
            failure = "the schedule name is missing";
            return false;
        }
        if (schedule.Options is null)
        {
            failure = "the task options are null";
            return false;
        }
        if (schedule.MaxDuration <= TimeSpan.Zero)
        {
            failure = "the maximum duration is not positive";
            return false;
        }
        if (schedule.MaxRetries < 0)
        {
            failure = "the maximum retry count is negative";
            return false;
        }
        if (schedule.TargetPaths is null)
        {
            failure = "the target path collection is null";
            return false;
        }
        if (schedule.Tags is null)
        {
            failure = "the tag collection is null";
            return false;
        }
        if (!Enum.IsDefined(schedule.TaskType))
        {
            failure = "the task type is unknown";
            return false;
        }
        if (!Enum.IsDefined(schedule.Priority))
        {
            failure = "the priority is unknown";
            return false;
        }
        if (!CronExpressionParser.IsValid(schedule.CronExpression))
        {
            failure = "the cron expression is invalid";
            return false;
        }
        try
        {
            _ = schedule.TimeZone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            failure = "the time zone is invalid";
            return false;
        }
        if (schedule.Options.ParallelOperations <= 0)
        {
            failure = "the parallel-operation count is not positive";
            return false;
        }
        if (schedule.PendingExecution is { } pending
            && (string.IsNullOrWhiteSpace(pending.ExecutionId)
                || pending.Options is null
                || pending.TargetPaths is null
                || pending.MaxDuration <= TimeSpan.Zero
                || !Enum.IsDefined(pending.State)
                || !Enum.IsDefined(pending.TaskType)))
        {
            failure = "the durable execution claim is invalid";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static void MarkScheduleRepair(ArchiveMaintenanceSchedule schedule, string reason)
    {
        var repairedAt = DateTimeOffset.UtcNow;
        schedule.ModifiedAt = repairedAt;
        schedule.LastRepairReason = reason;
        schedule.LastRepairedAt = repairedAt;
        schedule.Revision = NextRevision(schedule.Revision);
    }

    private static DateTimeOffset? ValidateAndCalculateNextExecution(
        ArchiveMaintenanceSchedule schedule,
        bool? requireFutureOccurrence = null)
    {
        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            throw new ArgumentException($"Invalid cron expression: {schedule.CronExpression}", nameof(schedule));

        DateTimeOffset? nextExecution;
        try
        {
            nextExecution = schedule.CalculateNextExecution();
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException($"Invalid time zone: {schedule.TimeZoneId}", nameof(schedule), ex);
        }

        if ((requireFutureOccurrence ?? schedule.Enabled) && !nextExecution.HasValue)
        {
            throw new ArgumentException(
                $"Enabled schedule '{schedule.ScheduleId}' has no future occurrence within the supported cron calendar horizon.",
                nameof(schedule));
        }

        return (requireFutureOccurrence ?? schedule.Enabled) ? nextExecution : null;
    }

    private static bool TryCalculateNextExecution(
        ArchiveMaintenanceSchedule schedule,
        DateTimeOffset from,
        out DateTimeOffset? nextExecution)
    {
        nextExecution = null;
        if (!CronExpressionParser.IsValid(schedule.CronExpression))
            return false;

        try
        {
            nextExecution = schedule.CalculateNextExecution(from);
            return nextExecution.HasValue;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}

internal sealed record ArchiveMaintenanceClaim(
    ArchiveMaintenanceSchedule Schedule,
    ArchiveMaintenanceExecutionClaim Execution);

/// <summary>
/// Raised when a revision-aware caller attempts to replace a newer retained schedule.
/// </summary>
public sealed class ArchiveMaintenanceScheduleConcurrencyException : InvalidOperationException
{
    public ArchiveMaintenanceScheduleConcurrencyException(
        string scheduleId,
        long expectedRevision,
        long actualRevision)
        : base(
            $"Schedule '{scheduleId}' changed concurrently (expected revision {expectedRevision}, actual revision {actualRevision}). Refresh and retry.")
    {
        ScheduleId = scheduleId;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }

    public string ScheduleId { get; }
    public long ExpectedRevision { get; }
    public long ActualRevision { get; }
}

/// <summary>
/// Tracks maintenance execution history with file-based persistence.
/// </summary>
public sealed class MaintenanceExecutionHistory : IMaintenanceExecutionHistory
{
    private readonly string _historyPath;
    private readonly string _historyLockPath;
    private volatile ConcurrentDictionary<string, MaintenanceExecution> _executions =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private const int MaxInMemoryExecutions = 1000;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public MaintenanceExecutionHistory(string dataRoot)
    {
        _historyPath = Path.Combine(dataRoot, ".maintenance", "history.json");
        _historyLockPath = _historyPath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        LoadHistory();
    }

    public async Task RecordExecutionAsync(MaintenanceExecution execution, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var candidate = CloneExecution(execution);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var snapshot = ReadHistorySnapshotUnderLock();
            snapshot[candidate.ExecutionId] = candidate;
            TrimHistory(snapshot);
            await PersistHistorySnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _executions = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    public async Task UpdateExecutionAsync(MaintenanceExecution execution, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var candidate = CloneExecution(execution);

        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var snapshot = ReadHistorySnapshotUnderLock();
            snapshot[candidate.ExecutionId] = candidate;
            TrimHistory(snapshot);
            await PersistHistorySnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _executions = snapshot;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    public MaintenanceExecution? GetExecution(string executionId)
    {
        return _executions.TryGetValue(executionId, out var execution)
            ? CloneExecution(execution)
            : null;
    }

    public IReadOnlyList<MaintenanceExecution> GetRecentExecutions(int limit = 50)
    {
        return _executions.Values
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .Select(CloneExecution)
            .ToList();
    }

    public IReadOnlyList<MaintenanceExecution> GetExecutionsForSchedule(string scheduleId, int limit = 50)
    {
        return _executions.Values
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .Select(CloneExecution)
            .ToList();
    }

    public IReadOnlyList<MaintenanceExecution> GetFailedExecutions(int limit = 50)
    {
        return _executions.Values
            .Where(e => e.Status == MaintenanceExecutionStatus.Failed ||
                       e.Status == MaintenanceExecutionStatus.TimedOut)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .Select(CloneExecution)
            .ToList();
    }

    public IReadOnlyList<MaintenanceExecution> GetExecutionsByTimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        return _executions.Values
            .Where(e => e.StartedAt >= from && e.StartedAt <= to)
            .OrderByDescending(e => e.StartedAt)
            .Select(CloneExecution)
            .ToList();
    }

    public ScheduleExecutionSummary GetScheduleSummary(string scheduleId, int recentCount = 10)
    {
        var executions = _executions.Values
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.StartedAt)
            .Select(CloneExecution)
            .ToList();

        var successful = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Completed ||
            e.Status == MaintenanceExecutionStatus.CompletedWithWarnings);

        var failed = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Failed ||
            e.Status == MaintenanceExecutionStatus.TimedOut);

        var completed = executions.Where(e => e.Duration.HasValue).ToList();
        var avgDuration = completed.Count > 0
            ? TimeSpan.FromTicks((long)completed.Average(e => e.Duration!.Value.Ticks))
            : TimeSpan.Zero;

        var lastExecution = executions.FirstOrDefault();

        return new ScheduleExecutionSummary(
            ScheduleId: scheduleId,
            ScheduleName: lastExecution?.ScheduleName ?? "Unknown",
            TotalExecutions: executions.Count,
            SuccessfulExecutions: successful,
            FailedExecutions: failed,
            SuccessRate: executions.Count > 0 ? (double)successful / executions.Count * 100 : 0,
            AverageDuration: avgDuration,
            LastExecutionAt: lastExecution?.StartedAt,
            LastStatus: lastExecution?.Status,
            NextScheduledAt: null,
            RecentExecutions: executions.Take(recentCount).ToList()
        );
    }

    public MaintenanceStatistics GetStatistics(TimeSpan? period = null)
    {
        var cutoff = period.HasValue
            ? DateTimeOffset.UtcNow - period.Value
            : DateTimeOffset.MinValue;

        var executions = _executions.Values
            .Where(e => e.StartedAt >= cutoff)
            .ToList();

        var last24h = executions.Count(e => e.StartedAt >= DateTimeOffset.UtcNow.AddHours(-24));
        var last7d = executions.Count(e => e.StartedAt >= DateTimeOffset.UtcNow.AddDays(-7));

        var successful = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Completed ||
            e.Status == MaintenanceExecutionStatus.CompletedWithWarnings);

        var failed = executions.Count(e =>
            e.Status == MaintenanceExecutionStatus.Failed ||
            e.Status == MaintenanceExecutionStatus.TimedOut);

        var completed = executions.Where(e => e.Duration.HasValue).ToList();
        var avgDuration = completed.Count > 0
            ? TimeSpan.FromTicks((long)completed.Average(e => e.Duration!.Value.Ticks))
            : TimeSpan.Zero;

        return new MaintenanceStatistics(
            GeneratedAt: DateTimeOffset.UtcNow,
            TotalSchedules: 0, // Will be filled by caller
            EnabledSchedules: 0,
            DisabledSchedules: 0,
            TotalExecutions: executions.Count,
            SuccessfulExecutions: successful,
            FailedExecutions: failed,
            ExecutionsLast24Hours: last24h,
            ExecutionsLast7Days: last7d,
            TotalBytesProcessed: executions.Sum(e => e.BytesProcessed),
            TotalBytesSaved: executions.Sum(e => e.BytesSaved),
            TotalIssuesFound: executions.Sum(e => e.IssuesFound),
            TotalIssuesResolved: executions.Sum(e => e.IssuesResolved),
            AverageExecutionDuration: avgDuration,
            LastExecutionAt: executions.OrderByDescending(e => e.StartedAt).FirstOrDefault()?.StartedAt,
            NextScheduledExecution: null
        );
    }

    public async Task<int> CleanupOldRecordsAsync(int maxAgeDays = 90, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        await _persistLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireCrossProcessLockAsync(ct).ConfigureAwait(false);
            var snapshot = ReadHistorySnapshotUnderLock();
            var toRemove = snapshot
                .Where(kvp => kvp.Value.StartedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
                snapshot.TryRemove(key, out _);

            if (toRemove.Count > 0)
                await PersistHistorySnapshotUnderLockAsync(snapshot, ct).ConfigureAwait(false);
            _executions = snapshot;

            return toRemove.Count;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private void LoadHistory()
    {
        _persistLock.Wait();
        try
        {
            using var processLock = AcquireCrossProcessLockAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _executions = ReadHistorySnapshotUnderLock();
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private ConcurrentDictionary<string, MaintenanceExecution> ReadHistorySnapshotUnderLock()
    {
        var snapshot = new ConcurrentDictionary<string, MaintenanceExecution>(StringComparer.Ordinal);
        if (!File.Exists(_historyPath))
            return snapshot;

        try
        {
            var json = File.ReadAllText(_historyPath);
            var executions = JsonSerializer.Deserialize<List<MaintenanceExecution>>(json, s_jsonOptions)
                ?? throw new JsonException("The retained maintenance execution history was null.");
            foreach (var execution in executions
                .OrderByDescending(e => e.StartedAt)
                .Take(MaxInMemoryExecutions))
            {
                if (!string.IsNullOrWhiteSpace(execution.ExecutionId))
                    snapshot[execution.ExecutionId] = CloneExecution(execution);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _ = ex;
            var quarantineDirectory = Path.Combine(Path.GetDirectoryName(_historyPath)!, "quarantine");
            Directory.CreateDirectory(quarantineDirectory);
            var quarantinePath = Path.Combine(
                quarantineDirectory,
                $"history-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.invalid.json");
            File.Copy(_historyPath, quarantinePath, overwrite: false);
        }

        return snapshot;
    }

    private static void TrimHistory(ConcurrentDictionary<string, MaintenanceExecution> snapshot)
    {
        if (snapshot.Count <= MaxInMemoryExecutions)
            return;

        foreach (var key in snapshot
            .OrderBy(kvp => kvp.Value.StartedAt)
            .Take(snapshot.Count - MaxInMemoryExecutions)
            .Select(kvp => kvp.Key))
        {
            snapshot.TryRemove(key, out _);
        }
    }

    private async Task PersistHistorySnapshotUnderLockAsync(
        ConcurrentDictionary<string, MaintenanceExecution> snapshot,
        CancellationToken ct)
    {
        var executions = snapshot.Values
            .OrderByDescending(e => e.StartedAt)
            .Take(MaxInMemoryExecutions)
            .ToList();
        var json = JsonSerializer.Serialize(executions, s_jsonOptions);
        await AtomicFileWriter.WriteAsync(_historyPath, json, ct).ConfigureAwait(false);
    }

    private async Task<FileStream> AcquireCrossProcessLockAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var lockDirectory = Path.GetDirectoryName(_historyLockPath)!;
            if (!Directory.Exists(lockDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Maintenance history directory '{lockDirectory}' does not exist.");
            }
            try
            {
                return new FileStream(
                    _historyLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private static MaintenanceExecution CloneExecution(MaintenanceExecution execution)
    {
        var json = JsonSerializer.Serialize(execution, s_jsonOptions);
        return JsonSerializer.Deserialize<MaintenanceExecution>(json, s_jsonOptions)
            ?? throw new JsonException("Maintenance execution serialization produced a null value.");
    }
}
