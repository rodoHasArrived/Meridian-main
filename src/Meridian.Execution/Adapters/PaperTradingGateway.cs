using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using Meridian.Core.Pipeline;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Simulated order gateway that routes no real orders to any exchange.
/// Fills are priced from the limit/stop price or the live feed's last observed price;
/// market orders with no available reference price are rejected unless scaffold notional
/// pricing is explicitly enabled via
/// <see cref="PaperTradingGatewayOptions.AllowScaffoldMarketFills"/>.
/// Implements ADR-015.
/// </summary>
[ImplementsAdr("ADR-015", "Simulated IOrderGateway over live Meridian feed — no real orders")]
public sealed class PaperTradingGateway : IOrderGateway
{
    // Notional fallback fill price used for market orders when no live feed price is
    // available, configurable via PaperTradingGatewayOptions. When a live feed adapter
    // is supplied, market fills are priced from the last trade (or quote midpoint).
    // Scaffold pricing is opt-in: without it, priceless market orders are rejected.
    private readonly decimal _scaffoldMarketFillPrice;
    private readonly bool _allowScaffoldMarketFills;
    private readonly Interfaces.ILiveFeedAdapter? _liveFeed;
    private int _scaffoldPriceWarningIssued;

    private readonly ILogger<PaperTradingGateway> _logger;
    private readonly PaperTradingGatewayTradingParameters _tradingParameters;
    private readonly System.Threading.Channels.Channel<OrderStatusUpdate> _updates;
    private readonly Dictionary<string, OrderRequest> _workingOrders = new();
    private readonly ConcurrentDictionary<string, Task> _fillTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private int _disposed;

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
            ["priceSource"] = "scaffold",
            ["supportsNativeTrailingStops"] = "false"
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
    /// Optional live feed adapter. When provided, market orders fill at the last observed
    /// trade price (or quote midpoint) instead of the scaffold notional price.
    /// </param>
    public PaperTradingGateway(
        ILogger<PaperTradingGateway> logger,
        ISecurityMasterQueryService? securityMaster = null,
        PaperTradingGatewayOptions? options = null,
        Interfaces.ILiveFeedAdapter? liveFeed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tradingParameters = new PaperTradingGatewayTradingParameters(
            _logger,
            securityMaster,
            LogLevel.Debug);
        _scaffoldMarketFillPrice = PaperTradingGatewayScaffoldPricing.ResolveScaffoldMarketFillPrice(options);
        _allowScaffoldMarketFills = PaperTradingGatewayScaffoldPricing.ResolveAllowScaffoldMarketFills(options);
        _liveFeed = liveFeed;
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
            if (!_workingOrders.TryAdd(orderId, trackedRequest))
            {
                throw new UnsupportedOrderRequestException(
                    $"An order with client order id '{orderId}' is already working in the paper gateway.");
            }
            TrackFillSimulationLocked(orderId, trackedRequest);
        }

