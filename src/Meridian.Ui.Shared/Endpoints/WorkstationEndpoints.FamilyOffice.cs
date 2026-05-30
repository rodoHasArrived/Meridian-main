using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Serialization;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapFamilyOfficeEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/family-office/overview", async Task<IResult> (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FamilyOfficeReadService>();
            if (service is null)
            {
                return Results.Problem("Family-office read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var overview = await service.GetOverviewAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(overview, FamilyOfficeJsonContext.Default.FamilyOfficeOverviewDto);
        })
        .WithName("GetWorkstationFamilyOfficeOverview")
        .WithTags("Workstation Family Office")
        .Produces<FamilyOfficeOverviewDto>(200)
        .Produces(501);

        group.MapGet("/family-office/balance-sheet", async Task<IResult> (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FamilyOfficeReadService>();
            if (service is null)
            {
                return Results.Problem("Family-office read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var balanceSheet = await service.GetBalanceSheetAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(balanceSheet, FamilyOfficeJsonContext.Default.FamilyOfficeBalanceSheetResponseDto);
        })
        .WithName("GetWorkstationFamilyOfficeBalanceSheet")
        .WithTags("Workstation Family Office")
        .Produces<FamilyOfficeBalanceSheetResponseDto>(200)
        .Produces(501);

        group.MapGet("/family-office/entities", async Task<IResult> (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FamilyOfficeReadService>();
            if (service is null)
            {
                return Results.Problem("Family-office read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var entities = await service.GetEntitiesAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(entities, FamilyOfficeJsonContext.Default.FamilyOfficeEntitiesResponseDto);
        })
        .WithName("GetWorkstationFamilyOfficeEntities")
        .WithTags("Workstation Family Office")
        .Produces<FamilyOfficeEntitiesResponseDto>(200)
        .Produces(501);

        group.MapGet("/family-office/ownership-graph", async Task<IResult> (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FamilyOfficeReadService>();
            if (service is null)
            {
                return Results.Problem("Family-office read service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var ownershipGraph = await service.GetOwnershipGraphAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(ownershipGraph, FamilyOfficeJsonContext.Default.FamilyOfficeOwnershipGraphResponseDto);
        })
        .WithName("GetWorkstationFamilyOfficeOwnershipGraph")
        .WithTags("Workstation Family Office")
        .Produces<FamilyOfficeOwnershipGraphResponseDto>(200)
        .Produces(501);
    }
}
