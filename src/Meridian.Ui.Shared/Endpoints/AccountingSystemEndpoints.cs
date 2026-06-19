using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
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

        group.MapGet(UiApiRoutes.AccountingSystemMappingProfiles, async (
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

            var result = await service.ListMappingProfilesAsync(providerId, fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListAccountingSystemMappingProfiles")
        .Produces<IReadOnlyList<ExternalGlMappingProfileDto>>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.AccountingSystemMappingProfiles, async (
            AccountingSystemMappingProfileUpsertRequestDto request,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.UpsertMappingProfileAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("UpsertAccountingSystemMappingProfile")
        .Produces<ExternalGlMappingProfileDto>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.AccountingSystemExportPackages, async (
            AccountingSystemExportPackageRequestDto request,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.CreateExportPackageAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("CreateAccountingSystemExportPackage")
        .Produces<ExternalGlExportPackageDto>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static bool HasAccountingAccess(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageFundStructure);
}
