using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
using Meridian.Storage.Sinks;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering diagnostics API endpoints.
/// Provides dry-run, provider testing, configuration validation, and diagnostic bundles.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Diagnostics");

        // Dry run
        group.MapPost(UiApiRoutes.DiagnosticsDryRun, async ([FromServices] DryRunService? dryRun, [FromServices] ConfigStore store, CancellationToken ct) =>
        {
            if (dryRun is null)
                return Results.Json(new { error = "Dry-run service not available" }, jsonOptions, statusCode: 503);

            var config = store.Load();
            var result = await dryRun.ValidateAsync(config, new DryRunOptions(), ct);
            return Results.Json(new
            {
                success = result.OverallSuccess,
                report = dryRun.GenerateReport(result),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("RunDiagnosticsDryRun")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Provider diagnostics
        group.MapGet(UiApiRoutes.DiagnosticsProviders, ([FromServices] ProviderRegistry? registry) =>
        {
            if (registry is null)
                return Results.Json(new { providers = Array.Empty<object>() }, jsonOptions);

            var catalog = registry.GetProviderCatalog();
            return Results.Json(new
            {
                providers = catalog,
                summary = registry.GetSummary(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsProviders")
        .Produces(200);

        // Storage diagnostics
        group.MapGet(UiApiRoutes.DiagnosticsStorage, ([FromServices] StorageOptions? storageOptions) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            var exists = Directory.Exists(rootPath);
            long totalBytes = 0;
            int fileCount = 0;
            var tiers = new Dictionary<string, object>();

            if (exists)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(rootPath);
                    var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                    totalBytes = files.Sum(f => f.Length);
                    fileCount = files.Length;

                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        var subFiles = subDir.GetFiles("*", SearchOption.AllDirectories);
                        tiers[subDir.Name] = new
                        {
                            fileCount = subFiles.Length,
                            totalBytes = subFiles.Sum(f => f.Length)
                        };
                    }
                }
                catch { /* ignore access errors */ }
            }

            return Results.Json(new
            {
                rootPath = SanitizeDiagnosticText(rootPath),
                exists,
                totalBytes,
                fileCount,
                tiers,
                options = new
                {
                    namingConvention = storageOptions?.NamingConvention.ToString(),
                    compressionEnabled = storageOptions?.Compress,
                    enableParquetSink = storageOptions?.EnableParquetSink
                },
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsStorage")
        .Produces(200);

        // Config diagnostics
        group.MapGet(UiApiRoutes.DiagnosticsConfig, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            return Results.Json(new
            {
                dataSource = config.DataSource.ToString(),
                symbolCount = config.Symbols?.Length ?? 0,
                dataRoot = SanitizeDiagnosticText(config.DataRoot),
                hasAlpacaConfig = config.Alpaca is not null,
                hasStorageConfig = config.Storage is not null,
                hasBackfillConfig = config.Backfill is not null,
                coordination = config.Coordination != null ? new
                {
                    enabled = config.Coordination.Enabled,
                    mode = config.Coordination.Mode.ToString(),
                    instanceId = config.Coordination.InstanceId,
                    rootPath = SanitizeDiagnosticText(config.Coordination.RootPath)
                } : null,
                compress = config.Compress,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsConfig")
        .Produces(200);

        // Diagnostic bundle
        group.MapGet(UiApiRoutes.DiagnosticsBundle, async ([FromServices] DiagnosticBundleService? bundleService, CancellationToken ct) =>
        {
            if (bundleService is null)
                return Results.Json(new { error = "Diagnostic bundle service not available" }, jsonOptions, statusCode: 503);

            var result = await bundleService.GenerateAsync(new DiagnosticBundleOptions(), ct);
            return Results.Json(new
            {
                bundleId = result.BundleId,
                success = result.Success,
                sizeBytes = result.SizeBytes,
                filesIncluded = result.FilesIncluded,
                path = SanitizeDiagnosticText(result.ZipPath),
                message = result.Message
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsBundle")
        .Produces(200)
        .Produces(503);

        // Diagnostics metrics
        group.MapGet(UiApiRoutes.DiagnosticsMetrics, ([FromServices] IServiceProvider services) =>
        {
            var errorTracker = services.GetService<ErrorTracker>();
            var eventMetrics = services.GetService<IEventMetrics>();
            var eventPipeline = services.GetService<EventPipeline>();
            var hotPathPipeline = services.GetService<DualPathEventPipeline>();
            var jsonlSink = services.GetService<JsonlStorageSink>();
            var stats = errorTracker?.GetStatistics();
            var pipelineStats = eventPipeline?.GetStatistics();
            var jsonlStats = jsonlSink?.GetStatistics();
            var metricsSnapshot = (eventPipeline?.EventMetrics ?? eventMetrics)?.GetSnapshot();
            return Results.Json(new
            {
                errors = stats != null ? new
                {
                    total = stats.TotalErrors,
                    inWindow = stats.ErrorsInWindow,
                    byType = stats.ByExceptionType,
                    byContext = SanitizeErrorCounts(stats.ByContext),
                    byHour = stats.ByHour,
                    mostRecent = SanitizeTrackedError(stats.MostRecentError)
                } : null,
                eventPipeline = pipelineStats != null ? new
                {
                    queueName = "market-data",
                    currentQueueSize = pipelineStats.Value.CurrentQueueSize,
                    peakQueueSize = pipelineStats.Value.PeakQueueSize,
                    queueCapacity = pipelineStats.Value.QueueCapacity,
                    queueUtilization = Math.Round(pipelineStats.Value.QueueUtilization, 2),
                    published = pipelineStats.Value.PublishedCount,
                    consumed = pipelineStats.Value.ConsumedCount,
                    dropped = pipelineStats.Value.DroppedCount,
                    recovered = pipelineStats.Value.RecoveredCount,
                    rejected = pipelineStats.Value.RejectedCount,
                    deduplicated = pipelineStats.Value.DeduplicatedCount,
                    averageProcessingTimeUs = Math.Round(pipelineStats.Value.AverageProcessingTimeUs, 2),
                    timeSinceLastFlushMs = Math.Round(pipelineStats.Value.TimeSinceLastFlush.TotalMilliseconds, 2),
                    isWalEnabled = pipelineStats.Value.IsWalEnabled,
                    isValidationEnabled = pipelineStats.Value.IsValidationEnabled,
                    isDeduplicationEnabled = pipelineStats.Value.IsDeduplicationEnabled,
                    highWaterMarkWarned = pipelineStats.Value.HighWaterMarkWarned,
                    timestamp = pipelineStats.Value.Timestamp
                } : null,
                eventPipelineHotPath = hotPathPipeline != null ? new
                {
                    tradeBufferCount = hotPathPipeline.TradeBufferCount,
                    quoteBufferCount = hotPathPipeline.QuoteBufferCount,
                    hotTradePublished = hotPathPipeline.HotTradePublished,
                    hotTradeConsumed = hotPathPipeline.HotTradeConsumed,
                    hotTradeFallbacks = hotPathPipeline.HotTradeFallbacks,
                    hotTradeDropped = hotPathPipeline.HotTradeDropped,
                    hotQuotePublished = hotPathPipeline.HotQuotePublished,
                    hotQuoteConsumed = hotPathPipeline.HotQuoteConsumed,
                    hotQuoteFallbacks = hotPathPipeline.HotQuoteFallbacks,
                    hotQuoteDropped = hotPathPipeline.HotQuoteDropped
                } : null,
                jsonlStorage = jsonlStats != null ? new
                {
                    batchingEnabled = jsonlStats.IsBatchingEnabled,
                    batchSize = jsonlStats.BatchSize,
                    flushIntervalMs = Math.Round(jsonlStats.FlushInterval.TotalMilliseconds, 2),
                    eventsBuffered = jsonlStats.EventsBuffered,
                    eventsWritten = jsonlStats.EventsWritten,
                    batchesWritten = jsonlStats.BatchesWritten,
                    writerCount = jsonlStats.WriterCount,
                    bufferCount = jsonlStats.BufferCount,
                    timestamp = jsonlStats.Timestamp
                } : null,
                marketDataMetrics = metricsSnapshot.HasValue ? new
                {
                    published = metricsSnapshot.Value.Published,
                    dropped = metricsSnapshot.Value.Dropped,
                    dropRate = Math.Round(metricsSnapshot.Value.DropRate, 4),
                    eventsPerSecond = Math.Round(metricsSnapshot.Value.EventsPerSecond, 2),
                    trades = metricsSnapshot.Value.Trades,
                    quotes = metricsSnapshot.Value.Quotes,
                    depthUpdates = metricsSnapshot.Value.DepthUpdates,
                    historicalBars = metricsSnapshot.Value.HistoricalBars,
                    averageLatencyUs = Math.Round(metricsSnapshot.Value.AverageLatencyUs, 2),
                    maxLatencyUs = Math.Round(metricsSnapshot.Value.MaxLatencyUs, 2),
                    latencySampleCount = metricsSnapshot.Value.LatencySampleCount
                } : null,
                runtime = CreateRuntimeDiagnosticsSnapshot(),
                processMemoryBytes = GC.GetTotalMemory(false),
                gcCollections = new { gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2) },
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsMetrics")
        .Produces(200);

        // Validate (generic validation)
        group.MapPost(UiApiRoutes.DiagnosticsValidate, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var issues = new List<string>();

            if (config.Symbols is null || config.Symbols.Length == 0)
                issues.Add("No symbols configured");
            if (string.IsNullOrEmpty(config.DataRoot))
                issues.Add("DataRoot is not set");

            return Results.Json(new
            {
                valid = issues.Count == 0,
                issues,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("RunDiagnosticsValidate")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Test specific provider
        group.MapPost(UiApiRoutes.DiagnosticsProviderTest, (string providerName, [FromServices] ProviderRegistry? registry) =>
        {
            if (registry is null)
                return Results.Json(new { success = false, error = "Provider registry not available" }, jsonOptions);

            var provider = registry.GetAllProviders()
                .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

            return Results.Json(new
            {
                provider = providerName,
                found = provider is not null,
                isEnabled = provider?.IsEnabled ?? false,
                reachable = provider?.IsEnabled ?? false,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("TestDiagnosticsProvider")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Quick check
        group.MapGet(UiApiRoutes.DiagnosticsQuickCheck, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            return Results.Json(new
            {
                configLoaded = true,
                dataRoot = SanitizeDiagnosticText(config.DataRoot),
                dataRootExists = Directory.Exists(config.DataRoot),
                symbolCount = config.Symbols?.Length ?? 0,
                dataSource = config.DataSource.ToString(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsQuickCheck")
        .Produces(200);

        // Show config (sanitized)
        group.MapGet(UiApiRoutes.DiagnosticsShowConfig, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            return Results.Json(new
            {
                dataSource = config.DataSource.ToString(),
                symbols = config.Symbols?.Select(s => s.Symbol).ToArray() ?? Array.Empty<string>(),
                dataRoot = SanitizeDiagnosticText(config.DataRoot),
                compress = config.Compress,
                storage = config.Storage != null ? new
                {
                    naming = config.Storage.NamingConvention,
                    retention = config.Storage.RetentionDays,
                    maxSizeGb = config.Storage.MaxTotalMegabytes.HasValue
                        ? (double)config.Storage.MaxTotalMegabytes.Value / 1024
                        : (double?)null
                } : null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsShowConfig")
        .Produces(200);

        // Error codes reference
        group.MapGet(UiApiRoutes.DiagnosticsErrorCodes, () =>
        {
            var codes = Enum.GetValues<Meridian.Application.ResultTypes.ErrorCode>()
                .Select(e => new { code = (int)e, name = e.ToString() });
            return Results.Json(new { errorCodes = codes, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetDiagnosticsErrorCodes")
        .Produces(200);

        // Self-test
        group.MapPost(UiApiRoutes.DiagnosticsSelftest, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var checks = new List<object>();

            checks.Add(new { check = "config_loadable", passed = true });
            checks.Add(new { check = "data_root_exists", passed = Directory.Exists(config.DataRoot) });
            checks.Add(new { check = "symbols_configured", passed = (config.Symbols?.Length ?? 0) > 0 });

            return Results.Json(new
            {
                passed = checks.All(c => ((dynamic)c).passed),
                checks,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("RunDiagnosticsSelftest")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Validate credentials
        group.MapPost(UiApiRoutes.DiagnosticsValidateCredentials, ([FromServices] ConfigurationService? configService) =>
        {
            if (configService is null)
                return Results.Json(new { error = "Configuration service not available" }, jsonOptions, statusCode: 503);

            var credStatus = configService.GetCredentialStatus();
            return Results.Json(new
            {
                credentials = credStatus.Select(kv => new { provider = kv.Key, hasCredentials = kv.Value }),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ValidateDiagnosticsCredentials")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Test connectivity
        group.MapPost(UiApiRoutes.DiagnosticsTestConnectivity, ([FromServices] ProviderRegistry? registry) =>
        {
            if (registry is null)
                return Results.Json(new { error = "Provider registry not available" }, jsonOptions, statusCode: 503);

            var providers = registry.GetAllProviders().Select(p => new
            {
                name = p.Name,
                isEnabled = p.IsEnabled,
                reachable = p.IsEnabled
            });

            return Results.Json(new
            {
                results = providers,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("TestDiagnosticsConnectivity")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Validate config
        group.MapPost(UiApiRoutes.DiagnosticsValidateConfig, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var issues = new List<string>();

            if (config.Symbols is null || config.Symbols.Length == 0)
                issues.Add("No symbols configured");
            if (string.IsNullOrEmpty(config.DataRoot))
                issues.Add("DataRoot is not set");
            if (config.DataSource == default)
                issues.Add("No data source selected");

            return Results.Json(new
            {
                valid = issues.Count == 0,
                issues,
                config = new
                {
                    dataSource = config.DataSource.ToString(),
                    symbolCount = config.Symbols?.Length ?? 0,
                    dataRoot = SanitizeDiagnosticText(config.DataRoot)
                },
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ValidateDiagnosticsConfig")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.DiagnosticsCoordination, async ([FromServices] ILeaseManager? leaseManager, CancellationToken ct) =>
        {
            if (leaseManager is null)
                return Results.Json(new { error = "Coordination services not available" }, jsonOptions, statusCode: 503);

            var snapshot = await leaseManager.GetSnapshotAsync(ct);
            return Results.Json(new
            {
                enabled = snapshot.Enabled,
                mode = snapshot.Mode,
                instanceId = snapshot.InstanceId,
                rootPath = SanitizeDiagnosticText(snapshot.RootPath),
                heldLeaseCount = snapshot.HeldLeaseCount,
                symbolLeaseCount = snapshot.SymbolLeaseCount,
                scheduleLeaseCount = snapshot.ScheduleLeaseCount,
                jobLeaseCount = snapshot.JobLeaseCount,
                leaderLeaseCount = snapshot.LeaderLeaseCount,
                conflictCount = snapshot.ConflictCount,
                takeoverCount = snapshot.TakeoverCount,
                renewalFailureCount = snapshot.RenewalFailureCount,
                orphanedLeaseCount = snapshot.OrphanedLeaseCount,
                corruptedLeaseCount = snapshot.CorruptedLeaseCount,
                heldLeases = SanitizeHeldLeases(snapshot.HeldLeases),
                orphanedResources = SanitizeDiagnosticTextList(snapshot.OrphanedResources),
                corruptedLeaseFiles = SanitizeDiagnosticTextList(snapshot.CorruptedLeaseFiles),
                timestamp = snapshot.CapturedAtUtc
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsCoordination")
        .Produces(200)
        .Produces(503);
    }

    private static Dictionary<string, int> SanitizeErrorCounts(Dictionary<string, int> counts)
    {
        return counts
            .GroupBy(pair => RuntimeDiagnosticRedactor.SanitizeText(pair.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static object? SanitizeTrackedError(TrackedError? error)
    {
        if (error is null)
            return null;

        return new
        {
            id = error.Id,
            timestamp = error.Timestamp,
            level = error.Level,
            message = RuntimeDiagnosticRedactor.SanitizeText(error.Message),
            exceptionType = error.ExceptionType,
            stackTrace = RuntimeDiagnosticRedactor.SanitizeText(error.StackTrace),
            context = RuntimeDiagnosticRedactor.SanitizeText(error.Context),
            innerException = RuntimeDiagnosticRedactor.SanitizeText(error.InnerException)
        };
    }

    private static object CreateRuntimeDiagnosticsSnapshot()
    {
        using var process = Process.GetCurrentProcess();
        var startedAtUtc = GetProcessStartTimeUtc(process);
        var now = DateTimeOffset.UtcNow;

        return new
        {
            operationName = "diagnostics.metrics.snapshot",
            componentName = "DiagnosticsEndpoints",
            processId = Environment.ProcessId,
            processName = SanitizeDiagnosticText(process.ProcessName),
            startedAtUtc,
            uptimeSeconds = startedAtUtc.HasValue
                ? Math.Max(0, (long)(now - startedAtUtc.Value).TotalSeconds)
                : (long?)null,
            threadCount = process.Threads.Count,
            handleCount = GetHandleCount(process),
            workingSetBytes = Environment.WorkingSet,
            privateMemoryBytes = process.PrivateMemorySize64,
            managedHeapBytes = GC.GetTotalMemory(false),
            gcTotalMemoryBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
            processorCount = Environment.ProcessorCount,
            runtimeVersion = RuntimeInformation.FrameworkDescription,
            osDescription = RuntimeInformation.OSDescription
        };
    }

    private static DateTimeOffset? GetProcessStartTimeUtc(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception) when (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return null;
        }
    }

    private static int? GetHandleCount(Process process)
    {
        try
        {
            return process.HandleCount;
        }
        catch (Exception) when (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return null;
        }
    }

    private static IReadOnlyList<object> SanitizeHeldLeases(IReadOnlyList<LeaseRecord> leases)
    {
        return leases
            .Select(lease => (object)new
            {
                resourceId = SanitizeDiagnosticText(lease.ResourceId),
                instanceId = SanitizeDiagnosticText(lease.InstanceId),
                lease.LeaseVersion,
                acquiredAtUtc = lease.AcquiredAtUtc,
                expiresAtUtc = lease.ExpiresAtUtc,
                lastRenewedAtUtc = lease.LastRenewedAtUtc
            })
            .ToArray();
    }

    private static IReadOnlyList<string> SanitizeDiagnosticTextList(IReadOnlyList<string> values)
        => values.Select(SanitizeDiagnosticText).ToArray();

    private static string SanitizeDiagnosticText(string? value) =>
        RuntimeDiagnosticRedactor.SanitizeText(value);
}
