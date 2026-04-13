using Meridian.Application.Config;
using Meridian.Application.UI;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 10: Persists the assembled configuration to disk.
/// Writes <see cref="WizardContext.SavedConfigPath"/>.
/// </summary>
public sealed class SaveConfigurationStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;
    private readonly Func<string?, ConfigStore> _configStoreFactory;

    public WizardStepId StepId => WizardStepId.SaveConfiguration;

    public SaveConfigurationStep(TextWriter output, TextReader input)
        : this(output, input, static path => new ConfigStore(path))
    {
    }

    internal SaveConfigurationStep(
        TextWriter output,
        TextReader input,
        Func<string?, ConfigStore> configStoreFactory)
    {
        _output = output;
        _input = input;
        _configStoreFactory = configStoreFactory;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 10: Save Configuration");
        _output.WriteLine("----------------------------------------");

        var config = context.FinalConfig;
        if (config == null)
            return WizardStepResult.Failed("No configuration to save (ReviewConfigurationStep must run first).");

        var configPath = _configStoreFactory(null).ConfigPath;

        if (File.Exists(configPath))
        {
            var overwrite = await PromptYesNoAsync(
                $"\n{configPath} already exists. Overwrite", defaultValue: false, ct: ct);

            if (!overwrite)
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var configDirectory = Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory;
                var configFileName = Path.GetFileNameWithoutExtension(configPath);
                var configExtension = Path.GetExtension(configPath);
                configPath = Path.Combine(configDirectory, $"{configFileName}.{timestamp}{configExtension}");
                _output.WriteLine($"  Saving to: {configPath}");
            }
        }

        await WriteConfigAsync(config, configPath, ct);
        context.SavedConfigPath = configPath;

        return WizardStepResult.Succeeded($"Configuration saved to {configPath}");
    }

    internal static Task WriteConfigAsync(AppConfig config, string path, CancellationToken ct = default)
    {
        return new ConfigStore(path).SaveAsync(config, ct);
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
