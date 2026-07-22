using System.Collections.Concurrent;
using Meridian.Contracts.Configuration;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>IB data access class returned by TWS/Gateway for a request.</summary>
public enum IBMarketDataAvailability
{
    Unknown = 0,
    Live = 1,
    Frozen = 2,
    Delayed = 3,
    DelayedFrozen = 4
}

/// <summary>
/// Immutable, entitlement-aware evidence for an IB request or subscription. Persist this with
/// downstream observations: exchange and data availability are part of the meaning of IB data.
/// </summary>
public sealed record IBDataLineage(
    int RequestId,
    string Service,
    string Symbol,
    string? Exchange,
    string? MarketRuleIds,
    string? MinimumIncrements,
    string? Subscription,
    IBMarketDataAvailability Availability,
    bool IsDelayed,
    string Status,
    DateTimeOffset ObservedAt);

/// <summary>Scanner criteria intentionally limited to IB's stable scanner subscription fields.</summary>
public sealed record IBScannerRequest(
    string Instrument,
    string LocationCode,
    string ScanCode,
    int NumberOfRows = 50,
    string? AbovePrice = null,
    string? AboveVolume = null);

/// <summary>
/// Compile-neutral IB data-service transport. The connection manager supplies the vendor calls in
/// an IBAPI build; tests can supply a deterministic transport without an official SDK assembly.
/// </summary>
public interface IIBDataServiceTransport
{
    void RequestScanner(int requestId, IBScannerRequest request);
    void RequestContractDetails(int requestId, SymbolConfig contract);
    void RequestOptionChain(int requestId, SymbolConfig underlying);
    void RequestHistoricalNews(int requestId, int conId, string providerCodes, DateTimeOffset start, DateTimeOffset end, int maximumResults);
    void RequestNewsArticle(int requestId, string providerCode, string articleId);
    void RequestFundamentals(int requestId, SymbolConfig contract, string reportType);
    void RequestDividendEarnings(int requestId, SymbolConfig contract);
    void RequestTickByTick(int requestId, SymbolConfig contract, string tickType, int numberOfTicks, bool ignoreSize);
    void RequestPnl(int requestId, string account, string? modelCode);
    void RequestMarketRule(int requestId, int marketRuleId);
    void RequestDepthExchanges(int requestId);
}

/// <summary>Optional runtime callback source for automatically captured IB entitlement evidence.</summary>
public interface IIBDataLineageSource
{
    event EventHandler<IBMarketDataTypeUpdate>? MarketDataTypeReceived;
}

/// <summary>IB's actual live/frozen/delayed classification for a request.</summary>
public sealed record IBMarketDataTypeUpdate(int RequestId, int MarketDataType);

/// <summary>
/// Issues IB's entitlement-sensitive discovery, reference, news, and richer market-data requests
/// while retaining request lineage. This surface never fabricates availability: callers begin at
/// <see cref="IBMarketDataAvailability.Unknown"/> until TWS/Gateway reports a data type.
/// </summary>
public sealed class IBDataServices
{
    private readonly IIBDataServiceTransport _transport;
    private readonly ConcurrentDictionary<int, IBDataLineage> _lineage = new();
    private int _nextRequestId = 90_000;

    public IBDataServices(IIBDataServiceTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (transport is IIBDataLineageSource source)
            source.MarketDataTypeReceived += OnMarketDataTypeReceived;
    }

    /// <summary>Raised after request, status, or contract-lineage evidence changes.</summary>
    public event Action<IBDataLineage>? LineageUpdated;

    /// <summary>Returns the current lineage evidence in stable request-id order.</summary>
    public IReadOnlyList<IBDataLineage> GetLineage() => _lineage.Values.OrderBy(x => x.RequestId).ToArray();

