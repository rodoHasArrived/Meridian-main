using System.Text;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for dry-run validation of configuration and operations.
/// Implements QW-93: Dry Run Mode.
/// </summary>
public sealed class DryRunService
{
    private readonly ILogger _log = LoggingSetup.ForContext<DryRunService>();

    /// <summary>
    /// Global flag indicating if the application is in dry-run mode.
    /// </summary>
    public static bool IsDryRunMode { get; private set; }

    /// <summary>
    /// Enables dry-run mode globally.
    /// </summary>
    public static void EnableDryRunMode()
    {
        IsDryRunMode = true;
    }

    /// <summary>
    /// Performs a comprehensive dry-run validation.
    /// </summary>
    public async Task<DryRunResult> ValidateAsync(
        AppConfig config,
        DryRunOptions options,
        CancellationToken ct = default)
    {
        var result = new DryRunResult
        {
            StartTime = DateTimeOffset.UtcNow,
            Options = options
        };

        _log.Information("Starting dry-run validation...");

        // Configuration validation
        if (options.ValidateConfiguration)
        {
            result.ConfigurationValidation = await ValidateConfigurationAsync(config, ct);
        }

        // File system validation
        if (options.ValidateFileSystem)
        {
            result.FileSystemValidation = await ValidateFileSystemAsync(config, ct);
        }

        // Network connectivity validation
        if (options.ValidateConnectivity)
        {
            result.ConnectivityValidation = await ValidateConnectivityAsync(config, ct);
        }

        // Provider validation
        if (options.ValidateProviders)
        {
            result.ProviderValidation = await ValidateProvidersAsync(config, ct);
        }

        // Credential authentication validation (live API calls)
        if (options.ValidateProviders && options.ValidateConnectivity)
        {
            result.CredentialAuthValidation = await ValidateCredentialAuthAsync(config, ct);
        }

        // Symbol validation
        if (options.ValidateSymbols)
        {
            result.SymbolValidation = await ValidateSymbolsAsync(config, ct);
        }

        // Resource validation
        if (options.ValidateResources)
        {
            result.ResourceValidation = await ValidateResourcesAsync(ct);
        }

        result.EndTime = DateTimeOffset.UtcNow;
        result.DurationMs = (long)(result.EndTime - result.StartTime).TotalMilliseconds;
        result.OverallSuccess = CalculateOverallSuccess(result);

        _log.Information("Dry-run validation completed in {DurationMs}ms. Overall: {Result}",
            result.DurationMs, result.OverallSuccess ? "PASS" : "FAIL");

        return result;
    }

    /// <summary>
    /// Generates a human-readable dry-run report.
    /// </summary>
    public string GenerateReport(DryRunResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                    DRY-RUN VALIDATION REPORT                     ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"Start Time:   {result.StartTime:O}");
        sb.AppendLine($"Duration:     {result.DurationMs} ms");
        sb.AppendLine($"Overall:      {(result.OverallSuccess ? "✓ PASS" : "✗ FAIL")}");
        sb.AppendLine();

        if (result.ConfigurationValidation != null)
        {
            AppendValidationSection(sb, "Configuration", result.ConfigurationValidation);
        }

        if (result.FileSystemValidation != null)
        {
            AppendValidationSection(sb, "File System", result.FileSystemValidation);
        }

        if (result.ConnectivityValidation != null)
        {
            AppendValidationSection(sb, "Connectivity", result.ConnectivityValidation);
        }

        if (result.ProviderValidation != null)
        {
            AppendValidationSection(sb, "Providers", result.ProviderValidation);
        }

        if (result.SymbolValidation != null)
        {
            AppendValidationSection(sb, "Symbols", result.SymbolValidation);
        }

        if (result.ResourceValidation != null)
        {
            AppendValidationSection(sb, "Resources", result.ResourceValidation);
        }

        if (result.CredentialAuthValidation != null)
        {
            AppendValidationSection(sb, "Credential Authentication", result.CredentialAuthValidation);
        }

