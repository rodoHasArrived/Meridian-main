using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapApprovalCollaborationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalCollaboration), async (Guid workflowId, HttpContext context) => {
            var authority = await ResolveApprovalCollaborationAuthorityAsync(workflowId, context).ConfigureAwait(false);
            if (authority is null) return EndpointHelpers.Forbidden();
            var discussion = await authority.Value.Service.GetApprovalCollaborationAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            if (discussion is null) return Results.NotFound();
            var canWrite = CanWriteApprovalCollaboration(context);
            return Results.Json(discussion with {
                CanComment = discussion.CanComment && canWrite, CanReassign = discussion.CanReassign && canWrite,
                DisabledReason = canWrite ? discussion.DisabledReason : "Approval workflow mutation authority is required."
            }, jsonOptions);
        }).WithName("GetOperationsApprovalCollaboration").RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalComments), async (
            Guid workflowId, OperationsApprovalCommentRequestDto request, HttpContext context) => {
            if (!CanWriteApprovalCollaboration(context)) return EndpointHelpers.Forbidden();
            var authority = await ResolveApprovalCollaborationAuthorityAsync(workflowId, context).ConfigureAwait(false);
            if (authority is null) return EndpointHelpers.Forbidden();
            foreach (var mention in request.Mentions ?? [])
                if (!await IsCompanyApprovalOperatorAsync(context, mention, authority.Value.CompanyId, requireReviewer: false).ConfigureAwait(false))
                    return Results.BadRequest(new { error = "Mentioned operators must be active and belong to the authenticated company." });
            var result = await authority.Value.Service.AddApprovalCommentAsync(workflowId,
                request with { Actor = authority.Value.Actor }, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        }).WithName("AddOperationsApprovalComment").RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalReviewer), async (
            Guid workflowId, OperationsApprovalReassignRequestDto request, HttpContext context) => {
            if (!CanWriteApprovalCollaboration(context)) return EndpointHelpers.Forbidden();
            var authority = await ResolveApprovalCollaborationAuthorityAsync(workflowId, context).ConfigureAwait(false);
            if (authority is null) return EndpointHelpers.Forbidden();
            if (!await IsCompanyApprovalOperatorAsync(context, request.Reviewer, authority.Value.CompanyId, requireReviewer: true).ConfigureAwait(false))
                return Results.BadRequest(new { error = "Select an approval-capable reviewer in the authenticated company." });
            var result = await authority.Value.Service.ReassignApprovalAsync(workflowId,
                request with { Actor = authority.Value.Actor }, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        }).WithName("ReassignOperationsApprovalReviewer").RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static bool CanWriteApprovalCollaboration(HttpContext context) => EndpointAuthorization.HasAnyPermission(context,
        UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

    private static async Task<bool> IsCompanyApprovalOperatorAsync(HttpContext context, string? username, string companyId, bool requireReviewer)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var profile = context.RequestServices.GetService<UserProfileRegistry>()?.GetProfile(username.Trim());
        if (profile is null || !string.Equals(profile.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)) return false;
        if (context.RequestServices.GetService<IUserAccountStore>() is { HasAccounts: true } accounts)
        {
            var matches = (await accounts.GetAccountsAsync(context.RequestAborted).ConfigureAwait(false))
                .Where(account => string.Equals(account.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].IsDisabled || !string.Equals(matches[0].CompanyId, companyId, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return !requireReviewer || (profile.Permissions & (UserPermission.ManageDirectLending | UserPermission.ModifySecurityMaster | UserPermission.AdminMaintenance)) != 0;
    }

    private static async Task<(IOperationsContinuityWorkflowService Service, string Actor, string CompanyId)?> ResolveApprovalCollaborationAuthorityAsync(
        Guid workflowId, HttpContext context)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
        var books = context.RequestServices.GetService<ILedgerBookService>();
        var ownership = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId)
            || string.IsNullOrWhiteSpace(tenant.Actor) || service is null || books is null || ownership is null) return null;
        var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
        if (workflow?.WorkflowId != workflowId || workflow.FundAccountId == Guid.Empty || workflow.LedgerBookId is not { } bookId) return null;
        var book = await books.GetBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
        if (book is null || book.LedgerBookId != bookId || book.FundStructureNodeKind != FundStructureNodeKindDto.Account
            || book.FundStructureNodeId != workflow.FundAccountId) return null;
        var owner = await ownership.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        if (owner?.IsHeldBy(tenant.TenantId) != true || !string.Equals(owner.CompanyId, tenant.CompanyId, StringComparison.Ordinal)) return null;
        return (service, tenant.Actor, tenant.CompanyId);
    }
}
