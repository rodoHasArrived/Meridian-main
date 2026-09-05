using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static async Task<IResult?> ValidateClosePublicationReadinessAsync(HttpContext context,
        Guid workflowId, OperationsCloseWorkflowRequestDto request, JsonSerializerOptions jsonOptions)
    {
        var scope = request.CloseScope;
        var authority = context.RequestServices.GetService<IFinancialOperationsCommandCenterReadService>();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (scope?.FundProfileId is { Length: > 0 } fundProfileId)
        {
            var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
            var failClosed = (context.RequestServices.GetService<TenantScopeEnforcementOptions>()
                ?? TenantScopeEnforcementOptions.DeploymentBoundary).IsFailClosed;
            if ((failClosed && (!tenant.HasTenantScope || guard is null))
                || (guard is not null && !(await guard.EvaluateAsync(tenant, fundProfileId, context.RequestAborted).ConfigureAwait(false)).IsAllowed))
                return EndpointHelpers.Forbidden();
            if (failClosed)
            {
                var registry = context.RequestServices.GetService<Meridian.Contracts.Tenancy.IFundProfileTenancyRegistry>();
                if (registry is null || (await registry.ResolveAsync(fundProfileId, context.RequestAborted).ConfigureAwait(false))?.IsHeldBy(tenant.TenantId) != true)
                    return EndpointHelpers.Forbidden();
            }
        }
        var decision = scope is null || authority is null ? null : await authority.GetCommandCenterAsync(
            scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId, scope.PeriodId, scope.EntityId,
            context.RequestAborted, tenant.TenantId, tenant.CompanyId).ConfigureAwait(false);
        if (decision?.CloseReadiness is { IsComplete: true, IsReadyToClose: true }
            && decision.CloseReadiness.Scope == scope
            && decision.ActiveWorkflow?.WorkflowId == workflowId
            && decision.ActiveWorkflow.Version == request.ExpectedVersion)
            return null;

        var blockers = decision?.CloseReadiness?.Blockers.Select(blocker => new OperationsWorkflowBlockerDto(
            blocker.Code, blocker.Message, null, blocker.Severity, [])).ToArray() ?? [];
        if (blockers.Length == 0)
            blockers = [new("CLOSE_READINESS_REQUIRED", "A current complete shared close decision for the selected workflow and scope is required.", null, "Critical", [])];
        return OperationsTransitionResult(new OperationsTransitionResultDto(false, "CLOSE_READINESS_REQUIRED",
            "Resolve shared close readiness before publishing the close package.", null, blockers, []), jsonOptions);
    }

    private static void MapCloseReadinessEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.FinancialOperationsCommandCenter), async (
            string? fundProfileId, Guid? ledgerBookId, Guid? fundAccountId, string? periodId,
            string? entityId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
                return EndpointHelpers.Forbidden();
            var service = context.RequestServices.GetService<IFinancialOperationsCommandCenterReadService>();
            if (service is null)
                return Results.Problem("Close readiness is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var payload = await service.GetCommandCenterAsync(fundProfileId, ledgerBookId, fundAccountId,
                periodId, entityId, context.RequestAborted, tenant.TenantId, tenant.CompanyId).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetFinancialOperationsCommandCenter")
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .RequireFundProfileTenantScope(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<FinancialOperationsCommandCenterDto>(200)
        .Produces(403)
        .Produces(503);
    }
}
