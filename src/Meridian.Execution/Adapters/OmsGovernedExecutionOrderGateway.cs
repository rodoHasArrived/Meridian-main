using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using GatewayMode = Meridian.Execution.Models.ExecutionMode;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;
using SdkOrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Execution.Adapters;

/// <summary>
/// Compatibility view that exposes health, capabilities and validation from the same
/// <see cref="IExecutionGateway"/> instance owned by the OMS, and order updates from the OMS's
/// own report publication. Direct mutations are structurally blocked so there is only one
/// authoritative order state and one risk-gated path.
/// </summary>
/// <remarks>
/// Order updates never read <see cref="IExecutionGateway.StreamExecutionReportsAsync"/>: the OMS
/// is that stream's only consumer, and a gateway such as the paper simulator hands each report to
/// exactly one reader. A second reader would take resting-order fills away from the OMS, leaving
/// its order state and portfolio unapplied.
/// </remarks>
public sealed class OmsGovernedExecutionOrderGateway : IOrderGateway
{
    private const string ObserverSubscriberName = "order-gateway-observer";

    private readonly IExecutionGateway _inner;
    private readonly Func<IOrderManager?>? _orderManagerAccessor;

    public OmsGovernedExecutionOrderGateway(IExecutionGateway inner)
        : this(inner, orderManagerAccessor: null)
    {
    }

    /// <param name="inner">The OMS's execution gateway.</param>
    /// <param name="orderManagerAccessor">
    /// Resolves the OMS whose published reports back <see cref="StreamOrderUpdatesAsync"/>. Lazy
    /// so the view can be composed before the OMS. Without it, the update stream is empty.
    /// </param>
    public OmsGovernedExecutionOrderGateway(IExecutionGateway inner, Func<IOrderManager?>? orderManagerAccessor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _orderManagerAccessor = orderManagerAccessor;
    }

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

    /// <summary>
    /// Fill updates from an independent OMS report subscription, as cumulative order snapshots.
    /// This is an observer: if it falls behind its bounded inbox, the excess updates are dropped
    /// for this stream only (the OMS fill path is unaffected) and the affected order's totals are
    /// resynchronised from OMS order state before its next snapshot. Totals cover the fills seen since
    /// the stream subscribed. The OMS publishes fills only, so
    /// acceptance, cancellation and rejection without a fill are not reported here; read those
    /// from <see cref="IOrderManager"/>.
    /// </summary>
    public async IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_orderManagerAccessor?.Invoke() is not OrderManagementSystem oms)
            yield break;

        // A slow observer must not block the OMS, so reports that overflow this subscription
        // are dropped; the order is marked so its totals are resynchronised before the next
        // snapshot instead of being reported short.
        var gapped = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        await using var subscription = oms.SubscribeLosslessExecutionReports(
            ObserverSubscriberName,
            (dropped, _, _) =>
            {
                gapped[dropped.OrderId] = 0;
                return ValueTask.CompletedTask;
            });
        // The OMS publishes each fill as an increment (quantity and execution price of that
        // fill only). Status updates are cumulative snapshots, so totals and the average price
        // are accumulated per order from the fills this stream has seen. OMS order state is read
        // only to resynchronise an order with dropped fills: it can be ahead of the report being
        // emitted, which is acceptable for recovery but not as the normal source.
        var totals = new Dictionary<string, (decimal Quantity, decimal Notional)>(StringComparer.Ordinal);
        await foreach (var report in subscription.Reports.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var status = report.OrderStatus;
            totals.TryGetValue(report.OrderId, out var running);
            if (gapped.TryRemove(report.OrderId, out _) && oms.GetOrder(report.OrderId) is { } state)
            {
                running = (state.FilledQuantity, state.FilledQuantity * (state.AverageFillPrice ?? 0m));
                status = state.Status;
                totals[report.OrderId] = running;
            }
            else if (report.FilledQuantity > 0m)
            {
                running = (
                    running.Quantity + report.FilledQuantity,
                    running.Notional + report.FilledQuantity * (report.FillPrice ?? 0m));
                totals[report.OrderId] = running;
            }

            var filled = running.Quantity;
            decimal? averagePrice = filled > 0m ? running.Notional / filled : report.FillPrice;
            // A terminal order's id may be reused by a later submission (the OMS allows it), so
            // a replacement order must start from zero rather than inherit these totals.
            if (OrderStatusFacts.IsTerminal(status))
                totals.Remove(report.OrderId);

            yield return new OrderStatusUpdate(
                report.OrderId,
                report.ClientOrderId ?? report.OrderId,
                report.Symbol,
                MapStatus(status),
                filled,
                averagePrice,
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
