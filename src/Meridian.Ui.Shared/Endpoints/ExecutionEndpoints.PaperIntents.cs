using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
    private static async Task<IResult> SubmitPaperIntentAsync(
        OrderRequest request, Guid intentId, string actor, IOrderManager oms, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!scope.HasTenantScope || !TenantReadPredicate.IsResolvedTenant(scope.CompanyId) ||
            request.FundAccountId is not { } accountId || accountId == Guid.Empty)
            return ExecutionFundAccountForbidden(jsonOptions);

        if (await RequireExecutionFundAccountAccessAsync(accountId, UserPermission.ManageOrders, context, jsonOptions)
                .ConfigureAwait(false) is { } denied)
            return denied;

        if (oms is not IOrderIntentSubmitter submitter)
            return Results.Problem("Scoped paper order intents are unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var account = await ResolveAccountQueryService(context)!.GetAccountAsync(accountId, context.RequestAborted)
            .ConfigureAwait(false);
        if (account is not { IsActive: true, AccountType: AccountTypeDto.Brokerage })
            return ExecutionFundAccountForbidden(jsonOptions);

        var identities = context.RequestServices.GetService<ISecurityIdentityQueryService>() ??
            context.RequestServices.GetService<ISecurityMasterQueryService>();
        var security = identities is null || string.IsNullOrWhiteSpace(request.Symbol) ? null :
            await identities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, request.Symbol.Trim(), null,
                context.RequestAborted).ConfigureAwait(false);
        if (security is not { Status: SecurityStatusDto.Active } ||
            !string.Equals(security.AssetClass, "equity", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new OrderResult
            {
                Success = false,
                OrderId = "blocked",
                IntentId = intentId,
                IntentStatus = OrderIntentStatus.Blocked,
                ErrorMessage = "The scoped paper ticket requires an active equity in Security Master."
            }, jsonOptions, statusCode: StatusCodes.Status400BadRequest);

        var result = await submitter.SubmitIntentAsync(
                new OrderIntent(intentId, scope.TenantId!, scope.CompanyId!, accountId, actor, request),
                context.RequestAborted)
            .ConfigureAwait(false);
        var status = result.IntentStatus switch
        {
            OrderIntentStatus.Conflict => StatusCodes.Status409Conflict,
            OrderIntentStatus.ReconciliationRequired => StatusCodes.Status202Accepted,
            _ when result.RequiresApproval => StatusCodes.Status202Accepted,
            _ when result.Success => StatusCodes.Status201Created,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(result, jsonOptions, statusCode: status);
    }

    internal static async Task<bool> CanAccessScopedPaperBookAsync(
        HttpContext context, PaperOrderIntentScope paper, UserPermission permission)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return scope.HasTenantScope && paper.CanAccess(scope.TenantId, scope.CompanyId, paper.FundAccountId) &&
            await EndpointAuthorization.HasScopedPermissionAsync(context, permission, AccessScopeKindDto.Account,
                paper.FundAccountId, context.RequestAborted).ConfigureAwait(false);
    }
}
