using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.CashFlows;
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
}
