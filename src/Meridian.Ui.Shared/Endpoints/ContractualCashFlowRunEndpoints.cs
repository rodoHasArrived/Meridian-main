using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

internal static class ContractualCashFlowRunEndpoints
{
    internal static void MapContractualCashFlowRuns(this RouteGroupBuilder group, string path, string kind,
        UserPermission view, UserPermission modify, JsonSerializerOptions json)
    {
        var writeRoute = group.MapPost(path, async (Guid subjectId, CreateContractualCashFlowRunRequest? request, HttpContext context) =>
        {
            if (request is null || request.Projection is null || request.RunId == Guid.Empty)
                return Results.BadRequest("Run identity and projection request are required.");
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            if (!await ExistsAsync(context, kind, subjectId).ConfigureAwait(false))
                return Results.NotFound();
            var store = context.RequestServices.GetRequiredService<IContractualCashFlowRunStore>();
            try
            {
                // Retry identity is checked before looking up current terms or market data.
                var retained = await store.GetAsync(kind, subjectId, request.RunId, context.RequestAborted).ConfigureAwait(false);
                if (retained is not null)
                    return ContractualCashFlowEngine.Hash(retained.Request) == ContractualCashFlowEngine.Hash(request.Projection)
                        ? Results.Json(retained, json) : Results.Conflict("Run identity is already bound to a different request.");
                var preview = context.RequestServices.GetRequiredService<IContractualCashFlowPreviewService>();
                var result = kind == "Security"
                    ? await preview.PreviewSecurityAsync(subjectId, request.Projection, context.RequestAborted).ConfigureAwait(false)
                    : await preview.PreviewLoanAsync(subjectId, request.Projection, context.RequestAborted).ConfigureAwait(false);
                if (result is null)
                    return Results.NotFound();
                if (!result.IsCalculated || result.ResolvedContract is null)
                    return Results.Json(result, json);
                var run = new ContractualCashFlowRun(request.RunId, kind, subjectId, DateTimeOffset.UtcNow, result.ResolvedContract, request.Projection, result, actor);
                return Results.Json(await store.SaveAsync(run, context.RequestAborted).ConfigureAwait(false), json);
            }
            catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["projection"] = [ex.Message] }); }
            catch (InvalidOperationException) { return Results.Conflict("Run identity conflicts with retained data."); }
        })
        .WithName("RetainContractual" + kind + "CashFlowRun")
        .RequirePermission(modify)
        .AddEndpointFilter(EndpointAuthorization.Require(modify))
        .RequireRateLimiting(kind == "Loan"
            ? UiEndpoints.DirectLendingMutationRateLimitPolicy
            : UiEndpoints.MutationRateLimitPolicy)
        .Produces<ContractualCashFlowRun>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        if (kind == "Loan")
            writeRoute.RequireFundScopedWriteTenant();

        group.MapGet(path + "/{runId:guid}", async (Guid subjectId, Guid runId, HttpContext context) =>
        {
            if (!await ExistsAsync(context, kind, subjectId).ConfigureAwait(false))
                return Results.NotFound();
            var store = context.RequestServices.GetRequiredService<IContractualCashFlowRunStore>();
            var run = await store.GetAsync(kind, subjectId, runId, context.RequestAborted).ConfigureAwait(false);
            return run is null ? Results.NotFound() : Results.Json(run, json);
        }).WithName("GetContractual" + kind + "CashFlowRun").RequireAnyPermission(view, modify)
        .Produces<ContractualCashFlowRun>().Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<bool> ExistsAsync(HttpContext context, string kind, Guid subjectId)
    {
        if (kind == "Security")
            return await context.RequestServices.GetRequiredService<ISecurityMasterQueryService>()
            .GetByIdAsync(subjectId, context.RequestAborted).ConfigureAwait(false) is not null;
        var loans = context.RequestServices.GetService<IDirectLendingService>();
        return loans is not null && await loans.GetLoanAsync(subjectId, context.RequestAborted).ConfigureAwait(false) is not null;
    }
}
