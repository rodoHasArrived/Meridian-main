using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Config;

/// <summary>
/// CLI tool for validating configuration files without starting the collector.
/// Provides detailed validation output and suggestions for fixes.
/// </summary>
public sealed class ConfigValidatorCli
{
    private readonly ILogger _log;
    private readonly ConfigurationPipeline _pipeline;

    public ConfigValidatorCli(ILogger? log = null, ConfigurationPipeline? pipeline = null)
    {
        _log = log ?? LoggingSetup.ForContext<ConfigValidatorCli>();
        _pipeline = pipeline ?? new ConfigurationPipeline(_log);
    }

    /// <summary>
    /// Validates a configuration file and prints results to console.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <returns>0 if valid, 1 if invalid.</returns>
    public int Validate(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    Configuration Validator                            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Step 1: Check file exists
        if (!File.Exists(configPath))
        {
            PrintError($"Configuration file not found: {configPath}");
            PrintSuggestion("Create a configuration file by copying appsettings.sample.json to appsettings.json");
            return 1;
        }

        Console.WriteLine($"  Validating: {Path.GetFullPath(configPath)}");
        Console.WriteLine();

        // Step 2: Parse JSON
        AppConfig? config;
        try
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);

            if (config is null)
            {
                PrintError("Configuration file parsed to null");
                return 1;
            }

            config = PrepareEffectiveConfig(config);
            PrintSuccess("JSON syntax is valid");
        }
        catch (JsonException ex)
        {
            PrintError($"Invalid JSON syntax: {ex.Message}");
            PrintSuggestion("Check for trailing commas, missing quotes, or mismatched brackets");
            PrintSuggestion("Use a JSON validator like jsonlint.com to find the error");
            return 1;
        }
        catch (Exception ex)
        {
            PrintError($"Failed to read configuration: {ex.Message}");
            return 1;
        }

        // Step 3: Validate schema (unified AppConfigValidator covers all checks)
        var validator = new AppConfigValidator();
        var result = validator.Validate(config);

        Console.WriteLine();
        Console.WriteLine("  Validation Results:");
        Console.WriteLine("  " + new string('─', 60));

        if (result.IsValid)
        {
            PrintSuccess("All configuration checks passed!");
            Console.WriteLine();
            PrintConfigSummary(config);
            return 0;
        }

        // Print errors grouped by property
        var errorGroups = result.Errors
            .GroupBy(e => e.PropertyName)
            .OrderBy(g => g.Key);

        foreach (var group in errorGroups)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{group.Key}]");
            Console.ResetColor();

            foreach (var error in group)
            {
                Console.Write("    ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("✗ ");
                Console.ResetColor();
                Console.WriteLine(error.ErrorMessage);

                // Add contextual suggestions
                var suggestion = GetSuggestionForError(error);
                if (!string.IsNullOrEmpty(suggestion))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"      → {suggestion}");
                    Console.ResetColor();
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Total: {result.Errors.Count} error(s)");
        Console.WriteLine();

        // Print environment variable hints
        PrintEnvironmentVariableHints(config);

        return 1;
    }

    /// <summary>
    /// Validates configuration from a JSON string.
    /// </summary>
    public ValidationResult ValidateJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);
            if (config is null)
            {
                return new ValidationResult(new[]
                {
                    new ValidationFailure("", "Configuration parsed to null")
                });
            }

            config = PrepareEffectiveConfig(config);
            var validator = new AppConfigValidator();
            return validator.Validate(config);
        }
        catch (JsonException ex)
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure("JSON", $"Invalid JSON: {ex.Message}")
            });
        }
    }

    private AppConfig PrepareEffectiveConfig(AppConfig config)
    {
        // Route CLI validation through the same override and credential-resolution stages
        // as the main configuration pipeline so env-backed configs validate consistently.
        return _pipeline.Process(config, PipelineOptions.Lenient).Config;
    }

    private void PrintSuccess(string message)
    {
        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("✓ ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    private void PrintError(string message)
    {
        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("✗ ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    private void PrintSuggestion(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"    → {message}");
        Console.ResetColor();
    }

    private void PrintConfigSummary(AppConfig config)
    {
        Console.WriteLine("  Configuration Summary:");
        Console.WriteLine("  " + new string('─', 60));
        Console.WriteLine($"    Data Source:     {config.DataSource}");
        Console.WriteLine($"    Data Root:       {config.DataRoot}");
        Console.WriteLine($"    Symbols:         {config.Symbols?.Length ?? 0} configured");
        Console.WriteLine($"    Compression:     {(config.Compress ?? false ? "Enabled" : "Disabled")}");

        if (config.Storage != null)
        {
            Console.WriteLine($"    Naming:          {config.Storage.NamingConvention}");
            Console.WriteLine($"    Partitioning:    {config.Storage.DatePartition}");
        }

        Console.WriteLine();

        // Symbol summary
        if (config.Symbols?.Length > 0)
        {
            Console.WriteLine("    Symbols:");
            foreach (var sym in config.Symbols.Take(10))
            {
                var flags = new List<string>();
                if (sym.SubscribeTrades)
                    flags.Add("Trades");
                if (sym.SubscribeDepth)
                    flags.Add($"Depth({sym.DepthLevels})");
                Console.WriteLine($"      • {sym.Symbol}: {string.Join(", ", flags)}");
            }

            if (config.Symbols.Length > 10)
            {
                Console.WriteLine($"      ... and {config.Symbols.Length - 10} more");
            }
        }
    }

    private void PrintEnvironmentVariableHints(AppConfig config)
    {
        var hints = new List<string>();

        if (config.DataSource == DataSourceKind.Alpaca)
        {
            var keyId = config.Alpaca?.KeyId;
            var secretKey = config.Alpaca?.SecretKey;

            if (string.IsNullOrEmpty(keyId) || keyId == "__SET_ME__")
            {
                hints.Add("ALPACA_KEY_ID - Your Alpaca API key ID");
            }
            if (string.IsNullOrEmpty(secretKey) || secretKey == "__SET_ME__")
            {
                hints.Add("ALPACA_SECRET_KEY - Your Alpaca API secret key");
            }
        }

        if (hints.Count > 0)
        {
            Console.WriteLine("  Environment Variables Required:");
            Console.WriteLine("  " + new string('─', 60));
            foreach (var hint in hints)
            {
                Console.WriteLine($"    • {hint}");
            }
            Console.WriteLine();
            Console.WriteLine("  Set these environment variables or update appsettings.json");
            Console.WriteLine();
        }
    }

    private string? GetSuggestionForError(ValidationFailure error)
    {
        return error.PropertyName switch
        {
            "DataRoot" => "Set a valid directory path for storing market data",
            "Alpaca.KeyId" => "Set ALPACA_KEY_ID environment variable or update config",
            "Alpaca.SecretKey" => "Set ALPACA_SECRET_KEY environment variable or update config",
            "Alpaca.Feed" => "Use 'iex' for free data or 'sip' for paid subscription",
            var p when p.Contains("Symbol") => "Symbol must be 1-20 uppercase characters",
            var p when p.Contains("DepthLevels") => "Depth levels should be between 1 and 50",
            var p when p.Contains("SecurityType") => "Valid types: STK, OPT, IND_OPT, FUT, FOP, SSF, CASH, FOREX, FX, IND, CFD, BOND, CMDTY, CRYPTO, ETF, FUND, WAR, BAG, MARGIN",
            var p when p.Contains("Currency") => "Valid currencies: USD, EUR, GBP, JPY, CHF, CAD, AUD",
            _ => null
        };
    }
}
