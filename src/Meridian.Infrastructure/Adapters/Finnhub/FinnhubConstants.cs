namespace Meridian.Infrastructure.Adapters.Finnhub;

/// <summary>
/// HTTP endpoint constants for the Finnhub REST API.
/// </summary>
internal static class FinnhubEndpoints
{
    /// <summary>Base URL for the Finnhub REST API v1.</summary>
    public const string BaseUrl = "https://finnhub.io/api/v1";

    /// <summary>Stock candles (OHLCV) endpoint path.</summary>
    public const string StockCandles = "/stock/candle";

    /// <summary>Stock splits endpoint path.</summary>
    public const string StockSplits = "/stock/split";

    /// <summary>Stock dividends endpoint path.</summary>
    public const string StockDividends = "/stock/dividend";

    /// <summary>Symbol search endpoint path.</summary>
    public const string SymbolSearch = "/search";

    /// <summary>Symbol lookup endpoint path.</summary>
    public const string StockSymbols = "/stock/symbol";
}

/// <summary>
/// HTTP header names used by the Finnhub API.
/// </summary>
internal static class FinnhubHeaders
{
    /// <summary>API key header name.</summary>
    public const string ApiKeyHeader = "X-Finnhub-Token";
}

/// <summary>
/// Supported candle resolution strings for the Finnhub <c>/stock/candle</c> endpoint.
/// </summary>
internal static class FinnhubResolutions
{
    /// <summary>1-minute bars.</summary>
    public const string Min1 = "1";

    /// <summary>5-minute bars.</summary>
    public const string Min5 = "5";

    /// <summary>15-minute bars.</summary>
    public const string Min15 = "15";

    /// <summary>30-minute bars.</summary>
    public const string Min30 = "30";

    /// <summary>60-minute (hourly) bars.</summary>
    public const string Hour1 = "60";

    /// <summary>Daily bars.</summary>
    public const string Daily = "D";

    /// <summary>Weekly bars.</summary>
    public const string Weekly = "W";

    /// <summary>Monthly bars.</summary>
    public const string Monthly = "M";

    /// <summary>All supported resolutions in ascending granularity order.</summary>
    public static readonly IReadOnlyList<string> All = [Min1, Min5, Min15, Min30, Hour1, Daily, Weekly, Monthly];
}

/// <summary>
/// Rate limit constants for the Finnhub API free tier.
/// </summary>
internal static class FinnhubRateLimits
{
    /// <summary>Maximum API calls per rate-limit window on the free plan.</summary>
    public const int MaxRequestsPerWindow = 60;

    /// <summary>Rate-limit window duration.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Recommended minimum delay between consecutive requests (60 req/min = 1 s/req).</summary>
    public static readonly TimeSpan MinRequestDelay = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Status strings returned in the <c>s</c> field of Finnhub candle responses.
/// </summary>
internal static class FinnhubCandleStatus
{
    /// <summary>Data returned successfully.</summary>
    public const string Ok = "ok";

    /// <summary>No data available for the requested symbol/range.</summary>
    public const string NoData = "no_data";
}
