namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Declares a conditional edge in the wizard step graph.
/// </summary>
/// <param name="From">Source step.</param>
/// <param name="To">Target step when <paramref name="Condition"/> is satisfied (or null = always).</param>
/// <param name="Condition">
/// Optional predicate evaluated against the current <see cref="WizardContext"/>.
/// When <c>null</c> the transition is unconditional.
/// </param>
public sealed record WizardTransition(
    WizardStepId From,
    WizardStepId To,
    Func<WizardContext, bool>? Condition = null)
{
    /// <summary>Returns true if this transition applies to the given context.</summary>
    public bool Applies(WizardContext context) => Condition == null || Condition(context);
}
