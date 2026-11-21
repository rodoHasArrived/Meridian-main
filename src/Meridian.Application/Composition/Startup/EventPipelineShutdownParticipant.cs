using Meridian.Application.Pipeline;
using Meridian.Contracts.Lifecycle;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Completes and flushes EventPipeline exactly once. The pipeline flush already drains its channel
/// and flushes the underlying storage sink, so the sink must not be registered separately.
/// </summary>
/// <remarks>
/// When a <see cref="DualPathEventPipeline"/> fronts the pipeline, its ring buffers are drained
/// into the slow path first. Completing the slow-path channel while accepted trades and quotes
/// still sit in those buffers would make every one of them fail to publish and be dropped.
/// </remarks>
public sealed class EventPipelineShutdownParticipant : IRuntimeShutdownParticipant
{
    private readonly EventPipeline _pipeline;
    private readonly DualPathEventPipeline? _hotPath;

    public EventPipelineShutdownParticipant(EventPipeline pipeline, DualPathEventPipeline? hotPath = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _hotPath = hotPath;
    }

    public string Id => "event-pipeline";
    public LifecycleShutdownStage Stage => LifecycleShutdownStage.Draining;
    public int Order => 100;
    public bool IsCritical => true;

    public async ValueTask ExecuteAsync(CancellationToken ct)
    {
        // Stop admission on the hot path and hand everything it accepted to the slow path
        // while the slow-path channel still accepts writes.
        DualPathPipelineStopOutcome? hotPathOutcome = null;
        if (_hotPath is not null)
        {
            hotPathOutcome = await _hotPath.StopAsync(ct).ConfigureAwait(false);
        }

        _pipeline.Complete();
        await _pipeline.FlushAsync(ct).ConfigureAwait(false);

        // Everything that did reach the slow path is flushed above either way; an incomplete
        // hot-path drain is still a failed shutdown step and must be reported as one.
        if (hotPathOutcome is { Succeeded: false })
        {
            throw new InvalidOperationException(
                $"Hot-path drain ended with status {hotPathOutcome.Status}: " +
                $"{hotPathOutcome.TradePending} trade and {hotPathOutcome.QuotePending} quote event(s) pending, " +
                $"{hotPathOutcome.TradeDropped} trade and {hotPathOutcome.QuoteDropped} quote event(s) dropped.",
                hotPathOutcome.Failure);
        }
    }
}
