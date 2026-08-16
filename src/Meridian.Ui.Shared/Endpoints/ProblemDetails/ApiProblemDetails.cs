using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Stable RFC 7807 problem type identifiers used by shared UI endpoints.
/// </summary>
public static class ApiProblemTypes
{
    public const string ClientError = "https://meridian.io/errors/client-error";
    public const string Validation = "https://meridian.io/errors/validation";
    public const string Unauthorized = "https://meridian.io/errors/unauthorized";
    public const string Forbidden = "https://meridian.io/errors/forbidden";
    public const string NotFound = "https://meridian.io/errors/not-found";
    public const string MethodNotAllowed = "https://meridian.io/errors/method-not-allowed";
    public const string Conflict = "https://meridian.io/errors/conflict";
    public const string VersionConflict = "https://meridian.io/errors/version-conflict";
    public const string UnsupportedMediaType = "https://meridian.io/errors/unsupported-media-type";
    public const string UnprocessableEntity = "https://meridian.io/errors/unprocessable-entity";
    public const string TooManyRequests = "https://meridian.io/errors/too-many-requests";
    public const string ServiceUnavailable = "https://meridian.io/errors/service-unavailable";
    public const string BadGateway = "https://meridian.io/errors/bad-gateway";
    public const string NotImplemented = "https://meridian.io/errors/not-implemented";
    public const string Timeout = "https://meridian.io/errors/timeout";
    public const string Internal = "https://meridian.io/errors/internal";
}

/// <summary>
/// Creates safe, consistent Problem Details responses for shared UI endpoints.
/// </summary>
public static class ApiProblemDetails
{
    private const int ValidationErrorCode = 2000;
    private const int NotFoundErrorCode = 1004;
    private const int ServiceUnavailableErrorCode = 5001;
    private const int TimeoutErrorCode = 1003;
    private const int InternalErrorCode = 1001;

    public static IResult Validation(
        HttpContext? context,
        string field,
        string message)
        => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [field] = [message]
            },
            detail: "One or more request values are invalid.",
            instance: ResolveInstance(context),
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Failed",
            type: ApiProblemTypes.Validation,
            extensions: CreateExtensions(context, ValidationErrorCode));

    public static IResult Unauthorized(
        HttpContext? context,
        string detail = "Authentication is required to access this resource.")
        => Problem(
            context,
            StatusCodes.Status401Unauthorized,
            ApiProblemTypes.Unauthorized,
            "Authentication Required",
            detail);

    public static IResult Forbidden(
        HttpContext? context,
        string detail = "You do not have permission to access this resource.")
        => Problem(
            context,
            StatusCodes.Status403Forbidden,
            ApiProblemTypes.Forbidden,
            "Access Denied",
            detail);

    public static IResult NotFound(
        HttpContext? context,
        string detail = "The requested resource was not found.")
        => Problem(
            context,
            StatusCodes.Status404NotFound,
            ApiProblemTypes.NotFound,
            "Resource Not Found",
            detail,
            NotFoundErrorCode);

    public static IResult Conflict(HttpContext? context, string detail)
        => Problem(
            context,
            StatusCodes.Status409Conflict,
            ApiProblemTypes.Conflict,
            "State Conflict",
            detail);

    /// <summary>
    /// The canonical 409 body for an optimistic-concurrency (stale expected version) conflict.
    /// Version values are transported as invariant strings so numeric versions, revision tokens,
    /// and timestamp revisions all fit one contract; omitted values simply leave the extension
    /// member out. See <c>docs/reference/api-conflict-contract.md</c> for the full contract and
    /// which route families have converged on it.
    /// </summary>
    public static IResult VersionConflict(
        HttpContext? context,
        string detail,
        string? resourceId = null,
        string? expectedVersion = null,
        string? currentVersion = null)
    {
        var extensions = new Dictionary<string, object?>();
        if (resourceId is not null)
        {
            extensions["resourceId"] = resourceId;
        }

        if (expectedVersion is not null)
        {
            extensions["expectedVersion"] = expectedVersion;
        }

        if (currentVersion is not null)
        {
            extensions["currentVersion"] = currentVersion;
        }

        return Problem(
            context,
            StatusCodes.Status409Conflict,
            ApiProblemTypes.VersionConflict,
            "Version Conflict",
            detail,
            additionalExtensions: extensions);
    }

    public static IResult TooManyRequests(
        HttpContext? context,
        int retryAfterSeconds,
        int? limit = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["retryAfterSeconds"] = retryAfterSeconds
        };
        if (limit.HasValue)
        {
            extensions["limit"] = limit.Value;
        }

        return Problem(
            context,
            StatusCodes.Status429TooManyRequests,
            ApiProblemTypes.TooManyRequests,
            "Rate Limit Exceeded",
            "The request rate limit has been exceeded. Retry after the indicated interval.",
            additionalExtensions: extensions);
    }

    public static IResult ServiceUnavailable(
        HttpContext? context,
        string service,
        string? detail = null)
        => Problem(
            context,
            StatusCodes.Status503ServiceUnavailable,
            ApiProblemTypes.ServiceUnavailable,
            "Service Unavailable",
            detail ?? $"The {service} service is temporarily unavailable.",
            ServiceUnavailableErrorCode,
            new Dictionary<string, object?>
            {
                ["service"] = service
            });

    public static IResult Timeout(HttpContext? context)
        => Problem(
            context,
            StatusCodes.Status504GatewayTimeout,
            ApiProblemTypes.Timeout,
            "Upstream Operation Timed Out",
            "A required upstream operation did not complete in time.",
            TimeoutErrorCode);

    public static IResult Internal(
        HttpContext? context,
        string detail = "An unexpected error occurred.")
        => Problem(
            context,
            StatusCodes.Status500InternalServerError,
            ApiProblemTypes.Internal,
            "Internal Server Error",
            detail,
            InternalErrorCode);

    private static IResult Problem(
        HttpContext? context,
        int statusCode,
        string type,
        string title,
        string detail,
        int? errorCode = null,
        IReadOnlyDictionary<string, object?>? additionalExtensions = null)
    {
        var extensions = CreateExtensions(context, errorCode);
        if (additionalExtensions is not null)
        {
            foreach (var (key, value) in additionalExtensions)
            {
                extensions[key] = value;
            }
        }

        return Results.Problem(
            detail: detail,
            instance: ResolveInstance(context),
            statusCode: statusCode,
            title: title,
            type: type,
            extensions: extensions);
    }

    private static Dictionary<string, object?> CreateExtensions(
        HttpContext? context,
        int? errorCode)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = Activity.Current?.Id ?? context?.TraceIdentifier,
            ["timestamp"] = DateTimeOffset.UtcNow
        };

        if (errorCode.HasValue)
        {
            extensions["errorCode"] = errorCode.Value;
        }

        return extensions;
    }

    private static string? ResolveInstance(HttpContext? context)
        => context?.Request.Path.Value;
}
