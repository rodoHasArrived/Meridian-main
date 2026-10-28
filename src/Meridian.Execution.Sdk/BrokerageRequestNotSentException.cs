namespace Meridian.Execution.Sdk;

/// <summary>
/// Thrown by a brokerage gateway when an operation failed before any request was sent to the
/// venue (for example, the order identity could not be resolved or the request failed local
/// validation). Callers may treat the operation as never dispatched: no venue state changed.
/// Any other exception from a gateway call remains ambiguous about dispatch.
/// </summary>
public sealed class BrokerageRequestNotSentException : InvalidOperationException
{
    public BrokerageRequestNotSentException(string message)
        : base(message)
    {
    }

    public BrokerageRequestNotSentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
