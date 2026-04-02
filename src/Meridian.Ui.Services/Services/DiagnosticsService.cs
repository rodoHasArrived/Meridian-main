using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for system diagnostics including dry-run, preflight checks, and diagnostic bundles.
/// Provides comprehensive system validation and troubleshooting capabilities.
/// </summary>
public sealed class DiagnosticsService
{
    private static readonly Lazy<DiagnosticsService> _instance = new(() => new DiagnosticsService());
    private readonly ApiClientService _apiClient;

    public static DiagnosticsService Instance => _instance.Value;

    private DiagnosticsService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Runs a dry-run validation of the configuration without starting data collection.
    /// </summary>
    public async Task<DryRunResult> RunDryRunAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<DryRunResponse>(
            UiApiRoutes.DiagnosticsDryRun,
            null,
            ct);

        if (response.Success && response.Data != null)
        {
            return new DryRunResult
            {
                Success = response.Data.Success,
                ConfigurationValid = response.Data.ConfigurationValid,
                CredentialsValid = response.Data.CredentialsValid,
                StorageWritable = response.Data.StorageWritable,
                ProvidersReachable = response.Data.ProvidersReachable,
                SymbolsValidated = response.Data.SymbolsValidated,
                Warnings = response.Data.Warnings?.ToList() ?? new List<string>(),
                Errors = response.Data.Errors?.ToList() ?? new List<string>(),
                ValidationDetails = response.Data.ValidationDetails?.ToList() ?? new List<ValidationDetail>()
            };
        }

