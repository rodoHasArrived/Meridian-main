using System.Runtime.CompilerServices;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
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
    /// Order snapshots (cumulative filled quantity, average price and status) taken from OMS
    /// order state each time the OMS publishes a fill, via an independent report subscription.
    /// This is an observer: reports that overflow its bounded inbox are dropped for this stream
    /// only, and because every snapshot is read from OMS state, a dropped report never leaves a
    /// later snapshot short. The OMS publishes fills only, so acceptance, cancellation and
    /// rejection without a fill do not trigger a snapshot; read those from
    /// <see cref="IOrderManager"/>.
    /// </summary>
    public async IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_orderManagerAccessor?.Invoke() is not OrderManagementSystem oms)
            yield break;

        // A slow observer must not block the OMS: reports that overflow this subscription are
        // dropped. Each published report is only a trigger; the update is built from the OMS's
        // authoritative order state (cumulative filled quantity, average price and status), so a
        // dropped, buffered or reused-id report can never make a snapshot short or double-count.
        // A snapshot may be ahead of the report that triggered it, which is still a correct state.
        await using var subscription = oms.SubscribeLosslessExecutionReports(
            ObserverSubscriberName,
            static (_, _, _) => ValueTask.CompletedTask);
        await foreach (var report in subscription.Reports.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (oms.GetOrder(report.OrderId) is { } state)
            {
                yield return new OrderStatusUpdate(
                    state.OrderId,
                    report.ClientOrderId ?? state.OrderId,
                    state.Symbol,
                    MapStatus(state.Status),
                    state.FilledQuantity,
                    state.AverageFillPrice,
                    report.RejectReason,
                    report.Timestamp);
            }
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
