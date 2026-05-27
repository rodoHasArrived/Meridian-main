using System.Diagnostics;

namespace Meridian.Infrastructure.Adapters.Core;

internal static class MarketDataTracing
{
    private static readonly ActivitySource Source = new("Meridian");

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

    public static Activity? StartBackfillFetchActivity(string provider, string symbol)
    {
        var activity = Source.StartActivity(
            $"BackfillFetch.{provider}",
            ActivityKind.Client);

        activity?.SetTag("backfill.provider", provider);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "backfill_fetch");

        return activity;
    }

    public static Activity? StartBackfillStorageActivity(string symbol, int barCount)
    {
        var activity = Source.StartActivity(
            "BackfillStorage.WriteBars",
            ActivityKind.Producer);

        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("event.count", barCount);
        activity?.SetTag("operation.type", "backfill_storage_write");

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
