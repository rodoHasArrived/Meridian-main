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

    /// <summary>Dedicated endpoint for the US crypto stream.</summary>
    public static Uri CryptoWssUri(string host) => new($"wss://{host}/v1beta3/crypto/us");

    /// <summary>Dedicated endpoint for the options stream.</summary>
    public static Uri OptionsWssUri(string host) => new($"wss://{host}/v1beta1/options");

    /// <summary>Dedicated endpoint for the news stream.</summary>
    public static Uri NewsWssUri(string host) => new($"wss://{host}/v1beta1/news");
}

/// <summary>
/// REST hosts for the Alpaca Trading API, which also serves the reference lookups (assets,
/// option contracts, corporate-action announcements) that are not on the market-data host.
/// </summary>
/// <remarks>
/// Paper and live credentials are separate key pairs and each authenticates only against its
/// own host: a paper key sent to the live host is answered 401. Reference lookups therefore
/// follow the same trading environment as the brokerage gateway rather than a fixed host.
/// </remarks>
internal static class AlpacaTradingApi
{
    /// <summary>Paper Trading API origin.</summary>
    public const string PaperBaseUrl = "https://paper-api.alpaca.markets";

    /// <summary>Live Trading API origin.</summary>
    public const string LiveBaseUrl = "https://api.alpaca.markets";

    /// <summary>Returns the Trading API origin for the selected environment.</summary>
    public static string BaseUrl(bool useSandbox) => useSandbox ? PaperBaseUrl : LiveBaseUrl;

    /// <summary>
    /// Returns the Trading API origin for reference lookups, in order of authority:
    /// <list type="number">
    ///   <item>an explicit environment (a selected credential record names its own);</item>
    ///   <item><c>ALPACA_TRADING_ENVIRONMENT</c>, when it is set;</item>
    ///   <item>a key ID that identifies itself as a paper key (<c>PK…</c>);</item>
    ///   <item>the live host, where these lookups went before any environment was selectable.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The last two steps apply only when nothing selected an environment. They keep a
    /// deployment that uses live keys and never set the variable on the live host, and stop a
    /// paper key from being sent where it is always refused. A misjudged key reaches the other
    /// Alpaca environment's host and is answered 401; it is never sent anywhere else.
    /// </remarks>
    public static string ResolveBaseUrl(bool? useSandbox, string? keyId)
    {
        if (useSandbox is { } selected)
            return BaseUrl(selected);

        var configured = Meridian.Core.Config.AlpacaCredentialEnvironment.ReadEnvironmentValue(
            Meridian.Core.Config.AlpacaCredentialEnvironment.TradingEnvironmentName);
        if (!string.IsNullOrWhiteSpace(configured))
            return BaseUrl(IsPaper(configured));

        return BaseUrl(IsPaperKeyId(keyId));
    }

    /// <summary>Whether a trading-environment name selects paper (anything that is not live).</summary>
    public static bool IsPaper(string? tradingEnvironment) => !string.Equals(
        Meridian.Core.Config.AlpacaCredentialEnvironment.NormalizeTradingEnvironment(tradingEnvironment),
        Meridian.Core.Config.AlpacaCredentialEnvironment.LiveEnvironment,
        StringComparison.OrdinalIgnoreCase);

    /// <summary>Alpaca issues paper Trading API key IDs with a <c>PK</c> prefix.</summary>
    public static bool IsPaperKeyId(string? keyId) =>
        keyId?.TrimStart().StartsWith("PK", StringComparison.Ordinal) == true;

    /// <summary>Names the environment of a Trading API origin for diagnostics.</summary>
    public static string DescribeEnvironment(string baseUrl) =>
        baseUrl.StartsWith(PaperBaseUrl, StringComparison.OrdinalIgnoreCase) ? "paper" : "live";
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
