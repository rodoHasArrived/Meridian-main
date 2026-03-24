using System.Runtime.CompilerServices;
using Meridian.Application.Exceptions;
using Meridian.Execution.Exceptions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using SdkOrderType = Meridian.Execution.Sdk.OrderType;
using SdkOrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Bridges an <see cref="IBrokerageGateway"/> (from Execution.Sdk) to the
/// <see cref="IOrderGateway"/> contract used by <see cref="Interfaces.IExecutionContext"/>.
/// This allows any brokerage provider to plug into the existing execution framework
/// without modifying the strategy-facing interfaces.
/// </summary>
[ImplementsAdr("ADR-015", "Adapts live brokerage gateways to the IOrderGateway contract")]
public sealed class BrokerageGatewayAdapter : IOrderGateway
{
    private readonly IBrokerageGateway _inner;
    private readonly ILogger<BrokerageGatewayAdapter> _logger;
    private bool _disposed;

    public BrokerageGatewayAdapter(IBrokerageGateway inner, ILogger<BrokerageGatewayAdapter> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    public Task<OrderValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caps = _inner.BrokerageCapabilities;

        if (!caps.SupportedOrderTypes.Contains(request.Type))
            return Task.FromResult(new OrderValidationResult(false, $"Order type '{request.Type}' not supported by {BrokerName}."));

        if (!caps.SupportedTimeInForce.Contains(request.TimeInForce))
            return Task.FromResult(new OrderValidationResult(false, $"Time in force '{request.TimeInForce}' not supported by {BrokerName}."));

        if (request.Quantity <= 0)
            return Task.FromResult(new OrderValidationResult(false, "Order quantity must be positive."));

        if (request.Type is SdkOrderType.Limit or SdkOrderType.StopLimit &&
            (!request.LimitPrice.HasValue || request.LimitPrice <= 0))
            return Task.FromResult(new OrderValidationResult(false, "Limit/stop-limit orders require a positive limit price."));

        if (request.Type is SdkOrderType.StopMarket or SdkOrderType.StopLimit &&
            (!request.StopPrice.HasValue || request.StopPrice <= 0))
            return Task.FromResult(new OrderValidationResult(false, "Stop/stop-limit orders require a positive stop price."));

        return Task.FromResult(new OrderValidationResult(true));
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
            BrokerName, report.OrderId, request.Side, request.Quantity, request.Symbol, report.OrderStatus);

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
            _logger.LogInformation("{Broker} order {OrderId} cancel — {Status}", BrokerName, orderId, report.OrderStatus);
            return report.OrderStatus is SdkOrderStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Broker} failed to cancel order {OrderId}", BrokerName, orderId);
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _inner.StreamExecutionReportsAsync(ct).ConfigureAwait(false))
        {
            var filledQuantity = report.FilledQuantity;
            var truncatedFilledQuantity = decimal.Truncate(filledQuantity);

            if (filledQuantity != truncatedFilledQuantity)
            {
                throw new MeridianException(
                    $"Execution report for order '{report.OrderId}' contains fractional FilledQuantity '{filledQuantity}' which cannot be represented in OrderStatusUpdate.");
            }

            yield return new OrderStatusUpdate(
                OrderId: report.OrderId,
                ClientOrderId: report.ClientOrderId ?? report.OrderId,
                Symbol: report.Symbol,
                Status: MapStatus(report.OrderStatus),
                FilledQuantity: (long)truncatedFilledQuantity,
                AverageFillPrice: report.FillPrice,
                RejectReason: report.RejectReason,
                Timestamp: report.Timestamp);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
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
}
