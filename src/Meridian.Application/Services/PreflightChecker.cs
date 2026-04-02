using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Http;
using Meridian.Storage.Services;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Performs comprehensive pre-flight checks before starting data collection.
/// Verifies disk space, network connectivity, file permissions, and system resources
/// to catch issues early and provide actionable error messages.
/// </summary>
public sealed class PreflightChecker
{
    private readonly ILogger _log = LoggingSetup.ForContext<PreflightChecker>();
    private readonly PreflightConfig _config;

    public PreflightChecker(PreflightConfig? config = null)
    {
        _config = config ?? PreflightConfig.Default;
    }

    /// <summary>
    /// Runs all pre-flight checks and returns a comprehensive result.
    /// </summary>
    /// <param name="dataRoot">The data directory path to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="activeDataSource">Active data source to validate credentials for.</param>
    /// <returns>Pre-flight check results with pass/fail status and details.</returns>
    public async Task<PreflightResult> RunChecksAsync(string dataRoot, CancellationToken ct = default, DataSourceKind? activeDataSource = null)
    {
        return await RunChecksAsync(dataRoot, activeDataSource: activeDataSource?.ToString(), ct);
    }

    /// <summary>
    /// Runs all pre-flight checks including provider credential validation.
    /// </summary>
    public async Task<PreflightResult> RunChecksAsync(string dataRoot, string? activeDataSource, CancellationToken ct = default)
    {
        var startTime = Stopwatch.GetTimestamp();
        var checks = new List<PreflightCheckResult>();

        _log.Information("Starting pre-flight checks...");

        // Run all checks
        checks.Add(CheckDiskSpace(dataRoot));
        checks.Add(CheckFilePermissions(dataRoot));

        // Run network connectivity check if configured
        if (_config.CheckNetworkConnectivity)
        {
            checks.Add(await CheckNetworkConnectivityAsync(ct));
        }

        checks.Add(CheckMemoryAvailability());
        checks.Add(CheckSystemTime());
        checks.Add(CheckEnvironmentVariables());

        // Validate provider credentials if active data source is known
        if (!string.IsNullOrEmpty(activeDataSource))
        {
            checks.Add(ValidateProviderCredentials(activeDataSource));
        }

        // Run provider-specific checks if configured
        if (_config.CheckProviderConnectivity)
        {
            checks.Add(await CheckProviderEndpointsAsync(ct));
        }

        var elapsed = GetElapsedMs(startTime);
        var allPassed = checks.All(c => c.Status != PreflightCheckStatus.Failed);
        var hasWarnings = checks.Any(c => c.Status == PreflightCheckStatus.Warning);
        var failedCount = checks.Count(c => c.Status == PreflightCheckStatus.Failed);
        var warningCount = checks.Count(c => c.Status == PreflightCheckStatus.Warning);

        var result = new PreflightResult(
            AllChecksPassed: allPassed,
            HasWarnings: hasWarnings,
            Checks: checks.ToArray(),
            TotalDurationMs: elapsed,
            CheckedAt: DateTimeOffset.UtcNow
        );

        if (allPassed && !hasWarnings)
        {
            _log.Information("All pre-flight checks passed in {ElapsedMs}ms", elapsed);
        }
        else if (allPassed)
        {
            _log.Warning("Pre-flight checks completed with {WarningCount} warning(s) in {ElapsedMs}ms",
                warningCount, elapsed);
        }
        else
        {
            _log.Error("Pre-flight checks failed: {FailedCount} failure(s), {WarningCount} warning(s) in {ElapsedMs}ms",
                failedCount, warningCount, elapsed);
        }

        return result;
    }

    /// <summary>
    /// Runs checks and throws if any critical check fails.
    /// </summary>
    public async Task EnsureReadyAsync(string dataRoot, CancellationToken ct = default, DataSourceKind? activeDataSource = null)
    {
        var result = await RunChecksAsync(dataRoot, ct, activeDataSource);

        if (!result.AllChecksPassed)
        {
            var failedChecks = result.Checks
                .Where(c => c.Status == PreflightCheckStatus.Failed)
                .Select(c => $"  - {c.Name}: {c.Message}")
                .ToList();

            var errorMessage = $"Pre-flight checks failed:\n{string.Join("\n", failedChecks)}";

            _log.Error("Pre-flight validation failed. The application cannot start safely.");
            foreach (var check in result.Checks.Where(c => c.Status == PreflightCheckStatus.Failed))
            {
                _log.Error("[FAILED] {CheckName}: {Message}", check.Name, check.Message);
                if (check.Remediation != null)
                {
                    _log.Information("  Remediation: {Remediation}", check.Remediation);
                }
            }

            throw new PreflightException(errorMessage, result);
        }

        if (result.HasWarnings)
        {
            foreach (var check in result.Checks.Where(c => c.Status == PreflightCheckStatus.Warning))
            {
                _log.Warning("[WARNING] {CheckName}: {Message}", check.Name, check.Message);
            }
        }
    }

