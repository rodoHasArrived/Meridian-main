using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Converts unhandled endpoint exceptions into safe, stable Problem Details responses.
/// </summary>
public sealed class MeridianApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<MeridianApiExceptionHandler> _logger;

    public MeridianApiExceptionHandler(ILogger<MeridianApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException &&
            httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        if (httpContext.Response.HasStarted)
            return false;

        var result = exception switch
        {
            BadHttpRequestException => ApiProblemDetails.Validation(
                httpContext,
                "request",
                "The request could not be processed."),
            ArgumentException => ApiProblemDetails.Validation(
                httpContext,
                "request",
                "One or more request values are invalid."),
            UnauthorizedAccessException => ApiProblemDetails.Forbidden(httpContext),
            TimeoutException => ApiProblemDetails.Timeout(httpContext),
            OperationCanceledException => ApiProblemDetails.Timeout(httpContext),
            _ => ApiProblemDetails.Internal(httpContext)
        };

        if (exception is BadHttpRequestException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Endpoint request failed with a handled client error at {RequestPath}.",
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogError(
                exception,
                "Unhandled endpoint exception at {RequestPath}.",
                httpContext.Request.Path);
        }

        httpContext.Response.Clear();
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
        return true;
    }
}
