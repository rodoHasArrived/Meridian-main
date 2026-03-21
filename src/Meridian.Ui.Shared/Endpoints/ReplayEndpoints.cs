using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Store;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Replay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering event replay API endpoints.
/// Integrates with JsonlReplayer for actual JSONL event reading and replay.
/// </summary>
public static class ReplayEndpoints
{
    private static readonly Dictionary<string, ReplaySession> s_sessions = new(StringComparer.OrdinalIgnoreCase);

    public static void MapReplayEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Replay");

        // List replay files - scans actual storage directory
        group.MapGet(UiApiRoutes.ReplayFiles, (string? symbol, [FromServices] StorageOptions? storageOptions) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            var files = new List<object>();

            if (Directory.Exists(rootPath))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(rootPath, "*.jsonl*", SearchOption.AllDirectories))
                    {
                        var info = new FileInfo(file);
                        if (symbol != null && !info.Name.Contains(symbol, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Extract symbol from filename (e.g., "SPY_trades.jsonl.gz" -> "SPY")
                        var extractedSymbol = ExtractSymbolFromFileName(info.Name);
                        var eventType = ExtractEventTypeFromFileName(info.Name);

                        files.Add(new
                        {
                            path = file,
                            name = info.Name,
                            symbol = extractedSymbol,
                            eventType,
                            sizeBytes = info.Length,
                            isCompressed = info.Extension.Equals(".gz", StringComparison.OrdinalIgnoreCase),
                            lastModified = info.LastWriteTimeUtc
                        });
                    }
                }
                catch (UnauthorizedAccessException) { /* skip inaccessible directories */ }
            }

