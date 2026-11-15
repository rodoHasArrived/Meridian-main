using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapAutomatedPostingGrantEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerAutomationGrant, async (Guid grantId, HttpContext context) =>
        {
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId) || tenant.TenantId.Equals("all", StringComparison.OrdinalIgnoreCase))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IAutomatedPostingGrantStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            {
                var grant = await store.GetAsync(grantId, tenant.TenantId, context.RequestAborted).ConfigureAwait(false);
                return grant is null || grant.Reference.CompanyId != tenant.CompanyId
                    ? Results.NotFound() : Results.Json(grant, jsonOptions);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        }).WithName("GetAutomatedPostingGrant")
            .RequirePermission(UserPermission.AdminMaintenance)
            .Produces<AutomatedPostingGrantDto>().Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerAutomationGrants, async (ActivateAutomatedPostingGrantRequest request, HttpContext context) =>
        {
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!TryResolveActor(context, out var actor) || string.IsNullOrWhiteSpace(actor)
                || string.IsNullOrWhiteSpace(tenant.TenantId) || tenant.TenantId.Equals("all", StringComparison.OrdinalIgnoreCase)
                || !OperationsOriginGuard.IsHumanOperator(EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IAutomatedPostingGrantStore>();
            var policies = context.RequestServices.GetService<IAccountingPolicyService>();
            var journals = context.RequestServices.GetService<ILedgerJournalStore>();
            if (store is null || policies is null || journals is null)
                return ServiceUnavailable();
            try
            {
                var period = await journals.GetPeriodAsync(request.PeriodId, context.RequestAborted).ConfigureAwait(false);
                if (period is null || period.LedgerBookId != request.LedgerBookId)
                    return Results.BadRequest(new { error = "The grant requires its retained book and period." });
                var book = await journals.GetLedgerBookAsync(request.LedgerBookId, context.RequestAborted).ConfigureAwait(false);
                if (book is null)
                    return Results.NotFound();
                await using var policyAuthority = await policies.AcquireConsolidationAuthorityLeaseAsync(context.RequestAborted)
                    .ConfigureAwait(false);
                var policy = await policies.ResolvePolicyAsync(new AccountingPolicyQuery(book.AccountingBasis,
                    period.StartDate, request.PolicyId, book.FundProfileId, book.FundStructureNodeId,
                    PolicyVersion: request.PolicyVersion), context.RequestAborted).ConfigureAwait(false);
                if (policy.PolicyId != request.PolicyId || policy.Version != request.PolicyVersion
                    || policy.EffectiveFrom > period.StartDate || policy.EffectiveTo is { } end && end < period.EndDate)
                    return Results.BadRequest(new { error = "The grant requires an exact policy effective for its entire period." });
                foreach (var source in new[] { "execution.trade-fill", "execution.trade-commission", "execution.trade-fee" })
                {
                    var rules = policy.RulePack?.Rules.Where(rule => rule.SourceEventType == source).ToArray() ?? [];
                    if (rules.Length != 1 || !rules[0].AllowsAutoPosting || rules[0].RequiresApproval || !rules[0].RequiresEvidence)
                        return Results.BadRequest(new { error = $"Policy must explicitly allow evidence-backed automatic posting for '{source}'." });
                }
                // Tenant, company and actor come from the authenticated session, never the body.
                var result = await store.ActivateWithPolicyAsync(request with { CompanyId = tenant.CompanyId }, policy, actor,
                    tenant.TenantId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (LedgerValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).WithName("ActivateAutomatedPostingGrant")
            .RequirePermission(UserPermission.AdminMaintenance).RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<AutomatedPostingGrantDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerAutomationGrantRevoke, async (
            Guid grantId, RevokeAutomatedPostingGrantRequest request, HttpContext context) =>
        {
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!TryResolveActor(context, out var actor) || string.IsNullOrWhiteSpace(actor)
                || string.IsNullOrWhiteSpace(tenant.TenantId) || tenant.TenantId.Equals("all", StringComparison.OrdinalIgnoreCase)
                || !OperationsOriginGuard.IsHumanOperator(EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IAutomatedPostingGrantStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            {
                var current = await store.GetAsync(grantId, tenant.TenantId, context.RequestAborted).ConfigureAwait(false);
                if (current is null || current.Reference.CompanyId != tenant.CompanyId)
                    return Results.NotFound();
                var result = await store.RevokeAsync(grantId, request.ExpectedVersion, actor, tenant.TenantId,
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (LedgerValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).WithName("RevokeAutomatedPostingGrant")
            .RequirePermission(UserPermission.AdminMaintenance).RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<AutomatedPostingGrantDto>().Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status501NotImplemented);
    }
}
