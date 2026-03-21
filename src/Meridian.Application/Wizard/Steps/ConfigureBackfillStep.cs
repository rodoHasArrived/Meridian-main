using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 8: Configures historical data backfill settings.
/// Writes <see cref="WizardContext.Backfill"/> (may remain <c>null</c> if skipped).
/// </summary>
public sealed class ConfigureBackfillStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.ConfigureBackfill;

    public ConfigureBackfillStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 8: Configure Historical Data (Backfill)");
        _output.WriteLine("----------------------------------------");

        var useCase = context.SelectedUseCase ?? UseCase.Development;

        if (useCase == UseCase.RealTimeTrading)
        {
            var enableBackfill = await PromptYesNoAsync("\nEnable historical data backfill", defaultValue: false, ct: ct);
            if (!enableBackfill)
            {
                _output.WriteLine("  Skipping backfill configuration.");
                context.Backfill = null;
                return WizardStepResult.Succeeded();
            }
        }

        _output.WriteLine("\n  Backfill providers allow fetching historical market data.");

        var historicalProviders = context.DetectedProviders
            .Where(p => p.Capabilities.Contains("Historical"))
            .OrderBy(p => p.SuggestedPriority)
            .ToList();

        _output.WriteLine("\n  Available providers (in priority order):");
        foreach (var provider in historicalProviders)
        {
            var status = provider.HasCredentials ? "[OK]" : "[--]";
            _output.WriteLine($"    {status} {provider.DisplayName}");
        }

        var configuredProviders = historicalProviders.Where(p => p.HasCredentials).ToList();
        if (configuredProviders.Count == 0)
            _output.WriteLine("\n  No premium providers configured. Using free providers (Yahoo, Stooq).");

        var priority = configuredProviders.Select(p => p.Name.ToLowerInvariant()).ToList();
        if (!priority.Contains("yahoo"))
            priority.Add("yahoo");
        if (!priority.Contains("stooq"))
            priority.Add("stooq");

        var enableRotation = await PromptYesNoAsync(
            "\nEnable automatic provider rotation on rate limits", defaultValue: true, ct: ct);

        context.Backfill = new BackfillConfig(
            Enabled: useCase is UseCase.BackfillOnly or UseCase.Research,
            Provider: "composite",
            EnableFallback: true,
            EnableRateLimitRotation: enableRotation,
            ProviderPriority: priority.Distinct().ToArray()
        );

        return WizardStepResult.Succeeded();
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
