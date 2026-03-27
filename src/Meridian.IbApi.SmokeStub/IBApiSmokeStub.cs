namespace IBApi;

public interface EWrapper { }

public interface EReaderSignal
{
    void waitForSignal();
}

public sealed class EReaderMonitorSignal : EReaderSignal
{
    public void waitForSignal() { }
}

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
        IList<TagValue>? chartOptions) { }

    public void cancelHistoricalData(int reqId) { }
}

public sealed class EReader
{
    public EReader(EClientSocket socket, EReaderSignal signal) { }

    public void Start() { }

    public void processMsgs() { }
}

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

public sealed class TickAttrib
{
    public bool CanAutoExecute { get; set; }
    public bool PastLimit { get; set; }
}

public sealed class TickAttribLast { }

public sealed class ContractDetails { }

public sealed class ContractDescription { }

public sealed class DepthMktDataDescription { }

public sealed class NewsProvider { }

public sealed class TagValue
{
    public string? Tag { get; set; }
    public string? Value { get; set; }
}
