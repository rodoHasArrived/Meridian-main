using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;
using Meridian.Storage;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 7: Lets the user pick a storage profile or configure advanced settings.
/// Writes <see cref="WizardContext.Storage"/>.
/// </summary>
public sealed class ConfigureStorageStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.ConfigureStorage;

    public ConfigureStorageStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 7: Configure Storage");
        _output.WriteLine("----------------------------------------");
        _output.WriteLine("\n  Storage profiles provide pre-configured settings for common use cases.\n");

        var presets = StorageProfilePresets.GetPresets();
        _output.WriteLine("  Available storage profiles:");
        for (var i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            var isDefault = preset.Id == StorageProfilePresets.DefaultProfile;
            _output.WriteLine($"    {i + 1}. {preset.Label}{(isDefault ? " (recommended)" : "")}");
            _output.WriteLine($"       {preset.Description}");
        }
        _output.WriteLine($"    {presets.Count + 1}. Custom - configure individual settings manually");

        var useCase = context.SelectedUseCase ?? UseCase.Development;
        var defaultChoice = useCase switch
        {
            UseCase.Development => 1,
            UseCase.Research => 1,
            UseCase.RealTimeTrading => 2,
            UseCase.Production => 3,
            _ => 1
        };

        var profileChoice = await PromptChoiceAsync("Select profile", 1, presets.Count + 1, defaultChoice, ct);

        StorageConfig storage;
        if (profileChoice > presets.Count)
        {
            storage = await ConfigureAdvancedStorageAsync(useCase, ct);
        }
        else
        {
            var selectedProfile = presets[profileChoice - 1];
            _output.WriteLine($"\n  Selected profile: {selectedProfile.Label}");

            var customize = await PromptYesNoAsync("\nCustomize advanced settings", defaultValue: false, ct: ct);
            if (customize)
            {
                storage = await ConfigureAdvancedStorageAsync(useCase, ct, selectedProfile.Id);
            }
            else
            {
                int? retentionDays = useCase switch
                {
                    UseCase.Development => 30,
                    UseCase.Production => 365,
                    _ => null
                };
                storage = new StorageConfig(Profile: selectedProfile.Id, RetentionDays: retentionDays);
            }
        }

        context.Storage = storage;
        return WizardStepResult.Succeeded();
    }

    private async Task<StorageConfig> ConfigureAdvancedStorageAsync(UseCase useCase, CancellationToken ct, string? baseProfile = null)
    {
        _output.WriteLine("\n  Advanced storage configuration:\n");

        _output.WriteLine("  Naming convention:");
        _output.WriteLine("    1. BySymbol - data/SPY/trades/2024-01-15.jsonl");
        _output.WriteLine("    2. ByDate - data/2024-01-15/SPY/trades.jsonl");
        _output.WriteLine("    3. ByType - data/trades/SPY/2024-01-15.jsonl");
        _output.WriteLine("    4. Flat - data/SPY_trades_2024-01-15.jsonl");

        var namingChoice = await PromptChoiceAsync("Select naming", 1, 4, 1, ct);
        var naming = namingChoice switch { 1 => "BySymbol", 2 => "ByDate", 3 => "ByType", 4 => "Flat", _ => "BySymbol" };

        _output.WriteLine("\n  Date partitioning:");
        _output.WriteLine("    1. Daily - new file each day");
        _output.WriteLine("    2. Hourly - new file each hour");
        _output.WriteLine("    3. Monthly - new file each month");
        _output.WriteLine("    4. None - single file per symbol/type");

        var partitionChoice = await PromptChoiceAsync("Select partitioning", 1, 4, 1, ct);
        var partition = partitionChoice switch { 1 => "Daily", 2 => "Hourly", 3 => "Monthly", 4 => "None", _ => "Daily" };

        int? retentionDays = useCase switch { UseCase.Development => 30, UseCase.Production => 365, _ => null };

        if (useCase != UseCase.Development)
        {
            var setRetention = await PromptYesNoAsync("\nSet data retention policy", defaultValue: false, ct: ct);
            if (setRetention)
            {
                var daysStr = await PromptStringAsync("Retention days (e.g., 365)", defaultValue: "365", ct: ct);
                retentionDays = int.TryParse(daysStr, out var d) ? d : null;
            }
        }
        else
        {
            _output.WriteLine($"\n  Using {retentionDays}-day retention for development.");
        }

        return new StorageConfig(NamingConvention: naming, DatePartition: partition,
            RetentionDays: retentionDays, Profile: baseProfile);
    }

    // ── Prompt helpers ───────────────────────────────────────────────────────

    private async Task<int> PromptChoiceAsync(string prompt, int min, int max, int defaultValue, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _output.Write($"\n{prompt} [{min}-{max}] (default: {defaultValue}): ");
            var input = await Task.Run(() => _input.ReadLine(), ct);
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;
            if (int.TryParse(input, out var value) && value >= min && value <= max)
                return value;
            _output.WriteLine($"  Please enter a number between {min} and {max}");
        }
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

    private async Task<string?> PromptStringAsync(string prompt, bool required = false, string? defaultValue = null, CancellationToken ct = default)
    {
        var defaultText = defaultValue != null ? $" (default: {defaultValue})" : "";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _output.Write($"{prompt}{defaultText}: ");
            var input = await Task.Run(() => _input.ReadLine(), ct);
            if (string.IsNullOrWhiteSpace(input))
            {
                if (defaultValue != null)
                    return defaultValue;
                if (!required)
                    return null;
                _output.WriteLine("  This field is required");
                continue;
            }
            return input;
        }
    }
}
