using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for generating diagnostic bundles containing system state and logs.
/// Implements QW-16: Diagnostic Bundle Generator.
/// </summary>
public sealed class DiagnosticBundleService
{
    private readonly ILogger _log = LoggingSetup.ForContext<DiagnosticBundleService>();
    private readonly string _dataRoot;
    private readonly Func<MetricsSnapshot>? _metricsProvider;
    private readonly Func<AppConfig>? _configProvider;

    private static readonly string[] SensitiveKeys =
    [
        "password", "secret", "key", "token", "apikey", "api_key",
        "connectionstring", "credential", "auth"
    ];

    public DiagnosticBundleService(
        string dataRoot,
        Func<MetricsSnapshot>? metricsProvider = null,
        Func<AppConfig>? configProvider = null)
    {
        _dataRoot = dataRoot;
        _metricsProvider = metricsProvider;
        _configProvider = configProvider;
    }

    /// <summary>
    /// Generates a diagnostic bundle as a ZIP file.
    /// </summary>
    public async Task<DiagnosticBundleResult> GenerateAsync(
        DiagnosticBundleOptions options,
        CancellationToken ct = default)
    {
        var bundleId = $"diag_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}".Substring(0, 40);
        var tempDir = Path.Combine(Path.GetTempPath(), bundleId);
        var zipPath = Path.Combine(Path.GetTempPath(), $"{bundleId}.zip");

        try
        {
            Directory.CreateDirectory(tempDir);

            var manifest = new DiagnosticManifest
            {
                BundleId = bundleId,
                GeneratedAt = DateTimeOffset.UtcNow,
                MachineName = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                Options = options
            };

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
            _log.Error(ex, "Failed to generate diagnostic bundle");
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
        info.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
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
            File.Copy(sampleConfigPath, Path.Combine(tempDir, "appsettings.sample.json"));
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

        var metrics = _metricsProvider();
        var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(Path.Combine(tempDir, "metrics.json"), json, ct);
        manifest.FilesCollected.Add("metrics.json");
    }

    private async Task CollectLogsAsync(string tempDir, int days, DiagnosticManifest manifest, CancellationToken ct)
    {
        var logsDir = Path.Combine(_dataRoot, "_logs");
        if (!Directory.Exists(logsDir))
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "logs-info.txt"),
                $"Logs directory not found: {logsDir}",
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
        info.AppendLine($"Data Root: {_dataRoot}");
        info.AppendLine($"Data Root Exists: {Directory.Exists(_dataRoot)}");
        info.AppendLine();

        if (Directory.Exists(_dataRoot))
        {
            // Directory structure
            info.AppendLine("=== DIRECTORY STRUCTURE ===");
            await ListDirectoryAsync(_dataRoot, info, "", 3);
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
        string[] relevantPrefixes = ["MDC_", "DOTNET_", "ASPNET", "ALPACA", "POLYGON", "PATH"];

        var envVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (Key: e.Key.ToString() ?? "", Value: e.Value?.ToString() ?? ""))
            .Where(e => relevantPrefixes.Any(p => e.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Select(e => $"{e.Key}={SanitizeEnvValue(e.Key, e.Value)}");

        var content = $"""
            === ENVIRONMENT VARIABLES (Meridian/DOTNET related) ===
            {string.Join(Environment.NewLine, envVars)}
            """;

        await File.WriteAllTextAsync(Path.Combine(tempDir, "environment.txt"), content, ct);
        manifest.FilesCollected.Add("environment.txt");
    }

    private static string SanitizeEnvValue(string key, string value) =>
        IsSensitiveKey(key) ? "[REDACTED]" :
        key.Equals("PATH", StringComparison.OrdinalIgnoreCase) ? "[PATH variable - omitted for brevity]" :
        value;

    private static async Task ListDirectoryAsync(string path, StringBuilder sb, string indent, int maxDepth, CancellationToken ct = default)
    {
        if (maxDepth <= 0)
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("."))
                    continue;

                sb.AppendLine($"{indent}[DIR] {dirName}/");

                await ListDirectoryAsync(dir, sb, indent + "  ", maxDepth - 1);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                var fileName = Path.GetFileName(file);
                var fileInfo = new FileInfo(file);
                sb.AppendLine($"{indent}{fileName} ({FormatSize(fileInfo.Length)})");
            }
        }
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
            dataRoot = config.DataRoot,
            compress = config.Compress ?? false,
            dataSource = config.DataSource.ToString(),
            alpaca = config.Alpaca != null ? new
            {
                keyId = "[REDACTED]",
                secretKey = "[REDACTED]",
                feed = config.Alpaca.Feed,
                useSandbox = config.Alpaca.UseSandbox
            } : null,
            storage = config.Storage,
            symbolCount = config.Symbols?.Length ?? 0,
            backfillEnabled = config.Backfill?.Enabled ?? false
        };
    }

    private static string SanitizeLogContent(string content)
    {
        // Remove potential sensitive data patterns
        var patterns = new[]
        {
            (@"(password|secret|key|token|apikey)[\s:=]+[^\s\]]+", "$1=[REDACTED]"),
            (@"Bearer\s+[A-Za-z0-9\-_]+", "Bearer [REDACTED]"),
            (@"Basic\s+[A-Za-z0-9+/=]+", "Basic [REDACTED]")
        };

        var result = content;
        foreach (var (pattern, replacement) in patterns)
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result, pattern, replacement,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static bool IsSensitiveKey(string key)
    {
        return SensitiveKeys.Any(s =>
            key.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

/// <summary>
/// Options for diagnostic bundle generation.
/// </summary>
public sealed record DiagnosticBundleOptions(
    bool IncludeSystemInfo = true,
    bool IncludeConfiguration = true,
    bool IncludeMetrics = true,
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

/// <summary>
/// Manifest included in diagnostic bundle.
/// </summary>
internal sealed class DiagnosticManifest
{
    public string? BundleId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public string? MachineName { get; set; }
    public string? OsVersion { get; set; }
    public string? DotNetVersion { get; set; }
    public DiagnosticBundleOptions? Options { get; set; }
    public List<string> FilesCollected { get; set; } = [];
}
