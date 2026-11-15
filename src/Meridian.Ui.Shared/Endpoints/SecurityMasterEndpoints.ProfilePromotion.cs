using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapSecurityAssetProfilePromotion(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterAssetProfilePromotionAssessments, async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<ISecurityAssetProfilePromotionReadService>();
            if (service is null)
                return Results.Problem("Profile usage is unavailable in this host.", statusCode: 503);
            try
            {
                var result = await service.GetAssessmentsAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result with { CanExport = EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster) }, jsonOptions);
            }
            catch (NotSupportedException)
            {
                return Results.Problem("Profile usage is unavailable in this host.", statusCode: 503);
            }
        }).WithName("GetSecurityAssetProfilePromotionAssessments")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);

        group.MapPost(UiApiRoutes.SecurityMasterAssetProfileCandidateExport, async (
            string profileId, int version, SecurityAssetProfileCandidateExportRequestDto? request, HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster))
                return EndpointHelpers.Forbidden();
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            if (request is null || string.IsNullOrWhiteSpace(request.ExpectedProfileFingerprint) || version < 1)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["An exact profile version and assessment fingerprint are required."] });
            var service = context.RequestServices.GetService<ISecurityAssetProfilePromotionReadService>();
            if (service is null)
                return Results.Problem("Profile export is unavailable in this host.", statusCode: 503);
            try
            {
                var artifact = await service.ExportCandidateAsync(profileId, version, request.ExpectedProfileFingerprint,
                    actor, context.RequestAborted).ConfigureAwait(false);
                return Results.File(JsonSerializer.SerializeToUtf8Bytes(artifact, jsonOptions), "application/json",
                    $"meridian-asset-pack-draft-v{version}.json");
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
            catch (NotSupportedException) { return Results.Problem("Profile export is unavailable in this host.", statusCode: 503); }
        }).WithName("ExportSecurityAssetProfileCandidate")
            .DeclareNonMutating("Returns a developer-review JSON artifact from an exact approved profile and aggregate record counts; creates no profile, class, draft workflow or registry entry.")
            .RequirePermission(UserPermission.ModifySecurityMaster);
    }
}
