using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Orchestrates execution of a wizard step graph.
/// Steps are registered via <see cref="AddStep"/>;
/// transitions are declared via <see cref="AddTransition"/>.
/// Call <see cref="RunAsync"/> to execute the graph from the entry step.
/// </summary>
public sealed class WizardCoordinator
{
    private readonly ILogger _log = LoggingSetup.ForContext<WizardCoordinator>();
    private readonly Dictionary<WizardStepId, IWizardStep> _steps = new();
    private readonly List<WizardTransition> _transitions = new();

    /// <summary>Registers a step implementation.</summary>
    public WizardCoordinator AddStep(IWizardStep step)
    {
        _steps[step.StepId] = step;
        return this;
    }

    /// <summary>Adds a directed transition between two steps.</summary>
    public WizardCoordinator AddTransition(WizardStepId from, WizardStepId to,
        Func<WizardContext, bool>? condition = null)
    {
        _transitions.Add(new WizardTransition(from, to, condition));
        return this;
    }

    /// <summary>
    /// Runs the step graph starting at <paramref name="entryStep"/> and returns the
    /// final <see cref="WizardContext"/> after all steps have been executed.
    /// </summary>
    public async Task<WizardContext> RunAsync(
        WizardContext context,
        WizardStepId entryStep,
        CancellationToken ct = default)
    {
        var currentStepId = (WizardStepId?)entryStep;

        while (currentStepId.HasValue)
        {
            ct.ThrowIfCancellationRequested();

            if (context.IsCancelled)
            {
                _log.Information("Wizard cancelled at step {Step}", currentStepId);
                break;
            }

            if (!_steps.TryGetValue(currentStepId.Value, out var step))
            {
                _log.Warning("No step registered for {StepId}; stopping.", currentStepId);
                break;
            }

            _log.Debug("Executing wizard step {StepId}", currentStepId);

            WizardStepResult result;
            try
            {
                result = await step.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                context.IsCancelled = true;
                _log.Information("Wizard step {StepId} was cancelled", currentStepId);
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Wizard step {StepId} threw an unhandled exception", currentStepId);
                break;
            }

            _log.Debug("Step {StepId} completed with status {Status}", currentStepId, result.Status);

            if (result.Status == WizardStepStatus.Cancelled)
            {
                context.IsCancelled = true;
                break;
            }

            if (result.Status == WizardStepStatus.Failure)
            {
                _log.Warning("Wizard step {StepId} failed: {Message}", currentStepId, result.Message);
                break;
            }

            // Determine next step: step can override, or fall back to declared transition
            if (result.NextStep.HasValue)
            {
                currentStepId = result.NextStep.Value;
            }
            else
            {
                currentStepId = ResolveTransition(currentStepId.Value, context);
            }
        }

        return context;
    }

    /// <summary>
    /// Resolves the next step using declared transitions.
    /// Returns <c>null</c> when no matching transition is found (end of graph).
    /// </summary>
    private WizardStepId? ResolveTransition(WizardStepId current, WizardContext context)
    {
        foreach (var transition in _transitions.Where(t => t.From == current))
        {
            if (transition.Applies(context))
                return transition.To;
        }
        return null;
    }
}
