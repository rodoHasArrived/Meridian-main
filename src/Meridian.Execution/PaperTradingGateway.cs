using Meridian.Execution.Logging;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Meridian.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Simulated execution gateway for paper trading. Orders are matched against observed
/// market data under <see cref="PaperOrderMatchingPolicy"/>: market orders fill from the
/// observed bid/ask/trade/bar in effect,
/// limit orders fill only at or better than their limit price, stop orders trigger per the
/// documented trade-preferred policy, and unmarketable limit/stop orders rest in this
/// gateway and re-evaluate as new market data arrives — resting fills are delivered
/// through <see cref="StreamExecutionReportsAsync"/>. Commission, fee, and slippage costs
/// from <see cref="PaperTradingCostModel"/> apply to every fill. Market orders with no
/// observed price are rejected, unless scaffold pricing is explicitly enabled via
/// <see cref="Adapters.PaperTradingGatewayOptions.AllowScaffoldMarketFills"/>, in which
/// case they fill at the configured scaffold notional price. Cancel and modify act only on
/// orders currently resting in this gateway; an unknown order id is rejected rather than
/// acknowledged.
/// </summary>
public sealed class PaperTradingGateway :
    IExecutionGateway,
    IExecutionGatewayModeProvider,
    IFaceValueOrderSizingGateway,
    Interfaces.IPaperFillEvaluationTrigger,
    IAsyncDisposable
{
    private readonly ILogger<PaperTradingGateway> _logger;
    private readonly Adapters.PaperTradingGatewayTradingParameters _tradingParameters;
    private readonly decimal _scaffoldMarketFillPrice;
    private readonly bool _allowScaffoldMarketFills;
    private readonly Interfaces.ILiveFeedAdapter? _liveFeed;
    private readonly PaperTradingCostModel _costModel;
    private readonly Meridian.Contracts.FixedIncome.IBondAnalyticsService? _bondAnalytics;
    private readonly bool _requireBondAccruedInterest;
    private readonly PaperSymbolEvaluationPump _evaluationPump;
    private readonly System.Threading.Channels.Channel<ExecutionReport> _restingReports;
    private readonly CancellationTokenSource _disposeCts = new();
    // Limit/stop orders accepted and resting in this gateway, keyed by order id, so
    // cancel/modify can distinguish a real resting order from an unknown id and so market
    // data arrival can re-evaluate them for fills.
    private readonly ConcurrentDictionary<string, RestingPaperOrder> _restingOrders = new(StringComparer.Ordinal);
    private int _scaffoldPriceWarningIssued;
    private readonly PaperObservationFreshnessPolicy _freshness;
    private bool _connected;
    private int _fillSequence;
    private int _disposed;
    private bool _canonicalSyntheticPricing;

    /// <summary>The canonical synthetic OMS supplies its reviewed, bounded simulated price.
    /// Native paper orders continue to use market observations rather than caller limits.</summary>
    internal void EnableCanonicalSyntheticPricing()
    {
        if (Volatile.Read(ref _fillSequence) != 0 || !_restingOrders.IsEmpty)
            throw new InvalidOperationException("Canonical synthetic pricing requires an unused paper gateway.");
        _canonicalSyntheticPricing = true;
    }

    /// <summary>One resting limit/stop order, with its stop-trigger state.</summary>
    private sealed class RestingPaperOrder
    {
        public RestingPaperOrder(OrderRequest request, string gatewayOrderId)
        {
            Request = request;
            GatewayOrderId = gatewayOrderId;
        }

        /// <summary>The gateway id reported on the New report; every later report for this order reuses it.</summary>
        public string GatewayOrderId { get; }

        /// <summary>Current request (replaced atomically under the instance lock on modify).</summary>
        public OrderRequest Request { get; set; }

        /// <summary>Set once the stop trigger condition has been met (never re-armed).</summary>
        public bool StopTriggered { get; set; }

        /// <summary>Serializes fill/modify decisions for this order.</summary>
        public object SyncRoot { get; } = new();

        /// <summary>True once a terminal report (fill) has been emitted for this order.</summary>
        public bool Completed { get; set; }
    }

    /// <summary>Retains the aggregate-query constructor used by compiled consumers.</summary>
    public PaperTradingGateway(
        ILogger<PaperTradingGateway> logger,
        ISecurityMasterQueryService? securityMaster = null,
        Adapters.PaperTradingGatewayOptions? options = null,
        Interfaces.ILiveFeedAdapter? liveFeed = null,
        PaperTradingCostOptions? costOptions = null)
        : this(logger, (ISecurityTradingParametersQueryService?)securityMaster, options, liveFeed, costOptions, securityMaster)
    {
    }

    public PaperTradingGateway(
        ILogger<PaperTradingGateway> logger,
        ISecurityTradingParametersQueryService? securityMaster,
        Adapters.PaperTradingGatewayOptions? options = null,
        Interfaces.ILiveFeedAdapter? liveFeed = null,
        PaperTradingCostOptions? costOptions = null,
        ISecurityIdentityQueryService? securityIdentity = null,
        Meridian.Contracts.FixedIncome.IBondAnalyticsService? bondAnalytics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bondAnalytics = bondAnalytics;
        _requireBondAccruedInterest = options?.RequireBondAccruedInterest == true;
        _tradingParameters = new Adapters.PaperTradingGatewayTradingParameters(
            _logger,
            securityMaster,
            LogLevel.Warning,
            securityIdentity);
        _scaffoldMarketFillPrice = Adapters.PaperTradingGatewayScaffoldPricing.ResolveScaffoldMarketFillPrice(options);
        _allowScaffoldMarketFills = Adapters.PaperTradingGatewayScaffoldPricing.ResolveAllowScaffoldMarketFills(options);
        _freshness = PaperObservationFreshnessPolicy.From(options);
        _liveFeed = liveFeed;
        _costModel = new PaperTradingCostModel(costOptions);
        _evaluationPump = new PaperSymbolEvaluationPump(EvaluateRestingOrdersForSymbolAsync, _logger);
        _restingReports = EventPipelinePolicy.CompletionQueue.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    /// <inheritdoc />
    public string GatewayId => "paper";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.Paper;

    /// <summary>
    /// Mirrors the live brokerage convention (see <c>AlpacaBrokerageGateway</c>): a treasury or
    /// corporate fixed-income order routes its quantity as face value with prices quoted as a
    /// percentage of par. Without this capability on the supported paper path, a 100,000-face
    /// order at 101.25 is measured by the pre-trade rails and booked by the paper book as
    /// $10,125,000 instead of $101,250 — the paper simulation must exercise the same sizing
    /// semantics the live gateway routes.
    /// </summary>
    public bool UsesFaceValuePercentageOfPar(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Metadata is null)
        {
            return false;
        }

        // Same alias order as the live gateway, and matched case-insensitively regardless of
        // the caller-supplied dictionary's comparer.
        foreach (var key in (string[])["asset_class", "assetClass", "alpaca:asset_class"])
        {
            foreach (var (metadataKey, assetClass) in request.Metadata)
            {
                if (string.Equals(metadataKey, key, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(assetClass))
                {
                    // The live gateway's fixed-income codes and aliases, from the shared table.
                    return ProviderAssetClassCodeCatalog.IsFaceValueFixedIncomeInput(
                        ProviderAssetClassCodeProviders.Alpaca,
                        assetClass);
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _logger.LogInformation("Paper trading gateway connected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("Paper trading gateway disconnected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (_canonicalSyntheticPricing && (request.Type != OrderType.Market || request.LimitPrice is not > 0m))
            throw new InvalidOperationException("Canonical synthetic pricing requires an immediate market order with an explicit positive simulated price.");
        var fillSeq = Interlocked.Increment(ref _fillSequence);

        var lotSizeError = await _tradingParameters.ValidateLotSizeAsync(request, ct).ConfigureAwait(false);
        if (lotSizeError is not null)
        {
            throw new InvalidOperationException(lotSizeError);
        }

        if (request.Type is OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose)
        {
            throw new NotSupportedException(
                $"Paper trading gateway does not preserve the {request.Type} session timing qualifier.");
        }

        var orderId = request.ClientOrderId ?? $"PAPER-{fillSeq}";
        var gatewayOrderId = $"PAPER-{fillSeq}";

        // Native market orders use observed market data. Only the separately authorized
        // canonical synthetic host treats its required explicit price as a simulated observation.
        if (request.Type is OrderType.Market)
        {
            var observation = _canonicalSyntheticPricing
                ? PaperMarketObservation.FromCallerPrice(request.LimitPrice!.Value)
                : _freshness.Capture(_liveFeed, request.Symbol);

            var match = PaperOrderMatchingPolicy.Evaluate(
                request.Side, OrderType.Market, limitPrice: null, stopPrice: null,
                stopAlreadyTriggered: false, observation);

            if (match.Outcome is PaperMatchOutcome.Filled)
            {
                return await BuildFillReportAsync(
                    request, orderId, gatewayOrderId, match.FillPrice!.Value, observation, ct)
                    .ConfigureAwait(false);
            }

            // A stalled feed is not "no feed wired": scaffold pricing must not stand in.
            if (observation.IsStale || !_allowScaffoldMarketFills)
            {
                var rejectReason = observation.IsStale
                    ? _freshness.BuildStaleObservationRejectReason(request.Symbol)
                    : Adapters.PaperTradingGatewayScaffoldPricing
                        .BuildNoReferencePriceRejectReason(request.Symbol);
                _logger.LogWarning(
                    "Paper order rejected: {Symbol} {Side} {Quantity} — {RejectReason}",
                    LogSanitizer.Sanitize(request.Symbol), request.Side, request.Quantity, LogSanitizer.Sanitize(rejectReason));

                return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Rejected,
                    OrderStatus.Rejected, filledQuantity: 0m, fillPrice: null, costs: null, rejectReason);
            }

            WarnScaffoldPriceUsed();
            return await BuildFillReportAsync(
                request, orderId, gatewayOrderId, _scaffoldMarketFillPrice,
                PaperMarketObservation.FromCallerPrice(_scaffoldMarketFillPrice), ct).ConfigureAwait(false);
        }

        // Limit/stop orders: evaluate against the current observation; marketable orders
        // fill now, others rest for re-evaluation as market data arrives. Resting orders
        // are keyed solely by id, so a duplicate id would collide in the tracking map and
        // become impossible to cancel/modify independently — reject it instead of
        // accepting an order the gateway cannot manage.
        var resting = new RestingPaperOrder(request with { ClientOrderId = orderId }, gatewayOrderId);
        if (!_restingOrders.TryAdd(orderId, resting))
        {
            return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Rejected,
                OrderStatus.Rejected, filledQuantity: 0m, fillPrice: null, costs: null,
                "An order with this client order id is already resting in the paper gateway.");
        }

        var initialObservation = _freshness.Capture(_liveFeed, request.Symbol);
        var initialMatch = PaperOrderMatchingPolicy.Evaluate(
            request.Side, request.Type, request.LimitPrice, request.StopPrice,
            stopAlreadyTriggered: false, initialObservation);

        // The order is already visible to the background evaluator and to CancelOrderAsync,
        // so every terminal outcome decided here must claim it through TryCompleteResting.
        // If another path claimed it first, that path owns the terminal report (a fill on
        // the resting stream or the cancel result); report the order as accepted only.
        if (initialMatch.Outcome is PaperMatchOutcome.Filled)
        {
            if (!TryCompleteResting(orderId, out _))
            {
                return BuildAcceptedReport(request, orderId, gatewayOrderId);
            }

            return await BuildFillReportAsync(
                request, orderId, gatewayOrderId, initialMatch.FillPrice!.Value, initialObservation, ct)
                .ConfigureAwait(false);
        }

        if (request.TimeInForce is TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill)
        {
            if (!TryCompleteResting(orderId, out _))
            {
                return BuildAcceptedReport(request, orderId, gatewayOrderId);
            }

            return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Cancelled,
                OrderStatus.Cancelled, filledQuantity: 0m, fillPrice: null, costs: null,
                $"{request.TimeInForce} order was not immediately fillable against observed market data.");
        }

        if (initialMatch.StopTriggered)
        {
            lock (resting.SyncRoot)
            {
                resting.StopTriggered = true;
            }
        }

        return BuildAcceptedReport(request, orderId, gatewayOrderId);
    }

    private ExecutionReport BuildAcceptedReport(OrderRequest request, string orderId, string gatewayOrderId) =>
        BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.New,
            OrderStatus.Accepted, filledQuantity: 0m, fillPrice: null, costs: null, rejectReason: null);

    /// <inheritdoc />
    public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!TryCompleteResting(orderId, out _))
        {
            // No resting order with this id: unknown, already filled (market order), or already
            // cancelled. Report a rejection rather than fabricating a successful cancellation.
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = "No resting paper order with this id to cancel.",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        return Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc />
    public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modification);

        // A zero or negative replacement would rest an order that can never be valid (and a
        // zero-quantity order could "fill" for nothing); refuse it before touching the order.
        if (modification.NewQuantity is <= 0m || modification.NewLimitPrice is <= 0m || modification.NewStopPrice is <= 0m)
        {
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = "Modified quantity and prices must be positive.",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        if (!_restingOrders.TryGetValue(orderId, out var resting))
        {
            // Only orders still resting in this gateway can be modified; reject unknown ids
            // rather than fabricating an acceptance.
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = "No resting paper order with this id to modify.",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        OrderRequest modified;
        lock (resting.SyncRoot)
        {
            if (resting.Completed)
            {
                return Task.FromResult(new ExecutionReport
                {
                    OrderId = orderId,
                    ReportType = ExecutionReportType.Rejected,
                    Symbol = resting.Request.Symbol,
                    Side = resting.Request.Side,
                    OrderStatus = OrderStatus.Rejected,
                    RejectReason = "The paper order already reached a terminal state.",
                    Timestamp = DateTimeOffset.UtcNow
                });
            }

            modified = resting.Request with
            {
                Quantity = modification.NewQuantity ?? resting.Request.Quantity,
                LimitPrice = modification.NewLimitPrice ?? resting.Request.LimitPrice,
                StopPrice = modification.NewStopPrice ?? resting.Request.StopPrice
            };
            resting.Request = modified;
        }

        // A price change can make the order immediately marketable — re-evaluate.
        EvaluateSymbol(modified.Symbol);

        return Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = modified.Symbol,
            Side = modified.Side,
            OrderStatus = OrderStatus.Accepted,
            OrderQuantity = modified.Quantity,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Immediate (market / marketable-limit) fills are returned synchronously from
        // SubmitOrderAsync; this stream carries fills of resting limit/stop orders that
        // become marketable as market data arrives.
        await foreach (var report in _restingReports.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    /// <inheritdoc />
    public void EvaluateSymbol(string symbol)
    {
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        var hasWorkForSymbol = _restingOrders.Values.Any(order =>
            string.Equals(order.Request.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (hasWorkForSymbol)
        {
            _evaluationPump.Poke(symbol);
        }
    }

    private async Task EvaluateRestingOrdersForSymbolAsync(string symbol)
    {
        foreach (var (orderId, resting) in _restingOrders)
        {
            if (!string.Equals(resting.Request.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            OrderRequest request;
            bool stopTriggered;
            lock (resting.SyncRoot)
            {
                if (resting.Completed)
                {
                    continue;
                }

                request = resting.Request;
                stopTriggered = resting.StopTriggered;
            }

            var observation = _freshness.Capture(_liveFeed, request.Symbol);
            var match = PaperOrderMatchingPolicy.Evaluate(
                request.Side, request.Type, request.LimitPrice, request.StopPrice,
                stopTriggered, observation);

            if (match.Outcome is PaperMatchOutcome.Filled)
            {
                if (!TryCompleteResting(orderId, out _))
                {
                    continue;
                }

                var report = await BuildFillReportAsync(
                    request, orderId, resting.GatewayOrderId, match.FillPrice!.Value, observation,
                    CancellationToken.None).ConfigureAwait(false);

                await EmitRestingReportAsync(report, orderId).ConfigureAwait(false);
            }
            else if (match.StopTriggered)
            {
                lock (resting.SyncRoot)
                {
                    resting.StopTriggered = true;
                }
            }
        }
    }

    /// <summary>
    /// Queues the fill of a resting order that has already been claimed as complete. The
    /// report channel is bounded with wait-on-full semantics, so <c>TryWrite</c> fails
    /// whenever the consumer is behind; dropping the report there would lose the fill for
    /// good. Wait for capacity instead, giving up only when the gateway is disposed.
    /// </summary>
    private async ValueTask EmitRestingReportAsync(ExecutionReport report, string orderId)
    {
        if (_restingReports.Writer.TryWrite(report))
        {
            return;
        }

        try
        {
            await _restingReports.Writer.WriteAsync(report, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Threading.Channels.ChannelClosedException)
        {
            _logger.LogWarning(
                "Paper resting fill for {OrderId} was dropped because the gateway was disposed before the report channel had capacity.",
                LogSanitizer.Sanitize(orderId));
        }
    }

    /// <summary>
    /// Atomically claims a resting order for terminal completion (fill or cancel), so a
    /// concurrent evaluation and cancel cannot both emit a terminal report.
    /// </summary>
    private bool TryCompleteResting(string orderId, out RestingPaperOrder? order)
    {
        if (!_restingOrders.TryGetValue(orderId, out var resting))
        {
            order = null;
            return false;
        }

        lock (resting.SyncRoot)
        {
            if (resting.Completed)
            {
                order = null;
                return false;
            }

            resting.Completed = true;
        }

        _restingOrders.TryRemove(orderId, out _);
        order = resting;
        return true;
    }

    private async Task<ExecutionReport> BuildFillReportAsync(
        OrderRequest request,
        string orderId,
        string gatewayOrderId,
        decimal fillPrice,
        PaperMarketObservation observation,
        CancellationToken ct)
    {
        fillPrice = await _tradingParameters.ResolveFillPriceAsync(request, fillPrice, observation, ct)
            .ConfigureAwait(false);

        // Resolve the lot convention first so notional-based costs use the same multiplied
        // principal the paper book will move. Face value keeps its existing raw sizing here;
        // without a convention the OMS's own resolution (100 for an option) applies.
        var lotConvention = await _tradingParameters.ResolveLotConventionAsync(request.Symbol, ct).ConfigureAwait(false);
        var costMultiplier = lotConvention is { } resolved
            ? (resolved.UnitKind == SecurityLotUnitKind.FaceValue ? 1m : resolved.Multiplier)
            : OrderRiskSizingPolicy.ResolveContractMultiplier(request);
        var costs = _costModel.Compute(request.Quantity, fillPrice, observation.MidPrice, costMultiplier);

        var report = BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Fill,
            OrderStatus.Filled, filledQuantity: request.Quantity, fillPrice, costs, rejectReason: null) with
        {
            MarketDataEvidence = Meridian.Contracts.Operations.MarketDataInputEvidenceTags.Freeze(observation.MarketDataEvidence)
        };
        if (lotConvention is { } convention)
        {
            report = report with
            {
                AssetClass = convention.AssetClass,
                LotUnitKind = convention.UnitKind,
                LotMultiplier = convention.Multiplier,
                UsesFaceValuePercentageOfPar = convention.UnitKind == SecurityLotUnitKind.FaceValue
            };
            if (convention.UnitKind == SecurityLotUnitKind.FaceValue)
                report = await StampAccruedInterestAsync(report, convention.SecurityId, fillPrice, ct).ConfigureAwait(false);
        }
        else if (request.OptionContract is not null)
        {
            report = report with { LotUnitKind = SecurityLotUnitKind.Contracts };
        }

        if (_requireBondAccruedInterest
            && (report.UsesFaceValuePercentageOfPar || UsesFaceValuePercentageOfPar(request))
            && (report.AccruedInterestPer100 is null || report.SettlementDate is null))
        {
            // A terminal rejection goes through the same immediate/resting report path as a
            // fill. No portfolio, cash or accounting event may observe a clean-only fill in
            // this explicitly supported mode. Historical reports remain replayable unchanged.
            return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Rejected,
                OrderStatus.Rejected, filledQuantity: 0m, fillPrice: null, costs: null,
                "Bond settlement unavailable: required accrued interest could not be verified for the security and settlement date. "
                + "Review the bond terms and analytics before resubmitting (PaperTrading:Gateway:RequireBondAccruedInterest).");
        }

        _logger.LogInformation(
            "Paper fill: {Symbol} {Side} {Quantity} @ {Price} (commission {Commission}, fees {Fees}, slippage {Slippage})",
            LogSanitizer.Sanitize(request.Symbol), request.Side, request.Quantity, report.FillPrice,
            costs.Commission, costs.Fees, costs.SlippageCost);

        return report;
    }

    /// <summary>
    /// Prices the accrued interest a face-value bond fill settles with, through the shared bond
    /// analytics kernel at the fill's clean price and its contractual settlement date. When no
    /// analytics service is composed, or the kernel refuses the bond (unsupported coupon
    /// structure, undeclared schedule, settlement outside its life), the fill keeps its clean
    /// settlement and the refusal is logged rather than guessed at. The opt-in required-accrual
    /// mode rejects that unstamped outcome before it can become an economic fill.
    /// </summary>
    private async Task<ExecutionReport> StampAccruedInterestAsync(
        ExecutionReport report, Guid securityId, decimal cleanPricePer100, CancellationToken ct)
    {
        if (_bondAnalytics is null || securityId == Guid.Empty)
            return report;

        var tradeTime = report.Timestamp.ToUniversalTime();
        var settlement = PaperBondSettlement.SettlementDate(DateOnly.FromDateTime(tradeTime.UtcDateTime));
        try
        {
            var result = await _bondAnalytics.CalculateAsync(
                new Meridian.Contracts.FixedIncome.BondAnalyticsRequest(
                    securityId,
                    new Meridian.Contracts.FixedIncome.BondCalculationContext(tradeTime, settlement),
                    CleanPrice: cleanPricePer100),
                ct).ConfigureAwait(false);
            if (result is { IsBlocked: false, AccruedInterest: { } accruedPer100 } && accruedPer100 >= 0m
                && (!_requireBondAccruedInterest ||
                    (result.SecurityId == securityId && result.Context.AsOf == tradeTime
                     && result.Context.SettlementDate == settlement && result.CleanPrice == cleanPricePer100
                     && result.DirtyPrice == cleanPricePer100 + accruedPer100)))
                return report with { AccruedInterestPer100 = accruedPer100, SettlementDate = settlement };

            _logger.LogWarning(
                "Paper bond settlement {Symbol}: accrued interest unavailable ({Blockers}); required-accrual mode {Required}",
                LogSanitizer.Sanitize(report.Symbol),
                result is null ? "SecurityUnknown" : string.Join(",", result.Blockers.Select(static b => b.Code)),
                _requireBondAccruedInterest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Paper bond settlement {Symbol}: accrued interest calculation failed; required-accrual mode {Required}",
                LogSanitizer.Sanitize(report.Symbol), _requireBondAccruedInterest);
        }

        return report;
    }

    private static ExecutionReport BuildReport(
        OrderRequest request,
        string orderId,
        string gatewayOrderId,
        ExecutionReportType reportType,
        OrderStatus status,
        decimal filledQuantity,
        decimal? fillPrice,
        PaperFillCostBreakdown? costs,
        string? rejectReason)
    {
        return new ExecutionReport
        {
            OrderId = orderId,
            ReportType = reportType,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = status,
            OrderQuantity = request.Quantity,
            FilledQuantity = filledQuantity,
            FillPrice = fillPrice,
            Commission = costs?.Commission,
            Fees = costs?.Fees,
            SlippageCost = costs?.SlippageCost,
            RejectReason = rejectReason,
            Timestamp = DateTimeOffset.UtcNow,
            GatewayOrderId = gatewayOrderId,
            ClientOrderId = request.ClientOrderId ?? orderId
        };
    }

    /// <summary>
    /// Emits a one-time loud warning when a market fill is priced from the scaffold
    /// notional price instead of a caller-provided simulated price.
    /// </summary>
    private void WarnScaffoldPriceUsed()
    {
        Adapters.PaperTradingGatewayScaffoldPricing.WarnIfFirstUse(
            ref _scaffoldPriceWarningIssued,
            _logger,
            _scaffoldMarketFillPrice,
            "Paper execution gateway");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Release an evaluation parked on a full report channel so disposal cannot hang when
        // no consumer is draining.
        await _disposeCts.CancelAsync().ConfigureAwait(false);
        await _evaluationPump.DisposeAsync().ConfigureAwait(false);
        _restingReports.Writer.TryComplete();
        _disposeCts.Dispose();
    }
}
