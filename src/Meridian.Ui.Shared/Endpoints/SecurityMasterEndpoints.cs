using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for Security Master command/query workflows.
/// </summary>
public static class SecurityMasterEndpoints
{
    public static void MapSecurityMasterEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("SecurityMaster");

        group.MapGet(UiApiRoutes.SecurityMasterById, async (
            Guid securityId,
            ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetSecurityMasterById")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.SecurityMasterResolve, async (
            ResolveSecurityRequest request,
            ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdentifierAsync(
                    request.IdentifierKind,
                    request.IdentifierValue,
                    request.Provider,
                    ct)
                .ConfigureAwait(false);

            if (detail is null)
            {
                return Results.NotFound();
            }

            if (request.ActiveOnly && detail.Status != SecurityStatusDto.Active)
            {
                return Results.NotFound();
            }

            return Results.Json(detail, jsonOptions);
        })
        .WithName("ResolveSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.SecurityMasterSearch, async (
            SecuritySearchRequest request,
            ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var results = await queryService.SearchAsync(request, ct).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("SearchSecurityMaster")
        .Produces<IReadOnlyList<SecuritySummaryDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.SecurityMasterHistory, async (
            Guid securityId,
            int? take,
            ISecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var history = await queryService.GetHistoryAsync(
                    new SecurityHistoryRequest(securityId, take.GetValueOrDefault(100)),
                    ct)
                .ConfigureAwait(false);

            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetSecurityMasterHistory")
        .Produces<IReadOnlyList<SecurityMasterEventEnvelope>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.SecurityMasterCreate, async (
            CreateSecurityRequest request,
            ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var detail = await service.CreateAsync(request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status201Created);

        group.MapPost(UiApiRoutes.SecurityMasterAmend, async (
            AmendSecurityTermsRequest request,
            ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var detail = await service.AmendTermsAsync(request, ct).ConfigureAwait(false);
            return Results.Json(detail, jsonOptions);
        })
        .WithName("AmendSecurityMaster")
        .Produces<SecurityDetailDto>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.SecurityMasterDeactivate, async (
            DeactivateSecurityRequest request,
            ISecurityMasterService service,
            CancellationToken ct) =>
        {
            await service.DeactivateAsync(request, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("DeactivateSecurityMaster")
        .Produces(StatusCodes.Status204NoContent);

        group.MapPost(UiApiRoutes.SecurityMasterAliasesUpsert, async (
            UpsertSecurityAliasRequest request,
            ISecurityMasterService service,
            CancellationToken ct) =>
        {
            var alias = await service.UpsertAliasAsync(request, ct).ConfigureAwait(false);
            return Results.Json(alias, jsonOptions);
        })
        .WithName("UpsertSecurityMasterAlias")
        .Produces<SecurityAliasDto>(StatusCodes.Status200OK);
    }
}
