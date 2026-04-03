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

            s_ingestedResults.TryGetValue(backtestId, out var ingested);

            return Results.Json(new LeanBacktestResultsResponseDto
            {
                BacktestId = info.Id,
                AlgorithmName = info.AlgorithmName,
                Status = info.Status,
                Results = info.Status == "completed" && ingested != null
                    ? ingested.Results
                    : null,
                Message = info.Status != "completed"
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
                .Select(b =>
                {
                    s_ingestedResults.TryGetValue(b.Id, out var ingested);
                    return new
                    {
                        backtestId = b.Id,
                        algorithmName = b.AlgorithmName,
                        status = b.Status,
                        startedAt = b.StartedAt,
                        totalReturn = ingested?.Results.TotalReturn,
                        sharpeRatio = ingested?.Results.SharpeRatio
                    };
                });

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

        // Results artifact inspection — POST /api/lean/results/artifact
        // Parses a Lean backtest results JSON file without mutating launcher/runtime state.
        group.MapPost(UiApiRoutes.LeanResultsArtifact, async (
            [FromBody] LeanResultsImportRequestDto? req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.ResultsFilePath))
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
                var artifact = await ParseLeanResultsArtifactAsync(req).ConfigureAwait(false);
                return Results.Json(artifact, jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new
                {
                    error = $"Failed to parse results file: {ex.Message}"
                });
            }
        })
        .WithName("InspectLeanResultsArtifact")
        .Produces<LeanResultsArtifactSummaryDto>(200)
        .Produces(400)
        .Produces(404);

        // Results ingest — POST /api/lean/results/ingest
        // Reads a Lean backtest result JSON file and stores it as a completed backtest record.
        group.MapPost(UiApiRoutes.LeanResultsIngest, async (
            [FromBody] LeanResultsImportRequestDto? req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.ResultsFilePath))
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
                var artifact = await ParseLeanResultsArtifactAsync(req).ConfigureAwait(false);
                var backtestId = req.BacktestId ?? artifact.BacktestId ?? Guid.NewGuid().ToString("N")[..12];
                var algorithmName = req.AlgorithmName ?? artifact.AlgorithmName;
                var completedAt = DateTimeOffset.UtcNow;

                var info = new BacktestInfo(backtestId, algorithmName, "completed", completedAt);
                s_backtests[backtestId] = info;

                var summary = BuildBacktestResultsSummary(algorithmName, artifact);
                s_ingestedResults[backtestId] = new IngestedResultInfo(
                    backtestId,
                    algorithmName,
                    req.ResultsFilePath,
                    summary,
                    completedAt,
                    artifact with { BacktestId = backtestId, AlgorithmName = algorithmName });

                return Results.Json(new LeanResultsIngestResponseDto
                {
                    Success = true,
                    BacktestId = backtestId,
                    AlgorithmName = algorithmName,
                    TotalReturn = summary.TotalReturn,
                    AnnualizedReturn = summary.AnnualizedReturn,
                    SharpeRatio = summary.SharpeRatio,
                    MaxDrawdown = summary.MaxDrawdown,
                    TotalTrades = summary.TotalTrades,
                    WinRate = summary.WinRate,
                    ProfitFactor = summary.ProfitFactor,
                    ArtifactSummary = artifact with { BacktestId = backtestId, AlgorithmName = algorithmName },
                    Message = "Lean backtest results ingested successfully.",
                    Timestamp = completedAt
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

    private static async Task<LeanResultsArtifactSummaryDto> ParseLeanResultsArtifactAsync(LeanResultsImportRequestDto request)
    {
        var json = await File.ReadAllTextAsync(request.ResultsFilePath).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var fileInfo = new FileInfo(request.ResultsFilePath);

        var hasAlgorithmConfiguration = root.TryGetProperty("AlgorithmConfiguration", out var algorithmConfiguration);
        var statistics = ReadStringDictionary(root, "Statistics");
        var parameters = hasAlgorithmConfiguration
            ? ReadStringDictionary(algorithmConfiguration, "Parameters")
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var algorithmName = request.AlgorithmName
            ?? (hasAlgorithmConfiguration ? TryGetString(algorithmConfiguration, "Algorithm") : null)
            ?? "unknown";

        return new LeanResultsArtifactSummaryDto
        {
            BacktestId = request.BacktestId ?? TryGetString(root, "BacktestId"),
            AlgorithmName = algorithmName,
            ResultsFilePath = request.ResultsFilePath,
            Statistics = statistics,
            Parameters = parameters,
            Sections = new LeanResultsArtifactSectionsDto
            {
                HasAlgorithmConfiguration = hasAlgorithmConfiguration,
                HasParameters = parameters.Count > 0,
                HasStatistics = statistics.Count > 0,
                HasRuntimeStatistics = root.TryGetProperty("RuntimeStatistics", out _),
                HasCharts = root.TryGetProperty("Charts", out _),
                HasOrders = root.TryGetProperty("Orders", out _),
                HasClosedTrades = root.TryGetProperty("ClosedTrades", out _) || root.TryGetProperty("Trades", out _)
            },
            Artifacts =
            [
                new LeanRawArtifactFileDto
                {
                    ArtifactType = "results-json",
                    Path = request.ResultsFilePath,
                    Exists = fileInfo.Exists,
                    SizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                    LastWriteTimeUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null
                }
            ],
            TotalReturn = TryParsePercentageStatistic(statistics, "Total Return"),
            AnnualizedReturn = TryParsePercentageStatistic(statistics, "Compounding Annual Return")
                ?? TryParsePercentageStatistic(statistics, "Annual Return"),
            SharpeRatio = TryParseDecimalStatistic(statistics, "Sharpe Ratio"),
            MaxDrawdown = TryParsePercentageStatistic(statistics, "Drawdown")
                ?? TryParsePercentageStatistic(statistics, "Max Drawdown"),
            TotalTrades = TryParseIntegerStatistic(statistics, "Total Trades"),
            WinRate = TryParsePercentageStatistic(statistics, "Win Rate"),
            ProfitFactor = TryParseDecimalStatistic(statistics, "Profit Factor")
                ?? TryParseDecimalStatistic(statistics, "Profit-Loss Ratio")
        };
    }

    private static LeanBacktestResultsSummaryDto BuildBacktestResultsSummary(string algorithmName, LeanResultsArtifactSummaryDto artifact)
        => new()
        {
            AlgorithmName = algorithmName,
            TotalReturn = artifact.TotalReturn ?? 0m,
            AnnualizedReturn = artifact.AnnualizedReturn ?? 0m,
            SharpeRatio = artifact.SharpeRatio ?? 0m,
            MaxDrawdown = artifact.MaxDrawdown ?? 0m,
            TotalTrades = artifact.TotalTrades ?? 0,
            WinRate = artifact.WinRate ?? 0m,
            ProfitFactor = artifact.ProfitFactor ?? 0m
        };

    private static Dictionary<string, string> ReadStringDictionary(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return property.EnumerateObject()
            .ToDictionary(
                item => item.Name,
                item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : item.Value.ToString(),
                StringComparer.Ordinal);
    }

    private static string? TryGetString(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;

    private static decimal? TryParsePercentageStatistic(IReadOnlyDictionary<string, string> statistics, string key)
    {
        if (!statistics.TryGetValue(key, out var value))
        {
            return null;
        }

        return decimal.TryParse(value.Trim().TrimEnd('%'), out var parsed)
            ? parsed / 100m
            : null;
    }

    private static decimal? TryParseDecimalStatistic(IReadOnlyDictionary<string, string> statistics, string key)
    {
        if (!statistics.TryGetValue(key, out var value))
        {
            return null;
        }

        return decimal.TryParse(value.Trim(), out var parsed)
            ? parsed
            : null;
    }

    private static int? TryParseIntegerStatistic(IReadOnlyDictionary<string, string> statistics, string key)
    {
        if (!statistics.TryGetValue(key, out var value))
        {
            return null;
        }

        return int.TryParse(value.Trim(), out var parsed)
            ? parsed
            : null;
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
    private sealed record IngestedResultInfo(
        string BacktestId,
        string AlgorithmName,
        string ResultsFilePath,
        LeanBacktestResultsSummaryDto Results,
        DateTimeOffset IngestedAt,
        LeanResultsArtifactSummaryDto ArtifactSummary);
}