    private PreflightCheckResult CheckDiskSpace(string dataRoot)
    {
        const string checkName = "Disk Space";

        try
        {
            var fullPath = Path.GetFullPath(dataRoot);
            var pathRoot = Path.GetPathRoot(fullPath);

            if (string.IsNullOrEmpty(pathRoot))
            {
                return PreflightCheckResult.Failed(checkName,
                    $"Could not determine root path for '{dataRoot}'",
                    "Ensure the data directory path is valid and accessible.");
            }

            var driveInfo = new DriveInfo(pathRoot);

            if (!driveInfo.IsReady)
            {
                return PreflightCheckResult.Failed(checkName,
                    $"Drive '{driveInfo.Name}' is not ready",
                    "Ensure the drive is mounted and accessible.");
            }

            var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            var totalGb = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
            var usedPercent = ((totalGb - freeGb) / totalGb) * 100;

            var details = new Dictionary<string, object>
            {
                ["drive"] = driveInfo.Name,
                ["freeGb"] = Math.Round(freeGb, 2),
                ["totalGb"] = Math.Round(totalGb, 2),
                ["usedPercent"] = Math.Round(usedPercent, 1)
            };

            if (freeGb < _config.MinDiskSpaceGb)
            {
                return PreflightCheckResult.Failed(checkName,
                    $"Insufficient disk space: {freeGb:F1} GB free (minimum: {_config.MinDiskSpaceGb} GB)",
                    $"Free up disk space on {driveInfo.Name} or change the data directory.",
                    details);
            }

            if (freeGb < _config.WarnDiskSpaceGb)
            {
                return PreflightCheckResult.Warning(checkName,
                    $"Low disk space: {freeGb:F1} GB free (warning threshold: {_config.WarnDiskSpaceGb} GB)",
                    $"Consider freeing up disk space on {driveInfo.Name}.",
                    details);
            }

            return PreflightCheckResult.Passed(checkName,
                $"Disk space OK: {freeGb:F1} GB free ({100 - usedPercent:F1}% available)",
                details);
        }
        catch (Exception ex)
        {
            return PreflightCheckResult.Failed(checkName,
                $"Failed to check disk space: {ex.Message}",
                "Ensure the data directory path is valid and the application has read access.");
        }
    }

