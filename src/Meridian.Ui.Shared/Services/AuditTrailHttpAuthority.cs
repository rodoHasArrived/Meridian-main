using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

/// <summary>One HTTP authority for audit browse and complete export; never admits foreign timeline reads.</summary>
internal static class AuditTrailHttpAuthority
{
    public static async Task<bool> CanReadExecutionFeedAsync(HttpContext context)
    {
        if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewTrades)) return false;
        if (context.RequestServices.GetService<IPortfolioState>() is not BrokeragePortfolioState brokerage) return true;
        return brokerage.FundAccountId is { } accountId &&
            await FundAccountEndpoints.CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false);
    }

    public static async Task<bool> CanReadWorkflowAsync(HttpContext context, OperationsContinuityWorkflowSummaryDto workflow, CancellationToken ct)
    {
        if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)) return false;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var tenancy = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId) || tenancy is null || workflow.FundAccountId == Guid.Empty) return false;
        string? fundId;
        if (workflow.LedgerBookId is { } bookId)
        {
            var books = context.RequestServices.GetService<ILedgerBookService>();
            var book = books is null ? null : await books.GetBookAsync(bookId, ct).ConfigureAwait(false);
            if (book?.LedgerBookId != bookId || book.FundStructureNodeKind != FundStructureNodeKindDto.Account || book.FundStructureNodeId != workflow.FundAccountId) return false;
            fundId = book.FundProfileId;
        }
        else
        {
            var accounts = context.RequestServices.GetService<IAccountQueryService>() ?? context.RequestServices.GetService<IFundAccountService>() as IAccountQueryService;
            var account = accounts is null ? null : await accounts.GetAccountAsync(workflow.FundAccountId, ct).ConfigureAwait(false);
            if (account?.AccountId != workflow.FundAccountId || account.FundId is not { } id || id == Guid.Empty) return false;
            fundId = id.ToString("D");
        }
        if (string.IsNullOrWhiteSpace(fundId)) return false;
        var owner = await tenancy.ResolveAsync(fundId, ct).ConfigureAwait(false);
        if (owner?.FundProfileId != fundId || owner.TenantId != tenant.TenantId || owner.CompanyId != tenant.CompanyId) return false;
        return EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance) || await EndpointAuthorization.HasScopedPermissionAsync(
            context, UserPermission.ViewTrades, AccessScopeKindDto.Account, workflow.FundAccountId, ct).ConfigureAwait(false);
    }
}