        _logger.LogInformation(
            "Paper order accepted: {ClientOrderId} {Quantity} {Symbol} @ {Type}",
            orderId, request.Quantity, request.Symbol, request.Type);

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
            if (_workingOrders.Remove(orderId, out var req))
            {
                cancelledRequest = req;
            }
        }

        if (cancelledRequest is not null)
        {
            _logger.LogInformation("Paper order cancelled: {OrderId} {Symbol}", orderId, cancelledRequest.Symbol);
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
                    orderId);
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

    private async Task SimulateFillAsync(OrderRequest request, CancellationToken ct)
    {
        // Yield to allow the caller to receive the acknowledgement before the fill.
        await Task.Yield();
        var orderId = request.ClientOrderId ?? throw new InvalidOperationException("Paper orders must have a client order id before fill simulation.");

        bool stillWorking;
        lock (_lock)
        {
            stillWorking = _workingOrders.Remove(orderId);
        }

        if (!stillWorking)
        {
            // The order left the working set before this simulated fill ran — it was cancelled
            // (CancelAsync removes it and emits a terminal Cancelled update) or already filled.
            // Emitting a Filled update here would contradict that terminal state.
            _logger.LogDebug(
                "Paper fill skipped for {OrderId}: order is no longer working (cancelled or already filled).",
                orderId);
            return;
        }

        // For limit orders use the limit price; for market orders source the fill price
        // from the live feed (last trade, then quote midpoint) before falling back to the
        // scaffold notional price.
        var referencePrice = ((OrderType)request.Type) switch
        {
            OrderType.Limit or OrderType.StopLimit => request.LimitPrice,
            OrderType.StopMarket => request.StopPrice,
            _ => null
        };

        referencePrice ??= GetLiveReferencePrice(request.Symbol);

        if (referencePrice is null)
        {
            if (!_allowScaffoldMarketFills)
            {
                var rejectReason = PaperTradingGatewayScaffoldPricing.BuildNoReferencePriceRejectReason(request.Symbol);
                _logger.LogWarning(
                    "Paper order rejected: {ClientOrderId} {Symbol} — {RejectReason}",
                    orderId, request.Symbol, rejectReason);

                var rejection = new OrderStatusUpdate(
                    OrderId: orderId,
                    ClientOrderId: orderId,
                    Symbol: request.Symbol,
                    Status: GatewayOrderStatus.Rejected,
                    FilledQuantity: 0,
                    AverageFillPrice: null,
                    RejectReason: rejectReason,
                    Timestamp: DateTimeOffset.UtcNow);

                if (!_updates.Writer.TryWrite(rejection))
                {
                    _logger.LogWarning(
                        "Paper rejection update for {OrderId} could not be queued because the update channel was unavailable.",
                        orderId);
                }

                return;
            }

            WarnScaffoldPriceUsed();
        }

        var fillPrice = referencePrice ?? _scaffoldMarketFillPrice;

        // Best-effort tick-size rounding: snap fill price to the instrument's tick grid.
        fillPrice = await _tradingParameters.SnapToTickSizeAsync(request.Symbol, fillPrice, ct)
            .ConfigureAwait(false);

        var fill = new OrderStatusUpdate(
            OrderId: orderId,
            ClientOrderId: orderId,
            Symbol: request.Symbol,
            Status: GatewayOrderStatus.Filled,
            FilledQuantity: decimal.Abs(request.Quantity),
            AverageFillPrice: fillPrice,
            RejectReason: null,
            Timestamp: DateTimeOffset.UtcNow);

        if (!_updates.Writer.TryWrite(fill))
        {
            _logger.LogWarning(
                "Paper fill update for {OrderId} could not be queued because the update channel was unavailable.",
                orderId);
        }

        _logger.LogInformation(
            "Paper fill: {ClientOrderId} {Quantity} {Symbol} @ {FillPrice}",
            request.ClientOrderId, request.Quantity, request.Symbol, fillPrice);
    }

    /// <summary>
    /// Returns the best live reference price for a symbol from the optional feed adapter:
    /// last trade price first, then best-bid/offer midpoint; <c>null</c> when no feed is
    /// wired or no tick has been observed yet.
    /// </summary>
    private decimal? GetLiveReferencePrice(string symbol)
    {
        if (_liveFeed is null)
        {
            return null;
        }

        if (_liveFeed.GetLastTrade(symbol) is { Price: > 0m } trade)
        {
            return trade.Price;
        }

        if (_liveFeed.GetLastQuote(symbol) is { } quote)
        {
            var mid = quote.MidPrice ?? (quote.BidPrice + quote.AskPrice) / 2m;
            if (mid > 0m)
            {
                return mid;
            }
        }

        return null;
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

        _updates.Writer.TryComplete();

        await Task.CompletedTask.ConfigureAwait(false);
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
            _logger.LogError(ex, "Paper fill simulation failed for {OrderId}.", orderId);
        }
        finally
        {
            _fillTasks.TryRemove(orderId, out _);
        }
    }
}