    private PreflightCheckResult CheckFilePermissions(string dataRoot)
    {
        const string checkName = "File Permissions";

        try
        {
            var fullPath = Path.GetFullPath(dataRoot);

            // Create directory if it doesn't exist
            if (!Directory.Exists(fullPath))
            {
                try
                {
                    Directory.CreateDirectory(fullPath);
                    _log.Debug("Created data directory: {Path}", fullPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return PreflightCheckResult.Failed(checkName,
                        $"Cannot create data directory: '{fullPath}' (access denied)",
                        "Run with appropriate permissions or choose a writable directory.");
                }
                catch (Exception ex)
                {
                    return PreflightCheckResult.Failed(checkName,
                        $"Cannot create data directory: {ex.Message}",
                        "Ensure the parent directory exists and is writable.");
                }
            }

            // Test write access with a temporary file
            var testFile = Path.Combine(fullPath, $".preflight_test_{Guid.NewGuid():N}");
            try
            {
                var testContent = $"Preflight check at {DateTimeOffset.UtcNow:O}";
                File.WriteAllText(testFile, testContent);

                // Verify read back
                var readContent = File.ReadAllText(testFile);
                if (readContent != testContent)
                {
                    return PreflightCheckResult.Failed(checkName,
                        "File read/write verification failed (content mismatch)",
                        "Check for disk corruption or filesystem issues.");
                }

                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                return PreflightCheckResult.Failed(checkName,
                    $"Cannot write to data directory: '{fullPath}' (access denied)",
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "Run as administrator or adjust folder permissions."
                        : $"Run 'chmod 755 {fullPath}' or change ownership with 'chown'.");
            }
            catch (IOException ex)
            {
                return PreflightCheckResult.Failed(checkName,
                    $"I/O error writing to data directory: {ex.Message}",
                    "Check disk health and ensure sufficient space.");
            }
            finally
            {
                // Clean up test file if it exists
                if (File.Exists(testFile))
                {
                    try
                    { File.Delete(testFile); }
                    catch (IOException) { }
                }
            }

            var details = new Dictionary<string, object>
            {
                ["path"] = fullPath,
                ["canRead"] = true,
                ["canWrite"] = true
            };

            return PreflightCheckResult.Passed(checkName,
                $"File permissions OK: '{fullPath}' is readable and writable",
                details);
        }
        catch (Exception ex)
        {
            return PreflightCheckResult.Failed(checkName,
                $"Unexpected error checking permissions: {ex.Message}",
                "Review the error message and check system logs.");
        }
    }

    private async Task<PreflightCheckResult> CheckNetworkConnectivityAsync(CancellationToken ct)
    {
        const string checkName = "Network Connectivity";

        var endpoints = new[]
        {
            ("dns", "8.8.8.8", 53),
            ("https", "api.alpaca.markets", 443),
            ("https", "stream.data.alpaca.markets", 443)
        };

        var results = new List<(string name, bool success, string? error)>();

        foreach (var (protocol, host, port) in endpoints)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(_config.NetworkTimeoutMs, ct);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    results.Add(($"{host}:{port}", false, "Connection timeout"));
                }
                else if (connectTask.IsFaulted)
                {
                    results.Add(($"{host}:{port}", false, connectTask.Exception?.InnerException?.Message ?? "Connection failed"));
                }
                else
                {
                    results.Add(($"{host}:{port}", true, null));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                results.Add(($"{host}:{port}", false, ex.Message));
            }
        }

        var successCount = results.Count(r => r.success);
        var failedEndpoints = results.Where(r => !r.success).ToList();

        var details = new Dictionary<string, object>
        {
            ["successCount"] = successCount,
            ["totalCount"] = results.Count,
            ["results"] = results.Select(r => new { r.name, r.success, r.error }).ToArray()
        };

        if (successCount == 0)
        {
            return PreflightCheckResult.Failed(checkName,
                "No network connectivity - all endpoint checks failed",
                "Check your internet connection, firewall settings, and DNS configuration.",
                details);
        }

        if (failedEndpoints.Any())
        {
            var failedNames = string.Join(", ", failedEndpoints.Select(f => f.name));
            return PreflightCheckResult.Warning(checkName,
                $"Partial network connectivity: {successCount}/{results.Count} endpoints reachable (failed: {failedNames})",
                "Some data providers may not be accessible. Check firewall and network settings.",
                details);
        }

