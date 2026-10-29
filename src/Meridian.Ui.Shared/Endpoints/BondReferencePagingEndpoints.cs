using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Identity.Auth;
using Meridian.Instruments.FixedIncome;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Bounded keyset pages alongside the retained array contracts.</summary>
public static class BondReferencePagingEndpoints
{
    public static void MapBondReferencePagingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.ReferenceDataBondIssuerLadderPage, (
            [FromQuery] string issuerName, [FromQuery] int? pageSize, [FromQuery] string? cursor,
            [FromQuery] DateOnly? asOf, [FromServices] IBondReferenceService service, CancellationToken ct) =>
            PageAsync(() => service.GetIssuerLadderPageAsync(issuerName, pageSize ?? 100, cursor, asOf, ct), jsonOptions))
            .WithTags("BondReference").WithName("GetBondIssuerLadderPage")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
            .Produces<BondReferencePageDto>().Produces(StatusCodes.Status400BadRequest);

        app.MapGet(UiApiRoutes.ReferenceDataBondMaturityLadderPage, (
            [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? pageSize,
            [FromQuery] string? cursor, [FromQuery] DateOnly? asOf,
            [FromServices] IBondReferenceService service, CancellationToken ct) =>
            PageAsync(() => service.GetMaturityLadderPageAsync(from, to, pageSize ?? 100, cursor, asOf, ct), jsonOptions))
            .WithTags("BondReference").WithName("GetBondMaturityLadderPage")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
            .Produces<BondReferencePageDto>().Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> PageAsync(Func<Task<BondReferencePageDto>> query, JsonSerializerOptions options)
    {
        try { return Results.Json(await query().ConfigureAwait(false), options); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    }
}
