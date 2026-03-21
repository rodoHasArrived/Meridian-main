using Meridian.Application.Config;
using Meridian.Application.Subscriptions;
using Meridian.Infrastructure;

namespace Meridian.Application.Monitoring;

public sealed record StatusSnapshot(
    DateTimeOffset TimestampUtc,
    long Published,
    long Dropped,
    long Integrity,
    bool IbEnabled,
    int SymbolCount,
    IReadOnlyDictionary<string, int> DepthSubscriptions,
    IReadOnlyDictionary<string, int> TradeSubscriptions
)
{
    public static StatusSnapshot FromRuntime(AppConfig cfg, IMarketDataClient ib, SubscriptionOrchestrator subs, IEventMetrics? metrics = null)
    {
        var m = metrics ?? new DefaultEventMetrics();
        return new(
            TimestampUtc: DateTimeOffset.UtcNow,
            Published: m.Published,
            Dropped: m.Dropped,
            Integrity: m.Integrity,
            IbEnabled: ib.IsEnabled,
            SymbolCount: cfg.Symbols?.Length ?? 0,
            DepthSubscriptions: new Dictionary<string, int>(subs.DepthSubscriptions, StringComparer.OrdinalIgnoreCase),
            TradeSubscriptions: new Dictionary<string, int>(subs.TradeSubscriptions, StringComparer.OrdinalIgnoreCase)
        );
    }
}
