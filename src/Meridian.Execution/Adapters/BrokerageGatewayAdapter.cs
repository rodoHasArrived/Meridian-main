using System.Runtime.CompilerServices;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using SdkOrderStatus = Meridian.Execution.Sdk.OrderStatus;
using SdkOrderType = Meridian.Execution.Sdk.OrderType;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Bridges an <see cref="IBrokerageGateway"/> (from Execution.Sdk) to the
/// <see cref="IOrderGateway"/> contract, mapping broker capabilities, order state, and execution
/// reports between the two shapes.
/// </summary>
/// <remarks>
/// This adapter is an <b>internal implementation detail</b> of the execution layer. It is never
/// registered as a directly resolvable order-submission service: live order submission and
/// cancellation are owned exclusively by <see cref="OrderManagementSystem"/> (the OMS), which runs
/// the pre-trade gate stack (placement gate, live-order readiness, operator controls, security
/// master, and risk validation) before any order reaches a broker. The live <see cref="IOrderGateway"/>
/// exposed through dependency injection is a read-only view over this adapter
/// (<see cref="OmsGovernedBrokerageOrderGateway"/>) whose submission and cancellation members are
/// blocked, so no caller can bypass the OMS gates by resolving <see cref="IOrderGateway"/> directly.
/// </remarks>
[ImplementsAdr("ADR-015", "Adapts live brokerage gateways to the IOrderGateway contract")]
internal sealed class BrokerageGatewayAdapter : IOrderGateway
{
    private readonly IBrokerageGateway _inner;
    private readonly ILogger<BrokerageGatewayAdapter> _logger;
    private readonly BrokerageConfiguration _configuration;
    private bool _disposed;

    public BrokerageGatewayAdapter(
        IBrokerageGateway inner,
        ILogger<BrokerageGatewayAdapter> logger,
        BrokerageConfiguration? configuration = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new BrokerageConfiguration();
    }

    /// <inheritdoc />
    public string BrokerName => _inner.BrokerDisplayName;

    /// <inheritdoc />
    public GatewayExecutionMode Mode => GatewayExecutionMode.Live;

    /// <inheritdoc />
    public OrderGatewayCapabilities Capabilities
    {
        get
        {
            var bc = _inner.BrokerageCapabilities;
            return new OrderGatewayCapabilities(
                SupportedOrderTypes: bc.SupportedOrderTypes,
                SupportedTimeInForce: bc.SupportedTimeInForce,
                SupportedExecutionModes: new HashSet<GatewayExecutionMode> { GatewayExecutionMode.Live },
                SupportsOrderModification: bc.SupportsOrderModification,
                SupportsPartialFills: bc.SupportsPartialFills,
                ProviderExtensions: new Dictionary<string, string>(bc.Extensions, StringComparer.OrdinalIgnoreCase)
                {
                    ["supportsShortSelling"] = bc.SupportsShortSelling.ToString(),
                    ["supportsFractionalShares"] = bc.SupportsFractionalShares.ToString(),
                    ["supportsExtendedHours"] = bc.SupportsExtendedHours.ToString(),
                });
        }
    }

    /// <inheritdoc />
    public async Task<OrderValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caps = _inner.BrokerageCapabilities;

        if (!caps.SupportedOrderTypes.Contains(request.Type))
            return new OrderValidationResult(false, $"Order type '{request.Type}' not supported by {BrokerName}.");

        if (!caps.SupportedTimeInForce.Contains(request.TimeInForce))
            return new OrderValidationResult(false, $"Time in force '{request.TimeInForce}' not supported by {BrokerName}.");

        if (request.Quantity <= 0)
            return new OrderValidationResult(false, "Order quantity must be positive.");

        if (request.Type is SdkOrderType.Limit or SdkOrderType.StopLimit or SdkOrderType.LimitOnOpen or SdkOrderType.LimitOnClose &&
            (!request.LimitPrice.HasValue || request.LimitPrice <= 0))
            return new OrderValidationResult(false, "Limit-style orders require a positive limit price.");

        if (request.Type is SdkOrderType.StopMarket or SdkOrderType.StopLimit &&
            (!request.StopPrice.HasValue || request.StopPrice <= 0))
            return new OrderValidationResult(false, "Stop/stop-limit orders require a positive stop price.");

        if (request.Type is SdkOrderType.TrailingStop &&
            (!HasExactlyOnePositiveTrail(request.TrailPrice, request.TrailPercent)))
            return new OrderValidationResult(false, "Trailing stop orders require exactly one positive trail price or trail percent.");

