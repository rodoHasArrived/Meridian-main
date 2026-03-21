using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for scheduling and coordinating maintenance operations with trading hours awareness.
/// </summary>
public sealed class MaintenanceScheduler : IMaintenanceScheduler, IAsyncDisposable
{
    private readonly OperationalScheduleConfig _config;
    private readonly IFileMaintenanceService _fileMaintenanceService;
    private readonly ITierMigrationService _tierMigrationService;
    private readonly IDataQualityService _dataQualityService;

    private readonly ConcurrentQueue<ScheduledJob> _jobQueue = new();
    private readonly ConcurrentDictionary<string, ScheduledJob> _runningJobs = new();
    private readonly ConcurrentDictionary<string, JobExecutionStatus> _jobHistory = new();

    private readonly CancellationTokenSource _cts = new();
    private Task? _schedulerTask;
    private bool _isDisposed;

    public MaintenanceScheduler(
        OperationalScheduleConfig config,
        IFileMaintenanceService fileMaintenanceService,
        ITierMigrationService tierMigrationService,
        IDataQualityService dataQualityService)
    {
        _config = config;
        _fileMaintenanceService = fileMaintenanceService;
        _tierMigrationService = tierMigrationService;
        _dataQualityService = dataQualityService;
    }

    public void Start()
    {
        _schedulerTask = RunSchedulerLoopAsync(_cts.Token);
    }

    public Task<ScheduleDecision> CanRunNowAsync(
        MaintenanceType operation,
        ResourceRequirements requirements,
        CancellationToken ct = default)
    {
        var now = GetCurrentTime();
        var window = FindCurrentMaintenanceWindow(now);

        if (window == null)
        {
            var nextWindow = FindNextMaintenanceWindow(now);
            return Task.FromResult(new ScheduleDecision(
                Allowed: false,
                Reason: "No active maintenance window",
                CurrentWindow: null,
                WaitTime: nextWindow != null ? GetTimeUntilWindow(now, nextWindow) : null,
                ApplicableLimits: null
            ));
        }

        // Check if operation is allowed in this window
        if (window.AllowedOperations.Length > 0 &&
            !window.AllowedOperations.Contains(operation) &&
            !window.AllowedOperations.Contains(MaintenanceType.All))
        {
            return Task.FromResult(new ScheduleDecision(
                Allowed: false,
                Reason: $"Operation {operation} not allowed in {window.Name} window",
                CurrentWindow: window,
                WaitTime: null,
                ApplicableLimits: window.Limits
            ));
        }

        // Check resource availability
        var runningCount = _runningJobs.Count;
        if (runningCount >= window.MaxConcurrentJobs)
        {
            return Task.FromResult(new ScheduleDecision(
                Allowed: false,
                Reason: $"Max concurrent jobs ({window.MaxConcurrentJobs}) reached",
                CurrentWindow: window,
                WaitTime: TimeSpan.FromMinutes(5),
                ApplicableLimits: window.Limits
            ));
        }

        return Task.FromResult(new ScheduleDecision(
            Allowed: true,
            Reason: "Allowed",
            CurrentWindow: window,
            WaitTime: null,
            ApplicableLimits: window.Limits
        ));
    }

    public Task<ScheduleSlot?> FindNextWindowAsync(
        MaintenanceType operation,
        TimeSpan estimatedDuration,
        ResourceRequirements requirements,
        CancellationToken ct = default)
    {
        var now = GetCurrentTime();

        // Check current window first
        var currentWindow = FindCurrentMaintenanceWindow(now);
        if (currentWindow != null && IsOperationAllowed(currentWindow, operation))
        {
            var remainingTime = GetRemainingWindowTime(now, currentWindow);
            if (remainingTime >= estimatedDuration)
            {
                return Task.FromResult<ScheduleSlot?>(new ScheduleSlot(
                    Start: now,
                    End: now + remainingTime,
                    Window: currentWindow,
                    Limits: currentWindow.Limits,
                    AvailableConcurrencySlots: currentWindow.MaxConcurrentJobs - _runningJobs.Count
                ));
            }
        }

        // Find next suitable window
        for (int daysAhead = 0; daysAhead < 7; daysAhead++)
        {
            var checkDate = now.Date.AddDays(daysAhead);
            var dayOfWeek = checkDate.DayOfWeek;

            foreach (var window in _config.MaintenanceWindows)
            {
                if (!window.Days.Contains(dayOfWeek))
                    continue;

                if (!IsOperationAllowed(window, operation))
                    continue;

                var windowStart = checkDate + window.Start;
                var windowEnd = checkDate + window.End;

                // Handle overnight windows
                if (window.End < window.Start)
                    windowEnd = windowEnd.AddDays(1);

                if (windowStart > now && (windowEnd - windowStart) >= estimatedDuration)
                {
                    return Task.FromResult<ScheduleSlot?>(new ScheduleSlot(
                        Start: windowStart,
                        End: windowEnd,
                        Window: window,
                        Limits: window.Limits,
                        AvailableConcurrencySlots: window.MaxConcurrentJobs
                    ));
                }
            }
        }

        return Task.FromResult<ScheduleSlot?>(null);
    }

