using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prometheus;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Times every HTTP request into <c>mdc_http_request_duration_seconds</c> and logs requests that
/// exceed a threshold, so slow workstation reports and API calls are measurable without enabling
/// OpenTelemetry tracing.
/// </summary>
/// <remarks>
/// The <c>route</c> label is the matched endpoint's route template (for example
/// <c>/api/reports/{id}</c>), never the raw path: raw paths carry identifiers and query strings,
/// which would leak data into metrics and make the series count unbounded. Requests that match no
/// endpoint are labelled <c>unmatched</c>. A request that throws is recorded with status 500
/// before the exception continues to the exception handler.
/// </remarks>
public sealed class RequestTimingMiddleware
{
    /// <summary>The <c>route</c> label for requests that matched no endpoint.</summary>
    public const string UnmatchedRoute = "unmatched";

    private static readonly Histogram RequestDuration = Prometheus.Metrics.CreateHistogram(
        "mdc_http_request_duration_seconds",
        "HTTP request duration by route template, method and status code",
        new HistogramConfiguration
        {
            LabelNames = ["route", "method", "status"],
            Buckets = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60]
        });

    private readonly RequestDelegate _next;
    private readonly TimeSpan _slowRequestThreshold;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="slowRequestThreshold">Requests at or above this duration are logged at Warning; zero or less disables the log.</param>
    /// <param name="logger">Logger for slow-request warnings.</param>
    public RequestTimingMiddleware(RequestDelegate next, TimeSpan slowRequestThreshold, ILogger<RequestTimingMiddleware>? logger = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _slowRequestThreshold = slowRequestThreshold;
        _logger = logger ?? NullLogger<RequestTimingMiddleware>.Instance;
    }

    /// <summary>Times the request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var route = RouteTemplate(context);
            var method = NormalizeMethod(context.Request.Method);
            var status = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            RequestDuration
                .WithLabels(route, method, status.ToString(CultureInfo.InvariantCulture))
                .Observe(elapsed.TotalSeconds);

            if (_slowRequestThreshold > TimeSpan.Zero && elapsed >= _slowRequestThreshold)
            {
                _logger.LogWarning(
                    "Slow request: {Method} {Route} returned {StatusCode} in {ElapsedMs} ms (threshold {ThresholdMs} ms, trace {TraceIdentifier})",
                    method,
                    SanitizeLogText(route),
                    status,
                    (long)elapsed.TotalMilliseconds,
                    (long)_slowRequestThreshold.TotalMilliseconds,
                    SanitizeLogText(context.TraceIdentifier));
            }
        }
    }

    /// <summary>The <c>method</c> label for methods outside the standard set.</summary>
    public const string OtherMethod = "OTHER";

    // Kestrel accepts any token as a method; mapping unknown ones to OTHER keeps the method label
    // bounded and keeps client-chosen text out of metrics and logs.
    private static string NormalizeMethod(string method)
        => HttpMethods.IsGet(method) ? HttpMethods.Get
            : HttpMethods.IsPost(method) ? HttpMethods.Post
            : HttpMethods.IsPut(method) ? HttpMethods.Put
            : HttpMethods.IsDelete(method) ? HttpMethods.Delete
            : HttpMethods.IsPatch(method) ? HttpMethods.Patch
            : HttpMethods.IsHead(method) ? HttpMethods.Head
            : HttpMethods.IsOptions(method) ? HttpMethods.Options
            : OtherMethod;

    private static string SanitizeLogText(string? value) =>
        (value ?? string.Empty).Replace('\r', '_').Replace('\n', '_');

    private static string RouteTemplate(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } template }
            ? template.StartsWith('/') ? template : "/" + template
            : UnmatchedRoute;
}

/// <summary>Registration for <see cref="RequestTimingMiddleware"/>.</summary>
public static class RequestTimingMiddlewareExtensions
{
    /// <summary>
    /// Adds request timing. Place it early, after the exception handler, so the measured duration
    /// covers authentication, rate limiting and the endpoint itself.
    /// </summary>
    public static IApplicationBuilder UseRequestTiming(this IApplicationBuilder app, TimeSpan slowRequestThreshold)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RequestTimingMiddleware>(slowRequestThreshold);
    }
}
