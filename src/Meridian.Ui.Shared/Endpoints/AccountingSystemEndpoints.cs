using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class AccountingSystemEndpoints
{
    public static void MapAccountingSystemEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Accounting System Providers");

        group.MapGet(UiApiRoutes.AccountingSystemProviders, async (
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var providers = await service.ListProvidersAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(providers, jsonOptions);
        })
        .WithName("ListAccountingSystemProviders")
        .Produces<IReadOnlyList<AccountingSystemProviderDto>>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.AccountingSystemImportPreview, async (
            AccountingSystemImportRequestDto request,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var previewRequest = request with { PersistPreview = request.PersistPreview };
            var result = await service.ImportAsync(previewRequest, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("PreviewAccountingSystemImport")
        .Produces<AccountingSystemImportDetailDto>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.AccountingSystemImportLatest, async (
            string? providerId,
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.GetLatestImportAsync(providerId, fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLatestAccountingSystemImport")
        .Produces<AccountingSystemImportDetailDto>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.AccountingSystemReconciliationLatest, async (
            string? providerId,
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.ReconcileLatestAsync(providerId, fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLatestAccountingSystemReconciliation")
        .Produces<AccountingSystemReconciliationSummaryDto>(StatusCodes.Status200OK);
    }

    private static bool HasAccountingAccess(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageFundStructure);
}
