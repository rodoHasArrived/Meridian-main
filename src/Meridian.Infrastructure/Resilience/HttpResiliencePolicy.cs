using Meridian.Application.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Serilog;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// HTTP resilience policies using Polly for backfill providers.
/// Provides standardized retry, circuit breaker, and timeout handling
/// to eliminate duplicate resilience logic across providers.
/// </summary>
/// <remarks>
/// This class parallels <see cref="WebSocketResiliencePolicy"/> but is optimized
/// for HTTP request/response patterns rather than long-lived connections.
/// </remarks>
public static class HttpResiliencePolicy
{
    /// <summary>
    /// Creates a resilience pipeline for HTTP GET requests.
    /// Uses exponential backoff with jitter to avoid thundering herd.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <param name="baseDelay">Base delay for exponential backoff (default: 1 second)</param>
    /// <param name="maxDelay">Maximum delay between retries (default: 30 seconds)</param>
    public static ResiliencePipeline<HttpResponseMessage> CreateRetryPipeline(
        int maxRetries = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null)
    {
        var logger = LoggingSetup.ForContext("HttpResilience");
        baseDelay ??= TimeSpan.FromSeconds(1);
        maxDelay ??= TimeSpan.FromSeconds(30);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetries,
                Delay = baseDelay.Value,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxDelay = maxDelay.Value,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
                    .HandleResult(r => IsTransientHttpError(r)),
                OnRetry = args =>
                {
                    var statusCode = args.Outcome.Result?.StatusCode;
                    logger.Warning(
                        "HTTP request failed (attempt {AttemptNumber}/{MaxRetryAttempts}). " +
                        "Status: {StatusCode}. Retrying after {DelayDuration}ms. Error: {Exception}",
                        args.AttemptNumber,
                        maxRetries + 1,
                        (int?)statusCode,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "Transient HTTP error");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a circuit breaker pipeline for HTTP operations.
    /// Prevents cascading failures by opening the circuit after consecutive failures.
    /// </summary>
    /// <param name="failureThreshold">Minimum failures before opening (default: 5)</param>
    /// <param name="breakDuration">Duration circuit stays open (default: 30 seconds)</param>
    public static ResiliencePipeline<HttpResponseMessage> CreateCircuitBreakerPipeline(
        int failureThreshold = 5,
        TimeSpan? breakDuration = null)
    {
        var logger = LoggingSetup.ForContext("HttpCircuitBreaker");
        breakDuration ??= TimeSpan.FromSeconds(30);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                MinimumThroughput = failureThreshold,
                BreakDuration = breakDuration.Value,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => IsTransientHttpError(r)),
                OnOpened = args =>
                {
                    logger.Error(
                        "HTTP circuit breaker OPENED. Service may be unavailable. " +
                        "Circuit will remain open for {BreakDuration}s. Last error: {Exception}",
                        breakDuration.Value.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "HTTP error");
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    logger.Information("HTTP circuit breaker CLOSED. Normal operation resumed.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    logger.Information("HTTP circuit breaker HALF-OPEN. Testing if service has recovered...");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a timeout policy for HTTP operations.
    /// </summary>
    /// <param name="timeout">Request timeout (default: 30 seconds)</param>
    public static ResiliencePipeline<HttpResponseMessage> CreateTimeoutPipeline(TimeSpan? timeout = null)
    {
        var logger = LoggingSetup.ForContext("HttpTimeout");
        timeout ??= TimeSpan.FromSeconds(30);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = timeout.Value,
                OnTimeout = args =>
                {
                    logger.Warning(
                        "HTTP request timed out after {TimeoutDuration}s",
                        timeout.Value.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a comprehensive resilience pipeline combining retry, circuit breaker, and timeout.
    /// This is the recommended pipeline for production HTTP clients.
    /// </summary>
    /// <param name="maxRetries">Maximum retry attempts (default: 3)</param>
    /// <param name="retryBaseDelay">Base delay for retries (default: 1 second)</param>
    /// <param name="circuitBreakerFailureThreshold">Failures before circuit opens (default: 5)</param>
    /// <param name="circuitBreakerDuration">Duration circuit stays open (default: 30 seconds)</param>
    /// <param name="requestTimeout">Individual request timeout (default: 30 seconds)</param>
    public static ResiliencePipeline<HttpResponseMessage> CreateComprehensivePipeline(
        int maxRetries = 3,
        TimeSpan? retryBaseDelay = null,
        int circuitBreakerFailureThreshold = 5,
        TimeSpan? circuitBreakerDuration = null,
        TimeSpan? requestTimeout = null)
    {
        retryBaseDelay ??= TimeSpan.FromSeconds(1);
        circuitBreakerDuration ??= TimeSpan.FromSeconds(30);
        requestTimeout ??= TimeSpan.FromSeconds(30);

        var logger = LoggingSetup.ForContext("HttpResilience");

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            // Outermost: Total operation timeout
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromMinutes(5),
                OnTimeout = args =>
                {
                    logger.Error("HTTP operation exceeded total timeout of 5 minutes");
                    return ValueTask.CompletedTask;
                }
            })
            // Middle: Circuit breaker (prevents cascading failures)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                MinimumThroughput = circuitBreakerFailureThreshold,
                BreakDuration = circuitBreakerDuration.Value,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => IsTransientHttpError(r))
            })
            // Innermost: Retry with exponential backoff
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetries,
                Delay = retryBaseDelay.Value,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxDelay = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
                    .HandleResult(r => IsRetryableHttpError(r))
            })
            .Build();
    }

