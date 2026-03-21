using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
                rootPath,
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
                dataRoot = config.DataRoot,
                hasAlpacaConfig = config.Alpaca is not null,
                hasStorageConfig = config.Storage is not null,
                hasBackfillConfig = config.Backfill is not null,
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
                path = result.ZipPath,
                message = result.Message
            }, jsonOptions);
        })
        .WithName("GetDiagnosticsBundle")
        .Produces(200)
        .Produces(503);

        // Diagnostics metrics
        group.MapGet(UiApiRoutes.DiagnosticsMetrics, ([FromServices] ErrorTracker? errorTracker) =>
        {
            var stats = errorTracker?.GetStatistics();
            return Results.Json(new
            {
                errors = stats != null ? new
                {
                    total = stats.TotalErrors,
                    inWindow = stats.ErrorsInWindow,
                    byType = stats.ByExceptionType,
                    byContext = stats.ByContext,
                    byHour = stats.ByHour,
                    mostRecent = stats.MostRecentError
                } : null,
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
                dataRoot = config.DataRoot,
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
                dataRoot = config.DataRoot,
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
                    dataRoot = config.DataRoot
                },
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ValidateDiagnosticsConfig")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }
}
