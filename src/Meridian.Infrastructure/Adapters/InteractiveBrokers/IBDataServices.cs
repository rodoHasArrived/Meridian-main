using System.Collections.Concurrent;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk;
using Meridian.Contracts.Integrity;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

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
    event EventHandler<(int RequestId, ProviderContractDetails Details)>? ContractDetailsReceived;
    event EventHandler<(int RequestId, ProviderOptionChainDefinition Definition)>? OptionChainDefinitionReceived;
    event EventHandler<(int RequestId, ProviderNewsHeadline Headline)>? HistoricalNewsReceived;
    event EventHandler<(int RequestId, ProviderNewsArticlePayload Article)>? NewsArticleReceived;
    event EventHandler<(int RequestId, ProviderFundamentalReport Report)>? FundamentalReportReceived;
    event EventHandler<(int RequestId, ProviderTickByTickObservation Observation)>? TickByTickReceived;
    event EventHandler<(int RequestId, IReadOnlyList<ProviderDepthExchangeDescription> Exchanges)>? DepthExchangesReceived;
    event EventHandler<(int RequestId, ProviderDividendEarnings Payload)>? DividendEarningsReceived;
    event EventHandler<(int RequestId, ProviderOptionContract Contract)>? OptionContractReceived;
    event EventHandler<(int RequestId, ProviderScannerResult Result)>? ScannerResultReceived;
    /// <summary>
    /// Non-terminal batch delimiter for a live scanner subscription (IB's scannerDataEnd): the
    /// vendor re-sends the full current ranked list each refresh cycle, so the rows after this
    /// delimiter REPLACE the request's accumulated results rather than extending them.
    /// </summary>
    event EventHandler<int>? ScannerBatchCompleted;
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
public sealed class IBDataServices : ITenantScopedProviderDataReadService, IDisposable
{
    private const string ProviderId = "interactive-brokers";
    private readonly string _providerConnectionId;
    private readonly IIBDataServiceTransport _transport;
    private readonly IIBDataCallbackSource? _callbackSource;
    private readonly IBDurableResultProjector? _projector;
    private readonly ConcurrentDictionary<int, IBDataLineage> _lineage = new();
    private readonly ConcurrentDictionary<int, ProviderDataRequestReadModel> _requests = new();
    private readonly ConcurrentDictionary<int, IBDataRequestOwnership> _ownership = new();
    private readonly ConcurrentDictionary<int, string> _requestCorrelationIds = new();
    // Scanner ids whose current result batch was delimited by the vendor (scannerDataEnd): the
    // next scanner row for that id begins a fresh batch and replaces the accumulated results.
    // Entries for terminal requests are inert — request ids are process-monotonic, never reused.
    private readonly ConcurrentDictionary<int, bool> _scannerBatchClosed = new();
    // Serializes each request's read-model transition WITH the ORDER of its publications:
    // without the gate, a callback that paused between AddOrUpdate and Publish could publish its
    // stale active model after another thread published the terminal one, resurrecting the
    // request for watchers and letting the last-write-wins durable projector overwrite the
    // terminal record. Publications themselves run OUTSIDE the lock: Publish reaches the durable
    // projector and the public synchronous ReadModelUpdated event, and a subscriber reacting to
    // one request by transitioning another must not be able to entangle two requests' gates into
    // a lock-ordering deadlock. Each transition therefore enqueues its publication under the
    // lock, and a single drainer per request delivers the queue in order with no lock held.
    // Lineage notifications ride the same queue for the same reason: recorders can run while a
    // gated cancel or timeout holds the request's gate, and raising the synchronous
    // LineageUpdated event under any such hold would hand a subscriber the ability to take a
    // second request's gate under the first.
    private readonly ConcurrentDictionary<int, RequestGate> _readModelGates = new();

    private sealed class RequestGate
    {
        public readonly object Lock = new();
        public readonly Queue<PendingPublication> PendingPublications = new();
        public bool Draining;
        // Both fields below are guarded by Lock. Submission tracks where Issue's transport
        // submission stands so cancel and timeout can coordinate with it without ever waiting
        // on the send itself, which runs outside the gate. DeferredWireCancel records that a
        // terminal transition fired while the send was in flight and left the wire cancel to
        // the submitting thread, which spends it once the subscription exists.
        public SubmissionState Submission;
        public bool DeferredWireCancel;
    }

    private enum SubmissionState
    {
        NotStarted,
        InFlight,
        Submitted,
        Failed,
    }

