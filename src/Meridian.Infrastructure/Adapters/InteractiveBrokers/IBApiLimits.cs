namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Constants defining Interactive Brokers API limits and constraints.
/// Based on official TWS API documentation for free equity data access.
/// </summary>
/// <remarks>
/// Free Data Availability for US Equities:
/// - Real-time streaming from Cboe One + IEX (non-consolidated, not NBBO)
/// - Up to 100 free snapshot quotes per month ($0.01 per additional)
/// - Historical data requires active streaming subscription
/// - No delayed data available for US equities
/// - Level 2 / Market Depth requires paid subscription
/// </remarks>
public static class IBApiLimits
{

    /// <summary>
    /// Maximum number of API clients that can connect to a single TWS/Gateway instance.
    /// </summary>
    public const int MaxClientsPerTWS = 32;

    /// <summary>
    /// Maximum messages per second that TWS accepts from all connected clients combined.
    /// </summary>
    public const int MaxMessagesPerSecond = 50;



    /// <summary>
    /// Default maximum number of simultaneous market data subscriptions (Level 1).
    /// Users can request increased limits based on commissions.
    /// </summary>
    public const int DefaultMarketDataLines = 100;

    /// <summary>
    /// Minimum number of market depth (L2) subscriptions allowed.
    /// </summary>
    public const int MinDepthSubscriptions = 3;

    /// <summary>
    /// Maximum number of market depth (L2) subscriptions allowed.
    /// Based on formula tied to account activity.
    /// </summary>
    public const int MaxDepthSubscriptions = 60;



    /// <summary>
    /// Maximum number of simultaneous open historical data requests.
    /// </summary>
    public const int MaxConcurrentHistoricalRequests = 50;

    /// <summary>
    /// Maximum historical data requests allowed within a 10-minute window.
    /// </summary>
    public const int MaxHistoricalRequestsPer10Min = 60;

    /// <summary>
    /// 10-minute window for historical request rate limiting.
    /// </summary>
    public static readonly TimeSpan HistoricalRequestWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Minimum seconds between identical historical data requests.
    /// Making identical requests within this window causes pacing violation.
    /// </summary>
    public const int MinSecondsBetweenIdenticalRequests = 15;

    /// <summary>
    /// Maximum requests for the same Contract/Exchange/TickType within 2 seconds.
    /// Exceeding this causes pacing violation.
    /// </summary>
    public const int MaxSameContractRequestsPer2Sec = 6;

    /// <summary>
    /// 2-second window for same-contract rate limiting.
    /// </summary>
    public static readonly TimeSpan SameContractWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum ticks returned per reqHistoricalTicks request.
    /// </summary>
    public const int MaxHistoricalTicksPerRequest = 1000;

    /// <summary>
    /// Maximum age in months for historical bars with size &lt;= 30 seconds.
    /// Data older than this is not available.
    /// </summary>
    public const int SmallBarMaxAgeMonths = 6;

    /// <summary>
    /// BID_ASK historical requests count as two requests toward rate limits.
    /// </summary>
    public const int BidAskRequestWeight = 2;



    /// <summary>
    /// Minimum seconds between tick-by-tick requests for the same instrument.
    /// </summary>
    public const int MinSecondsBetweenTickByTickSameInstrument = 15;

    /// <summary>
    /// Maximum tick-by-tick subscriptions (uses same formula as depth).
    /// </summary>
    public const int MaxTickByTickSubscriptions = 60;



    /// <summary>
    /// Free snapshot quotes per month ($1.00 value).
    /// </summary>
    public const int FreeSnapshotsPerMonth = 100;

    /// <summary>
    /// Cost per snapshot quote beyond the free allowance.
    /// </summary>
    public const decimal SnapshotCostUSD = 0.01m;



    /// <summary>
    /// Minimum account balance required for market data subscriptions.
    /// </summary>
    public const decimal MinAccountBalanceForMarketData = 500m;



    /// <summary>
    /// TWS Paper Trading connection port.
    /// </summary>
    public const int TwsPaperPort = 7497;

    /// <summary>
    /// TWS Live Trading connection port.
    /// </summary>
    public const int TwsLivePort = 7496;

    /// <summary>
    /// IB Gateway Paper Trading connection port.
    /// </summary>
    public const int GatewayPaperPort = 4002;

    /// <summary>
    /// IB Gateway Live Trading connection port.
    /// </summary>
    public const int GatewayLivePort = 4001;



    /// <summary>
    /// Historical market data service error.
    /// </summary>
    public const int ErrorHistoricalDataService = 162;

    /// <summary>
    /// No security definition found for the contract.
    /// </summary>
    public const int ErrorNoSecurityDefinition = 200;

