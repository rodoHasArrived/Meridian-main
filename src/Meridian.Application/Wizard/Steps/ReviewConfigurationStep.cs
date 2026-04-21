using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 9: Assembles the final <see cref="AppConfig"/> from wizard context and
/// shows it to the user for review.
/// Writes <see cref="WizardContext.FinalConfig"/>; sets <see cref="WizardContext.IsCancelled"/>
/// if the user declines.
/// </summary>
public sealed class ReviewConfigurationStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.ReviewConfiguration;

    public ReviewConfigurationStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 9: Review Configuration");
        _output.WriteLine("----------------------------------------");

        var config = BuildConfiguration(context);
        context.FinalConfig = config;

        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);
        _output.WriteLine("\nGenerated configuration:\n");
        _output.WriteLine("```json");
        _output.WriteLine(json);
        _output.WriteLine("```");

        var confirmed = await PromptYesNoAsync("\nSave this configuration", defaultValue: true, ct: ct);
        if (!confirmed)
        {
            _output.WriteLine("\nConfiguration cancelled. No changes made.");
            return WizardStepResult.Cancelled("User declined to save configuration.");
        }

        return WizardStepResult.Succeeded();
    }

    // ── Assembly ─────────────────────────────────────────────────────────────

    private static AppConfig BuildConfiguration(WizardContext context)
    {
        var dataSource = context.DataSource ?? new DataSourceSelection();
        return new AppConfig(
            DataRoot: "data",
            Compress: true,
            DataSource: dataSource.DataSource,
            Alpaca: dataSource.Alpaca,
            IB: dataSource.IB,
            IBClientPortal: dataSource.IBClientPortal,
            Polygon: dataSource.Polygon,
            Storage: context.Storage,
            Symbols: context.Symbols,
            Backfill: context.Backfill
        );
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
