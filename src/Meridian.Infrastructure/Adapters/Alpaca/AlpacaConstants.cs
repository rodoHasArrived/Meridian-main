namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// WebSocket endpoint constants for the Alpaca Market Data API.
/// </summary>
internal static class AlpacaEndpoints
{
    /// <summary>Live market data WebSocket host.</summary>
    public const string LiveHost = "stream.data.alpaca.markets";

    /// <summary>Paper/sandbox WebSocket host.</summary>
    public const string SandboxHost = "stream.data.sandbox.alpaca.markets";

    /// <summary>
    /// Returns the WebSocket URI for the given host and data feed.
    /// </summary>
    public static Uri WssUri(string host, string feed) =>
        new($"wss://{host}/v2/{feed}");
}

/// <summary>
/// JSON action names used in Alpaca WebSocket protocol messages.
/// </summary>
internal static class AlpacaActions
{
    /// <summary>Authentication action sent immediately after connect.</summary>
    public const string Auth = "auth";

    /// <summary>Subscribe to one or more channels.</summary>
    public const string Subscribe = "subscribe";

    /// <summary>Unsubscribe from one or more channels.</summary>
    public const string Unsubscribe = "unsubscribe";
}

/// <summary>
/// Message type values returned in the <c>T</c> field of Alpaca WebSocket messages.
/// </summary>
internal static class AlpacaMessageTypes
{
    /// <summary>Trade event.</summary>
    public const string Trade = "t";

    /// <summary>Quote (BBO) event.</summary>
    public const string Quote = "q";

    /// <summary>Minute aggregate bar event.</summary>
    public const string MinuteBar = "b";

    /// <summary>Daily aggregate bar event.</summary>
    public const string DailyBar = "d";

    /// <summary>Updated daily aggregate bar event.</summary>
    public const string UpdatedBar = "u";

    /// <summary>Trade correction event.</summary>
    public const string TradeCorrection = "x";

    /// <summary>Trade cancel event.</summary>
    public const string TradeCancel = "tc";

    /// <summary>Authentication response.</summary>
    public const string Success = "success";

    /// <summary>Subscription acknowledgement response.</summary>
    public const string Subscription = "subscription";

    /// <summary>Error response.</summary>
    public const string Error = "error";
}

/// <summary>
/// Rate limit constants for the Alpaca Market Data API.
/// </summary>
internal static class AlpacaRateLimits
{
    /// <summary>Maximum requests per rate-limit window (free / broker data plan).</summary>
    public const int MaxRequestsPerWindow = 200;

    /// <summary>Rate-limit window duration.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Recommended minimum delay between consecutive requests.</summary>
    public static readonly TimeSpan MinRequestDelay = TimeSpan.FromMilliseconds(300);
}

/// <summary>
/// Deduplication window size for content-based trade deduplication.
/// Alpaca can re-deliver identical trade messages during reconnections.
/// </summary>
internal static class AlpacaDedupLimits
{
    /// <summary>Maximum recent-trade entries tracked for deduplication.</summary>
    public const int MaxWindowSize = 2048;
}