        return new DryRunResult
        {
            Success = false,
            Errors = new List<string> { response.ErrorMessage ?? "Failed to run dry-run validation" }
        };
    }

    /// <summary>
    /// Runs preflight checks to verify system readiness.
    /// </summary>
    public async Task<PreflightResult> RunPreflightCheckAsync(CancellationToken ct = default)
    {
        var result = new PreflightResult();
        var checks = new List<PreflightCheck>();

        // Check service connectivity
        var serviceHealth = await _apiClient.CheckHealthAsync(ct);
        checks.Add(new PreflightCheck
        {
            Name = "Service Connectivity",
            Category = "Network",
            Passed = serviceHealth.IsReachable,
            Message = serviceHealth.IsReachable
                ? $"Service reachable (latency: {serviceHealth.LatencyMs:F0}ms)"
                : serviceHealth.ErrorMessage ?? "Service not reachable",
            Severity = serviceHealth.IsReachable ? CheckSeverity.Info : CheckSeverity.Critical
        });

        // Check provider status
        var providerResponse = await _apiClient.GetWithResponseAsync<ProviderStatusResponse>(
            UiApiRoutes.DiagnosticsProviders,
            ct);

        if (providerResponse.Success && providerResponse.Data?.Providers != null)
        {
            foreach (var provider in providerResponse.Data.Providers)
            {
                checks.Add(new PreflightCheck
                {
                    Name = provider.Name,
                    Category = "Providers",
                    Passed = provider.IsAvailable,
                    Message = provider.IsAvailable
                        ? $"Available (enabled: {provider.IsEnabled})"
                        : provider.Error ?? "Not available",
                    Severity = provider.IsAvailable ? CheckSeverity.Info :
                               provider.IsEnabled ? CheckSeverity.Warning : CheckSeverity.Info
                });
            }
        }

        // Check storage
        var storageResponse = await _apiClient.GetWithResponseAsync<StorageStatusResponse>(
            UiApiRoutes.DiagnosticsStorage,
            ct);

        if (storageResponse.Success && storageResponse.Data != null)
        {
            var storage = storageResponse.Data;
            checks.Add(new PreflightCheck
            {
                Name = "Storage Path",
                Category = "Storage",
                Passed = storage.PathExists,
                Message = storage.PathExists
                    ? $"Path exists: {storage.Path}"
                    : $"Path does not exist: {storage.Path}",
                Severity = storage.PathExists ? CheckSeverity.Info : CheckSeverity.Critical
            });

            checks.Add(new PreflightCheck
            {
                Name = "Storage Writable",
                Category = "Storage",
                Passed = storage.IsWritable,
                Message = storage.IsWritable ? "Storage is writable" : "Storage is not writable",
                Severity = storage.IsWritable ? CheckSeverity.Info : CheckSeverity.Critical
            });

            checks.Add(new PreflightCheck
            {
                Name = "Free Disk Space",
                Category = "Storage",
                Passed = storage.FreeSpaceGb > 1,
                Message = $"{storage.FreeSpaceGb:F1} GB free",
                Severity = storage.FreeSpaceGb > 10 ? CheckSeverity.Info :
                           storage.FreeSpaceGb > 1 ? CheckSeverity.Warning : CheckSeverity.Critical
            });
        }

        // Check configuration
        var configResponse = await _apiClient.GetWithResponseAsync<ConfigStatusResponse>(
            UiApiRoutes.DiagnosticsConfig,
            ct);

        if (configResponse.Success && configResponse.Data != null)
        {
            var config = configResponse.Data;
            checks.Add(new PreflightCheck
            {
                Name = "Configuration File",
                Category = "Configuration",
                Passed = config.FileExists,
                Message = config.FileExists
                    ? $"Config loaded: {config.FilePath}"
                    : "Configuration file not found",
                Severity = config.FileExists ? CheckSeverity.Info : CheckSeverity.Critical
            });

            checks.Add(new PreflightCheck
            {
                Name = "Symbols Configured",
                Category = "Configuration",
                Passed = config.SymbolCount > 0,
                Message = $"{config.SymbolCount} symbols configured",
                Severity = config.SymbolCount > 0 ? CheckSeverity.Info : CheckSeverity.Warning
            });
        }

        result.Checks = checks;
        result.PassedCount = checks.Count(c => c.Passed);
        result.FailedCount = checks.Count(c => !c.Passed);
        result.Success = !checks.Any(c => !c.Passed && c.Severity == CheckSeverity.Critical);

        return result;
    }

    /// <summary>
    /// Generates a diagnostic bundle for troubleshooting.
    /// </summary>
    public async Task<DiagnosticBundleResult> GenerateDiagnosticBundleAsync(
        DiagnosticBundleOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<DiagnosticBundleResponse>(
            UiApiRoutes.DiagnosticsBundle,
            new
            {
                includeLogs = options.IncludeLogs,
                includeConfig = options.IncludeConfig,
                includeMetrics = options.IncludeMetrics,
                includeSampleData = options.IncludeSampleData,
                logDays = options.LogDays,
                redactSecrets = options.RedactSecrets
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new DiagnosticBundleResult
            {
                Success = true,
                BundlePath = response.Data.BundlePath,
                FileSizeBytes = response.Data.FileSizeBytes,
                IncludedFiles = response.Data.IncludedFiles?.ToList() ?? new List<string>()
            };
        }

        return new DiagnosticBundleResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to generate diagnostic bundle"
        };
    }

    /// <summary>
    /// Gets the current system metrics.
    /// </summary>
    public async Task<DiagnosticSystemMetrics> GetDiagnosticSystemMetricsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<DiagnosticSystemMetrics>(
            UiApiRoutes.DiagnosticsMetrics,
            ct);

        return response.Data ?? new DiagnosticSystemMetrics();
    }

    /// <summary>
    /// Validates a specific configuration setting.
    /// </summary>
    public async Task<ValidationResult> ValidateConfigurationAsync(
        string settingName,
        string value,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<ValidationResult>(
            UiApiRoutes.DiagnosticsValidate,
            new { setting = settingName, value },
            ct);

        return response.Data ?? new ValidationResult { Valid = false, Error = response.ErrorMessage };
    }

    /// <summary>
    /// Tests connectivity to a specific provider.
    /// </summary>
    public async Task<DiagnosticProviderTestResult> TestProviderAsync(string providerName, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<DiagnosticProviderTestResult>(
            UiApiRoutes.WithParam(UiApiRoutes.DiagnosticsProviderTest, "providerName", providerName),
            null,
            ct);

        return response.Data ?? new DiagnosticProviderTestResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to test provider"
        };
    }

    /// <summary>
    /// Runs a quick health check (equivalent to --quick-check CLI command).
    /// </summary>
    public async Task<QuickCheckResult> RunQuickCheckAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<QuickCheckResponse>(
            UiApiRoutes.DiagnosticsQuickCheck,
            ct);

        if (response.Success && response.Data != null)
        {
            return new QuickCheckResult
            {
                Success = true,
                Overall = response.Data.Overall,
                Checks = response.Data.Checks?.ToList() ?? new List<QuickCheckItem>()
            };
        }

        return new QuickCheckResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Quick check failed"
        };
    }

    /// <summary>
    /// Gets current configuration in human-readable format (equivalent to --show-config CLI command).
    /// </summary>
    public async Task<ShowConfigResult> ShowConfigAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ShowConfigResponse>(
            UiApiRoutes.DiagnosticsShowConfig,
            ct);

        if (response.Success && response.Data != null)
        {
            return new ShowConfigResult
            {
                Success = true,
                Sections = response.Data.Sections?.ToList() ?? new List<ConfigSection>()
            };
        }

        return new ShowConfigResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get configuration"
        };
    }

    /// <summary>
    /// Gets list of available error codes and their descriptions (equivalent to --error-codes CLI command).
    /// </summary>
    public async Task<ErrorCodesResult> GetErrorCodesAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<ErrorCodesResponse>(
            UiApiRoutes.DiagnosticsErrorCodes,
            ct);

        if (response.Success && response.Data != null)
        {
            return new ErrorCodesResult
            {
                Success = true,
                ErrorCodes = response.Data.ErrorCodes?.ToList() ?? new List<ErrorCodeInfo>()
            };
        }

        return new ErrorCodesResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get error codes"
        };
    }

    /// <summary>
    /// Runs system self-tests (equivalent to --selftest CLI command).
    /// </summary>
    public async Task<SelfTestResult> RunSelfTestAsync(SelfTestOptions? options = null, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SelfTestResponse>(
            UiApiRoutes.DiagnosticsSelftest,
            options ?? new SelfTestOptions(),
            ct);

        if (response.Success && response.Data != null)
        {
            return new SelfTestResult
            {
                Success = response.Data.AllPassed,
                Tests = response.Data.Tests?.ToList() ?? new List<SelfTestItem>(),
                PassedCount = response.Data.PassedCount,
                FailedCount = response.Data.FailedCount,
                SkippedCount = response.Data.SkippedCount
            };
        }

        return new SelfTestResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Self-test failed"
        };
    }

    /// <summary>
    /// Validates all configured credentials.
    /// </summary>
    public async Task<CredentialValidationResult> ValidateCredentialsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<CredentialValidationResponse>(
            UiApiRoutes.DiagnosticsValidateCredentials,
            ct);

        if (response.Success && response.Data != null)
        {
            return new CredentialValidationResult
            {
                Success = true,
                Results = response.Data.Results?.ToList() ?? new List<ProviderCredentialStatus>()
            };
        }

        return new CredentialValidationResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    /// <summary>
    /// Tests connectivity to all configured providers.
    /// </summary>
    public async Task<AllProvidersTestResult> TestAllProvidersAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<AllProvidersTestResponse>(
            UiApiRoutes.DiagnosticsTestConnectivity,
            null,
            ct);

        if (response.Success && response.Data != null)
        {
            return new AllProvidersTestResult
            {
                Success = true,
                AllConnected = response.Data.AllConnected,
                Results = response.Data.Results?.ToList() ?? new List<ProviderConnectivityResult>()
            };
        }

        return new AllProvidersTestResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Connectivity test failed"
        };
    }

    /// <summary>
    /// Validates the full configuration file.
    /// </summary>
    public async Task<DiagnosticConfigValidationResult> ValidateFullConfigAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<ConfigValidationResponse>(
            UiApiRoutes.DiagnosticsValidateConfig,
            null,
            ct);

        if (response.Success && response.Data != null)
        {
            return new DiagnosticConfigValidationResult
            {
                Success = true,
                IsValid = response.Data.IsValid,
                Issues = response.Data.Issues?.ToList() ?? new List<ConfigIssue>(),
                Warnings = response.Data.Warnings?.ToList() ?? new List<string>()
            };
        }

        return new DiagnosticConfigValidationResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }
}


