using System.Diagnostics;

namespace Meridian.Infrastructure.Adapters.Core;

internal static class MarketDataTracing
{
    private static readonly ActivitySource Source = new("Meridian", "1.0.0");

    public static Activity? StartBackfillActivity(string provider, string symbol, string? from, string? to)
    {
        var activity = Source.StartActivity(
            $"Backfill.{provider}",
            ActivityKind.Client);

        activity?.SetTag("backfill.provider", provider);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("backfill.from", from ?? "unspecified");
        activity?.SetTag("backfill.to", to ?? "unspecified");
        activity?.SetTag("operation.type", "backfill");

        return activity;
    }

    public static void RecordError(Activity? activity, Exception ex)
    {
        if (activity == null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddException(ex);
    }

    public static void RecordEventCount(Activity? activity, int count)
    {
        activity?.SetTag("event.count", count);
    }
}
