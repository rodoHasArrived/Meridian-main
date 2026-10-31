using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapContractualCashFlowPreview(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapContractualCashFlowRuns("/api/security-master/{subjectId:guid}/cashflow-projections/runs", "Security",
            UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, jsonOptions);
        group.MapPost(UiApiRoutes.SecurityMasterCashFlowProjections + "/preview", async (
            Guid securityId, ContractualCashFlowRequest? request,
            [FromServices] IContractualCashFlowPreviewService service, CancellationToken ct) =>
        {
            if (request is null)
                return Results.BadRequest("A projection request is required.");
            try
            {
                var result = await service.PreviewSecurityAsync(securityId, request, ct).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["projection"] = [ex.Message] });
            }
        })
        .WithName("PreviewContractualSecurityCashFlows")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<ContractualCashFlowResult>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
    }
    // ── Cash Flow Source Assignments ─────────────────────────────────────────

    private static void MapCashFlowEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterCashFlowSource, async (
            Guid securityId,
            [FromServices] ISecurityMasterCashFlowService cashFlowService,
            CancellationToken ct) =>
        {
            var source = await cashFlowService
                .GetCashFlowSourceAsync(securityId, ct).ConfigureAwait(false);
            return source is null ? Results.NotFound() : Results.Json(source, jsonOptions);
        })
        .WithName("GetSecurityMasterCashFlowSource")
        .Produces<SecurityCashFlowSourceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut(UiApiRoutes.SecurityMasterCashFlowSource, async (
            Guid securityId,
            UpsertCashFlowSourceRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterCashFlowService cashFlowService,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out _))
                return Results.Unauthorized();

            await cashFlowService.UpsertCashFlowSourceAsync(request, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("UpsertSecurityMasterCashFlowSource")
        .Accepts<UpsertCashFlowSourceRequest>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapGet(UiApiRoutes.SecurityMasterCashFlowProjections, async (
            Guid securityId,
            string? scenario,
            [FromServices] ISecurityMasterCashFlowService cashFlowService,
            CancellationToken ct) =>
        {
            var parsed = Enum.TryParse<StructuredCashFlowScenario>(scenario, ignoreCase: true, out var s)
                ? s
                : StructuredCashFlowScenario.Base;

            var projection = await cashFlowService
                .GetProjectionAsync(securityId, parsed, ct).ConfigureAwait(false);
            return projection is null ? Results.NotFound() : Results.Json(projection, jsonOptions);
        })
        .WithName("GetSecurityMasterCashFlowProjections")
        .Produces<StructuredCashFlowProjectionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

}
