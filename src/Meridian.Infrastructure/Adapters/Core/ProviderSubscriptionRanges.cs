namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Centralized subscription ID ranges for each provider to prevent collisions.
///
/// Each provider allocates subscription IDs from a designated range to ensure
/// unique IDs across the application. This prevents confusion when debugging
/// subscription issues and ensures proper unsubscription.
///
/// Range allocation:
/// - 100,000-199,999: Alpaca Markets
/// - 200,000-299,999: Polygon.io
/// - 300,000-399,999: StockSharp
/// - 400,000-499,999: Interactive Brokers
/// - 500,000-599,999: NYSE
/// - 600,000+: Reserved for future providers
///
/// Usage:
/// <code>
/// private readonly SubscriptionManager _subscriptionManager =
///     new(startingId: ProviderSubscriptionRanges.AlpacaStart);
/// </code>
/// </summary>
public static class ProviderSubscriptionRanges
{
    /// <summary>
    /// Starting subscription ID for Alpaca Markets provider (100,000).
    /// </summary>
    public const int AlpacaStart = 100_000;

    /// <summary>
    /// Starting subscription ID for Polygon.io provider (200,000).
    /// </summary>
    public const int PolygonStart = 200_000;

    /// <summary>
    /// Starting subscription ID for StockSharp provider (300,000).
    /// Previously used 200,000 which collided with Polygon.
    /// </summary>
    public const int StockSharpStart = 300_000;

    /// <summary>
    /// Starting subscription ID for Interactive Brokers provider (400,000).
    /// </summary>
    public const int InteractiveBrokersStart = 400_000;

    /// <summary>
    /// Starting subscription ID for NYSE provider (500,000).
    /// </summary>
    public const int NyseStart = 500_000;

    /// <summary>
    /// Starting subscription ID for reserved/future providers (600,000).
    /// </summary>
    public const int ReservedStart = 600_000;

    /// <summary>
    /// Size of each provider's ID range (100,000 IDs per provider).
    /// </summary>
    public const int RangeSize = 100_000;

    /// <summary>
    /// Gets the provider name for a given subscription ID based on its range.
    /// Useful for debugging subscription issues.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID to check.</param>
    /// <returns>The provider name or "Unknown" if not in a known range.</returns>
    public static string GetProviderName(int subscriptionId)
    {
        return subscriptionId switch
        {
            >= AlpacaStart and < PolygonStart => "Alpaca",
            >= PolygonStart and < StockSharpStart => "Polygon",
            >= StockSharpStart and < InteractiveBrokersStart => "StockSharp",
            >= InteractiveBrokersStart and < NyseStart => "InteractiveBrokers",
            >= NyseStart and < ReservedStart => "NYSE",
            >= ReservedStart => "Reserved",
            _ => "Unknown"
        };
    }
}
