using System.Text.Json;
using Meridian.Application.Services;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Shared helpers to reduce boilerplate in endpoint handlers.
/// Provides consistent null-check, try/catch, and JSON response patterns.
/// Uses FriendlyErrorFormatter for user-friendly error responses.
/// </summary>
internal static class EndpointHelpers
{
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
        catch (Exception ex)
        {
            return FormatErrorResult(ex, opts);
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