        return PreflightCheckResult.Passed(checkName,
            $"Network connectivity OK: {successCount}/{results.Count} endpoints reachable",
            details);
    }

    private PreflightCheckResult CheckMemoryAvailability()
    {
        const string checkName = "Memory Availability";

        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var totalAvailableMb = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
            var heapSizeMb = gcInfo.HeapSizeBytes / (1024.0 * 1024.0);

            var process = Process.GetCurrentProcess();
            var workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);

            var details = new Dictionary<string, object>
            {
                ["totalAvailableMb"] = Math.Round(totalAvailableMb, 2),
                ["heapSizeMb"] = Math.Round(heapSizeMb, 2),
                ["workingSetMb"] = Math.Round(workingSetMb, 2)
            };

            if (totalAvailableMb < _config.MinMemoryMb)
            {
                return PreflightCheckResult.Failed(checkName,
                    $"Insufficient memory: {totalAvailableMb:F0} MB available (minimum: {_config.MinMemoryMb} MB)",
                    "Close other applications or increase system memory.",
                    details);
            }

            if (totalAvailableMb < _config.WarnMemoryMb)
            {
                return PreflightCheckResult.Warning(checkName,
                    $"Low memory: {totalAvailableMb:F0} MB available (warning threshold: {_config.WarnMemoryMb} MB)",
                    "Consider closing unused applications.",
                    details);
            }

            return PreflightCheckResult.Passed(checkName,
                $"Memory OK: {totalAvailableMb:F0} MB available, current usage {workingSetMb:F0} MB",
                details);
        }
        catch (Exception ex)
        {
            return PreflightCheckResult.Warning(checkName,
                $"Could not determine memory availability: {ex.Message}",
                "Memory check skipped. Monitor memory usage during operation.");
        }
    }

    private PreflightCheckResult CheckSystemTime()
    {
        const string checkName = "System Time";

        try
        {
            var localTime = DateTimeOffset.Now;
            var utcTime = DateTimeOffset.UtcNow;
            var offset = localTime.Offset;

            // Check if time seems reasonable (within 24 hours of a reference)
            var referenceYear = 2020;
            if (utcTime.Year < referenceYear)
            {
                return PreflightCheckResult.Failed(checkName,
                    $"System time appears invalid: {utcTime:O} (year < {referenceYear})",
                    "Synchronize system time using NTP or manual configuration.");
            }

            var details = new Dictionary<string, object>
            {
                ["utcTime"] = utcTime.ToString("O"),
                ["localTime"] = localTime.ToString("O"),
                ["offset"] = offset.ToString()
            };

            // Check for unreasonable timezone offset (more than 14 hours from UTC)
            if (Math.Abs(offset.TotalHours) > 14)
            {
                return PreflightCheckResult.Warning(checkName,
                    $"Unusual timezone offset: {offset}",
                    "Verify system timezone configuration.",
                    details);
            }

            return PreflightCheckResult.Passed(checkName,
                $"System time OK: {utcTime:yyyy-MM-dd HH:mm:ss} UTC (offset: {offset})",
                details);
        }
        catch (Exception ex)
        {
            return PreflightCheckResult.Warning(checkName,
                $"Could not verify system time: {ex.Message}",
                "Ensure system time is synchronized.");
        }
    }

    private PreflightCheckResult CheckEnvironmentVariables()
    {
        const string checkName = "Environment Variables";

        var requiredVars = new[] { "PATH" };
        var optionalVars = new[]
        {
            "ALPACA_KEY_ID",
            "ALPACA_SECRET_KEY",
            "POLYGON_API_KEY",
            "FINNHUB_API_KEY"
        };

        var missingRequired = new List<string>();
        var missingOptional = new List<string>();
        var foundOptional = new List<string>();

        foreach (var varName in requiredVars)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
            {
                missingRequired.Add(varName);
            }
        }

        foreach (var varName in optionalVars)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
            {
                missingOptional.Add(varName);
            }
            else
            {
                foundOptional.Add(varName);
            }
        }

        var details = new Dictionary<string, object>
        {
            ["missingRequired"] = missingRequired,
            ["missingOptional"] = missingOptional,
            ["foundOptional"] = foundOptional
        };

        if (missingRequired.Any())
        {
            return PreflightCheckResult.Failed(checkName,
                $"Missing required environment variables: {string.Join(", ", missingRequired)}",
                "Set the required environment variables before starting.",
                details);
        }

        if (foundOptional.Any())
        {
            return PreflightCheckResult.Passed(checkName,
                $"Environment OK: Found API credentials for {string.Join(", ", foundOptional)}",
                details);
        }

        return PreflightCheckResult.Warning(checkName,
            "No API credentials found in environment variables",
            "Set ALPACA_KEY_ID/ALPACA_SECRET_KEY or configure credentials in appsettings.json.",
            details);
    }

    /// <summary>
    /// Validates that all enabled providers have their required credentials configured.
    /// Returns a detailed table of missing credentials with the exact env var names to set.
    /// </summary>
    public PreflightCheckResult ValidateProviderCredentials(string activeDataSource)
    {
        const string checkName = "Provider Credentials";

        var providerCredentialMap = new Dictionary<string, ProviderCredentialRequirement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpaca"] = new("Alpaca Markets", new[]
            {
                new CredentialRequirement("ALPACA__KEYID", new[] { "ALPACA_KEY_ID", "MDC_ALPACA_KEY_ID" }),
                new CredentialRequirement("ALPACA__SECRETKEY", new[] { "ALPACA_SECRET_KEY", "MDC_ALPACA_SECRET_KEY" })
            }, "docs/providers/alpaca-setup.md"),
            ["Polygon"] = new("Polygon.io", new[]
            {
                new CredentialRequirement("POLYGON__APIKEY", new[] { "POLYGON_API_KEY", "MDC_POLYGON_API_KEY" })
            }, "docs/providers/data-sources.md"),
            ["IB"] = new("Interactive Brokers", Array.Empty<CredentialRequirement>(),
                "docs/providers/interactive-brokers-setup.md"),
            ["NYSE"] = new("NYSE Direct", new[]
            {
                new CredentialRequirement("NYSE__APIKEY", new[] { "NYSE_API_KEY", "MDC_NYSE_API_KEY" })
            }, null),
            ["Tiingo"] = new("Tiingo", new[]
            {
                new CredentialRequirement("TIINGO__TOKEN", new[] { "TIINGO_API_TOKEN", "TIINGO_TOKEN", "MDC_TIINGO_TOKEN" })
            }, null),
            ["Finnhub"] = new("Finnhub", new[]
            {
                new CredentialRequirement("FINNHUB__TOKEN", new[] { "FINNHUB_API_KEY", "MDC_FINNHUB_API_KEY" })
            }, null),
            ["AlphaVantage"] = new("Alpha Vantage", new[]
            {
                new CredentialRequirement("ALPHAVANTAGE__APIKEY", new[] { "ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE_API_KEY", "MDC_ALPHA_VANTAGE_API_KEY" })
            }, null),
            // Free / credential-free providers — no API key required
            ["Synthetic"] = new("Synthetic Market Data", Array.Empty<CredentialRequirement>(), null),
            ["StockSharp"] = new("StockSharp", Array.Empty<CredentialRequirement>(),
                "docs/providers/stocksharp-connectors.md")
        };

        if (!providerCredentialMap.TryGetValue(activeDataSource, out var requirement))
        {
            return PreflightCheckResult.Passed(checkName,
                $"Provider '{activeDataSource}' has no credential requirements registered");
        }

        if (requirement.Credentials.Length == 0)
        {
            return PreflightCheckResult.Passed(checkName,
                $"{requirement.DisplayName} does not require API credentials (uses local connection)");
        }

        var missing = new List<string>();
        var found = new List<string>();

        foreach (var cred in requirement.Credentials)
        {
            var value = Environment.GetEnvironmentVariable(cred.PrimaryEnvVar);

            if (string.IsNullOrEmpty(value))
            {
                value = cred.AlternativeEnvVars
                    .Select(Environment.GetEnvironmentVariable)
                    .FirstOrDefault(v => !string.IsNullOrEmpty(v));
            }

            if (string.IsNullOrEmpty(value))
            {
                missing.Add(cred.PrimaryEnvVar);
            }
            else
            {
                found.Add(cred.PrimaryEnvVar);
            }
        }

        var details = new Dictionary<string, object>
        {
            ["provider"] = requirement.DisplayName,
            ["missingCredentials"] = missing,
            ["foundCredentials"] = found
        };

        if (missing.Count > 0)
        {
            var envVarList = string.Join(", ", missing);
            var remediation = $"Set the following environment variables: {envVarList}";
            if (requirement.DocsLink != null)
            {
                remediation += $"\n  Documentation: {requirement.DocsLink}";
            }

            return PreflightCheckResult.Failed(checkName,
                $"{requirement.DisplayName} is the active provider but is missing credentials: {envVarList}",
                remediation,
                details);
        }

        return PreflightCheckResult.Passed(checkName,
            $"{requirement.DisplayName} credentials verified ({found.Count} credential(s) found)",
            details);
    }

    private sealed record ProviderCredentialRequirement(
        string DisplayName,
        CredentialRequirement[] Credentials,
        string? DocsLink
    );

    private sealed record CredentialRequirement(
        string PrimaryEnvVar,
        string[] AlternativeEnvVars
    );

    private async Task<PreflightCheckResult> CheckProviderEndpointsAsync(CancellationToken ct)
    {
        const string checkName = "Provider Endpoints";

        var providers = new[]
        {
            ("Alpaca REST", "https://api.alpaca.markets/v2/account", _config.NetworkTimeoutMs),
            ("Alpaca Stream", "https://stream.data.alpaca.markets/v2/iex", _config.NetworkTimeoutMs)
        };

        var results = new List<(string name, bool reachable, int? statusCode, string? error)>();

        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        using var httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.PreflightChecker);

        foreach (var (name, url, _) in providers)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // Just check if endpoint responds (we expect 401/403 without auth)
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                var statusCode = (int)response.StatusCode;

                // Any response (even 401/403) means the endpoint is reachable
                results.Add((name, true, statusCode, null));
            }
            catch (TaskCanceledException)
            {
                results.Add((name, false, null, "Timeout"));
            }
            catch (HttpRequestException ex)
            {
                results.Add((name, false, null, ex.Message));
            }
            catch (Exception ex)
            {
                results.Add((name, false, null, ex.Message));
            }
        }

        var reachableCount = results.Count(r => r.reachable);

        var details = new Dictionary<string, object>
        {
            ["results"] = results.Select(r => new { r.name, r.reachable, r.statusCode, r.error }).ToArray()
        };

        if (reachableCount == 0)
        {
            return PreflightCheckResult.Warning(checkName,
                "No provider endpoints reachable - data collection may fail",
                "Check network connectivity and firewall settings.",
                details);
        }

        if (reachableCount < results.Count)
        {
            var unreachable = results.Where(r => !r.reachable).Select(r => r.name);
            return PreflightCheckResult.Warning(checkName,
                $"Some provider endpoints unreachable: {string.Join(", ", unreachable)}",
                "Some data providers may not be available.",
                details);
        }

        return PreflightCheckResult.Passed(checkName,
            $"All {reachableCount} provider endpoints reachable",
            details);
    }

    private static double GetElapsedMs(long startTimestamp)
    {
        return (double)(Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency * 1000;
    }
}