    /// <summary>
    /// Creates a rate-limit aware pipeline that respects Retry-After headers.
    /// </summary>
    /// <param name="maxRetries">Maximum retry attempts for rate limits (default: 3)</param>
    /// <param name="defaultRetryAfter">Default delay when Retry-After header is missing (default: 60 seconds)</param>
    public static ResiliencePipeline<HttpResponseMessage> CreateRateLimitAwarePipeline(
        int maxRetries = 3,
        TimeSpan? defaultRetryAfter = null)
    {
        var logger = LoggingSetup.ForContext("HttpRateLimitResilience");
        defaultRetryAfter ??= TimeSpan.FromSeconds(60);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetries,
                DelayGenerator = args =>
                {
                    // Extract Retry-After from response if available
                    if (args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = ExtractRetryAfter(args.Outcome.Result) ?? defaultRetryAfter.Value;
                        logger.Debug("Rate limited. Waiting {RetryAfter}s before retry", retryAfter.TotalSeconds);
                        return ValueTask.FromResult<TimeSpan?>(retryAfter);
                    }

                    // Use exponential backoff for other errors
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber));
                    return ValueTask.FromResult<TimeSpan?>(delay);
                },
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                OnRetry = args =>
                {
                    logger.Warning(
                        "Rate limit hit (attempt {AttemptNumber}/{MaxRetryAttempts}). " +
                        "Waiting {DelayDuration}s before retry",
                        args.AttemptNumber,
                        maxRetries + 1,
                        args.RetryDelay.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Extracts the Retry-After value from an HTTP response.
    /// Supports both seconds and HTTP-date formats.
    /// </summary>
    public static TimeSpan? ExtractRetryAfter(HttpResponseMessage response)
    {
        // Try the typed header first
        if (response.Headers.RetryAfter?.Delta.HasValue == true)
        {
            return response.Headers.RetryAfter.Delta.Value;
        }

        if (response.Headers.RetryAfter?.Date.HasValue == true)
        {
            var delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay;
        }

        // Fall back to raw header parsing
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var retryAfterValue = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(retryAfterValue))
            {
                // Try parsing as seconds
                if (int.TryParse(retryAfterValue, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                // Try parsing as HTTP date
                if (DateTimeOffset.TryParse(retryAfterValue, out var retryDate))
                {
                    var delay = retryDate - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                        return delay;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Determines if an HTTP response represents a transient error that should trigger circuit breaker.
    /// </summary>
    private static bool IsTransientHttpError(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return statusCode >= 500 || // Server errors
               statusCode == 408 || // Request Timeout
               statusCode == 429;   // Too Many Requests
    }

    /// <summary>
    /// Determines if an HTTP response represents a retryable error.
    /// More conservative than IsTransientHttpError - excludes some cases.
    /// </summary>
    private static bool IsRetryableHttpError(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return statusCode >= 500 || // Server errors
               statusCode == 408 || // Request Timeout
               statusCode == 429;   // Too Many Requests (handled separately with Retry-After)
    }
}

/// <summary>
/// HTTP response handling result for providers.
/// </summary>
public sealed record HttpHandleResult(
    bool IsSuccess,
    bool IsNotFound = false,
    bool IsRateLimited = false,
    bool IsAuthError = false,
    TimeSpan? RetryAfter = null,
    string? ErrorMessage = null
)
{
    public static HttpHandleResult Success { get; } = new(true);
    public static HttpHandleResult NotFound { get; } = new(false, IsNotFound: true);
    public static HttpHandleResult AuthFailure { get; } = new(false, IsAuthError: true);

    public static HttpHandleResult RateLimited(TimeSpan? retryAfter = null) =>
        new(false, IsRateLimited: true, RetryAfter: retryAfter);

    public static HttpHandleResult Error(string message) =>
        new(false, ErrorMessage: message);
}

/// <summary>
/// Callback arguments for rate limit events.
/// </summary>
public sealed record RateLimitEventArgs(
    string ProviderName,
    TimeSpan? RetryAfter,
    int? RequestsMade = null,
    int? MaxRequests = null
);
