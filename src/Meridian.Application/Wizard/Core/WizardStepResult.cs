namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Returned by an <see cref="IWizardStep"/> after execution.
/// Carries the outcome, an optional message, and an optional override for the next step.
/// </summary>
/// <param name="Status">Step execution outcome.</param>
/// <param name="NextStep">
/// When not <c>null</c>, overrides the default transition in the graph.
/// Useful for steps that branch based on user input.
/// </param>
/// <param name="Message">Human-readable message for logging or display.</param>
public sealed record WizardStepResult(
    WizardStepStatus Status,
    WizardStepId? NextStep = null,
    string? Message = null)
{
    /// <summary>Creates a successful result with the default next step.</summary>
    public static WizardStepResult Succeeded(string? message = null) =>
        new(WizardStepStatus.Success, Message: message);

    /// <summary>Creates a successful result that jumps to a specific step.</summary>
    public static WizardStepResult JumpTo(WizardStepId next, string? message = null) =>
        new(WizardStepStatus.Success, NextStep: next, Message: message);

    /// <summary>Creates a skip result with the default next step.</summary>
    public static WizardStepResult Skipped(string? message = null) =>
        new(WizardStepStatus.Skip, Message: message);

    /// <summary>Creates a failure result.</summary>
    public static WizardStepResult Failed(string message) =>
        new(WizardStepStatus.Failure, Message: message);

    /// <summary>Creates a cancellation result.</summary>
    public static WizardStepResult Cancelled(string? message = null) =>
        new(WizardStepStatus.Cancelled, Message: message);
}
