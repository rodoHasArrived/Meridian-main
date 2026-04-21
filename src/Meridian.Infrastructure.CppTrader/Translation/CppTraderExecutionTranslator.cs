using Meridian.Infrastructure.CppTrader.Protocol;
using Meridian.Infrastructure.CppTrader.Symbols;
using GatewayOrderStatus = Meridian.Execution.Models.OrderStatus;

namespace Meridian.Infrastructure.CppTrader.Translation;

public sealed class CppTraderExecutionTranslator(ICppTraderSymbolMapper symbolMapper) : ICppTraderExecutionTranslator
{
    private readonly ICppTraderSymbolMapper _symbolMapper = symbolMapper;

    public OrderAcknowledgement ToAcknowledgement(SubmitOrderResponse response) =>
        new(
            OrderId: response.OrderId,
            ClientOrderId: response.ClientOrderId,
            Symbol: response.Symbol,
            Status: response.Accepted ? GatewayOrderStatus.Accepted : GatewayOrderStatus.Rejected,
            AcknowledgedAt: response.Timestamp);

    public OrderStatusUpdate ToAcceptedStatus(AcceptedEvent acceptedEvent) =>
        new(
            acceptedEvent.OrderId,
            acceptedEvent.ClientOrderId,
            acceptedEvent.Symbol,
            GatewayOrderStatus.Accepted,
            FilledQuantity: 0,
            AverageFillPrice: null,
            RejectReason: null,
            acceptedEvent.Timestamp);

    public OrderStatusUpdate ToRejectedStatus(RejectedEvent rejectedEvent) =>
        new(
            rejectedEvent.OrderId,
            rejectedEvent.ClientOrderId,
            rejectedEvent.Symbol,
            GatewayOrderStatus.Rejected,
            FilledQuantity: 0,
            AverageFillPrice: null,
            rejectedEvent.Reason,
            rejectedEvent.Timestamp);

    public OrderStatusUpdate ToCancelledStatus(CancelledEvent cancelledEvent) =>
        new(
            cancelledEvent.OrderId,
            cancelledEvent.ClientOrderId,
            cancelledEvent.Symbol,
            GatewayOrderStatus.Cancelled,
            FilledQuantity: 0,
            AverageFillPrice: null,
            RejectReason: null,
            cancelledEvent.Timestamp);

    public OrderStatusUpdate ToExecutionStatus(ExecutionEvent executionEvent)
    {
        var quantity = decimal.Abs(
            _symbolMapper.ConvertQuantityFromNanos(
                executionEvent.Symbol,
                executionEvent.CumulativeFilledQuantityNanos));

        return new OrderStatusUpdate(
            executionEvent.OrderId,
            executionEvent.ClientOrderId,
            executionEvent.Symbol,
            executionEvent.IsTerminal ? GatewayOrderStatus.Filled : GatewayOrderStatus.PartiallyFilled,
            quantity,
            executionEvent.AverageFillPrice,
            RejectReason: null,
            executionEvent.Timestamp);
    }
}