    /// <summary>
    /// Market data not subscribed (need subscription).
    /// </summary>
    public const int ErrorMarketDataNotSubscribed = 354;

    /// <summary>
    /// Delayed market data not subscribed.
    /// </summary>
    public const int ErrorDelayedDataNotSubscribed = 10167;

    /// <summary>
    /// No market data during competing live session.
    /// </summary>
    public const int ErrorCompetingLiveSession = 10197;

    /// <summary>
    /// Pacing violation - too many requests.
    /// </summary>
    public const int ErrorPacingViolation = 162;

}

/// <summary>
/// Standard tick type IDs returned by reqMktData without genericTickList.
/// </summary>
public static class IBTickTypes
{
    // Standard Tick Types (no generic tick required)
    public const int BidSize = 0;
    public const int BidPrice = 1;
    public const int AskPrice = 2;
    public const int AskSize = 3;
    public const int LastPrice = 4;
    public const int LastSize = 5;
    public const int High = 6;
    public const int Low = 7;
    public const int Volume = 8;
    public const int ClosePrice = 9;
    public const int Open = 14;
    public const int LastTimestamp = 45;
    public const int Halted = 49;

    // Generic Tick Type results
    public const int HistoricalVolatility = 23;
    public const int ImpliedVolatility = 24;
    public const int OptionCallOpenInterest = 27;
    public const int OptionPutOpenInterest = 28;
    public const int OptionCallVolume = 29;
    public const int OptionPutVolume = 30;
    public const int AuctionVolume = 34;
    public const int AuctionPrice = 35;
    public const int AuctionImbalance = 36;
    public const int MarkPrice = 37;
    public const int Shortable = 46;
    public const int RTVolume = 48;
    public const int TradeCount = 54;
    public const int TradeRate = 55;
    public const int VolumeRate = 56;
    public const int LastRTHTrade = 57;
    public const int RTHistoricalVolatility = 58;
    public const int Dividends = 59;
    public const int ShortTermVolume3Min = 63;
    public const int ShortTermVolume5Min = 64;
    public const int ShortTermVolume10Min = 65;
    public const int RTTradeVolume = 77;
    public const int ShortableShares = 89;
}

/// <summary>
/// Generic tick type codes for reqMktData genericTickList parameter.
/// </summary>
public static class IBGenericTickTypes
{
    /// <summary>Option Call/Put Volume (returns tick IDs 29, 30)</summary>
    public const int OptionVolume = 100;

    /// <summary>Option Call/Put Open Interest (returns tick IDs 27, 28)</summary>
    public const int OptionOpenInterest = 101;

    /// <summary>Historical Volatility 30-day (returns tick ID 23)</summary>
    public const int HistoricalVolatility = 104;

    /// <summary>Implied Volatility 30-day (returns tick ID 24)</summary>
    public const int ImpliedVolatility = 106;

    /// <summary>13/26/52 week high/low, avg volume (returns tick IDs 15-21)</summary>
    public const int FundamentalRatios = 165;

    /// <summary>Auction volume, price, imbalance (returns tick IDs 34-36)</summary>
    public const int AuctionData = 225;

    /// <summary>Mark Price (returns tick ID 37)</summary>
    public const int MarkPrice = 232;

    /// <summary>RT Volume - Time &amp; Sales with VWAP (returns tick ID 48)</summary>
    public const int RTVolume = 233;

    /// <summary>Shortable indicator + shares available (returns tick IDs 46, 89)</summary>
    public const int Shortable = 236;

    /// <summary>Trade Count (returns tick ID 54)</summary>
    public const int TradeCount = 293;

    /// <summary>Trade Rate per minute (returns tick ID 55)</summary>
    public const int TradeRate = 294;

    /// <summary>Volume Rate per minute (returns tick ID 56)</summary>
    public const int VolumeRate = 295;

    /// <summary>Last RTH Trade (returns tick ID 57)</summary>
    public const int LastRTHTrade = 318;

    /// <summary>RT Trade Volume (returns tick ID 77)</summary>
    public const int RTTradeVolume = 375;

    /// <summary>Real-time Historical Volatility (returns tick ID 58)</summary>
    public const int RTHistoricalVolatility = 411;

    /// <summary>Dividends (returns tick ID 59)</summary>
    public const int Dividends = 456;

    /// <summary>Short-term volume 3/5/10 min (returns tick IDs 63-65)</summary>
    public const int ShortTermVolume = 595;

    /// <summary>
    /// Default generic tick list for equity streaming with RT Volume and Shortable data.
    /// </summary>
    public const string DefaultEquityGenericTicks = "233,236";

    /// <summary>
    /// Comprehensive generic tick list for equity streaming with all useful ticks.
    /// </summary>
    public const string ComprehensiveEquityGenericTicks = "104,106,165,225,232,233,236,293,294,295,318,375,411,456";
}