public sealed class DryRunResult
{
    public bool Success { get; set; }
    public bool ConfigurationValid { get; set; }
    public bool CredentialsValid { get; set; }
    public bool StorageWritable { get; set; }
    public bool ProvidersReachable { get; set; }
    public int SymbolsValidated { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<ValidationDetail> ValidationDetails { get; set; } = new();
}

public sealed class ValidationDetail
{
    public string Category { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public bool Valid { get; set; }
    public string? Message { get; set; }
}

public sealed class PreflightResult
{
    public bool Success { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public List<PreflightCheck> Checks { get; set; } = new();
}

public sealed class PreflightCheck
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
    public CheckSeverity Severity { get; set; }
}

public enum CheckSeverity : byte
{
    Info,
    Warning,
    Critical
}

public sealed class DiagnosticBundleOptions
{
    public bool IncludeLogs { get; set; } = true;
    public bool IncludeConfig { get; set; } = true;
    public bool IncludeMetrics { get; set; } = true;
    public bool IncludeSampleData { get; set; } = false;
    public int LogDays { get; set; } = 7;
    public bool RedactSecrets { get; set; } = true;
}

public sealed class DiagnosticBundleResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? BundlePath { get; set; }
    public long FileSizeBytes { get; set; }
    public List<string> IncludedFiles { get; set; } = new();
}

public sealed class DiagnosticSystemMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double DiskUsagePercent { get; set; }
    public int ActiveConnections { get; set; }
    public int ActiveSubscriptions { get; set; }
    public long EventsPerSecond { get; set; }
    public long TotalEventsProcessed { get; set; }
    public TimeSpan Uptime { get; set; }
}

