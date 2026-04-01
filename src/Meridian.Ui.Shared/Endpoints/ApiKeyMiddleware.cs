using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Middleware that enforces API key authentication on /api/* endpoints.
/// The API key is read from the MDC_API_KEY environment variable and supports
/// key rotation (re-reads the variable on each request).
/// When no key is configured, requests pass through so other auth layers can decide access.
/// Health check endpoints (/healthz, /readyz, /livez) are always exempt.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string ApiKeyEnvVar = "MDC_API_KEY";

    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/healthz",
        "/readyz",
        "/livez"
    };

    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Re-read on each request to support key rotation without restart
        var expectedApiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);

        // If no API key is configured, defer to other authentication layers.
        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        // Health check endpoints are always exempt from authentication
        if (ExemptPaths.Contains(path.TrimEnd('/')))
        {
            await _next(context);
            return;
        }

        // Only enforce on API paths
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Check for API key in the header only to avoid leakage via URLs, logs, and browser history.
        var providedKey = context.Request.Headers[ApiKeyHeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedKey) ||
            !CryptographicEquals(providedKey, expectedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"Unauthorized. Provide a valid API key via the X-Api-Key header."}""");
            return;
        }

        // Store the validated API key identifier for downstream rate limiting
        context.Items["ApiKey"] = providedKey;

        await _next(context);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks on API key validation.
    /// Uses CryptographicOperations.FixedTimeEquals which handles differing lengths
    /// without leaking length information through timing.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }
}

/// <summary>
/// Per-API-key rate limiting middleware.
/// Tracks request counts per API key using a sliding window and returns 429 when exceeded.
/// </summary>
public sealed class ApiKeyRateLimitMiddleware
{
    private const int MaxRequestsPerMinute = 120;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const int CleanupThreshold = 1000;

    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _clients = new();
    private int _requestsSinceCleanup;

    public ApiKeyRateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only apply to API paths
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow tests and dev environments to opt out of rate limiting via env var.
        // This mirrors the behaviour of the ASP.NET Core mutation rate limiter in UiEndpoints.cs.
        if (string.Equals(
                Environment.GetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Partition by API key if present, otherwise by IP
        var partitionKey = context.Items.TryGetValue("ApiKey", out var apiKey) && apiKey is string key
            ? $"key:{key}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        var entry = _clients.GetOrAdd(partitionKey, _ => new RateLimitEntry());

        var now = DateTime.UtcNow;
        int remaining;
        bool rateLimited = false;
        int retryAfter = 0;

        lock (entry)
        {
            // Reset window if expired
            if (now - entry.WindowStart >= Window)
            {
                entry.WindowStart = now;
                entry.RequestCount = 0;
            }

            entry.RequestCount++;
            remaining = Math.Max(0, MaxRequestsPerMinute - entry.RequestCount);

            if (entry.RequestCount > MaxRequestsPerMinute)
            {
                retryAfter = (int)(Window - (now - entry.WindowStart)).TotalSeconds + 1;
                rateLimited = true;
            }
        }

        if (rateLimited)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Response.Headers["X-RateLimit-Limit"] = MaxRequestsPerMinute.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";

            await context.Response.WriteAsync(
                $$$"""{"error":"Rate limit exceeded. Maximum {{{MaxRequestsPerMinute}}} requests per minute.","retry_after":{{{retryAfter}}}}""");
            return;
        }

        // Add rate limit headers to successful responses
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-RateLimit-Limit"] = MaxRequestsPerMinute.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
            return Task.CompletedTask;
        });

        // Periodically clean up stale entries to prevent unbounded memory growth
        if (Interlocked.Increment(ref _requestsSinceCleanup) >= CleanupThreshold)
        {
            Interlocked.Exchange(ref _requestsSinceCleanup, 0);
            CleanupStaleEntries();
        }

        await _next(context);
    }

    private void CleanupStaleEntries()
    {
        var cutoff = DateTime.UtcNow - Window - Window; // 2x window for safety margin
        foreach (var (key, entry) in _clients)
        {
            lock (entry)
            {
                if (entry.WindowStart <= cutoff)
                {
                    _clients.TryRemove(key, out _);
                }
            }
        }
    }

    private sealed class RateLimitEntry
    {
        public DateTime WindowStart = DateTime.UtcNow;
        public int RequestCount;
    }
}

/// <summary>
/// Extension methods for registering the API key middleware.
/// </summary>
public static class ApiKeyMiddlewareExtensions
{
    /// <summary>
    /// Adds API key authentication middleware for /api/* endpoints.
    /// The key is read from the MDC_API_KEY environment variable.
    /// When no key is set, requests pass through so other authentication layers can decide access.
    /// Health check endpoints (/healthz, /readyz, /livez) are always exempt.
    /// </summary>
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app)
    {
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseMiddleware<ApiKeyRateLimitMiddleware>();
        return app;
    }
}
