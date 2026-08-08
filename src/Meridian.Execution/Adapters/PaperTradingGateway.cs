using Meridian.Execution.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Models;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using Meridian.Core.Pipeline;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Simulated order gateway that routes no real orders to any exchange. Orders are matched
/// against observed market data under <see cref="PaperOrderMatchingPolicy"/>: market
/// orders fill from the observed bid/ask/trade/bar in effect, limit orders fill only at
/// or better than their limit price, stop orders trigger per the documented trade-preferred
/// policy, and unmarketable orders rest and re-evaluate as new market data arrives.
/// Commission, fee, and slippage costs from <see cref="PaperTradingCostModel"/> apply to
/// every fill. Market orders with no observed reference price are rejected unless scaffold
/// notional pricing is explicitly enabled via
/// <see cref="PaperTradingGatewayOptions.AllowScaffoldMarketFills"/>.
/// Implements ADR-015.
/// </summary>
[ImplementsAdr("ADR-015", "Simulated IOrderGateway over live Meridian feed — no real orders")]
public sealed class PaperTradingGateway : IOrderGateway, Interfaces.IPaperFillEvaluationTrigger
{
    // Notional fallback fill price used for market orders when no live feed price is
    // available, configurable via PaperTradingGatewayOptions. When a live feed adapter
    // is supplied, market fills are priced from observed market data. Scaffold pricing
    // is opt-in: without it, priceless market orders are rejected.
    private readonly decimal _scaffoldMarketFillPrice;
    private readonly bool _allowScaffoldMarketFills;
    private readonly Interfaces.ILiveFeedAdapter? _liveFeed;
    private int _scaffoldPriceWarningIssued;

    private readonly ILogger<PaperTradingGateway> _logger;
    private readonly PaperTradingGatewayTradingParameters _tradingParameters;
    private readonly PaperTradingCostModel _costModel;
    private readonly PaperSymbolEvaluationPump _evaluationPump;
    private readonly System.Threading.Channels.Channel<OrderStatusUpdate> _updates;
    private readonly Dictionary<string, WorkingPaperOrder> _workingOrders = new();
    private readonly ConcurrentDictionary<string, Task> _fillTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private int _disposed;

    /// <summary>One order working in the paper gateway, with its stop-trigger state.</summary>
    private sealed class WorkingPaperOrder
    {
        public WorkingPaperOrder(OrderRequest request) => Request = request;

        public OrderRequest Request { get; }

        /// <summary>Set once the stop trigger condition has been met (never re-armed).</summary>
        public bool StopTriggered { get; set; }
    }

    /// <inheritdoc/>
    public string BrokerName => "Paper";

    /// <inheritdoc/>
    public GatewayExecutionMode Mode => GatewayExecutionMode.Paper;

