using System.Diagnostics;
using FluentAssertions;
using Meridian.Platform.Tracing;

namespace Meridian.Tests.Platform;

public sealed class EventTraceContextTests
{
    [Fact]
    public void CaptureCurrent_WithoutActivity_ReturnsDefaultContext()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var context = EventTraceContext.CaptureCurrent();

            context.HasParent.Should().BeFalse();
            context.CorrelationId.Should().BeNull();
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void CaptureCurrent_WithActivity_CapturesTraceId()
    {
        using var source = new ActivitySource("Meridian.Tests.Platform");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Meridian.Tests.Platform",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("capture", ActivityKind.Internal);

        var context = EventTraceContext.CaptureCurrent();

        activity.Should().NotBeNull();
        context.HasParent.Should().BeTrue();
        context.ParentContext.TraceId.Should().Be(activity!.TraceId);
        context.CorrelationId.Should().Be(activity.TraceId.ToString());
    }
}
