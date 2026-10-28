namespace IBApi;

/// <summary>
/// Provides the IB callback marker used by the smoke-test socket stub.
/// </summary>
public interface EWrapper { }

/// <summary>
/// Defines the reader signal contract required by IB integration smoke tests.
/// </summary>
public interface EReaderSignal
{
    void waitForSignal();
}

/// <summary>
/// Supplies a non-blocking, no-op reader signal for IB integration smoke tests.
/// </summary>
public sealed class EReaderMonitorSignal : EReaderSignal
{
    public void waitForSignal() { }
}

/// <summary>
/// Simulates IB socket connection state and accepts API requests without contacting a broker.
/// </summary>
public sealed class EClientSocket
{
    private bool _connected;

    public EClientSocket(EWrapper wrapper, EReaderSignal signal) { }

    public void eConnect(string host, int port, int clientId) => _connected = true;

    public void eDisconnect() => _connected = false;

    public bool IsConnected() => _connected;

    /// <summary>
    /// Server version returned after connection. Returns the maximum tested version in the smoke stub.
    /// </summary>
    public int ServerVersion => 178;

    public void reqCurrentTime() { }

    public void reqMktDepth(int tickerId, Contract contract, int depthLevels, bool isSmartDepth, IList<TagValue>? options) { }

    public void cancelMktDepth(int tickerId, bool isSmartDepth = true) { }

    public void reqTickByTickData(int tickerId, Contract contract, string tickType, int numberOfTicks, bool ignoreSize) { }

    public void cancelTickByTickData(int tickerId) { }

    public void reqMktData(int tickerId, Contract contract, string genericTicks, bool snapshot, bool regulatorySnapshot, IList<TagValue>? options) { }

    public void cancelMktData(int tickerId) { }

    public void reqScannerSubscription(
        int reqId,
        ScannerSubscription subscription,
        IList<TagValue>? scannerSubscriptionOptions,
        IList<TagValue>? scannerSubscriptionFilterOptions)
    { }

    public void reqContractDetails(int reqId, Contract contract) { }

    public void reqSecDefOptParams(
        int reqId,
        string underlyingSymbol,
        string futFopExchange,
        string underlyingSecType,
        int underlyingConId)
    { }

    public void reqHistoricalNews(
        int reqId,
        int conId,
        string providerCodes,
        string startDateTime,
        string endDateTime,
        int totalResults,
        IList<TagValue>? historicalNewsOptions)
    { }

    public void reqNewsArticle(
        int reqId,
        string providerCode,
        string articleId,
        IList<TagValue>? newsArticleOptions)
    { }

    public void reqFundamentalData(
        int reqId,
        Contract contract,
        string reportType,
        IList<TagValue>? fundamentalDataOptions)
    { }

    public void reqPnL(int reqId, string account, string modelCode) { }

    public void reqMarketRule(int marketRuleId) { }

    public void reqMktDepthExchanges() { }

    public void reqHistoricalData(
        int reqId,
        Contract contract,
        string endDateTime,
        string durationStr,
        string barSizeSetting,
        string whatToShow,
        int useRth,
        int formatDate,
        bool keepUpToDate,
        IList<TagValue>? chartOptions)
    { }

    public void cancelHistoricalData(int reqId) { }

    public void reqIds(int numIds) { }

    public void placeOrder(int orderId, Contract contract, Order order) { }

    public void cancelOrder(int orderId, OrderCancel orderCancel) { }

    public void reqAccountSummary(int reqId, string groupName, string tags) { }

    public void cancelAccountSummary(int reqId) { }

    public void reqPositions() { }

    public void cancelPositions() { }

    public void reqOpenOrders() { }
}

/// <summary>
/// Supplies no-op reader startup and message processing for IB integration smoke tests.
/// </summary>
public sealed class EReader
{
    public EReader(EClientSocket socket, EReaderSignal signal) { }

    public void Start() { }

    public void processMsgs() { }
}

/// <summary>
/// Carries IB instrument identifiers and derivative terms for smoke-test requests.
/// </summary>
public sealed class Contract
{
    public int ConId { get; set; }
    public string? Symbol { get; set; }
    public string? SecType { get; set; }
    public string? Exchange { get; set; }
    public string? Currency { get; set; }
    public string? PrimaryExch { get; set; }
    public string? TradingClass { get; set; }
    public string? LocalSymbol { get; set; }

    // Options / futures-options fields
    public double Strike { get; set; }
    public string? Right { get; set; }                    // "C" or "P"
    public string? LastTradeDateOrContractMonth { get; set; }
    public string? Multiplier { get; set; }
}

