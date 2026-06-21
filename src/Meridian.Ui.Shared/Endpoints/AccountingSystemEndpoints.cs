using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
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

        group.MapPost(UiApiRoutes.AccountingSystemProductionReadiness, async (
            AccountingProductionReadinessRequestDto request,
            HttpContext context,
            AccountingProductionReadinessService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var trustedRequest = request with
            {
                TenantId = string.IsNullOrWhiteSpace(request.TenantId) ? tenantContext.TenantId : request.TenantId,
                CompanyId = string.IsNullOrWhiteSpace(request.CompanyId) ? tenantContext.CompanyId : request.CompanyId
            };
            var result = await service.AssessAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("AssessAccountingProductionReadiness")
        .Produces<AccountingProductionReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet(UiApiRoutes.AccountingSystemTenantAdministrationProfile, async (
            string? tenantId,
            string? companyId,
            HttpContext context,
            IAccountingTenantAdministrationProfileStore store) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var resolvedTenantId = string.IsNullOrWhiteSpace(tenantId) ? tenantContext.TenantId : tenantId;
            var resolvedCompanyId = string.IsNullOrWhiteSpace(companyId) ? tenantContext.CompanyId : companyId;
            var result = await store.GetAsync(resolvedTenantId, resolvedCompanyId, context.RequestAborted).ConfigureAwait(false);
            return result is null
                ? Results.NotFound(new { error = "Accounting tenant administration profile was not found." })
                : Results.Json(result, jsonOptions);
        })
        .WithName("GetAccountingTenantAdministrationProfile")
        .Produces<AccountingTenantAdministrationProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.AccountingSystemTenantAdministrationProfile, async (
            AccountingTenantAdministrationProfileUpsertRequestDto request,
            HttpContext context,
            IAccountingTenantAdministrationProfileStore store) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var trustedProfile = request.Profile with
            {
                TenantId = string.IsNullOrWhiteSpace(request.Profile.TenantId) ? tenantContext.TenantId ?? string.Empty : request.Profile.TenantId,
                CompanyId = string.IsNullOrWhiteSpace(request.Profile.CompanyId) ? tenantContext.CompanyId ?? string.Empty : request.Profile.CompanyId
            };
            var trustedRequest = request with { Profile = trustedProfile };
            try
            {
                var result = await store.UpsertAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("UpsertAccountingTenantAdministrationProfile")
        .Produces<AccountingTenantAdministrationProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.AccountingSystemProductionCertificationProfile, async (
            string? tenantId,
            string? companyId,
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context,
            IAccountingProductionCertificationProfileStore store) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var resolvedTenantId = tenantContext.TenantId ?? tenantId;
            var resolvedCompanyId = tenantContext.CompanyId ?? companyId;
            var result = await store.GetAsync(resolvedTenantId, resolvedCompanyId, fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return result is null
                ? Results.NotFound(new { error = "Accounting production certification profile was not found." })
                : Results.Json(result, jsonOptions);
        })
        .WithName("GetAccountingProductionCertificationProfile")
        .Produces<AccountingProductionCertificationProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.AccountingSystemProductionCertificationProfile, async (
            AccountingProductionCertificationProfileUpsertRequestDto request,
            HttpContext context,
            IAccountingProductionCertificationProfileStore store) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var trustedProfile = request.Profile with
            {
                TenantId = tenantContext.TenantId ?? request.Profile.TenantId ?? string.Empty,
                CompanyId = tenantContext.CompanyId ?? request.Profile.CompanyId ?? string.Empty
            };
            var trustedRequest = request with { Profile = trustedProfile };
            try
            {
                var result = await store.UpsertAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("UpsertAccountingProductionCertificationProfile")
        .Produces<AccountingProductionCertificationProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.AccountingSystemMigrationRunArtifacts, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            HttpContext context,
            IAccountingMigrationRunArtifactStore store) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var artifacts = await store.ListAsync(fundProfileId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            var result = new AccountingMigrationRunArtifactListDto(fundProfileId, ledgerBookId, artifacts);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListAccountingMigrationRunArtifacts")
        .Produces<AccountingMigrationRunArtifactListDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost(UiApiRoutes.AccountingSystemMigrationRunArtifacts, async (
            AccountingMigrationRunArtifactUpsertRequestDto request,
            HttpContext context,
            IAccountingMigrationRunArtifactStore store) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await store.UpsertAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("UpsertAccountingMigrationRunArtifact")
        .Produces<AccountingMigrationRunArtifactDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

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

        group.MapGet(UiApiRoutes.AccountingSystemExportPackageManifest, async (
            string exportPackageId,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await service
                    .GetExportPackageManifestAsync(exportPackageId, context.RequestAborted)
                    .ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = "External GL export package manifest was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
        })
        .WithName("GetAccountingSystemExportPackageManifest")
        .Produces<ExternalGlExportPackageManifestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.AccountingSystemExportPackageCertification, async (
            CertifyAccountingSystemExportPackageRequestDto request,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await service.CertifyExportPackageAsync(request, context.RequestAborted).ConfigureAwait(false);
                return result is null
                    ? Results.NotFound(new { error = "External GL export package was not found." })
                    : Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("CertifyAccountingSystemExportPackage")
        .Produces<ExternalGlExportPackageDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static bool HasAccountingAccess(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageFundStructure);

    private static bool HasAccountingCertificationAccess(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);
}
