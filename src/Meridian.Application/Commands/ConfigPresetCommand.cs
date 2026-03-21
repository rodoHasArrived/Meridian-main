using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --preset CLI command for applying role-based configuration presets.
/// Presets: researcher, daytrader, optionstrader, crypto.
/// </summary>
internal sealed class ConfigPresetCommand : ICliCommand
{
    private readonly AutoConfigurationService _autoConfig;
    private readonly ILogger _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigPresetCommand(AutoConfigurationService autoConfig, ILogger log)
    {
        _autoConfig = autoConfig;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return args.Any(a =>
            a.Equals("--preset", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--list-presets", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (CliArguments.HasFlag(args, "--list-presets"))
        {
            ListPresets();
            return CliResult.Ok();
        }

        var presetName = CliArguments.RequireValue(args, "--preset", "--preset researcher");
        if (presetName is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        if (!TryParsePreset(presetName, out var preset))
        {
            Console.Error.WriteLine($"  Unknown preset: '{presetName}'");
            Console.Error.WriteLine("  Available presets: researcher, daytrader, optionstrader, crypto");
            Console.Error.WriteLine("  Run --list-presets for details.");
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }

        return await ApplyPresetAsync(preset, args, ct);
    }

    private void ListPresets()
    {
        var presets = AutoConfigurationService.GetPresetDescriptions();

        Console.WriteLine();
        Console.WriteLine("  Available Configuration Presets");
        Console.WriteLine("  " + new string('=', 60));

        foreach (var info in presets)
        {
            Console.WriteLine();
            Console.WriteLine($"  {info.Name}  (--preset {info.Preset.ToString().ToLowerInvariant()})");
            Console.WriteLine($"  {info.Description}");
            Console.WriteLine();
            Console.WriteLine("  Key settings:");
            foreach (var setting in info.KeySettings)
            {
                Console.WriteLine($"    - {setting}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Usage: dotnet run -- --preset researcher");
        Console.WriteLine("  Presets can be customized after applying by editing config/appsettings.json");
        Console.WriteLine();
    }

    private async Task<CliResult> ApplyPresetAsync(ConfigPreset preset, string[] args, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"  Applying '{preset}' preset...");

        var config = _autoConfig.ApplyPreset(preset);

        var configPath = CliArguments.GetValue(args, "--output") ?? "config/appsettings.json";
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        // Warn if overwriting
        if (File.Exists(configPath))
        {
            var backupPath = configPath + $".backup.{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(configPath, backupPath, overwrite: true);
            Console.WriteLine($"  Backed up existing config to: {backupPath}");
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct);

        Console.WriteLine($"  Configuration saved to: {configPath}");
        Console.WriteLine();
        Console.WriteLine("  Applied settings:");

        var presetInfo = AutoConfigurationService.GetPresetDescriptions()
            .FirstOrDefault(p => p.Preset == preset);
        if (presetInfo != null)
        {
            foreach (var setting in presetInfo.KeySettings)
            {
                Console.WriteLine($"    - {setting}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Next steps:");
        Console.WriteLine("    1. Review config/appsettings.json and customize if needed");
        Console.WriteLine("    2. Set required environment variables for your providers");
        Console.WriteLine("    3. Run: dotnet run --project src/Meridian");
        Console.WriteLine();

        _log.Information("Applied configuration preset {Preset} to {Path}", preset, configPath);

        return CliResult.Ok();
    }

    private static bool TryParsePreset(string name, out ConfigPreset preset)
    {
        preset = default;
        var normalized = name.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");

        return normalized switch
        {
            "researcher" or "research" => SetAndReturn(ConfigPreset.Researcher, out preset),
            "daytrader" or "trader" or "realtime" => SetAndReturn(ConfigPreset.DayTrader, out preset),
            "optionstrader" or "options" => SetAndReturn(ConfigPreset.OptionsTrader, out preset),
            "crypto" or "cryptocurrency" => SetAndReturn(ConfigPreset.Crypto, out preset),
            _ => false
        };
    }

    private static bool SetAndReturn(ConfigPreset value, out ConfigPreset result)
    {
        result = value;
        return true;
    }
}
