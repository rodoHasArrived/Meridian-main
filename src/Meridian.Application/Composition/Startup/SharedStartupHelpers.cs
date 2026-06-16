using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Application.Commands;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;
using DeploymentContext = Meridian.Platform.Runtime.DeploymentContext;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Shared startup helpers extracted from the console entry point so all hosts can reuse the same rules.
/// </summary>
public static class SharedStartupHelpers
{
    private const string ConfigPathEnvVar = "MDC_CONFIG_PATH";

    /// <summary>
    /// Resolves the configuration file path from CLI arguments, environment variables, or defaults.
    /// Priority: <c>--config</c> argument &gt; <c>MDC_CONFIG_PATH</c> env var &gt; <c>appsettings.json</c>.
    /// </summary>
    public static string ResolveConfigPath(CliArguments cliArgs)
    {
        if (!string.IsNullOrWhiteSpace(cliArgs.ConfigPath))
            return cliArgs.ConfigPath;

        var envValue = Environment.GetEnvironmentVariable(ConfigPathEnvVar);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        return ResolveDefaultConfigPath();
    }

    /// <summary>
    /// Performs a minimal configuration load so logging can be initialized before the full startup path runs.
    /// </summary>
    public static AppConfig LoadConfigMinimal(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[Warning] Configuration file not found: {path}");
                Console.Error.WriteLine("Using default configuration. Copy config/appsettings.sample.json to config/appsettings.json to customize.");
                return new AppConfig(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);
            var configuredDataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json, cfg?.DataRoot);
            var resolvedDataRoot = MeridianPathDefaults.ResolveDataRoot(path, configuredDataRoot);
            return (cfg ?? new AppConfig()) with { DataRoot = resolvedDataRoot };
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[Error] Invalid JSON in configuration file: {path}");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            Console.Error.WriteLine("  Troubleshooting:");
            Console.Error.WriteLine("    1. Validate JSON syntax at jsonlint.com");
            Console.Error.WriteLine("    2. Check for trailing commas or missing quotes");
            Console.Error.WriteLine("    3. Compare against appsettings.sample.json");
            Console.Error.WriteLine("    4. Run: dotnet user-secrets init (for sensitive data)");
            return new AppConfig(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));
        }
        catch (UnauthorizedAccessException)
        {
            throw new ConfigurationException(
                $"Access denied reading configuration file: {path}. Check file permissions.",
                path, null);
        }
        catch (IOException ex)
        {
            throw new ConfigurationException(
                $"I/O error reading configuration file: {path}. {ex.Message}",
                path, null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error] Failed to load configuration: {ex.Message}");
            Console.Error.WriteLine("Using default configuration.");
            Console.Error.WriteLine("For detailed help, see HELP.md or run with --help");
            return new AppConfig(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));
        }
    }

    /// <summary>
    /// Resolves deployment and mode selection using the shared deployment context rules.
    /// </summary>
    public static DeploymentContext ResolveDeployment(string[] args, string configPath)
        => DeploymentContext.FromArgs(args, configPath);

    /// <summary>
    /// Applies a default symbol subscription when the runtime configuration omits all symbols.
    /// </summary>
    public static AppConfig EnsureDefaultSymbols(AppConfig cfg)
    {
        if (cfg.Symbols is { Length: > 0 })
            return cfg;

        var fallback = new[] { new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10) };
        return cfg with { Symbols = fallback };
    }

    /// <summary>
    /// Builds the backfill request from configuration plus CLI overrides.
    /// </summary>
    public static BackfillRequest BuildBackfillRequest(AppConfig cfg, CliArguments cliArgs)
    {
        var baseRequest = BackfillRequest.FromConfig(cfg);
        var provider = cliArgs.BackfillProvider ?? baseRequest.Provider;
        var symbols = !string.IsNullOrWhiteSpace(cliArgs.BackfillSymbols)
            ? cliArgs.BackfillSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : baseRequest.Symbols;
        var from = ParseDate(cliArgs.BackfillFrom) ?? baseRequest.From;
        var to = ParseDate(cliArgs.BackfillTo) ?? baseRequest.To;
        var granularity = string.IsNullOrWhiteSpace(cliArgs.BackfillGranularity)
            ? baseRequest.Granularity
            : DataGranularityExtensions.TryParseValue(cliArgs.BackfillGranularity, out var parsedGranularity)
                ? parsedGranularity
                : throw new InvalidOperationException(
                    $"Unsupported backfill granularity '{cliArgs.BackfillGranularity}'. " +
                    "Use one of: Daily, Hourly, 1Min, 5Min, 15Min, 30Min, 4Hour.");

        return new BackfillRequest(provider, symbols.ToArray(), from, to, granularity);
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParse(value, out var date) ? date : null;

    private static string ResolveDefaultConfigPath()
        => DefaultConfigPathResolver.Resolve();
}