/// <summary>
/// Valid duration strings for reqHistoricalData.
/// </summary>
public static class IBDurationStrings
{
    public const string Seconds60 = "60 S";
    public const string Seconds1800 = "1800 S"; // 30 min
    public const string Seconds3600 = "3600 S"; // 1 hour
    public const string Day1 = "1 D";
    public const string Days5 = "5 D";
    public const string Week1 = "1 W";
    public const string Weeks2 = "2 W";
    public const string Month1 = "1 M";
    public const string Months3 = "3 M";
    public const string Months6 = "6 M";
    public const string Year1 = "1 Y";
}

/// <summary>
/// Valid bar size strings for reqHistoricalData.
/// </summary>
public static class IBBarSizes
{
    public const string Secs1 = "1 secs";
    public const string Secs5 = "5 secs";
    public const string Secs10 = "10 secs";
    public const string Secs15 = "15 secs";
    public const string Secs30 = "30 secs";
    public const string Min1 = "1 min";
    public const string Mins2 = "2 mins";
    public const string Mins3 = "3 mins";
    public const string Mins5 = "5 mins";
    public const string Mins10 = "10 mins";
    public const string Mins15 = "15 mins";
    public const string Mins20 = "20 mins";
    public const string Mins30 = "30 mins";
    public const string Hour1 = "1 hour";
    public const string Hours2 = "2 hours";
    public const string Hours3 = "3 hours";
    public const string Hours4 = "4 hours";
    public const string Hours8 = "8 hours";
    public const string Day1 = "1 day";
    public const string Week1 = "1 week";
    public const string Month1 = "1 month";
}

/// <summary>
/// Valid whatToShow values for reqHistoricalData on stocks.
/// </summary>
public static class IBWhatToShow
{
    /// <summary>Trade prices (split-adjusted). Includes volume.</summary>
    public const string Trades = "TRADES";

    /// <summary>Midpoint of bid/ask. No volume.</summary>
    public const string Midpoint = "MIDPOINT";

    /// <summary>Bid prices. No volume.</summary>
    public const string Bid = "BID";

    /// <summary>Ask prices. No volume.</summary>
    public const string Ask = "ASK";

    /// <summary>Time-averaged bid/ask. No volume. Counts as 2 requests.</summary>
    public const string BidAsk = "BID_ASK";

    /// <summary>Dividend + split adjusted prices. Includes volume.</summary>
    public const string AdjustedLast = "ADJUSTED_LAST";

    /// <summary>30-day historical volatility. No volume.</summary>
    public const string HistoricalVolatility = "HISTORICAL_VOLATILITY";

    /// <summary>30-day implied volatility. No volume.</summary>
    public const string OptionImpliedVolatility = "OPTION_IMPLIED_VOLATILITY";

    /// <summary>Trading schedule only.</summary>
    public const string Schedule = "SCHEDULE";
}

/// <summary>
/// Tick-by-tick data types for reqTickByTickData.
/// </summary>
public static class IBTickByTickTypes
{
    /// <summary>Trade ticks only.</summary>
    public const string Last = "Last";

    /// <summary>All trades including combos/derivatives.</summary>
    public const string AllLast = "AllLast";

    /// <summary>Bid/Ask updates.</summary>
    public const string BidAsk = "BidAsk";

    /// <summary>Midpoint price updates.</summary>
    public const string MidPoint = "MidPoint";
}

