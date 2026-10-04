using System.Reflection;
using System.Text.Json;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Ui.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering QuantConnect Lean integration API endpoints.
/// Provides actual LEAN_PATH detection, algorithm scanning, symbol mapping, auto-export, and
/// results ingestion for externally run backtests. The data-sync and backtest lifecycle routes
/// (start/status/results) answer 501: no Lean engine integration exists, and honest refusal beats
/// a fabricated "queued" job that never runs.
/// </summary>
public static class LeanEndpoints
{
    private static readonly Dictionary<string, BacktestInfo> s_backtests = new(StringComparer.OrdinalIgnoreCase);
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
                // Meridian neither launches Lean backtests nor runs sync jobs (those routes answer
                // 501), so neither can ever be non-zero. Reported as constants rather than counted:
                // the only records held here are results ingested after an external run finished,
                // all of them "completed", so filtering them for "running" only read as though it
                // could return something.
                activeBacktests = 0,
                activeSyncs = 0,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetLeanStatus").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
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
        .WithName("GetLeanConfig").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
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
        .RequirePermission(UserPermission.ManageStrategies)
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
        .WithName("GetLeanAlgorithms").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces(200);

        // No Lean engine integration exists behind the sync and backtest lifecycle routes: jobs
        // were created "queued", never transitioned, and results hardcoded zeros for a state no
        // job could reach. The routes stay mapped — with their authorization declarations intact —
        // so clients get an honest 501 problem document instead of a 404 that reads as a wrong URL
        // or a fabricated 200. The results-ingest route below remains the real path for recording
        // externally run Lean backtests.
        group.MapPost(UiApiRoutes.LeanSync, (HttpContext context) =>
            ApiProblemDetails.NotImplemented(
                context,
                "No Lean engine integration exists: data sync jobs are not implemented. "
                + "Use the auto-export service (/api/lean/auto-export) to mirror stored data into the Lean data folder."))
        .WithName("StartLeanSync")
        .RequirePermission(UserPermission.ManageStrategies)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.LeanSyncStatus, (HttpContext context) =>
            ApiProblemDetails.NotImplemented(
                context,
                "No Lean engine integration exists: data sync jobs are not implemented, so there is no sync status to report."))
        .WithName("GetLeanSyncStatus").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost(UiApiRoutes.LeanBacktestStart, (HttpContext context) =>
            ApiProblemDetails.NotImplemented(
                context,
                "No Lean engine integration exists: Meridian does not launch Lean backtests. "
                + "Run the backtest with the Lean CLI and record its result via /api/lean/results/ingest."))
        .WithName("StartLeanBacktest")
        .RequirePermission(UserPermission.ManageStrategies)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.LeanBacktestStatus, (string backtestId, HttpContext context) =>
            ApiProblemDetails.NotImplemented(
                context,
                "No Lean engine integration exists: Meridian does not run Lean backtests, so there is no live status to report. "
                + "Ingested results appear in /api/lean/backtest/history."))
        .WithName("GetLeanBacktestStatus").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet(UiApiRoutes.LeanBacktestResults, (string backtestId, HttpContext context) =>
            ApiProblemDetails.NotImplemented(
                context,
                "No Lean engine integration exists: Meridian does not run Lean backtests and fabricates no metrics. "
                + "Record an externally run backtest's result file via /api/lean/results/ingest."))
        .WithName("GetLeanBacktestResults").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces(StatusCodes.Status501NotImplemented);

        // Stop backtest. Meridian never launched one, so there is nothing here to stop: the only
        // records this route could find are results ingested *after* an external Lean run had
        // already finished. Marking one "stopped" claimed an action Meridian cannot perform and
        // overwrote a completed result's status while doing it, which is the fabrication the rest
        // of this lifecycle was already retired for (#2726).
        group.MapPost(UiApiRoutes.LeanBacktestStop, (string backtestId, HttpContext context) =>
            ApiProblemDetails.NotImplemented(
                context,
                "No Lean engine integration exists: Meridian does not launch Lean backtests and cannot stop one. "
                + "Stop the run with the Lean CLI that started it."))
        .WithName("StopLeanBacktest")
        .RequirePermission(UserPermission.ManageStrategies)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Backtest history
        group.MapGet(UiApiRoutes.LeanBacktestHistory, async (int? limit, [FromServices] IStrategyRepository? repository) =>
        {
            var requestedLimit = Math.Max(1, limit ?? 20);
            var history = s_backtests.Values
                .OrderByDescending(b => b.StartedAt)
                .Take(requestedLimit)
                .Select(b => new { backtestId = b.Id, algorithmName = b.AlgorithmName, status = b.Status, startedAt = b.StartedAt })
                .ToList();

            if (repository is not null)
            {
                var persistedLeanRuns = await repository
                    .QueryRunsAsync(new StrategyRunRepositoryQuery(RunTypes: [RunType.Backtest], Limit: requestedLimit * 4))
                    .ConfigureAwait(false);

                foreach (var run in persistedLeanRuns.Where(static run =>
                             string.Equals(run.Engine, "Lean", StringComparison.OrdinalIgnoreCase)))
                {
                    history.Add(new
                    {
                        backtestId = run.RunId,
                        algorithmName = run.StrategyName,
                        status = run.EndedAt.HasValue ? "completed" : "running",
                        startedAt = run.StartedAt
                    });
                }
            }

            var deduped = history
                .GroupBy(entry => entry.backtestId, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.OrderByDescending(entry => entry.startedAt).First())
                .OrderByDescending(entry => entry.startedAt)
                .Take(requestedLimit)
                .ToArray();

            return Results.Json(new { backtests = deduped, total = deduped.Length, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetLeanBacktestHistory").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
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
        .RequirePermission(UserPermission.ManageStrategies)
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
        .WithName("GetLeanAutoExportStatus").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
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
        .RequirePermission(UserPermission.ManageStrategies)
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Results ingest — POST /api/lean/results/ingest
        // Reads a Lean backtest result JSON file and stores it as a completed backtest record.
        group.MapPost(UiApiRoutes.LeanResultsIngest, async (
            [FromBody] LeanResultsIngestRequest? req,
            HttpContext context,
            [FromServices] IStrategyRepository? repository) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageStrategies))
            {
                return Results.Forbid();
            }

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

                var canonicalResult = CanonicalBacktestResultNormalizer.FromLeanResult(
                    root,
                    backtestId,
                    algorithmName,
                    DateTimeOffset.UtcNow);
                var metrics = canonicalResult.Metrics;
                var totalReturn = metrics.TotalReturn;
                var sharpe = (decimal)metrics.SharpeRatio;
                var totalTrades = metrics.TotalTrades;

                if (repository is not null)
                {
                    var runEntry = StrategyRunEntry.Start(
                            strategyId: $"lean-{algorithmName.ToLowerInvariant().Replace(' ', '-')}",
                            strategyName: algorithmName,
                            runType: RunType.Backtest,
                            runId: backtestId,
                            engine: "Lean")
                        .Complete(canonicalResult);

                    await repository.RecordRunAsync(runEntry).ConfigureAwait(false);
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
        .WithName("IngestLeanResults").RequirePermission(UserPermission.ManageStrategies)
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
        .WithName("GetLeanSymbolMap").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
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
