using Meridian.Application.FundStructure;
using Meridian.Contracts.Tenancy;
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
            // W9-GOV-008 criterion 2: a read refused for want of a resolvable tenant scope is a
            // refusal, not a fault. Letting it fall through to 500 would tell the caller the server
            // broke, bury the reason in error logs, and make the "rejected rather than defaulted"
            // behaviour indistinguishable from a bug to everyone who had to operate it.
            TenantScopeRejectedException => ApiProblemDetails.Forbidden(
                httpContext,
                "A tenant-scoped session is required for this read."),
            FundStructureTenantScopeException => ApiProblemDetails.Forbidden(
                httpContext,
                "A tenant-scoped session is required to read the fund structure."),
            TimeoutException => ApiProblemDetails.Timeout(httpContext),
            OperationCanceledException => ApiProblemDetails.Timeout(httpContext),
            _ => ApiProblemDetails.Internal(httpContext)
        };

        if (exception is BadHttpRequestException
            or ArgumentException
            or UnauthorizedAccessException
            or TenantScopeRejectedException
            or FundStructureTenantScopeException)
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
