using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for scheduling and managing batch export jobs.
/// Implements Feature #40: Batch Export Scheduler
/// </summary>
public sealed class BatchExportSchedulerService : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, ExportJob> _jobs = new();
    private readonly ConcurrentQueue<(ExportJob Job, long Version)> _queue = new();
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly SemaphoreSlim _workerSemaphore;
    public Exception? LastPersistenceError { get; private set; }
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _workers = new();
    private readonly string _jobStorePath;
    private readonly int _queuePollIntervalMs;
    private Timer? _schedulerTimer;
    private int _disposeState;

    public BatchExportSchedulerService(int maxConcurrentJobs = 4, string? jobStorePath = null, int queuePollIntervalMs = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queuePollIntervalMs);
        _workerSemaphore = new SemaphoreSlim(maxConcurrentJobs, maxConcurrentJobs);
        _jobStorePath = jobStorePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian", "export_jobs.json");
        _queuePollIntervalMs = queuePollIntervalMs;
    }

    /// <summary>
    /// Event raised when a job starts.
    /// </summary>
    public event EventHandler<ExportJobEventArgs>? JobStarted;

    /// <summary>
    /// Event raised when a job completes.
    /// </summary>
    public event EventHandler<ExportJobEventArgs>? JobCompleted;

    /// <summary>
    /// Event raised when a job fails.
    /// </summary>
    public event EventHandler<ExportJobEventArgs>? JobFailed;

    /// <summary>
    /// Event raised when job progress updates.
    /// </summary>
    public event EventHandler<ExportJobProgressEventArgs>? JobProgress;

    /// <summary>
    /// Gets all jobs.
    /// </summary>
    public IReadOnlyDictionary<string, ExportJob> Jobs => _jobs;

    /// <summary>
    /// Starts the scheduler.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await LoadJobsAsync();

        // Start worker tasks
        for (int i = 0; i < 4; i++)
        {
            _workers.Add(ProcessQueueAsync(_cts.Token));
        }

        // Start scheduler timer (check every minute for scheduled jobs)
        _schedulerTimer = new Timer(CheckScheduledJobs, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Stops the scheduler.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _schedulerTimer?.Dispose();
        _cts.Cancel();
        await Task.WhenAll(_workers).ConfigureAwait(false);
        await SaveJobsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    public ExportJob CreateJob(ExportJobRequest request)
    {
        if (request.Format is not (ExportFormat.Raw or ExportFormat.JsonLines or ExportFormat.Csv))
            throw new NotSupportedException($"Export format {request.Format} is not supported.");
        var job = new ExportJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name,
            SourcePath = request.SourcePath,
            DestinationPath = request.DestinationPath,
            Symbols = request.Symbols,
            EventTypes = request.EventTypes,
            DateRange = request.DateRange,
            Format = request.Format,
            Schedule = request.Schedule,
            IncrementalMode = request.IncrementalMode,
            Priority = request.Priority,
            CreatedAt = DateTime.UtcNow,
            Status = ExportJobStatus.Pending
        };

        lock (_stateGate)
        {
            _jobs.TryAdd(job.Id, job);
            if (request.Schedule == null)
                QueueJobNoLock(job);
        }
        SaveJobs();

        return job;
    }

    /// <summary>
    /// Queues a job for immediate execution.
    /// </summary>
    public bool QueueJob(string jobId)
    {
        lock (_stateGate)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || !QueueJobNoLock(job))
                return false;
        }
        SaveJobs();
        return true;
    }

    private bool QueueJobNoLock(ExportJob job)
    {
        if (job.Status is ExportJobStatus.Running or ExportJobStatus.Queued || job.CancellationSource != null)
            return false;
        job.Status = ExportJobStatus.Queued;
        _queue.Enqueue((job, ++job.QueueVersion));
        return true;
    }

    /// <summary>Cancels a running or queued job.</summary>
    public bool CancelJob(string jobId)
    {
        lock (_stateGate)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return false;
            job.CancellationSource?.Cancel();
            job.Status = ExportJobStatus.Cancelled;
        }
        SaveJobs();
        return true;
    }

    /// <summary>Removes a job from the system.</summary>
    public bool RemoveJob(string jobId)
    {
        lock (_stateGate)
        {
            if (!_jobs.TryRemove(jobId, out var job))
                return false;
            job.CancellationSource?.Cancel();
            job.Status = ExportJobStatus.Cancelled;
        }
        SaveJobs();
        return true;
    }

    /// <summary>
    /// Gets the status of a job.
    /// </summary>
    public ExportJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    /// <summary>
    /// Gets job history.
    /// </summary>
    public List<ExportJobRun> GetJobHistory(string jobId, int limit = 10)
    {
        lock (_stateGate)
        {
            return _jobs.TryGetValue(jobId, out var job)
                ? job.RunHistory.TakeLast(limit).Reverse().ToList()
                : new List<ExportJobRun>();
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _workerSemaphore.WaitAsync(ct);

                try
                {
                    if (_queue.TryDequeue(out var entry))
                        await ExecuteJobAsync(entry.Job, entry.Version, ct).ConfigureAwait(false);
                    else
                        await Task.Delay(_queuePollIntervalMs, ct).ConfigureAwait(false);
                }
                finally
                {
                    _workerSemaphore.Release();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Export worker failed: {0}", ex.Message);
            }
        }
    }

    private async Task ExecuteJobAsync(ExportJob job, long version, CancellationToken ct)
    {
        CancellationTokenSource linkedCts;
        lock (_stateGate)
        {
            if (job.Status != ExportJobStatus.Queued || job.QueueVersion != version || !_jobs.ContainsKey(job.Id))
                return;
            ct.ThrowIfCancellationRequested();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            job.CancellationSource = linkedCts;
            job.Status = ExportJobStatus.Running;
            job.LastRunAt = DateTime.UtcNow;
        }
        using var executionCancellation = linkedCts;

        var run = new ExportJobRun
        {
            StartedAt = job.LastRunAt!.Value
        };

        try
        {
            JobStarted?.Invoke(this, new ExportJobEventArgs(job));

            if (job.Format is not (ExportFormat.Raw or ExportFormat.JsonLines or ExportFormat.Csv))
                throw new NotSupportedException($"Export format {job.Format} is not supported.");

            // Get source files
            var sourceFiles = GetSourceFiles(job);
            run.TotalFiles = sourceFiles.Count;

            // Create destination directory
            var destPath = ExpandDestinationPath(job.DestinationPath, job);
            Directory.CreateDirectory(destPath);

            // Process files
            var processedFiles = 0;
            var totalBytes = 0L;

            foreach (var sourceFile in sourceFiles)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var destFile = GetDestinationFilePath(sourceFile, job.SourcePath, destPath, job);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                // Handle format conversion
                if (job.Format != ExportFormat.Raw)
                {
                    destFile = await ConvertAndExportAsync(sourceFile, destFile, job.Format, linkedCts.Token);
                }
                else
                {
                    File.Copy(sourceFile, destFile, true);
                }

                processedFiles++;
                totalBytes += new FileInfo(destFile).Length;

                // Report progress
                var progress = new ExportJobProgressEventArgs(
                    job,
                    processedFiles,
                    run.TotalFiles,
                    sourceFile
                );
                JobProgress?.Invoke(this, progress);
            }

            run.CompletedAt = DateTime.UtcNow;
            run.FilesExported = processedFiles;
            run.BytesExported = totalBytes;
            run.Success = true;
            run.DestinationPath = destPath;

            lock (_stateGate)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                job.Status = ExportJobStatus.Completed;
                job.LastSuccessAt = run.CompletedAt;
                job.TotalFilesExported += processedFiles;
                job.TotalBytesExported += totalBytes;
            }

            JobCompleted?.Invoke(this, new ExportJobEventArgs(job, run));
        }
        catch (OperationCanceledException)
        {
            run.CompletedAt = DateTime.UtcNow;
            run.Success = false;
            run.ErrorMessage = "Job cancelled";
            lock (_stateGate)
                job.Status = ExportJobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            run.CompletedAt = DateTime.UtcNow;
            run.Success = false;
            run.ErrorMessage = ex.Message;
            lock (_stateGate)
                job.Status = ExportJobStatus.Failed;

            JobFailed?.Invoke(this, new ExportJobEventArgs(job, run));
        }
        finally
        {
            lock (_stateGate)
            {
                job.RunHistory.Add(run);
                job.CancellationSource = null;
            }
            await SaveJobsAsync().ConfigureAwait(false);
        }
    }

    private List<string> GetSourceFiles(ExportJob job)
    {
        var files = new List<string>();
        var searchPatterns = new List<string>();

        // Build search patterns based on event types
        if (job.EventTypes?.Length > 0)
        {
            foreach (var type in job.EventTypes)
            {
                searchPatterns.Add($"*{type}*.jsonl*");
            }
        }
        else
        {
            searchPatterns.Add("*.jsonl*");
        }

        // Get files for each symbol
        var symbolPaths = job.Symbols?.Length > 0
            ? job.Symbols.Select(s => Path.Combine(job.SourcePath, s))
            : new[] { job.SourcePath };

        foreach (var symbolPath in symbolPaths)
        {
            if (!Directory.Exists(symbolPath))
                continue;

            foreach (var pattern in searchPatterns)
            {
                files.AddRange(
                    Directory.GetFiles(symbolPath, pattern, SearchOption.AllDirectories)
                        .Where(f => MatchesDateRange(f, job.DateRange))
                );
            }
        }

        // Handle incremental mode
        if (job.IncrementalMode && job.LastSuccessAt.HasValue)
        {
            files = files.Where(f =>
                File.GetLastWriteTimeUtc(f) > job.LastSuccessAt.Value).ToList();
        }

        return files.Distinct().OrderBy(f => f).ToList();
    }

    private static bool MatchesDateRange(string filePath, ExportDateRange? range)
    {
        if (range == null)
            return true;

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith(".jsonl"))
            fileName = Path.GetFileNameWithoutExtension(fileName);

        if (DateOnly.TryParse(fileName, out var fileDate))
        {
            if (range.StartDate.HasValue && fileDate < range.StartDate.Value)
                return false;
            if (range.EndDate.HasValue && fileDate > range.EndDate.Value)
                return false;
        }

        return true;
    }

    private static string ExpandDestinationPath(string template, ExportJob job)
    {
        var now = DateTime.UtcNow;
        return template
            .Replace("{year}", now.Year.ToString())
            .Replace("{month}", now.Month.ToString("D2"))
            .Replace("{day}", now.Day.ToString("D2"))
            .Replace("{job_id}", job.Id)
            .Replace("{job_name}", job.Name ?? job.Id);
    }

    private static string GetDestinationFilePath(
        string sourceFile,
        string sourceRoot,
        string destRoot,
        ExportJob job)
    {
        var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
        var destFile = Path.Combine(destRoot, relativePath);

        // Handle format changes
        if (job.Format == ExportFormat.Parquet)
        {
            destFile = Path.ChangeExtension(
                destFile.Replace(".jsonl.gz", ".jsonl"),
                ".parquet");
        }
        else if (job.Format == ExportFormat.Csv)
        {
            destFile = Path.ChangeExtension(
                destFile.Replace(".jsonl.gz", ".jsonl"),
                ".csv");
        }

        return destFile;
    }

    private static async Task<string> ConvertAndExportAsync(
        string sourceFile,
        string destFile,
        ExportFormat format,
        CancellationToken ct)
    {
        var lines = await ReadAllLinesAsync(sourceFile, ct);

        switch (format)
        {
            case ExportFormat.Csv:
                await ExportToCsvAsync(lines, destFile, ct);
                return destFile;

            case ExportFormat.Parquet:
                throw new NotSupportedException("Parquet export is not supported.");

            case ExportFormat.JsonLines:
                var decompressedPath = destFile.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? destFile[..^3] : destFile;
                await File.WriteAllLinesAsync(decompressedPath, lines, ct);
                return decompressedPath;

            default:
                throw new NotSupportedException($"Export format {format} is not supported.");
        }
    }

    private static async Task<List<string>> ReadAllLinesAsync(string filePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        Stream readStream = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(stream, CompressionMode.Decompress)
            : stream;

        using var reader = new StreamReader(readStream);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            lines.Add(line);
        }
        return lines;
    }

    private static async Task ExportToCsvAsync(List<string> jsonLines, string destFile, CancellationToken ct)
    {
        var headers = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var rejectedRows = new List<int>();
        // Discover the union schema and validate every row before replacing an artifact.
        for (var i = 0; i < jsonLines.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(jsonLines[i]);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    rejectedRows.Add(i + 1);
                    continue;
                }
                var rowNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (!rowNames.Add(property.Name))
                    {
                        rejectedRows.Add(i + 1);
                        break;
                    }
                    if (names.Add(property.Name))
                        headers.Add(property.Name);
                }
            }
            catch (JsonException)
            {
                rejectedRows.Add(i + 1);
            }
        }
        if (rejectedRows.Count > 0)
            throw new InvalidDataException($"CSV export rejected {rejectedRows.Count} row(s): {string.Join(", ", rejectedRows.Take(20))}.");

        var csvLines = new List<string>();
        if (headers.Count > 0)
            csvLines.Add(string.Join(",", headers.Select(EscapeCsvCell)));
        foreach (var line in jsonLines)
        {
            ct.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(line);
            csvLines.Add(string.Join(",", headers.Select(header =>
                EscapeCsvCell(doc.RootElement.TryGetProperty(header, out var property)
                    ? property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.GetRawText()
                    : ""))));
        }
        await AtomicFileWriter.WriteAsync(destFile, string.Join(Environment.NewLine, csvLines), ct).ConfigureAwait(false);
    }

    private static string EscapeCsvCell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private void CheckScheduledJobs(object? state)
    {
        try
        {
            var changed = false;
            lock (_stateGate)
            {
                if (_cts.IsCancellationRequested)
                    return;
                foreach (var job in _jobs.Values)
                {
                    if (job.Status != ExportJobStatus.Cancelled && ShouldRunScheduledJob(job, DateTime.UtcNow))
                        changed |= QueueJobNoLock(job);
                }
            }
            if (changed)
                SaveJobs();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Export scheduler failed: {0}", ex.Message);
        }
    }

    private static bool ShouldRunScheduledJob(ExportJob job, DateTime now)
    {
        if (job.Schedule == null)
            return false;

        var lastRun = job.LastRunAt ?? DateTime.MinValue;
        var nextRun = GetNextRunTime(lastRun, job.Schedule);

        return now >= nextRun;
    }

    private static DateTime GetNextRunTime(DateTime lastRun, ExportSchedule schedule)
    {
        return schedule.Frequency switch
        {
            ScheduleFrequency.Hourly => lastRun.AddHours(1),
            ScheduleFrequency.Daily => lastRun.AddDays(1).Date.Add(schedule.TimeOfDay ?? TimeSpan.Zero),
            ScheduleFrequency.Weekly => lastRun.AddDays(7),
            ScheduleFrequency.Monthly => lastRun.AddMonths(1),
            _ => lastRun.AddDays(1)
        };
    }

    private async Task LoadJobsAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_jobStorePath))
            {
                var json = await File.ReadAllTextAsync(_jobStorePath, ct);
                var jobs = JsonSerializer.Deserialize<List<ExportJob>>(json, DesktopJsonOptions.PrettyPrint);
                if (jobs != null)
                {
                    foreach (var job in jobs)
                    {
                        if (job.Status == ExportJobStatus.Running)
                            job.Status = ExportJobStatus.Pending;
                        lock (_stateGate)
                        {
                            if (_jobs.TryAdd(job.Id, job) && job.Status == ExportJobStatus.Queued)
                            {
                                job.Status = ExportJobStatus.Pending;
                                QueueJobNoLock(job);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Failed to load export jobs from {0}: {1}", _jobStorePath, ex.Message);
        }
    }

    /// <summary>
    /// Reads the persisted export jobs without starting the scheduler workers.
    /// This is used by shell surfaces that need job visibility but do not own
    /// export execution.
    /// </summary>
    public async Task<IReadOnlyList<ExportJob>> ReadPersistedJobsAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_jobStorePath))
            {
                return Array.Empty<ExportJob>();
            }

            var json = await File.ReadAllTextAsync(_jobStorePath, ct).ConfigureAwait(false);
            var jobs = JsonSerializer.Deserialize<List<ExportJob>>(json, DesktopJsonOptions.PrettyPrint) ?? [];

            foreach (var job in jobs)
            {
                if (job.Status == ExportJobStatus.Running)
                {
                    job.Status = ExportJobStatus.Pending;
                }
            }

            return jobs
                .OrderByDescending(job => job.LastRunAt ?? job.CreatedAt)
                .ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Failed to read persisted export jobs from {0}: {1}", _jobStorePath, ex.Message);
            return Array.Empty<ExportJob>();
        }
    }

    /// <summary>Persists the latest job state; storage errors propagate to the caller.</summary>
    public async Task SaveJobsAsync(CancellationToken ct = default)
    {
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string json;
            lock (_stateGate)
                json = JsonSerializer.Serialize(_jobs.Values.ToList(), DesktopJsonOptions.PrettyPrint);
            // The synchronous compatibility APIs share this gate; do not let a writer
            // capture a UI context that may be synchronously waiting for the gate.
            await Task.Run(() => AtomicFileWriter.Write(_jobStorePath, json, ct), ct).ConfigureAwait(false);
            LastPersistenceError = null;
        }
        catch (Exception ex)
        {
            LastPersistenceError = ex;
            throw;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void SaveJobs()
    {
        _saveGate.Wait();
        try
        {
            string json;
            lock (_stateGate)
                json = JsonSerializer.Serialize(_jobs.Values.ToList(), DesktopJsonOptions.PrettyPrint);
            AtomicFileWriter.Write(_jobStorePath, json);
            LastPersistenceError = null;
        }
        catch (Exception ex)
        {
            LastPersistenceError = ex;
            throw;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        DisposeCoreAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _workerSemaphore.Dispose();
    }
}


public sealed class ExportJob
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string SourcePath { get; init; } = "";
    public string DestinationPath { get; init; } = "";
    public string[]? Symbols { get; init; }
    public string[]? EventTypes { get; init; }
    public ExportDateRange? DateRange { get; init; }
    public ExportFormat Format { get; init; }
    public ExportSchedule? Schedule { get; init; }
    public bool IncrementalMode { get; init; }
    public ExportPriority Priority { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public ExportJobStatus Status { get; set; }
    public int TotalFilesExported { get; set; }
    public long TotalBytesExported { get; set; }
    public List<ExportJobRun> RunHistory { get; init; } = new();

    internal long QueueVersion { get; set; }
    internal CancellationTokenSource? CancellationSource { get; set; }
}

public sealed record ExportJobRequest
{
    public string? Name { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public string[]? Symbols { get; init; }
    public string[]? EventTypes { get; init; }
    public ExportDateRange? DateRange { get; init; }
    public ExportFormat Format { get; init; } = ExportFormat.Raw;
    public ExportSchedule? Schedule { get; init; }
    public bool IncrementalMode { get; init; }
    public ExportPriority Priority { get; init; } = ExportPriority.Normal;
}

public sealed record ExportDateRange
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    public static ExportDateRange LastNDays(int days) => new()
    {
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days)),
        EndDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    public static ExportDateRange Yesterday => LastNDays(1);
    public static ExportDateRange LastWeek => LastNDays(7);
    public static ExportDateRange LastMonth => LastNDays(30);
}