/// <summary>
/// Configuration for pre-flight checks.
/// </summary>
public sealed record PreflightConfig
{
    /// <summary>Minimum required disk space in GB.</summary>
    public double MinDiskSpaceGb { get; init; } = 1.0;

    /// <summary>Disk space warning threshold in GB.</summary>
    public double WarnDiskSpaceGb { get; init; } = 5.0;

    /// <summary>Minimum required memory in MB.</summary>
    public double MinMemoryMb { get; init; } = 256;

    /// <summary>Memory warning threshold in MB.</summary>
    public double WarnMemoryMb { get; init; } = 512;

    /// <summary>Network connectivity check timeout in milliseconds.</summary>
    public int NetworkTimeoutMs { get; init; } = 5000;

    /// <summary>Whether to check basic network connectivity (DNS, HTTP endpoints).</summary>
    public bool CheckNetworkConnectivity { get; init; } = true;

    /// <summary>Whether to check provider endpoint connectivity.</summary>
    public bool CheckProviderConnectivity { get; init; } = true;

    public static PreflightConfig Default => new();
}

/// <summary>
/// Result of all pre-flight checks.
/// </summary>
public readonly record struct PreflightResult(
    bool AllChecksPassed,
    bool HasWarnings,
    IReadOnlyList<PreflightCheckResult> Checks,
    double TotalDurationMs,
    DateTimeOffset CheckedAt
)
{
    /// <summary>
    /// Gets a summary string of the pre-flight results.
    /// </summary>
    public string GetSummary()
    {
        var passed = Checks.Count(c => c.Status == PreflightCheckStatus.Passed);
        var warnings = Checks.Count(c => c.Status == PreflightCheckStatus.Warning);
        var failed = Checks.Count(c => c.Status == PreflightCheckStatus.Failed);

        return $"Pre-flight: {passed} passed, {warnings} warnings, {failed} failed ({TotalDurationMs:F0}ms)";
    }
}

/// <summary>
/// Result of a single pre-flight check.
/// </summary>
public readonly record struct PreflightCheckResult(
    string Name,
    PreflightCheckStatus Status,
    string Message,
    string? Remediation,
    IReadOnlyDictionary<string, object>? Details
)
{
    public static PreflightCheckResult Passed(string name, string message, Dictionary<string, object>? details = null)
        => new(name, PreflightCheckStatus.Passed, message, null, details);

    public static PreflightCheckResult Warning(string name, string message, string? remediation = null, Dictionary<string, object>? details = null)
        => new(name, PreflightCheckStatus.Warning, message, remediation, details);

    public static PreflightCheckResult Failed(string name, string message, string? remediation = null, Dictionary<string, object>? details = null)
        => new(name, PreflightCheckStatus.Failed, message, remediation, details);
}

/// <summary>
/// Status of a pre-flight check.
/// </summary>
public enum PreflightCheckStatus : byte
{
    Passed,
    Warning,
    Failed
}

/// <summary>
/// Exception thrown when pre-flight checks fail.
/// </summary>
public sealed class PreflightException : Exception
{
    public PreflightResult Result { get; }

    public PreflightException(string message, PreflightResult result) : base(message)
    {
        Result = result;
    }
}
