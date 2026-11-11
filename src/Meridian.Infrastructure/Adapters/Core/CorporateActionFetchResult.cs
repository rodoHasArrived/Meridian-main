using System.Net;
using System.Text.Json;
using Meridian.Core.Exceptions;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Coverage of an adapter's supported request scope, not live-provider certification.</summary>
public enum CorporateActionFetchStatus
{
    Unknown,
    Complete,
    Partial,
    Failed,
}

/// <summary>Safe machine-readable failure metadata. Upstream payloads and exception text are excluded.</summary>
public sealed record CorporateActionFetchFailure(
    string Code,
    int? HttpStatusCode = null,
    TimeSpan? RetryAfter = null,
    string? Scope = null);

/// <summary>Usable observations and the completeness of their source fetch.</summary>
public sealed record CorporateActionFetchResult(
    IReadOnlyList<CorporateActionCommand> Actions,
    CorporateActionFetchStatus Status,
    IReadOnlyList<CorporateActionFetchFailure> Failures)
{
    public static CorporateActionFetchResult Complete(IReadOnlyList<CorporateActionCommand> actions)
        => new(actions, CorporateActionFetchStatus.Complete, []);

    public static CorporateActionFetchResult Failed(
        string code, int? statusCode = null, TimeSpan? retryAfter = null, string? scope = null)
        => new([], CorporateActionFetchStatus.Failed, [new(code, statusCode, retryAfter, scope)]);

    /// <summary>A successful empty endpoint still contributes to partial coverage when another fails.</summary>
    public static CorporateActionFetchResult Combine(params CorporateActionFetchResult[] results)
    {
        var status = results.Length == 0 || results.Any(result => result.Status == CorporateActionFetchStatus.Unknown)
            ? CorporateActionFetchStatus.Unknown
            : results.All(result => result.Status == CorporateActionFetchStatus.Complete)
                ? CorporateActionFetchStatus.Complete
                : results.Any(result => result.Status is CorporateActionFetchStatus.Complete or CorporateActionFetchStatus.Partial)
                    ? CorporateActionFetchStatus.Partial
                    : CorporateActionFetchStatus.Failed;
        return new(results.SelectMany(result => result.Actions).ToArray(), status,
            results.SelectMany(result => result.Failures).ToArray());
    }

    public static CorporateActionFetchResult FromHttpFailure(HttpResponseMessage response, string? scope = null)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryAt)
            retryAfter = retryAt - DateTimeOffset.UtcNow;
        if (retryAfter < TimeSpan.Zero)
            retryAfter = TimeSpan.Zero;

        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "unauthorized",
            HttpStatusCode.TooManyRequests => "rate-limited",
            _ => "http-error",
        };
        return Failed(code, (int)response.StatusCode, retryAfter, scope);
    }

    /// <summary>Caller cancellation must be rethrown by the caller before using this helper.</summary>
    public static CorporateActionFetchResult FromException(Exception exception, string? scope = null)
        => exception switch
        {
            RateLimitException rateLimit => Failed("rate-limited", retryAfter: rateLimit.RetryAfter, scope: scope),
            OperationCanceledException or TimeoutException => Failed("timeout", scope: scope),
            JsonException => Failed("invalid-response", scope: scope),
            HttpRequestException request => Failed("transport-error", (int?)request.StatusCode, scope: scope),
            _ => Failed("fetch-failed", scope: scope),
        };
}
