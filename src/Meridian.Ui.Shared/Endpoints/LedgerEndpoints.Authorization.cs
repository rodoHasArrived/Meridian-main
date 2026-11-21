using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static bool HasAccountingPackageTenantScope(WorkstationTenantContext tenantContext)
        => !string.IsNullOrWhiteSpace(tenantContext.TenantId) &&
           !string.IsNullOrWhiteSpace(tenantContext.CompanyId);

    /// <summary>Read authority over the governed ledger. The ledger-report permissions are what this surface means; ManageDirectLending stays accepted so deployments that used it as the fund-accounting grant keep working.</summary>
    private static bool HasLedgerReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports);

    private static async Task<CloseWorkflowTenantScope> ResolveCloseWorkflowTenantScopeAsync(
        HttpContext context,
        IAccountingCloseManagementService service,
        Guid workflowId)
    {
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var plan = await service
            .GetPeriodPlanScopedAsync(workflowId, tenant.TenantId, tenant.CompanyId, context.RequestAborted)
            .ConfigureAwait(false);
        if (!tenant.HasTenantScope ||
            string.IsNullOrWhiteSpace(tenant.TenantId) ||
            string.IsNullOrWhiteSpace(tenant.CompanyId))
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        if (plan is null)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: true);
        }

        if (plan.LedgerBookId is not { } ledgerBookId || ledgerBookId == Guid.Empty)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        var ledgerBookService = context.RequestServices.GetService<ILedgerBookService>();
        if (ledgerBookService is null)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
        if (registry is null)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }

        try
        {
            var book = await ledgerBookService.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            if (book is null ||
                !Guid.TryParse(plan.FundProfileId, out var fundAccountId) ||
                fundAccountId == Guid.Empty ||
                book.FundStructureNodeId != fundAccountId ||
                !string.Equals(book.BaseCurrency, plan.MaterialityPolicy.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
            }

            var periods = await ledgerBookService
                .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: ledgerBookId), context.RequestAborted)
                .ConfigureAwait(false);
            var hasPeriodId = Guid.TryParse(plan.PeriodId, out var requestedPeriodId);
            if (!periods.Any(period =>
                    (hasPeriodId && period.PeriodId == requestedPeriodId) ||
                    string.Equals(period.Label, plan.PeriodId, StringComparison.OrdinalIgnoreCase)))
            {
                return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
            }

            // FundProfileId on the close plan identifies the operations fund account. The
            // authoritative tenant owner is the ledger book's fund profile, after proving that the
            // book belongs to that exact fund-account node.
            var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
            if (owner is null || !CloseWorkflowOwnerMatches(owner, tenant))
            {
                return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
            }

            if (guard is not null)
            {
                var decision = await guard
                    .EvaluateAsync(tenant, book.FundProfileId, context.RequestAborted)
                    .ConfigureAwait(false);
                if (!decision.IsAllowed)
                {
                    return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
                }
            }

            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: true);
        }
        catch (LedgerBookServiceException)
        {
            return new CloseWorkflowTenantScope(plan, tenant, IsAccessible: false);
        }
    }

    private static bool CloseWorkflowOwnerMatches(
        FundProfileOwnership owner,
        WorkstationTenantContext tenant)
        => owner.IsHeldBy(tenant.TenantId) &&
           !string.IsNullOrWhiteSpace(owner.CompanyId) &&
           !string.IsNullOrWhiteSpace(tenant.CompanyId) &&
           string.Equals(owner.CompanyId.Trim(), tenant.CompanyId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IResult CloseWorkflowScopeDenied()
        => Results.Problem(
            "The requested close workflow is not accessible to the current tenant and company.",
            statusCode: StatusCodes.Status403Forbidden);

    private sealed record CloseWorkflowTenantScope(
        ClosePeriodPlanDto? Plan,
        WorkstationTenantContext TenantContext,
        bool IsAccessible);

    /// <summary>
    /// Tenant isolation (SEC-005 slice 3) for body-supplied fund scopes on POST read/preview routes the
    /// query-string <see cref="FundProfileScopeEndpointFilters"/> filter cannot see. Returns true (allow)
    /// for a blank fund or an unavailable guard (fail open); denies only a fund the registry positively
    /// attributes to another tenant. Call it after the route's permission check so an unauthorized caller
    /// never receives an ownership verdict.
    /// </summary>
    private static async Task<bool> IsBodyFundScopeAccessibleAsync(
        HttpContext context,
        WorkstationTenantContext tenant,
        string? fundProfileId)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return true;
        }

        var guard = context.RequestServices.GetService<IFundProfileTenantGuard>();
        if (guard is null)
        {
            return true;
        }

        var decision = await guard.EvaluateAsync(tenant, fundProfileId, context.RequestAborted).ConfigureAwait(false);
        return decision.IsAllowed;
    }

    /// <summary>Write authority over the governed ledger. Deliberately excludes ViewLedgerReports: reading the trial balance must never confer the authority to post to it.</summary>
    private static bool HasLedgerMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports);

    private static bool TryResolveControllerRole(HttpContext context, out string role)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) &&
            rawRole is UserRole userRole &&
            userRole == UserRole.Controller)
        {
            role = "Controller";
            return true;
        }

        var profile = HttpContextWorkstationTenantContextAccessor.Resolve(context).RoleProfileName;
        if (string.Equals(profile, "Controller", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile, "Fund Controller", StringComparison.OrdinalIgnoreCase))
        {
            role = profile!.Trim();
            return true;
        }

        role = string.Empty;
        return false;
    }

    private static bool HasLedgerCertificationPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);

    private static bool CanConfigureCloseTaskApprovalRoles(
        HttpContext context,
        IReadOnlyList<CloseTaskConfigurationDto> taskConfigurations)
        => taskConfigurations.All(configuration =>
            string.IsNullOrWhiteSpace(configuration.RequiredApprovalRole) ||
            HasCloseTaskSignOffRoleAuthority(context, configuration.RequiredApprovalRole));

    private static bool HasCloseTaskSignOffRoleAuthority(HttpContext context, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        if (HasLedgerCertificationPermission(context))
        {
            return true;
        }

        var normalizedRole = role.Trim();
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var rawRole) &&
            rawRole is UserRole currentRole &&
            string.Equals(currentRole.ToString(), normalizedRole, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleProfileNameKey, out var rawProfile) &&
            rawProfile is string profileName &&
            string.Equals(profileName.Trim(), normalizedRole, StringComparison.OrdinalIgnoreCase);
    }

    private static JournalEntryLifecycleActionDto? ResolveAuthorizedManualJournalLifecycleAction(
        HttpContext context,
        JournalEntryLifecycleActionDto requestedAction)
        => requestedAction switch
        {
            JournalEntryLifecycleActionDto.Validate when HasLedgerReadPermission(context) => JournalEntryLifecycleActionDto.Validate,
            JournalEntryLifecycleActionDto.Submit when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.Submit,
            JournalEntryLifecycleActionDto.Approve when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.Approve,
            JournalEntryLifecycleActionDto.Reject when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.Reject,
            JournalEntryLifecycleActionDto.Post when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.Post,
            JournalEntryLifecycleActionDto.Reverse when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.Reverse,
            JournalEntryLifecycleActionDto.Rebook when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.Rebook,
            JournalEntryLifecycleActionDto.LockAfterClose when HasLedgerCertificationPermission(context) => JournalEntryLifecycleActionDto.LockAfterClose,
            _ => null
        };

    private static bool HasLedgerClosePermission(HttpContext context)
        => HasLedgerMutationPermission(context);

    private static bool TryResolveActor(HttpContext context, out string actor)
        => EndpointAuthorization.TryResolveActor(context, out actor);

    private static string ResolveMutationActor(HttpContext context, string suppliedActor)
        => EndpointAuthorization.TryResolveActor(context, out var actor) && !string.IsNullOrWhiteSpace(actor)
            ? actor
            : suppliedActor;
}