        sb.AppendLine("══════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    private void AppendValidationSection(StringBuilder sb, string name, ValidationSection section)
    {
        var status = section.Success ? "✓" : "✗";
        sb.AppendLine($"┌─────────────────────────────────────────────────────────────────┐");
        sb.AppendLine($"│ {status} {name,-60} │");
        sb.AppendLine($"└─────────────────────────────────────────────────────────────────┘");

        foreach (var check in section.Checks)
        {
            var checkStatus = check.Passed ? "  ✓" : "  ✗";
            sb.AppendLine($"{checkStatus} {check.Name}: {check.Message}");
        }

        if (section.Warnings.Count > 0)
        {
            sb.AppendLine("  Warnings:");
            foreach (var warning in section.Warnings)
            {
                sb.AppendLine($"    ⚠ {warning}");
            }
        }

        if (section.Errors.Count > 0)
        {
            sb.AppendLine("  Errors:");
            foreach (var error in section.Errors)
            {
                sb.AppendLine($"    ✗ {error}");
            }
        }

        sb.AppendLine();
    }

    private Task<ValidationSection> ValidateConfigurationAsync(AppConfig config, CancellationToken ct)
    {
        var section = new ValidationSection { Name = "Configuration" };

        // Check DataRoot
        section.AddCheck("DataRoot", !string.IsNullOrWhiteSpace(config.DataRoot),
            config.DataRoot ?? "(not set)",
            "DataRoot must be specified");

        // Check DataSource
        section.AddCheck("DataSource", Enum.IsDefined(config.DataSource),
            config.DataSource.ToString(),
            "Invalid DataSource value");

        // Check Alpaca credentials if Alpaca is selected
        if (config.DataSource == DataSourceKind.Alpaca)
        {
            var hasAlpacaCreds = !string.IsNullOrWhiteSpace(config.Alpaca?.KeyId) &&
                                 !string.IsNullOrWhiteSpace(config.Alpaca?.SecretKey);
            section.AddCheck("Alpaca Credentials", hasAlpacaCreds,
                hasAlpacaCreds ? "Configured" : "Missing",
                "Alpaca KeyId and SecretKey are required");
        }

        // Check symbols
        var hasSymbols = config.Symbols?.Length > 0;
        section.AddCheck("Symbols", hasSymbols,
            hasSymbols ? $"{config.Symbols!.Length} symbol(s)" : "None (will use default SPY)",
            "");

        // Validate storage config
        if (config.Storage != null)
        {
            var validNaming = new[] { "flat", "bysymbol", "bydate", "bytype", "bysource" };
            var isValidNaming = validNaming.Contains(config.Storage.NamingConvention?.ToLowerInvariant());
            section.AddCheck("Storage Naming", isValidNaming,
                config.Storage.NamingConvention ?? "BySymbol",
                "Invalid naming convention");
        }

        section.Success = section.Errors.Count == 0;
        return Task.FromResult(section);
    }

    private async Task<ValidationSection> ValidateFileSystemAsync(AppConfig config, CancellationToken ct)
    {
        var section = new ValidationSection { Name = "FileSystem" };

        // Check data root directory
        var dataRootExists = Directory.Exists(config.DataRoot);
        if (!dataRootExists)
        {
            try
            {
                // Try to create
                Directory.CreateDirectory(config.DataRoot);
                section.AddCheck("DataRoot Directory", true, "Created successfully", "");
            }
            catch (Exception ex)
            {
                section.AddCheck("DataRoot Directory", false,
                    $"Cannot create: {ex.Message}",
                    "Data root directory must be writable");
            }
        }
        else
        {
            section.AddCheck("DataRoot Directory", true, "Exists", "");
        }

        // Check write permission
        try
        {
            var testFile = Path.Combine(config.DataRoot, $".dry-run-test-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(testFile, "test", ct);
            File.Delete(testFile);
            section.AddCheck("Write Permission", true, "Writable", "");
        }
        catch (Exception ex)
        {
            section.AddCheck("Write Permission", false,
                $"Not writable: {ex.Message}",
                "Data root must be writable");
        }

        // Check disk space
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(config.DataRoot)) ?? "/";
            var driveInfo = new DriveInfo(root);
            var freeGb = driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;

            if (freeGb < 1)
            {
                section.AddCheck("Disk Space", false, $"{freeGb:F2} GB free", "Less than 1GB free");
            }
            else if (freeGb < 10)
            {
                section.AddCheck("Disk Space", true, $"{freeGb:F2} GB free", "");
                section.Warnings.Add($"Low disk space: {freeGb:F2} GB");
            }
            else
            {
                section.AddCheck("Disk Space", true, $"{freeGb:F2} GB free", "");
            }
        }
        catch
        {
            section.Warnings.Add("Could not check disk space");
        }

        section.Success = section.Errors.Count == 0;
        return section;
    }

    private async Task<ValidationSection> ValidateConnectivityAsync(AppConfig config, CancellationToken ct)
    {
        var section = new ValidationSection { Name = "Connectivity" };

        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        using var httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.DryRun);

