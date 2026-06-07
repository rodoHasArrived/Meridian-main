using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Monitoring;
using Meridian.Core.Diagnostics;
using Meridian.Platform.Diagnostics;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Platform service for generating diagnostic bundles containing system state and logs.
/// Implements QW-16: Diagnostic Bundle Generator.
/// </summary>
public sealed class DiagnosticBundleService
{
    private readonly ILogger _log = LoggingSetup.ForContext<DiagnosticBundleService>();
    private readonly string _dataRoot;
    private readonly Func<MetricsSnapshot>? _metricsProvider;
    private readonly Func<AppConfig>? _configProvider;
    private readonly Func<ErrorQueryResult>? _recentErrorsProvider;
    private readonly Func<ShutdownSequenceDiagnosticSnapshot>? _shutdownDiagnosticsProvider;
    private readonly Func<IReadOnlyCollection<ProviderConnectionDiagnosticSnapshot>>? _providerConnectionDiagnosticsProvider;

    public DiagnosticBundleService(
        string dataRoot,
        Func<MetricsSnapshot>? metricsProvider = null,
        Func<AppConfig>? configProvider = null,
        Func<ErrorQueryResult>? recentErrorsProvider = null,
        Func<ShutdownSequenceDiagnosticSnapshot>? shutdownDiagnosticsProvider = null,
        Func<IReadOnlyCollection<ProviderConnectionDiagnosticSnapshot>>? providerConnectionDiagnosticsProvider = null)
    {
        _dataRoot = dataRoot;
        _metricsProvider = metricsProvider;
        _configProvider = configProvider;
        _recentErrorsProvider = recentErrorsProvider;
        _shutdownDiagnosticsProvider = shutdownDiagnosticsProvider;
        _providerConnectionDiagnosticsProvider = providerConnectionDiagnosticsProvider;
    }

    /// <summary>
    /// Generates a diagnostic bundle as a ZIP file.
    /// </summary>
    public async Task<DiagnosticBundleResult> GenerateAsync(
        DiagnosticBundleOptions options,
        CancellationToken ct = default)
    {
        var bundleId = $"diag_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}".Substring(0, 40);
        var operationId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tempDir = Path.Combine(Path.GetTempPath(), bundleId);
        var zipPath = Path.Combine(Path.GetTempPath(), $"{bundleId}.zip");

        try
        {
            _log.Information(
                "Diagnostic bundle generation started for {OperationName} with {CorrelationId}",
                "diagnostic-bundle.generate",
                operationId);

            Directory.CreateDirectory(tempDir);

            var manifest = new DiagnosticManifest
            {
                BundleId = bundleId,
                CorrelationId = operationId,
                GeneratedAt = startedAt,
                MachineName = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                Options = options
            };

            if (options.IncludeRuntimeSummary)
            {
                await CollectRuntimeSummaryAsync(tempDir, manifest, ct);
            }

            // Collect diagnostic data
            if (options.IncludeSystemInfo)
            {
                await CollectSystemInfoAsync(tempDir, manifest, ct);
            }

            if (options.IncludeConfiguration)
            {
                await CollectConfigurationAsync(tempDir, manifest, ct);
            }

            if (options.IncludeMetrics)
            {
                await CollectMetricsAsync(tempDir, manifest, ct);
            }

            if (options.IncludeTrackedErrors)
            {
                await CollectTrackedErrorsAsync(tempDir, manifest, ct);
            }

            if (options.IncludeLogs)
            {
                await CollectLogsAsync(tempDir, options.LogDays, manifest, ct);
            }

            if (options.IncludeStorageInfo)
            {
                await CollectStorageInfoAsync(tempDir, manifest, ct);
            }

            if (options.IncludeEnvironmentVariables)
            {
                await CollectEnvironmentVariablesAsync(tempDir, manifest, ct);
            }

            // Write manifest
            stopwatch.Stop();
            manifest.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "manifest.json"), manifestJson, ct);

