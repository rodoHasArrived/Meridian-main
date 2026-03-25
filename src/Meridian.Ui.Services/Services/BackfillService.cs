using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing backfill operations with progress tracking.
/// Uses real API integration with the core Meridian service.
/// </summary>
public sealed class BackfillService
{
    private static readonly Lazy<BackfillService> _instance = new(() => new BackfillService());
    private readonly NotificationService _notificationService;
    private readonly BackfillApiService _backfillApiService;
    private readonly BackfillCheckpointService _checkpointService;
    private BackfillProgress? _currentProgress;
    private CancellationTokenSource? _cancellationTokenSource;
    private DateTime _startTime;
    private int _totalBarsDownloaded;
    private readonly object _progressLock = new();
    private string? _currentCheckpointJobId;

    // Polling configuration for progress updates
    private const int ProgressPollIntervalMs = 1000;
    private const int MaxPollAttempts = 3600; // 1 hour max at 1 second intervals

    public static BackfillService Instance => _instance.Value;

    private BackfillService()
    {
        _notificationService = NotificationService.Instance;
        _backfillApiService = new BackfillApiService();
        _checkpointService = BackfillCheckpointService.Instance;
    }

    /// <summary>
    /// Public constructor for direct instantiation.
    /// </summary>
    public BackfillService(bool useInstance = false)
    {
        if (useInstance)
        {
            throw new InvalidOperationException("Use BackfillService.Instance for singleton access");
        }
        _notificationService = NotificationService.Instance;
        _backfillApiService = new BackfillApiService();
        _checkpointService = BackfillCheckpointService.Instance;
    }

    /// <summary>
    /// Gets the current backfill progress.
    /// </summary>
    public BackfillProgress? CurrentProgress => _currentProgress;

    /// <summary>
    /// Gets whether a backfill is currently running.
    /// </summary>
    public bool IsRunning => _currentProgress?.Status == "Running";

    /// <summary>
    /// Gets whether a backfill is paused.
    /// </summary>
    public bool IsPaused => _currentProgress?.Status == "Paused";

    /// <summary>
    /// Gets the download speed in bars per second.
    /// </summary>
    public double BarsPerSecond
    {
        get
        {
            if (_currentProgress == null || !IsRunning)
                return 0;
            var elapsed = DateTime.UtcNow - _startTime;
            return elapsed.TotalSeconds > 0
                ? _totalBarsDownloaded / elapsed.TotalSeconds
                : 0;
        }
    }

    /// <summary>
    /// Gets the estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (_currentProgress == null || !IsRunning)
                return null;
            var speed = BarsPerSecond;
            if (speed <= 0)
                return null;

