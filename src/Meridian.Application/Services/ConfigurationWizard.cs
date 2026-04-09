using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Wizard;
using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Metadata;
using Meridian.Application.Wizard.Steps;
using Meridian.Storage;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Interactive configuration wizard for first-time setup.
/// Guides users through configuration with a step-by-step process.
/// Delegates orchestration to <see cref="WizardCoordinator"/> and individual
/// <see cref="IWizardStep"/> implementations.
/// </summary>
public sealed class ConfigurationWizard
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConfigurationWizard>();
    private readonly AutoConfigurationService _autoConfig;
    private readonly TextWriter _output;
    private readonly TextReader _input;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigurationWizard(TextWriter? output = null, TextReader? input = null)
    {
        _autoConfig = new AutoConfigurationService();
        _output = output ?? Console.Out;
        _input = input ?? Console.In;
    }

    /// <summary>
    /// Runs the interactive configuration wizard using the step-graph coordinator.
    /// </summary>
    public async Task<WizardResult> RunAsync(CancellationToken ct = default)
    {
        try
        {
            PrintHeader();

            var coordinator = WizardWorkflowFactory.CreateInteractive(_autoConfig, _output, _input);
            var context = new WizardContext();

            await coordinator.RunAsync(context, WizardStepId.DetectProviders, ct);

            if (context.IsCancelled || context.FinalConfig == null)
                return new WizardResult(Success: false, Config: null, ConfigPath: null);

            if (context.SavedConfigPath != null)
                PrintNextSteps(context.SavedConfigPath, context.FinalConfig.DataSource);

            return new WizardResult(
                Success: context.SavedConfigPath != null,
                Config: context.FinalConfig,
                ConfigPath: context.SavedConfigPath);
        }
        catch (OperationCanceledException)
        {
            PrintLine("\nWizard cancelled.");
            return new WizardResult(Success: false, Config: null, ConfigPath: null);
        }
    }

    /// <summary>
    /// Runs a quick auto-configuration without interactive prompts.
    /// </summary>
    public WizardResult RunQuickSetup()
    {
        PrintHeader();
        PrintLine("Running quick auto-configuration...\n");

        var result = _autoConfig.AutoConfigure();

        if (result.AppliedFixes.Count > 0)
        {
            PrintLine("Applied automatic fixes:");
            foreach (var fix in result.AppliedFixes)
                PrintLine($"  - {fix}");
            PrintLine();
        }

        if (result.Warnings.Count > 0)
        {
            PrintWarning("Warnings:");
            foreach (var warning in result.Warnings)
                PrintLine($"  - {warning}");
            PrintLine();
        }

        PrintLine("Detected providers:");
        foreach (var provider in result.DetectedProviders)
        {
            var status = provider.HasCredentials ? "[OK]" : "[--]";
            PrintLine($"  {status} {provider.DisplayName}");
        }
        PrintLine();

        var configPath = Path.Combine("config", "appsettings.json");
        SaveConfiguration(result.Config, configPath);

        PrintSuccess($"Configuration saved to: {configPath}");

        if (result.Recommendations.Count > 0)
        {
            PrintLine("\nRecommendations:");
            foreach (var rec in result.Recommendations)
                PrintLine($"  - {rec}");
        }

        PrintLine("\nNext steps:");
        PrintLine("  1. Validate:  dotnet run -- --dry-run");
        PrintLine("  2. Start:     dotnet run -- --mode desktop");
        PrintLine("  3. API:       http://localhost:8080");
        PrintLine();

        return new WizardResult(Success: true, Config: result.Config, ConfigPath: configPath);
    }

    /// <summary>
    /// Runs a quickstart flow: auto-configures, validates, and returns a config ready to launch.
    /// </summary>
    public async Task<WizardResult> RunQuickstartAsync(CancellationToken ct = default)
    {
        PrintLine("=".PadRight(60, '='));
        PrintLine("  Meridian - Quickstart");
        PrintLine("=".PadRight(60, '='));
        PrintLine();

        // Step 1: Auto-detect providers from environment
        PrintLine("[1/4] Detecting providers from environment...");
        var autoResult = _autoConfig.AutoConfigure();

        var configuredProviders = autoResult.DetectedProviders.Where(p => p.HasCredentials).ToList();

        PrintLine($"  Found {configuredProviders.Count} configured provider(s)");
        foreach (var p in configuredProviders)
            PrintLine($"    [OK] {p.DisplayName}");

        if (configuredProviders.Count == 0)
        {
            PrintLine("  No API credentials found - using free providers (Yahoo, Stooq) for backfill.");
            PrintLine("  Tip: Set ALPACA_KEY_ID + ALPACA_SECRET_KEY for real-time streaming.\n");
        }

        // Step 2: Generate config
        PrintLine("\n[2/4] Generating configuration...");
        var config = autoResult.Config;
        PrintLine($"  Data source: {config.DataSource}");
        PrintLine($"  Symbols: {string.Join(", ", (config.Symbols ?? []).Select(s => s.Symbol))}");
        PrintLine($"  Storage: {config.Storage?.Profile ?? "default"}");

        // Step 3: Validate credentials if any
        if (configuredProviders.Count > 0)
        {
            PrintLine("\n[3/4] Validating credentials...");
            try
            {
                await using var validator = new CredentialValidationService();
                var validationSummary = await validator.ValidateAllAsync(config, ct);

                foreach (var r in validationSummary.Results)
                {
                    var status = r.IsValid ? "[OK]" : "[FAIL]";
                    PrintLine($"  {status} {r.Provider}: {r.Message} ({r.ResponseTime.TotalMilliseconds:F0}ms)");
                }

                if (!validationSummary.AllValid)
                {
                    PrintWarning("\n  Some credentials failed validation. Check the warnings above.");
                    PrintLine("  The collector may not be able to connect to all providers.\n");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PrintWarning($"  Credential validation failed: {ex.Message}");
                PrintLine("  Continuing anyway - this may be a network issue.");
            }
        }
        else
        {
            PrintLine("\n[3/4] Skipping credential validation (no API keys configured).");
        }

        // Step 4: Save config
        PrintLine("\n[4/4] Saving configuration...");
        var configPath = Path.Combine("config", "appsettings.json");
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        if (File.Exists(configPath))
        {
            var backupPath = configPath + $".backup-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(configPath, backupPath, overwrite: true);
            PrintLine($"  Backed up existing config to: {backupPath}");
        }

        SaveConfiguration(config, configPath);
        PrintSuccess($"  Configuration saved to: {configPath}");

        PrintLine();
        PrintSuccess("  Quickstart complete! Starting with --mode desktop will launch the desktop-local API host.");
        PrintLine("  Local API endpoint: http://localhost:8080");
        PrintLine();

        return new WizardResult(Success: true, Config: config, ConfigPath: configPath);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void PrintHeader()
    {
        PrintLine("=".PadRight(60, '='));
        PrintLine("  Meridian - Configuration Wizard");
        PrintLine("=".PadRight(60, '='));
        PrintLine();
    }

    private void PrintNextSteps(string savedPath, DataSourceKind dataSource)
    {
        PrintLine();
        PrintSuccess("=".PadRight(60, '='));
        PrintSuccess("  Configuration Complete!");
        PrintSuccess("=".PadRight(60, '='));
        PrintLine();
        PrintLine($"  Configuration saved to: {savedPath}");
        PrintLine();
        PrintLine("  Next steps:");
        PrintLine("  -".PadRight(40, '-'));
        PrintLine();
        PrintLine("  1. Validate your setup (recommended):");
        PrintLine("     dotnet run --project src/Meridian -- --dry-run");
        PrintLine();
        PrintLine("  2. Start the desktop-local backend:");
        PrintLine("     dotnet run --project src/Meridian -- --mode desktop");
        PrintLine("     Local API endpoint: http://localhost:8080");
        PrintLine();
        PrintLine("  3. Or use quickstart (auto-validates and starts):");
        PrintLine("     dotnet run --project src/Meridian -- --quickstart");
        PrintLine();
        PrintLine("  4. Backfill historical data:");
        PrintLine("     dotnet run --project src/Meridian -- --backfill \\");
        PrintLine("       --backfill-symbols SPY,AAPL --backfill-from 2024-01-01");
        PrintLine();

        var descriptor = ProviderRegistry.Get(dataSource.ToString());
        if (descriptor != null)
        {
            PrintLine("  Credentials (for production, use environment variables):");
            PrintLine($"    See: docs/providers/{dataSource.ToString().ToLowerInvariant()}-setup.md");
        }

        PrintLine();
        PrintLine("  Full documentation: docs/HELP.md");
        PrintLine("  Provider setup:     docs/providers/");
        PrintLine("  Troubleshooting:    dotnet run -- --quick-check");
        PrintLine();
    }

    private void SaveConfiguration(AppConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
        _log.Information("Configuration saved to {Path}", path);
    }

    private void PrintLine(string text = "") => _output.WriteLine(text);

    private void PrintSuccess(string text)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Green;
            _output.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private void PrintWarning(string text)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            _output.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }
}

/// <summary>
/// Result of running the configuration wizard.
/// </summary>
public sealed record WizardResult(
    bool Success,
    AppConfig? Config,
    string? ConfigPath
);

/// <summary>
/// Data source selection from wizard.
/// </summary>
public sealed class DataSourceSelection
{
    public DataSourceKind DataSource { get; set; } = DataSourceKind.IB;
    public AlpacaOptions? Alpaca { get; set; }
    public PolygonOptions? Polygon { get; set; }
    public IBOptions? IB { get; set; }
}