            // Create ZIP archive (delete existing if present)
            File.Delete(zipPath); // No-op if file doesn't exist
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);

            var zipInfo = new FileInfo(zipPath);

            _log.Information(
                "Diagnostic bundle generation completed for {OperationName} with {CorrelationId} in {ElapsedMs} ms; files={FileCount}; sizeBytes={SizeBytes}",
                "diagnostic-bundle.generate",
                operationId,
                stopwatch.ElapsedMilliseconds,
                manifest.FilesCollected.Count,
                zipInfo.Length);

            return new DiagnosticBundleResult
            {
                Success = true,
                BundleId = bundleId,
                ZipPath = zipPath,
                SizeBytes = zipInfo.Length,
                FilesIncluded = manifest.FilesCollected,
                Message = $"Diagnostic bundle generated: {bundleId}"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log.Error(
                ex,
                "Diagnostic bundle generation failed for {OperationName} with {CorrelationId} after {ElapsedMs} ms",
                "diagnostic-bundle.generate",
                operationId,
                stopwatch.ElapsedMilliseconds);
            return new DiagnosticBundleResult
            {
                Success = false,
                BundleId = bundleId,
                Message = $"Failed to generate bundle: {ex.Message}"
            };
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { /* Best effort cleanup - directory may be in use */ }
        }
    }

    /// <summary>
    /// Reads a generated diagnostic bundle.
    /// </summary>
    public byte[] ReadBundle(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Diagnostic bundle not found", zipPath);

        return File.ReadAllBytes(zipPath);
    }

    private async Task CollectSystemInfoAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        var info = new StringBuilder();
        info.AppendLine("=== SYSTEM INFORMATION ===");
        info.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        info.AppendLine($"Machine: {Environment.MachineName}");
        info.AppendLine($"OS: {Environment.OSVersion}");
        info.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        info.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
        info.AppendLine($".NET Version: {Environment.Version}");
        info.AppendLine($"Processors: {Environment.ProcessorCount}");
        info.AppendLine($"Working Set: {Environment.WorkingSet / 1024 / 1024} MB");
        info.AppendLine($"System Page Size: {Environment.SystemPageSize}");
        info.AppendLine($"Current Directory: {RuntimeDiagnosticRedactor.SanitizeText(Environment.CurrentDirectory)}");
        info.AppendLine();

        // GC Info
        info.AppendLine("=== GARBAGE COLLECTION ===");
        info.AppendLine($"GC Heap Size: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
        info.AppendLine($"Gen0 Collections: {GC.CollectionCount(0)}");
        info.AppendLine($"Gen1 Collections: {GC.CollectionCount(1)}");
        info.AppendLine($"Gen2 Collections: {GC.CollectionCount(2)}");
        info.AppendLine();

        // Process Info
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        info.AppendLine("=== PROCESS INFO ===");
        info.AppendLine($"Process ID: {process.Id}");
        info.AppendLine($"Start Time: {process.StartTime:O}");
        info.AppendLine($"Total Processor Time: {process.TotalProcessorTime}");
        info.AppendLine($"Private Memory: {process.PrivateMemorySize64 / 1024 / 1024} MB");
        info.AppendLine($"Virtual Memory: {process.VirtualMemorySize64 / 1024 / 1024} MB");
        info.AppendLine($"Thread Count: {process.Threads.Count}");

        await File.WriteAllTextAsync(Path.Combine(tempDir, "system-info.txt"), info.ToString(), ct);
        manifest.FilesCollected.Add("system-info.txt");
    }

    private async Task CollectRuntimeSummaryAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        using var process = Process.GetCurrentProcess();
        var logsDir = Path.Combine(_dataRoot, "_logs");
        var recentLogCount = Directory.Exists(logsDir)
            ? Directory.GetFiles(logsDir, "*.log", SearchOption.TopDirectoryOnly).Length
            : 0;

        var metrics = TryCollectMetricsSnapshot();
        var recentErrors = TryCollectRecentErrors();
        var shutdownDiagnostics = TryCollectShutdownDiagnostics();
        var providerConnectionDiagnostics = TryCollectProviderConnectionDiagnostics();
        var metricsSummary = metrics is { } snapshot
            ? new
            {
                available = true,
                published = snapshot.Published,
                dropped = snapshot.Dropped,
                rejected = snapshot.Integrity,
                trades = snapshot.Trades,
                quotes = snapshot.Quotes,
                depthUpdates = snapshot.DepthUpdates,
                historicalBars = snapshot.HistoricalBars,
                dropRate = snapshot.DropRate,
                eventsPerSecond = snapshot.EventsPerSecond,
                tradesPerSecond = snapshot.TradesPerSecond,
                depthUpdatesPerSecond = snapshot.DepthUpdatesPerSecond,
                historicalBarsPerSecond = snapshot.HistoricalBarsPerSecond,
                averageLatencyUs = snapshot.AverageLatencyUs,
                minLatencyUs = snapshot.MinLatencyUs,
                maxLatencyUs = snapshot.MaxLatencyUs,
                latencySampleCount = snapshot.LatencySampleCount,
                gc0Collections = snapshot.Gc0Collections,
                gc1Collections = snapshot.Gc1Collections,
                gc2Collections = snapshot.Gc2Collections,
                memoryUsageMb = snapshot.MemoryUsageMb,
                heapSizeMb = snapshot.HeapSizeMb,
                timestamp = (DateTimeOffset?)snapshot.Timestamp
            }
            : new
            {
                available = false,
                published = 0L,
                dropped = 0L,
                rejected = 0L,
                trades = 0L,
                quotes = 0L,
                depthUpdates = 0L,
                historicalBars = 0L,
                dropRate = 0.0,
                eventsPerSecond = 0.0,
                tradesPerSecond = 0.0,
                depthUpdatesPerSecond = 0.0,
                historicalBarsPerSecond = 0.0,
                averageLatencyUs = 0.0,
                minLatencyUs = 0.0,
                maxLatencyUs = 0.0,
                latencySampleCount = 0L,
                gc0Collections = 0L,
                gc1Collections = 0L,
                gc2Collections = 0L,
                memoryUsageMb = 0.0,
                heapSizeMb = 0.0,
                timestamp = (DateTimeOffset?)null
            };
        var errorsSummary = recentErrors is { } errorResult
            ? new
            {
                available = true,
                totalErrors = errorResult.TotalErrors,
                queuedErrors = errorResult.QueuedErrors,
                queryTime = (DateTimeOffset?)errorResult.QueryTime,
                recentCount = errorResult.Errors.Count,
                mostRecent = SanitizeTrackedError(errorResult.Errors.OrderByDescending(static error => error.Timestamp).FirstOrDefault())
            }
            : new
            {
                available = false,
                totalErrors = 0,
                queuedErrors = 0,
                queryTime = (DateTimeOffset?)null,
                recentCount = 0,
                mostRecent = (object?)null
            };

        var summary = new
        {
            schemaVersion = "1.0",
            operationName = "diagnostic-bundle.generate",
            correlationId = manifest.CorrelationId,
            generatedAt = DateTimeOffset.UtcNow,
            componentName = nameof(DiagnosticBundleService),
            diagnostics = new
            {
                redactionPolicy = "secrets, tokens, account identifiers, auth headers, connection strings, and query credentials are redacted",
                logDirectory = RuntimeDiagnosticRedactor.SanitizeText(logsDir),
                logDirectoryExists = Directory.Exists(logsDir),
                recentLogFileCount = recentLogCount,
                dataRoot = RuntimeDiagnosticRedactor.SanitizeText(_dataRoot)
            },
            process = new
            {
                processId = Environment.ProcessId,
                startTimeUtc = TryGetProcessStartTimeUtc(process),
                workingSetMb = Environment.WorkingSet / 1024 / 1024,
                privateMemoryMb = process.PrivateMemorySize64 / 1024 / 1024,
                threadCount = process.Threads.Count,
                gcHeapMb = GC.GetTotalMemory(forceFullCollection: false) / 1024 / 1024,
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2)
            },
            metrics = metricsSummary,
            errors = errorsSummary,
            providerConnections = SanitizeProviderConnectionDiagnostics(providerConnectionDiagnostics),
            shutdown = SanitizeShutdownDiagnostics(shutdownDiagnostics)
        };

        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(Path.Combine(tempDir, "runtime-summary.json"), json, ct);
        manifest.FilesCollected.Add("runtime-summary.json");
    }

    private async Task CollectConfigurationAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        // Collect sanitized configuration
        if (_configProvider is not null)
        {
            try
            {
                var config = _configProvider();
                var sanitized = SanitizeConfig(config);
                var json = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(Path.Combine(tempDir, "config-sanitized.json"), json, ct);
                manifest.FilesCollected.Add("config-sanitized.json");
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(tempDir, "config-error.txt"),
                    $"Failed to collect config: {ex.Message}",
                    ct);
            }
        }

        // Collect sample config if exists
        const string sampleConfigPath = "config/appsettings.sample.json";
        if (File.Exists(sampleConfigPath))
        {
            var sampleConfig = await File.ReadAllTextAsync(sampleConfigPath, ct);
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "appsettings.sample.json"),
                RuntimeDiagnosticRedactor.SanitizeText(sampleConfig),
                ct);
            manifest.FilesCollected.Add("appsettings.sample.json");
        }
    }

    private async Task CollectMetricsAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        if (_metricsProvider == null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "metrics.txt"),
                "Metrics provider not available",
                ct);
            return;
        }

        var metrics = TryCollectMetricsSnapshot();
        if (metrics is null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "metrics.txt"),
                "Metrics snapshot unavailable",
                ct);
            manifest.FilesCollected.Add("metrics.txt");
            return;
        }

        var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(Path.Combine(tempDir, "metrics.json"), json, ct);
        manifest.FilesCollected.Add("metrics.json");
    }

    private async Task CollectTrackedErrorsAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        var recentErrors = TryCollectRecentErrors();
        if (recentErrors is null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "recent-errors.txt"),
                "Tracked error provider not available",
                ct);
            return;
        }

        var payload = new
        {
            schemaVersion = "1.0",
            operationName = "diagnostic-bundle.generate",
            componentName = nameof(DiagnosticBundleService),
            totalErrors = recentErrors.TotalErrors,
            queuedErrors = recentErrors.QueuedErrors,
            queryTime = recentErrors.QueryTime,
            message = RuntimeDiagnosticRedactor.SanitizeText(recentErrors.Message),
            errors = recentErrors.Errors
                .OrderByDescending(static error => error.Timestamp)
                .Take(10)
                .Select(SanitizeTrackedError)
                .ToArray()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(Path.Combine(tempDir, "recent-errors.json"), json, ct);
        manifest.FilesCollected.Add("recent-errors.json");
    }

    private async Task CollectLogsAsync(string tempDir, int days, DiagnosticManifest manifest, CancellationToken ct)
    {
        var logsDir = Path.Combine(_dataRoot, "_logs");
        if (!Directory.Exists(logsDir))
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "logs-info.txt"),
                $"Logs directory not found: {RuntimeDiagnosticRedactor.SanitizeText(logsDir)}",
                ct);
            return;
        }

        var bundleLogsDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(bundleLogsDir);

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var logFiles = Directory.GetFiles(logsDir, "*.log", SearchOption.TopDirectoryOnly)
            .Where(f => new FileInfo(f).LastWriteTimeUtc >= cutoff)
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .Take(10); // Limit to 10 most recent log files

        foreach (var logFile in logFiles)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(logFile);
            var destPath = Path.Combine(bundleLogsDir, fileName);

            try
            {
                // Copy and sanitize log file
                var content = await File.ReadAllTextAsync(logFile, ct);
                var sanitized = SanitizeLogContent(content);
                await File.WriteAllTextAsync(destPath, sanitized, ct);
                manifest.FilesCollected.Add($"logs/{fileName}");
            }
            catch (IOException ex)
            {
                // Log file might be locked
                await File.WriteAllTextAsync(
                    Path.Combine(bundleLogsDir, $"{fileName}.error"),
                    $"Could not read log file: {ex.Message}",
                    ct);
            }
        }
    }

    private async Task CollectStorageInfoAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        var info = new StringBuilder();
        info.AppendLine("=== STORAGE INFORMATION ===");
        info.AppendLine($"Data Root: {RuntimeDiagnosticRedactor.SanitizeText(_dataRoot)}");
        info.AppendLine($"Data Root Exists: {Directory.Exists(_dataRoot)}");
        info.AppendLine();

        if (Directory.Exists(_dataRoot))
        {
            // Directory structure
            info.AppendLine("=== DIRECTORY STRUCTURE ===");
            await ListDirectoryAsync(_dataRoot, info, "", 3, ct);
            info.AppendLine();

            // Disk space
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(_dataRoot) ?? "/");
                info.AppendLine("=== DISK SPACE ===");
                info.AppendLine($"Drive: {driveInfo.Name}");
                info.AppendLine($"Total Size: {driveInfo.TotalSize / 1024 / 1024 / 1024} GB");
                info.AppendLine($"Free Space: {driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024} GB");
                info.AppendLine($"Free Percent: {(double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100:F1}%");
            }
            catch (IOException) { /* Disk info not available on all systems */ }
        }

        await File.WriteAllTextAsync(Path.Combine(tempDir, "storage-info.txt"), info.ToString(), ct);
        manifest.FilesCollected.Add("storage-info.txt");
    }

    private async Task CollectEnvironmentVariablesAsync(string tempDir, DiagnosticManifest manifest, CancellationToken ct)
    {
        string[] relevantPrefixes =
        [
            "MDC_", "DOTNET_", "ASPNET", "ALPACA", "POLYGON", "TIINGO", "FINNHUB",
            "FRED", "NASDAQ", "ALPHAVANTAGE", "IB", "ROBINHOOD", "PATH"
        ];

        var envVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (Key: e.Key.ToString() ?? "", Value: e.Value?.ToString() ?? ""))
            .Where(e => relevantPrefixes.Any(p => e.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Select(e => $"{e.Key}={RuntimeDiagnosticRedactor.SanitizeEnvValue(e.Key, e.Value)}");

        var content = $"""
            === ENVIRONMENT VARIABLES (Meridian/DOTNET related) ===
            {string.Join(Environment.NewLine, envVars)}
            """;

        await File.WriteAllTextAsync(Path.Combine(tempDir, "environment.txt"), content, ct);
        manifest.FilesCollected.Add("environment.txt");
    }

    private static async Task ListDirectoryAsync(string path, StringBuilder sb, string indent, int maxDepth, CancellationToken ct = default)
    {
        if (maxDepth <= 0)
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = RuntimeDiagnosticRedactor.SanitizeText(Path.GetFileName(dir));
                if (dirName.StartsWith("."))
                    continue;

                sb.AppendLine($"{indent}[DIR] {dirName}/");

                await ListDirectoryAsync(dir, sb, indent + "  ", maxDepth - 1, ct);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = RuntimeDiagnosticRedactor.SanitizeText(Path.GetFileName(file));
                var fileInfo = new FileInfo(file);
                sb.AppendLine($"{indent}{fileName} ({FormatSize(fileInfo.Length)})");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { /* Access denied */ }
        catch (IOException) { /* Directory access error */ }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private static object SanitizeConfig(AppConfig config)
    {
        return new
        {
            dataRoot = RuntimeDiagnosticRedactor.SanitizeText(config.DataRoot),
            compress = config.Compress ?? false,
            dataSource = config.DataSource.ToString(),
            alpaca = config.Alpaca != null ? new
            {
                keyId = "[REDACTED]",
                secretKey = "[REDACTED]",
                feed = RuntimeDiagnosticRedactor.SanitizeText(config.Alpaca.Feed),
                useSandbox = config.Alpaca.UseSandbox
            } : null,
            polygon = config.Polygon != null ? new
            {
                apiKey = RedactedWhenPresent(config.Polygon.ApiKey),
                useDelayed = config.Polygon.UseDelayed,
                feed = RuntimeDiagnosticRedactor.SanitizeText(config.Polygon.Feed),
                subscribeTrades = config.Polygon.SubscribeTrades,
                subscribeQuotes = config.Polygon.SubscribeQuotes,
                subscribeAggregates = config.Polygon.SubscribeAggregates
            } : null,
            storage = config.Storage,
            symbolCount = config.Symbols?.Length ?? 0,
            backfillEnabled = config.Backfill?.Enabled ?? false,
            backfillProvider = RuntimeDiagnosticRedactor.SanitizeText(config.Backfill?.Provider),
            backfillProviderCount = CountConfiguredBackfillProviders(config.Backfill?.Providers),
            dataSources = SanitizeDataSources(config.DataSources),
            providerRegistry = config.ProviderRegistry,
            providerConnections = SanitizeProviderConnections(config.ProviderConnections)
        };
    }

    private static object? SanitizeDataSources(DataSourcesConfig? dataSources)
    {
        if (dataSources is null)
            return null;

        return new
        {
            sourceCount = dataSources.Sources?.Length ?? 0,
            defaultRealTimeSourceId = RuntimeDiagnosticRedactor.SanitizeText(dataSources.DefaultRealTimeSourceId),
            defaultHistoricalSourceId = RuntimeDiagnosticRedactor.SanitizeText(dataSources.DefaultHistoricalSourceId),
            enableFailover = dataSources.EnableFailover,
            failoverTimeoutSeconds = dataSources.FailoverTimeoutSeconds,
            healthCheckIntervalSeconds = dataSources.HealthCheckIntervalSeconds,
            autoRecover = dataSources.AutoRecover,
            sources = dataSources.Sources?.Select(source => new
            {
                id = RuntimeDiagnosticRedactor.SanitizeText(source.Id),
                name = RuntimeDiagnosticRedactor.SanitizeText(source.Name),
                provider = source.Provider.ToString(),
                enabled = source.Enabled,
                type = source.Type.ToString(),
                priority = source.Priority,
                symbolCount = source.Symbols?.Length ?? 0,
                hasAlpacaCredentials = HasValue(source.Alpaca?.KeyId) || HasValue(source.Alpaca?.SecretKey),
                hasPolygonApiKey = HasValue(source.Polygon?.ApiKey),
                ibConfigured = source.IB is not null
            }).ToArray()
        };
    }

    private static object? SanitizeProviderConnections(ProviderConnectionsConfig? providerConnections)
    {
        if (providerConnections is null)
            return null;

        return new
        {
            connectionCount = providerConnections.Connections?.Length ?? 0,
            bindingCount = providerConnections.Bindings?.Length ?? 0,
            policyCount = providerConnections.Policies?.Length ?? 0,
            presetCount = providerConnections.Presets?.Length ?? 0,
            certificationCount = providerConnections.Certifications?.Length ?? 0,
            connections = providerConnections.Connections?.Select(connection => new
            {
                connectionId = RuntimeDiagnosticRedactor.SanitizeText(connection.ConnectionId),
                providerFamilyId = RuntimeDiagnosticRedactor.SanitizeText(connection.ProviderFamilyId),
                displayName = RuntimeDiagnosticRedactor.SanitizeText(connection.DisplayName),
                connectionType = connection.ConnectionType.ToString(),
                connectionMode = connection.ConnectionMode.ToString(),
                enabled = connection.Enabled,
                hasCredentialReference = HasValue(connection.CredentialReference),
                institutionId = RuntimeDiagnosticRedactor.SanitizeText(connection.InstitutionId),
                externalAccountId = RedactedWhenPresent(connection.ExternalAccountId),
                hasScope = connection.Scope is not null && !connection.Scope.IsGlobal,
                tagCount = connection.Tags?.Length ?? 0,
                productionReady = connection.ProductionReady
            }).ToArray()
        };
    }

    private static string? RedactedWhenPresent(string? value) =>
        HasValue(value) ? "[REDACTED]" : null;

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static int CountConfiguredBackfillProviders(BackfillProvidersConfig? providers)
    {
        if (providers is null)
            return 0;

        var count = 0;
        if (providers.Alpaca is not null) count++;
        if (providers.Yahoo is not null) count++;
        if (providers.Nasdaq is not null) count++;
        if (providers.Stooq is not null) count++;
        if (providers.OpenFigi is not null) count++;
        if (providers.Tiingo is not null) count++;
        if (providers.Polygon is not null) count++;
        if (providers.AlphaVantage is not null) count++;
        if (providers.Finnhub is not null) count++;
        if (providers.Fred is not null) count++;
        if (providers.Synthetic is not null) count++;
        if (providers.Robinhood is not null) count++;
        return count;
    }

    private static string SanitizeLogContent(string content) => RuntimeDiagnosticRedactor.SanitizeText(content);

    private MetricsSnapshot? TryCollectMetricsSnapshot()
    {
        if (_metricsProvider is null)
            return null;

        try
        {
            return _metricsProvider();
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to collect metrics snapshot for {OperationName}; continuing diagnostic bundle generation without runtime metrics",
                "diagnostic-bundle.generate");
            return null;
        }
    }

    private ErrorQueryResult? TryCollectRecentErrors()
    {
        if (_recentErrorsProvider is null)
            return null;

        try
        {
            return _recentErrorsProvider();
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to collect tracked errors for {OperationName}; continuing diagnostic bundle generation without recent errors",
                "diagnostic-bundle.generate");
            return null;
        }
    }

    private ShutdownSequenceDiagnosticSnapshot? TryCollectShutdownDiagnostics()
    {
        if (_shutdownDiagnosticsProvider is null)
            return null;

        try
        {
            return _shutdownDiagnosticsProvider();
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to collect shutdown diagnostics for {OperationName}; continuing diagnostic bundle generation without shutdown status",
                "diagnostic-bundle.generate");
            return null;
        }
    }

    private IReadOnlyCollection<ProviderConnectionDiagnosticSnapshot>? TryCollectProviderConnectionDiagnostics()
    {
        if (_providerConnectionDiagnosticsProvider is null)
            return null;

        try
        {
            return _providerConnectionDiagnosticsProvider();
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to collect provider connection diagnostics for {OperationName}; continuing diagnostic bundle generation without provider connection status",
                "diagnostic-bundle.generate");
            return null;
        }
    }

    private static object? SanitizeTrackedError(TrackedError? error)
    {
        if (error is null)
            return null;

        return new
        {
            id = RuntimeDiagnosticRedactor.SanitizeText(error.Id),
            timestamp = error.Timestamp,
            level = RuntimeDiagnosticRedactor.SanitizeText(error.Level),
            message = RuntimeDiagnosticRedactor.SanitizeText(error.Message),
            exceptionType = RuntimeDiagnosticRedactor.SanitizeText(error.ExceptionType),
            stackTrace = RuntimeDiagnosticRedactor.SanitizeText(error.StackTrace),
            context = RuntimeDiagnosticRedactor.SanitizeText(error.Context),
            innerException = RuntimeDiagnosticRedactor.SanitizeText(error.InnerException)
        };
    }

    private static object SanitizeShutdownDiagnostics(ShutdownSequenceDiagnosticSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new
            {
                available = false,
                operationName = "runtime.shutdown.sequence",
                status = "Unavailable"
            };
        }

        return new
        {
            available = snapshot.Available,
            operationName = RuntimeDiagnosticRedactor.SanitizeText(snapshot.OperationName),
            status = RuntimeDiagnosticRedactor.SanitizeText(snapshot.Status),
            correlationId = RuntimeDiagnosticRedactor.SanitizeText(snapshot.CorrelationId),
            reason = RuntimeDiagnosticRedactor.SanitizeText(snapshot.Reason),
            startedAtUtc = snapshot.StartedAtUtc,
            completedAtUtc = snapshot.CompletedAtUtc,
            durationMs = snapshot.DurationMs,
            flushTimeoutOccurred = snapshot.FlushTimeoutOccurred,
            incompleteFlushCount = snapshot.IncompleteFlushCount,
            warningCount = snapshot.WarningCount,
            warningSummary = snapshot.WarningSummary.Select(RuntimeDiagnosticRedactor.SanitizeText).ToArray(),
            flushableComponentCount = snapshot.FlushableComponentCount,
            disposableComponentCount = snapshot.DisposableComponentCount,
            callbackCount = snapshot.CallbackCount,
            duplicateRequestCount = snapshot.DuplicateRequestCount,
            lastDuplicateRequestAtUtc = snapshot.LastDuplicateRequestAtUtc,
            lastUpdatedAtUtc = snapshot.LastUpdatedAtUtc
        };
    }

    private static object SanitizeProviderConnectionDiagnostics(IReadOnlyCollection<ProviderConnectionDiagnosticSnapshot>? snapshots)
    {
        if (snapshots is null)
        {
            return new
            {
                available = false,
                count = 0,
                providers = Array.Empty<object>()
            };
        }

        return new
        {
            available = true,
            count = snapshots.Count,
            providers = snapshots
                .OrderBy(static snapshot => snapshot.ProviderName, StringComparer.OrdinalIgnoreCase)
                .Select(static snapshot => new
                {
                    providerName = RuntimeDiagnosticRedactor.SanitizeText(snapshot.ProviderName),
                    lifecycleState = RuntimeDiagnosticRedactor.SanitizeText(snapshot.LifecycleState),
                    webSocketState = RuntimeDiagnosticRedactor.SanitizeText(snapshot.WebSocketState),
                    isConnected = snapshot.IsConnected,
                    isReconnecting = snapshot.IsReconnecting,
                    reconnectAttempts = snapshot.ReconnectAttempts,
                    lastConnectedAt = snapshot.LastConnectedAt,
                    lastDisconnectedAt = snapshot.LastDisconnectedAt,
                    lastHeartbeatReceivedAt = snapshot.LastHeartbeatReceivedAt,
                    lastMessageReceivedAt = snapshot.LastMessageReceivedAt,
                    lastReconnectAttemptAt = snapshot.LastReconnectAttemptAt,
                    lastFailureKind = RuntimeDiagnosticRedactor.SanitizeText(snapshot.LastFailureKind),
                    connectionAge = snapshot.ConnectionAge,
                    idleDuration = snapshot.IdleDuration,
                    activeSubscriptions = snapshot.ActiveSubscriptions,
                    failedSubscriptions = snapshot.FailedSubscriptions,
                    recoveringSubscriptions = snapshot.RecoveringSubscriptions,
                    lastSubscriptionMessageAt = snapshot.LastSubscriptionMessageAt
                })
                .ToArray()
        };
    }

    private static DateTimeOffset? TryGetProcessStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

}

