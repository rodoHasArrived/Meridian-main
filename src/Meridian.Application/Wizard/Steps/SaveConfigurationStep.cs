using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 10: Persists the assembled configuration to disk.
/// Writes <see cref="WizardContext.SavedConfigPath"/>.
/// </summary>
public sealed class SaveConfigurationStep : IWizardStep
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.SaveConfiguration;

    public SaveConfigurationStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 10: Save Configuration");
        _output.WriteLine("----------------------------------------");

        var config = context.FinalConfig;
        if (config == null)
            return WizardStepResult.Failed("No configuration to save (ReviewConfigurationStep must run first).");

        var configDir = "config";
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        var configPath = Path.Combine(configDir, "appsettings.json");

        if (File.Exists(configPath))
        {
            var overwrite = await PromptYesNoAsync(
                $"\n{configPath} already exists. Overwrite", defaultValue: false, ct: ct);

            if (!overwrite)
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                configPath = Path.Combine(configDir, $"appsettings.{timestamp}.json");
                _output.WriteLine($"  Saving to: {configPath}");
            }
        }

        WriteConfig(config, configPath);
        context.SavedConfigPath = configPath;

        return WizardStepResult.Succeeded($"Configuration saved to {configPath}");
    }

    internal static void WriteConfig(AppConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }

    private async Task<bool> PromptYesNoAsync(string prompt, bool defaultValue, CancellationToken ct)
    {
        var defaultText = defaultValue ? "Y/n" : "y/N";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _output.Write($"{prompt} [{defaultText}]: ");
            var input = await Task.Run(() => _input.ReadLine(), ct);
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;
            if (input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
            _output.WriteLine("  Please enter 'y' or 'n'");
        }
    }
}
