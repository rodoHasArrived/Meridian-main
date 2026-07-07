using Meridian.Core.Config;
using Meridian.Platform.Results;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles configuration setup CLI commands:
/// --setup, --first-run, --quickstart, --wizard, --auto-config, --detect-providers,
/// --generate-config, --generate-config-schema
/// </summary>
internal sealed class ConfigCommands : ICliCommand
{
    private readonly ConfigurationService _configService;
    private readonly ILogger _log;
    private readonly CliCommandRouteTable _routes;

    public ConfigCommands(ConfigurationService configService, ILogger log)
    {
        _configService = configService;
        _log = log;
        _routes = new CliCommandRouteTable(
            CliCommandRoute.Flag("--wizard", RunWizardAsync),
            CliCommandRoute.Flag("--auto-config", RunAutoConfig),
            CliCommandRoute.When(IsQuickstartAlias, RunQuickstartAsync),
            CliCommandRoute.Flag("--detect-providers", _ => RunDetectProviders()),
            CliCommandRoute.Flag("--generate-config", RunGenerateConfig),
            CliCommandRoute.Flag("--generate-config-schema", RunGenerateConfigSchema),
            CliCommandRoute.Flag("--list-presets", _ => RunListPresets()),
            CliCommandRoute.Flag("--preset", RunApplyPreset));
    }

    public IReadOnlyList<string> Triggers { get; } =
    [
        "--wizard", "--auto-config", "--quickstart", "--setup", "--first-run", "--detect-providers",
        "--generate-config", "--generate-config-schema", "--list-presets", "--preset"
    ];

    public bool CanHandle(string[] args) => _routes.CanHandle(args);

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
        => _routes.ExecuteAsync(args, ct);

    private async Task<CliResult> RunWizardAsync(string[] args, CancellationToken ct)
    {
        _log.Information("Starting configuration wizard...");
        var result = await _configService.RunWizardAsync(ct);
        return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
    }

    private CliResult RunAutoConfig(string[] args)
    {
        _log.Information("Running auto-configuration...");
        var result = _configService.RunAutoConfig();
        return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
    }

    private async Task<CliResult> RunQuickstartAsync(string[] args, CancellationToken ct)
    {
        _log.Information("Running quickstart setup...");
        var result = await _configService.RunQuickstartAsync(ct);
        return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
    }

    private CliResult RunDetectProviders()
    {
        _configService.PrintProviderDetection();
        return CliResult.Ok();
    }

    private static CliResult RunListPresets()
    {
        Console.WriteLine();
        Console.WriteLine("  Available Configuration Presets");
        Console.WriteLine("  " + new string('=', 55));
        Console.WriteLine();

        foreach (var (name, description) in ConfigurationPresets.PresetDescriptions)
        {
            Console.WriteLine($"  {name,-14} {description}");
        }

        Console.WriteLine();
        Console.WriteLine("  Usage: Meridian --preset <name>");
        Console.WriteLine("  Example: Meridian --preset daytrader");
        Console.WriteLine();

        return CliResult.Ok();
    }

    private static bool IsQuickstartAlias(string[] args)
        => CliArguments.HasFlag(args, "--quickstart") ||
            CliArguments.HasFlag(args, "--setup") ||
            CliArguments.HasFlag(args, "--first-run");

    private async Task<CliResult> RunApplyPreset(string[] args, CancellationToken ct = default)
    {
        var presetName = CliArguments.GetValue(args, "--preset");
        if (string.IsNullOrWhiteSpace(presetName))
        {
            Console.Error.WriteLine("Error: --preset requires a preset name");
            Console.Error.WriteLine($"Available: {string.Join(", ", ConfigurationPresets.AvailablePresets)}");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        if (!ConfigurationPresets.PresetDescriptions.ContainsKey(presetName))
        {
            Console.Error.WriteLine($"Error: Unknown preset '{presetName}'");
            Console.Error.WriteLine($"Available: {string.Join(", ", ConfigurationPresets.AvailablePresets)}");
            return CliResult.Fail(ErrorCode.NotFound);
        }

        try
        {
            var currentConfig = _configService.GetConfig();
            var newConfig = ConfigurationPresets.ApplyPreset(presetName, currentConfig);
            await _configService.SaveConfigAsync(newConfig);

            Console.WriteLine();
            Console.WriteLine($"  Applied preset: {presetName}");
            Console.WriteLine($"  Description: {ConfigurationPresets.PresetDescriptions[presetName]}");
            Console.WriteLine();
            Console.WriteLine($"  Data Source: {newConfig.DataSource}");
            Console.WriteLine($"  Symbols: {string.Join(", ", (newConfig.Symbols ?? []).Select(s => s.Symbol))}");
            Console.WriteLine($"  Storage Profile: {newConfig.Storage?.Profile ?? "default"}");
            Console.WriteLine($"  Compression: {(newConfig.Compress == true ? "Enabled" : "Disabled")}");
            Console.WriteLine();
            Console.WriteLine("  Configuration saved. Run without --preset to start collection.");
            Console.WriteLine();

            return CliResult.Ok();
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }
    }

    private CliResult RunGenerateConfig(string[] args)
    {
        var templateName = CliArguments.GetValue(args, "--template") ?? "minimal";
        var outputPath = CliArguments.GetValue(args, "--output") ?? "config/appsettings.generated.json";

        var generator = new ConfigTemplateGenerator();
        var template = generator.GetTemplate(templateName);

        if (template == null)
        {
            Console.Error.WriteLine($"Unknown template: {templateName}");
            Console.Error.WriteLine("Available templates: minimal, full, alpaca, backfill, production, docker");
            return CliResult.Fail(ErrorCode.NotFound);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(outputPath, template.Json);
        Console.WriteLine($"Generated {template.Name} configuration template: {outputPath}");

        if (template.EnvironmentVariables?.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Required environment variables:");
            foreach (var (key, desc) in template.EnvironmentVariables)
                Console.WriteLine($"  {key}: {desc}");
        }

        return CliResult.Ok();
    }

    private static CliResult RunGenerateConfigSchema(string[] args)
    {
        var outputPath = CliArguments.GetValue(args, "--output") ?? "config/appsettings.schema.json";

        var generator = new ConfigJsonSchemaGenerator();
        generator.WriteSchema(outputPath);

        Console.WriteLine($"Generated configuration schema: {outputPath}");
        Console.WriteLine("Use \"$schema\": \"./appsettings.schema.json\" in your config file for IDE validation.");

        return CliResult.Ok();
    }

}