            var remainingBars = _currentProgress.TotalBars - _currentProgress.DownloadedBars;
            return TimeSpan.FromSeconds(remainingBars / speed);
        }
    }

    /// <summary>
    /// Starts a new backfill operation using the real API.
    /// </summary>
    public async Task StartBackfillAsync(
        string[] symbols,
        string provider,
        DateTime fromDate,
        DateTime toDate,
        string granularity = "Daily",
        Action<BackfillProgress>? progressCallback = null, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A backfill operation is already running");
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _startTime = DateTime.UtcNow;
        _totalBarsDownloaded = 0;

        _currentProgress = new BackfillProgress
        {
            JobId = Guid.NewGuid().ToString(),
            Status = "Running",
            TotalSymbols = symbols.Length,
            StartedAt = DateTime.UtcNow,
            CurrentProvider = provider,
            SymbolProgress = symbols.Select(s => new SymbolBackfillProgress
            {
                Symbol = s,
                Status = "Pending"
            }).ToArray()
        };

        ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });

        // Create checkpoint for resumability
        var checkpoint = await _checkpointService.CreateCheckpointAsync(
            _currentProgress.JobId,
            provider,
            symbols,
            fromDate,
            toDate);
        _currentCheckpointJobId = checkpoint.JobId;

        try
        {
            await RunBackfillAsync(symbols, provider, fromDate, toDate, granularity, progressCallback, _cancellationTokenSource.Token);

            _currentProgress.Status = "Completed";
            _currentProgress.CompletedAt = DateTime.UtcNow;

            // Update checkpoint: mark all symbols completed
            foreach (var sym in symbols)
            {
                await _checkpointService.UpdateSymbolProgressAsync(
                    _currentCheckpointJobId, sym,
                    SymbolCheckpointStatus.Completed,
                    (int)(_currentProgress.DownloadedBars / Math.Max(1, symbols.Length)));
            }

            await _notificationService.NotifyBackfillCompleteAsync(
                true,
                _currentProgress.CompletedSymbols,
                (int)_currentProgress.DownloadedBars,
                DateTime.UtcNow - _startTime);

            BackfillCompleted?.Invoke(this, new BackfillCompletedEventArgs
            {
                Success = true,
                Progress = _currentProgress
            });
        }
        catch (OperationCanceledException)
        {
            _currentProgress.Status = "Cancelled";
            _currentProgress.CompletedAt = DateTime.UtcNow;

            if (_currentCheckpointJobId != null)
            {
                await _checkpointService.MarkJobFailedAsync(
                    _currentCheckpointJobId, "Cancelled by user");
            }

            BackfillCompleted?.Invoke(this, new BackfillCompletedEventArgs
            {
                Success = false,
                Progress = _currentProgress,
                WasCancelled = true
            });
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "Failed";
            _currentProgress.ErrorMessage = ex.Message;
            _currentProgress.CompletedAt = DateTime.UtcNow;

            if (_currentCheckpointJobId != null)
            {
                await _checkpointService.MarkJobFailedAsync(
                    _currentCheckpointJobId, ex.Message);
            }

            await _notificationService.NotifyBackfillCompleteAsync(
                false,
                _currentProgress.CompletedSymbols,
                (int)_currentProgress.DownloadedBars,
                DateTime.UtcNow - _startTime);

            BackfillCompleted?.Invoke(this, new BackfillCompletedEventArgs
            {
                Success = false,
                Progress = _currentProgress,
                Error = ex
            });
        }
        finally
        {
            ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
        }
    }

    private async Task RunBackfillAsync(
        string[] symbols,
        string provider,
        DateTime fromDate,
        DateTime toDate,
        string granularity,
        Action<BackfillProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        // Estimate total bars (rough estimate: ~252 trading days per year * date range)
        var tradingDays = (int)((toDate - fromDate).TotalDays * 252 / 365);
        _currentProgress!.TotalBars = symbols.Length * tradingDays;

        // Format dates for API
        var fromStr = fromDate.ToString(FormatHelpers.IsoDateFormat);
        var toStr = toDate.ToString(FormatHelpers.IsoDateFormat);

        // Call the real API to start the backfill
        var result = await _backfillApiService.RunBackfillAsync(
            provider,
            symbols,
            fromStr,
            toStr,
            granularity,
            cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException("Failed to connect to the Meridian service. Please ensure the service is running.");
        }

        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Backfill operation failed");
        }

        // Update progress based on API result
        lock (_progressLock)
        {
            _totalBarsDownloaded = result.BarsWritten;
            _currentProgress.DownloadedBars = result.BarsWritten;
            _currentProgress.CompletedSymbols = result.Symbols?.Length ?? symbols.Length;
            _currentProgress.BarsPerSecond = (float)BarsPerSecond;

            // Mark all symbols as completed based on API response
            if (_currentProgress.SymbolProgress != null)
            {
                var completedSymbols = result.Symbols ?? symbols;
                foreach (var symbolProgress in _currentProgress.SymbolProgress)
                {
                    if (completedSymbols.Contains(symbolProgress.Symbol))
                    {
                        symbolProgress.Status = "Completed";
                        symbolProgress.Progress = 100;
                        symbolProgress.CompletedAt = result.CompletedUtc?.DateTime;
                        symbolProgress.BarsDownloaded = result.BarsWritten / Math.Max(1, completedSymbols.Length);
                        symbolProgress.Provider = result.Provider;
                    }
                }
            }
        }

        ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
        progressCallback?.Invoke(_currentProgress);
    }

    /// <summary>
    /// Starts a quick gap-fill operation for immediate data gaps.
    /// </summary>
    public async Task StartGapFillAsync(
        string[] symbols,
        int lookbackDays = 30,
        Action<BackfillProgress>? progressCallback = null, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A backfill operation is already running");
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _startTime = DateTime.UtcNow;
        _totalBarsDownloaded = 0;

        _currentProgress = new BackfillProgress
        {
            JobId = Guid.NewGuid().ToString(),
            Status = "Running",
            TotalSymbols = symbols.Length,
            StartedAt = DateTime.UtcNow,
            CurrentProvider = "composite",
            SymbolProgress = symbols.Select(s => new SymbolBackfillProgress
            {
                Symbol = s,
                Status = "Pending"
            }).ToArray()
        };

        ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });

        try
        {
            var result = await _backfillApiService.RunGapFillAsync(
                symbols,
                lookbackDays,
                "High",
                _cancellationTokenSource.Token);

            if (result == null)
            {
                throw new InvalidOperationException("Gap-fill request failed - service may be unavailable");
            }

            _currentProgress.Status = "Completed";
            _currentProgress.CompletedAt = DateTime.UtcNow;
            _currentProgress.CompletedSymbols = symbols.Length;

            await _notificationService.NotifyBackfillCompleteAsync(
                true,
                symbols.Length,
                0,
                DateTime.UtcNow - _startTime);

            BackfillCompleted?.Invoke(this, new BackfillCompletedEventArgs
            {
                Success = true,
                Progress = _currentProgress
            });
        }
        catch (OperationCanceledException)
        {
            _currentProgress.Status = "Cancelled";
            _currentProgress.CompletedAt = DateTime.UtcNow;

            BackfillCompleted?.Invoke(this, new BackfillCompletedEventArgs
            {
                Success = false,
                Progress = _currentProgress,
                WasCancelled = true
            });
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "Failed";
            _currentProgress.ErrorMessage = ex.Message;
            _currentProgress.CompletedAt = DateTime.UtcNow;

            BackfillCompleted?.Invoke(this, new BackfillCompletedEventArgs
            {
                Success = false,
                Progress = _currentProgress,
                Error = ex
            });
        }
        finally
        {
            ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
            progressCallback?.Invoke(_currentProgress);
        }
    }

    /// <summary>
    /// Gets available backfill providers from the API.
    /// </summary>
    public async Task<List<BackfillProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        return await _backfillApiService.GetProvidersAsync(ct);
    }

    /// <summary>
    /// Gets backfill presets from the API.
    /// </summary>
    public async Task<List<BackfillPreset>> GetPresetsAsync(CancellationToken ct = default)
    {
        return await _backfillApiService.GetPresetsAsync(ct);
    }

    /// <summary>
    /// Checks provider health.
    /// </summary>
    public async Task<BackfillHealthResponse?> CheckProviderHealthAsync(CancellationToken ct = default)
    {
        return await _backfillApiService.CheckProviderHealthAsync(ct);
    }

    /// <summary>
    /// Gets execution history.
    /// </summary>
    public async Task<List<BackfillExecution>> GetExecutionHistoryAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _backfillApiService.GetExecutionHistoryAsync(limit, ct);
    }

    /// <summary>
    /// Gets backfill statistics.
    /// </summary>
    public async Task<BackfillStatistics?> GetStatisticsAsync(int? hours = null, CancellationToken ct = default)
    {
        return await _backfillApiService.GetStatisticsAsync(hours, ct);
    }

    /// <summary>
    /// Pauses the current backfill operation.
    /// </summary>
    public void Pause()
    {
        if (_currentProgress != null && _currentProgress.Status == "Running")
        {
            _currentProgress.Status = "Paused";
            ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
        }
    }

    /// <summary>
    /// Resumes a paused backfill operation.
    /// </summary>
    public void Resume()
    {
        if (_currentProgress != null && _currentProgress.Status == "Paused")
        {
            _currentProgress.Status = "Running";
            ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
        }
    }

    /// <summary>
    /// Cancels the current backfill operation.
    /// </summary>
    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// Reorders the symbol queue (drag-drop priority).
    /// </summary>
    public void ReorderQueue(int oldIndex, int newIndex)
    {
        if (_currentProgress?.SymbolProgress == null)
            return;

        var symbols = _currentProgress.SymbolProgress.ToList();
        if (oldIndex < 0 || oldIndex >= symbols.Count || newIndex < 0 || newIndex >= symbols.Count)
            return;

        // Only reorder pending symbols
        if (symbols[oldIndex].Status != "Pending")
            return;

        var item = symbols[oldIndex];
        symbols.RemoveAt(oldIndex);
        symbols.Insert(newIndex, item);
        _currentProgress.SymbolProgress = symbols.ToArray();

        ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
    }

    /// <summary>
    /// Gets a formatted ETA string.
    /// </summary>
    public string GetFormattedEta()
    {
        var eta = EstimatedTimeRemaining;
        if (!eta.HasValue)
            return "Calculating...";

        if (eta.Value.TotalHours >= 1)
            return $"{(int)eta.Value.TotalHours}h {eta.Value.Minutes}m remaining";
        if (eta.Value.TotalMinutes >= 1)
            return $"{eta.Value.Minutes}m {eta.Value.Seconds}s remaining";
        return $"{eta.Value.Seconds}s remaining";
    }

    /// <summary>
    /// Gets a formatted speed string.
    /// </summary>
    public string GetFormattedSpeed()
    {
        var speed = BarsPerSecond;
        if (speed >= 1000)
            return $"{speed / 1000:F1}k bars/s";
        return $"{speed:F0} bars/s";
    }

    /// <summary>
    /// Gets the last backfill status from the backend API.
    /// </summary>
    public Task<BackfillResultDto?> GetLastStatusAsync(CancellationToken ct = default)
        => _backfillApiService.GetLastStatusAsync(ct);

    /// <summary>
    /// Runs a backfill operation via the backend API (convenience overload).
    /// Delegates to <see cref="BackfillApiService.RunBackfillAsync"/> with Daily granularity.
    /// </summary>
    public Task<BackfillResultDto?> RunBackfillAsync(
        string provider,
        string[] symbols,
        string? from,
        string? to,
        CancellationToken ct = default)
        => _backfillApiService.RunBackfillAsync(provider, symbols, from, to, "Daily", ct);

    /// <summary>
    /// Gets historical bars for a symbol by reading from local storage.
    /// Falls back to triggering a backfill if no stored data is found.
    /// </summary>
    public async Task<List<HistoricalBar>> GetHistoricalBarsAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default)
    {
        var configService = new ConfigService();
        var config = await configService.LoadConfigAsync(ct);
        var dataRoot = config?.DataRoot ?? "data";

        // Try reading from local storage first
        var bars = await ReadStoredBarsAsync(dataRoot, symbol, fromDate, toDate, ct);
        if (bars.Count > 0)
            return bars;

        // No stored data found — attempt a backfill via the API
        try
        {
            var result = await _backfillApiService.RunBackfillAsync(
                "composite",
                new[] { symbol },
                fromDate.ToString(FormatHelpers.IsoDateFormat),
                toDate.ToString(FormatHelpers.IsoDateFormat),
                ct: ct);

            if (result?.Success == true && result.BarsWritten > 0)
            {
                // Re-read from storage after backfill
                bars = await ReadStoredBarsAsync(dataRoot, symbol, fromDate, toDate, ct);
            }
        }
        catch
        {
            // API unavailable — return whatever we have
        }

        return bars;
    }

    private static async Task<List<HistoricalBar>> ReadStoredBarsAsync(
        string dataRoot,
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        var bars = new List<HistoricalBar>();

        // Scan common storage layouts for bar data files
        var searchPaths = new[]
        {
            Path.Combine(dataRoot, "historical"),
            Path.Combine(dataRoot, symbol),
            Path.Combine(dataRoot, "live"),
            dataRoot
        };

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath))
                continue;

            var files = Directory.GetFiles(basePath, "*.jsonl*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var name = f.ToLowerInvariant();
                    return name.Contains(symbol.ToLowerInvariant()) &&
                           (name.Contains("bar") || name.Contains("historical") || name.Contains("ohlc"));
                })
                .ToArray();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                bars.AddRange(await ParseBarsFromFileAsync(file, symbol, fromDate, toDate, ct));
            }

            if (bars.Count > 0)
                break;
        }

        return bars.OrderBy(b => b.SessionDate).ToList();
    }

    private static async Task<List<HistoricalBar>> ParseBarsFromFileAsync(
        string filePath,
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        var bars = new List<HistoricalBar>();

        try
        {
            using var stream = File.OpenRead(filePath);
            Stream readStream = filePath.EndsWith(".gz")
                ? new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress)
                : stream;

            using var reader = new StreamReader(readStream);
            string? line;
            long seq = 0;

            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!TryGetDecimal(root, "Open", out var open) ||
                        !TryGetDecimal(root, "High", out var high) ||
                        !TryGetDecimal(root, "Low", out var low) ||
                        !TryGetDecimal(root, "Close", out var close))
                        continue;

                    DateOnly sessionDate;
                    if (root.TryGetProperty("SessionDate", out var sd) && sd.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        if (!DateOnly.TryParse(sd.GetString(), out sessionDate))
                            continue;
                    }
                    else if (root.TryGetProperty("Timestamp", out var ts) && ts.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        if (!DateTime.TryParse(ts.GetString(), out var dt))
                            continue;
                        sessionDate = DateOnly.FromDateTime(dt);
                    }
                    else
                    {
                        continue;
                    }

                    if (sessionDate < fromDate || sessionDate > toDate)
                        continue;

                    var volume = root.TryGetProperty("Volume", out var v) && v.TryGetInt64(out var vol) ? vol : 0L;
                    var source = root.TryGetProperty("Source", out var src) ? src.GetString() ?? "storage" : "storage";

                    bars.Add(new HistoricalBar(
                        Symbol: symbol,
                        SessionDate: sessionDate,
                        Open: open,
                        High: high,
                        Low: low,
                        Close: close,
                        Volume: volume,
                        Source: source,
                        SequenceNumber: seq++));
                }
                catch
                {
                    // Skip malformed lines
                }
            }
        }
        catch
        {
            // File read error — return whatever we've parsed so far
        }

        return bars;
    }

    private static bool TryGetDecimal(System.Text.Json.JsonElement root, string propertyName, out decimal value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var prop))
        {
            // Try lowercase variant
            var lower = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
            if (!root.TryGetProperty(lower, out prop))
                return false;
        }

        if (prop.TryGetDecimal(out value))
            return true;

        if (prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetDouble(out var d))
        {
            value = (decimal)d;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resumes a previously interrupted backfill job from its checkpoint.
    /// Only processes symbols that were pending or failed.
    /// </summary>
    public async Task ResumeBackfillAsync(
        string checkpointJobId,
        Action<BackfillProgress>? progressCallback = null, CancellationToken ct = default)
    {
        var checkpoint = await _checkpointService.LoadCheckpointAsync(checkpointJobId);
        if (checkpoint == null)
        {
            throw new InvalidOperationException($"No checkpoint found for job {checkpointJobId}");
        }

        var pendingSymbols = await _checkpointService.GetPendingSymbolsAsync(checkpointJobId);
        if (pendingSymbols.Length == 0)
        {
            throw new InvalidOperationException("All symbols in this job are already completed");
        }

        await StartBackfillAsync(
            pendingSymbols,
            checkpoint.Provider,
            checkpoint.FromDate,
            checkpoint.ToDate,
            "Daily",
            progressCallback);
    }

    /// <summary>
    /// Polls the backend for the latest backfill status and syncs it with local progress.
    /// Call periodically from the UI to keep progress up to date with backend state.
    /// </summary>
    public async Task<BackfillResultDto?> PollBackendStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var backendStatus = await _backfillApiService.GetLastStatusAsync(ct);
            if (backendStatus == null)
                return null;

            // Sync backend status with local progress if a job is running
            if (_currentProgress != null && _currentProgress.Status == "Running")
            {
                lock (_progressLock)
                {
                    if (backendStatus.BarsWritten > _currentProgress.DownloadedBars)
                    {
                        _totalBarsDownloaded = backendStatus.BarsWritten;
                        _currentProgress.DownloadedBars = backendStatus.BarsWritten;
                        _currentProgress.BarsPerSecond = (float)BarsPerSecond;
                    }

                    if (backendStatus.Success && backendStatus.CompletedUtc.HasValue)
                    {
                        _currentProgress.Status = "Completed";
                        _currentProgress.CompletedAt = backendStatus.CompletedUtc?.DateTime;
                        _currentProgress.CompletedSymbols = backendStatus.Symbols?.Length ?? _currentProgress.TotalSymbols;
                    }
                }

                ProgressUpdated?.Invoke(this, new BackfillProgressEventArgs { Progress = _currentProgress });
            }

            return backendStatus;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackfillService] Backend poll failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets resumable jobs from the checkpoint store.
    /// </summary>
    public async Task<IReadOnlyList<BackfillCheckpoint>> GetResumableJobsAsync(
        CancellationToken ct = default)
    {
        return await _checkpointService.GetResumableJobsAsync(ct);
    }

    /// <summary>
    /// Gets job history from checkpoints.
    /// </summary>
    public async Task<IReadOnlyList<BackfillCheckpoint>> GetCheckpointHistoryAsync(
        int limit = 20,
        CancellationToken ct = default)
    {
        return await _checkpointService.GetHistoryAsync(limit, ct);
    }

    /// <summary>
    /// Event raised when progress is updated.
    /// </summary>
    public event EventHandler<BackfillProgressEventArgs>? ProgressUpdated;

    /// <summary>
    /// Event raised when backfill completes.
    /// </summary>
    public event EventHandler<BackfillCompletedEventArgs>? BackfillCompleted;
}

/// <summary>
/// Backfill progress event args.
/// </summary>
public sealed class BackfillProgressEventArgs : EventArgs
{
    public BackfillProgress? Progress { get; set; }
}

/// <summary>
/// Backfill completed event args.
/// </summary>
public sealed class BackfillCompletedEventArgs : EventArgs
{
    public bool Success { get; set; }
    public BackfillProgress? Progress { get; set; }
    public bool WasCancelled { get; set; }
    public Exception? Error { get; set; }
}
