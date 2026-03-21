namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when a provider's rate limit is exceeded
/// </summary>
public sealed class RateLimitException : DataProviderException
{
    public TimeSpan? RetryAfter { get; }
    public int? RemainingRequests { get; }
    public int? RequestLimit { get; }

    public RateLimitException(string message) : base(message)
    {
    }

    public RateLimitException(
        string message,
        string? provider = null,
        string? symbol = null,
        TimeSpan? retryAfter = null,
        int? remainingRequests = null,
        int? requestLimit = null)
        : base(message, provider, symbol)
    {
        RetryAfter = retryAfter;
        RemainingRequests = remainingRequests;
        RequestLimit = requestLimit;
    }

    public RateLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
