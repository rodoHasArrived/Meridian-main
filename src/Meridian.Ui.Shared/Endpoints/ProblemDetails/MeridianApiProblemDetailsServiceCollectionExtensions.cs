using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Registers the shared Meridian Problem Details writer and exception handler.
/// </summary>
public static class MeridianApiProblemDetailsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the RFC 7807 services used by the shared UI endpoint surface.
    /// Hosts should call <c>UseExceptionHandler()</c> after building the application.
    /// </summary>
    public static IServiceCollection AddMeridianApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                var statusCode = problem.Status ?? context.HttpContext.Response.StatusCode;
                var hasFrameworkDefault = IsFrameworkDefaultProblemType(problem.Type);

                problem.Status = statusCode;
                problem.Instance ??= context.HttpContext.Request.Path.Value;

                if (string.IsNullOrWhiteSpace(problem.Type) || hasFrameworkDefault)
                    problem.Type = ResolveType(statusCode);
                if (string.IsNullOrWhiteSpace(problem.Title) || hasFrameworkDefault)
                    problem.Title = ResolveTitle(statusCode);

                problem.Extensions.TryAdd(
                    "traceId",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
                problem.Extensions.TryAdd("timestamp", DateTimeOffset.UtcNow);
            };
        });
        services.AddExceptionHandler<MeridianApiExceptionHandler>();

        return services;
    }

    private static bool IsFrameworkDefaultProblemType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;
        if (string.Equals(type, "about:blank", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Uri.TryCreate(type, UriKind.Absolute, out var uri))
            return false;

        return (string.Equals(uri.Host, "tools.ietf.org", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "www.rfc-editor.org", StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath.Contains("rfc", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveType(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status400BadRequest => ApiProblemTypes.Validation,
            StatusCodes.Status401Unauthorized => ApiProblemTypes.Unauthorized,
            StatusCodes.Status403Forbidden => ApiProblemTypes.Forbidden,
            StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
            StatusCodes.Status405MethodNotAllowed => ApiProblemTypes.MethodNotAllowed,
            StatusCodes.Status409Conflict => ApiProblemTypes.Conflict,
            StatusCodes.Status415UnsupportedMediaType => ApiProblemTypes.UnsupportedMediaType,
            StatusCodes.Status422UnprocessableEntity => ApiProblemTypes.UnprocessableEntity,
            StatusCodes.Status429TooManyRequests => ApiProblemTypes.TooManyRequests,
            StatusCodes.Status501NotImplemented => ApiProblemTypes.NotImplemented,
            StatusCodes.Status502BadGateway => ApiProblemTypes.BadGateway,
            StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
            StatusCodes.Status408RequestTimeout => ApiProblemTypes.Timeout,
            StatusCodes.Status504GatewayTimeout => ApiProblemTypes.Timeout,
            >= 400 and < 500 => ApiProblemTypes.ClientError,
            _ => ApiProblemTypes.Internal
        };

    private static string ResolveTitle(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status400BadRequest => "Validation Failed",
            StatusCodes.Status401Unauthorized => "Authentication Required",
            StatusCodes.Status403Forbidden => "Access Denied",
            StatusCodes.Status404NotFound => "Resource Not Found",
            StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
            StatusCodes.Status409Conflict => "State Conflict",
            StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
            StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
            StatusCodes.Status429TooManyRequests => "Rate Limit Exceeded",
            StatusCodes.Status501NotImplemented => "Not Implemented",
            StatusCodes.Status502BadGateway => "Bad Gateway",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            StatusCodes.Status408RequestTimeout => "Request Timed Out",
            StatusCodes.Status504GatewayTimeout => "Upstream Operation Timed Out",
            >= 400 and < 500 => "Request Failed",
            _ => "Internal Server Error"
        };
}
