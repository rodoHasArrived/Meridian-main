using Meridian.Execution.Logging;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Meridian.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Simulated execution gateway for paper trading. Orders are matched against observed
/// market data under <see cref="PaperOrderMatchingPolicy"/>: market orders fill from the
/// observed bid/ask/trade/bar in effect (callers may still supply a simulated mark via
/// <see cref="OrderRequest.LimitPrice"/> on a market order, which becomes the observation),
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
    Interfaces.IPaperFillEvaluationTrigger,
    IAsyncDisposable
{
    private readonly ILogger<PaperTradingGateway> _logger;
    private readonly Adapters.PaperTradingGatewayTradingParameters _tradingParameters;
    private readonly decimal _scaffoldMarketFillPrice;
    private readonly bool _allowScaffoldMarketFills;
    private readonly Interfaces.ILiveFeedAdapter? _liveFeed;
    private readonly PaperTradingCostModel _costModel;
    private readonly PaperSymbolEvaluationPump _evaluationPump;
    private readonly System.Threading.Channels.Channel<ExecutionReport> _restingReports;
    // Limit/stop orders accepted and resting in this gateway, keyed by order id, so
    // cancel/modify can distinguish a real resting order from an unknown id and so market
    // data arrival can re-evaluate them for fills.
    private readonly ConcurrentDictionary<string, RestingPaperOrder> _restingOrders = new(StringComparer.Ordinal);
    private int _scaffoldPriceWarningIssued;
    private bool _connected;
    private int _fillSequence;
    private int _disposed;

    /// <summary>One resting limit/stop order, with its stop-trigger state.</summary>
    private sealed class RestingPaperOrder
    {
        public RestingPaperOrder(OrderRequest request) => Request = request;

        /// <summary>Current request (replaced atomically under the instance lock on modify).</summary>
        public OrderRequest Request { get; set; }

        /// <summary>Set once the stop trigger condition has been met (never re-armed).</summary>
        public bool StopTriggered { get; set; }

        /// <summary>Serializes fill/modify decisions for this order.</summary>
        public object SyncRoot { get; } = new();

        /// <summary>True once a terminal report (fill) has been emitted for this order.</summary>
        public bool Completed { get; set; }
    }

    public PaperTradingGateway(
        ILogger<PaperTradingGateway> logger,
        ISecurityMasterQueryService? securityMaster = null,
        Adapters.PaperTradingGatewayOptions? options = null,
        Interfaces.ILiveFeedAdapter? liveFeed = null,
        PaperTradingCostOptions? costOptions = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tradingParameters = new Adapters.PaperTradingGatewayTradingParameters(
            _logger,
            securityMaster,
            LogLevel.Warning);
        _scaffoldMarketFillPrice = Adapters.PaperTradingGatewayScaffoldPricing.ResolveScaffoldMarketFillPrice(options);
        _allowScaffoldMarketFills = Adapters.PaperTradingGatewayScaffoldPricing.ResolveAllowScaffoldMarketFills(options);
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

        // Market-style orders evaluate immediately. Callers may set a simulated price via
        // LimitPrice on a market order; it becomes the observation. Otherwise the observed
        // live market data applies.
        if (request.Type is OrderType.Market)
        {
            var observation = request.LimitPrice is { } callerPrice
                ? PaperMarketObservation.FromCallerPrice(callerPrice)
                : PaperMarketObservation.Capture(_liveFeed, request.Symbol);

            var match = PaperOrderMatchingPolicy.Evaluate(
                request.Side, OrderType.Market, limitPrice: null, stopPrice: null,
                stopAlreadyTriggered: false, observation);

            if (match.Outcome is PaperMatchOutcome.Filled)
            {
                return await BuildFillReportAsync(
                    request, orderId, gatewayOrderId, match.FillPrice!.Value, observation, ct)
                    .ConfigureAwait(false);
            }

            if (!_allowScaffoldMarketFills)
            {
                var rejectReason = Adapters.PaperTradingGatewayScaffoldPricing
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
        var resting = new RestingPaperOrder(request with { ClientOrderId = orderId });
        if (!_restingOrders.TryAdd(orderId, resting))
        {
            return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Rejected,
                OrderStatus.Rejected, filledQuantity: 0m, fillPrice: null, costs: null,
                "An order with this client order id is already resting in the paper gateway.");
        }

        var initialObservation = PaperMarketObservation.Capture(_liveFeed, request.Symbol);
        var initialMatch = PaperOrderMatchingPolicy.Evaluate(
            request.Side, request.Type, request.LimitPrice, request.StopPrice,
            stopAlreadyTriggered: false, initialObservation);

        if (initialMatch.Outcome is PaperMatchOutcome.Filled)
        {
            _restingOrders.TryRemove(orderId, out _);
            return await BuildFillReportAsync(
                request, orderId, gatewayOrderId, initialMatch.FillPrice!.Value, initialObservation, ct)
                .ConfigureAwait(false);
        }

        if (request.TimeInForce is TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill)
        {
            _restingOrders.TryRemove(orderId, out _);
            return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Cancelled,
                OrderStatus.Cancelled, filledQuantity: 0m, fillPrice: null, costs: null,
                $"{request.TimeInForce} order was not immediately fillable against observed market data.");
        }

        resting.StopTriggered = initialMatch.StopTriggered;

        return BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.New,
            OrderStatus.Accepted, filledQuantity: 0m, fillPrice: null, costs: null, rejectReason: null);
    }

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

            var observation = PaperMarketObservation.Capture(_liveFeed, request.Symbol);
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
                    request, orderId, gatewayOrderId: orderId, match.FillPrice!.Value, observation,
                    CancellationToken.None).ConfigureAwait(false);

                if (!_restingReports.Writer.TryWrite(report))
                {
                    _logger.LogWarning(
                        "Paper resting fill for {OrderId} could not be queued because the report channel was unavailable.",
                        LogSanitizer.Sanitize(orderId));
                }
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
        fillPrice = await _tradingParameters.SnapToTickSizeAsync(request.Symbol, fillPrice, ct)
            .ConfigureAwait(false);

        var costs = _costModel.Compute(request.Quantity, fillPrice, observation.MidPrice);

        var report = BuildReport(request, orderId, gatewayOrderId, ExecutionReportType.Fill,
            OrderStatus.Filled, filledQuantity: request.Quantity, fillPrice, costs, rejectReason: null);

        _logger.LogInformation(
            "Paper fill: {Symbol} {Side} {Quantity} @ {Price} (commission {Commission}, fees {Fees}, slippage {Slippage})",
            LogSanitizer.Sanitize(request.Symbol), request.Side, request.Quantity, report.FillPrice,
            costs.Commission, costs.Fees, costs.SlippageCost);

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

        await _evaluationPump.DisposeAsync().ConfigureAwait(false);
        _restingReports.Writer.TryComplete();
    }
}
