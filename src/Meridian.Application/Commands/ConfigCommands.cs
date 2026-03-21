using Meridian.Application.Config;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles configuration setup CLI commands:
/// --wizard, --auto-config, --detect-providers, --generate-config, --generate-config-schema
/// </summary>
internal sealed class ConfigCommands : ICliCommand
{
    private readonly ConfigurationService _configService;
    private readonly ILogger _log;

    public ConfigCommands(ConfigurationService configService, ILogger log)
    {
        _configService = configService;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return CliArguments.HasFlag(args, "--wizard") ||
            CliArguments.HasFlag(args, "--auto-config") ||
            CliArguments.HasFlag(args, "--quickstart") ||
            CliArguments.HasFlag(args, "--detect-providers") ||
            CliArguments.HasFlag(args, "--generate-config") ||
            CliArguments.HasFlag(args, "--generate-config-schema") ||
            CliArguments.HasFlag(args, "--preset") ||
            CliArguments.HasFlag(args, "--list-presets");
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (CliArguments.HasFlag(args, "--wizard"))
        {
            _log.Information("Starting configuration wizard...");
            var result = await _configService.RunWizardAsync(ct);
            return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
        }

        if (CliArguments.HasFlag(args, "--auto-config"))
        {
            _log.Information("Running auto-configuration...");
            var result = _configService.RunAutoConfig();
            return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
        }

        if (CliArguments.HasFlag(args, "--quickstart"))
        {
            _log.Information("Running quickstart setup...");
            var result = await _configService.RunQuickstartAsync(ct);
            return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
        }

        if (CliArguments.HasFlag(args, "--detect-providers"))
        {
            _configService.PrintProviderDetection();
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--generate-config"))
        {
            return RunGenerateConfig(args);
        }

        if (CliArguments.HasFlag(args, "--generate-config-schema"))
        {
            return RunGenerateConfigSchema(args);
        }

        if (CliArguments.HasFlag(args, "--list-presets"))
        {
            return RunListPresets();
        }

        if (CliArguments.HasFlag(args, "--preset"))
        {
            return await RunApplyPreset(args);
        }

        return CliResult.Fail(ErrorCode.Unknown);
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
            Console.Error.WriteLine("Available templates: minimal, full, alpaca, stocksharp, backfill, production, docker");
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
