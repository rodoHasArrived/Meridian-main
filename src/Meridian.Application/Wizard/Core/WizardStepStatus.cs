namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Outcome of a single wizard step execution.
/// </summary>
public enum WizardStepStatus : byte
{
    /// <summary>Step completed normally; continue to next step.</summary>
    Success,

    /// <summary>Step failed; the wizard should surface an error.</summary>
    Failure,

    /// <summary>Step was skipped (condition not met); continue to next step.</summary>
    Skip,

    /// <summary>User or code cancelled; stop the wizard immediately.</summary>
    Cancelled
}
