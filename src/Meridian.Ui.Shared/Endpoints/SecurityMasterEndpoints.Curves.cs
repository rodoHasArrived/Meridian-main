using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Maps the curve routes only when an <see cref="ISecurityCurveStore"/> is registered and the
    /// valuation route only when an <see cref="ISecurityValuationService"/> is. Neither has a Null
    /// fallback in DI, so a host without the Security Master database simply leaves the routes
    /// unavailable.
    /// </summary>
    private static void MapCurveAndValuationEndpoints(WebApplication app, RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        if (app.Services.GetService<ISecurityCurveStore>() is not null)
            MapCurveEndpoints(group, jsonOptions);

        if (app.Services.GetService<ISecurityValuationService>() is not null)
            MapValuationEndpoints(group, jsonOptions);
    }

    /// <summary>
    /// Stored market curves: write-once definitions and append-only published versions. Mapped only
    /// when an <see cref="ISecurityCurveStore"/> is registered; the actor on every write comes from
    /// the session, never the body.
    /// </summary>
    private static void MapCurveEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        /// <summary>Lists curve definitions, optionally by kind, currency and index.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterCurves, async (
            string? kind,
            string? currency,
            string? indexName,
            [FromServices] ISecurityCurveStore store,
            CancellationToken ct) =>
        {
            SecurityCurveKind? parsedKind = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<SecurityCurveKind>(kind, ignoreCase: true, out var value) || !Enum.IsDefined(value))
                    return Results.BadRequest(new { error = $"Unknown curve kind '{kind}'; expected Discount, Forward or Inflation." });
                parsedKind = value;
            }

            return Results.Json(await store.ListDefinitionsAsync(parsedKind, currency, indexName, ct).ConfigureAwait(false), jsonOptions);
        })
        .WithName("ListSecurityMasterCurves").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityCurveDefinitionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        /// <summary>Defines a curve. An existing key with identical facts returns the stored definition; different facts are refused (409).</summary>
        group.MapPost(UiApiRoutes.SecurityMasterCurves, async (
            DefineSecurityCurveRequest? request,
            HttpContext context,
            [FromServices] ISecurityCurveStore store,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.BadRequest(new { error = "A curve definition is required." });

            return await RunCurveWriteAsync(context, () => store.DefineAsync(request with { CreatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false);
        })
        .WithName("DefineSecurityMasterCurve").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<SecurityCurveDefinitionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads one curve definition.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterCurve, async (
            Guid curveId,
            [FromServices] ISecurityCurveStore store,
            CancellationToken ct) =>
            await store.GetDefinitionAsync(curveId, ct).ConfigureAwait(false) is { } definition
                ? Results.Json(definition, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterCurve").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityCurveDefinitionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Lists a curve's versions; with <c>asOf</c> (and optionally <c>knownAt</c>) the single latest match, as a list of one or none.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterCurveVersions, async (
            Guid curveId,
            DateOnly? asOf,
            DateTimeOffset? knownAt,
            [FromServices] ISecurityCurveStore store,
            CancellationToken ct) =>
        {
            if (await store.GetDefinitionAsync(curveId, ct).ConfigureAwait(false) is null)
                return Results.NotFound();

            if (asOf is { } date)
            {
                var latest = await store.GetLatestVersionAsOfAsync(curveId, date, knownAt, ct).ConfigureAwait(false);
                IReadOnlyList<SecurityCurveVersionDto> match = latest is null ? [] : [latest];
                return Results.Json(match, jsonOptions);
            }

            return Results.Json(await store.ListVersionsAsync(curveId, ct).ConfigureAwait(false), jsonOptions);
        })
        .WithName("ListSecurityMasterCurveVersions").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityCurveVersionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Publishes a version of the curve. Identical content for the same as-of returns the retained version.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterCurveVersions, async (
            Guid curveId,
            PublishSecurityCurveVersionRequest? request,
            HttpContext context,
            [FromServices] ISecurityCurveStore store,
            CancellationToken ct) =>
        {
            if (request is null || request.CurveId != curveId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["curveId"] = ["Request CurveId must match the route parameter."]
                });

            return await RunCurveWriteAsync(context, () => store.PublishVersionAsync(request with { PublishedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false);
        })
        .WithName("PublishSecurityMasterCurveVersion").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<SecurityCurveVersionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads one published version by number.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterCurveVersion, async (
            Guid curveId,
            long version,
            [FromServices] ISecurityCurveStore store,
            CancellationToken ct) =>
            await store.GetVersionAsync(curveId, version, ct).ConfigureAwait(false) is { } stored
                ? Results.Json(stored, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterCurveVersion").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityCurveVersionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Values a security as of a date with the model its request or asset class names. A read with
    /// the same receipt side effect the golden-copy read has; blocked results are 200 with blockers.
    /// Mapped only when an <see cref="ISecurityValuationService"/> is registered.
    /// </summary>
    private static void MapValuationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(UiApiRoutes.SecurityMasterValuation, async (
            Guid securityId,
            SecurityValuationRequest? request,
            [FromServices] ISecurityValuationService valuation,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            var result = await valuation.ValueAsync(request, ct).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("ValueSecurityMaster").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityValuationResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Runs a curve write: permission first, then the store; a rule failure is 400 and a write-once conflict 409.</summary>
    private static async Task<IResult> RunCurveWriteAsync<T>(HttpContext context, Func<Task<T>> write, JsonSerializerOptions jsonOptions)
    {
        var authorizationResult = RequireSecurityMasterMutationPermission(context);
        if (authorizationResult is not null)
            return authorizationResult;

        try
        {
            return Results.Json(await write().ConfigureAwait(false), jsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }
}
