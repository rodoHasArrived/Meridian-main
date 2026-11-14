using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Tests.TestHelpers;

/// <summary>Isolates recorded-response fixtures while retaining production's 12-second pacing.</summary>
internal static class AlphaVantageTestBudget
{
    public static RateLimiterRegistry Create()
        => TwelveDataTestBudget.Create(automaticDelayLimit: TimeSpan.FromSeconds(20));
}
