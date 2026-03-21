using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Standardized error response for all HTTP API endpoints.
/// Follows RFC 7807 (Problem Details for HTTP APIs) conventions.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// A URI reference that identifies the problem type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "about:blank";

    /// <summary>
    /// A short, human-readable summary of the problem type.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// The HTTP status code.
    /// </summary>
    [JsonPropertyName("status")]
    public required int Status { get; init; }

    /// <summary>
    /// A human-readable explanation specific to this occurrence of the problem.
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>
    /// A URI reference that identifies the specific occurrence of the problem.
    /// </summary>
    [JsonPropertyName("instance")]
    public string? Instance { get; init; }

    /// <summary>
    /// Application-specific error code for programmatic handling.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; init; }

    /// <summary>
    /// Timestamp when the error occurred.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Validation errors if applicable.
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FieldError>? Errors { get; init; }

    /// <summary>
    /// Additional context information.
    /// </summary>
    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }

    /// <summary>
    /// Creates a validation error response.
    /// </summary>
    public static ErrorResponse Validation(
        string detail,
        IEnumerable<FieldError>? fieldErrors = null,
        string? correlationId = null)
    {
        return new ErrorResponse
        {
            Type = "https://meridian.io/errors/validation",
            Title = "Validation Failed",
            Status = 400,
            Detail = detail,
            ErrorCode = 2000,
            CorrelationId = correlationId,
            Errors = fieldErrors?.ToList()
        };
    }

    /// <summary>
    /// Creates a not found error response.
    /// </summary>
    public static ErrorResponse NotFound(
        string entityType,
        string entityId,
        string? correlationId = null)
    {
        return new ErrorResponse
        {
            Type = "https://meridian.io/errors/not-found",
            Title = $"{entityType} Not Found",
            Status = 404,
            Detail = $"The requested {entityType} with ID '{entityId}' was not found.",
            ErrorCode = 1004,
            CorrelationId = correlationId,
            Extensions = new Dictionary<string, object?>
            {
                ["entityType"] = entityType,
                ["entityId"] = entityId
            }
        };
    }

    /// <summary>
    /// Creates a rate limit exceeded error response.
    /// </summary>
    public static ErrorResponse RateLimitExceeded(
        string provider,
        TimeSpan? retryAfter = null,
        string? correlationId = null)
    {
        return new ErrorResponse
        {
            Type = "https://meridian.io/errors/rate-limit",
            Title = "Rate Limit Exceeded",
            Status = 429,
            Detail = $"Rate limit exceeded for provider '{provider}'. Please retry after {retryAfter?.TotalSeconds ?? 60} seconds.",
            ErrorCode = 5002,
            CorrelationId = correlationId,
            Extensions = new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["retryAfterSeconds"] = retryAfter?.TotalSeconds ?? 60
            }
        };
    }

    /// <summary>
    /// Creates a service unavailable error response.
    /// </summary>
    public static ErrorResponse ServiceUnavailable(
        string service,
        string? reason = null,
        string? correlationId = null)
    {
        return new ErrorResponse
        {
            Type = "https://meridian.io/errors/service-unavailable",
            Title = "Service Unavailable",
            Status = 503,
            Detail = reason ?? $"The {service} service is currently unavailable. Please try again later.",
            ErrorCode = 5001,
            CorrelationId = correlationId,
            Extensions = new Dictionary<string, object?>
            {
                ["service"] = service
            }
        };
    }

    /// <summary>
    /// Creates an internal server error response.
    /// </summary>
    public static ErrorResponse InternalError(
        string? detail = null,
        string? correlationId = null)
    {
        return new ErrorResponse
        {
            Type = "https://meridian.io/errors/internal",
            Title = "Internal Server Error",
            Status = 500,
            Detail = detail ?? "An unexpected error occurred. Please try again later.",
            ErrorCode = 1001,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Creates a timeout error response.
    /// </summary>
    public static ErrorResponse Timeout(
        string operation,
        TimeSpan timeout,
        string? correlationId = null)
    {
        return new ErrorResponse
        {
            Type = "https://meridian.io/errors/timeout",
            Title = "Operation Timed Out",
            Status = 408,
            Detail = $"The operation '{operation}' timed out after {timeout.TotalSeconds} seconds.",
            ErrorCode = 1003,
            CorrelationId = correlationId,
            Extensions = new Dictionary<string, object?>
            {
                ["operation"] = operation,
                ["timeoutSeconds"] = timeout.TotalSeconds
            }
        };
    }

    /// <summary>
    /// Creates an error response from an error code.
    /// </summary>
    public static ErrorResponse FromErrorCode(
        int errorCode,
        string message,
        string? detail = null,
        string? correlationId = null)
    {
        var httpStatus = GetHttpStatusFromErrorCode(errorCode);
        var category = GetCategoryFromErrorCode(errorCode);

        return new ErrorResponse
        {
            Type = $"https://meridian.io/errors/{category.ToLowerInvariant()}",
            Title = message,
            Status = httpStatus,
            Detail = detail,
            ErrorCode = errorCode,
            CorrelationId = correlationId
        };
    }

    private static int GetHttpStatusFromErrorCode(int code)
    {
        return code switch
        {
            1004 => 404, // NotFound
            >= 2000 and < 3000 => 400, // Validation
            >= 3000 and < 4000 => 400, // Configuration
            >= 4000 and < 5000 => 502, // Connection
            5002 => 429, // RateLimit
            5001 or 5007 => 503, // Unavailable/CircuitBreaker
            >= 5000 and < 6000 => 502, // Provider
            >= 6000 and < 7000 => 422, // DataIntegrity
            >= 7000 and < 8000 => 500, // Storage
            >= 8000 and < 9000 => 500, // Messaging
            _ => 500
        };
    }

    private static string GetCategoryFromErrorCode(int code)
    {
        return code switch
        {
            >= 1000 and < 2000 => "General",
            >= 2000 and < 3000 => "Validation",
            >= 3000 and < 4000 => "Configuration",
            >= 4000 and < 5000 => "Connection",
            >= 5000 and < 6000 => "Provider",
            >= 6000 and < 7000 => "DataIntegrity",
            >= 7000 and < 8000 => "Storage",
            >= 8000 and < 9000 => "Messaging",
            _ => "Unknown"
        };
    }
}

/// <summary>
/// Represents a field-level validation error.
/// </summary>
public sealed record FieldError(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("code")] string? Code = null,
    [property: JsonPropertyName("attemptedValue")] object? AttemptedValue = null);
