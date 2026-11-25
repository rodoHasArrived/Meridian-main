using System.Text.Json;
using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Body of a merge request; the retired security is the route's <c>securityId</c>.</summary>
public sealed record MergeSecurityIntoRequest(Guid SurvivorSecurityId, long ExpectedVersion, string Reason);

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Review C1: merges the route's security (a duplicate) into a survivor. The duplicate is deactivated
    /// with a MergedInto reason and identifier resolution follows the recorded redirect; lots and history
    /// keep the duplicate's id.
    /// </summary>
    private static void MapMergeEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(UiApiRoutes.SecurityMasterMerge, async (
            Guid securityId,
            MergeSecurityIntoRequest request,
            HttpContext context,
            [FromServices] AppSecurityMaster.SecurityMasterMergeService merges,
            CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
            {
                return authorization;
            }

            try
            {
                var result = await merges.MergeAsync(
                        new AppSecurityMaster.MergeSecuritiesRequest(
                            securityId, request.SurvivorSecurityId, request.ExpectedVersion, ResolveActor(context), request.Reason),
                        ct)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AppSecurityMaster.SecurityMasterWriteRefusedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("MergeSecurityMasterSecurity").RequirePermission(Meridian.Identity.Auth.UserPermission.ModifySecurityMaster)
        .Produces<AppSecurityMaster.MergeSecuritiesResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        // ADR-034: GET terms valid at `validAt` as known at `knownAt` (default now).
        group.MapGet(UiApiRoutes.SecurityMasterTermsAsOf, async (
            Guid securityId,
            DateTimeOffset validAt,
            DateTimeOffset? knownAt,
            [FromServices] AppSecurityMaster.SecurityMasterValidTimeQuery? validTime,
            CancellationToken ct) =>
        {
            if (validTime is null)
            {
                return Results.Problem("Valid-time reads need the Security Master event store.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var terms = await validTime.GetAsync(securityId, validAt, knownAt, ct).ConfigureAwait(false);
            return terms is null ? Results.NotFound() : Results.Json(terms, jsonOptions);
        })
        .WithName("GetSecurityMasterTermsAsOf")
        .RequireAnyPermission(Meridian.Identity.Auth.UserPermission.ViewSecurityMaster, Meridian.Identity.Auth.UserPermission.ModifySecurityMaster)
        .Produces<AppSecurityMaster.SecurityTermsAsOf>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
