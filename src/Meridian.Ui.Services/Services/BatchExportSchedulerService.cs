using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for scheduling and managing batch export jobs.
/// Implements Feature #40: Batch Export Scheduler
/// </summary>
public sealed class BatchExportSchedulerService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ExportJob> _jobs = new();
    private readonly ConcurrentQueue<ExportJob> _queue = new();
    private readonly SemaphoreSlim _workerSemaphore;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _workers = new();
    private readonly string _jobStorePath;
    private Timer? _schedulerTimer;

    public BatchExportSchedulerService(int maxConcurrentJobs = 4, string? jobStorePath = null)
    {
        _workerSemaphore = new SemaphoreSlim(maxConcurrentJobs, maxConcurrentJobs);
        _jobStorePath = jobStorePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian", "export_jobs.json");
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
        await Task.WhenAll(_workers);
    }

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    public ExportJob CreateJob(ExportJobRequest request)
    {
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

        _jobs.TryAdd(job.Id, job);
        SaveJobsAsync().ConfigureAwait(false);

        if (request.Schedule == null)
        {
            // Immediate execution
            _queue.Enqueue(job);
        }

        return job;
    }

    /// <summary>
    /// Queues a job for immediate execution.
    /// </summary>
    public bool QueueJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job) && job.Status != ExportJobStatus.Running)
        {
            job.Status = ExportJobStatus.Queued;
            _queue.Enqueue(job);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Cancels a running or queued job.
    /// </summary>
    public bool CancelJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.Status == ExportJobStatus.Running)
            {
                job.CancellationSource?.Cancel();
            }
            job.Status = ExportJobStatus.Cancelled;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a job from the system.
    /// </summary>
    public bool RemoveJob(string jobId)
    {
        CancelJob(jobId);
        return _jobs.TryRemove(jobId, out _);
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
        if (_jobs.TryGetValue(jobId, out var job))
        {
            return job.RunHistory.TakeLast(limit).Reverse().ToList();
        }
        return new List<ExportJobRun>();
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _workerSemaphore.WaitAsync(ct);

                if (_queue.TryDequeue(out var job))
                {
                    await ExecuteJobAsync(job, ct);
                }
                else
                {
                    _workerSemaphore.Release();
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                _workerSemaphore.Release();
            }
        }
    }

    private async Task ExecuteJobAsync(ExportJob job, CancellationToken ct)
    {
        job.CancellationSource = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, job.CancellationSource.Token);

        var run = new ExportJobRun
        {
            StartedAt = DateTime.UtcNow
        };

        try
        {
            job.Status = ExportJobStatus.Running;
            job.LastRunAt = run.StartedAt;
            JobStarted?.Invoke(this, new ExportJobEventArgs(job));

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
                    await ConvertAndExportAsync(sourceFile, destFile, job.Format, linkedCts.Token);
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

            job.Status = ExportJobStatus.Completed;
            job.LastSuccessAt = run.CompletedAt;
            job.TotalFilesExported += processedFiles;
            job.TotalBytesExported += totalBytes;

            JobCompleted?.Invoke(this, new ExportJobEventArgs(job, run));
        }
        catch (OperationCanceledException)
        {
            run.CompletedAt = DateTime.UtcNow;
            run.Success = false;
            run.ErrorMessage = "Job cancelled";
            job.Status = ExportJobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            run.CompletedAt = DateTime.UtcNow;
            run.Success = false;
            run.ErrorMessage = ex.Message;
            job.Status = ExportJobStatus.Failed;

            JobFailed?.Invoke(this, new ExportJobEventArgs(job, run));
        }
        finally
        {
            job.RunHistory.Add(run);
            _workerSemaphore.Release();
            await SaveJobsAsync();
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

    private static async Task ConvertAndExportAsync(
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
                break;

            case ExportFormat.Parquet:
                // For now, just copy - would integrate Apache.Arrow or Parquet.Net
                await File.WriteAllLinesAsync(destFile.Replace(".parquet", ".jsonl"), lines, ct);
                break;

            case ExportFormat.JsonLines:
                var decompressedPath = destFile.Replace(".gz", "");
                await File.WriteAllLinesAsync(decompressedPath, lines, ct);
                break;

            default:
                File.Copy(sourceFile, destFile, true);
                break;
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
        if (jsonLines.Count == 0)
        {
            await File.WriteAllTextAsync(destFile, "", ct);
            return;
        }

        // Parse first line to get headers
        using var firstDoc = JsonDocument.Parse(jsonLines[0]);
        var headers = firstDoc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        var csvLines = new List<string> { string.Join(",", headers) };

        foreach (var line in jsonLines)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(line);
                var values = headers.Select(h =>
                {
                    if (doc.RootElement.TryGetProperty(h, out var prop))
                    {
                        var val = prop.ValueKind == JsonValueKind.String
                            ? $"\"{prop.GetString()?.Replace("\"", "\"\"")}\""
                            : prop.GetRawText();
                        return val;
                    }
                    return "";
                });
                csvLines.Add(string.Join(",", values));
            }
            catch
            {
                // Skip malformed lines
            }
        }

        await File.WriteAllLinesAsync(destFile, csvLines, ct);
    }

    private void CheckScheduledJobs(object? state)
    {
        var now = DateTime.UtcNow;

        foreach (var (_, job) in _jobs)
        {
            if (job.Schedule != null && job.Status != ExportJobStatus.Running)
            {
                if (ShouldRunScheduledJob(job, now))
                {
                    job.Status = ExportJobStatus.Queued;
                    _queue.Enqueue(job);
                }
            }
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
                var json = await File.ReadAllTextAsync(_jobStorePath);
                var jobs = JsonSerializer.Deserialize<List<ExportJob>>(json);
                if (jobs != null)
                {
                    foreach (var job in jobs)
                    {
                        if (job.Status == ExportJobStatus.Running)
                            job.Status = ExportJobStatus.Pending;
                        _jobs.TryAdd(job.Id, job);
                    }
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    private async Task SaveJobsAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_jobStorePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_jobs.Values.ToList(), DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(_jobStorePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
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

