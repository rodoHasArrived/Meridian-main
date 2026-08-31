using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Shared helpers to reduce boilerplate in endpoint handlers.
/// Provides consistent fail-closed service checks, safe error handling, and JSON response patterns.
/// </summary>
internal static class EndpointHelpers
{
    /// <summary>
    /// Returns a plain 403 result without invoking ASP.NET Core authentication handlers.
    /// Minimal workstation test hosts and the desktop-local UI host store permissions in
    /// <see cref="HttpContext.Items"/>, so endpoint-level authorization failures should not
    /// depend on registered authentication services.
    /// </summary>
    internal static IResult Forbidden(HttpContext? context = null)
        => ApiProblemDetails.Forbidden(context);

    /// <summary>
    /// Handles a synchronous endpoint handler with service null-check and error handling.
    /// </summary>
    internal static IResult HandleSync<TService>(
        TService? service,
        Func<TService, object> handler,
        JsonSerializerOptions opts,
        HttpContext? context = null) where TService : class
    {
        if (service is null)
            return ApiProblemDetails.ServiceUnavailable(context, typeof(TService).Name);

        try
        {
            return Results.Json(handler(service), opts);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FormatErrorResult(ex, context);
        }
    }

    /// <summary>
    /// Handles an async endpoint handler with service null-check and error handling.
    /// </summary>
    internal static async Task<IResult> HandleAsync<TService>(
        TService? service,
        Func<TService, Task<object>> handler,
        JsonSerializerOptions opts,
        HttpContext? context = null) where TService : class
    {
        if (service is null)
            return ApiProblemDetails.ServiceUnavailable(context, typeof(TService).Name);

        try
        {
            var result = await handler(service);
            return Results.Json(result, opts);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FormatErrorResult(ex, context);
        }
    }

    /// <summary>
    /// Handles an async endpoint with a cancellation token.
    /// </summary>
    internal static async Task<IResult> HandleAsync<TService>(
        TService? service,
        Func<TService, CancellationToken, Task<object>> handler,
        JsonSerializerOptions opts,
        CancellationToken ct,
        HttpContext? context = null) where TService : class
    {
        if (service is null)
            return ApiProblemDetails.ServiceUnavailable(context, typeof(TService).Name);

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
            return FormatErrorResult(ex, context);
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
    /// Retained temporarily for source compatibility with older endpoint call sites. Arbitrary
    /// exception messages are never copied into a 500 response; the stable
    /// <paramref name="errorMessage"/> is always returned.
    /// </param>
    /// <param name="context">Optional request context used to populate Problem Details metadata.</param>
    internal static async Task<IResult> GuardAsync(
        Func<Task<IResult>> handler,
        string errorMessage,
        ILogger? logger = null,
        Func<Exception, IResult?>? mapException = null,
        bool includeExceptionMessage = false,
        HttpContext? context = null)
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
            _ = includeExceptionMessage;
            return ApiProblemDetails.Internal(context, errorMessage);
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
    /// Converts only well-understood client and upstream failures to their HTTP equivalents.
    /// Ambiguous exceptions fail closed as a safe internal-error Problem response.
    /// </summary>
    private static IResult FormatErrorResult(Exception ex, HttpContext? context) => ex switch
    {
        ArgumentException => ApiProblemDetails.Validation(
            context,
            "request",
            "The request is invalid."),
        UnauthorizedAccessException => ApiProblemDetails.Forbidden(context),
        TimeoutException => ApiProblemDetails.Timeout(context),
        _ => ApiProblemDetails.Internal(context)
    };
}
