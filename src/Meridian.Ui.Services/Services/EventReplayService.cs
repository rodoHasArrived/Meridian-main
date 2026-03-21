using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for replaying historical market data events from JSONL files.
/// Supports playback controls, speed adjustment, and event filtering.
/// </summary>
public sealed class EventReplayService
{
    private static readonly Lazy<EventReplayService> _instance = new(() => new EventReplayService());
    private readonly ApiClientService _apiClient;

    public static EventReplayService Instance => _instance.Value;

    private EventReplayService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Raised when replay state changes.
    /// </summary>
    public event EventHandler<ReplayStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised when an event is replayed.
    /// </summary>
#pragma warning disable CS0067 // Events are declared but never raised in this implementation
    public event EventHandler<ReplayEventArgs>? EventReplayed;

    /// <summary>
    /// Raised when replay progress updates.
    /// </summary>
    public event EventHandler<ReplayProgressEventArgs>? ProgressChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Gets available data files for replay.
    /// </summary>
    public async Task<ReplayFilesResult> GetAvailableFilesAsync(
        string? symbol = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(symbol)) queryParams.Add($"symbol={symbol}");
        if (fromDate.HasValue) queryParams.Add($"from={fromDate:yyyy-MM-dd}");
        if (toDate.HasValue) queryParams.Add($"to={toDate:yyyy-MM-dd}");

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var response = await _apiClient.GetWithResponseAsync<ReplayFilesResponse>(
            $"/api/replay/files{query}",
            ct);

        if (response.Success && response.Data != null)
        {
            return new ReplayFilesResult
            {
                Success = true,
                Files = response.Data.Files?.ToList() ?? new List<ReplayFileInfo>()
            };
        }

        return new ReplayFilesResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get replay files"
        };
    }

    /// <summary>
    /// Starts event replay from a file or date range.
    /// </summary>
    public async Task<ReplayStartResult> StartReplayAsync(
        ReplayOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<ReplayStartResponse>(
            "/api/replay/start",
            new
            {
                filePath = options.FilePath,
                symbol = options.Symbol,
                fromDate = options.FromDate?.ToString(FormatHelpers.IsoDateFormat),
                toDate = options.ToDate?.ToString(FormatHelpers.IsoDateFormat),
                eventTypes = options.EventTypes,
                speedMultiplier = options.SpeedMultiplier,
                publishToEventBus = options.PublishToEventBus,
                preserveTiming = options.PreserveTiming
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new ReplayStartResult
            {
                Success = true,
                SessionId = response.Data.SessionId,
                TotalEvents = response.Data.TotalEvents,
                EstimatedDuration = TimeSpan.FromSeconds(response.Data.EstimatedDurationSeconds)
            };
        }

        return new ReplayStartResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to start replay"
        };
    }

    /// <summary>
    /// Pauses the current replay session.
    /// </summary>
    public async Task<bool> PauseReplayAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/replay/{sessionId}/pause",
            null,
            ct);

        if (response.Success)
        {
            StateChanged?.Invoke(this, new ReplayStateChangedEventArgs
            {
                SessionId = sessionId,
                State = ReplayState.Paused
            });
        }

        return response.Success;
    }

    /// <summary>
    /// Resumes a paused replay session.
    /// </summary>
    public async Task<bool> ResumeReplayAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/replay/{sessionId}/resume",
            null,
            ct);

        if (response.Success)
        {
            StateChanged?.Invoke(this, new ReplayStateChangedEventArgs
            {
                SessionId = sessionId,
                State = ReplayState.Playing
            });
        }

        return response.Success;
    }

    /// <summary>
    /// Stops the current replay session.
    /// </summary>
    public async Task<bool> StopReplayAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/replay/{sessionId}/stop",
            null,
            ct);

        if (response.Success)
        {
            StateChanged?.Invoke(this, new ReplayStateChangedEventArgs
            {
                SessionId = sessionId,
                State = ReplayState.Stopped
            });
        }

        return response.Success;
    }

    /// <summary>
    /// Seeks to a specific position in the replay.
    /// </summary>
    public async Task<bool> SeekAsync(string sessionId, TimeSpan position, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/replay/{sessionId}/seek",
            new { positionSeconds = position.TotalSeconds },
            ct);

        return response.Success;
    }

    /// <summary>
    /// Changes the replay speed.
    /// </summary>
    public async Task<bool> SetSpeedAsync(string sessionId, double speedMultiplier, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<object>(
            $"/api/replay/{sessionId}/speed",
            new { speedMultiplier },
            ct);

        return response.Success;
    }

    /// <summary>
    /// Gets the current status of a replay session.
    /// </summary>
    public async Task<ReplayStatus> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ReplayStatus>(
            $"/api/replay/{sessionId}/status",
            ct);

        return response.Data ?? new ReplayStatus { State = ReplayState.Unknown };
    }

    /// <summary>
    /// Gets a preview of events in a file.
    /// </summary>
    public async Task<EventPreviewResult> PreviewEventsAsync(
        string filePath,
        int limit = 100,
        CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<EventPreviewResponse>(
            $"/api/replay/preview?path={Uri.EscapeDataString(filePath)}&limit={limit}",
            ct);

        if (response.Success && response.Data != null)
        {
            return new EventPreviewResult
            {
                Success = true,
                Events = response.Data.Events?.ToList() ?? new List<ReplayEvent>(),
                TotalCount = response.Data.TotalCount
            };
        }

        return new EventPreviewResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to preview events"
        };
    }

    /// <summary>
    /// Gets aggregate statistics for a replay file.
    /// </summary>
    public async Task<ReplayFileStats> GetFileStatsAsync(string filePath, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ReplayFileStats>(
            $"/api/replay/stats?path={Uri.EscapeDataString(filePath)}",
            ct);

        return response.Data ?? new ReplayFileStats();
    }
}