    // One slot in a request's ordered publication queue: exactly one payload is set.
    private readonly struct PendingPublication
    {
        private PendingPublication(ProviderDataRequestReadModel? model, IBDataLineage? lineage)
        {
            Model = model;
            Lineage = lineage;
        }

        public ProviderDataRequestReadModel? Model { get; }
        public IBDataLineage? Lineage { get; }

        public static PendingPublication For(ProviderDataRequestReadModel model) => new(model, null);
        public static PendingPublication For(IBDataLineage lineage) => new(null, lineage);
    }
    private readonly TenantScopedProviderDataUpdateHub _updates = new();
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
            callbacks.ContractDetailsReceived += OnContractDetailsReceived;
            callbacks.OptionChainDefinitionReceived += OnOptionChainDefinitionReceived;
            callbacks.HistoricalNewsReceived += OnHistoricalNewsReceived;
            callbacks.NewsArticleReceived += OnNewsArticleReceived;
            callbacks.FundamentalReportReceived += OnFundamentalReportReceived;
            callbacks.TickByTickReceived += OnTickByTickReceived;
            callbacks.DepthExchangesReceived += OnDepthExchangesReceived;
            callbacks.DividendEarningsReceived += OnDividendEarningsReceived;
            callbacks.OptionContractReceived += OnOptionContractReceived;
            callbacks.ScannerResultReceived += OnScannerResultReceived;
            callbacks.ScannerBatchCompleted += OnScannerBatchCompleted;
            callbacks.RealTimeBarReceived += OnRealTimeBarReceived;
            callbacks.HistoricalTickReceived += OnHistoricalTickReceived;
            callbacks.PnlReceived += OnPnlReceived;
            callbacks.MarketRuleReceived += OnMarketRuleReceived;
            callbacks.RequestCompleted += OnRequestCompleted;
            callbacks.RequestRejected += OnRequestRejected;
        }
    }

    public IBDataServices(
        IIBDataServiceTransport transport,
        IBDurableResultProjector projector,
        string providerConnectionId = "interactive-brokers/default")
        : this(transport, providerConnectionId)
    {
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
    }

    /// <summary>
    /// Compatibility event raised only for explicitly ownerless request lineage. Owner-bound
    /// updates remain available through the scoped request watch and durable result paths.
    /// </summary>
    public event Action<IBDataLineage>? LineageUpdated;

    /// <summary>
    /// Compatibility event raised only when an explicitly ownerless request projection changes.
    /// </summary>
    public event Action<ProviderDataRequestReadModel>? ReadModelUpdated;

    /// <summary>Returns explicitly ownerless lineage evidence in stable request-id order.</summary>
    public IReadOnlyList<IBDataLineage> GetLineage()
        => _lineage.Values
            .Where(lineage => !_ownership.ContainsKey(lineage.RequestId))
            .OrderBy(static lineage => lineage.RequestId)
            .ToArray();

    /// <summary>Returns lineage owned by the exact tenant and company scope.</summary>
    public IReadOnlyList<IBDataLineage> GetLineage(string tenantId, string companyId)
    {
        var ownership = IBDataRequestOwnership.Require(new IBDataRequestOwnership(tenantId, companyId));
        return _lineage.Values
            .Where(lineage =>
                _ownership.TryGetValue(lineage.RequestId, out var owner) &&
                string.Equals(owner.TenantId, ownership.TenantId, StringComparison.Ordinal) &&
                string.Equals(owner.CompanyId, ownership.CompanyId, StringComparison.Ordinal))
            .OrderBy(static lineage => lineage.RequestId)
            .ToArray();
    }

    /// <summary>
    /// Compatibility read for explicitly ownerless requests only. Owner-bound requests must be
    /// queried through the tenant/company overload and are never downgraded to this surface.
    /// </summary>
    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests()
        => _requests.Values
            .Where(request => !_ownership.ContainsKey(request.RequestId))
            .OrderBy(static request => request.RequestId)
            .ToArray();

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests(string tenantId, string companyId)
    {
        var ownership = IBDataRequestOwnership.Require(new IBDataRequestOwnership(tenantId, companyId));
        return _requests.Values
            .Where(request =>
                _ownership.TryGetValue(request.RequestId, out var owner) &&
                string.Equals(owner.TenantId, ownership.TenantId, StringComparison.Ordinal) &&
                string.Equals(owner.CompanyId, ownership.CompanyId, StringComparison.Ordinal))
            .OrderBy(static request => request.RequestId)
            .ToArray();
    }

    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(CancellationToken cancellationToken = default)
        => _updates.WatchUnownedAsync(cancellationToken);

    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
        string tenantId,
        string companyId,
        CancellationToken cancellationToken = default)
        => _updates.WatchAsync(
            IBDataRequestOwnership.Require(new IBDataRequestOwnership(tenantId, companyId)),
            cancellationToken);

    public int RequestScanner(
        IBScannerRequest request,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Instrument) || string.IsNullOrWhiteSpace(request.LocationCode) || string.IsNullOrWhiteSpace(request.ScanCode))
            throw new ArgumentException("IB scanner instrument, location code, and scan code are required.", nameof(request));
        if (request.NumberOfRows is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(request), "IB scanner row count must be between 1 and 50.");

        return Issue("scanner", request.Instrument, request.LocationCode, request.ScanCode, id => _transport.RequestScanner(id, request), ct, ownership);
    }

    public int RequestContractDetails(
        SymbolConfig contract,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
        => Issue("contract-details", RequireSymbol(contract), contract.Exchange, null, id => _transport.RequestContractDetails(id, contract), ct, ownership);

    public int RequestOptionChain(
        SymbolConfig underlying,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
        => Issue("option-chain", RequireSymbol(underlying), underlying.Exchange, null, id => _transport.RequestOptionChain(id, underlying), ct, ownership);

    public int RequestHistoricalNews(
        int conId,
        string providerCodes,
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumResults = 100,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        if (conId <= 0)
            throw new ArgumentOutOfRangeException(nameof(conId));
        if (string.IsNullOrWhiteSpace(providerCodes))
            throw new ArgumentException("At least one IB news provider code is required.", nameof(providerCodes));
        if (start > end)
            throw new ArgumentException("News start must not be after end.", nameof(start));
        if (maximumResults is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        return Issue("historical-news", conId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, providerCodes, id => _transport.RequestHistoricalNews(id, conId, providerCodes, start, end, maximumResults), ct, ownership);
    }

    public int RequestNewsArticle(
        string providerCode,
        string articleId,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        if (string.IsNullOrWhiteSpace(providerCode) || string.IsNullOrWhiteSpace(articleId))
            throw new ArgumentException("IB news provider code and article id are required.");
        return Issue("news-article", articleId, null, providerCode, id => _transport.RequestNewsArticle(id, providerCode, articleId), ct, ownership);
    }

    public int RequestFundamentals(
        SymbolConfig contract,
        string reportType,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        if (string.IsNullOrWhiteSpace(reportType))
            throw new ArgumentException("IB fundamental report type is required.", nameof(reportType));
        return Issue("fundamentals", RequireSymbol(contract), contract.Exchange, reportType, id => _transport.RequestFundamentals(id, contract, reportType), ct, ownership);
    }

    /// <summary>
    /// Requests IB's dividend forecast and fundamental-ratio generic ticks. Availability remains
    /// entitlement-dependent and is recorded as lineage rather than inferred from the request.
    /// </summary>
    public int SubscribeDividendEarnings(
        SymbolConfig contract,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
        => Issue("dividend-earnings", RequireSymbol(contract), contract.Exchange, "456,258", id => _transport.RequestDividendEarnings(id, contract), ct, ownership);

    public int SubscribeTickByTick(
        SymbolConfig contract,
        string tickType = "Last",
        int numberOfTicks = 0,
        bool ignoreSize = false,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        if (string.IsNullOrWhiteSpace(tickType))
            throw new ArgumentException("IB tick-by-tick type is required.", nameof(tickType));
        if (numberOfTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(numberOfTicks));
        return Issue("tick-by-tick", RequireSymbol(contract), contract.Exchange, tickType, id => _transport.RequestTickByTick(id, contract, tickType, numberOfTicks, ignoreSize), ct, ownership);
    }

    public int SubscribePnl(
        string account,
        string? modelCode = null,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("IB account is required.", nameof(account));
        return Issue("pnl", account, null, modelCode, id => _transport.RequestPnl(id, account, modelCode), ct, ownership);
    }

    public int SubscribeRealTimeBars(
        IBRealTimeBarRequest request,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Issue("real-time-bars", RequireSymbol(request.Contract), request.Contract.Exchange, request.WhatToShow, id => _transport.RequestRealTimeBars(id, request), ct, ownership);
    }

    public int RequestHistoricalTicks(
        IBHistoricalTickRequest request,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NumberOfTicks is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(request), "IB historical-tick requests must contain 1 to 1,000 ticks.");
        if (request.Start.HasValue && request.End.HasValue && request.Start > request.End)
            throw new ArgumentException("Historical tick start must not be after end.", nameof(request));
        return Issue("historical-ticks", RequireSymbol(request.Contract), request.Contract.Exchange, request.WhatToShow, id => _transport.RequestHistoricalTicks(id, request), ct, ownership);
    }

    public int RequestMarketRule(
        int marketRuleId,
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
    {
        if (marketRuleId <= 0)
            throw new ArgumentOutOfRangeException(nameof(marketRuleId));
        return Issue("market-rule", marketRuleId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, marketRuleId.ToString(System.Globalization.CultureInfo.InvariantCulture), id => _transport.RequestMarketRule(id, marketRuleId), ct, ownership);
    }

    public int RequestDepthExchanges(
        CancellationToken ct = default,
        IBDataRequestOwnership? ownership = null)
        => Issue("depth-exchanges", "IB", null, null, _transport.RequestDepthExchanges, ct, ownership);

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
        // The provenance refresh rides the SAME gated transition as the lineage update: as two
        // separate transitions, a cancellation, timeout, or rejection landing between them froze
        // the read model with the new lineage availability embedded but the old availability
        // still in its request and observation provenance — incoherent evidence the durable
        // projector then materialized permanently.
        Update(
            requestId,
            x => x with { Availability = availability, IsDelayed = availability is IBMarketDataAvailability.Delayed or IBMarketDataAvailability.DelayedFrozen, Status = "market-data-type", ObservedAt = DateTimeOffset.UtcNow },
            (current, updated) => RefreshProvenance(current, updated));
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
        if (string.IsNullOrWhiteSpace(serialized))
            throw new ArgumentException("At least one market-rule increment is required.", nameof(increments));
        Update(requestId, x => x with { MinimumIncrements = serialized, Status = "market-rule", ObservedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>Records an entitlement or exchange-specific terminal status without discarding lineage.</summary>
    public void RecordStatus(int requestId, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status is required.", nameof(status));
        Update(requestId, x => x with { Status = status, ObservedAt = DateTimeOffset.UtcNow });
    }


    public void RecordContractDetails(int requestId, ProviderContractDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        RecordContractMetadata(requestId, details.Exchange, details.MarketRuleIds);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, ContractDetails = Append(current.ContractDetails, details) });
    }

    public void RecordOptionChainDefinition(int requestId, ProviderOptionChainDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, OptionChainDefinitions = Append(current.OptionChainDefinitions, definition) });
    }

    public void RecordNewsHeadline(int requestId, ProviderNewsHeadline headline)
    {
        ArgumentNullException.ThrowIfNull(headline);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, NewsHeadlines = Append(current.NewsHeadlines, headline) });
    }

    public void RecordNewsArticle(int requestId, ProviderNewsArticlePayload article)
    {
        ArgumentNullException.ThrowIfNull(article);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed, NewsArticle = article });
    }

    public void RecordFundamentalReport(int requestId, ProviderFundamentalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed, FundamentalReport = report });
    }

    public void RecordTickByTick(int requestId, ProviderTickByTickObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, TickByTickObservations = Append(current.TickByTickObservations, observation) });
    }

    public void RecordDepthExchanges(int requestId, IEnumerable<ProviderDepthExchangeDescription> exchanges)
    {
        ArgumentNullException.ThrowIfNull(exchanges);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed, DepthExchanges = exchanges.ToArray() });
    }

    public void RecordDividendEarnings(int requestId, ProviderDividendEarnings payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, DividendEarnings = payload });
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
        // The vendor re-sends the full current ranked list every refresh cycle; the first row
        // after a batch delimiter therefore REPLACES the accumulated results, so the read model
        // reports the current scan instead of an ever-growing union of every cycle.
        //
        // The marker is consumed INSIDE the transition rather than before it. Callback sources
        // that dispatch on several threads can deliver two rows of one cycle concurrently: read
        // outside the gate, the first row could claim the marker and stall before publishing
        // while the second row, seeing no marker, appended to the cycle the first was about to
        // replace — and the first's replacement then discarded it. Consuming it under the same
        // per-request gate that serializes the publication makes "claimed the marker" and
        // "replaced the batch" one step. The delegate can be re-entered if the underlying
        // dictionary retries the update, so the answer is computed once and reused.
        bool? batchDelimiterClaimed = null;
        UpdateReadModel(requestId, current =>
        {
            batchDelimiterClaimed ??= _scannerBatchClosed.TryRemove(requestId, out _);
            var startsNewBatch = batchDelimiterClaimed.Value;
            var enriched = result with { Provenance = CreateObservationProvenance(current, result.ProviderContractId ?? $"{result.Symbol}:{result.Rank}", result.Provenance.SourceTimestamp) };
            return current with
            {
                Status = ProviderDataRequestStatus.Streaming,
                ScannerResults = startsNewBatch ? new[] { enriched } : Append(current.ScannerResults, enriched),
            };
        });
    }

    public void RecordRealTimeBar(int requestId, ProviderRealTimeBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Streaming, RealTimeBars = Append(current.RealTimeBars, bar with { Provenance = CreateObservationProvenance(current, $"{bar.Timestamp:O}:{bar.Open}:{bar.High}:{bar.Low}:{bar.Close}:{bar.Volume}:{bar.TradeCount}", bar.Timestamp) }) });
    }

    public void RecordHistoricalTick(int requestId, ProviderHistoricalTick tick, bool completed = false)
    {
        ArgumentNullException.ThrowIfNull(tick);
        // Side prices and sizes join the identity when present: two BID_ASK snapshots sharing a
        // midpoint and combined size can still differ in spread (bid/ask 200.05/200.15 versus
        // 200.00/200.20) or book imbalance (sizes 1/9 versus 9/1), and each is a distinct
        // observation that must not share a deduplication key. Kinds without side data keep the
        // historical composition, so existing keys stay stable.
        var sideIdentity = tick.Bid is not null || tick.Ask is not null || tick.BidSize is not null || tick.AskSize is not null
            ? $":{tick.Bid}:{tick.Ask}:{tick.BidSize}:{tick.AskSize}"
            : string.Empty;
        UpdateReadModel(requestId, current => current with { Status = completed ? ProviderDataRequestStatus.Completed : ProviderDataRequestStatus.Streaming, HistoricalTicks = Append(current.HistoricalTicks, tick with { Provenance = CreateObservationProvenance(current, $"{tick.Timestamp:O}:{tick.TickKind}:{tick.Price}:{tick.Size}{sideIdentity}", tick.Timestamp) }) });
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
        if (values.Length == 0)
            throw new ArgumentException("At least one market-rule increment is required.", nameof(increments));
        UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed, MarketRuleIncrements = values.Select((value, index) => value with { Provenance = CreateObservationProvenance(current, $"{value.LowEdge}:{value.Increment}:{index}", value.Provenance.SourceTimestamp) }).ToArray() });
    }

    public void CompleteRequest(int requestId) => UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Completed });

    public void CancelRequest(int requestId) => UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Cancelled });

    /// <summary>Cancels the vendor request and marks only its correlated read model as cancelled.</summary>
    public void CancelRequest(int requestId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // The terminal transition is serialized with Issue's pre-send decision on the request's
        // transition gate, but no transport call runs under the gate — a submission stalled on
        // the wire must never block cancellation behind it. Where the submission stands decides
        // the wire work: in the pre-send window the freeze itself stops the stream (Issue skips
        // the send) and no wire cancel is spent at all — nothing was submitted, and a transport
        // that rejects cancellation of an unknown id would throw through a subscriber running
        // inside Issue's registration drain and tear down the already-cancelled request; while
        // a send is in flight the wire cancel is deferred to the submitting thread, which
        // spends it once the subscription is real; after the submission has completed the wire
        // cancel runs before the transition, so a wire failure leaves the read model
        // un-cancelled while the vendor stream may still be live.
        var gate = _readModelGates.GetOrAdd(requestId, static _ => new RequestGate());
        string capability;
        SubmissionState submission;
        lock (gate.Lock)
        {
            if (!_requests.TryGetValue(requestId, out var request))
                throw new KeyNotFoundException($"Unknown IB request id {requestId}.");
            capability = request.Capability;
            submission = gate.Submission;
            if (submission is SubmissionState.InFlight)
                gate.DeferredWireCancel = true;
            if (submission is SubmissionState.NotStarted or SubmissionState.InFlight)
                CancelRequest(requestId);
        }

        // The nested transition only enqueued its publication (this thread held the gate);
        // deliver it now that the lock is released.
        DrainPublications(gate);

        if (submission is SubmissionState.NotStarted or SubmissionState.InFlight)
            return;

        _transport.CancelDataRequest(requestId, capability);
        CancelRequest(requestId);
    }

    /// <summary>Fails closed on a local timeout and stops a cancellable vendor stream.</summary>
    public void TimeoutRequest(int requestId)
    {
        // Timeout is the local fail-closed judgment, so unlike CancelRequest the read model is
        // frozen first in every submission state: a wire stalled inside the send, or inside
        // this thread's own cancel call below, can delay releasing the vendor stream but never
        // the TimedOut outcome itself. The submission handshake matches CancelRequest — a
        // pre-send timeout makes Issue skip the send with no wire call at all, an in-flight
        // one defers the wire cancel to the submitting thread, and a completed one releases
        // the stream at the wire here.
        var gate = _readModelGates.GetOrAdd(requestId, static _ => new RequestGate());
        string capability;
        SubmissionState submission;
        lock (gate.Lock)
        {
            if (!_requests.TryGetValue(requestId, out var request))
                throw new KeyNotFoundException($"Unknown IB request id {requestId}.");
            capability = request.Capability;
            submission = gate.Submission;
            if (submission is SubmissionState.InFlight)
                gate.DeferredWireCancel = true;
            UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.TimedOut, ErrorCode = "timeout", ErrorMessage = "The provider callback did not complete before the request timeout." });
        }

        // The nested transition only enqueued its publication (this thread held the gate);
        // deliver it now that the lock is released.
        DrainPublications(gate);

        if (submission is SubmissionState.NotStarted or SubmissionState.InFlight)
            return;

        _transport.CancelDataRequest(requestId, capability);
    }

    public void RejectRequest(int requestId, string code, string message)
        => UpdateReadModel(requestId, current => current with { Status = ProviderDataRequestStatus.Rejected, ErrorCode = code, ErrorMessage = message });

    private void OnMarketDataTypeReceived(object? sender, IBMarketDataTypeUpdate update)
    {
        // Routability, not lineage presence: lineage evidence outlives terminal requests, so a
        // late availability callback after cancellation, timeout, or rejection would otherwise
        // mutate the retained lineage while the frozen read model keeps its terminal snapshot,
        // leaving the two surfaces reporting different availability.
        if (IsRoutable(update.RequestId))
            RecordMarketDataType(update.RequestId, update.MarketDataType);
    }

    private static bool IsActiveStatus(ProviderDataRequestStatus status)
        => status is ProviderDataRequestStatus.Requested or ProviderDataRequestStatus.Streaming;

    /// <summary>
    /// True only for a tracked request that has not reached a terminal status. This is the
    /// allocation-free pre-check on the reader loop; the race it cannot close (a terminal
    /// transition landing between this check and the recorder) is closed atomically inside
    /// <see cref="UpdateReadModel"/>.
    /// </summary>
    private bool IsRoutable(int requestId)
        => _requests.TryGetValue(requestId, out var request) && IsActiveStatus(request.Status);

    /// <summary>
    /// Capability-checked routability for payload recorders. The vendor allocates ordinary
    /// market-data ticker ids independently of this service's request ids, so after enough
    /// subscription churn a foreign stream's id can collide with a tracked request; routed on
    /// id alone, its payload would be appended to an unrelated read model and carried into the
    /// durable projection. Each payload callback therefore also requires the tracked request's
    /// immutable capability to match the callback's domain. Terminal notices and availability
    /// reports stay id-routed: they carry no payload to misfile.
    /// </summary>
    private bool IsRoutable(int requestId, string capability)
        => _requests.TryGetValue(requestId, out var request)
           && IsActiveStatus(request.Status)
           && string.Equals(request.Capability, capability, StringComparison.Ordinal);

    // Ownership guards are inline rather than a shared delegate-taking wrapper: these run on
    // the IB reader loop at market-data rates, and a capturing lambda per callback would
    // allocate a closure for every vendor tick, tracked or not.
    private void OnContractDetailsReceived(object? sender, (int RequestId, ProviderContractDetails Details) value)
    {
        if (IsRoutable(value.RequestId, "contract-details"))
            RecordContractDetails(value.RequestId, value.Details);
    }

    private void OnOptionChainDefinitionReceived(object? sender, (int RequestId, ProviderOptionChainDefinition Definition) value)
    {
        if (IsRoutable(value.RequestId, "option-chain"))
            RecordOptionChainDefinition(value.RequestId, value.Definition);
    }

    private void OnHistoricalNewsReceived(object? sender, (int RequestId, ProviderNewsHeadline Headline) value)
    {
        if (IsRoutable(value.RequestId, "historical-news"))
            RecordNewsHeadline(value.RequestId, value.Headline);
    }

    private void OnNewsArticleReceived(object? sender, (int RequestId, ProviderNewsArticlePayload Article) value)
    {
        if (IsRoutable(value.RequestId, "news-article"))
            RecordNewsArticle(value.RequestId, value.Article);
    }

    private void OnFundamentalReportReceived(object? sender, (int RequestId, ProviderFundamentalReport Report) value)
    {
        if (IsRoutable(value.RequestId, "fundamentals"))
            RecordFundamentalReport(value.RequestId, value.Report);
    }

    private void OnTickByTickReceived(object? sender, (int RequestId, ProviderTickByTickObservation Observation) value)
    {
        if (IsRoutable(value.RequestId, "tick-by-tick"))
            RecordTickByTick(value.RequestId, value.Observation);
    }

    private void OnDepthExchangesReceived(object? sender, (int RequestId, IReadOnlyList<ProviderDepthExchangeDescription> Exchanges) value)
    {
        if (IsRoutable(value.RequestId, "depth-exchanges"))
            RecordDepthExchanges(value.RequestId, value.Exchanges);
    }

    private void OnDividendEarningsReceived(object? sender, (int RequestId, ProviderDividendEarnings Payload) value)
    {
        if (IsRoutable(value.RequestId, "dividend-earnings"))
            RecordDividendEarnings(value.RequestId, value.Payload);
    }

    private void OnOptionContractReceived(object? sender, (int RequestId, ProviderOptionContract Contract) value)
    {
        // The manager's contractDetails callback deliberately emits an option payload alongside
        // the contract details when a contract-details request resolves to an option, so both
        // originating capabilities legitimately receive option contracts.
        if (IsRoutable(value.RequestId, "option-chain") || IsRoutable(value.RequestId, "contract-details"))
            RecordOptionContract(value.RequestId, value.Contract);
    }

    private void OnScannerResultReceived(object? sender, (int RequestId, ProviderScannerResult Result) value)
    {
        if (IsRoutable(value.RequestId, "scanner"))
            RecordScannerResult(value.RequestId, value.Result);
    }

    private void OnScannerBatchCompleted(object? sender, int requestId)
    {
        if (!IsRoutable(requestId, "scanner"))
            return;

        // A cycle that delivered no rows never reaches the replacement in RecordScannerResult,
        // so its empty current scan is represented at the delimiter itself. A repeat delimiter
        // (marker still set) means the closed cycle was empty; a first delimiter closing a scan
        // that never delivered a row is the same case, recognizable by the still-absent result
        // set. Either way the still-set marker keeps a later cycle's first row starting fresh.
        if (!_scannerBatchClosed.TryAdd(requestId, true)
            || (_requests.TryGetValue(requestId, out var snapshot) && snapshot.ScannerResults is not { Count: > 0 }))
        {
            UpdateReadModel(requestId, static current => current with
            {
                Status = ProviderDataRequestStatus.Streaming,
                ScannerResults = Array.Empty<ProviderScannerResult>(),
            });
        }
    }

    private void OnRealTimeBarReceived(object? sender, (int RequestId, ProviderRealTimeBar Bar) value)
    {
        if (IsRoutable(value.RequestId, "real-time-bars"))
            RecordRealTimeBar(value.RequestId, value.Bar);
    }

    private void OnHistoricalTickReceived(object? sender, (int RequestId, ProviderHistoricalTick Tick, bool Completed) value)
    {
        if (IsRoutable(value.RequestId, "historical-ticks"))
            RecordHistoricalTick(value.RequestId, value.Tick, value.Completed);
    }

    private void OnPnlReceived(object? sender, (int RequestId, ProviderAccountPnl Pnl) value)
    {
        if (IsRoutable(value.RequestId, "pnl"))
            RecordPnl(value.RequestId, value.Pnl);
    }

    private void OnMarketRuleReceived(object? sender, (int RequestId, IReadOnlyList<ProviderMarketRuleIncrement> Increments) value)
    {
        if (IsRoutable(value.RequestId, "market-rule"))
            RecordMarketRule(value.RequestId, value.Increments);
    }

    private void OnRequestCompleted(object? sender, int requestId)
    {
        if (IsRoutable(requestId))
            CompleteRequest(requestId);
    }

    private void OnRequestRejected(object? sender, (int RequestId, string Code, string Message) value)
    {
        if (IsRoutable(value.RequestId))
            RejectRequest(value.RequestId, value.Code, value.Message);
    }

    private int Issue(
        string service,
        string symbol,
        string? exchange,
        string? subscription,
        Action<int> send,
        CancellationToken ct,
        IBDataRequestOwnership? ownership)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedOwnership = ownership is null
            ? null
            : IBDataRequestOwnership.Require(ownership);
        if (_projector is not null && normalizedOwnership is null)
        {
            throw new InvalidOperationException(
                "Durable IB requests require tenant and company ownership before transport submission.");
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var correlationId = Guid.NewGuid().ToString("N");
        var evidence = new IBDataLineage(requestId, service, symbol, exchange, null, null, subscription, IBMarketDataAvailability.Unknown, false, "requested", DateTimeOffset.UtcNow);
        if (!_requestCorrelationIds.TryAdd(requestId, correlationId))
        {
            throw new InvalidOperationException($"Duplicate IB request correlation for request id {requestId}.");
        }
        if (normalizedOwnership is not null && !_ownership.TryAdd(requestId, normalizedOwnership))
        {
            _requestCorrelationIds.TryRemove(requestId, out _);
            throw new InvalidOperationException($"Duplicate IB request ownership for request id {requestId}.");
        }
        if (!_lineage.TryAdd(requestId, evidence))
        {
            _ownership.TryRemove(requestId, out _);
            _requestCorrelationIds.TryRemove(requestId, out _);
            throw new InvalidOperationException($"Duplicate IB request id {requestId}.");
        }
        var projection = new ProviderDataRequestReadModel(
            requestId,
            "interactive-brokers",
            service,
            ProviderDataRequestStatus.Requested,
            evidence.ObservedAt,
            CreateRequestProvenance(evidence),
            Lineage: evidence);
        if (!_requests.TryAdd(requestId, projection))
        {
            _lineage.TryRemove(requestId, out _);
            _ownership.TryRemove(requestId, out _);
            _requestCorrelationIds.TryRemove(requestId, out _);
            throw new InvalidOperationException($"Duplicate IB request projection for request id {requestId}.");
        }

        try
        {
            // Persist and publish the immutable owner-bound Requested state before transport. IB
            // transports may deliver callbacks synchronously; publishing after send could otherwise
            // overwrite a callback's richer Streaming/Completed snapshot with stale Requested state.
            // Lineage evidence and the Requested projection are queued together under one hold, in
            // that delivery order: a consumer that reacts to LineageUpdated by terminating this
            // request publishes through the same per-request queue, so its terminal model finds
            // Requested already ahead of it and watchers never see stale Requested as the latest
            // update.
            var registrationGate = _readModelGates.GetOrAdd(requestId, static _ => new RequestGate());
            lock (registrationGate.Lock)
            {
                registrationGate.PendingPublications.Enqueue(PendingPublication.For(evidence));
                registrationGate.PendingPublications.Enqueue(PendingPublication.For(projection));
            }
            DrainPublications(registrationGate);
        }
        catch
        {
            _lineage.TryRemove(requestId, out _);
            _requests.TryRemove(requestId, out _);
            _ownership.TryRemove(requestId, out _);
            _requestCorrelationIds.TryRemove(requestId, out _);
            // Request ids are monotonic and the transport was never submitted, so the
            // registration gate — and any publication still queued inside it — can never be
            // drained or reused; leaving it behind would leak one gate per attempt while a
            // persistence outage keeps failing registrations.
            _readModelGates.TryRemove(requestId, out _);
            throw;
        }

        try
        {
            // The pre-send decision is serialized with cancel and timeout on the request's
            // transition gate, but the transport call itself runs outside the gate: a
            // submission stalled on the wire must not pin the gate, or the fail-closed timeout
            // and the cancellation a watcher fires against the published Requested model would
            // block until the stall clears. The gate-held handshake keeps the original
            // guarantee — a terminal transition in the pre-send window skips the send entirely,
            // and one that lands while the send is in flight defers its wire cancel to this
            // thread, which spends it below once the submission returns and the subscription
            // actually exists.
            var gate = _readModelGates.GetOrAdd(requestId, static _ => new RequestGate());
            var proceed = false;
            lock (gate.Lock)
            {
                if (_requests.TryGetValue(requestId, out var preSend) && IsActiveStatus(preSend.Status))
                {
                    gate.Submission = SubmissionState.InFlight;
                    proceed = true;
                }
            }

            if (proceed)
            {
                try
                {
                    send(requestId);
                }
                catch
                {
                    lock (gate.Lock)
                    {
                        // The submission never created a subscription, so a wire cancel a
                        // racing terminal transition deferred here has nothing to release.
                        gate.Submission = SubmissionState.Failed;
                        gate.DeferredWireCancel = false;
                    }

                    throw;
                }

                bool releaseAtWire;
                lock (gate.Lock)
                {
                    gate.Submission = SubmissionState.Submitted;
                    releaseAtWire = gate.DeferredWireCancel;
                    gate.DeferredWireCancel = false;
                }

                // A cancel or timeout froze the read model while the send was in flight and
                // deferred its wire cancel here, where the subscription finally exists to be
                // released. Synchronous callbacks raised during the send took the gate for
                // themselves and drained their own publications, so nothing is left parked.
                if (releaseAtWire)
                    _transport.CancelDataRequest(requestId, service);
            }
        }
        catch (Exception transportException)
        {
            try
            {
                UpdateReadModel(requestId, current => current with
                {
                    Status = ProviderDataRequestStatus.Failed,
                    ErrorCode = "transport-submission-failed",
                    ErrorMessage = "The IB transport failed before request submission completed."
                });
            }
            catch (Exception materializationException)
            {
                throw new AggregateException(
                    "The IB transport failed and the terminal request state could not be materialized.",
                    transportException,
                    materializationException);
            }

            throw;
        }

        return requestId;
    }

    private IBDataLineage Update(
        int requestId,
        Func<IBDataLineage, IBDataLineage> update,
        Func<ProviderDataRequestReadModel, IBDataLineage, ProviderDataRequestReadModel>? project = null)
    {
        // The lineage transition shares the request's gate and publication queue with read-model
        // transitions: recorders can run synchronously inside a gated cancel or timeout,
        // and raising LineageUpdated there would put subscriber code under the gate — a
        // subscriber transitioning another request could then entangle two gates into the very
        // cross-request deadlock the queue exists to prevent. Queueing under the gate also keeps
        // two racing lineage updates from delivering stale-last. A caller-supplied projection
        // (the availability recorder's provenance refresh) applies in the SAME read-model
        // transition, so a terminal transition can never land between the lineage embed and its
        // dependent read-model derivation; the embedded Lineage itself comes from
        // UpdateReadModel's wrapper, which reads the map under this same gate hold.
        var gate = _readModelGates.GetOrAdd(requestId, static _ => new RequestGate());
        IBDataLineage updated;
        lock (gate.Lock)
        {
            updated = _lineage.AddOrUpdate(requestId, _ => throw new KeyNotFoundException($"Unknown IB request id {requestId}."), (_, current) => update(current));
            gate.PendingPublications.Enqueue(PendingPublication.For(updated));
            UpdateReadModel(requestId, current => project is null ? current : project(current, updated));
        }

        DrainPublications(gate);
        return updated;
    }

    private void UpdateReadModel(int requestId, Func<ProviderDataRequestReadModel, ProviderDataRequestReadModel> update)
    {
        // The transition and the ORDER of its publication are serialized per request: the map
        // alone would stay consistent (terminal outcomes are frozen in the atomic update below),
        // but a publication escaping the transition's ordering could deliver a stale active model
        // after the terminal one to watchers and the durable projector. The publication itself is
        // only enqueued here and delivered by DrainPublications after the lock is released, so
        // subscriber callbacks never run under any request's gate.
        var gate = _readModelGates.GetOrAdd(requestId, static _ => new RequestGate());
        lock (gate.Lock)
        {
            var lineage = _lineage.TryGetValue(requestId, out var currentLineage) ? currentLineage : null;
            var applied = true;
            var updated = _requests.AddOrUpdate(
                requestId,
                _ => throw new KeyNotFoundException($"Unknown IB request id {requestId}."),
                (_, current) =>
                {
                    // Terminal outcomes are frozen inside the atomic update, not only at the
                    // routability pre-checks: a queued payload or completion racing cancellation,
                    // timeout, or rejection on another thread must not resurrect the read model,
                    // and the first terminal status recorded is the one the operator keeps seeing.
                    if (!IsActiveStatus(current.Status))
                    {
                        applied = false;
                        return current;
                    }

                    applied = true;
                    return update(current) with { UpdatedAt = DateTimeOffset.UtcNow, Lineage = lineage };
                });
            if (applied)
                gate.PendingPublications.Enqueue(PendingPublication.For(updated));
        }

        DrainPublications(gate);
    }

    /// <summary>
    /// Delivers a request's pending publications — read-model updates and lineage notifications —
    /// in transition order with no gate held. A thread that still holds the gate (a cancel or
    /// timeout wrapping a transition, or a reentrant callback) skips delivery —
    /// the outermost holder drains after releasing — and the Draining flag hands the queue to
    /// exactly one drainer at a time, so per-request order is preserved while subscriber
    /// callbacks can transition other requests without ever forming a cross-request lock cycle.
    /// </summary>
    private void DrainPublications(RequestGate gate)
    {
        if (Monitor.IsEntered(gate.Lock))
        {
            return;
        }

        while (true)
        {
            PendingPublication next;
            lock (gate.Lock)
            {
                if (gate.Draining || gate.PendingPublications.Count == 0)
                {
                    return;
                }

                gate.Draining = true;
                next = gate.PendingPublications.Dequeue();
            }

            try
            {
                if (next.Model is { } model)
                {
                    Publish(model);
                }
                else if (next.Lineage is { } lineage)
                {
                    PublishLineageUpdated(lineage);
                }
            }
            finally
            {
                lock (gate.Lock)
                {
                    gate.Draining = false;
                }
            }
        }
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
        var correlationId = _requestCorrelationIds.TryGetValue(lineage.RequestId, out var capturedCorrelationId)
            ? capturedCorrelationId
            : throw new KeyNotFoundException($"Unknown IB request correlation for request id {lineage.RequestId}.");
        var keyMaterial = string.Join("|", ProviderId, _providerConnectionId, providerNativeId, descriptor, correlationId);
        var deduplicationKey = Sha256Digest.ComputeUtf8(keyMaterial);
        return new ProviderDataProvenance(ProviderId, _providerConnectionId, sourceTimestamp, receiptTimestamp ?? DateTimeOffset.UtcNow,
            availability == nameof(IBMarketDataAvailability.Unknown) ? "unknown" : "reported", lineage.Subscription ?? "unspecified",
            availability, descriptor, providerNativeId, correlationId, deduplicationKey);
    }

    private void Publish(ProviderDataRequestReadModel model)
    {
        _ownership.TryGetValue(model.RequestId, out var capturedOwnership);
        if (_projector is not null)
        {
            var ownership = capturedOwnership
                ?? throw new InvalidOperationException(
                    $"Durable IB callback {model.RequestId} has no captured tenant and company ownership.");
            // The standalone lineage MUST be the snapshot the model itself carries, not the live
            // dictionary: a callback recording newer lineage while this queued model drains would
            // otherwise pair an older read model with newer standalone evidence in one durable
            // materialization — internally inconsistent, and retained if the process dies before
            // the next queued publication lands. Per-request delivery is FIFO, so the final
            // materialization still carries the newest model with its own newest lineage.
            _projector.Materialize(ownership, model, model.Lineage);
        }

        _updates.Publish(capturedOwnership, model);
        if (capturedOwnership is null)
        {
            ReadModelUpdated?.Invoke(model);
        }
    }

    private void PublishLineageUpdated(IBDataLineage lineage)
    {
        if (!_ownership.ContainsKey(lineage.RequestId))
        {
            LineageUpdated?.Invoke(lineage);
        }
    }

    private static IReadOnlyList<T> Append<T>(IReadOnlyList<T>? existing, T value)
        => existing is null ? [value] : [.. existing, value];

    public void Dispose()
    {
        if (_transport is IIBDataLineageSource source)
            source.MarketDataTypeReceived -= OnMarketDataTypeReceived;
        if (_callbackSource is { } callbacks)
        {
            callbacks.ContractDetailsReceived -= OnContractDetailsReceived;
            callbacks.OptionChainDefinitionReceived -= OnOptionChainDefinitionReceived;
            callbacks.HistoricalNewsReceived -= OnHistoricalNewsReceived;
            callbacks.NewsArticleReceived -= OnNewsArticleReceived;
            callbacks.FundamentalReportReceived -= OnFundamentalReportReceived;
            callbacks.TickByTickReceived -= OnTickByTickReceived;
            callbacks.DepthExchangesReceived -= OnDepthExchangesReceived;
            callbacks.DividendEarningsReceived -= OnDividendEarningsReceived;
            callbacks.OptionContractReceived -= OnOptionContractReceived;
            callbacks.ScannerResultReceived -= OnScannerResultReceived;
            callbacks.ScannerBatchCompleted -= OnScannerBatchCompleted;
            callbacks.RealTimeBarReceived -= OnRealTimeBarReceived;
            callbacks.HistoricalTickReceived -= OnHistoricalTickReceived;
            callbacks.PnlReceived -= OnPnlReceived;
            callbacks.MarketRuleReceived -= OnMarketRuleReceived;
            callbacks.RequestCompleted -= OnRequestCompleted;
            callbacks.RequestRejected -= OnRequestRejected;
        }
        _updates.Complete();
    }

    private static string RequireSymbol(SymbolConfig contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.Symbol))
            throw new ArgumentException("IB contract symbol is required.", nameof(contract));
        return contract.Symbol;
    }
}
