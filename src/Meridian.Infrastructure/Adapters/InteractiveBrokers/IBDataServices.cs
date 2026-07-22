using System.Collections.Concurrent;
using System.Threading.Channels;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk;

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

/// <summary>Explicit parameters for a five-second IB real-time-bar stream.</summary>
public sealed record IBRealTimeBarRequest(SymbolConfig Contract, string WhatToShow = "TRADES", bool UseRegularTradingHours = true);

/// <summary>Explicit parameters for an IB historical-tick request.</summary>
public sealed record IBHistoricalTickRequest(SymbolConfig Contract, DateTimeOffset? Start, DateTimeOffset? End, int NumberOfTicks, string WhatToShow = "TRADES", bool UseRegularTradingHours = true);

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
    void RequestRealTimeBars(int requestId, IBRealTimeBarRequest request) => throw new NotSupportedException("The configured IB transport does not support real-time bars.");
    void RequestHistoricalTicks(int requestId, IBHistoricalTickRequest request) => throw new NotSupportedException("The configured IB transport does not support historical ticks.");
    void CancelDataRequest(int requestId, string capability) { }
}

/// <summary>Optional runtime callback source for automatically captured IB entitlement evidence.</summary>
public interface IIBDataLineageSource
{
    event EventHandler<IBMarketDataTypeUpdate>? MarketDataTypeReceived;
}

/// <summary>Exposes the configured physical/logical IB connection identity for provenance.</summary>
public interface IIBProviderConnectionIdentity
{
    string ProviderConnectionId { get; }
}

/// <summary>Callback bridge used to correlate vendor callbacks without exposing IB API types above Infrastructure.</summary>
public interface IIBDataCallbackSource
{
    event EventHandler<(int RequestId, ProviderOptionContract Contract)>? OptionContractReceived;
    event EventHandler<(int RequestId, ProviderScannerResult Result)>? ScannerResultReceived;
    event EventHandler<(int RequestId, ProviderRealTimeBar Bar)>? RealTimeBarReceived;
    event EventHandler<(int RequestId, ProviderHistoricalTick Tick, bool Completed)>? HistoricalTickReceived;
    event EventHandler<(int RequestId, ProviderAccountPnl Pnl)>? PnlReceived;
    event EventHandler<(int RequestId, IReadOnlyList<ProviderMarketRuleIncrement> Increments)>? MarketRuleReceived;
    event EventHandler<int>? RequestCompleted;
    event EventHandler<(int RequestId, string Code, string Message)>? RequestRejected;
}

/// <summary>IB's actual live/frozen/delayed classification for a request.</summary>
public sealed record IBMarketDataTypeUpdate(int RequestId, int MarketDataType);

/// <summary>
/// Issues IB's entitlement-sensitive discovery, reference, news, and richer market-data requests
/// while retaining request lineage. This surface never fabricates availability: callers begin at
/// <see cref="IBMarketDataAvailability.Unknown"/> until TWS/Gateway reports a data type.
/// </summary>
public sealed class IBDataServices : IProviderDataReadService, IDisposable
{
    private const string ProviderId = "interactive-brokers";
    private readonly string _providerConnectionId;
    private readonly IIBDataServiceTransport _transport;
    private readonly IIBDataCallbackSource? _callbackSource;
    private readonly IBDataResultMaterializer? _materializer;
    private readonly ConcurrentDictionary<int, IBDataLineage> _lineage = new();
    private readonly ConcurrentDictionary<int, ProviderDataRequestReadModel> _requests = new();
    private readonly Channel<ProviderDataRequestReadModel> _updates = Channel.CreateBounded<ProviderDataRequestReadModel>(
        new BoundedChannelOptions(256) { SingleReader = false, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
    private int _nextRequestId = 90_000;

    public IBDataServices(IIBDataServiceTransport transport, string providerConnectionId = "interactive-brokers/default")
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _providerConnectionId = transport is IIBProviderConnectionIdentity identity
            ? identity.ProviderConnectionId
            : providerConnectionId;
        if (string.IsNullOrWhiteSpace(_providerConnectionId))
            throw new ArgumentException("Provider connection identity is required.", nameof(providerConnectionId));
        if (transport is IIBDataLineageSource source)
            source.MarketDataTypeReceived += OnMarketDataTypeReceived;
        if (transport is IIBDataCallbackSource callbacks)
        {
            _callbackSource = callbacks;
            callbacks.OptionContractReceived += OnOptionContractReceived;
            callbacks.ScannerResultReceived += OnScannerResultReceived;
            callbacks.RealTimeBarReceived += OnRealTimeBarReceived;
            callbacks.HistoricalTickReceived += OnHistoricalTickReceived;
            callbacks.PnlReceived += OnPnlReceived;
            callbacks.MarketRuleReceived += OnMarketRuleReceived;
            callbacks.RequestCompleted += OnRequestCompleted;
            callbacks.RequestRejected += OnRequestRejected;
        }
    }

    /// <summary>Raised after request, status, or contract-lineage evidence changes.</summary>
    public event Action<IBDataLineage>? LineageUpdated;

    /// <summary>Raised when a provider-neutral request projection changes.</summary>
    public event Action<ProviderDataRequestReadModel>? ReadModelUpdated;

    /// <summary>Returns the current lineage evidence in stable request-id order.</summary>
    public IReadOnlyList<IBDataLineage> GetLineage() => _lineage.Values.OrderBy(x => x.RequestId).ToArray();

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => _requests.Values.OrderBy(x => x.RequestId).ToArray();

    public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return update;
    }

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

