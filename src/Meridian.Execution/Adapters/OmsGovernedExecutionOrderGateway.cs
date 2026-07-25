using System.Runtime.CompilerServices;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using GatewayMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using SdkOrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Compatibility view that exposes health, capabilities, validation, and execution reports from
/// the same <see cref="IExecutionGateway"/> instance owned by the OMS. Direct mutations are
/// structurally blocked so there is only one authoritative order state and one risk-gated path.
/// </summary>
public sealed class OmsGovernedExecutionOrderGateway : IOrderGateway
{
    private readonly IExecutionGateway _inner;

    public OmsGovernedExecutionOrderGateway(IExecutionGateway inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public string BrokerName => _inner.GatewayId;

    public GatewayMode Mode => _inner is IExecutionGatewayModeProvider modeProvider
        ? modeProvider.ExecutionMode switch
        {
            Meridian.Execution.Sdk.ExecutionMode.Live => GatewayMode.Live,
            Meridian.Execution.Sdk.ExecutionMode.Simulation => GatewayMode.Simulation,
            _ => GatewayMode.Paper
        }
        : GatewayMode.Paper;

    public OrderGatewayCapabilities Capabilities { get; } = new(
        Enum.GetValues<OrderType>().ToHashSet(),
        Enum.GetValues<TimeInForce>().ToHashSet(),
        Enum.GetValues<GatewayMode>().ToHashSet(),
        SupportsOrderModification: true,
        SupportsPartialFills: true);

    public Task<OrderValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (request.Quantity <= 0m)
        {
            return Task.FromResult(new OrderValidationResult(false, "Order quantity must be positive."));
        }

        if (request.Type is OrderType.Limit or OrderType.StopLimit && request.LimitPrice is not > 0m)
        {
            return Task.FromResult(new OrderValidationResult(false, "Limit-style orders require a positive limit price."));
        }

        return Task.FromResult(new OrderValidationResult(true));
    }

    public Task<OrderAcknowledgement> SubmitAsync(OrderRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Order submission must go through IOrderManager so the OMS risk and control gates cannot be bypassed.");

    public Task<bool> CancelAsync(string orderId, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "Order cancellation must go through IOrderManager so authoritative OMS state cannot be bypassed.");

    public async IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _inner.StreamExecutionReportsAsync(ct).ConfigureAwait(false))
        {
            yield return new OrderStatusUpdate(
                report.OrderId,
                report.ClientOrderId ?? report.OrderId,
                report.Symbol,
                MapStatus(report.OrderStatus),
                report.FilledQuantity,
                report.FillPrice,
                report.RejectReason,
                report.Timestamp);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static GatewayOrderStatus MapStatus(SdkOrderStatus status) => status switch
    {
        SdkOrderStatus.PartiallyFilled => GatewayOrderStatus.PartiallyFilled,
        SdkOrderStatus.Filled => GatewayOrderStatus.Filled,
        SdkOrderStatus.Cancelled or SdkOrderStatus.Expired => GatewayOrderStatus.Cancelled,
        SdkOrderStatus.Rejected => GatewayOrderStatus.Rejected,
        SdkOrderStatus.Accepted => GatewayOrderStatus.Accepted,
        _ => GatewayOrderStatus.Working
    };
}
