namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when an async operation times out
/// </summary>
public sealed class OperationTimeoutException : MeridianException
{
    public string? OperationName { get; }
    public TimeSpan? Timeout { get; }
    public string? Provider { get; }

    public OperationTimeoutException(string message) : base(message)
    {
    }

    public OperationTimeoutException(
        string message,
        string? operationName = null,
        TimeSpan? timeout = null,
        string? provider = null)
        : base(message)
    {
        OperationName = operationName;
        Timeout = timeout;
        Provider = provider;
    }

    public OperationTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
