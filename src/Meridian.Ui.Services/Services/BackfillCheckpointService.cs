using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service that tracks backfill job progress with checkpoint/resume support.
/// Persists per-symbol progress to disk so interrupted jobs can be resumed.
/// </summary>
public sealed class BackfillCheckpointService
{
    private static readonly Lazy<BackfillCheckpointService> _instance = new(() => new BackfillCheckpointService());
    private readonly string _checkpointDir;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static BackfillCheckpointService Instance => _instance.Value;

    private BackfillCheckpointService()
    {
        _checkpointDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "backfill-checkpoints");

        if (!Directory.Exists(_checkpointDir))
        {
            Directory.CreateDirectory(_checkpointDir);
        }
    }

    /// <summary>
    /// Creates a new checkpoint for a backfill job.
    /// </summary>
    public async Task<BackfillCheckpoint> CreateCheckpointAsync(
        string jobId,
        string provider,
        string[] symbols,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default)
    {
        var checkpoint = new BackfillCheckpoint
        {
            JobId = jobId,
            Provider = provider,
            FromDate = fromDate,
            ToDate = toDate,
            CreatedAt = DateTime.UtcNow,
            Status = CheckpointStatus.InProgress,
            SymbolCheckpoints = symbols.Select(s => new SymbolCheckpoint
            {
                Symbol = s,
                Status = SymbolCheckpointStatus.Pending,
                BarsDownloaded = 0,
                RetryCount = 0
            }).ToList()
        };

        await SaveCheckpointAsync(checkpoint, ct);
        return checkpoint;
    }

    /// <summary>
    /// Updates the progress for a specific symbol within a job.
    /// </summary>
    public async Task UpdateSymbolProgressAsync(
        string jobId,
        string symbol,
        SymbolCheckpointStatus status,
        int barsDownloaded,
        string? lastDate = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var checkpoint = await LoadCheckpointAsync(jobId, ct);
        if (checkpoint == null) return;

        var symbolCp = checkpoint.SymbolCheckpoints.FirstOrDefault(
            s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (symbolCp != null)
        {
            symbolCp.Status = status;
            symbolCp.BarsDownloaded = barsDownloaded;
            symbolCp.LastProcessedDate = lastDate;
            symbolCp.ErrorMessage = errorMessage;
            symbolCp.LastUpdated = DateTime.UtcNow;

            if (status == SymbolCheckpointStatus.Completed)
            {
                symbolCp.CompletedAt = DateTime.UtcNow;
            }
            else if (status == SymbolCheckpointStatus.Failed)
            {
                symbolCp.RetryCount++;
            }
        }

        // Update overall status
        var allCompleted = checkpoint.SymbolCheckpoints.All(
            s => s.Status is SymbolCheckpointStatus.Completed or SymbolCheckpointStatus.Skipped);
        var anyFailed = checkpoint.SymbolCheckpoints.Any(
            s => s.Status == SymbolCheckpointStatus.Failed);

        if (allCompleted)
        {
            checkpoint.Status = CheckpointStatus.Completed;
            checkpoint.CompletedAt = DateTime.UtcNow;
        }
        else if (anyFailed && checkpoint.SymbolCheckpoints.All(
            s => s.Status is SymbolCheckpointStatus.Completed or SymbolCheckpointStatus.Failed or SymbolCheckpointStatus.Skipped))
        {
            checkpoint.Status = CheckpointStatus.PartiallyCompleted;
            checkpoint.CompletedAt = DateTime.UtcNow;
        }

        checkpoint.TotalBarsDownloaded = checkpoint.SymbolCheckpoints.Sum(s => s.BarsDownloaded);
        await SaveCheckpointAsync(checkpoint, ct);
    }

    /// <summary>
    /// Marks a job as failed.
    /// </summary>
    public async Task MarkJobFailedAsync(
        string jobId,
        string errorMessage,
        CancellationToken ct = default)
    {
        var checkpoint = await LoadCheckpointAsync(jobId, ct);
        if (checkpoint == null) return;

        checkpoint.Status = CheckpointStatus.Failed;
        checkpoint.ErrorMessage = errorMessage;
        checkpoint.CompletedAt = DateTime.UtcNow;

        await SaveCheckpointAsync(checkpoint, ct);
    }

    /// <summary>
    /// Gets all incomplete jobs that can be resumed.
    /// </summary>
    public async Task<IReadOnlyList<BackfillCheckpoint>> GetResumableJobsAsync(
        CancellationToken ct = default)
    {
        var resumable = new List<BackfillCheckpoint>();

        if (!Directory.Exists(_checkpointDir)) return resumable;

        foreach (var file in Directory.GetFiles(_checkpointDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var checkpoint = JsonSerializer.Deserialize<BackfillCheckpoint>(json, _jsonOptions);

                if (checkpoint != null &&
                    checkpoint.Status is CheckpointStatus.InProgress or CheckpointStatus.Failed or CheckpointStatus.PartiallyCompleted)
                {
                    resumable.Add(checkpoint);
                }
            }
            catch
            {
                // Skip corrupted checkpoint files
            }
        }

        return resumable.OrderByDescending(c => c.CreatedAt).ToList();
    }

    /// <summary>
    /// Gets the symbols that still need processing for a resumable job.
    /// </summary>
    public async Task<string[]> GetPendingSymbolsAsync(
        string jobId,
        CancellationToken ct = default)
    {
        var checkpoint = await LoadCheckpointAsync(jobId, ct);
        if (checkpoint == null) return Array.Empty<string>();

        return checkpoint.SymbolCheckpoints
            .Where(s => s.Status is SymbolCheckpointStatus.Pending or SymbolCheckpointStatus.Failed)
            .Select(s => s.Symbol)
            .ToArray();
    }

    /// <summary>
    /// Gets the last checkpoint for a specific job.
    /// </summary>
    public Task<BackfillCheckpoint?> LoadCheckpointAsync(
        string jobId,
        CancellationToken ct = default)
    {
        return LoadCheckpointFromFileAsync(GetCheckpointPath(jobId), ct);
    }

    /// <summary>
    /// Gets execution history with optional limit.
    /// </summary>
    public async Task<IReadOnlyList<BackfillCheckpoint>> GetHistoryAsync(
        int limit = 20,
        CancellationToken ct = default)
    {
        var all = new List<BackfillCheckpoint>();

        if (!Directory.Exists(_checkpointDir)) return all;

        foreach (var file in Directory.GetFiles(_checkpointDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var checkpoint = JsonSerializer.Deserialize<BackfillCheckpoint>(json, _jsonOptions);
                if (checkpoint != null)
                {
                    all.Add(checkpoint);
                }
            }
            catch
            {
                // Skip corrupted files
            }
        }

        return all.OrderByDescending(c => c.CreatedAt).Take(limit).ToList();
    }

    /// <summary>
    /// Deletes old completed checkpoints beyond retention period.
    /// </summary>
    public Task CleanupOldCheckpointsAsync(
        int retentionDays = 30,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(_checkpointDir)) return Task.CompletedTask;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var file in Directory.GetFiles(_checkpointDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Skip files we can't access
            }
        }

        return Task.CompletedTask;
    }

    private string GetCheckpointPath(string jobId) =>
        Path.Combine(_checkpointDir, $"{jobId}.json");

    private async Task SaveCheckpointAsync(BackfillCheckpoint checkpoint, CancellationToken ct)
    {
        var path = GetCheckpointPath(checkpoint.JobId);
        var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private async Task<BackfillCheckpoint?> LoadCheckpointFromFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<BackfillCheckpoint>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Represents a full backfill job checkpoint with per-symbol progress.
/// </summary>
public sealed class BackfillCheckpoint
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("fromDate")]
    public DateTime FromDate { get; set; }

    [JsonPropertyName("toDate")]
    public DateTime ToDate { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("status")]
    public CheckpointStatus Status { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("totalBarsDownloaded")]
    public int TotalBarsDownloaded { get; set; }

    [JsonPropertyName("symbolCheckpoints")]
    public List<SymbolCheckpoint> SymbolCheckpoints { get; set; } = new();

    /// <summary>
    /// Gets the number of completed symbols.
    /// </summary>
    [JsonIgnore]
    public int CompletedCount => SymbolCheckpoints.Count(
        s => s.Status == SymbolCheckpointStatus.Completed);

    /// <summary>
    /// Gets the number of failed symbols.
    /// </summary>
    [JsonIgnore]
    public int FailedCount => SymbolCheckpoints.Count(
        s => s.Status == SymbolCheckpointStatus.Failed);

    /// <summary>
    /// Gets the number of pending symbols.
    /// </summary>
    [JsonIgnore]
    public int PendingCount => SymbolCheckpoints.Count(
        s => s.Status is SymbolCheckpointStatus.Pending or SymbolCheckpointStatus.Failed);

    /// <summary>
    /// Gets overall progress as a percentage.
    /// </summary>
    [JsonIgnore]
    public double ProgressPercent => SymbolCheckpoints.Count > 0
        ? CompletedCount * 100.0 / SymbolCheckpoints.Count
        : 0;
}

/// <summary>
/// Per-symbol checkpoint within a backfill job.
/// </summary>
public sealed class SymbolCheckpoint
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public SymbolCheckpointStatus Status { get; set; }

    [JsonPropertyName("barsDownloaded")]
    public int BarsDownloaded { get; set; }

    [JsonPropertyName("lastProcessedDate")]
    public string? LastProcessedDate { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Overall checkpoint status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CheckpointStatus : byte
{
    InProgress,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled
}

/// <summary>
/// Per-symbol checkpoint status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SymbolCheckpointStatus : byte
{
    Pending,
    Downloading,
    Completed,
    Failed,
    Skipped
}
