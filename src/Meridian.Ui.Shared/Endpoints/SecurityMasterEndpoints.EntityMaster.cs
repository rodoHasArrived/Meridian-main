using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.EntityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Body linking an entity to its direct parent from a date.</summary>
public sealed record SetEntityParentRequest(Guid ParentEntityId, DateOnly ValidFrom);

/// <summary>Body linking a security to its issuer from a date.</summary>
public sealed record LinkSecurityIssuerRequest(Guid IssuerEntityId, DateOnly ValidFrom);

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// ADR-033 entity master routes: legal entities (LEI/CIK), valid-dated parent hierarchy, and the
    /// valid-dated issuer link from a Security Master security to its legal entity.
    /// </summary>
    private static void MapEntityMasterEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("EntityMaster");

        group.MapPost(UiApiRoutes.EntityMasterEntities, async (
            CreateLegalEntityRequest request, HttpContext context, [FromServices] IEntityMasterStore store, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            try
            {
                return Results.Json(await store.CreateEntityAsync(request, ResolveActor(context), ct).ConfigureAwait(false), jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        }).WithName("CreateEntityMasterLegalEntity");

        group.MapGet(UiApiRoutes.EntityMasterEntity, async (Guid entityId, DateOnly? asOf, [FromServices] IEntityMasterStore store, CancellationToken ct) =>
        {
            var entity = await store.GetEntityAsync(entityId, ct).ConfigureAwait(false);
            if (entity is null)
                return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var parent = await store.GetParentAsync(entityId, date, ct).ConfigureAwait(false);
            var ultimate = await store.GetUltimateParentAsync(entityId, date, ct).ConfigureAwait(false);
            return Results.Json(new { entity, parent, ultimateParent = ultimate }, jsonOptions);
        }).WithName("GetLegalEntity").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);

        group.MapPut(UiApiRoutes.EntityMasterEntityParent, async (
            Guid entityId, SetEntityParentRequest request, HttpContext context, [FromServices] IEntityMasterStore store, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            try
            {
                return Results.Json(await store.SetParentAsync(entityId, request.ParentEntityId, request.ValidFrom, ResolveActor(context), ct).ConfigureAwait(false), jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        }).WithName("SetLegalEntityParent");

        group.MapGet(UiApiRoutes.EntityMasterSecurityIssuer, async (Guid securityId, DateOnly? asOf, [FromServices] IEntityMasterStore store, CancellationToken ct) =>
        {
            var link = await store.GetIssuerAsync(securityId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), ct).ConfigureAwait(false);
            return link is null ? Results.NotFound() : Results.Json(link, jsonOptions);
        }).WithName("GetSecurityIssuer").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);

        group.MapPut(UiApiRoutes.EntityMasterSecurityIssuer, async (
            Guid securityId, LinkSecurityIssuerRequest request, HttpContext context, [FromServices] IEntityMasterStore store, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            try
            {
                return Results.Json(await store.LinkIssuerAsync(securityId, request.IssuerEntityId, request.ValidFrom, ResolveActor(context), ct).ConfigureAwait(false), jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        }).WithName("LinkSecurityIssuer");
    }
}
