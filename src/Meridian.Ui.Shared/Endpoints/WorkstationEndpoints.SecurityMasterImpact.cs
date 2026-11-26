using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapSecurityMasterImpactEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/security-master").RequireWorkstationTenantScope();
        group.MapPost("/{securityId:guid}/revisions/{revisionId:guid}/impact-preview", async (
            Guid securityId, Guid revisionId, CreateSecurityMasterImpactPreviewRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<ISecurityMasterImpactPreviewService>();
            var scope = context.RequestServices.GetService<ISecurityMasterImpactScopeAccessor>();
            if (service is null || scope is null)
                return WorkbenchServiceUnavailable();
            return await ExecuteWorkbenchAsync(() => service.CreateAsync(securityId, revisionId, scope.GetRequired(), request.AsOf, context.RequestAborted), jsonOptions).ConfigureAwait(false);
        }).RequirePermission(UserPermission.ModifySecurityMaster);
        group.MapGet("/impact-previews/{receiptId}", async (string receiptId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<ISecurityMasterImpactPreviewService>();
            var scope = context.RequestServices.GetService<ISecurityMasterImpactScopeAccessor>();
            if (service is null || scope is null)
                return WorkbenchServiceUnavailable();
            try
            {
                var receipt = await service.GetAsync(scope.GetRequired(), receiptId, context.RequestAborted).ConfigureAwait(false);
                return receipt is null ? Results.NotFound() : Results.Json(receipt, jsonOptions);
            }
            catch (ArgumentException)
            { return WorkbenchUnprocessable("invalid-impact-receipt", "A canonical impact receipt digest is required."); }
            catch (SecurityMasterImpactPreviewException ex)
            { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: ex.Code, detail: ex.Message); }
        }).RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);
    }
}