        // Check basic internet connectivity
        try
        {
            var response = await httpClient.GetAsync("https://api.github.com/", ct);
            section.AddCheck("Internet Connectivity", true, "Connected", "");
        }
        catch
        {
            section.AddCheck("Internet Connectivity", false, "No connection", "Internet connection required");
        }

        // Check provider endpoints based on configuration
        if (config.DataSource == DataSourceKind.Alpaca)
        {
            try
            {
                var response = await httpClient.GetAsync("https://data.alpaca.markets/v2/stocks/bars/latest?symbols=SPY", ct);
                section.AddCheck("Alpaca API", true, $"Reachable ({(int)response.StatusCode})", "");
            }
            catch (Exception ex)
            {
                section.AddCheck("Alpaca API", false, ex.Message, "");
                section.Warnings.Add("Alpaca API not reachable - check network/firewall");
            }
        }

        // Check backfill providers if enabled
        if (config.Backfill?.Enabled == true)
        {
            try
            {
                var response = await httpClient.GetAsync("https://stooq.com/", ct);
                section.AddCheck("Stooq (Backfill)", true, "Reachable", "");
            }
            catch
            {
                section.Warnings.Add("Stooq not reachable - backfill may fail");
            }
        }

        section.Success = section.Errors.Count == 0;
        return section;
    }

    private Task<ValidationSection> ValidateProvidersAsync(AppConfig config, CancellationToken ct)
    {
        var section = new ValidationSection { Name = "Providers" };

        // Check configured provider
        section.AddCheck("Active Provider", true, config.DataSource.ToString(), "");

        // Check provider-specific configuration
        switch (config.DataSource)
        {
            case DataSourceKind.Alpaca:
                var alpacaConfigured = !string.IsNullOrWhiteSpace(config.Alpaca?.KeyId);
                section.AddCheck("Alpaca Configuration", alpacaConfigured,
                    alpacaConfigured ? "Credentials set" : "Credentials missing",
                    alpacaConfigured ? "" : "Alpaca requires KeyId and SecretKey");
                break;

            case DataSourceKind.IB:
                section.AddCheck("IB Configuration", true,
                    "IB Gateway/TWS required",
                    "");
                section.Warnings.Add("Ensure IB Gateway or TWS is running on port 7497/7496");
                break;

            case DataSourceKind.Polygon:
                var polygonApiKey = config.Polygon?.ApiKey ?? Environment.GetEnvironmentVariable("POLYGON_API_KEY");
                var polygonConfigured = !string.IsNullOrWhiteSpace(polygonApiKey);
                section.AddCheck("Polygon Configuration", polygonConfigured,
                    polygonConfigured ? "API key set" : "API key missing",
                    polygonConfigured ? "" : "Polygon requires API key");
                break;

            case DataSourceKind.Synthetic:
                section.AddCheck("Synthetic Configuration", config.Synthetic?.Enabled == true,
                    config.Synthetic?.Enabled == true ? "Offline dataset enabled" : "Synthetic dataset disabled",
                    config.Synthetic?.Enabled == true ? "" : "Enable Synthetic:Enabled to run without live providers");
                break;
        }

        // Check backfill providers
        if (config.Backfill?.Enabled == true)
        {
            section.AddCheck("Backfill Provider", true,
                config.Backfill.Provider ?? "composite",
                "");
        }

        section.Success = section.Errors.Count == 0;
        return Task.FromResult(section);
    }

    private Task<ValidationSection> ValidateSymbolsAsync(AppConfig config, CancellationToken ct)
    {
        var section = new ValidationSection { Name = "Symbols" };

        var symbols = config.Symbols ?? Array.Empty<SymbolConfig>();

        if (symbols.Length == 0)
        {
            section.Warnings.Add("No symbols configured - SPY will be used as default");
            section.AddCheck("Symbol Count", true, "0 (default SPY)", "");
        }
        else
        {
            section.AddCheck("Symbol Count", true, symbols.Length.ToString(), "");

            // Validate each symbol
            foreach (var sym in symbols)
            {
                var isValid = !string.IsNullOrWhiteSpace(sym.Symbol) &&
                              System.Text.RegularExpressions.Regex.IsMatch(sym.Symbol, @"^[A-Z0-9\-\.]+$");

                if (!isValid)
                {
                    section.Warnings.Add($"Symbol '{sym.Symbol}' may be invalid");
                }

                if (sym.SubscribeDepth && sym.DepthLevels <= 0)
                {
                    section.Warnings.Add($"Symbol '{sym.Symbol}' has depth enabled but DepthLevels <= 0");
                }
            }

            // Summary of subscriptions
            var tradeCount = symbols.Count(s => s.SubscribeTrades);
            var depthCount = symbols.Count(s => s.SubscribeDepth);
            section.AddCheck("Trade Subscriptions", true, tradeCount.ToString(), "");
            section.AddCheck("Depth Subscriptions", true, depthCount.ToString(), "");
        }

        section.Success = section.Errors.Count == 0;
        return Task.FromResult(section);
    }

    private Task<ValidationSection> ValidateResourcesAsync(CancellationToken ct)
    {
        var section = new ValidationSection { Name = "Resources" };

        // Memory
        var totalMemoryMb = GC.GetTotalMemory(false) / 1024 / 1024;
        section.AddCheck("Current Memory", true, $"{totalMemoryMb} MB", "");

        // Processor count
        section.AddCheck("Processors", true, Environment.ProcessorCount.ToString(), "");

        // .NET version
        section.AddCheck(".NET Version", true, Environment.Version.ToString(), "");

        // OS
        section.AddCheck("OS", true, Environment.OSVersion.ToString(), "");

        section.Success = true;
        return Task.FromResult(section);
    }

    private async Task<ValidationSection> ValidateCredentialAuthAsync(AppConfig config, CancellationToken ct)
    {
        var section = new ValidationSection { Name = "CredentialAuth" };

        try
        {
            await using var validator = new CredentialValidationService();
            var summary = await validator.ValidateAllAsync(config, ct);

            foreach (var result in summary.Results)
            {
                section.AddCheck(
                    result.Provider,
                    result.IsValid,
                    result.IsValid
                        ? $"Authenticated ({result.ResponseTime.TotalMilliseconds:F0}ms){(result.AccountInfo != null ? $" - {result.AccountInfo}" : "")}"
                        : $"Failed: {result.Message}",
                    result.IsValid ? "" : $"{result.Provider} credentials are invalid or expired"
                );
            }

            foreach (var warning in summary.Warnings)
            {
                section.Warnings.Add(warning);
            }

            if (summary.Results.Count == 0)
            {
                section.AddCheck("Credentials", true,
                    "No API credentials configured (using free providers only)", "");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            section.Warnings.Add($"Credential validation failed: {ex.Message}");
        }

        section.Success = section.Errors.Count == 0;
        return section;
    }

    private static bool CalculateOverallSuccess(DryRunResult result)
    {
        return (result.ConfigurationValidation?.Success ?? true) &&
               (result.FileSystemValidation?.Success ?? true) &&
               (result.ConnectivityValidation?.Success ?? true) &&
               (result.ProviderValidation?.Success ?? true) &&
               (result.SymbolValidation?.Success ?? true) &&
               (result.ResourceValidation?.Success ?? true) &&
               (result.CredentialAuthValidation?.Success ?? true);
    }
}

