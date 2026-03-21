namespace Meridian.Application.ResultTypes;

/// <summary>
/// Represents a standardized error from any operation in the system.
/// Can be used with Result&lt;T, OperationError&gt; for consistent error handling.
/// </summary>
public sealed record OperationError
{
    public required ErrorCode Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public Exception? Exception { get; init; }
    public IReadOnlyDictionary<string, object?>? Context { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public static OperationError Create(ErrorCode code, string message, string? detail = null)
        => new() { Code = code, Message = message, Detail = detail };

    public static OperationError FromException(Exception ex, ErrorCode? code = null)
        => new()
        {
            Code = code ?? ErrorCode.InternalError,
            Message = ex.Message,
            Detail = ex.StackTrace,
            Exception = ex
        };

    public static OperationError NotFound(string entityType, string entityId)
        => new()
        {
            Code = ErrorCode.NotFound,
            Message = $"{entityType} not found",
            Context = new Dictionary<string, object?> { ["EntityType"] = entityType, ["EntityId"] = entityId }
        };

    public static OperationError Validation(string message, IEnumerable<(string Field, string Error)>? fieldErrors = null)
        => new()
        {
            Code = ErrorCode.ValidationFailed,
            Message = message,
            Context = fieldErrors?.ToDictionary(
                x => x.Field,
                x => (object?)x.Error)
        };

    public static OperationError RateLimit(string provider, TimeSpan? retryAfter = null)
        => new()
        {
            Code = ErrorCode.RateLimitExceeded,
            Message = $"Rate limit exceeded for provider {provider}",
            Context = new Dictionary<string, object?>
            {
                ["Provider"] = provider,
                ["RetryAfter"] = retryAfter?.TotalSeconds
            }
        };

    public static OperationError Timeout(string operation, TimeSpan timeout)
        => new()
        {
            Code = ErrorCode.Timeout,
            Message = $"Operation {operation} timed out after {timeout.TotalSeconds}s",
            Context = new Dictionary<string, object?>
            {
                ["Operation"] = operation,
                ["TimeoutSeconds"] = timeout.TotalSeconds
            }
        };

    public static OperationError Connection(string provider, string? reason = null)
        => new()
        {
            Code = ErrorCode.ConnectionFailed,
            Message = $"Failed to connect to {provider}",
            Detail = reason,
            Context = new Dictionary<string, object?> { ["Provider"] = provider }
        };

    public OperationError WithContext(string key, object? value)
    {
        var newContext = Context is null
            ? new Dictionary<string, object?> { [key] = value }
            : new Dictionary<string, object?>(Context) { [key] = value };
        return this with { Context = newContext };
    }

    public override string ToString()
        => Detail is not null
            ? $"[{Code}] {Message}: {Detail}"
            : $"[{Code}] {Message}";
}