/// <summary>
/// Carries IB scanner criteria and row limits for smoke-test requests.
/// </summary>
public sealed class ScannerSubscription
{
    public string? Instrument { get; set; }
    public string? LocationCode { get; set; }
    public string? ScanCode { get; set; }
    public int NumberOfRows { get; set; }
    public string? AbovePrice { get; set; }
    public string? AboveVolume { get; set; }
}

/// <summary>
/// Carries IB order instructions for smoke-test requests without submitting them to a broker.
/// </summary>
public sealed class Order
{
    public int OrderId { get; set; }
    public string Action { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public double LmtPrice { get; set; }
    public double AuxPrice { get; set; }
    public string Tif { get; set; } = string.Empty;
    public bool Transmit { get; set; }
    public string OrderRef { get; set; } = string.Empty;
    public bool OutsideRth { get; set; }
    public string Account { get; set; } = string.Empty;
}

/// <summary>
/// Carries order status, fees, and rejection information in the IB smoke-test API.
/// </summary>
public sealed class OrderState
{
    public string Status { get; set; } = string.Empty;
    public double CommissionAndFees { get; set; }
    public string RejectReason { get; set; } = string.Empty;
}

/// <summary>
/// Carries IB fill identifiers, quantities, prices, and account details for smoke tests.
/// </summary>
public sealed class Execution
{
    public int OrderId { get; set; }
    public int ClientId { get; set; }
    public string ExecId { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string AcctNumber { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Shares { get; set; }
    public double Price { get; set; }
    public long PermId { get; set; }
    public decimal CumQty { get; set; }
    public double AvgPrice { get; set; }
    public string OrderRef { get; set; } = string.Empty;
}

/// <summary>
/// Provides the IB cancellation request placeholder required by smoke-test API signatures.
/// </summary>
public sealed class OrderCancel
{
}

/// <summary>
/// Carries timestamped OHLC prices, volume, trade count, and weighted average price for IB smoke tests.
/// </summary>
public sealed class Bar
{
    public string Time { get; set; } = string.Empty;
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public int Count { get; set; }
    public decimal WAP { get; set; }
    public decimal Wap { get => WAP; set => WAP = value; }
}

/// <summary>
/// Carries automatic-execution and price-limit flags for IB smoke-test ticks.
/// </summary>
public sealed class TickAttrib
{
    public bool CanAutoExecute { get; set; }
    public bool PastLimit { get; set; }
}

/// <summary>
/// Provides the last-trade attribute placeholder required by IB smoke-test callbacks.
/// </summary>
public sealed class TickAttribLast { }

/// <summary>
/// Carries bid and ask price-bound flags for IB smoke-test callbacks.
/// </summary>
public sealed class TickAttribBidAsk
{
    public TickAttribBidAsk()
    {
    }

    public TickAttribBidAsk(bool bidPastLow, bool askPastHigh)
    {
        BidPastLow = bidPastLow;
        AskPastHigh = askPastHigh;
    }

    public bool BidPastLow { get; set; }
    public bool AskPastHigh { get; set; }
}

/// <summary>
/// Carries a price-band lower bound and tick increment for IB smoke-test market rules.
/// </summary>
public sealed class PriceIncrement
{
    public PriceIncrement()
    {
    }

    public PriceIncrement(double lowEdge, double increment)
    {
        LowEdge = lowEdge;
        Increment = increment;
    }

    public double LowEdge { get; set; }
    public double Increment { get; set; }
}

/// <summary>
/// Carries instrument metadata, trading hours, and market-rule identifiers for IB smoke tests.
/// </summary>
public sealed class ContractDetails
{
    public Contract Contract { get; set; } = new();
    public string? MarketRuleIds { get; set; }
    public double MinTick { get; set; }
    public string? LongName { get; set; }
    public string? Industry { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public string? TimeZoneId { get; set; }
    public string? TradingHours { get; set; }
    public string? LiquidHours { get; set; }
}

/// <summary>
/// Provides the contract-description placeholder required by IB smoke-test callbacks.
/// </summary>
public sealed class ContractDescription { }

/// <summary>
/// Carries exchange and service metadata for IB smoke-test market-depth descriptions.
/// </summary>
public sealed class DepthMktDataDescription
{
    public string? Exchange { get; set; }
    public string? SecType { get; set; }
    public string? ListingExch { get; set; }
    public string? ServiceDataType { get; set; }
    public int AggGroup { get; set; }
}

/// <summary>
/// Provides the news-provider placeholder required by IB smoke-test callbacks.
/// </summary>
public sealed class NewsProvider { }

/// <summary>
/// Carries a named string option for IB smoke-test API requests.
/// </summary>
public sealed class TagValue
{
    public string? Tag { get; set; }
    public string? Value { get; set; }
}