/// <summary>
/// Maps IB error codes to user-friendly descriptions, severities, and remediation advice.
/// </summary>
public static class IBErrorCodeMap
{
    private static readonly Dictionary<int, IBErrorInfo> Errors = new()
    {
        [100] = new("Max rate of messages exceeded", IBErrorSeverity.Warning, "Reduce request frequency. Max 50 msgs/sec."),
        [110] = new("Price does not conform to minimum variation", IBErrorSeverity.Warning, "Adjust price to valid tick size."),
        [162] = new("Historical market data service error / Pacing violation", IBErrorSeverity.Warning, "Wait 15 seconds before retrying. Max 60 requests per 10 minutes."),
        [200] = new("No security definition found", IBErrorSeverity.Error, "Verify symbol, exchange, and contract details. Use reqContractDetails to validate."),
        [300] = new("Can't find EId with tickerId", IBErrorSeverity.Error, "Subscription ID not recognized. May have been unsubscribed."),
        [354] = new("Market data not subscribed", IBErrorSeverity.Error, "Subscribe to market data in Account Management. Free US equity data requires account >= $500."),
        [502] = new("Couldn't connect to TWS", IBErrorSeverity.Critical, "Ensure TWS or IB Gateway is running. Check host/port (TWS=7496/7497, Gateway=4001/4002)."),
        [504] = new("Not connected", IBErrorSeverity.Critical, "Connection lost. Automatic reconnection will be attempted with exponential backoff."),
        [1100] = new("Connectivity lost to IB server", IBErrorSeverity.Critical, "Internet connection may be down. Data resumes when connectivity restores."),
        [1102] = new("Connectivity restored, data lost", IBErrorSeverity.Warning, "Re-subscribe to market data. Some data may have been missed during outage."),
        [2104] = new("Market data farm connection is OK", IBErrorSeverity.Info, "Normal operation — data feed is healthy."),
        [2106] = new("HMDS data farm connection is OK", IBErrorSeverity.Info, "Historical data is available."),
        [2107] = new("HMDS data farm connection is inactive", IBErrorSeverity.Warning, "Historical data may be temporarily unavailable."),
        [2108] = new("Market data farm is inactive", IBErrorSeverity.Warning, "Real-time data may be temporarily unavailable."),
        [2158] = new("Sec-def data farm connection is OK", IBErrorSeverity.Info, "Normal operation."),
        [10167] = new("Delayed market data not subscribed", IBErrorSeverity.Error, "No delayed data available for US equities. Need live market data subscription."),
        [10197] = new("No market data during competing live session", IBErrorSeverity.Warning, "Another TWS session has market data priority. Close competing sessions."),
    };

    /// <summary>Looks up user-friendly error information for an IB error code.</summary>
    public static IBErrorInfo? GetErrorInfo(int errorCode)
        => Errors.GetValueOrDefault(errorCode);

    /// <summary>Returns all known error codes and their descriptions.</summary>
    public static IReadOnlyDictionary<int, IBErrorInfo> GetAll() => Errors;

    /// <summary>Formats an IB error for display with code, description, and remediation.</summary>
    public static string FormatError(int errorCode, string? rawMessage = null)
    {
        var info = GetErrorInfo(errorCode);
        if (info is null)
            return $"IB Error {errorCode}: {rawMessage ?? "Unknown error"}";

        return $"IB Error {errorCode} [{info.Severity}]: {info.Description}" +
               (info.Remediation is not null ? $" — {info.Remediation}" : "");
    }
}

/// <summary>Detailed information about an IB API error code.</summary>
public sealed record IBErrorInfo(string Description, IBErrorSeverity Severity, string? Remediation);

/// <summary>Severity level for IB API errors.</summary>
public enum IBErrorSeverity : byte { Info, Warning, Error, Critical }

/// <summary>
/// Represents an error from the IB API.
/// </summary>
public sealed record IBApiError(
    int RequestId,
    int ErrorCode,
    string ErrorMessage,
    string? AdvancedOrderRejectJson
)
{
    public bool IsPacingViolation =>
        ErrorCode == IBApiLimits.ErrorPacingViolation ||
        ErrorMessage.Contains("pacing", StringComparison.OrdinalIgnoreCase);

    public bool IsMarketDataError =>
        ErrorCode == IBApiLimits.ErrorMarketDataNotSubscribed ||
        ErrorCode == IBApiLimits.ErrorDelayedDataNotSubscribed;

    public bool IsSecurityNotFound =>
        ErrorCode == IBApiLimits.ErrorNoSecurityDefinition;
}

/// <summary>
/// Base exception for IB API errors.
/// </summary>
public class IBApiException : Exception
{
    public int ErrorCode { get; }

    public IBApiException(int errorCode, string message)
        : base($"IB API Error {errorCode}: {message}")
    {
        ErrorCode = errorCode;
    }

    public IBApiException(int errorCode, string message, Exception innerException)
        : base($"IB API Error {errorCode}: {message}", innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Exception thrown when a pacing violation occurs.
/// </summary>
public sealed class IBPacingViolationException : IBApiException
{
    public IBPacingViolationException(int errorCode, string message)
        : base(errorCode, message)
    {
    }

    /// <summary>
    /// Recommended wait time before retrying.
    /// </summary>
    public TimeSpan RecommendedWait => TimeSpan.FromSeconds(IBApiLimits.MinSecondsBetweenIdenticalRequests);
}

/// <summary>
/// Exception thrown when market data is not subscribed.
/// </summary>
public sealed class IBMarketDataNotSubscribedException : IBApiException
{
    public IBMarketDataNotSubscribedException(int errorCode, string message)
        : base(errorCode, message)
    {
    }
}

/// <summary>
/// Exception thrown when a security definition is not found.
/// </summary>
public sealed class IBSecurityNotFoundException : IBApiException
{
    public IBSecurityNotFoundException(int errorCode, string message)
        : base(errorCode, message)
    {
    }
}
