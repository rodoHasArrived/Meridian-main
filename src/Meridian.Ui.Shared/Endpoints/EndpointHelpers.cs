using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Meridian.Platform.Results;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Shared helpers to reduce boilerplate in endpoint handlers.
/// Provides consistent null-check, try/catch, and JSON response patterns.
/// Uses FriendlyErrorFormatter for user-friendly error responses.
/// </summary>
internal static class EndpointHelpers
{
    /// <summary>
    /// Returns a plain 403 result without invoking ASP.NET Core authentication handlers.
    /// Minimal workstation test hosts and the desktop-local UI host store permissions in
    /// <see cref="HttpContext.Items"/>, so endpoint-level authorization failures should not
    /// depend on registered authentication services.
    /// </summary>
    internal static IResult Forbidden()
        => Results.StatusCode(StatusCodes.Status403Forbidden);

    /// <summary>
    /// Handles a synchronous endpoint handler with service null-check and error handling.
    /// </summary>
    internal static IResult HandleSync<TService>(
        TService? service,
        Func<TService, object> handler,
        JsonSerializerOptions opts) where TService : class
    {
        if (service is null)
            return Results.Json(new { error = "Service unavailable" }, opts);

        try
        {
            return Results.Json(handler(service), opts);
        }
        catch (Exception ex)
        {
            return FormatErrorResult(ex, opts);
        }
    }

    /// <summary>
    /// Handles an async endpoint handler with service null-check and error handling.
    /// </summary>
    internal static async Task<IResult> HandleAsync<TService>(
        TService? service,
        Func<TService, Task<object>> handler,
        JsonSerializerOptions opts) where TService : class
    {
        if (service is null)
            return Results.Json(new { error = "Service unavailable" }, opts);

        try
        {
            var result = await handler(service);
            return Results.Json(result, opts);
        }
        catch (Exception ex)
        {
            return FormatErrorResult(ex, opts);
        }
    }

    /// <summary>
    /// Handles an async endpoint with a cancellation token.
    /// </summary>
    internal static async Task<IResult> HandleAsync<TService>(
        TService? service,
        Func<TService, CancellationToken, Task<object>> handler,
        JsonSerializerOptions opts,
        CancellationToken ct) where TService : class
    {
        if (service is null)
            return Results.Json(new { error = "Service unavailable" }, opts);

        try
        {
            var result = await handler(service, ct);
            return Results.Json(result, opts);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The request was aborted; propagate so the host treats it as a cancellation
            // instead of reporting an error response to a client that is no longer listening.
            throw;
        }
        catch (Exception ex)
        {
            return FormatErrorResult(ex, opts);
        }
    }

    /// <summary>
    /// Runs an endpoint handler with the shared guarded-call error contract: cancellations
    /// propagate to the host, optional per-endpoint exception mapping runs first, and any other
    /// failure is logged (when a logger is supplied) and converted to a
    /// <see cref="Results.Problem(string?, string?, int?, string?, string?, System.Collections.Generic.IDictionary{string, object?}?)"/>
    /// response. Handlers keep full control of their success payloads.
    /// </summary>
    /// <param name="handler">The endpoint body producing the success result.</param>
    /// <param name="errorMessage">Stable, user-facing failure message for the Problem response.</param>
    /// <param name="logger">Optional logger; failures are recorded as errors when present.</param>
    /// <param name="mapException">
    /// Optional per-endpoint mapping for expected exception types (validation, not-found, ...).
    /// Return null to fall through to the generic Problem response.
    /// </param>
    /// <param name="includeExceptionMessage">
    /// When true the Problem detail is "{errorMessage}: {ex.Message}" — used by endpoints whose
    /// clients historically surfaced the exception text to operators.
    /// </param>
    internal static async Task<IResult> GuardAsync(
        Func<Task<IResult>> handler,
        string errorMessage,
        ILogger? logger = null,
        Func<Exception, IResult?>? mapException = null,
        bool includeExceptionMessage = false)
    {
        try
        {
            return await handler();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (mapException?.Invoke(ex) is { } mapped)
                return mapped;

            logger?.LogError(ex, "{EndpointFailure}", errorMessage);
            return Results.Problem(includeExceptionMessage ? $"{errorMessage}: {ex.Message}" : errorMessage);
        }
    }

    /// <summary>
    /// Parses a date string or returns today's date.
    /// </summary>
    internal static DateOnly ParseDateOrToday(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return DateOnly.FromDateTime(DateTime.UtcNow);

        return DateOnly.TryParse(dateStr, out var date) ? date : DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Formats an exception into a structured JSON error response using FriendlyErrorFormatter.
    /// Returns a consistent error envelope with error code, message, and actionable suggestion.
    /// </summary>
    private static IResult FormatErrorResult(Exception ex, JsonSerializerOptions opts)
    {
        var formatted = FriendlyErrorFormatter.Format(ex);
        var statusCode = GetHttpStatusCode(ex);

        return Results.Json(new
        {
            error = formatted.Title,
            code = formatted.Code,
            message = formatted.Message,
            suggestion = formatted.Suggestion,
            docsLink = formatted.DocsLink,
            timestamp = DateTimeOffset.UtcNow
        }, opts, statusCode: statusCode);
    }

    /// <summary>
    /// Maps exception types to appropriate HTTP status codes.
    /// </summary>
    private static int GetHttpStatusCode(Exception ex) => ex switch
    {
        ArgumentException or ArgumentNullException => 400,
        UnauthorizedAccessException => 403,
        FileNotFoundException or DirectoryNotFoundException => 404,
        InvalidOperationException => 409,
        NotSupportedException or NotImplementedException => 501,
        TimeoutException => 504,
        OperationCanceledException => 408,
        _ => 500
    };
}