/// <summary>
/// Options for dry-run validation.
/// </summary>
public sealed record DryRunOptions(
    bool ValidateConfiguration = true,
    bool ValidateFileSystem = true,
    bool ValidateConnectivity = true,
    bool ValidateProviders = true,
    bool ValidateSymbols = true,
    bool ValidateResources = true
);

/// <summary>
/// Result of dry-run validation.
/// </summary>
public sealed class DryRunResult
{
    public bool OverallSuccess { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public long DurationMs { get; set; }
    public DryRunOptions? Options { get; set; }

    public ValidationSection? ConfigurationValidation { get; set; }
    public ValidationSection? FileSystemValidation { get; set; }
    public ValidationSection? ConnectivityValidation { get; set; }
    public ValidationSection? ProviderValidation { get; set; }
    public ValidationSection? SymbolValidation { get; set; }
    public ValidationSection? ResourceValidation { get; set; }
    public ValidationSection? CredentialAuthValidation { get; set; }
}

/// <summary>
/// A validation section with checks.
/// </summary>
public sealed class ValidationSection
{
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public List<ValidationCheck> Checks { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public void AddCheck(string name, bool passed, string message, string errorMessage)
    {
        Checks.Add(new ValidationCheck
        {
            Name = name,
            Passed = passed,
            Message = message
        });

        if (!passed && !string.IsNullOrWhiteSpace(errorMessage))
        {
            Errors.Add(errorMessage);
        }
    }
}

/// <summary>
/// A single validation check.
/// </summary>
public sealed class ValidationCheck
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Message { get; set; } = "";
}
