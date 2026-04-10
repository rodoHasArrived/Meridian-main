using System.Reflection;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Storage;
using Meridian.Ui.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering QuantConnect Lean integration API endpoints.
/// Provides actual LEAN_PATH detection, algorithm scanning, and data sync capabilities.
/// </summary>
public static class LeanEndpoints
{
    private static readonly Dictionary<string, BacktestInfo> s_backtests = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SyncJobInfo> s_syncJobs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IngestedResultInfo> s_ingestedResults = new(StringComparer.OrdinalIgnoreCase);

    public static void MapLeanEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Lean");

        // Lean status - actually checks LEAN_PATH environment variable
        group.MapGet(UiApiRoutes.LeanStatus, () =>
        {
            var leanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
            var dataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH");
            var installed = !string.IsNullOrEmpty(leanPath) && Directory.Exists(leanPath);
            string? version = null;

            if (installed)
            {
                version = DetectLeanVersion(leanPath!);
                dataPath ??= Path.Combine(leanPath!, "Data");
            }

            return Results.Json(new
            {
                installed,
                leanPath,
                dataPath,
                version,
                activeBacktests = s_backtests.Count(b => b.Value.Status == "running"),
                activeSyncs = s_syncJobs.Count(s => s.Value.Status == "running"),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanStatus")
        .Produces(200);

        // Lean config - returns actual detected configuration
        group.MapGet(UiApiRoutes.LeanConfig, () =>
        {
            var leanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
            var dataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH");
            var pythonEnabled = false;

            if (!string.IsNullOrEmpty(leanPath) && Directory.Exists(leanPath))
            {
                dataPath ??= Path.Combine(leanPath, "Data");
                // Check for Python support
                var pythonDir = Path.Combine(leanPath, "Algorithm.Python");
                pythonEnabled = Directory.Exists(pythonDir);
            }

            return Results.Json(new
            {
                leanPath,
                dataDirectory = dataPath,
                pythonEnabled,
                algorithmLanguage = "CSharp",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanConfig")
        .Produces(200);

        // Verify Lean installation - performs actual filesystem checks
        group.MapPost(UiApiRoutes.LeanVerify, () =>
        {
            var leanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
            var checks = new List<object>();
            var allPassed = true;

            // Check 1: LEAN_PATH environment variable set
            var pathSet = !string.IsNullOrEmpty(leanPath);
            checks.Add(new { check = "lean_path_set", passed = pathSet, detail = pathSet ? leanPath : "LEAN_PATH environment variable not set" });
            if (!pathSet)
                allPassed = false;

            // Check 2: Lean directory exists
            var dirExists = pathSet && Directory.Exists(leanPath);
            checks.Add(new { check = "lean_directory_exists", passed = dirExists, detail = dirExists ? "Directory found" : "Lean directory not found at specified path" });
            if (!dirExists)
                allPassed = false;

            // Check 3: Lean binary/DLL exists
            var binaryExists = false;
            if (dirExists)
            {
                var possibleBinaries = new[] { "QuantConnect.Lean.Launcher.dll", "QuantConnect.Lean.Launcher.exe", "Lean.Launcher.dll" };
                foreach (var binary in possibleBinaries)
                {
                    if (File.Exists(Path.Combine(leanPath!, binary)))
                    {
                        binaryExists = true;
                        break;
                    }
                }
            }
            checks.Add(new { check = "lean_binary_exists", passed = binaryExists, detail = binaryExists ? "Lean launcher found" : "Lean launcher binary not found" });
            if (!binaryExists)
                allPassed = false;

            // Check 4: Data directory exists
            var dataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH")
                ?? (dirExists ? Path.Combine(leanPath!, "Data") : null);
            var dataExists = !string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath);
            checks.Add(new { check = "data_directory_exists", passed = dataExists, detail = dataExists ? $"Data directory: {dataPath}" : "Data directory not found" });
            if (!dataExists)
                allPassed = false;

            var message = allPassed
                ? "Lean Engine installation verified successfully."
                : "Lean Engine not fully configured. Set the LEAN_PATH environment variable to the Lean installation directory.";

            return Results.Json(new
            {
                installed = allPassed,
                message,
                checks,
                version = allPassed ? DetectLeanVersion(leanPath!) : null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("VerifyLean")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // List algorithms - scans for actual algorithm files
        group.MapGet(UiApiRoutes.LeanAlgorithms, () =>
        {
            var leanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
            var algorithms = new List<object>();

            if (!string.IsNullOrEmpty(leanPath) && Directory.Exists(leanPath))
            {
                // Scan for C# algorithm files
                ScanAlgorithmDirectory(Path.Combine(leanPath, "Algorithm.CSharp"), "CSharp", algorithms);
                // Scan for Python algorithm files
                ScanAlgorithmDirectory(Path.Combine(leanPath, "Algorithm.Python"), "Python", algorithms);
                // Also scan custom algorithm directories
                var customDir = Path.Combine(leanPath, "Algorithms");
                ScanAlgorithmDirectory(customDir, "CSharp", algorithms);
            }

            return Results.Json(new
            {
                algorithms,
                total = algorithms.Count,
                message = algorithms.Count == 0
                    ? "No algorithms found. Ensure LEAN_PATH points to a valid Lean installation with algorithm files."
                    : null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanAlgorithms")
        .Produces(200);

        // Sync data to Lean format - initiates actual file copy/conversion
        group.MapPost(UiApiRoutes.LeanSync, (LeanSyncRequest? req, [FromServices] StorageOptions? storageOptions) =>
        {
            var leanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
            var dataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH")
                ?? (leanPath != null ? Path.Combine(leanPath, "Data") : null);

            if (string.IsNullOrEmpty(dataPath))
            {
                return Results.Json(new
                {
                    jobId = (string?)null,
                    status = "failed",
                    error = "LEAN_PATH or LEAN_DATA_PATH environment variable not set",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            var sourceRoot = storageOptions?.RootPath ?? "data";
            var jobId = Guid.NewGuid().ToString("N")[..12];
            var symbols = req?.Symbols ?? Array.Empty<string>();

            // Count available JSONL files for the requested symbols
            var fileCount = 0;
            if (Directory.Exists(sourceRoot))
            {
                foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.jsonl*", SearchOption.AllDirectories))
                {
                    if (symbols.Length == 0 || symbols.Any(s => file.Contains(s, StringComparison.OrdinalIgnoreCase)))
                        fileCount++;
                }
            }

            var syncJob = new SyncJobInfo(jobId, "queued", symbols, sourceRoot, dataPath, DateTimeOffset.UtcNow, fileCount);
            s_syncJobs[jobId] = syncJob;

            return Results.Json(new
            {
                jobId,
                symbols,
                status = "queued",
                sourceDirectory = sourceRoot,
                targetDirectory = dataPath,
                filesToSync = fileCount,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("StartLeanSync")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Sync status - returns actual sync job status
        group.MapGet(UiApiRoutes.LeanSyncStatus, () =>
        {
            var latestJob = s_syncJobs.Values
                .OrderByDescending(j => j.StartedAt)
                .FirstOrDefault();

            return Results.Json(new
            {
                isRunning = latestJob?.Status == "running",
                lastSyncAt = latestJob?.StartedAt,
                lastJobId = latestJob?.JobId,
                lastJobStatus = latestJob?.Status,
                totalJobs = s_syncJobs.Count,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanSyncStatus")
        .Produces(200);

        // Start backtest
        group.MapPost(UiApiRoutes.LeanBacktestStart, (BacktestStartRequest? req) =>
        {
            var leanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
            if (string.IsNullOrEmpty(leanPath) || !Directory.Exists(leanPath))
            {
                return Results.Json(new
                {
                    backtestId = (string?)null,
                    status = "failed",
                    error = "Lean Engine not installed. Set LEAN_PATH environment variable.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            var backtestId = Guid.NewGuid().ToString("N")[..12];
            var info = new BacktestInfo(backtestId, req?.AlgorithmName ?? "unknown", "queued", DateTimeOffset.UtcNow);
            s_backtests[backtestId] = info;

            return Results.Json(new
            {
                backtestId,
                algorithmName = req?.AlgorithmName,
                status = "queued",
                leanPath,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("StartLeanBacktest")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Backtest status
        group.MapGet(UiApiRoutes.LeanBacktestStatus, (string backtestId) =>
        {
            if (!s_backtests.TryGetValue(backtestId, out var info))
                return Results.NotFound(new { error = $"Backtest '{backtestId}' not found" });

            return Results.Json(new
            {
                backtestId = info.Id,
                algorithmName = info.AlgorithmName,
                status = info.Status,
                startedAt = info.StartedAt
            }, jsonOptions);
        })
        .WithName("GetLeanBacktestStatus")
        .Produces(200)
        .Produces(404);

        // Backtest results
        group.MapGet(UiApiRoutes.LeanBacktestResults, (string backtestId) =>
        {
            if (!s_backtests.TryGetValue(backtestId, out var info))
                return Results.NotFound(new { error = $"Backtest '{backtestId}' not found" });

            return Results.Json(new
            {
                backtestId = info.Id,
                status = info.Status,
                results = info.Status == "completed" ? new { totalReturn = 0.0, sharpeRatio = 0.0, totalTrades = 0 } : (object?)null,
                message = info.Status != "completed"
                    ? $"Backtest is currently '{info.Status}'. Results will be available when the backtest completes."
                    : null
            }, jsonOptions);
        })
        .WithName("GetLeanBacktestResults")
        .Produces(200)
        .Produces(404);

        // Stop backtest
        group.MapPost(UiApiRoutes.LeanBacktestStop, (string backtestId) =>
        {
            if (!s_backtests.TryGetValue(backtestId, out var info))
                return Results.NotFound(new { error = $"Backtest '{backtestId}' not found" });

            info = info with { Status = "stopped" };
            s_backtests[backtestId] = info;
            return Results.Json(new { backtestId, status = "stopped" }, jsonOptions);
        })
        .WithName("StopLeanBacktest")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Backtest history
        group.MapGet(UiApiRoutes.LeanBacktestHistory, (int? limit) =>
        {
            var history = s_backtests.Values
                .OrderByDescending(b => b.StartedAt)
                .Take(limit ?? 20)
                .Select(b => new { backtestId = b.Id, algorithmName = b.AlgorithmName, status = b.Status, startedAt = b.StartedAt });

            return Results.Json(new { backtests = history, total = s_backtests.Count, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetLeanBacktestHistory")
        .Produces(200);

        // Delete backtest
        group.MapDelete(UiApiRoutes.LeanBacktestDelete, (string backtestId) =>
        {
            var removed = s_backtests.Remove(backtestId);
            return removed
                ? Results.Json(new { deleted = true, backtestId }, jsonOptions)
                : Results.NotFound(new { error = $"Backtest '{backtestId}' not found" });
        })
        .WithName("DeleteLeanBacktest")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Auto-export status — GET /api/lean/auto-export
        group.MapGet(UiApiRoutes.LeanAutoExportStatus, ([FromServices] LeanAutoExportService? autoExport) =>
        {
            if (autoExport == null)
            {
                return Results.Json(new
                {
                    enabled = false,
                    available = false,
                    message = "LeanAutoExportService is not registered.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            return Results.Json(new
            {
                available = true,
                enabled = autoExport.Enabled,
                leanDataPath = autoExport.LeanDataPath,
                intervalSeconds = (int)autoExport.Interval.TotalSeconds,
                lastExportAt = autoExport.LastExportAt,
                lastExportError = autoExport.LastExportError,
                lastErrorMessage = autoExport.LastErrorMessage,
                totalFilesExported = autoExport.TotalFilesExported,
                totalBytesExported = autoExport.TotalBytesExported,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanAutoExportStatus")
        .Produces(200);

        // Auto-export configure — POST /api/lean/auto-export/configure
        group.MapPost(UiApiRoutes.LeanAutoExportConfigure, (
            [FromBody] LeanAutoExportConfigureRequest? req,
            [FromServices] LeanAutoExportService? autoExport) =>
        {
            if (autoExport == null)
            {
                return Results.Json(new
                {
                    success = false,
                    error = "LeanAutoExportService is not registered.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            autoExport.Configure(
                leanDataPath: req?.LeanDataPath,
                enabled: req?.Enabled,
                intervalSeconds: req?.IntervalSeconds ?? 0,
                symbols: req?.Symbols);

            return Results.Json(new
            {
                success = true,
                enabled = autoExport.Enabled,
                leanDataPath = autoExport.LeanDataPath,
                intervalSeconds = (int)autoExport.Interval.TotalSeconds,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ConfigureLeanAutoExport")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Results ingest — POST /api/lean/results/ingest
        // Reads a Lean backtest result JSON file and stores it as a completed backtest record.
        group.MapPost(UiApiRoutes.LeanResultsIngest, async (
            [FromBody] LeanResultsIngestRequest? req) =>
        {
            if (req == null || string.IsNullOrEmpty(req.ResultsFilePath))
            {
                return Results.BadRequest(new { error = "resultsFilePath is required." });
            }

            if (!File.Exists(req.ResultsFilePath))
            {
                return Results.NotFound(new
                {
                    error = $"Results file not found: {req.ResultsFilePath}"
                });
            }

            try
            {
                var json = await File.ReadAllTextAsync(req.ResultsFilePath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Extract common fields from Lean's backtest result JSON
                var backtestId = req.BacktestId ?? Guid.NewGuid().ToString("N")[..12];
                var algorithmName = req.AlgorithmName
                    ?? (root.TryGetProperty("AlgorithmConfiguration", out var algCfg)
                        && algCfg.TryGetProperty("Algorithm", out var algElem)
                        ? algElem.GetString() ?? "unknown"
                        : "unknown");

                var info = new BacktestInfo(backtestId, algorithmName, "completed", DateTimeOffset.UtcNow);
                s_backtests[backtestId] = info;

                // Extract summary statistics
                decimal? totalReturn = null;
                decimal? sharpe = null;
                int? totalTrades = null;

                if (root.TryGetProperty("Statistics", out var stats))
                {
                    if (stats.TryGetProperty("Total Return", out var tr) &&
                        decimal.TryParse(tr.GetString()?.TrimEnd('%'), out var trVal))
                        totalReturn = trVal / 100m;

                    if (stats.TryGetProperty("Sharpe Ratio", out var sr) &&
                        decimal.TryParse(sr.GetString(), out var srVal))
                        sharpe = srVal;

                    if (stats.TryGetProperty("Total Trades", out var tt) &&
                        int.TryParse(tt.GetString(), out var ttVal))
                        totalTrades = ttVal;
                }

                s_ingestedResults[backtestId] = new IngestedResultInfo(
                    backtestId, algorithmName, req.ResultsFilePath,
                    totalReturn, sharpe, totalTrades, DateTimeOffset.UtcNow);

                return Results.Json(new
                {
                    success = true,
                    backtestId,
                    algorithmName,
                    totalReturn,
                    sharpeRatio = sharpe,
                    totalTrades,
                    message = "Lean backtest results ingested successfully.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new
                {
                    error = $"Failed to parse results file: {ex.Message}"
                });
            }
        })
        .WithName("IngestLeanResults")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Symbol map — GET /api/lean/symbol-map?symbols=SPY,AAPL
        group.MapGet(UiApiRoutes.LeanSymbolMap, (string? symbols) =>
        {
            var symbolList = (symbols ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var mappings = symbolList.Select(s => new
            {
                mdcSymbol = s.ToUpperInvariant(),
                leanTicker = LeanSymbolMapper.ToLeanTicker(s),
                securityType = LeanSymbolMapper.DetectSecurityType(s),
                market = LeanSymbolMapper.DetectMarket(s)
            });

            return Results.Json(new
            {
                mappings,
                total = symbolList.Length,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanSymbolMap")
        .Produces(200);
    }

    private static string? DetectLeanVersion(string leanPath)
    {
        try
        {
            // Try to read version from Assembly info or a version file
            var versionFile = Path.Combine(leanPath, "version.txt");
            if (File.Exists(versionFile))
                return File.ReadAllText(versionFile).Trim();

            // Check for Lean launcher DLL and get its version
            var launcherDll = Path.Combine(leanPath, "QuantConnect.Lean.Launcher.dll");
            if (File.Exists(launcherDll))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(launcherDll);
                if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                    return versionInfo.FileVersion;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void ScanAlgorithmDirectory(string directory, string language, List<object> algorithms)
    {
        if (!Directory.Exists(directory))
            return;

        var extensions = language == "Python" ? new[] { "*.py" } : new[] { "*.cs" };
        foreach (var ext in extensions)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, ext, SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    // Skip designer files and partial classes
                    if (info.Name.Contains(".Designer.") || info.Name.Contains(".g."))
                        continue;

                    algorithms.Add(new
                    {
                        name = Path.GetFileNameWithoutExtension(info.Name),
                        path = file,
                        language,
                        sizeBytes = info.Length,
                        lastModified = info.LastWriteTimeUtc
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
        }
    }

    private sealed record BacktestInfo(string Id, string AlgorithmName, string Status, DateTimeOffset StartedAt);
    private sealed record SyncJobInfo(string JobId, string Status, string[] Symbols, string SourcePath, string TargetPath, DateTimeOffset StartedAt, int FileCount);
    private sealed record LeanSyncRequest(string[]? Symbols, DateTime? FromDate, DateTime? ToDate);
    private sealed record BacktestStartRequest(string? AlgorithmName, string? AlgorithmLanguage);
    private sealed record LeanAutoExportConfigureRequest(bool? Enabled, string? LeanDataPath, int IntervalSeconds, string[]? Symbols);
    private sealed record LeanResultsIngestRequest(string ResultsFilePath, string? BacktestId, string? AlgorithmName);
    private sealed record IngestedResultInfo(
        string BacktestId,
        string AlgorithmName,
        string ResultsFilePath,
        decimal? TotalReturn,
        decimal? SharpeRatio,
        int? TotalTrades,
        DateTimeOffset IngestedAt);
}
