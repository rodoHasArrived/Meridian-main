using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

// ============================================================
// Generic API Response Wrapper
// ============================================================

/// <summary>
/// Generic API response wrapper used by UI clients.
/// Encapsulates success/failure state, data, and error information.
/// </summary>
/// <typeparam name="T">The type of the response data.</typeparam>
public sealed class ApiResponse<T> where T : class
{
    /// <summary>
    /// Whether the API call was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// HTTP status code of the response.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    /// <summary>
    /// The response data, if successful.
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    /// <summary>
    /// Error message, if the call failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether this error was due to a connection failure.
    /// </summary>
    [JsonPropertyName("isConnectionError")]
    public bool IsConnectionError { get; init; }

    /// <summary>
    /// Creates a successful response with data.
    /// </summary>
    public static ApiResponse<T> Ok(T data, int statusCode = 200) => new()
    {
        Success = true,
        StatusCode = statusCode,
        Data = data
    };

    /// <summary>
    /// Creates a failure response with an error message.
    /// </summary>
    public static ApiResponse<T> Fail(string errorMessage, int statusCode = 0, bool isConnectionError = false) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorMessage = errorMessage,
        IsConnectionError = isConnectionError
    };
}

// ============================================================
// Service Health Check Result
// ============================================================

/// <summary>
/// Result of a service health check.
/// Used by UI clients to verify connectivity.
/// </summary>
public sealed class ServiceHealthResult
{
    /// <summary>
    /// Whether the service endpoint is reachable.
    /// </summary>
    [JsonPropertyName("isReachable")]
    public bool IsReachable { get; init; }

    /// <summary>
    /// Whether the service is connected to data providers.
    /// </summary>
    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; init; }

    /// <summary>
    /// Round-trip latency in milliseconds.
    /// </summary>
    [JsonPropertyName("latencyMs")]
    public float LatencyMs { get; init; }

    /// <summary>
    /// HTTP status code received (if any).
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    /// <summary>
    /// Error message if the health check failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    public static ServiceHealthResult Healthy(bool isConnected, float latencyMs) => new()
    {
        IsReachable = true,
        IsConnected = isConnected,
        LatencyMs = latencyMs,
        StatusCode = 200
    };

    /// <summary>
    /// Creates an unhealthy result with an error.
    /// </summary>
    public static ServiceHealthResult Unhealthy(string errorMessage, float latencyMs = 0) => new()
    {
        IsReachable = false,
        IsConnected = false,
        LatencyMs = latencyMs,
        ErrorMessage = errorMessage
    };
}
