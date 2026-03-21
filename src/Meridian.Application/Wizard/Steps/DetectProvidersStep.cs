using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Metadata;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 1: Scans environment variables and returns the list of detected providers.
/// Writes <see cref="WizardContext.DetectedProviders"/>.
/// </summary>
public sealed class DetectProvidersStep : IWizardStep
{
    private readonly AutoConfigurationService _autoConfig;
    private readonly TextWriter _output;

    public WizardStepId StepId => WizardStepId.DetectProviders;

    public DetectProvidersStep(AutoConfigurationService autoConfig, TextWriter output)
    {
        _autoConfig = autoConfig;
        _output = output;
    }

    public Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        var providers = _autoConfig.DetectAvailableProviders();
        context.DetectedProviders = providers;

        _output.WriteLine();
        _output.WriteLine("Step 1: Detecting Available Providers");
        _output.WriteLine("----------------------------------------");
        _output.WriteLine("\nDetected providers:\n");

        foreach (var provider in providers)
        {
            var statusIcon = provider.HasCredentials ? "[OK]" : "[  ]";
            var statusText = provider.HasCredentials ? "Configured" : "Not configured";
            _output.WriteLine($"  {statusIcon} {provider.DisplayName,-25} {statusText}");

            if (!provider.HasCredentials && provider.MissingCredentials.Length > 0)
                _output.WriteLine($"        Missing: {string.Join(", ", provider.MissingCredentials)}");
        }

        var configuredCount = providers.Count(p => p.HasCredentials);
        _output.WriteLine($"\n  {configuredCount}/{providers.Count} providers configured");

        if (configuredCount == 0)
        {
            _output.WriteLine("\n  No API credentials detected. You can still use free providers (Yahoo, Stooq)");
            _output.WriteLine("  or configure credentials via environment variables:\n");
            _output.WriteLine("    export ALPACA_KEY_ID=your-key-id");
            _output.WriteLine("    export ALPACA_SECRET_KEY=your-secret-key");
        }

        return Task.FromResult(WizardStepResult.Succeeded());
    }
}
