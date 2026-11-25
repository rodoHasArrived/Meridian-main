using System.Collections.Concurrent;
using Meridian.Identity.Auth;
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
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering event replay API endpoints.
/// Integrates with JsonlReplayer for JSONL event inspection and event-count scans.
/// Timed playback and playback controls are not implemented by this host.
/// </summary>
public static class ReplayEndpoints
{
    private const int MaxRetainedScans = 64;
    public static void MapReplayEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var scans = app.Services.GetRequiredService<ReplayScanLifetime>();
        var sessions = new ConcurrentDictionary<string, ReplaySession>(StringComparer.OrdinalIgnoreCase);
        var sessionGate = new object();
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
        .WithName("GetReplayFiles").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200);

        // Legacy route: starts an event-count scan, not timed market-event playback.
        group.MapPost(UiApiRoutes.ReplayStart, (ReplayStartRequest req, [FromServices] StorageOptions? storageOptions) =>
        {
            if (string.IsNullOrWhiteSpace(req.FilePath))
                return Results.BadRequest(new { error = "File path is required" });

            var rootPath = Path.GetFullPath(storageOptions?.RootPath ?? "data");
            if (!TryResolvePathWithinRoot(req.FilePath, rootPath, out var resolvedFilePath))
                return Results.BadRequest(new { error = "Invalid file path." });

            var speed = req.SpeedMultiplier ?? 1.0;
            if (!double.IsFinite(speed) || speed <= 0)
                return Results.BadRequest(new { error = "Speed multiplier must be finite and greater than zero." });
            if (speed != 1.0)
                return PlaybackUnavailable();

            var sessionId = Guid.NewGuid().ToString("N")[..12];
            ReplaySession session;

            lock (sessionGate)
            {
                // Retain a bounded recent scan history. Never evict an active reader to admit work.
                if (sessions.Count >= MaxRetainedScans)
                {
                    var oldestFinished = sessions.Values.Where(s => s.IsFinished)
                        .OrderBy(s => s.StartedAt).FirstOrDefault();
                    if (oldestFinished is not null)
                        sessions.TryRemove(oldestFinished.SessionId, out _);
                }
                if (sessions.Count >= MaxRetainedScans)
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Replay scan capacity reached", detail: "Stop an active event-count scan before starting another.");

                session = new ReplaySession(sessionId, resolvedFilePath);
                // The host tracks readers through shutdown before releasing its owned data files.
                if (!session.StartEventCounting(scans))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                sessions[sessionId] = session;
            }

            return Results.Json(new
            {
                sessionId,
                filePath = resolvedFilePath,
                status = "scanning",
                mode = "event_count",
                playbackSupported = false,
                detail = "Counts stored events only. Timed playback and playback controls are not implemented.",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("StartReplay").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(400)
        .Produces(StatusCodes.Status501NotImplemented)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // These legacy playback routes have no playback engine behind them.
        group.MapPost(UiApiRoutes.ReplayPause, () => PlaybackUnavailable())
        .WithName("PauseReplay").RequirePermission(UserPermission.ViewHistoricalData)
        .DeclareNonMutating("Reports that playback control is unavailable; no replay state is changed.")
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ReplayResume, () => PlaybackUnavailable())
        .WithName("ResumeReplay").RequirePermission(UserPermission.ViewHistoricalData)
        .DeclareNonMutating("Reports that playback control is unavailable; no replay state is changed.")
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Stop the event-count scan. Keep it tracked until its reader has actually exited.
        group.MapPost(UiApiRoutes.ReplayStop, (string sessionId) =>
        {
            if (!sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            session.Cancel();
            return Results.Json(new { sessionId, status = session.Status, eventsProcessed = session.EventsProcessed }, jsonOptions);
        })
        .WithName("StopReplay").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ReplaySeek, () => PlaybackUnavailable())
        .WithName("SeekReplay").RequirePermission(UserPermission.ViewHistoricalData)
        .DeclareNonMutating("Reports that playback control is unavailable; no replay state is changed.")
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ReplaySpeed, (SpeedRequest req) =>
        {
            if (!double.IsFinite(req.SpeedMultiplier) || req.SpeedMultiplier <= 0)
                return Results.BadRequest(new { error = "Speed multiplier must be finite and greater than zero." });
            return PlaybackUnavailable();
        })
        .WithName("SetReplaySpeed").RequirePermission(UserPermission.ViewHistoricalData)
        .DeclareNonMutating("Reports that playback control is unavailable; no replay state is changed.")
        .Produces(400)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Get replay status - returns actual event count and progress
        group.MapGet(UiApiRoutes.ReplayStatus, (string sessionId) =>
        {
            if (!sessions.TryGetValue(sessionId, out var session))
                return Results.NotFound(new { error = $"Session '{sessionId}' not found" });

            var progress = session.TotalEvents > 0
                ? (double)session.EventsProcessed / session.TotalEvents * 100.0
                : 0.0;

            return Results.Json(new
            {
                sessionId,
                filePath = session.FilePath,
                status = session.Status,
                mode = "event_count",
                playbackSupported = false,
                eventsProcessed = session.EventsProcessed,
                totalEvents = session.TotalEvents,
                progressPercent = Math.Round(progress, 2),
                startedAt = session.StartedAt,
                elapsed = DateTimeOffset.UtcNow - session.StartedAt
            }, jsonOptions);
        })
        .WithName("GetReplayStatus").RequirePermission(UserPermission.ViewHistoricalData)
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
            [FromServices] StorageOptions? storageOptions,
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
            if (string.IsNullOrWhiteSpace(filePath))
                return Results.Json(new { events = Array.Empty<object>(), total = 0, error = "File not found or path not provided" }, jsonOptions);

            var rootPath = Path.GetFullPath(storageOptions?.RootPath ?? "data");
            if (!TryResolvePathWithinRoot(filePath, rootPath, out var resolvedFilePath))
                return Results.Json(new { events = Array.Empty<object>(), total = 0, error = "Invalid file path" }, jsonOptions);

            try
            {
                var replayer = new JsonlReplayer(resolvedFilePath);
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
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                return Results.Json(new
                {
                    events = Array.Empty<object>(),
                    total = 0,
                    filePath = resolvedFilePath,
                    error = "Malformed replay data",
                    detail = ex.Message
                }, jsonOptions);
            }
            catch (IOException)
            {
                return Results.Json(new
                {
                    events = Array.Empty<object>(),
                    total = 0,
                    filePath = resolvedFilePath,
                    error = "Replay input unavailable. Close the capture writer before retrying."
                }, jsonOptions);
            }

            return Results.Json(new { events, total = events.Count, filePath = resolvedFilePath }, jsonOptions);
        })
        .WithName("PreviewReplayEvents").RequirePermission(UserPermission.ViewHistoricalData)
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
                activeSessions = sessions.Values.Count(s => !s.IsFinished),
                retainedSessions = sessions.Count,
                maxRetainedSessions = MaxRetainedScans,
                playbackSupported = false,
                mode = "event_count",
                sessions = sessions.Values.Select(s => new
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
        .WithName("GetReplayStats").RequirePermission(UserPermission.ViewHistoricalData)
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

        public ReplaySession(string sessionId, string filePath)
        {
            SessionId = sessionId;
            FilePath = filePath;
        }

        public string SessionId { get; }
        public string FilePath { get; }
        private string _status = "scanning";
        private long _eventsProcessed;
        private long _totalEvents;
        private readonly object _gate = new();
        private bool _finished;

        public string Status => Volatile.Read(ref _status);
        public bool IsFinished => Volatile.Read(ref _finished);
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public long EventsProcessed => Interlocked.Read(ref _eventsProcessed);
        public long TotalEvents => Interlocked.Read(ref _totalEvents);

        public void Cancel()
        {
            lock (_gate)
            {
                if (_finished)
                    return;
                Volatile.Write(ref _status, "stopping");
                _cts.Cancel();
            }
        }

        /// <summary>
        /// Starts a background task to count events in the file using JsonlReplayer.
        /// Uses an async method directly instead of Task.Run to avoid wasting thread pool
        /// threads on I/O-bound work.
        /// </summary>
        public bool StartEventCounting(ReplayScanLifetime scans)
        {
            if (scans.TryStart(CountEventsAsync))
                return true;
            _cts.Dispose();
            return false;
        }

        private async Task CountEventsAsync(CancellationToken stoppingToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, stoppingToken);
            var finalStatus = "completed";
            try
            {
                var replayer = new JsonlReplayer(FilePath);
                long count = 0;
                await foreach (var _ in replayer.ReadEventsAsync(linked.Token))
                {
                    count++;
                    Interlocked.Exchange(ref _eventsProcessed, count);
                }
                Interlocked.Exchange(ref _totalEvents, count);
            }
            catch (OperationCanceledException)
            {
                finalStatus = "stopped";
            }
            catch (Exception ex)
            {
                _ = ex; // Observed to prevent unobserved task exception
                finalStatus = "error";
            }
            finally
            {
                lock (_gate)
                {
                    Volatile.Write(ref _finished, true);
                    Volatile.Write(ref _status, linked.IsCancellationRequested ? "stopped" : finalStatus);
                    _cts.Dispose();
                }
            }
        }
    }

    private static IResult PlaybackUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Replay playback controls are not implemented",
        detail: "This host supports file previews and event-count scans only. No playback position, speed or pause state was changed.");

    private sealed record ReplayStartRequest(string? FilePath, double? SpeedMultiplier);
    private sealed record SpeedRequest(double SpeedMultiplier);

    private static bool TryResolvePathWithinRoot(string candidatePath, string rootPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        try
        {
            var fullRootPath = Path.GetFullPath(rootPath);
            var fullCandidatePath = Path.GetFullPath(candidatePath);
            if (!File.Exists(fullCandidatePath))
                return false;

            var rootPrefix = fullRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullCandidatePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            resolvedPath = fullCandidatePath;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
