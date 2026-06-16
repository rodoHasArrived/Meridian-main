using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;
using Meridian.Identity.Auth;
using Meridian.Instruments.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class OptionChainEndpoints
{
    public static void MapOptionChainEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("OptionChainReference");

        group.MapPost(UiApiRoutes.ReferenceDataOptionChainImport, async (
            OptionChainSnapshot snapshot,
            HttpContext context,
            [FromServices] IOptionChainImportService importService,
            CancellationToken ct) =>
        {
            if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is not UserPermission permissions)
            {
                return EndpointHelpers.Forbidden();
            }

            if ((permissions & UserPermission.ModifySecurityMaster) != UserPermission.ModifySecurityMaster)
            {
                return EndpointHelpers.Forbidden();
            }

            var imported = await importService.ImportSnapshotAsync(snapshot, ct).ConfigureAwait(false);
            return Results.Json(imported, jsonOptions);
        })
        .WithName("ImportOptionChainSnapshot")
        .Accepts<OptionChainSnapshot>("application/json")
        .Produces<OptionChainSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.ReferenceDataOptionChainSnapshot, async (
            [FromQuery] string underlyingSymbol,
            [FromQuery] DateOnly expiryDate,
            HttpContext context,
            [FromServices] IOptionReferenceService service,
            CancellationToken ct) =>
        {
            if (!HasViewSecurityMasterPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var snapshot = await service.GetChainSnapshotAsync(underlyingSymbol, expiryDate, ct).ConfigureAwait(false);
            return snapshot is null ? Results.NotFound() : Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetOptionChainSnapshot")
        .Produces<OptionChainSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static bool HasModifySecurityMasterPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster);

    private static bool HasViewSecurityMasterPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewSecurityMaster,
            UserPermission.ModifySecurityMaster);
}
