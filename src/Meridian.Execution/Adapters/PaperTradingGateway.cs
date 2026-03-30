using System.Runtime.CompilerServices;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Simulated order gateway that routes no real orders to any exchange.
/// Fills are generated synthetically at a notional price (or at the limit price for
/// limit orders), making this the safe default for strategy validation before live promotion.
/// Implements ADR-015.
/// </summary>
[ImplementsAdr("ADR-015", "Simulated IOrderGateway over live Meridian feed — no real orders")]
public sealed class PaperTradingGateway : IOrderGateway
{
    // Notional fill price used for market orders in this scaffold.
    // A production implementation would source the last-traded price from ILiveFeedAdapter.
    private const decimal ScaffoldMarketFillPrice = 1m;

    private readonly ILogger<PaperTradingGateway> _logger;
    private readonly ISecurityMasterQueryService? _securityMaster;
    private readonly System.Threading.Channels.Channel<OrderStatusUpdate> _updates;
    private readonly Dictionary<string, OrderRequest> _workingOrders = new();
    private readonly Lock _lock = new();
    private bool _disposed;

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
    /// <param name="logger">Logger.</param>
    /// <param name="securityMaster">
    /// Optional Security Master query service. When provided, order lot-size constraints
    /// are validated against the instrument's <see cref="TradingParametersDto.LotSize"/>.
    /// </param>
    public PaperTradingGateway(ILogger<PaperTradingGateway> logger, ISecurityMasterQueryService? securityMaster = null)
    {
        _logger = logger;
        _securityMaster = securityMaster;
        // Use EventPipelinePolicy for consistent backpressure settings across the platform (ADR-013).
        // CompletionQueue (Wait mode, 500 capacity) ensures no terminal order updates are dropped.
        _updates = EventPipelinePolicy.CompletionQueue.CreateChannel<OrderStatusUpdate>(
            singleReader: false, singleWriter: false);
    }

    /// <inheritdoc/>
    public async Task<OrderAcknowledgement> SubmitAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validation = await ValidateOrderAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new UnsupportedOrderRequestException(validation.Reason ?? "Order request is not supported by the paper gateway.");
        }

        var orderId = request.ClientOrderId ?? $"paper-{Guid.NewGuid():N}";

        lock (_lock)
        {
            _workingOrders[orderId] = request with { ClientOrderId = orderId };
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

        // Use CancellationToken.None so the fill simulation always runs to completion
        // and emits a terminal update, even if the caller cancels after receiving the ack.
        _ = SimulateFillAsync(request with { ClientOrderId = orderId }, CancellationToken.None);

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

        if (((OrderType)request.Type is OrderType.Limit or OrderType.StopLimit) && (!request.LimitPrice.HasValue || request.LimitPrice <= 0))
        {
            return new OrderValidationResult(false, "Limit and stop-limit orders require a positive limit price.");
        }

        if (((OrderType)request.Type is OrderType.StopMarket or OrderType.StopLimit) && (!request.StopPrice.HasValue || request.StopPrice <= 0))
        {
            return new OrderValidationResult(false, "Stop and stop-limit orders require a positive stop price.");
        }

        // Lot-size validation via Security Master (when configured).
        if (_securityMaster is not null)
        {
            var lotSizeError = await ValidateLotSizeAsync(request, ct).ConfigureAwait(false);
            if (lotSizeError is not null)
                return new OrderValidationResult(false, lotSizeError);
        }

        return new OrderValidationResult(true);
    }

    /// <summary>
    /// Looks up the instrument's lot-size constraint from Security Master and returns an error
    /// message if the order quantity is not a valid multiple.  Returns <c>null</c> when the
    /// security is not found or when no lot-size is configured.
    /// </summary>
    private async Task<string?> ValidateLotSizeAsync(OrderRequest request, CancellationToken ct)
    {
        try
        {
            var detail = await _securityMaster!
                .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, request.Symbol, provider: null, ct)
                .ConfigureAwait(false);

            if (detail is null)
                return null;

            var tradingParams = await _securityMaster
                .GetTradingParametersAsync(detail.SecurityId, DateTimeOffset.UtcNow, ct)
                .ConfigureAwait(false);

            if (tradingParams?.LotSize is { } lotSize && lotSize > 0m)
            {
                var absQty = Math.Abs(request.Quantity);
                if (absQty % lotSize != 0m)
                {
                    return $"Order quantity {absQty} is not a valid lot-size multiple for {request.Symbol} (lot size = {lotSize}).";
                }
            }
        }
        catch (Exception ex)
        {
            // Treat Security Master unavailability as a non-blocking advisory — log and continue.
            _logger.LogWarning(ex,
                "Could not validate lot size for {Symbol} from Security Master; proceeding without constraint",
                request.Symbol);
        }

        return null;
    }

    /// <inheritdoc/>
    public Task<bool> CancelAsync(string orderId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

            _updates.Writer.TryWrite(update);
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

        lock (_lock)
        {
            _workingOrders.Remove(orderId);
        }

        // For limit orders use the limit price; for market orders use the scaffold notional price.
        // A real implementation would source the fill price from the live feed via ILiveFeedAdapter.
        var fillPrice = ((OrderType)request.Type) switch
        {
            OrderType.Limit or OrderType.StopLimit => request.LimitPrice ?? ScaffoldMarketFillPrice,
            OrderType.StopMarket => request.StopPrice ?? ScaffoldMarketFillPrice,
            _ => ScaffoldMarketFillPrice
        };

        var fill = new OrderStatusUpdate(
            OrderId: orderId,
            ClientOrderId: orderId,
            Symbol: request.Symbol,
            Status: GatewayOrderStatus.Filled,
            FilledQuantity: checked((long)decimal.Truncate(decimal.Abs(request.Quantity))),
            AverageFillPrice: fillPrice,
            RejectReason: null,
            Timestamp: DateTimeOffset.UtcNow);

        _updates.Writer.TryWrite(fill);

        _logger.LogInformation(
            "Paper fill: {ClientOrderId} {Quantity} {Symbol} @ {FillPrice}",
            request.ClientOrderId, request.Quantity, request.Symbol, fillPrice);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _updates.Writer.TryComplete();

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
