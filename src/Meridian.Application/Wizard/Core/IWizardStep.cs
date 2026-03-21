namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Contract that every wizard step must implement.
/// Steps are independent units: each one reads from and writes to
/// <see cref="WizardContext"/>, then returns a <see cref="WizardStepResult"/>
/// that tells the <see cref="WizardCoordinator"/> what to do next.
/// </summary>
public interface IWizardStep
{
    /// <summary>The unique identifier of this step in the workflow graph.</summary>
    WizardStepId StepId { get; }

    /// <summary>
    /// Executes the step.
    /// Implementations should not throw for user-facing cancellations — return
    /// <see cref="WizardStepResult.Cancelled"/> instead.
    /// </summary>
    Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct);
}
