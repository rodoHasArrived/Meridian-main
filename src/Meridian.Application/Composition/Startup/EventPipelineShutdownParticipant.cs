using Meridian.Application.Pipeline;
using Meridian.Contracts.Lifecycle;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Completes and flushes EventPipeline exactly once. The pipeline flush already drains its channel
/// and flushes the underlying storage sink, so the sink must not be registered separately.
/// </summary>
public sealed class EventPipelineShutdownParticipant : IRuntimeShutdownParticipant
{
    private readonly EventPipeline _pipeline;

    public EventPipelineShutdownParticipant(EventPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public string Id => "event-pipeline";
    public LifecycleShutdownStage Stage => LifecycleShutdownStage.Draining;
    public int Order => 100;
    public bool IsCritical => true;

    public async ValueTask ExecuteAsync(CancellationToken ct)
    {
        _pipeline.Complete();
        await _pipeline.FlushAsync(ct).ConfigureAwait(false);
    }
}