/// <summary>
/// Options for diagnostic bundle generation.
/// </summary>
public sealed record DiagnosticBundleOptions(
    bool IncludeRuntimeSummary = true,
    bool IncludeSystemInfo = true,
    bool IncludeConfiguration = true,
    bool IncludeMetrics = true,
    bool IncludeTrackedErrors = true,
    bool IncludeLogs = true,
    bool IncludeStorageInfo = true,
    bool IncludeEnvironmentVariables = true,
    int LogDays = 3
);

/// <summary>
/// Result of diagnostic bundle generation.
/// </summary>
public sealed class DiagnosticBundleResult
{
    public bool Success { get; set; }
    public string? BundleId { get; set; }
    public string? ZipPath { get; set; }
    public long SizeBytes { get; set; }
    public List<string> FilesIncluded { get; set; } = [];
    public string? Message { get; set; }
}

public sealed record ProviderConnectionDiagnosticSnapshot(
    string ProviderName,
    string LifecycleState,
    string WebSocketState,
    bool IsConnected,
    bool IsReconnecting,
    int ReconnectAttempts,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastHeartbeatReceivedAt,
    DateTimeOffset? LastMessageReceivedAt,
    DateTimeOffset? LastReconnectAttemptAt,
    string? LastFailureKind,
    TimeSpan? ConnectionAge,
    TimeSpan? IdleDuration,
    int ActiveSubscriptions = 0,
    int FailedSubscriptions = 0,
    int RecoveringSubscriptions = 0,
    DateTimeOffset? LastSubscriptionMessageAt = null);

/// <summary>
/// Manifest included in diagnostic bundle.
/// </summary>
internal sealed class DiagnosticManifest
{
    public string? BundleId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public double DurationMs { get; set; }
    public string? MachineName { get; set; }
    public string? OsVersion { get; set; }
    public string? DotNetVersion { get; set; }
    public DiagnosticBundleOptions? Options { get; set; }
    public List<string> FilesCollected { get; set; } = [];
}
