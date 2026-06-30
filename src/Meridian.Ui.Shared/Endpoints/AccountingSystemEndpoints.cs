using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
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
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
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
            var resolvedTenantId = tenantContext.TenantId ?? tenantId;
            var resolvedCompanyId = tenantContext.CompanyId ?? companyId;
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
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
            };
            var trustedRequest = request with
            {
                Profile = trustedProfile,
                Actor = ResolveMutationActor(context, request.Actor)
            };
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
        .Produces(StatusCodes.Status404NotFound)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

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
                TenantId = tenantContext.TenantId ?? string.Empty,
                CompanyId = tenantContext.CompanyId ?? string.Empty
            };
            var trustedRequest = request with
            {
                Profile = trustedProfile,
                Actor = ResolveMutationActor(context, request.Actor)
            };
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

        group.MapPost(UiApiRoutes.AccountingSystemMigrationRuns, async (
            AccountingMigrationRunExecutionRequestDto request,
            HttpContext context,
            AccountingMigrationRunExecutionService service) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var trustedRequest = request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                };
                var result = await service.ExecuteAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
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
        .WithName("ExecuteAccountingMigrationRun")
        .Produces<AccountingMigrationRunExecutionResultDto>(StatusCodes.Status200OK)
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

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var artifacts = await store
                .ListAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
                .ConfigureAwait(false);
            var result = new AccountingMigrationRunArtifactListDto(
                fundProfileId,
                ledgerBookId,
                artifacts,
                tenantContext.TenantId,
                tenantContext.CompanyId);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListAccountingMigrationRunArtifacts")
        .Produces<AccountingMigrationRunArtifactListDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

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
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var trustedArtifact = request.Artifact with
                {
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                };
                var trustedRequest = request with
                {
                    Artifact = trustedArtifact,
                    Actor = ResolveMutationActor(context, request.Actor)
                };
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
        .WithName("UpsertAccountingMigrationRunArtifact")
        .Produces<AccountingMigrationRunArtifactDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.AccountingSystemMigrationWorkerPlans, async (
            string? fundProfileId,
            Guid? ledgerBookId,
            AccountingMigrationRunKindDto? kind,
            HttpContext context,
            IAccountingMigrationRunWorkerPlanStore store) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var plans = await store
                .ListAsync(fundProfileId, ledgerBookId, kind, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
                .ConfigureAwait(false);
            var result = new AccountingMigrationRunWorkerPlanListDto(
                fundProfileId,
                ledgerBookId,
                kind,
                plans,
                tenantContext.TenantId,
                tenantContext.CompanyId);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListAccountingMigrationWorkerPlans")
        .Produces<AccountingMigrationRunWorkerPlanListDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

        group.MapPost(UiApiRoutes.AccountingSystemMigrationWorkerPlans, async (
            AccountingMigrationRunWorkerPlanUpsertRequestDto request,
            HttpContext context,
            IAccountingMigrationRunWorkerPlanWriter store) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                EnsureHumanOrigin(request.ActionOrigin);
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                _ = ResolveMutationActor(context, request.Actor);
                var trustedPlan = request.Plan with
                {
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                };
                var result = await store.UpsertAsync(trustedPlan, context.RequestAborted).ConfigureAwait(false);
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
        .WithName("UpsertAccountingMigrationWorkerPlan")
        .Produces<AccountingMigrationRunWorkerPlanDto>(StatusCodes.Status200OK)
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

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var previewRequest = request with
            {
                PersistPreview = request.PersistPreview,
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
            };
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

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var result = await service
                .GetLatestImportAsync(
                    providerId,
                    fundProfileId,
                    ledgerBookId,
                    context.RequestAborted,
                    tenantContext.TenantId,
                    tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLatestAccountingSystemImport")
        .Produces<AccountingSystemImportDetailDto>(StatusCodes.Status200OK)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

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

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var result = await service
                .ReconcileLatestAsync(
                    providerId,
                    fundProfileId,
                    ledgerBookId,
                    context.RequestAborted,
                    tenantContext.TenantId,
                    tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetLatestAccountingSystemReconciliation")
        .Produces<AccountingSystemReconciliationSummaryDto>(StatusCodes.Status200OK)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

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

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var result = await service
                .ListMappingProfilesAsync(
                    providerId,
                    fundProfileId,
                    ledgerBookId,
                    context.RequestAborted,
                    tenantContext.TenantId,
                    tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListAccountingSystemMappingProfiles")
        .Produces<IReadOnlyList<ExternalGlMappingProfileDto>>(StatusCodes.Status200OK)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

        group.MapPost(UiApiRoutes.AccountingSystemMappingProfiles, async (
            AccountingSystemMappingProfileUpsertRequestDto request,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var trustedRequest = request with
            {
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
            };
            try
            {
                var result = await service.UpsertMappingProfileAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
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
                if (IsReviewedAutomationOriginError(ex))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["request"] = [ex.Message]
                    });
                }

                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("UpsertAccountingSystemMappingProfile")
        .Produces<ExternalGlMappingProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(UiApiRoutes.AccountingSystemExportPackages, async (
            string? providerId,
            string? fundProfileId,
            Guid? ledgerBookId,
            AccountingCertificationStateDto? certificationState,
            string? tenantId,
            string? companyId,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var result = await service
                .ListExportPackagesAsync(
                    providerId,
                    fundProfileId,
                    ledgerBookId,
                    certificationState,
                    tenantContext.TenantId ?? tenantId,
                    tenantContext.CompanyId ?? companyId,
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("ListAccountingSystemExportPackages")
        .Produces<IReadOnlyList<ExternalGlExportPackageDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireFundProfileTenantScope(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);

        group.MapPost(UiApiRoutes.AccountingSystemExportPackages, async (
            AccountingSystemExportPackageRequestDto request,
            HttpContext context,
            AccountingSystemIntegrationService service) =>
        {
            if (!HasAccountingCertificationAccess(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var trustedRequest = request with
            {
                TenantId = tenantContext.TenantId,
                CompanyId = tenantContext.CompanyId
            };
            try
            {
                var result = await service.CreateExportPackageAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
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
                if (IsReviewedAutomationOriginError(ex))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["request"] = [ex.Message]
                    });
                }

                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("CreateAccountingSystemExportPackage")
        .Produces<ExternalGlExportPackageDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
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
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var result = await service
                    .GetExportPackageManifestAsync(
                        exportPackageId,
                        tenantContext.TenantId,
                        tenantContext.CompanyId,
                        context.RequestAborted)
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
                var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                var trustedRequest = request with
                {
                    Actor = ResolveMutationActor(context, request.Actor),
                    TenantId = tenantContext.TenantId,
                    CompanyId = tenantContext.CompanyId
                };
                var result = await service.CertifyExportPackageAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
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

    private static string ResolveMutationActor(HttpContext context, string suppliedActor)
        => context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserKey, out var user) &&
           user is string currentUser &&
           !string.IsNullOrWhiteSpace(currentUser)
            ? currentUser.Trim()
            : suppliedActor;

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new ArgumentException("Accounting migration worker plan retention requires a human operator origin.", nameof(actionOrigin));
        }
    }

    private static bool IsReviewedAutomationOriginError(Exception ex)
        => ex.Message.StartsWith("Reviewed automation cannot ", StringComparison.OrdinalIgnoreCase);
}
