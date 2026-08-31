using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Middleware that enforces API key authentication on /api/* endpoints.
/// The API key is read from the MDC_API_KEY environment variable and supports
/// key rotation (re-reads the variable on each request).
/// When no key is configured, requests pass through so other auth layers can decide access.
/// Requests already authenticated by a login session (the browser workstation) are exempt —
/// the API key protects out-of-band API clients, so this middleware must run after
/// <see cref="LoginSessionMiddleware"/>.
/// Health check endpoints (/health, /healthz, /readyz, /livez) and the Prometheus scrape path (/metrics) are always exempt.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string ApiKeyEnvVar = "MDC_API_KEY";
    private const string ApiKeyRoleEnvVar = "MDC_API_KEY_ROLE";

    /// <summary>
    /// Request-item key whose presence identifies a principal established by a validated API key.
    /// Kept separate from the secret-bearing rate-limit item so authorization checks depend only
    /// on principal type, not on the key material itself.
    /// </summary>
    internal const string ApiKeyPrincipalKey = "CurrentUserIsApiKey";

    internal const string ApiKeyRateLimitKey = "ApiKey";

    /// <summary>
    /// Actor recorded for API-key requests so audit trails attribute them rather than leaving them
    /// unattributed.
    /// </summary>
    internal const string ApiKeyActor = "api-key";

    /// <summary>
    /// Role a validated key carries when <c>MDC_API_KEY_ROLE</c> is unset. Deliberately the weakest
    /// built-in role: a key that nobody has scoped should not be able to do more than look.
    /// </summary>
    private const UserRole DefaultApiKeyRole = UserRole.ReadOnly;

    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // "/health" (compose healthcheck) and "/metrics" (Prometheus scrape) must stay
        // reachable in an authenticated deployment, or the probes that watch the host are
        // the first thing authentication breaks. "/health/detailed" stays authenticated.
        "/health",
        "/healthz",
        "/metrics",
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

        // Requests authenticated via a login session (the browser workstation) are governed
        // by the session + CSRF layers; the API key protects out-of-band API clients. An
        // optional-mode anonymous principal is not a session and must not inherit that exemption,
        // or configuring MDC_API_KEY alongside MDC_ANONYMOUS_ROLE would stop enforcing the key.
        if (context.Items.ContainsKey(LoginSessionMiddleware.CurrentUserKey) &&
            !context.Items.ContainsKey(LoginSessionMiddleware.AnonymousPrincipalKey))
        {
            await _next(context);
            return;
        }

        // Check for API key in the header only to avoid leakage via URLs, logs, and browser history.
        var providedKey = context.Request.Headers[ApiKeyHeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedKey) ||
            !CryptographicEquals(providedKey, expectedApiKey))
        {
            await ApiProblemDetails.Unauthorized(
                    context,
                    "Provide a valid API key using the X-Api-Key header.")
                .ExecuteAsync(context);
            return;
        }

        // Store the validated API key identifier for downstream rate limiting
        context.Items[ApiKeyRateLimitKey] = providedKey;
        context.Items[ApiKeyPrincipalKey] = true;

        // A validated key needs an authorization context or it cannot pass any route that declares a
        // permission -- the endpoint filters resolve permissions from the session items, and without
        // these an API-key caller is refused everywhere the surface is actually governed.
        if (!TryResolveApiKeyRole(out var apiKeyRole))
        {
            await ApiProblemDetails.ServiceUnavailable(
                    context,
                    "authentication",
                    $"{ApiKeyRoleEnvVar} is set to a value that is not a known role. Set it to a valid role or unset it to use {DefaultApiKeyRole}.")
                .ExecuteAsync(context);
            return;
        }

        // A read-only role deliberately means a read-only API client whether the role was defaulted or
        // explicitly configured. Permission names alone are not enough to enforce that contract
        // because a few legacy POST routes use view permissions while mutating process-local replay,
        // option, or sampling state. Fail closed for every method outside the safe-method allowlist,
        // except where the endpoint itself declares an action grant the role holds; operators that
        // intentionally need a command endpoint must name the role that authorizes it.
        if (EndpointAuthorization.IsReadOnlyRoleMutation(context, apiKeyRole))
        {
            await ApiProblemDetails.Forbidden(
                    context,
                    $"The {apiKeyRole} API-key role allows only GET, HEAD, and OPTIONS requests, plus routes requiring {UserPermission.ExportData}. Set {ApiKeyRoleEnvVar} to a role that authorizes the required command endpoint.")
                .ExecuteAsync(context);
            return;
        }

        // LoginSessionMiddleware runs first, so optional mode may already have stamped its anonymous
        // actor and tenant. A validated key authenticates as its own principal and must not borrow
        // any of that scope or profile: authority comes from exactly one authentication posture.
        context.Items.Remove(LoginSessionMiddleware.AnonymousPrincipalKey);
        context.Items.Remove(LoginSessionMiddleware.DemoLocalOperatorPrincipalKey);
        context.Items.Remove(LoginSessionMiddleware.CurrentTenantIdKey);
        context.Items.Remove(LoginSessionMiddleware.CurrentUserCompanyIdKey);
        context.Items.Remove(LoginSessionMiddleware.CurrentUserRoleProfileNameKey);

        context.Items[LoginSessionMiddleware.CurrentUserKey] = ApiKeyActor;
        context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = apiKeyRole;

        // The canonical snapshot as well as the role: the endpoint filters resolve either, but many
        // handlers read the permissions item directly and refuse a caller that has only a role, which
        // would admit the request at the route boundary and reject it inside.
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = RolePermissions.For(apiKeyRole);

        await _next(context);
    }

    /// <summary>
    /// True when API-key authentication is configured and an API request presents an
    /// <c>X-Api-Key</c> header. <see cref="LoginSessionMiddleware"/> uses this to defer
    /// judgment on such requests to this middleware instead of rejecting them for
    /// lacking a session. Non-API routes remain protected by session authentication.
    /// </summary>
    internal static bool IsApiKeyCandidate(HttpContext context) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnvVar)) &&
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        context.Request.Headers.ContainsKey(ApiKeyHeaderName);

    /// <summary>
    /// Resolves the role a validated key carries. Unset means <see cref="DefaultApiKeyRole"/>; a value
    /// that names no known role fails closed rather than silently granting the default, because
    /// quietly applying a different permission set than the operator configured is the kind of
    /// authorization drift the governed surface exists to prevent.
    /// </summary>
    private static bool TryResolveApiKeyRole(out UserRole role)
    {
        var configured = Environment.GetEnvironmentVariable(ApiKeyRoleEnvVar);
        if (string.IsNullOrWhiteSpace(configured))
        {
            role = DefaultApiKeyRole;
            return true;
        }

        return TryParseRoleName(configured, out role);
    }


    /// <summary>
    /// Parses a role by name only. <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also
    /// accepts numeric text, and <see cref="UserRole.Admin"/> is the zero value, so a configuration of
    /// "0" -- or any stray number -- would otherwise resolve to full administrator rather than failing
    /// closed as an unrecognised value.
    /// </summary>
    internal static bool TryParseRoleName(string? configured, out UserRole role)
        => RolePermissions.TryParseRoleName(configured, out role);

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
        var partitionKey = context.Items.TryGetValue(ApiKeyMiddleware.ApiKeyRateLimitKey, out var apiKey) && apiKey is string key
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
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Response.Headers["X-RateLimit-Limit"] = MaxRequestsPerMinute.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";

            await ApiProblemDetails.TooManyRequests(
                    context,
                    retryAfter,
                    MaxRequestsPerMinute)
                .ExecuteAsync(context);
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
    /// Health check endpoints (/health, /healthz, /readyz, /livez) and the Prometheus scrape path (/metrics) are always exempt.
    /// </summary>
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app)
    {
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseMiddleware<ApiKeyRateLimitMiddleware>();
        return app;
    }
}
