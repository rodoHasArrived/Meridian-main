using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Review A2: ingest amendments held because a lower-ranked source tried to overwrite a value a
    /// higher-ranked source supplied. Each row names the conflicts opened for it; an operator adopts the
    /// held value through the conflict's governed resolution (which stages a draft) or discards the row.
    /// </summary>
    private static void MapHeldAmendmentEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterHeldAmendments, async (
            Guid? securityId,
            string? status,
            [FromServices] AppSecurityMaster.ISecurityMasterHeldAmendmentStore store,
            CancellationToken ct) =>
        {
            var rows = await store
                .ListAsync(securityId, string.IsNullOrWhiteSpace(status) ? AppSecurityMaster.SecurityMasterHeldAmendmentStatus.Held : status.Trim(), ct)
                .ConfigureAwait(false);
            return Results.Json(rows.Select(SecurityMasterReviewQueueProjection.ToDto).ToArray(), jsonOptions);
        })
        .WithName("ListSecurityMasterHeldAmendments")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityMasterHeldAmendmentDto>>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.SecurityMasterHeldAmendmentDiscard, async (
            Guid heldAmendmentId,
            HttpContext context,
            [FromServices] AppSecurityMaster.ISecurityMasterHeldAmendmentStore store,
            CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
            {
                return authorization;
            }

            if (await store.GetAsync(heldAmendmentId, ct).ConfigureAwait(false) is null)
            {
                return Results.NotFound();
            }

            var closed = await store
                .CloseAsync(heldAmendmentId, AppSecurityMaster.SecurityMasterHeldAmendmentStatus.Discarded, ResolveActor(context), ct)
                .ConfigureAwait(false);
            return closed
                ? Results.NoContent()
                : Results.Conflict(new { error = $"Held amendment '{heldAmendmentId:D}' is no longer held." });
        })
        .WithName("DiscardSecurityMasterHeldAmendment")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
