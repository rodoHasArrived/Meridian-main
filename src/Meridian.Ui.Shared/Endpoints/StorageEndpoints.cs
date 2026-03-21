using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering storage operation API endpoints.
/// Implements Phase 3B.2 — replaces 19 stub endpoints with working handlers.
/// </summary>
public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Storage");

        // GET /api/storage/profiles — available storage profile presets
        group.MapGet(UiApiRoutes.StorageProfiles, () =>
        {
            var presets = StorageProfilePresets.GetPresets();
            return Results.Json(new
            {
                defaultProfile = StorageProfilePresets.DefaultProfile,
                profiles = presets.Select(p => new { p.Id, p.Label, p.Description })
            }, jsonOptions);
        })
        .WithName("GetStorageProfiles").Produces(200);

        // GET /api/storage/stats — overall storage statistics
        group.MapGet(UiApiRoutes.StorageStats, (StorageOptions opts) =>
        {
            var rootPath = Path.GetFullPath(opts.RootPath);
            long totalSize = 0;
            int totalFiles = 0;
            int totalDirs = 0;

            if (Directory.Exists(rootPath))
            {
                try
                {
                    var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz") || f.EndsWith(".parquet"));
                    foreach (var file in files)
                    {
                        totalFiles++;
                        totalSize += new FileInfo(file).Length;
                    }
                    totalDirs = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories).Count();
                }
                catch (IOException) { /* permission / access issues */ }
                catch (UnauthorizedAccessException) { /* access denied */ }
            }

            return Results.Json(new
            {
                rootPath,
                exists = Directory.Exists(rootPath),
                totalFiles,
                totalDirectories = totalDirs,
                totalSizeBytes = totalSize,
                totalSizeMb = Math.Round(totalSize / (1024.0 * 1024.0), 2),
                namingConvention = opts.NamingConvention.ToString(),
                datePartition = opts.DatePartition.ToString(),
                compress = opts.Compress,
                compressionCodec = opts.CompressionCodec.ToString(),
                parquetEnabled = opts.EnableParquetSink,
                retentionDays = opts.RetentionDays
            }, jsonOptions);
        })
        .WithName("GetStorageStats").Produces(200);

        // GET /api/storage/breakdown — breakdown by symbol
        group.MapGet(UiApiRoutes.StorageBreakdown, async (
            IStorageSearchService? searchService,
            StorageOptions opts,
            CancellationToken ct) =>
        {
            if (searchService is null)
            {
                return Results.Json(new { message = "Storage search service not available", breakdown = Array.Empty<object>() }, jsonOptions);
            }

            try
            {
                var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                return Results.Json(new
                {
                    totalEvents = catalog.TotalEvents,
                    totalBytes = catalog.TotalBytes,
                    symbols = catalog.Symbols,
                    eventTypes = catalog.EventTypes,
                    sources = catalog.Sources
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to compute breakdown: {ex.Message}");
            }
        })
        .WithName("GetStorageBreakdown").Produces(200);

        // GET /api/storage/symbol/{symbol}/info — storage info for a symbol
        group.MapGet(UiApiRoutes.StorageSymbolInfo, async (
            string symbol,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { symbol, message = "Search service not available" }, jsonOptions);

            var result = await searchService.SearchFilesAsync(
                new FileSearchQuery(Symbols: new[] { symbol }, Take: 100), ct);

            return Results.Json(new
            {
                symbol,
                totalFiles = result.TotalMatches,
                totalBytes = result.Results?.Sum(f => f.SizeBytes) ?? 0,
                totalEvents = result.Results?.Sum(f => f.EventCount) ?? 0,
                dateRange = result.Results?.Any() == true
                    ? new { from = result.Results.Min(f => f.Date), to = result.Results.Max(f => f.Date) }
                    : null
            }, jsonOptions);
        })
        .WithName("GetStorageSymbolInfo").Produces(200);

        // GET /api/storage/symbol/{symbol}/stats — detailed stats for a symbol
        group.MapGet(UiApiRoutes.StorageSymbolStats, async (
            string symbol,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { symbol, message = "Search service not available" }, jsonOptions);

            var result = await searchService.SearchFilesAsync(
                new FileSearchQuery(Symbols: new[] { symbol }, Take: 500), ct);

            var files = result.Results ?? Array.Empty<FileSearchResult>();
            var byType = files.GroupBy(f => f.EventType ?? "unknown")
                .Select(g => new { type = g.Key, files = g.Count(), bytes = g.Sum(f => f.SizeBytes), events = g.Sum(f => f.EventCount) });

            return Results.Json(new
            {
                symbol,
                totalFiles = result.TotalMatches,
                totalBytes = files.Sum(f => f.SizeBytes),
                totalEvents = files.Sum(f => f.EventCount),
                byEventType = byType
            }, jsonOptions);
        })
        .WithName("GetStorageSymbolStats").Produces(200);

        // GET /api/storage/symbol/{symbol}/files — list files for a symbol
        group.MapGet(UiApiRoutes.StorageSymbolFiles, async (
            string symbol,
            HttpContext ctx,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { symbol, files = Array.Empty<object>() }, jsonOptions);

            var skip = int.TryParse(ctx.Request.Query["skip"].FirstOrDefault(), out var s) ? s : 0;
            var take = int.TryParse(ctx.Request.Query["take"].FirstOrDefault(), out var t) ? Math.Min(t, 200) : 50;

            var result = await searchService.SearchFilesAsync(
                new FileSearchQuery(Symbols: new[] { symbol }, Skip: skip, Take: take), ct);

            return Results.Json(new
            {
                symbol,
                totalFiles = result.TotalMatches,
                skip,
                take,
                files = result.Results?.Select(f => new { f.Path, f.SizeBytes, f.EventCount, f.Date, f.EventType })
            }, jsonOptions);
        })
        .WithName("GetStorageSymbolFiles").Produces(200);

        // GET /api/storage/symbol/{symbol}/path — storage path for a symbol
        group.MapGet(UiApiRoutes.StorageSymbolPath, (string symbol, StorageOptions opts) =>
        {
            var root = Path.GetFullPath(opts.RootPath);
            var symbolPath = opts.NamingConvention switch
            {
                FileNamingConvention.BySymbol => Path.Combine(root, symbol.ToUpperInvariant()),
                FileNamingConvention.ByType => root, // symbol is a subdirectory of each type
                FileNamingConvention.ByDate => root, // symbol is a subdirectory of each date
                FileNamingConvention.Flat => root,
                _ => Path.Combine(root, symbol.ToUpperInvariant())
            };

            return Results.Json(new
            {
                symbol,
                path = symbolPath,
                exists = Directory.Exists(symbolPath),
                namingConvention = opts.NamingConvention.ToString()
            }, jsonOptions);
        })
        .WithName("GetStorageSymbolPath").Produces(200);

        // GET /api/storage/health — storage health summary
        group.MapGet(UiApiRoutes.StorageHealth, (StorageOptions opts) =>
        {
            var rootPath = Path.GetFullPath(opts.RootPath);
            var exists = Directory.Exists(rootPath);
            bool writable = false;

            if (exists)
            {
                try
                {
                    var testFile = Path.Combine(rootPath, $".health-check-{Guid.NewGuid():N}");
                    File.WriteAllText(testFile, "ok");
                    File.Delete(testFile);
                    writable = true;
                }
                catch (IOException) { /* not writable */ }
                catch (UnauthorizedAccessException) { /* access denied */ }
            }

            return Results.Json(new
            {
                status = exists && writable ? "healthy" : exists ? "degraded" : "unhealthy",
                rootPath,
                exists,
                writable,
                namingConvention = opts.NamingConvention.ToString(),
                compress = opts.Compress
            }, jsonOptions);
        })
        .WithName("GetStorageHealth").Produces(200);

        // GET /api/storage/cleanup/candidates — files eligible for cleanup
        group.MapGet(UiApiRoutes.StorageCleanupCandidates, (StorageOptions opts) =>
        {
            var rootPath = Path.GetFullPath(opts.RootPath);
            var candidates = new List<object>();

            if (opts.RetentionDays.HasValue && Directory.Exists(rootPath))
            {
                var cutoff = DateTime.UtcNow.AddDays(-opts.RetentionDays.Value);
                try
                {
                    foreach (var file in Directory.EnumerateFiles(rootPath, "*.jsonl*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc < cutoff)
                        {
                            candidates.Add(new { path = file, sizeBytes = fi.Length, lastModified = fi.LastWriteTimeUtc });
                            if (candidates.Count >= 100)
                                break;
                        }
                    }
                }
                catch (IOException) { /* permission issues */ }
                catch (UnauthorizedAccessException) { /* access denied */ }
            }

            return Results.Json(new
            {
                retentionDays = opts.RetentionDays,
                candidateCount = candidates.Count,
                candidates
            }, jsonOptions);
        })
        .WithName("GetCleanupCandidates").Produces(200);

        // POST /api/storage/cleanup — run storage cleanup
        group.MapPost(UiApiRoutes.StorageCleanup, async (
            IFileMaintenanceService? maintenanceService,
            CancellationToken ct) =>
        {
            if (maintenanceService is null)
                return Results.Problem("File maintenance service not available");

            try
            {
                var report = await maintenanceService.RunHealthCheckAsync(new HealthCheckOptions(), ct);
                return Results.Json(new
                {
                    success = true,
                    report = new
                    {
                        report.ReportId,
                        report.GeneratedAt,
                        report.ScanDurationMs,
                        summary = new
                        {
                            report.Summary.TotalFiles,
                            report.Summary.TotalBytes,
                            report.Summary.HealthyFiles,
                            report.Summary.WarningFiles,
                            report.Summary.CorruptedFiles,
                            report.Summary.OrphanedFiles
                        }
                    }
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Cleanup failed: {ex.Message}");
            }
        })
        .WithName("RunStorageCleanup").Produces(200).Produces(500)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/storage/archive/stats — archive tier statistics
        group.MapGet(UiApiRoutes.StorageArchiveStats, (StorageOptions opts) =>
        {
            var rootPath = Path.GetFullPath(opts.RootPath);
            var archivePath = Path.Combine(rootPath, "_archive");
            long archiveSize = 0;
            int archiveFiles = 0;

            if (Directory.Exists(archivePath))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(archivePath, "*.*", SearchOption.AllDirectories))
                    {
                        archiveFiles++;
                        archiveSize += new FileInfo(file).Length;
                    }
                }
                catch (IOException) { /* permission issues */ }
                catch (UnauthorizedAccessException) { /* access denied */ }
            }

            return Results.Json(new
            {
                archivePath,
                exists = Directory.Exists(archivePath),
                totalFiles = archiveFiles,
                totalSizeBytes = archiveSize,
                totalSizeMb = Math.Round(archiveSize / (1024.0 * 1024.0), 2),
                tiering = opts.Tiering is not null ? new { opts.Tiering.Enabled, tiers = opts.Tiering.Tiers?.Count ?? 0 } : null
            }, jsonOptions);
        })
        .WithName("GetArchiveStats").Produces(200);

        // GET /api/storage/catalog — storage catalog summary
        group.MapGet(UiApiRoutes.StorageCatalog, async (
            IStorageSearchService? searchService,
            StorageOptions opts,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { message = "Storage search not available" }, jsonOptions);

            try
            {
                var catalog = await searchService.DiscoverAsync(new DiscoveryQuery(), ct);
                return Results.Json(catalog, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to load catalog: {ex.Message}");
            }
        })
        .WithName("GetStorageCatalog").Produces(200);

        // GET /api/storage/search/files — search for files
        group.MapGet(UiApiRoutes.StorageSearchFiles, async (
            HttpContext ctx,
            IStorageSearchService? searchService,
            CancellationToken ct) =>
        {
            if (searchService is null)
                return Results.Json(new { message = "Storage search not available", results = Array.Empty<object>() }, jsonOptions);

            var symbol = ctx.Request.Query["symbol"].FirstOrDefault();
            var type = ctx.Request.Query["type"].FirstOrDefault();
            var q = ctx.Request.Query["q"].FirstOrDefault();
            var skip = int.TryParse(ctx.Request.Query["skip"].FirstOrDefault(), out var s) ? s : 0;
            var take = int.TryParse(ctx.Request.Query["take"].FirstOrDefault(), out var t) ? Math.Min(t, 200) : 50;

            // If natural language query provided, parse it
            if (!string.IsNullOrWhiteSpace(q))
            {
                var parsed = searchService.ParseNaturalLanguageQuery(q);
                if (parsed is not null)
                {
                    return Results.Json(new { query = q, parsed, message = "Natural language query parsed" }, jsonOptions);
                }
            }

            var query = new FileSearchQuery(
                Symbols: string.IsNullOrWhiteSpace(symbol) ? null : new[] { symbol },
                Skip: skip,
                Take: take);

            var result = await searchService.SearchFilesAsync(query, ct);
            return Results.Json(new
            {
                totalCount = result.TotalMatches,
                skip,
                take,
                files = result.Results?.Select(f => new { f.Path, f.SizeBytes, f.EventCount, f.Date, f.EventType })
            }, jsonOptions);
        })
        .WithName("SearchStorageFiles").Produces(200);

        // GET /api/storage/health/check — detailed health check
        group.MapGet(UiApiRoutes.StorageHealthCheck, async (
            IFileMaintenanceService? maintenanceService,
            CancellationToken ct) =>
        {
            if (maintenanceService is null)
                return Results.Json(new { status = "unavailable", message = "File maintenance service not available" }, jsonOptions);

            try
            {
                var report = await maintenanceService.RunHealthCheckAsync(
                    new HealthCheckOptions(ValidateChecksums: false, ParallelChecks: 2), ct);
                return Results.Json(report, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Health check failed: {ex.Message}");
            }
        })
        .WithName("RunStorageHealthCheck").Produces(200);

        // GET /api/storage/health/orphans — find orphaned files
        group.MapGet(UiApiRoutes.StorageHealthOrphans, async (
            IFileMaintenanceService? maintenanceService,
            CancellationToken ct) =>
        {
            if (maintenanceService is null)
                return Results.Json(new { message = "File maintenance service not available" }, jsonOptions);

            try
            {
                var report = await maintenanceService.FindOrphansAsync(ct);
                return Results.Json(report, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Orphan scan failed: {ex.Message}");
            }
        })
        .WithName("FindOrphanedFiles").Produces(200);

        // POST /api/storage/tiers/migrate — trigger tier migration
        group.MapPost(UiApiRoutes.StorageTiersMigrate, async (
            ITierMigrationService? tierService,
            StorageOptions opts,
            TierMigrateRequest req,
            CancellationToken ct) =>
        {
            if (tierService is null)
                return Results.Problem("Tier migration service not available");

            if (!Enum.TryParse<StorageTier>(req.TargetTier, ignoreCase: true, out var tier))
                return Results.BadRequest(new { error = $"Invalid target tier: {req.TargetTier}. Use: Hot, Warm, Cold, Archive" });

            try
            {
                var result = await tierService.MigrateAsync(
                    req.SourcePath ?? opts.RootPath,
                    tier,
                    new MigrationOptions(DeleteSource: req.DeleteSource),
                    ct);
                return Results.Json(result, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Migration failed: {ex.Message}");
            }
        })
        .WithName("MigrateTier").Produces(200).Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/storage/tiers/statistics — tier statistics
        group.MapGet(UiApiRoutes.StorageTiersStatistics, async (
            ITierMigrationService? tierService,
            CancellationToken ct) =>
        {
            if (tierService is null)
                return Results.Json(new { message = "Tier migration service not available" }, jsonOptions);

            try
            {
                var stats = await tierService.GetTierStatisticsAsync(ct);
                return Results.Json(stats, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get tier statistics: {ex.Message}");
            }
        })
        .WithName("GetTierStatistics").Produces(200);

        // GET /api/storage/tiers/plan — generate tier migration plan
        group.MapGet(UiApiRoutes.StorageTiersPlan, async (
            HttpContext ctx,
            ITierMigrationService? tierService,
            CancellationToken ct) =>
        {
            if (tierService is null)
                return Results.Json(new { message = "Tier migration service not available" }, jsonOptions);

            var days = int.TryParse(ctx.Request.Query["days"].FirstOrDefault(), out var d) ? d : 7;

            try
            {
                var plan = await tierService.PlanMigrationAsync(TimeSpan.FromDays(days), ct);
                return Results.Json(plan, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to generate migration plan: {ex.Message}");
            }
        })
        .WithName("GetTierMigrationPlan").Produces(200);

        // POST /api/storage/maintenance/defrag — run defragmentation
        group.MapPost(UiApiRoutes.StorageMaintenanceDefrag, async (
            IFileMaintenanceService? maintenanceService,
            CancellationToken ct) =>
        {
            if (maintenanceService is null)
                return Results.Problem("File maintenance service not available");

            try
            {
                var result = await maintenanceService.DefragmentAsync(new DefragOptions(), ct);
                return Results.Json(result, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Defragmentation failed: {ex.Message}");
            }
        })
        .WithName("RunDefragmentation").Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/storage/convert-parquet — convert completed JSONL files to Parquet
        group.MapPost(UiApiRoutes.StorageConvertParquet, async (
            StorageOptions opts,
            CancellationToken ct) =>
        {
            try
            {
                var conversionService = new Meridian.Storage.Services.ParquetConversionService(opts);
                var result = await conversionService.ConvertCompletedDaysAsync(ct: ct);
                return Results.Json(new
                {
                    filesConverted = result.FilesConverted,
                    recordsConverted = result.RecordsConverted,
                    bytesSaved = result.BytesSaved,
                    skippedAlreadyConverted = result.SkippedAlreadyConverted,
                    skippedEmpty = result.SkippedEmpty,
                    errors = result.Errors,
                    outputDirectory = Path.Combine(opts.RootPath, "_parquet")
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Parquet conversion failed: {ex.Message}");
            }
        })
        .WithName("ConvertToParquet").Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/storage/capacity-forecast — predictive storage capacity warning
        group.MapGet(UiApiRoutes.StorageCapacityForecast, (StorageOptions opts) =>
        {
            var rootPath = Path.GetFullPath(opts.RootPath);
            if (!Directory.Exists(rootPath))
            {
                return Results.Json(new
                {
                    error = "Storage directory not found",
                    rootPath
                }, jsonOptions, statusCode: 404);
            }

            try
            {
                // Calculate current storage usage
                var dirInfo = new DirectoryInfo(rootPath);
                var allFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories);
                var totalBytes = allFiles.Sum(f => f.Length);
                var fileCount = allFiles.Length;

                // Calculate growth rate from recent files (last 24h vs previous 24h)
                var now = DateTime.UtcNow;
                var recentFiles = allFiles.Where(f => f.LastWriteTimeUtc > now.AddHours(-24)).ToList();
                var olderFiles = allFiles.Where(f => f.LastWriteTimeUtc > now.AddHours(-48) && f.LastWriteTimeUtc <= now.AddHours(-24)).ToList();

                var recentBytes = recentFiles.Sum(f => f.Length);
                var olderBytes = olderFiles.Sum(f => f.Length);
                var dailyGrowthBytes = recentBytes > 0 ? recentBytes : olderBytes;

                // Check available disk space
                var driveInfo = new DriveInfo(Path.GetPathRoot(rootPath) ?? "/");
                var availableBytes = driveInfo.AvailableFreeSpace;
                var totalDiskBytes = driveInfo.TotalSize;

                // Project when storage will be full
                double? daysUntilFull = null;
                string? warning = null;

                if (dailyGrowthBytes > 0)
                {
                    // Factor in quota limit if configured
                    var effectiveLimit = opts.Quotas?.Global?.MaxBytes ?? availableBytes;
                    var remainingBytes = Math.Min(effectiveLimit - totalBytes, availableBytes);

                    if (remainingBytes > 0)
                    {
                        daysUntilFull = (double)remainingBytes / dailyGrowthBytes;

                        if (daysUntilFull < 3)
                            warning = $"CRITICAL: At current rate ({FormatBytes(dailyGrowthBytes)}/day), storage will be full in {daysUntilFull:F1} days. Enable tier migration or increase disk space.";
                        else if (daysUntilFull < 7)
                            warning = $"WARNING: At current rate ({FormatBytes(dailyGrowthBytes)}/day), storage will be full in {daysUntilFull:F1} days. Consider enabling tier migration or increasing disk space.";
                    }
                    else
                    {
                        warning = "CRITICAL: Storage quota exceeded or disk full.";
                        daysUntilFull = 0;
                    }
                }

                return Results.Json(new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    rootPath,
                    currentUsage = new
                    {
                        totalBytes,
                        totalFormatted = FormatBytes(totalBytes),
                        fileCount
                    },
                    growthRate = new
                    {
                        last24hBytes = recentBytes,
                        last24hFormatted = FormatBytes(recentBytes),
                        previous24hBytes = olderBytes,
                        dailyEstimateBytes = dailyGrowthBytes,
                        dailyEstimateFormatted = FormatBytes(dailyGrowthBytes)
                    },
                    disk = new
                    {
                        availableBytes,
                        availableFormatted = FormatBytes(availableBytes),
                        totalDiskBytes,
                        usagePercent = totalDiskBytes > 0 ? (double)(totalDiskBytes - availableBytes) / totalDiskBytes * 100 : 0
                    },
                    forecast = new
                    {
                        daysUntilFull,
                        warning,
                        quotaConfigured = opts.Quotas?.Global?.MaxBytes != null,
                        quotaMaxBytes = opts.Quotas?.Global?.MaxBytes
                    }
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    error = $"Failed to compute capacity forecast: {ex.Message}"
                }, jsonOptions, statusCode: 500);
            }
        })
        .WithName("GetStorageCapacityForecast")
        .WithDescription("Returns storage capacity forecast with growth rate, disk usage, and days-until-full prediction.")
        .Produces(200);
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }
}

// Request DTOs
internal sealed record TierMigrateRequest(string TargetTier, string? SourcePath = null, bool DeleteSource = false);
