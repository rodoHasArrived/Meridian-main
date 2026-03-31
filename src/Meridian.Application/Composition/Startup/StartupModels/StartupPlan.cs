namespace Meridian.Application.Composition.Startup.StartupModels;

/// <summary>
/// The resolved startup plan: which <see cref="HostMode"/> to execute and the <see cref="StartupContext"/>
/// it operates on. Produced by <see cref="StartupOrchestrator"/> after all pre-run phases have passed.
/// </summary>
public sealed record StartupPlan
{
    /// <summary>The execution mode selected for this process invocation.</summary>
    public required HostMode Mode { get; init; }

    /// <summary>The fully-resolved context the selected mode runner will use.</summary>
    public required StartupContext Context { get; init; }
}