    public int RequestScanner(IBScannerRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Instrument) || string.IsNullOrWhiteSpace(request.LocationCode) || string.IsNullOrWhiteSpace(request.ScanCode))
            throw new ArgumentException("IB scanner instrument, location code, and scan code are required.", nameof(request));
        if (request.NumberOfRows is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(request), "IB scanner row count must be between 1 and 50.");

        return Issue("scanner", request.Instrument, request.LocationCode, request.ScanCode, id => _transport.RequestScanner(id, request), ct);
    }

    public int RequestContractDetails(SymbolConfig contract, CancellationToken ct = default)
        => Issue("contract-details", RequireSymbol(contract), contract.Exchange, null, id => _transport.RequestContractDetails(id, contract), ct);

    public int RequestOptionChain(SymbolConfig underlying, CancellationToken ct = default)
        => Issue("option-chain", RequireSymbol(underlying), underlying.Exchange, null, id => _transport.RequestOptionChain(id, underlying), ct);

    public int RequestHistoricalNews(int conId, string providerCodes, DateTimeOffset start, DateTimeOffset end, int maximumResults = 100, CancellationToken ct = default)
    {
        if (conId <= 0) throw new ArgumentOutOfRangeException(nameof(conId));
        if (string.IsNullOrWhiteSpace(providerCodes)) throw new ArgumentException("At least one IB news provider code is required.", nameof(providerCodes));
        if (start > end) throw new ArgumentException("News start must not be after end.", nameof(start));
        if (maximumResults is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        return Issue("historical-news", conId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, providerCodes, id => _transport.RequestHistoricalNews(id, conId, providerCodes, start, end, maximumResults), ct);
    }

    public int RequestNewsArticle(string providerCode, string articleId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerCode) || string.IsNullOrWhiteSpace(articleId)) throw new ArgumentException("IB news provider code and article id are required.");
        return Issue("news-article", articleId, null, providerCode, id => _transport.RequestNewsArticle(id, providerCode, articleId), ct);
    }

    public int RequestFundamentals(SymbolConfig contract, string reportType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reportType)) throw new ArgumentException("IB fundamental report type is required.", nameof(reportType));
        return Issue("fundamentals", RequireSymbol(contract), contract.Exchange, reportType, id => _transport.RequestFundamentals(id, contract, reportType), ct);
    }

    /// <summary>
    /// Requests IB's dividend forecast and fundamental-ratio generic ticks. Availability remains
    /// entitlement-dependent and is recorded as lineage rather than inferred from the request.
    /// </summary>
    public int SubscribeDividendEarnings(SymbolConfig contract, CancellationToken ct = default)
        => Issue("dividend-earnings", RequireSymbol(contract), contract.Exchange, "456,258", id => _transport.RequestDividendEarnings(id, contract), ct);

    public int SubscribeTickByTick(SymbolConfig contract, string tickType = "Last", int numberOfTicks = 0, bool ignoreSize = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tickType)) throw new ArgumentException("IB tick-by-tick type is required.", nameof(tickType));
        if (numberOfTicks < 0) throw new ArgumentOutOfRangeException(nameof(numberOfTicks));
        return Issue("tick-by-tick", RequireSymbol(contract), contract.Exchange, tickType, id => _transport.RequestTickByTick(id, contract, tickType, numberOfTicks, ignoreSize), ct);
    }

    public int SubscribePnl(string account, string? modelCode = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account)) throw new ArgumentException("IB account is required.", nameof(account));
        return Issue("pnl", account, null, modelCode, id => _transport.RequestPnl(id, account, modelCode), ct);
    }

    public int RequestMarketRule(int marketRuleId, CancellationToken ct = default)
    {
        if (marketRuleId <= 0) throw new ArgumentOutOfRangeException(nameof(marketRuleId));
        return Issue("market-rule", marketRuleId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, marketRuleId.ToString(System.Globalization.CultureInfo.InvariantCulture), id => _transport.RequestMarketRule(id, marketRuleId), ct);
    }

    public int RequestDepthExchanges(CancellationToken ct = default)
        => Issue("depth-exchanges", "IB", null, null, _transport.RequestDepthExchanges, ct);

    /// <summary>Records the actual data type reported by IB for a request/subscription.</summary>
    public void RecordMarketDataType(int requestId, int marketDataType)
    {
        var availability = marketDataType switch
        {
            1 => IBMarketDataAvailability.Live,
            2 => IBMarketDataAvailability.Frozen,
            3 => IBMarketDataAvailability.Delayed,
            4 => IBMarketDataAvailability.DelayedFrozen,
            _ => IBMarketDataAvailability.Unknown
        };
        Update(requestId, x => x with { Availability = availability, IsDelayed = availability is IBMarketDataAvailability.Delayed or IBMarketDataAvailability.DelayedFrozen, Status = "market-data-type", ObservedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>Records contract exchange and market-rule evidence returned by IB.</summary>
    public void RecordContractMetadata(int requestId, string? exchange, string? marketRuleIds)
        => Update(requestId, x => x with { Exchange = exchange ?? x.Exchange, MarketRuleIds = marketRuleIds ?? x.MarketRuleIds, Status = "contract-details", ObservedAt = DateTimeOffset.UtcNow });

    /// <summary>Records the ordered IB price-increment table returned for a market rule.</summary>
    public void RecordMarketRuleIncrements(int requestId, IEnumerable<(decimal LowEdge, decimal Increment)> increments)
    {
        ArgumentNullException.ThrowIfNull(increments);
        var serialized = string.Join(';', increments.Select(x =>
            string.Concat(
                x.LowEdge.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ":",
                x.Increment.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        if (string.IsNullOrWhiteSpace(serialized)) throw new ArgumentException("At least one market-rule increment is required.", nameof(increments));
        Update(requestId, x => x with { MinimumIncrements = serialized, Status = "market-rule", ObservedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>Records an entitlement or exchange-specific terminal status without discarding lineage.</summary>
    public void RecordStatus(int requestId, string status)
    {
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));
        Update(requestId, x => x with { Status = status, ObservedAt = DateTimeOffset.UtcNow });
    }

    private void OnMarketDataTypeReceived(object? sender, IBMarketDataTypeUpdate update)
    {
        if (_lineage.ContainsKey(update.RequestId))
            RecordMarketDataType(update.RequestId, update.MarketDataType);
    }

    private int Issue(string service, string symbol, string? exchange, string? subscription, Action<int> send, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var evidence = new IBDataLineage(requestId, service, symbol, exchange, null, null, subscription, IBMarketDataAvailability.Unknown, false, "requested", DateTimeOffset.UtcNow);
        if (!_lineage.TryAdd(requestId, evidence)) throw new InvalidOperationException($"Duplicate IB request id {requestId}.");
        try { send(requestId); }
        catch { _lineage.TryRemove(requestId, out _); throw; }
        LineageUpdated?.Invoke(evidence);
        return requestId;
    }

    private void Update(int requestId, Func<IBDataLineage, IBDataLineage> update)
    {
        var updated = _lineage.AddOrUpdate(requestId, _ => throw new KeyNotFoundException($"Unknown IB request id {requestId}."), (_, current) => update(current));
        LineageUpdated?.Invoke(updated);
    }

    private static string RequireSymbol(SymbolConfig contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.Symbol)) throw new ArgumentException("IB contract symbol is required.", nameof(contract));
        return contract.Symbol;
    }
}
