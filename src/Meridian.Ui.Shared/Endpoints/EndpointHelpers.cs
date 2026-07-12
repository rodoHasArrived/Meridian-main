using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
    /// Handles a synchronous endpoint handler with shared error formatting.
    /// </summary>
    internal static IResult HandleSync(Func<IResult> handler, JsonSerializerOptions? opts = null)
    {
        try
        {
            return handler();
        }
        catch (Exception ex)
        {
            return Error(ex, opts);
        }
    }

    /// <summary>
    /// Handles an async endpoint handler with shared error formatting.
    /// </summary>
    internal static async Task<IResult> HandleAsync(Func<Task<IResult>> handler, JsonSerializerOptions? opts = null)
    {
        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error(ex, opts);
        }
    }

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
            return Error(ex, opts);
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
            return Error(ex, opts);
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
        catch (Exception ex)
        {
            return Error(ex, opts);
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
    internal static IResult Error(
        Exception ex,
        JsonSerializerOptions? opts = null,
        string? error = null,
        int? statusCode = null)
    {
        var formatted = FriendlyErrorFormatter.Format(ex);
        var resolvedStatusCode = statusCode ?? GetHttpStatusCode(ex);
        var title = string.IsNullOrWhiteSpace(error) ? formatted.Title : error.Trim();

        return Results.Json(new
        {
            error = title,
            code = formatted.Code,
            message = GetClientMessage(ex, formatted, resolvedStatusCode, title),
            suggestion = formatted.Suggestion,
            docsLink = formatted.DocsLink,
            timestamp = DateTimeOffset.UtcNow
        }, opts, statusCode: resolvedStatusCode);
    }

    private static string GetClientMessage(
        Exception ex,
        FormattedError formatted,
        int statusCode,
        string title)
    {
        if (CanExposeMessage(ex, statusCode))
        {
            return formatted.Message;
        }

        return statusCode >= StatusCodes.Status500InternalServerError
            ? title
            : formatted.Title;
    }

    private static bool CanExposeMessage(Exception ex, int statusCode)
        => statusCode < StatusCodes.Status500InternalServerError &&
           ex is ArgumentException or UnauthorizedAccessException;

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