    public int SubscribeRealTimeBars(IBRealTimeBarRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Issue("real-time-bars", RequireSymbol(request.Contract), request.Contract.Exchange, request.WhatToShow, id => _transport.RequestRealTimeBars(id, request), ct);
    }

    public int RequestHistoricalTicks(IBHistoricalTickRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NumberOfTicks is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(request), "IB historical-tick requests must contain 1 to 1,000 ticks.");
        if (request.Start.HasValue && request.End.HasValue && request.Start > request.End) throw new ArgumentException("Historical tick start must not be after end.", nameof(request));
        return Issue("historical-ticks", RequireSymbol(request.Contract), request.Contract.Exchange, request.WhatToShow, id => _transport.RequestHistoricalTicks(id, request), ct);
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
        var lineage = Update(requestId, x => x with { Availability = availability, IsDelayed = availability is IBMarketDataAvailability.Delayed or IBMarketDataAvailability.DelayedFrozen, Status = "market-data-type", ObservedAt = DateTimeOffset.UtcNow });
        UpdateReadModel(requestId, current => RefreshProvenance(current, lineage));
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

    /// <summary>Correlates an option-discovery callback to its originating request.</summary>
    public void RecordOptionContract(int requestId, ProviderOptionContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        UpdateReadModel(requestId, current => current with
        {
            Status = ProviderDataRequestStatus.Streaming,
            OptionContracts = Append(current.OptionContracts, contract with { Provenance = CreateObservationProvenance(current, contract.ProviderContractId ?? contract.Symbol, contract.Provenance.SourceTimestamp) })
        });
    }