    /// <inheritdoc/>
    public OrderGatewayCapabilities Capabilities { get; } = new(
        SupportedOrderTypes: new HashSet<OrderType>
        {
            OrderType.Market,
            OrderType.Limit,
            OrderType.StopMarket,
            OrderType.StopLimit
        },
        SupportedTimeInForce: new HashSet<TimeInForce>
        {
            TimeInForce.Day,
            TimeInForce.GoodTilCancelled,
            TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill
        },
        SupportedExecutionModes: new HashSet<GatewayExecutionMode>
        {
            GatewayExecutionMode.Paper,
            GatewayExecutionMode.Simulation
        },
        SupportsOrderModification: false,
        SupportsPartialFills: false,
        ProviderExtensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["priceSource"] = "observed-market-data",
            ["supportsNativeTrailingStops"] = "false",
            ["matchingModel"] = PaperOrderMatchingPolicy.MatchingModelVersion,
            ["costModel"] = PaperTradingCostModel.CostModelVersion
        });

    /// <summary>
    /// Creates a new paper trading gateway.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="securityMaster">
    /// Optional Security Master query service. When provided, lot-size validation and
    /// tick-size price rounding are applied on a best-effort basis.
    /// </param>
    /// <param name="options">
    /// Optional gateway options. When omitted, defaults apply (including the notional
    /// scaffold market fill price).
    /// </param>
    /// <param name="liveFeed">
    /// Optional live feed adapter. When provided, orders match against the observed
    /// market data (quotes, trades, bars) instead of the scaffold notional price.
    /// </param>
    /// <param name="costOptions">
    /// Optional transaction-cost options. When omitted, the default per-share commission
    /// schedule applies (see <see cref="PaperTradingCostOptions"/>).
    /// </param>
    public PaperTradingGateway(
        ILogger<PaperTradingGateway> logger,
        ISecurityMasterQueryService? securityMaster = null,
        PaperTradingGatewayOptions? options = null,
        Interfaces.ILiveFeedAdapter? liveFeed = null,
        PaperTradingCostOptions? costOptions = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tradingParameters = new PaperTradingGatewayTradingParameters(
            _logger,
            securityMaster,
            LogLevel.Debug);
        _scaffoldMarketFillPrice = PaperTradingGatewayScaffoldPricing.ResolveScaffoldMarketFillPrice(options);
        _allowScaffoldMarketFills = PaperTradingGatewayScaffoldPricing.ResolveAllowScaffoldMarketFills(options);
        _liveFeed = liveFeed;
        _costModel = new PaperTradingCostModel(costOptions);
        _evaluationPump = new PaperSymbolEvaluationPump(EvaluateRestingOrdersForSymbolAsync, _logger);
        // Use EventPipelinePolicy for consistent backpressure settings across the platform (ADR-013).
        // Disposal waits for in-flight fills before completing this bounded update channel.
        _updates = EventPipelinePolicy.CompletionQueue.CreateChannel<OrderStatusUpdate>(
            singleReader: false, singleWriter: false);
    }

    /// <inheritdoc/>
    public async Task<OrderAcknowledgement> SubmitAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var validation = await ValidateOrderAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new UnsupportedOrderRequestException(validation.Reason ?? "Order request is not supported by the paper gateway.");
        }

        var orderId = request.ClientOrderId ?? $"paper-{Guid.NewGuid():N}";

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var trackedRequest = request with { ClientOrderId = orderId };
            // Reject a duplicate id rather than overwriting the working entry: two fill tasks would
            // share one dictionary key, and once the first removes it the second would skip its
            // terminal update, leaving an accepted order with no fill or cancel event.
            if (!_workingOrders.TryAdd(orderId, new WorkingPaperOrder(trackedRequest)))
            {
                throw new UnsupportedOrderRequestException(
                    $"An order with client order id '{orderId}' is already working in the paper gateway.");
            }
            TrackFillSimulationLocked(orderId, trackedRequest);
        }

        _logger.LogInformation(
            "Paper order accepted: {ClientOrderId} {Quantity} {Symbol} @ {Type}",
            LogSanitizer.Sanitize(orderId), request.Quantity, LogSanitizer.Sanitize(request.Symbol), request.Type);

        var ack = new OrderAcknowledgement(
            OrderId: orderId,
            ClientOrderId: orderId,
            Symbol: request.Symbol,
            Status: GatewayOrderStatus.Accepted,
            AcknowledgedAt: DateTimeOffset.UtcNow);

        return ack;
    }

    /// <inheritdoc/>
    public async Task<OrderValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Capabilities.SupportedOrderTypes.Contains((OrderType)request.Type))
        {
            return new OrderValidationResult(false, $"Order type '{request.Type}' is not supported by the paper gateway.");
        }

        if (!Capabilities.SupportedTimeInForce.Contains(request.TimeInForce))
        {
            return new OrderValidationResult(false, $"Time in force '{request.TimeInForce}' is not supported by the paper gateway.");
        }

        if (request.Quantity == 0)
        {
            return new OrderValidationResult(false, "Order quantity cannot be zero.");
        }

        if (((OrderType)request.Type is OrderType.Limit or OrderType.StopLimit)
            && (!request.LimitPrice.HasValue || request.LimitPrice <= 0))
        {
            return new OrderValidationResult(false, "Limit-style orders require a positive limit price.");
        }

        if (((OrderType)request.Type is OrderType.StopMarket or OrderType.StopLimit) && (!request.StopPrice.HasValue || request.StopPrice <= 0))
        {
            return new OrderValidationResult(false, "Stop and stop-limit orders require a positive stop price.");
        }

        // Best-effort lot-size validation using the Security Master (requires ISecurityMasterQueryService).
        var lotSizeError = await _tradingParameters.ValidateLotSizeAsync(request, ct).ConfigureAwait(false);
        if (lotSizeError is not null)
        {
            return new OrderValidationResult(false, lotSizeError);
        }

        return new OrderValidationResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> CancelAsync(string orderId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        OrderRequest? cancelledRequest = null;
        lock (_lock)
        {
            if (_workingOrders.Remove(orderId, out var working))
            {
                cancelledRequest = working.Request;
            }
        }

        if (cancelledRequest is not null)
        {
            _logger.LogInformation("Paper order cancelled: {OrderId} {Symbol}", LogSanitizer.Sanitize(orderId), LogSanitizer.Sanitize(cancelledRequest.Symbol));
            var update = new OrderStatusUpdate(
                OrderId: orderId,
                ClientOrderId: orderId,
                Symbol: cancelledRequest.Symbol,
                Status: GatewayOrderStatus.Cancelled,
                FilledQuantity: 0,
                AverageFillPrice: null,
                RejectReason: null,
                Timestamp: DateTimeOffset.UtcNow);

            if (!_updates.Writer.TryWrite(update))
            {
                _logger.LogWarning(
                    "Paper cancellation update for {OrderId} could not be queued because the update channel was unavailable.",
                    LogSanitizer.Sanitize(orderId));
            }
        }

        return Task.FromResult(cancelledRequest is not null);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _updates.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc/>
    public void EvaluateSymbol(string symbol)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        bool hasWorkForSymbol;
        lock (_lock)
        {
            hasWorkForSymbol = _workingOrders.Values.Any(order =>
                string.Equals(order.Request.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        }

        if (hasWorkForSymbol)
        {
            _evaluationPump.Poke(symbol);
        }
    }

    private async Task EvaluateRestingOrdersForSymbolAsync(string symbol)
    {
        List<(string OrderId, WorkingPaperOrder Order)> candidates;
        lock (_lock)
        {
            candidates = _workingOrders
                .Where(pair => string.Equals(pair.Value.Request.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
        }

        foreach (var (orderId, order) in candidates)
        {
            await EvaluateOrderAsync(orderId, order, initialSubmission: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task SimulateFillAsync(OrderRequest request, CancellationToken ct)
    {
        // Yield to allow the caller to receive the acknowledgement before the fill.
        await Task.Yield();
        var orderId = request.ClientOrderId ?? throw new InvalidOperationException("Paper orders must have a client order id before fill simulation.");

        WorkingPaperOrder? order;
        lock (_lock)
        {
            _workingOrders.TryGetValue(orderId, out order);
        }

        if (order is null)
        {
            // The order left the working set before the initial evaluation ran — it was
            // cancelled (CancelAsync removes it and emits a terminal Cancelled update).
            _logger.LogDebug(
                "Paper evaluation skipped for {OrderId}: order is no longer working (cancelled or already filled).",
                LogSanitizer.Sanitize(orderId));
            return;
        }

        await EvaluateOrderAsync(orderId, order, initialSubmission: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates one working order against the current observation under
    /// <see cref="PaperOrderMatchingPolicy"/>. Fills and terminal decisions remove the
    /// order from the working set atomically, so concurrent evaluations cannot double-fill.
    /// </summary>
    private async Task EvaluateOrderAsync(
        string orderId, WorkingPaperOrder order, bool initialSubmission, CancellationToken ct)
    {
        var request = order.Request;
        var observation = PaperMarketObservation.Capture(_liveFeed, request.Symbol);

        bool stopTriggered;
        lock (_lock)
        {
            stopTriggered = order.StopTriggered;
        }

        var result = PaperOrderMatchingPolicy.Evaluate(
            request.Side,
            (OrderType)request.Type,
            request.LimitPrice,
            request.StopPrice,
            stopTriggered,
            observation);

        switch (result.Outcome)
        {
            case PaperMatchOutcome.Filled:
                await EmitFillIfStillWorkingAsync(orderId, request, result.FillPrice!.Value, observation, ct)
                    .ConfigureAwait(false);
                return;

            case PaperMatchOutcome.NoMarketData when (OrderType)request.Type is OrderType.Market:
                if (_allowScaffoldMarketFills)
                {
                    WarnScaffoldPriceUsed();
                    await EmitFillIfStillWorkingAsync(
                        orderId, request, _scaffoldMarketFillPrice, observation, ct).ConfigureAwait(false);
                    return;
                }

                EmitTerminalIfStillWorking(
                    orderId,
                    request,
                    GatewayOrderStatus.Rejected,
                    PaperTradingGatewayScaffoldPricing.BuildNoReferencePriceRejectReason(request.Symbol));
                return;

            default:
                // Resting (or a patient order with no observation yet): immediate-or-cancel
                // and fill-or-kill orders cannot rest, so they cancel on the initial pass.
                if (initialSubmission
                    && request.TimeInForce is TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill)
                {
                    EmitTerminalIfStillWorking(
                        orderId,
                        request,
                        GatewayOrderStatus.Cancelled,
                        $"{request.TimeInForce} order was not immediately fillable against observed market data.");
                    return;
                }

                if (result.StopTriggered)
                {
                    lock (_lock)
                    {
                        if (_workingOrders.TryGetValue(orderId, out var working))
                        {
                            working.StopTriggered = true;
                        }
                    }
                }

                return;
        }
    }

    private async Task EmitFillIfStillWorkingAsync(
        string orderId,
        OrderRequest request,
        decimal fillPrice,
        PaperMarketObservation observation,
        CancellationToken ct)
    {
        // Best-effort tick-size rounding: snap fill price to the instrument's tick grid.
        fillPrice = await _tradingParameters.SnapToTickSizeAsync(request.Symbol, fillPrice, ct)
            .ConfigureAwait(false);

        lock (_lock)
        {
            if (!_workingOrders.Remove(orderId))
            {
                return;
            }
        }

        var quantity = decimal.Abs(request.Quantity);
        var costs = _costModel.Compute(quantity, fillPrice, observation.MidPrice);

        var fill = new OrderStatusUpdate(
            OrderId: orderId,
            ClientOrderId: orderId,
            Symbol: request.Symbol,
            Status: GatewayOrderStatus.Filled,
            FilledQuantity: quantity,
            AverageFillPrice: fillPrice,
            RejectReason: null,
            Timestamp: DateTimeOffset.UtcNow,
            Commission: costs.Commission,
            Fees: costs.Fees,
            SlippageCost: costs.SlippageCost);

        if (!_updates.Writer.TryWrite(fill))
        {
            _logger.LogWarning(
                "Paper fill update for {OrderId} could not be queued because the update channel was unavailable.",
                LogSanitizer.Sanitize(orderId));
        }

        _logger.LogInformation(
            "Paper fill: {ClientOrderId} {Quantity} {Symbol} @ {FillPrice} (commission {Commission}, fees {Fees}, slippage {Slippage})",
            LogSanitizer.Sanitize(orderId), request.Quantity, LogSanitizer.Sanitize(request.Symbol),
            fillPrice, costs.Commission, costs.Fees, costs.SlippageCost);
    }

    private void EmitTerminalIfStillWorking(
        string orderId, OrderRequest request, GatewayOrderStatus status, string reason)
    {
        lock (_lock)
        {
            if (!_workingOrders.Remove(orderId))
            {
                return;
            }
        }

        _logger.LogWarning(
            "Paper order {Status}: {ClientOrderId} {Symbol} — {Reason}",
            status, LogSanitizer.Sanitize(orderId), LogSanitizer.Sanitize(request.Symbol), LogSanitizer.Sanitize(reason));

        var update = new OrderStatusUpdate(
            OrderId: orderId,
            ClientOrderId: orderId,
            Symbol: request.Symbol,
            Status: status,
            FilledQuantity: 0,
            AverageFillPrice: null,
            RejectReason: reason,
            Timestamp: DateTimeOffset.UtcNow);

        if (!_updates.Writer.TryWrite(update))
        {
            _logger.LogWarning(
                "Paper terminal update for {OrderId} could not be queued because the update channel was unavailable.",
                LogSanitizer.Sanitize(orderId));
        }
    }

    /// <summary>
    /// Emits a one-time loud warning when a fill is priced from the scaffold notional price
    /// instead of a real market price, so paper P&amp;L consumers cannot miss it.
    /// </summary>
    private void WarnScaffoldPriceUsed()
    {
        PaperTradingGatewayScaffoldPricing.WarnIfFirstUse(
            ref _scaffoldPriceWarningIssued,
            _logger,
            _scaffoldMarketFillPrice,
            "Paper gateway");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Task[] fillTasks;
        lock (_lock)
        {
            fillTasks = _fillTasks.Values.ToArray();
        }

        if (fillTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(fillTasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Paper gateway observed one or more fill simulation failures during disposal.");
            }
        }

        // Drain resting-order evaluations before completing the update channel so a fill
        // emitted by a late market-data poke is not lost.
        await _evaluationPump.DisposeAsync().ConfigureAwait(false);

        _updates.Writer.TryComplete();
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void TrackFillSimulationLocked(string orderId, OrderRequest request)
    {
        // Use CancellationToken.None so the fill simulation always runs to completion
        // and emits a terminal update, even if the caller cancels after receiving the ack.
        var fillTask = SimulateFillAsync(request, CancellationToken.None);
        _fillTasks[orderId] = fillTask;
        _ = ObserveFillSimulationAsync(orderId, fillTask);
    }

    private async Task ObserveFillSimulationAsync(string orderId, Task fillTask)
    {
        try
        {
            await fillTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paper fill simulation failed for {OrderId}.", LogSanitizer.Sanitize(orderId));
        }
        finally
        {
            _fillTasks.TryRemove(orderId, out _);
        }
    }
}
