using System.Diagnostics;

namespace Meridian.Application.Tracing;

/// <summary>
/// Captures the trace context present when a market event enters the pipeline so
/// it can be restored after queueing on a different thread.
/// </summary>
public readonly record struct EventTraceContext(
    ActivityContext ParentContext,
    string? CorrelationId)
{
    public bool HasParent => ParentContext.TraceId != default;

    public static EventTraceContext CaptureCurrent()
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            return default;
        }

        return new EventTraceContext(
            activity.Context,
            activity.TraceId.ToString());
    }
}
