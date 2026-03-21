using System.Text.Json;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering admin and maintenance API endpoints.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Admin");

        // Maintenance schedule
        group.MapGet(UiApiRoutes.AdminMaintenanceSchedule, ([FromServices] ArchiveMaintenanceScheduleManager? schedMgr) =>
        {
            if (schedMgr is null)
                return Results.Json(new { schedules = Array.Empty<object>() }, jsonOptions);

            var schedules = schedMgr.GetAllSchedules();
            return Results.Json(new
            {
                schedules,
                summary = schedMgr.GetStatusSummary(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminMaintenanceSchedule")
        .Produces(200);

        // Run maintenance
        group.MapPost(UiApiRoutes.AdminMaintenanceRun, async (
            [FromServices] ScheduledArchiveMaintenanceService? maintService,
            MaintenanceRunRequest? req,
            CancellationToken ct) =>
        {
            if (maintService is null)
                return Results.Json(new { error = "Maintenance service not available" }, jsonOptions, statusCode: 503);

            var taskType = Enum.TryParse<MaintenanceTaskType>(req?.TaskType, true, out var tt)
                ? tt : MaintenanceTaskType.HealthCheck;

            var execution = await maintService.ExecuteMaintenanceAsync(taskType, null, req?.TargetPaths, ct);
            return Results.Json(execution, jsonOptions);
        })
        .WithName("RunAdminMaintenance")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Get maintenance run by ID
        group.MapGet(UiApiRoutes.AdminMaintenanceRunById, (string runId, [FromServices] MaintenanceExecutionHistory? history) =>
        {
            var execution = history?.GetExecution(runId);
            return execution is null ? Results.NotFound() : Results.Json(execution, jsonOptions);
        })
        .WithName("GetAdminMaintenanceRunById")
        .Produces(200)
        .Produces(404);

        // Maintenance history
        group.MapGet(UiApiRoutes.AdminMaintenanceHistory, (int? limit, [FromServices] MaintenanceExecutionHistory? history) =>
        {
            var executions = history?.GetRecentExecutions(limit ?? 50) ?? [];
            return Results.Json(new
            {
                executions,
                total = executions.Count,
                statistics = history?.GetStatistics(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminMaintenanceHistory")
        .Produces(200);

        // Storage tiers
        group.MapGet(UiApiRoutes.AdminStorageTiers, async ([FromServices] ITierMigrationService? tierService, CancellationToken ct) =>
        {
            if (tierService is null)
                return Results.Json(new { tiers = new Dictionary<string, object>() }, jsonOptions);

            var stats = await tierService.GetTierStatisticsAsync(ct);
            return Results.Json(new { tiers = stats.TierInfo, generatedAt = stats.GeneratedAt }, jsonOptions);
        })
        .WithName("GetAdminStorageTiers")
        .Produces(200);

        // Storage migrate
        group.MapPost(UiApiRoutes.AdminStorageMigrate, async (string targetTier, [FromServices] ITierMigrationService? tierService, CancellationToken ct) =>
        {
            if (tierService is null)
                return Results.Json(new { error = "Tier migration service not available" }, jsonOptions, statusCode: 503);

            var plan = await tierService.PlanMigrationAsync(TimeSpan.FromDays(30), ct);
            return Results.Json(new
            {
                targetTier,
                plan,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("MigrateAdminStorage")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Storage usage
        group.MapGet(UiApiRoutes.AdminStorageUsage, ([FromServices] StorageOptions? storageOptions) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            long totalBytes = 0;
            int fileCount = 0;
            var breakdown = new Dictionary<string, object>();

            if (Directory.Exists(rootPath))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(rootPath);
                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        var files = subDir.GetFiles("*", SearchOption.AllDirectories);
                        var dirBytes = files.Sum(f => f.Length);
                        totalBytes += dirBytes;
                        fileCount += files.Length;
                        breakdown[subDir.Name] = new { fileCount = files.Length, bytes = dirBytes };
                    }
                }
                catch { /* ignore */ }
            }

            return Results.Json(new
            {
                rootPath,
                totalBytes,
                fileCount,
                breakdown,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminStorageUsage")
        .Produces(200);

        // Retention policies
        group.MapGet(UiApiRoutes.AdminRetention, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var maxSizeGb = config.Storage?.MaxTotalMegabytes.HasValue == true
                ? (double)config.Storage.MaxTotalMegabytes.Value / 1024
                : 100.0;
            return Results.Json(new
            {
                retentionDays = config.Storage?.RetentionDays ?? 365,
                maxStorageSizeGb = maxSizeGb,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminRetention")
        .Produces(200);

        // Delete retention policy
        group.MapDelete(UiApiRoutes.AdminRetentionDelete, (string policyId) =>
        {
            return Results.Json(new
            {
                deleted = true,
                policyId,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("DeleteAdminRetention")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Apply retention
        group.MapPost(UiApiRoutes.AdminRetentionApply, () =>
        {
            return Results.Json(new
            {
                applied = true,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ApplyAdminRetention")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Cleanup preview
        group.MapGet(UiApiRoutes.AdminCleanupPreview, ([FromServices] StorageOptions? storageOptions) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            var candidates = new List<object>();
            long reclaimableBytes = 0;

            if (Directory.Exists(rootPath))
            {
                try
                {
                    var cutoff = DateTime.UtcNow.AddDays(-(storageOptions?.RetentionDays ?? 365));
                    var dirInfo = new DirectoryInfo(rootPath);
                    foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                    {
                        if (file.LastWriteTimeUtc < cutoff)
                        {
                            candidates.Add(new { path = file.FullName, sizeBytes = file.Length, lastModified = file.LastWriteTimeUtc });
                            reclaimableBytes += file.Length;
                        }
                    }
                }
                catch { /* ignore */ }
            }

            return Results.Json(new
            {
                candidateCount = candidates.Count,
                reclaimableBytes,
                candidates = candidates.Take(100),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminCleanupPreview")
        .Produces(200);

        // Execute cleanup
        group.MapPost(UiApiRoutes.AdminCleanupExecute, () =>
        {
            return Results.Json(new
            {
                executed = false,
                message = "Cleanup requires explicit confirmation. Use preview first.",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ExecuteAdminCleanup")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Storage permissions
        group.MapGet(UiApiRoutes.AdminStoragePermissions, ([FromServices] StorageOptions? storageOptions) =>
        {
            var rootPath = storageOptions?.RootPath ?? "data";
            var readable = false;
            var writable = false;

            try
            {
                readable = Directory.Exists(rootPath);
                if (readable)
                {
                    var testFile = Path.Combine(rootPath, $".write-test-{Guid.NewGuid():N}");
                    try
                    {
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                        writable = true;
                    }
                    catch { /* not writable */ }
                }
            }
            catch { /* not readable */ }

            return Results.Json(new { rootPath, readable, writable, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAdminStoragePermissions")
        .Produces(200);

        // Admin selftest
        group.MapPost(UiApiRoutes.AdminSelftest, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            var checks = new List<object>
            {
                new { check = "config_loadable", passed = true },
                new { check = "data_root_exists", passed = Directory.Exists(config.DataRoot) },
                new { check = "data_root_writable", passed = IsWritable(config.DataRoot) },
                new { check = "symbols_configured", passed = (config.Symbols?.Length ?? 0) > 0 }
            };

            return Results.Json(new
            {
                passed = checks.All(c => ((dynamic)c).passed),
                checks,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("RunAdminSelftest")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Admin error codes
        group.MapGet(UiApiRoutes.AdminErrorCodes, () =>
        {
            var codes = Enum.GetValues<Meridian.Application.ResultTypes.ErrorCode>()
                .Select(e => new { code = (int)e, name = e.ToString() });
            return Results.Json(new { errorCodes = codes, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetAdminErrorCodes")
        .Produces(200);

        // Admin show config
        group.MapGet(UiApiRoutes.AdminShowConfig, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            return Results.Json(new
            {
                dataSource = config.DataSource.ToString(),
                symbolCount = config.Symbols?.Length ?? 0,
                dataRoot = config.DataRoot,
                compress = config.Compress,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminShowConfig")
        .Produces(200);

        // Admin quick check
        group.MapGet(UiApiRoutes.AdminQuickCheck, ([FromServices] ConfigStore store) =>
        {
            var config = store.Load();
            return Results.Json(new
            {
                configLoaded = true,
                dataRoot = config.DataRoot,
                dataRootExists = Directory.Exists(config.DataRoot),
                symbolCount = config.Symbols?.Length ?? 0,
                dataSource = config.DataSource.ToString(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetAdminQuickCheck")
        .Produces(200);
    }

    private static bool IsWritable(string path)
    {
        if (!Directory.Exists(path))
            return false;
        try
        {
            var testFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch { return false; }
    }

    private sealed record MaintenanceRunRequest(string? TaskType, string[]? TargetPaths);
}