            return Results.Json(new { files = files.Take(500), total = files.Count, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetReplayFiles")
        .Produces(200);

        // Start replay - creates a replay session backed by JsonlReplayer
        group.MapPost(UiApiRoutes.ReplayStart, (ReplayStartRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.FilePath))
                return Results.BadRequest(new { error = "File path is required" });

            if (!File.Exists(req.FilePath))
                return Results.BadRequest(new { error = $"File not found: {req.FilePath}" });

            var sessionId = Guid.NewGuid().ToString("N")[..12];
            var fileDir = Path.GetDirectoryName(req.FilePath) ?? ".";
            var session = new ReplaySession(sessionId, req.FilePath, req.SpeedMultiplier ?? 1.0, fileDir);

            // Start background event counting
            session.StartEventCounting();

            s_sessions[sessionId] = session;

            return Results.Json(new
            {
                sessionId,
                filePath = req.FilePath,
                status = "started",
                speedMultiplier = session.Speed,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("StartReplay")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Pause replay
        group.MapPost(UiApiRoutes.ReplayPause, (string sessionId) =>
        {
            if (!s_sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            session.Status = "paused";
            return Results.Json(new { sessionId, status = session.Status, eventsProcessed = session.EventsProcessed }, jsonOptions);
        })
        .WithName("PauseReplay")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Resume replay
        group.MapPost(UiApiRoutes.ReplayResume, (string sessionId) =>
        {
            if (!s_sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            session.Status = "running";
            return Results.Json(new { sessionId, status = session.Status, eventsProcessed = session.EventsProcessed }, jsonOptions);
        })
        .WithName("ResumeReplay")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Stop replay - cancels and cleans up the session
        group.MapPost(UiApiRoutes.ReplayStop, (string sessionId) =>
        {
            if (!s_sessions.Remove(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            session.Cancel();
            return Results.Json(new { sessionId, status = "stopped", eventsProcessed = session.EventsProcessed }, jsonOptions);
        })
        .WithName("StopReplay")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Seek replay
        group.MapPost(UiApiRoutes.ReplaySeek, (string sessionId, SeekRequest req) =>
        {
            if (!s_sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            return Results.Json(new { sessionId, positionMs = req.PositionMs, status = session.Status }, jsonOptions);
        })
        .WithName("SeekReplay")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Set replay speed
        group.MapPost(UiApiRoutes.ReplaySpeed, (string sessionId, SpeedRequest req) =>
        {
            if (!s_sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            session.Speed = req.SpeedMultiplier;
            return Results.Json(new { sessionId, speedMultiplier = session.Speed, status = session.Status }, jsonOptions);
        })
        .WithName("SetReplaySpeed")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Get replay status - returns actual event count and progress
        group.MapGet(UiApiRoutes.ReplayStatus, (string sessionId) =>
        {
            if (!s_sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            var progress = session.TotalEvents > 0
                ? (double)session.EventsProcessed / session.TotalEvents * 100.0
                : 0.0;

            return Results.Json(new
            {
                sessionId,
                filePath = session.FilePath,
                status = session.Status,
                speedMultiplier = session.Speed,
                eventsProcessed = session.EventsProcessed,
                totalEvents = session.TotalEvents,
                progressPercent = Math.Round(progress, 2),
                startedAt = session.StartedAt,
                elapsed = DateTimeOffset.UtcNow - session.StartedAt
            }, jsonOptions);
        })
        .WithName("GetReplayStatus")
        .Produces(200)
        .Produces(404);

        // Preview replay events - uses IMarketDataStore to read actual events
        group.MapGet(UiApiRoutes.ReplayPreview, async (
            string? filePath,
            string? symbol,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? limit,
            [FromServices] IMarketDataStore? store,
            CancellationToken ct) =>
        {
            var events = new List<object>();
            var maxEvents = Math.Min(limit ?? 10, 100);

            // Prefer the unified IMarketDataStore when symbol/time filters are supplied
            // (or when no specific file path is given).
            if (store is not null && (symbol is not null || from.HasValue || to.HasValue || filePath is null))
            {
                try
                {
                    var query = new MarketDataQuery(
                        Symbol: symbol is not null ? new SymbolId(symbol) : null,
                        From: from,
                        To: to,
                        Limit: maxEvents);

                    await foreach (var evt in store.QueryAsync(query, ct))
                    {
                        events.Add(new
                        {
                            eventType = evt.Type,
                            symbol = evt.EffectiveSymbol,
                            timestamp = evt.Timestamp,
                            sequence = evt.Sequence,
                            source = evt.Source
                        });
                    }
                }
                catch (OperationCanceledException) { /* request cancelled */ }

                return Results.Json(new { events, total = events.Count, source = "store" }, jsonOptions);
            }

            // Fallback: single-file preview via path (legacy behaviour)
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return Results.Json(new { events = Array.Empty<object>(), total = 0, error = "File not found or path not provided" }, jsonOptions);

            try
            {
                var fileDir = Path.GetDirectoryName(filePath) ?? ".";
                var replayer = new JsonlReplayer(fileDir);
                var count = 0;

                await foreach (var evt in replayer.ReadEventsAsync(ct))
                {
                    events.Add(new
                    {
                        eventType = evt.Type,
                        symbol = evt.Symbol,
                        timestamp = evt.Timestamp,
                        sequence = evt.Sequence,
                        source = evt.Source
                    });

                    count++;
                    if (count >= maxEvents)
                        break;
                }
            }
            catch (OperationCanceledException) { /* request cancelled */ }
            catch (JsonException) { /* malformed data */ }

            return Results.Json(new { events, total = events.Count, filePath }, jsonOptions);
        })
        .WithName("PreviewReplayEvents")
        .Produces(200);

        // Replay stats - returns file statistics using MemoryMappedJsonlReader
        group.MapGet(UiApiRoutes.ReplayStats, ([FromServices] StorageOptions? storageOptions) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            FileStatistics? fileStats = null;

            if (Directory.Exists(rootPath))
            {
                var reader = new MemoryMappedJsonlReader(rootPath);
                fileStats = reader.GetFileStatistics();
            }

            return Results.Json(new
            {
                activeSessions = s_sessions.Count,
                sessions = s_sessions.Values.Select(s => new
                {
                    sessionId = s.SessionId,
                    status = s.Status,
                    filePath = s.FilePath,
                    eventsProcessed = s.EventsProcessed,
                    totalEvents = s.TotalEvents,
                    startedAt = s.StartedAt
                }),
                storage = fileStats.HasValue ? new
                {
                    totalFiles = fileStats.Value.TotalFiles,
                    totalBytes = fileStats.Value.TotalBytes,
                    compressedFiles = fileStats.Value.CompressedFiles,
                    uncompressedFiles = fileStats.Value.UncompressedFiles
                } : null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetReplayStats")
        .Produces(200);
    }

    private static string ExtractSymbolFromFileName(string fileName)
    {
        // Patterns: "SPY_trades.jsonl.gz", "AAPL_quotes.jsonl", "SPY_depth.jsonl"
        var nameWithoutExt = fileName;
        if (nameWithoutExt.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            nameWithoutExt = nameWithoutExt[..^3];
        if (nameWithoutExt.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            nameWithoutExt = nameWithoutExt[..^6];

        var underscoreIdx = nameWithoutExt.IndexOf('_');
        return underscoreIdx > 0 ? nameWithoutExt[..underscoreIdx] : nameWithoutExt;
    }

    private static string ExtractEventTypeFromFileName(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("trade"))
            return "trade";
        if (lower.Contains("quote"))
            return "quote";
        if (lower.Contains("depth"))
            return "depth";
        if (lower.Contains("bar"))
            return "bar";
        return "unknown";
    }

    private sealed class ReplaySession
    {
        private readonly CancellationTokenSource _cts = new();

        public ReplaySession(string sessionId, string filePath, double speed, string fileDirectory)
        {
            SessionId = sessionId;
            FilePath = filePath;
            Speed = speed;
            FileDirectory = fileDirectory;
        }

        public string SessionId { get; }
        public string FilePath { get; }
        public string FileDirectory { get; }
        public string Status { get; set; } = "running";
        public double Speed { get; set; }
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public long EventsProcessed { get; private set; }
        public long TotalEvents { get; private set; }

        public void Cancel()
        {
            _cts.Cancel();
            Status = "stopped";
        }

        /// <summary>
        /// Starts a background task to count events in the file using JsonlReplayer.
        /// Uses an async method directly instead of Task.Run to avoid wasting thread pool
        /// threads on I/O-bound work.
        /// </summary>
        public void StartEventCounting()
        {
            _ = CountEventsAsync();
        }

        private async Task CountEventsAsync(CancellationToken ct = default)
        {
            try
            {
                var replayer = new JsonlReplayer(FileDirectory);
                long count = 0;
                await foreach (var _ in replayer.ReadEventsAsync(_cts.Token))
                {
                    count++;
                    EventsProcessed = count;
                }
                TotalEvents = count;
                Status = "completed";
            }
            catch (OperationCanceledException)
            {
                Status = "stopped";
            }
            catch (Exception ex)
            {
                _ = ex; // Observed to prevent unobserved task exception
                Status = "error";
            }
        }
    }

    private sealed record ReplayStartRequest(string? FilePath, double? SpeedMultiplier);
    private sealed record SeekRequest(long PositionMs);
    private sealed record SpeedRequest(double SpeedMultiplier);
}