public sealed class ValidationResult
{
    public bool Valid { get; set; }
    public string? Error { get; set; }
    public string? Suggestion { get; set; }
}

public sealed class DiagnosticProviderTestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public double LatencyMs { get; set; }
    public string? Version { get; set; }
    public Dictionary<string, string>? Capabilities { get; set; }
}



public sealed class DryRunResponse
{
    public bool Success { get; set; }
    public bool ConfigurationValid { get; set; }
    public bool CredentialsValid { get; set; }
    public bool StorageWritable { get; set; }
    public bool ProvidersReachable { get; set; }
    public int SymbolsValidated { get; set; }
    public string[]? Warnings { get; set; }
    public string[]? Errors { get; set; }
    public List<ValidationDetail>? ValidationDetails { get; set; }
}

public sealed class ProviderStatusResponse
{
    public List<ProviderInfo>? Providers { get; set; }
}

public sealed class ProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsAvailable { get; set; }
    public string? Error { get; set; }
}

public sealed class StorageStatusResponse
{
    public string Path { get; set; } = string.Empty;
    public bool PathExists { get; set; }
    public bool IsWritable { get; set; }
    public double FreeSpaceGb { get; set; }
}

public sealed class ConfigStatusResponse
{
    public bool FileExists { get; set; }
    public string? FilePath { get; set; }
    public int SymbolCount { get; set; }
}

public sealed class DiagnosticBundleResponse
{
    public string? BundlePath { get; set; }
    public long FileSizeBytes { get; set; }
    public string[]? IncludedFiles { get; set; }
}

// Quick Check
public sealed class QuickCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Overall { get; set; } = string.Empty;
    public List<QuickCheckItem> Checks { get; set; } = new();
}

public sealed class QuickCheckResponse
{
    public string Overall { get; set; } = string.Empty;
    public List<QuickCheckItem>? Checks { get; set; }
}

public sealed class QuickCheckItem
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Details { get; set; }
}

// Show Config
public sealed class ShowConfigResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ConfigSection> Sections { get; set; } = new();
}

public sealed class ShowConfigResponse
{
    public List<ConfigSection>? Sections { get; set; }
}

public sealed class ConfigSection
{
    public string Name { get; set; } = string.Empty;
    public List<ConfigItem> Items { get; set; } = new();
}

public sealed class ConfigItem
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Source { get; set; }
    public bool IsSensitive { get; set; }
}

// Error Codes
public sealed class ErrorCodesResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ErrorCodeInfo> ErrorCodes { get; set; } = new();
}

public sealed class ErrorCodesResponse
{
    public List<ErrorCodeInfo>? ErrorCodes { get; set; }
}

public sealed class ErrorCodeInfo
{
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string Severity { get; set; } = string.Empty;
}

// Self Test
public sealed class SelfTestOptions
{
    public bool TestStorage { get; set; } = true;
    public bool TestProviders { get; set; } = true;
    public bool TestConfiguration { get; set; } = true;
    public bool TestNetwork { get; set; } = true;
}

public sealed class SelfTestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<SelfTestItem> Tests { get; set; } = new();
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class SelfTestResponse
{
    public bool AllPassed { get; set; }
    public List<SelfTestItem>? Tests { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class SelfTestItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public double DurationMs { get; set; }
}

// Credential Validation
public sealed class CredentialValidationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderCredentialStatus> Results { get; set; } = new();
}

public sealed class CredentialValidationResponse
{
    public List<ProviderCredentialStatus>? Results { get; set; }
}

public sealed class ProviderCredentialStatus
{
    public string Provider { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

// All Providers Test
public sealed class AllProvidersTestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool AllConnected { get; set; }
    public List<ProviderConnectivityResult> Results { get; set; } = new();
}

public sealed class AllProvidersTestResponse
{
    public bool AllConnected { get; set; }
    public List<ProviderConnectivityResult>? Results { get; set; }
}

public sealed class ProviderConnectivityResult
{
    public string Provider { get; set; } = string.Empty;
    public bool Connected { get; set; }
    public double LatencyMs { get; set; }
    public string? Error { get; set; }
    public string? Version { get; set; }
}

// Config Validation
public sealed class DiagnosticConfigValidationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool IsValid { get; set; }
    public List<ConfigIssue> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class ConfigValidationResponse
{
    public bool IsValid { get; set; }
    public List<ConfigIssue>? Issues { get; set; }
    public List<string>? Warnings { get; set; }
}

public sealed class ConfigIssue
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Suggestion { get; set; }
}

