using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for EDGAR reference-data ingest and local partition reads.
/// </summary>
public static class EdgarReferenceDataEndpoints
{
    private const int MaxEdgarIngestFilers = 250;

    public static void MapEdgarReferenceDataEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("EDGAR");

        group.MapPost(UiApiRoutes.SecurityMasterEdgarIngest, async (
            HttpContext httpContext,
            EdgarIngestRequest request,
            [FromServices] IEdgarIngestOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            if (!HasPermission(httpContext, UserPermission.ModifySecurityMaster))
                return EndpointHelpers.Forbidden();

            var normalizedRequest = request with
            {
                MaxFilers = request.MaxFilers is > 0 and <= MaxEdgarIngestFilers
                    ? request.MaxFilers
                    : MaxEdgarIngestFilers
            };

            var result = await orchestrator.IngestAsync(normalizedRequest, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("IngestEdgarSecurityMaster").RequirePermission(UserPermission.ModifySecurityMaster)
        .Accepts<EdgarIngestRequest>("application/json")
        .Produces<EdgarIngestResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.ReferenceDataEdgarFiler, async (
            string cik,
            [FromServices] IEdgarReferenceDataStore store,
            CancellationToken ct) =>
        {
            var record = await store.LoadFilerAsync(cik, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetEdgarFiler").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<EdgarFilerRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataEdgarFacts, async (
            string cik,
            [FromServices] IEdgarReferenceDataStore store,
            CancellationToken ct) =>
        {
            var record = await store.LoadFactsAsync(cik, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetEdgarFacts").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<EdgarFactsRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.ReferenceDataEdgarSecurityData, async (
            string cik,
            [FromServices] IEdgarReferenceDataStore store,
            CancellationToken ct) =>
        {
            var record = await store.LoadSecurityDataAsync(cik, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(record, jsonOptions);
        })
        .WithName("GetEdgarSecurityData").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<EdgarSecurityDataRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static bool HasPermission(HttpContext context, UserPermission permission)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserPermissionsKey, out var value) && value is UserPermission current)
            return (current & permission) == permission;

        return false;
    }
}
