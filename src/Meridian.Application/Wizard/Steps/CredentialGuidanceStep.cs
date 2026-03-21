using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Metadata;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 2: Shows credential setup guidance when no providers are configured.
/// Skipped automatically when at least one provider has credentials.
/// </summary>
public sealed class CredentialGuidanceStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.CredentialGuidance;

    public CredentialGuidanceStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        var configuredCount = context.DetectedProviders.Count(p => p.HasCredentials);

        // Skip if user already has at least one provider
        if (configuredCount > 0)
            return WizardStepResult.Skipped("Providers already configured; skipping credential guidance.");

        _output.WriteLine();
        _output.WriteLine("Step 2: Credential Setup Guide");
        _output.WriteLine("----------------------------------------");
        _output.WriteLine("\n  No API credentials detected. Here's how to get started:");
        _output.WriteLine("  Most providers offer free tiers that are perfect for getting started.\n");
        _output.WriteLine("  Popular providers with free tiers:");

        // Show first three providers that have signup URLs
        foreach (var descriptor in ProviderRegistry.All
            .Where(d => !string.IsNullOrEmpty(d.FreeTierDescription))
            .Take(3))
        {
            _output.WriteLine($"\n    {descriptor.DisplayName}:");
            _output.WriteLine($"      {descriptor.FreeTierDescription}");
            _output.WriteLine($"      Sign up:  {descriptor.SignupUrl}");
        }

        _output.WriteLine("\n  Once you have an API key, set it as an environment variable:");
        _output.WriteLine("  (Copy and paste the relevant lines below)\n");
        _output.WriteLine("    # Alpaca (recommended for real-time data)");
        _output.WriteLine("    export ALPACA_KEY_ID=your-key-id");
        _output.WriteLine("    export ALPACA_SECRET_KEY=your-secret-key\n");
        _output.WriteLine("    # Polygon");
        _output.WriteLine("    export POLYGON_API_KEY=your-api-key\n");
        _output.WriteLine("    # Tiingo (historical data)");
        _output.WriteLine("    export TIINGO_API_TOKEN=your-token\n");
        _output.WriteLine("  Tip: Add these to your ~/.bashrc or ~/.zshrc to persist them.");

        var continueSetup = await PromptYesNoAsync(
            "\n  Continue setup without credentials? (You can add them later)",
            defaultValue: true, ct: ct);

        if (!continueSetup)
        {
            _output.WriteLine("\n  Set your environment variables and re-run: Meridian --wizard");
            return WizardStepResult.Cancelled("User chose not to continue without credentials.");
        }

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
            if (input.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (input.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
            _output.WriteLine("  Please enter 'y' or 'n'");
        }
    }
}