#region Event Args

public sealed class ReplayStateChangedEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public ReplayState State { get; set; }
}

public sealed class ReplayEventArgs : EventArgs
{
    public ReplayEvent? Event { get; set; }
}

public sealed class ReplayProgressEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public long EventsReplayed { get; set; }
    public long TotalEvents { get; set; }
    public double ProgressPercent { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Remaining { get; set; }
}

#endregion

#region Result Classes

public sealed class ReplayFilesResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ReplayFileInfo> Files { get; set; } = new();
}

public sealed class ReplayFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long FileSizeBytes { get; set; }
    public long EventCount { get; set; }
    public bool IsCompressed { get; set; }
}

public sealed class ReplayOptions
{
    public string? FilePath { get; set; }
    public string? Symbol { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string[]? EventTypes { get; set; }
    public double SpeedMultiplier { get; set; } = 1.0;
    public bool PublishToEventBus { get; set; }
    public bool PreserveTiming { get; set; } = true;
}

public sealed class ReplayStartResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SessionId { get; set; }
    public long TotalEvents { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
}

public sealed class ReplayStatus
{
    public ReplayState State { get; set; }
    public string? SessionId { get; set; }
    public long EventsReplayed { get; set; }
    public long TotalEvents { get; set; }
    public double ProgressPercent { get; set; }
    public double SpeedMultiplier { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Remaining { get; set; }
    public DateTime? CurrentEventTime { get; set; }
}

public enum ReplayState : byte
{
    Unknown,
    Initializing,
    Playing,
    Paused,
    Stopped,
    Completed,
    Error
}

public sealed class EventPreviewResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ReplayEvent> Events { get; set; } = new();
    public long TotalCount { get; set; }
}

public sealed class ReplayEvent
{
    public string EventType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Data { get; set; }
}

public sealed class ReplayFileStats
{
    public long EventCount { get; set; }
    public DateTime FirstEventTime { get; set; }
    public DateTime LastEventTime { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, long>? EventTypeCounts { get; set; }
}

#endregion

#region API Response Classes

public sealed class ReplayFilesResponse
{
    public List<ReplayFileInfo>? Files { get; set; }
}

public sealed class ReplayStartResponse
{
    public string? SessionId { get; set; }
    public long TotalEvents { get; set; }
    public double EstimatedDurationSeconds { get; set; }
}

public sealed class EventPreviewResponse
{
    public List<ReplayEvent>? Events { get; set; }
    public long TotalCount { get; set; }
}

#endregion