    public void RecordScannerResult(int requestId, ProviderScannerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, ScannerResults = Append(current.ScannerResults, result with { Provenance = CreateObservationProvenance(current, result.ProviderContractId ?? $"{result.Symbol}:{result.Rank}", result.Provenance.SourceTimestamp) }) });
    }

    public void RecordRealTimeBar(int requestId, ProviderRealTimeBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, RealTimeBars = Append(current.RealTimeBars, bar with { Provenance = CreateObservationProvenance(current, $"{bar.Timestamp:O}:{bar.Open}:{bar.High}:{bar.Low}:{bar.Close}:{bar.Volume}:{bar.TradeCount}", bar.Timestamp) }) });
    }

    public void RecordHistoricalTick(int requestId, ProviderHistoricalTick tick, bool completed = false)
    {
        ArgumentNullException.ThrowIfNull(tick);
        UpdateReadModel(requestId, current => current with { Status = completed ? ProviderDataRequestStatus.Completed : ProviderDataRequestStatus.Streaming, HistoricalTicks = Append(current.HistoricalTicks, tick with { Provenance = CreateObservationProvenance(current, $"{tick.Timestamp:O}:{tick.TickKind}:{tick.Price}:{tick.Size}", tick.Timestamp) }) });
    }

    public void RecordPnl(int requestId, ProviderAccountPnl pnl)
    {
        ArgumentNullException.ThrowIfNull(pnl);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, AccountId = pnl.AccountId, ModelAccountId = pnl.ModelAccountId, Pnl = pnl with { Provenance = CreateObservationProvenance(current, $"{pnl.AccountId}:{pnl.ModelAccountId ?? string.Empty}", pnl.Provenance.SourceTimestamp) } });
    }

    public void RecordMarketRule(int requestId, IEnumerable<ProviderMarketRuleIncrement> increments)
    {
        ArgumentNullException.ThrowIfNull(increments);
        var values = increments.ToArray();
        if (values.Length == 0) throw new ArgumentException("At least one market-rule increment is required.", nameof(increments));
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed, MarketRuleIncrements = values.Select((value, index) => value with { Provenance = CreateObservationProvenance(current, $"{value.LowEdge}:{value.Increment}:{index}", value.Provenance.SourceTimestamp) }).ToArray() });
    }

    public void CompleteRequest(int requestId) => UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed });

    public void CancelRequest(int requestId) => UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Cancelled });

    /// <summary>Cancels the vendor request and marks only its correlated read model as cancelled.</summary>
    public void CancelRequest(int requestId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_requests.TryGetValue(requestId, out var request)) throw new KeyNotFoundException($"Unknown IB request id {requestId}.");
        _transport.CancelDataRequest(requestId, request.Capability);
        CancelRequest(requestId);
    }

    /// <summary>Fails closed on a local timeout and stops a cancellable vendor stream.</summary>
    public void TimeoutRequest(int requestId)
    {
        if (!_requests.TryGetValue(requestId, out var request)) throw new KeyNotFoundException($"Unknown IB request id {requestId}.");
        _transport.CancelDataRequest(requestId, request.Capability);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.TimedOut, ErrorCode = "timeout", ErrorMessage = "The provider callback did not complete before the request timeout." });
    }

    public void RejectRequest(int requestId, string code, string message)
        => UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Rejected, ErrorCode = code, ErrorMessage = message });

    private void OnMarketDataTypeReceived(object? sender, IBMarketDataTypeUpdate update)
    {
        if (_lineage.ContainsKey(update.RequestId))
            RecordMarketDataType(update.RequestId, update.MarketDataType);
    }

    private void OnOptionContractReceived(object? sender, (int RequestId, ProviderOptionContract Contract) value) => RecordOptionContract(value.RequestId, value.Contract);
    private void OnScannerResultReceived(object? sender, (int RequestId, ProviderScannerResult Result) value) => RecordScannerResult(value.RequestId, value.Result);
    private void OnRealTimeBarReceived(object? sender, (int RequestId, ProviderRealTimeBar Bar) value) => RecordRealTimeBar(value.RequestId, value.Bar);
    private void OnHistoricalTickReceived(object? sender, (int RequestId, ProviderHistoricalTick Tick, bool Completed) value) => RecordHistoricalTick(value.RequestId, value.Tick, value.Completed);
    private void OnPnlReceived(object? sender, (int RequestId, ProviderAccountPnl Pnl) value) => RecordPnl(value.RequestId, value.Pnl);
    private void OnMarketRuleReceived(object? sender, (int RequestId, IReadOnlyList<ProviderMarketRuleIncrement> Increments) value) => RecordMarketRule(value.RequestId, value.Increments);
    private void OnRequestCompleted(object? sender, int requestId) => CompleteRequest(requestId);
    private void OnRequestRejected(object? sender, (int RequestId, string Code, string Message) value) => RejectRequest(value.RequestId, value.Code, value.Message);

    private int Issue(string service, string symbol, string? exchange, string? subscription, Action<int> send, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var evidence = new IBDataLineage(requestId, service, symbol, exchange, null, null, subscription, IBMarketDataAvailability.Unknown, false, "requested", DateTimeOffset.UtcNow);
        if (!_lineage.TryAdd(requestId, evidence)) throw new InvalidOperationException($"Duplicate IB request id {requestId}.");
        var projection = new ProviderDataRequestReadModel(requestId, ProviderId, service, ProviderDataRequestStatus.Requested, evidence.ObservedAt, CreateRequestProvenance(evidence));
        _requests.TryAdd(requestId, projection);
        try { send(requestId); }
        catch { _lineage.TryRemove(requestId, out _); _requests.TryRemove(requestId, out _); throw; }
        LineageUpdated?.Invoke(evidence);
        Publish(projection);
        return requestId;
    }

    private IBDataLineage Update(int requestId, Func<IBDataLineage, IBDataLineage> update)
    {
        var updated = _lineage.AddOrUpdate(requestId, _ => throw new KeyNotFoundException($"Unknown IB request id {requestId}."), (_, current) => update(current));
        LineageUpdated?.Invoke(updated);
        return updated;
    }

    private void UpdateReadModel(int requestId, Func<ProviderDataRequestReadModel, ProviderDataRequestReadModel> update)
    {
        var updated = _requests.AddOrUpdate(requestId, _ => throw new KeyNotFoundException($"Unknown IB request id {requestId}."), (_, current) => update(current) with { UpdatedAt = DateTimeOffset.UtcNow });
        Publish(updated);
    }

    private ProviderDataProvenance CreateRequestProvenance(IBDataLineage lineage)
        => CreateProvenance(lineage, lineage.Symbol, lineage.ObservedAt);

    private ProviderDataProvenance CreateObservationProvenance(ProviderDataRequestReadModel request, string providerNativeId, DateTimeOffset sourceTimestamp)
    {
        var lineage = _lineage.TryGetValue(request.RequestId, out var current)
            ? current
            : throw new KeyNotFoundException($"Unknown IB request id {request.RequestId}.");
        return CreateProvenance(lineage, providerNativeId, sourceTimestamp);
    }

    private ProviderDataRequestReadModel RefreshProvenance(ProviderDataRequestReadModel request, IBDataLineage lineage)
        => request with
        {
            Provenance = RefreshProvenance(lineage, request.Provenance),
            OptionContracts = request.OptionContracts?.Select(contract => contract with { Provenance = RefreshProvenance(lineage, contract.Provenance) }).ToArray(),
            ScannerResults = request.ScannerResults?.Select(result => result with { Provenance = RefreshProvenance(lineage, result.Provenance) }).ToArray(),
            RealTimeBars = request.RealTimeBars?.Select(bar => bar with { Provenance = RefreshProvenance(lineage, bar.Provenance) }).ToArray(),
            HistoricalTicks = request.HistoricalTicks?.Select(tick => tick with { Provenance = RefreshProvenance(lineage, tick.Provenance) }).ToArray(),
            Pnl = request.Pnl is { } pnl ? pnl with { Provenance = RefreshProvenance(lineage, pnl.Provenance) } : null,
            MarketRuleIncrements = request.MarketRuleIncrements?.Select(increment => increment with { Provenance = RefreshProvenance(lineage, increment.Provenance) }).ToArray()
        };

    private ProviderDataProvenance RefreshProvenance(IBDataLineage lineage, ProviderDataProvenance provenance)
        => CreateProvenance(lineage, provenance.ProviderNativeId, provenance.SourceTimestamp, provenance.ReceiptTimestamp);

    private ProviderDataProvenance CreateProvenance(IBDataLineage lineage, string providerNativeId, DateTimeOffset sourceTimestamp, DateTimeOffset? receiptTimestamp = null)
    {
        providerNativeId = string.IsNullOrWhiteSpace(providerNativeId) ? lineage.Symbol : providerNativeId;
        var availability = lineage.Availability.ToString();
        var descriptor = string.Join("|", lineage.Service, lineage.Symbol, lineage.Exchange ?? string.Empty, lineage.Subscription ?? string.Empty);
        var correlationId = lineage.RequestId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var keyMaterial = string.Join("|", ProviderId, _providerConnectionId, providerNativeId, descriptor, correlationId);
        var deduplicationKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(keyMaterial))).ToLowerInvariant();
        return new ProviderDataProvenance(ProviderId, _providerConnectionId, sourceTimestamp, receiptTimestamp ?? DateTimeOffset.UtcNow,
            availability == nameof(IBMarketDataAvailability.Unknown) ? "unknown" : "reported", lineage.Subscription ?? "unspecified",
            availability, descriptor, providerNativeId, correlationId, deduplicationKey);
    }

    private void Publish(ProviderDataRequestReadModel model)
    {
        _updates.Writer.TryWrite(model);
        ReadModelUpdated?.Invoke(model);
        _materializer?.Materialize(model, _lineage.TryGetValue(model.RequestId, out var lineage) ? lineage : null);
    }

    private static IReadOnlyList<T> Append<T>(IReadOnlyList<T>? existing, T value)
        => existing is null ? [value] : [.. existing, value];

    public void Dispose()
    {
        if (_transport is IIBDataLineageSource source)
            source.MarketDataTypeReceived -= OnMarketDataTypeReceived;
        if (_callbackSource is { } callbacks)
        {
            callbacks.OptionContractReceived -= OnOptionContractReceived;
            callbacks.ScannerResultReceived -= OnScannerResultReceived;
            callbacks.RealTimeBarReceived -= OnRealTimeBarReceived;
            callbacks.HistoricalTickReceived -= OnHistoricalTickReceived;
            callbacks.PnlReceived -= OnPnlReceived;
            callbacks.MarketRuleReceived -= OnMarketRuleReceived;
            callbacks.RequestCompleted -= OnRequestCompleted;
            callbacks.RequestRejected -= OnRequestRejected;
        }
        _updates.Writer.TryComplete();
    }

    private static string RequireSymbol(SymbolConfig contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.Symbol)) throw new ArgumentException("IB contract symbol is required.", nameof(contract));
        return contract.Symbol;
    }
}
