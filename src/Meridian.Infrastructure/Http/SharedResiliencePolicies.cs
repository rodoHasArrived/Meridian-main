using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace Meridian.Infrastructure.Http;

/// <summary>
/// Shared resilience policies for HTTP clients to eliminate duplicate policy definitions.
/// Provides consistent retry and circuit breaker policies across all projects.
/// </summary>
public static class SharedResiliencePolicies
{
    /// <summary>
    /// Default timeout for HTTP requests.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Short timeout for quick operations like health checks.
    /// </summary>
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Long timeout for batch operations like backfill.
    /// </summary>
    public static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Adds standard resilience policies (retry with exponential backoff, circuit breaker)
    /// to an HttpClient builder.
    /// </summary>
    public static IHttpClientBuilder AddSharedResiliencePolicy(this IHttpClientBuilder builder)
    {
        return builder
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());
    }

    /// <summary>
    /// Adds custom resilience policies with configurable parameters.
    /// </summary>
    public static IHttpClientBuilder AddCustomResiliencePolicy(
        this IHttpClientBuilder builder,
        int retryCount = 3,
        int circuitBreakerThreshold = 5,
        TimeSpan? circuitBreakerDuration = null)
    {
        return builder
            .AddPolicyHandler(GetRetryPolicy(retryCount))
            .AddPolicyHandler(GetCircuitBreakerPolicy(circuitBreakerThreshold, circuitBreakerDuration ?? TimeSpan.FromSeconds(30)));
    }

    /// <summary>
    /// Creates a retry policy with exponential backoff for transient HTTP errors.
    /// Handles 429 Too Many Requests, 5xx server errors, and network failures.
    /// </summary>
    /// <param name="retryCount">Number of retry attempts (default: 3).</param>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int retryCount = 3)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, _) =>
                {
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy to prevent cascading failures.
    /// Opens the circuit after consecutive failures and prevents requests for a duration.
    /// </summary>
    /// <param name="failureThreshold">Number of failures before opening circuit (default: 5).</param>
    /// <param name="breakDuration">How long circuit stays open (default: 30s).</param>
    /// <param name="breakerName">Optional name used when reporting state transitions via <paramref name="onStateChanged"/>.</param>
    /// <param name="onStateChanged">
    /// Optional callback invoked on every state transition.
    /// Parameters: (breakerName, newState "Open"|"Closed"|"HalfOpen", lastError or null).
    /// </param>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        int failureThreshold = 5,
        TimeSpan? breakDuration = null,
        string? breakerName = null,
        Action<string, string, string?>? onStateChanged = null)
    {
        var duration = breakDuration ?? TimeSpan.FromSeconds(30);
        var name = breakerName ?? "HttpCircuitBreaker";

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: failureThreshold,
                durationOfBreak: duration,
                onBreak: (outcome, _) =>
                    onStateChanged?.Invoke(name, "Open",
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()),
                onReset: () => onStateChanged?.Invoke(name, "Closed", null),
                onHalfOpen: () => onStateChanged?.Invoke(name, "HalfOpen", null));
    }

    /// <summary>
    /// Adds standard resilience policies (retry with exponential backoff, circuit breaker)
    /// to an HttpClient builder, reporting circuit breaker state changes via <paramref name="onStateChanged"/>.
    /// </summary>
    /// <param name="builder">The HttpClient builder to configure.</param>
    /// <param name="clientName">Name used to identify this circuit breaker in state-change reports.</param>
    /// <param name="onStateChanged">
    /// Callback invoked on every circuit breaker state transition.
    /// Parameters: (breakerName, newState "Open"|"Closed"|"HalfOpen", lastError or null).
    /// </param>
    public static IHttpClientBuilder AddSharedResiliencePolicyTracked(
        this IHttpClientBuilder builder,
        string clientName,
        Action<string, string, string?> onStateChanged)
    {
        return builder
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy(
                breakerName: clientName,
                onStateChanged: onStateChanged));
    }

    /// <summary>
    /// Creates a policy for rate-limited APIs that respects Retry-After headers.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRateLimitPolicy()
    {
        return Policy<HttpResponseMessage>
            .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (_, result, _) =>
                {
                    var retryAfter = result.Result?.Headers.RetryAfter;
                    if (retryAfter?.Delta.HasValue == true)
                        return retryAfter.Delta.Value;
                    if (retryAfter?.Date.HasValue == true)
                        return retryAfter.Date.Value - DateTimeOffset.UtcNow;
                    return TimeSpan.FromSeconds(60);
                },
                onRetryAsync: (_, _, _, _) => Task.CompletedTask);
    }
}