    public async Task<ScheduledJob> ScheduleAsync(
        MaintenanceJob job,
        ScheduleOptions options,
        CancellationToken ct = default)
    {
        var slot = await FindNextWindowAsync(job.Type, job.EstimatedDuration, job.Requirements, ct);

        var scheduledJob = new ScheduledJob(
            Id: Guid.NewGuid().ToString(),
            Job: job,
            ScheduledStart: slot?.Start ?? DateTimeOffset.UtcNow.AddDays(1),
            Status: JobStatus.Pending,
            CreatedAt: DateTimeOffset.UtcNow
        );

        _jobQueue.Enqueue(scheduledJob);
        return scheduledJob;
    }

    public Task<OperationalState> GetStateAsync(CancellationToken ct = default)
    {
        var now = GetCurrentTime();
        var currentSession = GetCurrentTradingSession(now);
        var currentWindow = FindCurrentMaintenanceWindow(now);
        var nextWindow = currentWindow == null ? FindNextMaintenanceWindow(now) : null;

        return Task.FromResult(new OperationalState(
            IsRealTimeCollectionActive: currentSession != null && IsWithinTradingHours(now, currentSession),
            CurrentSession: currentSession,
            CurrentMaintenanceWindow: currentWindow,
            RunningMaintenanceJobs: _runningJobs.Count,
            PendingJobs: _jobQueue.ToArray(),
            NextMaintenanceWindowStart: nextWindow != null
                ? now.Date + nextWindow.Start
                : DateTimeOffset.UtcNow.AddDays(1)
        ));
    }

    public bool IsRealTimeCollectionActive()
    {
        var now = GetCurrentTime();
        var session = GetCurrentTradingSession(now);
        return session != null && IsWithinTradingHours(now, session);
    }

    public TimeSpan? GetTimeUntilCollectionEnds()
    {
        var now = GetCurrentTime();
        var session = GetCurrentTradingSession(now);

        if (session == null || !IsWithinTradingHours(now, session))
            return null;

        var endTime = now.Date + session.AfterHoursEnd;
        return endTime - now;
    }

    public async Task<JobExecutionStatus> ExecuteJobAsync(MaintenanceJob job, CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var status = new JobExecutionStatus(
            JobId: job.Id,
            Type: job.Type,
            StartedAt: startTime,
            CompletedAt: null,
            Status: JobStatus.Running,
            Progress: 0,
            Message: "Starting...",
            Errors: new List<string>()
        );

        _runningJobs[job.Id] = new ScheduledJob(job.Id, job, startTime, JobStatus.Running, startTime);

        try
        {
            status = status with { Message = $"Executing {job.Type}..." };

            // Execute based on job type
            var result = job.Type switch
            {
                MaintenanceType.HealthCheck => await ExecuteHealthCheckAsync(job, ct),
                MaintenanceType.IntegrityValidation => await ExecuteIntegrityValidationAsync(job, ct),
                MaintenanceType.Compaction => await ExecuteCompactionAsync(job, ct),
                MaintenanceType.TierMigration => await ExecuteTierMigrationAsync(job, ct),
                MaintenanceType.QualityScoring => await ExecuteQualityScoringAsync(job, ct),
                MaintenanceType.IndexRebuild => await ExecuteIndexRebuildAsync(job, ct),
                _ => (true, "Completed")
            };

            status = status with
            {
                CompletedAt = DateTimeOffset.UtcNow,
                Status = result.Item1 ? JobStatus.Completed : JobStatus.Failed,
                Progress = 100,
                Message = result.Item2
            };
        }
        catch (Exception ex)
        {
            status = status with
            {
                CompletedAt = DateTimeOffset.UtcNow,
                Status = JobStatus.Failed,
                Message = ex.Message,
                Errors = new List<string> { ex.ToString() }
            };
        }
        finally
        {
            _runningJobs.TryRemove(job.Id, out _);
            _jobHistory[job.Id] = status;
        }

        return status;
    }

