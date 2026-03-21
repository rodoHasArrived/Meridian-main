using System.Net;
using Meridian.Application.Exceptions;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Serilog;

namespace Meridian.Infrastructure.Utilities;

/// <summary>
/// Centralized HTTP response handling to eliminate duplicate error handling code
/// across providers. Handles common HTTP status codes (429, 403, 404, etc.) consistently.
/// </summary>
public sealed class HttpResponseHandler
{
    private readonly ILogger _log;
    private readonly string _providerName;

    public HttpResponseHandler(string providerName, ILogger? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName, nameof(providerName));
        _providerName = providerName;
        _log = log ?? LoggingSetup.ForContext<HttpResponseHandler>();
    }

    /// <summary>
    /// Handle HTTP response and throw appropriate exceptions for error status codes.
    /// Returns result if successful, throws on error (except 404 which returns IsNotFound).
    /// </summary>
    public async Task<HttpResponseResult> HandleResponseAsync(
        HttpResponseMessage response,
        string symbol,
        string dataType,
        Action<RateLimitEventArgs>? onRateLimitHit = null,
        CancellationToken ct = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return new HttpResponseResult(true, response.StatusCode);
        }

        var statusCode = (int)response.StatusCode;
        var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        return statusCode switch
        {
            403 => HandleForbidden(symbol, dataType, errorContent),
            429 => HandleRateLimited(response, symbol, dataType, onRateLimitHit),
            404 => HandleNotFound(symbol, dataType),
            401 => HandleUnauthorized(symbol, dataType, errorContent),
            500 or 502 or 503 or 504 => HandleServerError(statusCode, symbol, dataType, errorContent),
            _ => HandleOtherError(statusCode, symbol, dataType, errorContent)
        };
    }

    /// <summary>
    /// Handle HTTP response without throwing exceptions. Returns a result object
    /// with categorized error information. This is the preferred method for use
    /// in base provider classes that need to return results rather than throw.
    /// </summary>
    /// <remarks>
    /// Use this method when you need to handle errors in the calling code
    /// rather than letting exceptions propagate.
    /// </remarks>
    public Task<HttpHandleResult> TryHandleResponseAsync(
        HttpResponseMessage response,
        string symbol,
        string dataType,
        Action<RateLimitInfo>? onRateLimitHit = null,
        CancellationToken ct = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.FromResult(HttpHandleResult.Success);
        }

        var statusCode = (int)response.StatusCode;

        switch (statusCode)
        {
            case 401:
                _log.Error("{Provider} API returned 401 for {Symbol} {DataType}: Unauthorized. Check API credentials.",
                    _providerName, symbol, dataType);
                return Task.FromResult(HttpHandleResult.AuthFailure);

            case 403:
                _log.Error("{Provider} API returned 403 for {Symbol} {DataType}: Forbidden. Verify API permissions.",
                    _providerName, symbol, dataType);
                return Task.FromResult(HttpHandleResult.AuthFailure);

            case 404:
                _log.Debug("{Provider} API returned 404 for {Symbol} {DataType}: Not found",
                    _providerName, symbol, dataType);
                return Task.FromResult(HttpHandleResult.NotFound);

            case 429:
                var retryAfter = HttpResiliencePolicy.ExtractRetryAfter(response) ?? TimeSpan.FromSeconds(60);

                if (onRateLimitHit != null)
                {
                    var info = new RateLimitInfo(_providerName, 0, 0, TimeSpan.Zero,
                        DateTimeOffset.UtcNow + retryAfter, true, retryAfter);
                    onRateLimitHit(info);
                }

                _log.Warning("{Provider} API returned 429 for {Symbol} {DataType}: Rate limit exceeded. Retry-After: {RetryAfter}s",
                    _providerName, symbol, dataType, retryAfter.TotalSeconds);
                return Task.FromResult(HttpHandleResult.RateLimited(retryAfter));

            case >= 500:
                _log.Error("{Provider} API returned {StatusCode} for {Symbol} {DataType}: Server error",
                    _providerName, statusCode, symbol, dataType);
                return Task.FromResult(HttpHandleResult.Error($"Server error: {statusCode}"));

            default:
                _log.Warning("{Provider} API returned {StatusCode} for {Symbol} {DataType}",
                    _providerName, statusCode, symbol, dataType);
                return Task.FromResult(HttpHandleResult.Error($"HTTP {statusCode}"));
        }
    }

    /// <summary>
    /// Synchronous version for responses already read.
    /// </summary>
    public HttpResponseResult HandleResponse(
        HttpStatusCode statusCode,
        string symbol,
        string dataType,
        string? errorContent = null)
    {
        if ((int)statusCode >= 200 && (int)statusCode < 300)
        {
            return new HttpResponseResult(true, statusCode);
        }

        return (int)statusCode switch
        {
            403 => HandleForbidden(symbol, dataType, errorContent),
            404 => HandleNotFound(symbol, dataType),
            401 => HandleUnauthorized(symbol, dataType, errorContent),
            _ => HandleOtherError((int)statusCode, symbol, dataType, errorContent)
        };
    }

    private HttpResponseResult HandleForbidden(string symbol, string dataType, string? errorContent)
    {
        _log.Error("{Provider} API returned 403 for {Symbol} {DataType}: Authentication failed. Error: {Error}",
            _providerName, symbol, dataType, errorContent);
        throw new InvalidOperationException(
            $"{_providerName} API returned 403: Authentication failed for {symbol}. Verify API keys.");
    }

    private HttpResponseResult HandleRateLimited(
        HttpResponseMessage response,
        string symbol,
        string dataType,
        Action<RateLimitEventArgs>? onRateLimitHit)
    {
        var retryAfter = ExtractRetryAfter(response);
        var resetsAt = DateTimeOffset.UtcNow + (retryAfter ?? TimeSpan.FromSeconds(60));

        var args = new RateLimitEventArgs(_providerName, symbol, dataType, resetsAt, retryAfter);
        onRateLimitHit?.Invoke(args);

        _log.Warning("{Provider} API returned 429 for {Symbol} {DataType}: Rate limit exceeded. Resets at {ResetsAt}",
            _providerName, symbol, dataType, resetsAt);

        throw new RateLimitException(
            $"{_providerName} API returned 429: Rate limit exceeded for {symbol}. Retry after {retryAfter?.TotalSeconds ?? 60}s",
            provider: _providerName,
            symbol: symbol,
            retryAfter: retryAfter ?? TimeSpan.FromSeconds(60));
    }

    private HttpResponseResult HandleNotFound(string symbol, string dataType)
    {
        _log.Warning("{Provider} API returned 404 for {Symbol} {DataType}: Symbol not found",
            _providerName, symbol, dataType);

        // Return result indicating not found but not an error that should throw
        return new HttpResponseResult(false, HttpStatusCode.NotFound, IsNotFound: true);
    }

    private HttpResponseResult HandleUnauthorized(string symbol, string dataType, string? errorContent)
    {
        _log.Error("{Provider} API returned 401 for {Symbol} {DataType}: Unauthorized. Error: {Error}",
            _providerName, symbol, dataType, errorContent);
        throw new InvalidOperationException(
            $"{_providerName} API returned 401: Unauthorized for {symbol}. Check API credentials.");
    }

    private HttpResponseResult HandleServerError(int statusCode, string symbol, string dataType, string? errorContent)
    {
        _log.Warning("{Provider} API returned {StatusCode} for {Symbol} {DataType}: Server error. Error: {Error}",
            _providerName, statusCode, symbol, dataType, errorContent);
        throw new InvalidOperationException(
            $"{_providerName} API returned {statusCode}: Server error for {symbol}. Please retry later.");
    }

    private HttpResponseResult HandleOtherError(int statusCode, string symbol, string dataType, string? errorContent)
    {
        _log.Warning("{Provider} API returned {StatusCode} for {Symbol} {DataType}: {Error}",
            _providerName, statusCode, symbol, dataType, errorContent);
        throw new InvalidOperationException(
            $"{_providerName} API returned {statusCode} for {symbol}");
    }

    /// <summary>
    /// Extract Retry-After header value from response.
    /// </summary>
    public static TimeSpan? ExtractRetryAfter(HttpResponseMessage response)
    {
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
}

/// <summary>
/// Result of HTTP response handling.
/// </summary>
public readonly record struct HttpResponseResult(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    bool IsNotFound = false,
    string? ErrorMessage = null
);

/// <summary>
/// Event arguments for rate limit events.
/// </summary>
public sealed class RateLimitEventArgs : EventArgs
{
    public string ProviderName { get; }
    public string Symbol { get; }
    public string DataType { get; }
    public DateTimeOffset ResetsAt { get; }
    public TimeSpan? RetryAfter { get; }

    public RateLimitEventArgs(string providerName, string symbol, string dataType, DateTimeOffset resetsAt, TimeSpan? retryAfter)
    {
        ProviderName = providerName;
        Symbol = symbol;
        DataType = dataType;
        ResetsAt = resetsAt;
        RetryAfter = retryAfter;
    }
}
