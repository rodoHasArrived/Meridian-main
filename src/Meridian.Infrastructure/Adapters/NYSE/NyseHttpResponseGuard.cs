using System.Net;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Maps NYSE HTTP throttling responses to the shared typed provider exception.
/// </summary>
internal static class NyseHttpResponseGuard
{
    public static void ThrowIfRateLimited(
        HttpResponseMessage response,
        string providerId,
        string operation,
        string? symbol = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return;

        var retryAfter = HttpResiliencePolicy.ExtractRetryAfter(response) ?? TimeSpan.FromMinutes(1);
        throw new RateLimitException(
            $"{providerId} API rate limit exceeded during {operation}",
            provider: providerId,
            symbol: symbol,
            retryAfter: retryAfter);
    }
}