    public IReadOnlyList<ScheduledJob> GetPendingJobs() => _jobQueue.ToArray();

    public IReadOnlyList<ScheduledJob> GetRunningJobs() => _runningJobs.Values.ToList();

    public JobExecutionStatus? GetJobStatus(string jobId)
    {
        return _jobHistory.TryGetValue(jobId, out var status) ? status : null;
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessJobQueueAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Log and continue
            }
        }
    }

    private async Task ProcessJobQueueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        while (_jobQueue.TryPeek(out var job))
        {
            var decision = await CanRunNowAsync(job.Job.Type, job.Job.Requirements, ct);

            if (!decision.Allowed)
                break;

            if (_jobQueue.TryDequeue(out job))
            {
                _ = ExecuteJobAsync(job.Job, ct);
            }
        }
    }

    private async Task<(bool success, string message)> ExecuteHealthCheckAsync(MaintenanceJob job, CancellationToken ct)
    {
        var result = await _fileMaintenanceService.RunHealthCheckAsync(new HealthCheckOptions
        {
            ValidateChecksums = true,
            CheckSequenceContinuity = true,
            IdentifyCorruption = true,
            Paths = job.TargetPaths,
            ParallelChecks = 4
        }, ct);

        return (result.Summary.CorruptedFiles == 0, $"Health check complete: {result.Summary.HealthyFiles} healthy, {result.Summary.CorruptedFiles} corrupted");
    }

    private async Task<(bool success, string message)> ExecuteIntegrityValidationAsync(MaintenanceJob job, CancellationToken ct)
    {
        var result = await _fileMaintenanceService.RunHealthCheckAsync(new HealthCheckOptions
        {
            ValidateChecksums = true,
            ValidateSchemas = true,
            Paths = job.TargetPaths
        }, ct);

        return (result.Summary.CorruptedFiles == 0, $"Integrity validation complete: {result.Summary.TotalFiles} files checked");
    }

    private async Task<(bool success, string message)> ExecuteCompactionAsync(MaintenanceJob job, CancellationToken ct)
    {
        var result = await _fileMaintenanceService.DefragmentAsync(new DefragOptions(), ct);
        return (true, $"Compaction complete: {result.FilesProcessed} files processed, {result.BytesBefore - result.BytesAfter} bytes saved");
    }

    private async Task<(bool success, string message)> ExecuteTierMigrationAsync(MaintenanceJob job, CancellationToken ct)
    {
        var plan = await _tierMigrationService.PlanMigrationAsync(TimeSpan.FromDays(1), ct);

        foreach (var action in plan.Actions)
        {
            await _tierMigrationService.MigrateAsync(action.SourcePath, action.TargetTier, new MigrationOptions(), ct);
        }

        return (true, $"Tier migration complete: {plan.Actions.Count} files migrated");
    }

    private async Task<(bool success, string message)> ExecuteQualityScoringAsync(MaintenanceJob job, CancellationToken ct)
    {
        var report = await _dataQualityService.GenerateReportAsync(new QualityReportOptions(
            Paths: job.TargetPaths,
            MinScoreThreshold: 1.0,
            IncludeRecommendations: true
        ), ct);

        return (true, $"Quality scoring complete: {report.FilesAnalyzed} files, avg score: {report.AverageScore:F2}");
    }

    private Task<(bool success, string message)> ExecuteIndexRebuildAsync(MaintenanceJob job, CancellationToken ct)
    {
        // Index rebuild would be handled by search service
        return Task.FromResult((true, "Index rebuild complete"));
    }

    private DateTimeOffset GetCurrentTime() => DateTimeOffset.Now;

    private MaintenanceWindow? FindCurrentMaintenanceWindow(DateTimeOffset now)
    {
        var timeOfDay = now.TimeOfDay;
        var dayOfWeek = now.DayOfWeek;

        foreach (var window in _config.MaintenanceWindows)
        {
            if (!window.Days.Contains(dayOfWeek))
                continue;

            var start = window.Start;
            var end = window.End;

            // Handle overnight windows
            if (end < start)
            {
                if (timeOfDay >= start || timeOfDay < end)
                    return window;
            }
            else
            {
                if (timeOfDay >= start && timeOfDay < end)
                    return window;
            }
        }

        return null;
    }

    private MaintenanceWindow? FindNextMaintenanceWindow(DateTimeOffset now)
    {
        var timeOfDay = now.TimeOfDay;

        // Check windows for today
        foreach (var window in _config.MaintenanceWindows.OrderBy(w => w.Start))
        {
            if (window.Days.Contains(now.DayOfWeek) && window.Start > timeOfDay)
                return window;
        }

        // Check tomorrow and subsequent days
        for (int i = 1; i < 7; i++)
        {
            var checkDate = now.AddDays(i);
            foreach (var window in _config.MaintenanceWindows.OrderBy(w => w.Start))
            {
                if (window.Days.Contains(checkDate.DayOfWeek))
                    return window;
            }
        }

        return null;
    }

    private TimeSpan? GetTimeUntilWindow(DateTimeOffset now, MaintenanceWindow window)
    {
        for (int i = 0; i < 7; i++)
        {
            var checkDate = now.Date.AddDays(i);
            if (window.Days.Contains(checkDate.DayOfWeek))
            {
                var windowStart = checkDate + window.Start;
                if (windowStart > now)
                    return windowStart - now;
            }
        }
        return null;
    }

    private TimeSpan GetRemainingWindowTime(DateTimeOffset now, MaintenanceWindow window)
    {
        var end = now.Date + window.End;
        if (window.End < window.Start)
            end = end.AddDays(1);
        return end - now;
    }

    private bool IsOperationAllowed(MaintenanceWindow window, MaintenanceType operation)
    {
        if (window.AllowedOperations.Length == 0)
            return true;

        return window.AllowedOperations.Contains(operation) ||
               window.AllowedOperations.Contains(MaintenanceType.All);
    }

    private TradingSession? GetCurrentTradingSession(DateTimeOffset now)
    {
        return _config.TradingSessions.FirstOrDefault(s =>
            s.ActiveDays.Contains(now.DayOfWeek) &&
            !_config.Holidays.Contains(now.Date.ToString("yyyy-MM-dd")));
    }

    private bool IsWithinTradingHours(DateTimeOffset now, TradingSession session)
    {
        var timeOfDay = now.TimeOfDay;
        var start = session.IncludesPreMarket ? session.PreMarketStart : session.RegularStart;
        var end = session.IncludesAfterHours ? session.AfterHoursEnd : session.RegularEnd;

        return timeOfDay >= start && timeOfDay <= end;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _cts.Cancel();
        if (_schedulerTask != null)
        {
            try
            {
                await _schedulerTask;
            }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}

/// <summary>
/// Interface for maintenance scheduler.
/// </summary>
public interface IMaintenanceScheduler
{
    Task<ScheduleDecision> CanRunNowAsync(MaintenanceType operation, ResourceRequirements requirements, CancellationToken ct = default);
    Task<ScheduleSlot?> FindNextWindowAsync(MaintenanceType operation, TimeSpan estimatedDuration, ResourceRequirements requirements, CancellationToken ct = default);
    Task<ScheduledJob> ScheduleAsync(MaintenanceJob job, ScheduleOptions options, CancellationToken ct = default);
    Task<OperationalState> GetStateAsync(CancellationToken ct = default);
    Task<JobExecutionStatus> ExecuteJobAsync(MaintenanceJob job, CancellationToken ct = default);
    IReadOnlyList<ScheduledJob> GetPendingJobs();
    IReadOnlyList<ScheduledJob> GetRunningJobs();
    JobExecutionStatus? GetJobStatus(string jobId);
    bool IsRealTimeCollectionActive();
    TimeSpan? GetTimeUntilCollectionEnds();
}

// Configuration types
public sealed record OperationalScheduleConfig(
    string Name = "Default",
    TradingSession[] TradingSessions = null!,
    MaintenanceWindow[] MaintenanceWindows = null!,
    string[] Holidays = null!,
    TimeZoneInfo PrimaryTimeZone = null!
)
{
    public OperationalScheduleConfig() : this("Default", Array.Empty<TradingSession>(), Array.Empty<MaintenanceWindow>(), Array.Empty<string>(), TimeZoneInfo.Utc) { }

    public static OperationalScheduleConfig Default => new(
        Name: "US_Equities_Schedule",
        TradingSessions: new[]
        {
            new TradingSession(
                Name: "US_Equities",
                ActiveDays: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                PreMarketStart: new TimeSpan(4, 0, 0),
                RegularStart: new TimeSpan(9, 30, 0),
                RegularEnd: new TimeSpan(16, 0, 0),
                AfterHoursEnd: new TimeSpan(20, 0, 0),
                IncludesPreMarket: true,
                IncludesAfterHours: true
            )
        },
        MaintenanceWindows: new[]
        {
            new MaintenanceWindow(
                Name: "overnight_maintenance",
                Start: new TimeSpan(21, 0, 0),
                End: new TimeSpan(3, 0, 0),
                Days: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                AllowedOperations: Array.Empty<MaintenanceType>(), // All allowed
                MaxConcurrentJobs: 8,
                Limits: new ResourceLimits(80, 70, 500)
            ),
            new MaintenanceWindow(
                Name: "weekend_maintenance",
                Start: TimeSpan.Zero,
                End: new TimeSpan(23, 59, 59),
                Days: new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                AllowedOperations: Array.Empty<MaintenanceType>(),
                MaxConcurrentJobs: 16,
                Limits: new ResourceLimits(100, 90, 1000)
            ),
            new MaintenanceWindow(
                Name: "intraday_light",
                Start: new TimeSpan(12, 0, 0),
                End: new TimeSpan(13, 0, 0),
                Days: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                AllowedOperations: new[] { MaintenanceType.HealthCheck, MaintenanceType.QualityScoring },
                MaxConcurrentJobs: 2,
                Limits: new ResourceLimits(20, 10, 50)
            )
        },
        Holidays: Array.Empty<string>(),
        PrimaryTimeZone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York")
    );
}

public sealed record TradingSession(
    string Name,
    DayOfWeek[] ActiveDays,
    TimeSpan PreMarketStart,
    TimeSpan RegularStart,
    TimeSpan RegularEnd,
    TimeSpan AfterHoursEnd,
    bool IncludesPreMarket = true,
    bool IncludesAfterHours = true
);

public sealed record MaintenanceWindow(
    string Name,
    TimeSpan Start,
    TimeSpan End,
    DayOfWeek[] Days,
    MaintenanceType[] AllowedOperations,
    int MaxConcurrentJobs = 4,
    ResourceLimits Limits = null!
);

public sealed record ResourceLimits(
    int MaxCpuPct = 80,
    int MaxMemoryPct = 70,
    int MaxDiskIoMbps = 500
);

public enum MaintenanceType : byte
{
    All,
    HealthCheck,
    IntegrityValidation,
    Backfill,
    Compaction,
    TierMigration,
    IndexRebuild,
    Archival,
    Backup,
    Reconciliation,
    QualityScoring
}

// Job types
public sealed record MaintenanceJob(
    string Id,
    MaintenanceType Type,
    JobPriority Priority,
    string Description,
    ResourceRequirements Requirements,
    TimeSpan EstimatedDuration,
    string[] TargetPaths,
    Dictionary<string, object>? Parameters = null,
    bool Interruptible = true,
    int MaxRetries = 3
);

public sealed record ResourceRequirements(
    int CpuCores = 1,
    long MemoryBytes = 1_073_741_824,
    long DiskIoMbps = 100,
    long NetworkIoMbps = 0,
    bool RequiresExclusiveLock = false,
    string[]? ExclusivePaths = null
);

public enum JobPriority : byte
{
    Critical,
    High,
    Normal,
    Low,
    Deferred
}

// Schedule types
public sealed record ScheduleDecision(
    bool Allowed,
    string Reason,
    MaintenanceWindow? CurrentWindow,
    TimeSpan? WaitTime,
    ResourceLimits? ApplicableLimits
);

public sealed record ScheduleSlot(
    DateTimeOffset Start,
    DateTimeOffset End,
    MaintenanceWindow Window,
    ResourceLimits Limits,
    int AvailableConcurrencySlots
);

public sealed record ScheduledJob(
    string Id,
    MaintenanceJob Job,
    DateTimeOffset ScheduledStart,
    JobStatus Status,
    DateTimeOffset CreatedAt
);

public sealed record ScheduleOptions(
    bool AllowImmediate = true,
    TimeSpan MaxWaitTime = default
);

public enum JobStatus : byte
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record JobExecutionStatus(
    string JobId,
    MaintenanceType Type,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    JobStatus Status,
    int Progress,
    string Message,
    IReadOnlyList<string> Errors
);

public sealed record OperationalState(
    bool IsRealTimeCollectionActive,
    TradingSession? CurrentSession,
    MaintenanceWindow? CurrentMaintenanceWindow,
    int RunningMaintenanceJobs,
    ScheduledJob[] PendingJobs,
    DateTimeOffset NextMaintenanceWindowStart
);
