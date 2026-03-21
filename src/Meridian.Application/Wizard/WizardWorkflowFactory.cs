using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Steps;

namespace Meridian.Application.Wizard;

/// <summary>
/// Builds the default interactive configuration workflow graph and returns a
/// pre-wired <see cref="WizardCoordinator"/> ready to run from
/// <see cref="WizardStepId.DetectProviders"/>.
/// </summary>
public static class WizardWorkflowFactory
{
    /// <summary>
    /// Creates the interactive (CLI) wizard coordinator.
    /// </summary>
    public static WizardCoordinator CreateInteractive(
        AutoConfigurationService autoConfig,
        TextWriter output,
        TextReader input)
    {
        var coordinator = new WizardCoordinator();

        // ── Register steps ───────────────────────────────────────────────────

        coordinator
            .AddStep(new DetectProvidersStep(autoConfig, output))
            .AddStep(new CredentialGuidanceStep(output, input))
            .AddStep(new SelectUseCaseStep(output, input))
            .AddStep(new ConfigureDataSourceStep(output, input))
            .AddStep(new ValidateCredentialsStep(output, input))
            .AddStep(new ConfigureSymbolsStep(output, input))
            .AddStep(new ConfigureStorageStep(output, input))
            .AddStep(new ConfigureBackfillStep(output, input))
            .AddStep(new ReviewConfigurationStep(output, input))
            .AddStep(new SaveConfigurationStep(output, input));

        // ── Declare transitions (linear flow) ────────────────────────────────

        coordinator
            .AddTransition(WizardStepId.DetectProviders, WizardStepId.CredentialGuidance)
            .AddTransition(WizardStepId.CredentialGuidance, WizardStepId.SelectUseCase)
            .AddTransition(WizardStepId.SelectUseCase, WizardStepId.ConfigureDataSource)
            .AddTransition(WizardStepId.ConfigureDataSource, WizardStepId.ValidateCredentials)
            .AddTransition(WizardStepId.ValidateCredentials, WizardStepId.ConfigureSymbols)
            .AddTransition(WizardStepId.ConfigureSymbols, WizardStepId.ConfigureStorage)
            .AddTransition(WizardStepId.ConfigureStorage, WizardStepId.ConfigureBackfill)
            .AddTransition(WizardStepId.ConfigureBackfill, WizardStepId.ReviewConfiguration)
            .AddTransition(WizardStepId.ReviewConfiguration, WizardStepId.SaveConfiguration);
        // SaveConfiguration → end (no further transition)

        return coordinator;
    }
}
