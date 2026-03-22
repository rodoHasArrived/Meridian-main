using System.Diagnostics;

namespace Meridian.Domain.Telemetry;

internal static class MarketEventIngressTracing
{
    private static readonly ActivitySource Source = new("Meridian");

    public static Activity? StartCollectorActivity(string collector, string eventType, string symbol)
    {
        var activity = Source.StartActivity($"{collector}.publish", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag("collector.name", collector);
        activity.SetTag("event.type", eventType);
        activity.SetTag("symbol", symbol);
        return activity;
    }
}
