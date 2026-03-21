namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Identifies each discrete step in the configuration wizard workflow.
/// Steps form a directed graph; transitions are declared in <see cref="WizardTransition"/>.
/// </summary>
public enum WizardStepId : byte
{
    DetectProviders,
    CredentialGuidance,
    SelectUseCase,
    ConfigureDataSource,
    ValidateCredentials,
    ConfigureSymbols,
    ConfigureStorage,
    ConfigureBackfill,
    ReviewConfiguration,
    SaveConfiguration,
    Summary
}
