using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapStatementImportReviewEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementImportReviewRows), async (
            string reviewId, int? offset, int? limit, HttpContext context, [FromServices] StatementImportService? service)
            => await ReadStatementImportReviewPageAsync(reviewId, offset ?? 0, limit ?? 100, false, context, service, jsonOptions).ConfigureAwait(false))
            .WithName("GetStatementImportReviewRows")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
            .Produces<StatementImportReviewRowsPageDto>(200).Produces(403).Produces(404).ProducesValidationProblem()
            .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementImportReviewOutcomes), async (
            string reviewId, int? offset, int? limit, HttpContext context, [FromServices] StatementImportService? service)
            => await ReadStatementImportReviewPageAsync(reviewId, offset ?? 0, limit ?? 100, true, context, service, jsonOptions).ConfigureAwait(false))
            .WithName("GetStatementImportReviewOutcomes")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
            .Produces<StatementImportRowOutcomesPageDto>(200).Produces(403).Produces(404).ProducesValidationProblem()
            .RequireWorkstationTenantCompanyScope();
    }

    private static async Task<IResult> ReadStatementImportReviewPageAsync(
        string reviewId, int offset, int limit, bool outcomes, HttpContext context,
        StatementImportService? service, JsonSerializerOptions jsonOptions)
    {
        if (!HasReconciliationMutationPermission(context))
            return EndpointHelpers.Forbidden();
        if (service is null)
            return StatementConnectorsNotRegistered();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId))
            return EndpointHelpers.Forbidden();
        try
        {
            var review = await service.GetReviewAsync(reviewId, tenant.TenantId, tenant.CompanyId, context.RequestAborted).ConfigureAwait(false);
            if (review is null)
                return Results.NotFound();
            var scope = new StatementReconciliationReportScope(review.SourceKind, review.SourceInstitution, review.FundAccountId,
                review.ExternalAccountId, review.PeriodStart, review.PeriodEnd, null, review.AccountingScope);
            var ownershipProblem = await RequireStatementReconciliationReportAccountOwnershipAsync(scope, tenant, context).ConfigureAwait(false);
            if (ownershipProblem is not null)
                return ownershipProblem;
            if (!outcomes)
                return Results.Json(StatementImportService.ReviewRows(review, offset, limit), jsonOptions);
            var page = await service.GetReviewOutcomesAsync(review, offset, limit, context.RequestAborted).ConfigureAwait(false);
            return page is null ? Results.NotFound() : Results.Json(page, jsonOptions);
        }
        catch (InvalidDataException ex)
        {
            return MissingDataUploadPayload("review", ex.Message);
        }
    }
}
