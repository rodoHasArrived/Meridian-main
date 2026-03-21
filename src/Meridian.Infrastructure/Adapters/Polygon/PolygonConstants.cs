namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// WebSocket endpoint constants for the Polygon.io Market Data API.
/// </summary>
internal static class PolygonEndpoints
{
    /// <summary>Live WebSocket base URI template. Substitute <c>{feed}</c> with a feed name.</summary>
    public const string LiveWssBase = "wss://socket.polygon.io/{feed}";

    /// <summary>Delayed WebSocket base URI template (15-min delayed).</summary>
    public const string DelayedWssBase = "wss://delayed.polygon.io/{feed}";

    /// <summary>
    /// Returns the appropriate live or delayed WebSocket URI for the given feed.
    /// </summary>
    public static Uri WssUri(string feed, bool useDelayed) =>
        new((useDelayed ? DelayedWssBase : LiveWssBase).Replace("{feed}", feed));
}

/// <summary>
/// WebSocket feed names for Polygon.io.
/// </summary>
internal static class PolygonFeeds
{
    /// <summary>US equities feed.</summary>
    public const string Stocks = "stocks";

    /// <summary>US options feed.</summary>
    public const string Options = "options";

    /// <summary>Forex feed.</summary>
    public const string Forex = "forex";

    /// <summary>Crypto feed.</summary>
    public const string Crypto = "crypto";

    /// <summary>Launchpad (enterprise) feed.</summary>
    public const string Launchpad = "launchpad";
}

/// <summary>
/// JSON action names used in Polygon.io WebSocket protocol messages.
/// </summary>
internal static class PolygonActions
{
    /// <summary>Authentication action sent immediately after connect.</summary>
    public const string Auth = "auth";

    /// <summary>Subscribe to one or more event prefixes.</summary>
    public const string Subscribe = "subscribe";

    /// <summary>Unsubscribe from one or more event prefixes.</summary>
    public const string Unsubscribe = "unsubscribe";
}

/// <summary>
/// <c>ev</c> (event) field values in Polygon.io WebSocket messages.
/// </summary>
internal static class PolygonEventTypes
{
    /// <summary>Trade event (stocks).</summary>
    public const string Trade = "T";

    /// <summary>Quote (NBBO) event.</summary>
    public const string Quote = "Q";

    /// <summary>Per-second aggregate bar event.</summary>
    public const string SecondAggregate = "A";

    /// <summary>Per-minute aggregate bar event.</summary>
    public const string MinuteAggregate = "AM";

    /// <summary>Status / control message.</summary>
    public const string Status = "status";

    /// <summary>Connection established status value.</summary>
    public const string StatusConnected = "connected";

    /// <summary>Authentication success status value.</summary>
    public const string StatusAuthSuccess = "auth_success";

    /// <summary>Authentication failure status value.</summary>
    public const string StatusAuthFailed = "auth_failed";
}

/// <summary>
/// Rate limit constants for the Polygon.io Market Data API.
/// </summary>
internal static class PolygonRateLimits
{
    /// <summary>Maximum requests per rate-limit window on the free plan.</summary>
    public const int MaxRequestsPerWindowFree = 5;

    /// <summary>Rate-limit window duration.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Validation constants for Polygon.io API keys.
/// </summary>
internal static class PolygonApiKeyLimits
{
    /// <summary>
    /// Minimum accepted length for a Polygon API key.
    /// Actual keys are typically 32 characters; 20 is accepted for test flexibility.
    /// </summary>
    public const int MinLength = 20;
}
