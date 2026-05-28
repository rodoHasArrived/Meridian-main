using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Synthetic;

namespace Meridian.Tests.Infrastructure.Providers;

internal static class SyntheticProviderTestHarness
{
    public static SyntheticMarketDataConfig BuildScenarioConfig(
        int throttleEveryNCalls = 0,
        int timeoutEveryNCalls = 0,
        int degradeEveryNEvents = 0,
        int replayBarsLimit = 0,
        int backfillBarsLimit = 0,
        int throttleDelayMs = 20)
        => new(
            Enabled: true,
            EventsPerSecond: 40,
            HistoricalTradeDensityPerDay: 24,
            HistoricalQuoteDensityPerDay: 24,
            Scenario: new SyntheticScenarioConfig(
                Enabled: true,
                ThrottleEveryNCalls: throttleEveryNCalls,
                TimeoutEveryNCalls: timeoutEveryNCalls,
                DegradeEveryNEvents: degradeEveryNEvents,
                ReplayBarsLimit: replayBarsLimit,
                BackfillBarsLimit: backfillBarsLimit,
                ThrottleDelayMs: throttleDelayMs,
                ApplyToHistorical: true,
                ApplyToStreaming: true));

    public static SyntheticHistoricalDataProvider CreateHistorical(SyntheticMarketDataConfig? config = null)
        => new(config ?? BuildScenarioConfig());
}
