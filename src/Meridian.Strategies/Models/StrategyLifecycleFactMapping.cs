using Meridian.FSharp.Trading;

namespace Meridian.Strategies.Models;

/// <summary>
/// Associates deterministic F# policy facts with the retained command intent or verified outcome
/// that establishes them. Facts are evidence inside the existing lifecycle snapshot, not commands
/// to execute or a second event stream to replay.
/// </summary>
public static class StrategyLifecycleFactMapping
{
    /// <summary>
    /// Copies facts for <paramref name="eventType"/> in policy order. Repeating a transition does
    /// not duplicate facts. Callers must retain the returned snapshot unchanged when retrying its
    /// append; the run store binds its ordered facts into the snapshot's existing event identity.
    /// </summary>
    public static StrategyRunEntry Attach(
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType eventType,
        params StrategyTransitionDto[] transitions)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(transitions);

        var facts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transition in transitions)
        {
            ArgumentNullException.ThrowIfNull(transition);
            if (!transition.IsValid)
            {
                throw new ArgumentException("An invalid lifecycle verdict cannot establish retained facts.", nameof(transitions));
            }

            foreach (var fact in transition.EmittedFacts ?? [])
            {
                var mappedEvents = MappedEvents(fact);
                if (mappedEvents is null)
                {
                    throw new InvalidOperationException($"Lifecycle fact '{fact}' has no retained event mapping.");
                }

                if ((eventType == StrategyRunLifecycleEventType.EvidencePersistenceFailed || mappedEvents.Contains(eventType)) &&
                    seen.Add(fact))
                {
                    facts.Add(fact);
                }
            }
        }

        return entry with
        {
            LastLifecycleEvent = eventType,
            LifecycleFacts = Array.AsReadOnly(facts.ToArray())
        };
    }

    internal static string? GetValidationError(StrategyRunEntry entry)
    {
        if (entry.LifecycleFacts is null)
        {
            return "the retained lifecycle facts collection is null";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in entry.LifecycleFacts)
        {
            var mappedEvents = MappedEvents(fact);
            if (mappedEvents is null)
            {
                return $"lifecycle fact '{fact}' has no retained event mapping";
            }

            if (entry.LastLifecycleEvent != StrategyRunLifecycleEventType.EvidencePersistenceFailed &&
                !mappedEvents.Contains(entry.LastLifecycleEvent))
            {
                return $"lifecycle fact '{fact}' does not belong to retained event '{entry.LastLifecycleEvent}'";
            }

            if (!seen.Add(fact))
            {
                return $"lifecycle fact '{fact}' is duplicated in one retained event";
            }
        }

        return null;
    }

    private static StrategyRunLifecycleEventType[]? MappedEvents(string fact) => fact switch
    {
        "strategy-start-requested" or "strategy-restart-requested" or "strategy-resume-requested" or
            "strategy-warmup-entered" => [StrategyRunLifecycleEventType.StartRequested],
        "strategy-warmup-completed" or "strategy-running-entered" =>
        [
            StrategyRunLifecycleEventType.Started,
            StrategyRunLifecycleEventType.Activated,
            StrategyRunLifecycleEventType.Recovered,
            StrategyRunLifecycleEventType.Resumed
        ],
        "strategy-paused" => [StrategyRunLifecycleEventType.Paused],
        "strategy-stop-requested" or "strategy-stopping-entered" => [StrategyRunLifecycleEventType.StopRequested],
        "strategy-stopped" => [StrategyRunLifecycleEventType.Completed],
        "strategy-faulted" =>
        [
            StrategyRunLifecycleEventType.StartFailed,
            StrategyRunLifecycleEventType.PauseFailed,
            StrategyRunLifecycleEventType.StopFailed,
            StrategyRunLifecycleEventType.Failed,
            StrategyRunLifecycleEventType.Cancelled
        ],
        _ => null
    };
}