public sealed record ExportSchedule
{
    public ScheduleFrequency Frequency { get; init; }
    public TimeSpan? TimeOfDay { get; init; }
    public DayOfWeek? DayOfWeek { get; init; }
    public int? DayOfMonth { get; init; }
}

public sealed record ExportJobRun
{
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public int TotalFiles { get; set; }
    public int FilesExported { get; set; }
    public long BytesExported { get; set; }
    public string? DestinationPath { get; set; }
    public string? ErrorMessage { get; set; }

    public TimeSpan? Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : null;
}

public enum ExportJobStatus : byte
{
    Pending,
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum ExportFormat : byte
{
    Raw,
    JsonLines,
    Csv,
    Parquet
}

public enum ExportPriority : byte
{
    Low,
    Normal,
    High
}

public enum ScheduleFrequency : byte
{
    Hourly,
    Daily,
    Weekly,
    Monthly
}

public sealed class ExportJobEventArgs : EventArgs
{
    public ExportJob Job { get; }
    public ExportJobRun? Run { get; }

    public ExportJobEventArgs(ExportJob job, ExportJobRun? run = null)
    {
        Job = job;
        Run = run;
    }
}

public sealed class ExportJobProgressEventArgs : EventArgs
{
    public ExportJob Job { get; }
    public int FilesProcessed { get; }
    public int TotalFiles { get; }
    public string CurrentFile { get; }
    public int PercentComplete => TotalFiles > 0 ? (int)(100.0 * FilesProcessed / TotalFiles) : 0;

    public ExportJobProgressEventArgs(
        ExportJob job,
        int filesProcessed,
        int totalFiles,
        string currentFile)
    {
        Job = job;
        FilesProcessed = filesProcessed;
        TotalFiles = totalFiles;
        CurrentFile = currentFile;
    }
}