        if (_configuration.MaxOrderNotional > 0m)
        {
            var effectivePrice = ResolveEffectivePrice(request);
            if (!effectivePrice.HasValue)
                return new OrderValidationResult(false, "Unable to evaluate order notional for this order type without a price input.");

            if ((request.Quantity * effectivePrice.Value) > _configuration.MaxOrderNotional)
                return new OrderValidationResult(false, $"Order notional exceeds configured maximum of {_configuration.MaxOrderNotional}.");
        }

        if (_configuration.MaxOpenOrders > 0)
        {
            var openOrders = await _inner.GetOpenOrdersAsync(ct).ConfigureAwait(false);
            if (openOrders.Count >= _configuration.MaxOpenOrders)
                return new OrderValidationResult(false, $"Open order count exceeds configured maximum of {_configuration.MaxOpenOrders}.");
        }

        if (_configuration.MaxPositionSize > 0m)
        {
            var positions = await _inner.GetPositionsAsync(ct).ConfigureAwait(false);
            var aggregatePositionSize = positions.Sum(p => Math.Abs(p.Quantity));
            var projectedPositionSize = aggregatePositionSize + Math.Abs(request.Quantity);
            if (projectedPositionSize > _configuration.MaxPositionSize)
                return new OrderValidationResult(false, $"Projected position size exceeds configured maximum of {_configuration.MaxPositionSize}.");
        }

        return new OrderValidationResult(true);
    }

    /// <inheritdoc />
    public async Task<OrderAcknowledgement> SubmitAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var validation = await ValidateOrderAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new UnsupportedOrderRequestException(validation.Reason ?? "Order validation failed.");

        var report = await _inner.SubmitOrderAsync(request, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "{Broker} order submitted: {OrderId} {Side} {Quantity} {Symbol} — {Status}",
            BrokerName, report.OrderId, request.Side, request.Quantity, ExecutionLogText.ForLog(request.Symbol), report.OrderStatus);

        return new OrderAcknowledgement(
            OrderId: report.OrderId,
            ClientOrderId: request.ClientOrderId ?? report.OrderId,
            Symbol: request.Symbol,
            Status: MapStatus(report.OrderStatus),
            AcknowledgedAt: report.Timestamp);
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(string orderId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var report = await _inner.CancelOrderAsync(orderId, ct).ConfigureAwait(false);
            _logger.LogInformation("{Broker} order {OrderId} cancel — {Status}", BrokerName, ExecutionLogText.ForLog(orderId), report.OrderStatus);
            return report.OrderStatus is SdkOrderStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Broker} failed to cancel order {OrderId}", BrokerName, ExecutionLogText.ForLog(orderId));
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _inner.StreamExecutionReportsAsync(ct).ConfigureAwait(false))
        {
            yield return new OrderStatusUpdate(
                OrderId: report.OrderId,
                ClientOrderId: report.ClientOrderId ?? report.OrderId,
                Symbol: report.Symbol,
                Status: MapStatus(report.OrderStatus),
                FilledQuantity: report.FilledQuantity,
                AverageFillPrice: report.FillPrice,
                RejectReason: report.RejectReason,
                Timestamp: report.Timestamp);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private static GatewayOrderStatus MapStatus(SdkOrderStatus sdkStatus) => sdkStatus switch
    {
        SdkOrderStatus.PendingNew => GatewayOrderStatus.Accepted,
        SdkOrderStatus.Accepted => GatewayOrderStatus.Accepted,
        SdkOrderStatus.PartiallyFilled => GatewayOrderStatus.PartiallyFilled,
        SdkOrderStatus.Filled => GatewayOrderStatus.Filled,
        SdkOrderStatus.PendingCancel => GatewayOrderStatus.Working,
        SdkOrderStatus.Cancelled => GatewayOrderStatus.Cancelled,
        SdkOrderStatus.Rejected => GatewayOrderStatus.Rejected,
        SdkOrderStatus.Expired => GatewayOrderStatus.Cancelled,
        _ => GatewayOrderStatus.Rejected
    };

    private static bool HasExactlyOnePositiveTrail(decimal? trailPrice, decimal? trailPercent)
    {
        var hasTrailPrice = trailPrice.HasValue && trailPrice.Value > 0m;
        var hasTrailPercent = trailPercent.HasValue && trailPercent.Value > 0m;
        return hasTrailPrice ^ hasTrailPercent;
    }

    private static decimal? ResolveEffectivePrice(OrderRequest request) => request.Type switch
    {
        SdkOrderType.Limit or SdkOrderType.StopLimit or SdkOrderType.LimitOnOpen or SdkOrderType.LimitOnClose => request.LimitPrice,
        SdkOrderType.StopMarket => request.StopPrice,
        _ => request.LimitPrice ?? request.StopPrice
    };
}
