using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Translation;

public interface ICppTraderExecutionTranslator
{
    OrderAcknowledgement ToAcknowledgement(SubmitOrderResponse response);

    OrderStatusUpdate ToAcceptedStatus(AcceptedEvent acceptedEvent);

    OrderStatusUpdate ToRejectedStatus(RejectedEvent rejectedEvent);

    OrderStatusUpdate ToCancelledStatus(CancelledEvent cancelledEvent);

    OrderStatusUpdate ToExecutionStatus(ExecutionEvent executionEvent);
}
