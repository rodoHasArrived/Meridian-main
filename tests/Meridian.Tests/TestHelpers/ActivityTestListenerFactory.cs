using System.Diagnostics;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Factory for creating ActivityListener instances used in tracing unit tests.
/// </summary>
public static class ActivityTestListenerFactory
{
    public static ActivityListener CreateForMeridianSource()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Meridian",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
