using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Monitoring;
using Meridian.Reporting;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Instruments.AssetOperations;
using Meridian.QuantScript.Compilation;
using Meridian.Storage.Export;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Query;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for the desktop workstation API surface.
/// </summary>
public static partial class WorkstationEndpoints
{
    private const int MaxRunComparisonRequestIds = 10;
    private const int SecurityCoveragePreviewLimit = 5;
    private const int MaxOperatorInboxTokenLength = 256;
    private const string WorkstationStructuredXlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string WorkstationApiRoutePrefix = "/api/workstation";
    private const string PortfolioApiRoutePrefix = "/api/portfolio";
    public static void MapWorkstationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/workstation")
            .WithTags("Workstation")
            .RequireWorkstationTenantScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSession), async (HttpContext context) =>
        {
            return await BuildSessionPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationSession").RequireAuthenticatedSessionOrScopedLocalOperatorRead();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationResearch), async (HttpContext context) =>
        {
            var payload = await BuildStrategyPayloadAsync(context).ConfigureAwait(false);
            return payload is null
                ? StrategyReadServiceUnavailable()
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationResearch").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<WorkstationStrategyPayload>(200)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategy), async (HttpContext context) =>
        {
            var payload = await BuildStrategyPayloadAsync(context).ConfigureAwait(false);
            return payload is null
                ? StrategyReadServiceUnavailable()
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationStrategy").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<WorkstationStrategyPayload>(200)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationResearchBriefing), async (HttpContext context) =>
        {
            var briefing = await BuildStrategyBriefingAsync(context).ConfigureAwait(false);
            return briefing is null
                ? StrategyReadServiceUnavailable()
                : Results.Json(ToResearchBriefingDto(briefing), jsonOptions);
        })
        .WithName("GetWorkstationResearchBriefing").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<ResearchBriefingDto>(200)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyBriefing), async (HttpContext context) =>
        {
            var briefing = await BuildStrategyBriefingAsync(context).ConfigureAwait(false);
            return briefing is null
                ? StrategyReadServiceUnavailable()
                : Results.Json(briefing, jsonOptions);
        })
        .WithName("GetWorkstationStrategyBriefing").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyBriefingDto>(200)
        .Produces(503);

        // Governed Security Master write surface lives under /api/security-master (not /api/workstation),
        // so it maps its own tenant-scoped group directly on the app.
        MapSecurityMasterWorkbenchEndpoints(app, jsonOptions);

        MapStreamEndpoints(group, jsonOptions);
        MapStrategyDesignerEndpoints(group, jsonOptions);
        MapStrategyEngineEndpoints(group, jsonOptions);
        MapFeatureCapabilityEndpoints(group, jsonOptions);
        MapExtensibilityEndpoints(group, jsonOptions);
        MapFinancialRecordExplorerEndpoints(group, jsonOptions);
        MapFamilyOfficeEndpoints(group);
        MapDataUploadEndpoints(group, jsonOptions);
        MapDataOperationsAssuranceEndpoints(group);
        MapStatementConnectorEndpoints(group, jsonOptions);
        MapProviderIntegrationEndpoints(group, jsonOptions);
        MapIBResultEndpoints(group, jsonOptions);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowSummary), async (
            bool? hasOperatingContext,
            string? operatingContext,
            string? fundProfileId,
            string? fundAccountId,
            string? fundDisplayName,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkstationWorkflowSummaryService>();
            if (service is null)
            {
                return Results.Problem("Workflow summary service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await service
                .GetAsync(
                    hasOperatingContext: hasOperatingContext ?? false,
                    operatingContextDisplayName: operatingContext,
                    fundProfileId: fundProfileId,
                    fundAccountId: fundAccountId,
                    fundDisplayName: fundDisplayName,
                    ct: context.RequestAborted, readScope: WorkstationWorkflowReadScope.ForRequest(context))
                .ConfigureAwait(false);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetWorkstationWorkflowSummary").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage, UserPermission.AdminMaintenance)
        .Produces<OperatorWorkflowHomeSummary>(200)
        .Produces(403)
        .Produces(501)
        // Admission is the union of the projection's family sets, plus the reporting reads whose card is
        // ungated furniture. The ownership gate takes that same set (SEC-005 slice 3b).
        .RequireFundProfileTenantScope(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowLibrary), (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowLibraryService>();
            if (service is null)
            {
                var fallback = new WorkflowLibraryService(WorkflowRegistry.CreateDefault());
                return Results.Json(fallback.GetLibrary(), jsonOptions);
            }

            return Results.Json(service.GetLibrary(), jsonOptions);
        })
        .WithName("GetWorkstationWorkflowLibrary").DeclareOpenRead("Static workflow catalog from WorkflowRegistry; carries no deployment, account or tenant state.")
        .Produces<WorkflowLibraryDto>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationCollateralIngest), (
            IReadOnlyList<CollateralInputRow> rows,
            HttpContext context) =>
        {
            if (!HasOperationsContinuityMutationPermission(context))
            {
                return Results.Forbid();
            }

            const int maxRowsPerRequest = 1_000;
            if (rows.Count > maxRowsPerRequest)
            {
                return Results.BadRequest(new { error = $"A maximum of {maxRowsPerRequest} collateral rows can be ingested per request." });
            }

            var buffer = context.RequestServices.GetService<CollateralIngestionBuffer>();
            if (buffer is null)
            {
                return Results.Accepted(value: new { ingested = 0, buffered = false });
            }

            // One call, not a loop: a delivery replaces the exposures it restates, so ingesting row by
            // row would make the batch overwrite itself and report only its last row per exposure.
            buffer.IngestBatch(rows);

            return Results.Accepted(value: new { ingested = rows.Count, buffered = true });
        })
        .WithName("IngestCollateralRows").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces(202)
        .Produces(400)
        .Produces(403)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowPresets), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var library = await service.GetLibraryAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(library, jsonOptions);
        })
        .WithName("GetWorkstationWorkflowPresets").RequireAuthenticatedSessionOrScopedLocalOperatorRead()
        .Produces<WorkflowPresetLibraryDto>(200)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowPresets), async (WorkflowPresetSaveRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            WorkflowPresetMutationResult result;
            try
            {
                result = await service.SaveAsync(request, context.RequestAborted).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return result.Success
                ? Results.Json(result.Preset, jsonOptions)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("SaveWorkstationWorkflowPreset").RequireAuthenticatedSession()
        .Produces<WorkflowPresetDto>(200)
        .Produces(400)
        .Produces(501);

        group.MapPut(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowPresetById), async (
            string presetId,
            WorkflowPresetSaveRequest request,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            WorkflowPresetMutationResult result;
            try
            {
                result = await service
                    .SaveAsync(request with { PresetId = presetId }, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return result.Success
                ? Results.Json(result.Preset, jsonOptions)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("UpdateWorkstationWorkflowPreset").RequireAuthenticatedSession()
        .Produces<WorkflowPresetDto>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowPresetPin), async (
            string presetId,
            WorkflowPresetPinRequest request,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await service.SetPinnedAsync(presetId, request.IsPinned, context.RequestAborted).ConfigureAwait(false);
            if (result.NotFound)
            {
                return Results.NotFound(new { error = result.Error });
            }

            return result.Success
                ? Results.Json(result.Preset, jsonOptions)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("PinWorkstationWorkflowPreset").RequireAuthenticatedSession()
        .Produces<WorkflowPresetDto>(200)
        .Produces(400)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowPresetUsed), async (string presetId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await service.MarkUsedAsync(presetId, context.RequestAborted).ConfigureAwait(false);
            if (result.NotFound)
            {
                return Results.NotFound(new { error = result.Error });
            }

            return result.Success
                ? Results.Json(result.Preset, jsonOptions)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("MarkWorkstationWorkflowPresetUsed").RequireAuthenticatedSession()
        .Produces<WorkflowPresetDto>(200)
        .Produces(400)
        .Produces(404)
        .Produces(501);

        group.MapDelete(WorkstationSubroute(UiApiRoutes.WorkstationWorkflowPresetById), async (string presetId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var deleted = await service.DeleteAsync(presetId, context.RequestAborted).ConfigureAwait(false);
            return deleted
                ? Results.NoContent()
                : Results.NotFound(new { error = $"Workflow preset '{presetId}' was not found." });
        })
        .WithName("DeleteWorkstationWorkflowPreset").RequireAuthenticatedSession()
        .Produces(204)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationTrading), async (Guid? fundAccountId, HttpContext context) =>
        {
            var payload = await BuildTradingPayloadAsync(context, fundAccountId).ConfigureAwait(false);
            return payload is null
                ? WorkstationServiceUnavailable(
                    "Trading data is unavailable: no execution portfolio state, order manager, or strategy run read service is registered.")
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationTrading").RequirePermission(UserPermission.ViewTrades)
        .Produces<WorkstationTradingPayload>(200)
        .Produces(403)
        .Produces(503)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationTradingReadiness), async (Guid? fundAccountId, HttpContext context) =>
        {
            var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);
            return Results.Json(readiness, jsonOptions);
        })
        .WithName("GetWorkstationTradingReadiness").RequirePermission(UserPermission.ViewTrades)
        .Produces<TradingOperatorReadinessDto>(200)
        .Produces(403)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationCollateralExposure), (HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return Results.Forbid();
            }

            var service = context.RequestServices.GetRequiredService<CollateralExposureService>();
            var buffer = context.RequestServices.GetService<CollateralIngestionBuffer>();
            var rows = buffer?.SnapshotRows(5_000) ?? [];
            return Results.Json(BuildCollateralExposureSnapshot(service, rows), jsonOptions);
        })
        .WithName("GetWorkstationCollateralExposure").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ExposureSnapshotDto>(200)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationOperatorInbox), async (Guid? fundAccountId, HttpContext context) =>
        {
            var inbox = await BuildOperatorInboxAsync(fundAccountId, context).ConfigureAwait(false);
            return Results.Json(inbox, jsonOptions);
        })
        .WithName("GetWorkstationOperatorInbox").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewStrategies, UserPermission.ManageStrategies, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<OperatorInboxDto>(200)
        .Produces(403)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationDataOperations), async (HttpContext context) =>
        {
            var payload = await BuildDataPayloadAsync(context).ConfigureAwait(false);
            return payload is null
                ? DataReadServicesUnavailable()
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationDataOperations").RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage)
        .Produces<WorkstationDataPayload>(200)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationData), async (HttpContext context) =>
        {
            var payload = await BuildDataPayloadAsync(context).ConfigureAwait(false);
            return payload is null
                ? DataReadServicesUnavailable()
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationData").RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage)
        .Produces<WorkstationDataPayload>(200)
        .Produces(503);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationDataQuery), async (DataQueryRequest request, HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<DuckDbQueryService>();
            if (queryService is null)
            {
                return Results.Problem(
                    "The data query service is not available.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var options = context.RequestServices
                .GetService<IOptionsMonitor<DataQueryOptions>>()?.CurrentValue
                ?? new DataQueryOptions();
            if (!options.Enabled)
            {
                return Results.Problem(
                    "The data query workbench is disabled.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Guard violations and SQL errors are part of the result payload (Success=false)
            // so the workbench can render them inline without parsing problem responses.
            var result = await queryService.ExecuteAsync(request.Sql, options, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("PostWorkstationDataQuery").RequirePermission(UserPermission.ViewHistoricalData).DeclareNonMutating("SqlStatementGuard admits one SELECT-family statement with no embedded semicolon and a blocked-keyword list, so this is a read whose query does not fit in a URL.")
        .Produces<DataQueryResult>(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationDataReplacementCost), (HttpContext context) =>
        {
            var estimate = TryBuildDataReplacementCostEstimate(context);
            return estimate is null
                ? Results.Problem(
                    "Storage catalog is not available.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(estimate);
        })
        .WithName("GetWorkstationDataReplacementCost").RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage)
        .Produces<DataReplacementCostEstimate>(200)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationGovernance), async (HttpContext context) =>
        {
            var payload = await BuildAccountingPayloadAsync(context).ConfigureAwait(false);
            return payload is null
                ? StrategyReadServiceUnavailable()
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationGovernance").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<WorkstationAccountingPayload>(200)
        .Produces(503)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccounting), async (HttpContext context) =>
        {
            var payload = await BuildAccountingPayloadAsync(context).ConfigureAwait(false);
            return payload is null
                ? StrategyReadServiceUnavailable()
                : Results.Ok(payload);
        })
        .WithName("GetWorkstationAccounting").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<WorkstationAccountingPayload>(200)
        .Produces(503)
        .RequireWorkstationTenantCompanyScope();

        MapReportingAuthorityEndpoints(group, jsonOptions);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolio), async (HttpContext context) =>
        {
            var payload = await BuildPortfolioPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolio").RequirePermission(UserPermission.ViewTrades)
        .Produces<WorkstationPortfolioPayload>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolioSummary), async (string? fundAccountId, string? strategyId, string? entity, HttpContext context) =>
        {
            var payload = await BuildPortfolioSummaryPayloadAsync(context, fundAccountId, strategyId, entity).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolioSummary").RequirePermission(UserPermission.ViewTrades)
        .Produces<WorkstationPortfolioSummaryPayload>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolioMultiAssetCoverage), async (string? fundAccountId, string? entity, string? assetClass, HttpContext context) =>
        {
            var payload = await BuildMultiAssetCoveragePayloadAsync(context, fundAccountId, entity, assetClass).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolioMultiAssetCoverage").RequirePermission(UserPermission.ViewTrades)
        .Produces<MultiAssetCoverageSummaryDto>(200)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAssetOperations), async (Guid securityId, HttpContext context) =>
        {
            var payload = await BuildAssetOperationsPayloadAsync(context, securityId).ConfigureAwait(false);
            return payload is null
                ? Results.NotFound()
                : Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationAssetOperations").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<AssetOperationsDetailDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuity), async (
            Guid? fundAccountId,
            string? periodId,
            Guid? ledgerBookId,
            string? status,
            HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var parsedStatus = ParseOperationsWorkflowStatus(status);
            if (!string.IsNullOrWhiteSpace(status) && parsedStatus is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Status '{status}' is not a valid operations workflow status."]
                });
            }

            var workflows = await service.ListAsync(fundAccountId, periodId, parsedStatus, context.RequestAborted, ledgerBookId: ledgerBookId).ConfigureAwait(false);
            return Results.Json(workflows, jsonOptions);
        })
        .WithName("GetOperationsContinuitySummary").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalPolicyMatrix), (HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsApprovalPolicyMatrixService>();
            if (service is null)
            {
                return Results.Problem("Operations approval policy matrix service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            return Results.Json(service.GetMatrix(), jsonOptions);
        })
        .WithName("GetOperationsContinuityApprovalPolicyMatrix").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<OperationsApprovalPolicyMatrixDto>(200)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalPolicyRules), async (
            OperationsApprovalPolicyRuleUpsertRequestDto? request,
            HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An operations approval policy rule request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsApprovalPolicyMatrixService>();
            if (service is null)
            {
                return Results.Problem("Operations approval policy matrix service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var trustedRequest = request with { RequestedBy = currentUser };
                var result = await service.UpsertRuleAsync(trustedRequest, currentUser, context.RequestAborted).ConfigureAwait(false);
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
        .WithName("UpsertOperationsContinuityApprovalPolicyRule").RequirePermission(UserPermission.AdminMaintenance)
        .Produces<OperationsApprovalPolicyRuleUpsertResultDto>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityCloseCalendar), async (
            Guid? fundAccountId,
            string? periodId,
            HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsCloseCalendarService>();
            if (service is null)
            {
                return Results.Problem("Operations close calendar service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var calendar = await service.GetCalendarAsync(fundAccountId, periodId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(calendar, jsonOptions);
        })
        .WithName("GetOperationsContinuityCloseCalendar").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<OperationsCloseCalendarDto>(200)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsPrivateCapitalCloseCockpit), async (
            string? fundProfileId,
            Guid? ledgerBookId,
            Guid? fundAccountId,
            string? periodId,
            string? entityId,
            HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IPrivateCapitalCloseCockpitService>();
            if (service is null)
            {
                return Results.Problem("Private-capital close cockpit service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            var cockpit = await service
                .GetCockpitAsync(
                    fundProfileId,
                    ledgerBookId,
                    fundAccountId,
                    periodId,
                    entityId,
                    context.RequestAborted,
                    tenantContext.TenantId,
                    tenantContext.CompanyId)
                .ConfigureAwait(false);
            return Results.Json(cockpit, jsonOptions);
        })
        .WithName("GetOperationsPrivateCapitalCloseCockpit").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<PrivateCapitalCloseCockpitDto>(200)
        .Produces(403)
        .RequireFundProfileTenantScope(
            UserPermission.ViewDirectLending,
            UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending,
            UserPermission.ModifySecurityMaster,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityCloseCalendarItems), async (
            OperationsCloseCalendarItemUpsertRequestDto? request,
            HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An operations close calendar item request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsCloseCalendarService>();
            if (service is null)
            {
                return Results.Problem("Operations close calendar service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var trustedRequest = request with { RequestedBy = currentUser };
                var result = await service.UpsertItemAsync(trustedRequest, currentUser, context.RequestAborted).ConfigureAwait(false);
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
        .WithName("UpsertOperationsContinuityCloseCalendarItem").RequirePermission(UserPermission.AdminMaintenance)
        .Produces<OperationsCloseCalendarItemUpsertResultDto>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuity), async (
            OperationsStartWorkflowRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An operations continuity start request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.StartWorkflowAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("StartOperationsContinuityWorkflow").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityById), async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow, jsonOptions);
        })
        .WithName("GetOperationsContinuityDetail").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityCloseReadiness), async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow?.CloseReadiness is null
                ? Results.NotFound()
                : Results.Json(workflow.CloseReadiness, jsonOptions);
        })
        .WithName("GetOperationsContinuityCloseReadiness").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<OperationsCloseReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityTimeline), async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            if (workflow is null)
            {
                return Results.NotFound();
            }

            var timeline = await service.GetTimelineAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(timeline, jsonOptions);
        })
        .WithName("GetOperationsContinuityTimeline").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityBrokerImport), async (
            Guid workflowId,
            OperationsTransitionRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An operations continuity transition request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.ImportBrokerDataAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ImportOperationsContinuityBrokerData").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityBrokerNormalize), async (
            Guid workflowId,
            OperationsTransitionRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An operations continuity transition request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.NormalizeBrokerTransactionsAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("NormalizeOperationsContinuityBrokerTransactions").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityPostureRefresh), async (
            Guid workflowId,
            OperationsGatePostureRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An operations continuity posture request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.RefreshGatePostureAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("RefreshOperationsContinuityGatePosture").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuitySecurityMasterResolve), async (
            Guid workflowId,
            OperationsSecurityMasterResolveRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A Security Master resolution request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.ResolveSecurityMasterMappingsAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ResolveOperationsContinuitySecurityMasterMappings").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuitySecurityMasterOverrideApprove), async (
            Guid workflowId,
            string overrideId,
            OperationsSecurityMasterOverrideApprovalRequestDto? request,
            HttpContext context) =>
        {
            if (!HasSecurityMasterOverrideApprovalPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A Security Master override approval request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser, OverrideId = overrideId };
            var result = await service.ApproveSecurityMasterOverrideAsync(workflowId, overrideId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ApproveOperationsContinuitySecurityMasterOverride").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ModifySecurityMaster)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityLedgerDraft), async (
            Guid workflowId,
            OperationsLedgerDraftRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A ledger draft request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.BuildLedgerDraftAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("BuildOperationsContinuityLedgerDraft").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityLedgerValidate), async (
            Guid workflowId,
            OperationsLedgerValidationRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A ledger validation request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.ValidateLedgerDraftAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ValidateOperationsContinuityLedgerDraft").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityLedgerPost), async (
            Guid workflowId,
            OperationsLedgerPostRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A ledger posting request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.PostLedgerEntriesAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("PostOperationsContinuityLedgerEntries").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityReconciliationRun), async (
            Guid workflowId,
            OperationsReconciliationRunRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A reconciliation run request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityReconciliationBridge>();
            if (service is null)
            {
                return Results.Problem("Operations continuity reconciliation bridge is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service
                .RunReconciliationAsync(workflowId, trustedRequest, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("RunOperationsContinuityReconciliation").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityReconciliationBreakAssign), async (
            Guid workflowId,
            string breakId,
            OperationsAssignBreakCaseRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A reconciliation break assignment request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.AssignBreakCaseAsync(workflowId, breakId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("AssignOperationsContinuityReconciliationBreak").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityReconciliationBreakResolve), async (
            Guid workflowId,
            string breakId,
            OperationsResolveBreakCaseRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A reconciliation break resolution request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with
            {
                Actor = currentUser,
                ActionOrigin = OperationsActionOriginDto.HumanOperator,
                ApprovalActor = NormalizeOptional(request.ApprovalActor),
                ApprovalReference = NormalizeOptional(request.ApprovalReference)
            };
            var result = await service.ResolveBreakCaseAsync(workflowId, breakId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ResolveOperationsContinuityReconciliationBreak").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalSubmit), async (
            Guid workflowId,
            OperationsSubmitApprovalRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An approval submission request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.SubmitForApprovalAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("SubmitOperationsContinuityApproval").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalApprove), async (
            Guid workflowId,
            OperationsApprovalDecisionRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An approval decision request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser, Reviewer = currentUser };
            var result = await service.ApproveWorkflowAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ApproveOperationsContinuityWorkflow").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityApprovalReject), async (
            Guid workflowId,
            OperationsRejectWorkflowRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "An approval rejection request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser, Reviewer = currentUser };
            var result = await service.RejectWorkflowAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("RejectOperationsContinuityWorkflow").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityClose), async (
            Guid workflowId,
            OperationsCloseWorkflowRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A close workflow request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.CloseWorkflowAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("CloseOperationsContinuityWorkflow").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityReopen), async (
            Guid workflowId,
            OperationsReopenWorkflowRequestDto? request,
            HttpContext context) =>
        {
            if (!HasGovernedWorkflowReopenPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return MissingOperationsPayload("request", "A reopen workflow request is required.");
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser, IsGovernedAdmin = HasGovernedWorkflowReopenPermission(context) };
            var result = await service.ReopenWorkflowAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ReopenOperationsContinuityWorkflow").RequirePermission(UserPermission.AdminMaintenance)
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityBreaks), async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow.BreakCases, jsonOptions);
        })
        .WithName("GetOperationsContinuityBreaks").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityLedgerPreview), async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow.LedgerPreview, jsonOptions);
        })
        .WithName("GetOperationsContinuityLedgerPreview").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityChecklist), async (Guid workflowId, HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var checklist = await service.GetChecklistAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(checklist, jsonOptions);
        })
        .WithName("GetOperationsContinuityChecklist").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.OperationsContinuityChecklistAcknowledge), async (
            Guid workflowId,
            string taskId,
            OperationsChecklistAcknowledgeRequestDto request,
            HttpContext context) =>
        {
            if (!HasOperationsContinuityMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.AcknowledgeChecklistTaskAsync(workflowId, taskId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .WithName("AcknowledgeOperationsContinuityChecklistTask").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationRuns), async (ReconciliationRunRequest request, HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.RunAsync(request, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .WithName("CreateReconciliationRun").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404)
        .Produces(429);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationRunById), async (string reconciliationRunId, HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.GetByIdAsync(reconciliationRunId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetReconciliationRun").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsReconciliation), async (string runId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.GetLatestForRunAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .WithName("GetLatestRunReconciliation").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsReconciliationHistory), async (string runId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationRunService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var history = await service.GetHistoryForRunAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetRunReconciliationHistory").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<ReconciliationRunSummary>>(200)
        .Produces(404)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRuns), async (
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            return Results.Json(
                await service.ListStatementRunsAsync(accessScope, context.RequestAborted).ConfigureAwait(false),
                jsonOptions);
        })
        .WithName("ListStatementRuns").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<StatementRunSummaryDto>>(200)
        .Produces(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRuns), async (
            StatementRunCreateDto request,
            HttpContext context) =>
            await CreateStatementRunAsync(request, context, jsonOptions).ConfigureAwait(false))
        .WithName("CreateStatementRun").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<StatementRunDto>(201)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunById), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var detail = await service
                .GetStatementRunAsync(runId, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("GetStatementRun").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<StatementRunDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunValidation), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var validation = await service
                .GetStatementRunValidationAsync(runId, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return validation is null ? Results.NotFound() : Results.Json(validation, jsonOptions);
        })
        .WithName("GetStatementRunValidation").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<StatementRunValidationDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunBreaks), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var breaks = await service
                .ListStatementRunBreaksAsync(runId, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return breaks is null ? Results.NotFound() : Results.Json(breaks, jsonOptions);
        })
        .WithName("ListStatementRunBreaks").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<StatementRunBreakDto>>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunReconcile), async (
            string runId,
            StatementRunReconcileRequestDto request,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var trustedRequest = request with { Actor = currentUser };
            var detail = await service
                .ReconcileStatementRunAsync(runId, trustedRequest, accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("ReconcileStatementRun").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<StatementRunDto>(200)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementExceptions), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var exceptions = await service
                .ListOpenExceptionsAsync(accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(exceptions, jsonOptions);
        })
        .WithName("ListStatementExceptions").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<StatementRunExceptionDto>>(200)
        .Produces(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();
        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementBreaks), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var breaks = await service
                .ListOpenStatementBreaksAsync(accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(breaks, jsonOptions);
        })
        .WithName("ListOpenStatementBreaks").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<StatementBreakDto>>(200)
        .Produces(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationOpenCases), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var cases = await service
                .ListOpenCasesAsync(accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(cases, jsonOptions);
        })
        .WithName("ListOpenReconciliationCases").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReconciliationCaseSummaryDto>>(200)
        .Produces(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationQueueStatus), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var accessScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var queueStatus = await service
                .ListQueueStatusAsync(accessScope, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(queueStatus, jsonOptions);
        })
        .WithName("ListReconciliationQueueStatus").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReconciliationQueueAccountStatusDto>>(200)
        .Produces(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();


        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueue), async (string? status, string? fundAccountId, Guid? ledgerBookId, HttpContext context) =>
        {
            if (!CanViewReconciliationBreakQueue(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem(
                    "Reconciliation break queue repository is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            var items = await GetBreakQueueItemsAsync(repository, queueScope, status, fundAccountId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(items, jsonOptions);
        })
        .WithName("GetReconciliationBreakQueue").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReconciliationBreakQueueItem>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueueById), async (string breakId, HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.GetByIdAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
            return item is null ? Results.NotFound() : Results.Json(item, jsonOptions);
        })
        .WithName("GetReconciliationBreakQueueItem").RequireAnyPermission(UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationCalibrationSummary), async (HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var asOf = DateTimeOffset.UtcNow;
            var ledgerBookId = ParseOptionalGuid(context.Request.Query["ledgerBookId"].FirstOrDefault());
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem(
                    "Reconciliation break queue repository is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            var items = await GetBreakQueueItemsAsync(repository, queueScope, status: null, fundAccountId: null, ledgerBookId: ledgerBookId, ct: context.RequestAborted).ConfigureAwait(false);
            var summary = BuildReconciliationCalibrationSummary(items, asOf);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetReconciliationCalibrationSummary").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ReconciliationCalibrationSummaryDto>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakAudit), async (string breakId, HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var history = await repository.GetAuditHistoryAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetReconciliationBreakAudit").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReconciliationBreakQueueAuditEvent>>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakReview), async (string breakId, ReviewReconciliationBreakRequest request, HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            }

            if (!CanMutateReconciliationBreakQueue(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var transition = await ReviewBreakAsync(context.RequestServices, queueScope, request with
            {
                ReviewedBy = ResolveCurrentActor(context)
            }, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(ToReconciliationCaseworkOperationResult(transition), jsonOptions);
        })
        .WithName("ReviewReconciliationBreak").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakResolve), async (string breakId, ResolveReconciliationBreakRequest request, HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            }

            if (request.Status is not ReconciliationBreakQueueStatus.Resolved and not ReconciliationBreakQueueStatus.Dismissed)
            {
                return Results.BadRequest(new { error = "Status must be Resolved or Dismissed for resolve action." });
            }
            if (string.IsNullOrWhiteSpace(request.OperatorRationale))
            {
                return Results.BadRequest(new { error = "Operator rationale is required for resolve or waive transitions." });
            }

            if (!CanMutateReconciliationBreakQueue(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var transition = await ResolveBreakAsync(
                    context.RequestServices,
                    queueScope,
                    request with
                    {
                        ResolvedBy = ResolveCurrentActor(context)
                    },
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(ToReconciliationCaseworkOperationResult(transition), jsonOptions);
            }
            catch (StatementReconciliationCaseworkHandoffException exception)
            {
                return Results.Problem(
                    detail: $"{exception.Code}: {exception.Message}",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Statement reconciliation casework handoff failed");
            }
        })
        .WithName("ResolveReconciliationBreak").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(503);


        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakAssign), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.Assign }, context, jsonOptions).ConfigureAwait(false))
        .WithName("AssignReconciliationBreakCase").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakTransition), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.TransitionStatus }, context, jsonOptions).ConfigureAwait(false))
        .WithName("TransitionReconciliationBreakCase").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakWaive), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.Waive }, context, jsonOptions).ConfigureAwait(false))
        .WithName("WaiveReconciliationBreakCase").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakSupersede), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.Supersede }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SupersedeReconciliationBreakCase").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationCaseTaxonomy), () => Results.Json(FileReconciliationBreakQueueRepository.Taxonomy, jsonOptions))
        .WithName("GetReconciliationCaseTaxonomy").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ReconciliationTaxonomySnapshot>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComments), async (string breakId, HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.GetByIdAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
            return item is null ? Results.NotFound() : Results.Json(item.Comments ?? [], jsonOptions);
        })
        .WithName("GetReconciliationBreakComments").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<IReadOnlyList<ReconciliationCaseComment>>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakRebuiltSnapshot), async (string breakId, HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.RebuildSnapshotFromAuditAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
            return item is null ? Results.NotFound() : Results.Json(item, jsonOptions);
        })
        .WithName("RebuildReconciliationBreakSnapshot").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComments), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.AddComment }, context, jsonOptions).ConfigureAwait(false))
        .WithName("AddReconciliationBreakComment").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComment), async (string breakId, string commentId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.EditComment, CommentId = commentId }, context, jsonOptions).ConfigureAwait(false))
        .WithName("EditReconciliationBreakComment").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapDelete(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComment), async (
            string breakId,
            string commentId,
            [FromQuery] long expectedVersion,
            [FromQuery] string? reason,
            [FromQuery] string? commandId,
            [FromQuery] string? correlationId,
            [FromQuery] string? source,
            HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(
                breakId,
                new ReconciliationCaseworkCommand(
                    breakId,
                    ReconciliationCaseworkAction.DeleteComment,
                    Actor: string.Empty,
                    CommandId: string.IsNullOrWhiteSpace(commandId) ? Guid.NewGuid().ToString("N") : commandId,
                    CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
                    Source: string.IsNullOrWhiteSpace(source) ? "workstation-api" : source,
                    ExpectedVersion: expectedVersion,
                    Reason: reason,
                    CommentId: commentId),
                context,
                jsonOptions).ConfigureAwait(false))
        .WithName("DeleteReconciliationBreakComment").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakRootCause), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.SetRootCause }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SetReconciliationBreakRootCause").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakResolution), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.SetResolution }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SetReconciliationBreakResolution").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakSignOff), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.SignOff }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SignOffReconciliationBreakCase").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakReopen), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.Reopen }, context, jsonOptions).ConfigureAwait(false))
        .WithName("ReopenReconciliationBreakCase").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationCaseworkOperationResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkDryRun), async (ReconciliationBulkCaseworkRequest request, HttpContext context) =>
            await ApplyReconciliationBulkEndpointAsync(request with { DryRun = true }, context, jsonOptions).ConfigureAwait(false))
        .WithName("DryRunReconciliationBreakBulkAction").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationBulkCaseworkResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkExecute), async (ReconciliationBulkCaseworkRequest request, HttpContext context) =>
            await ApplyReconciliationBulkEndpointAsync(request with { DryRun = false }, context, jsonOptions).ConfigureAwait(false))
        .WithName("ExecuteReconciliationBreakBulkAction").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces<ReconciliationBulkCaseworkResult>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkStatus), async (string bulkActionId, HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            var result = repository is null
                ? null
                : await repository.GetBulkCaseworkResultAsync(queueScope, bulkActionId, context.RequestAborted).ConfigureAwait(false);

            return Results.Json(new
            {
                bulkActionId,
                status = result is null ? "unknown" : "completed",
                requestedCount = result?.RequestedCount ?? 0,
                succeededCount = result?.SucceededCount ?? 0,
                failedCount = result?.FailedCount ?? 0,
                resultEndpoint = $"/api/workstation/reconciliation/break-queue/bulk/{Uri.EscapeDataString(bulkActionId)}/result"
            }, jsonOptions);
        })
        .WithName("GetReconciliationBreakBulkActionStatus").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkResult), async (string bulkActionId, HttpContext context) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await repository.GetBulkCaseworkResultAsync(queueScope, bulkActionId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetReconciliationBreakBulkActionResult").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ReconciliationBulkCaseworkResult>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedger), async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetLedgerSummaryAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return summary is null
                ? Results.NotFound()
                : Results.Json(summary, jsonOptions);
        })
        .WithName("GetRunLedger").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<LedgerSummary>(200)
        .Produces(404)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsContinuity), async (string runId, HttpContext context) =>
        {
            var continuityService = context.RequestServices.GetService<StrategyRunContinuityService>();
            if (continuityService is null)
            {
                return Results.Problem("Strategy run continuity service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await continuityService.GetRunContinuityAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(new StrategyRunContinuityDto(
                    detail.Run,
                    detail.Lineage,
                    detail.CashFlow,
                    detail.Reconciliation,
                    detail.ContinuityStatus), jsonOptions);
        })
        .WithName("GetRunContinuity").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyRunContinuityDto>(200)
        .Produces(404)
        .Produces(501)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsReviewPacket), async (string runId, Guid? fundAccountId, HttpContext context) =>
        {
            var reviewPacketService = context.RequestServices.GetService<StrategyRunReviewPacketService>();
            if (reviewPacketService is null)
            {
                return Results.Problem("Strategy run review packet service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var packet = await reviewPacketService.GetAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    fundAccountId,
                    context.RequestAborted)
                .ConfigureAwait(false);
            return packet is null
                ? Results.NotFound()
                : Results.Json(packet, jsonOptions);
        })
        .WithName("GetRunReviewPacket")
        .Produces<StrategyRunReviewPacketDto>(200)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsEquityCurve), async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var curve = await readService.GetEquityCurveAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return curve is null
                ? Results.NotFound()
                : Results.Json(curve, jsonOptions);
        })
        .WithName("GetRunEquityCurve").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<EquityCurveSummary>(200)
        .Produces(404)
        .Produces(501)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsFills), async (string runId, string? symbol, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetFillsAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (summary is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var filtered = summary with
                {
                    Fills = summary.Fills
                        .Where(f => string.Equals(f.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        .ToArray(),
                    TotalFills = summary.Fills
                        .Count(f => string.Equals(f.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                };
                return Results.Json(filtered, jsonOptions);
            }

            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetRunFills").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<RunFillSummary>(200)
        .Produces(404)
        .Produces(501)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsAttribution), async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var attribution = await readService.GetAttributionAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return attribution is null
                ? Results.NotFound()
                : Results.Json(attribution, jsonOptions);
        })
        .WithName("GetRunAttribution").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<RunAttributionSummary>(200)
        .Produces(404)
        .Produces(501)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerTrialBalance), async (
            string runId,
            string? accountType,
            string? fundId,
            string? entityId,
            string? sleeveId,
            string? strategyId,
            string? portfolioId,
            string? ledgerBookId,
            string? bookId,
            string? accountId,
            string? investorId,
            string? capitalAccountId,
            Guid? instrumentId,
            string? taxLotId,
            string? costCenterId,
            string? counterpartyId,
            string? organizationId,
            string? customerId,
            string? vendorId,
            string? projectId,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetLedgerSummaryAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (summary is null)
            {
                return Results.NotFound();
            }

            var lines = string.IsNullOrWhiteSpace(accountType)
                ? summary.TrialBalance
                : summary.TrialBalance
                    .Where(l => string.Equals(l.AccountType, accountType, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            var externalGlDimensions = BuildExternalGlDimensionFilter(context.Request.Query);
            lines = lines
                .Where(line => MatchesLedgerDimensionFilter(
                    line.Dimensions,
                    fundId,
                    entityId,
                    sleeveId,
                    strategyId,
                    portfolioId,
                    NormalizeOptional(ledgerBookId) ?? NormalizeOptional(bookId),
                    accountId,
                    investorId,
                    capitalAccountId,
                    instrumentId,
                    taxLotId,
                    costCenterId,
                    counterpartyId,
                    organizationId,
                    customerId,
                    vendorId,
                    projectId,
                    externalGlDimensions))
                .ToArray();
            return Results.Json(lines, jsonOptions);
        })
        .WithName("GetRunLedgerTrialBalance").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<LedgerTrialBalanceLine>>(200)
        .Produces(404)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerJournal), async (
            string runId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? fundId,
            string? entityId,
            string? sleeveId,
            string? strategyId,
            string? portfolioId,
            string? ledgerBookId,
            string? bookId,
            string? accountId,
            string? investorId,
            string? capitalAccountId,
            Guid? instrumentId,
            string? taxLotId,
            string? costCenterId,
            string? counterpartyId,
            string? organizationId,
            string? customerId,
            string? vendorId,
            string? projectId,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetLedgerSummaryAsync(
                    runId,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (summary is null)
            {
                return Results.NotFound();
            }

            IEnumerable<LedgerJournalLine> entries = summary.Journal;
            if (from.HasValue)
            {
                entries = entries.Where(e => e.Timestamp >= from.Value);
            }

            if (to.HasValue)
            {
                entries = entries.Where(e => e.Timestamp <= to.Value);
            }

            var externalGlDimensions = BuildExternalGlDimensionFilter(context.Request.Query);
            entries = entries.Where(entry => MatchesLedgerDimensionFilter(
                entry.Dimensions,
                fundId,
                entityId,
                sleeveId,
                strategyId,
                portfolioId,
                NormalizeOptional(ledgerBookId) ?? NormalizeOptional(bookId),
                accountId,
                investorId,
                capitalAccountId,
                instrumentId,
                taxLotId,
                costCenterId,
                counterpartyId,
                organizationId,
                customerId,
                vendorId,
                projectId,
                externalGlDimensions));

            return Results.Json(entries.ToArray(), jsonOptions);
        })
        .WithName("GetRunLedgerJournal").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<LedgerJournalLine>>(200)
        .Produces(404)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterSearch), async (
            string? query,
            int? take,
            bool? activeOnly,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query is required." });
            }

            var request = new SecuritySearchRequest(
                Query: query.Trim(),
                Take: Math.Clamp(take ?? 25, 1, 100),
                ActiveOnly: activeOnly ?? true);
            var results = await queryService.SearchAsync(request, ct).ConfigureAwait(false);
            return Results.Json(results.Select(MapToWorkstationSecurity).ToArray(), jsonOptions);
        })
        .WithName("SearchSecurityMasterWorkstation").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityMasterWorkstationDto>>(200)
        .Produces(400);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterById), async (
            Guid securityId,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(MapToWorkstationSecurity(detail), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationSecurity").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityMasterWorkstationDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterHistory), async (
            Guid securityId,
            int? take,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var history = await queryService.GetHistoryAsync(
                    new SecurityHistoryRequest(
                        SecurityId: securityId,
                        Take: Math.Clamp(take ?? 50, 1, 500)),
                    ct)
                .ConfigureAwait(false);

            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationSecurityHistory").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityMasterEventEnvelope>>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterIdentity), async (
            Guid securityId,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(MapToIdentityDrillIn(detail), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationIdentityDrillIn").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityIdentityDrillInDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterEconomicDefinition), async (
            Guid securityId,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var record = await queryService.GetEconomicDefinitionByIdAsync(securityId, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(MapToEconomicDefinitionSummary(record), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationEconomicDefinition").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityEconomicDefinitionSummaryDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterTrustSnapshot), async (
            Guid securityId,
            string? fundProfileId,
            HttpContext context) =>
        {
            if (!EndpointAuthorization.HasAnyPermission(
                    context,
                    UserPermission.ViewSecurityMaster,
                    UserPermission.ModifySecurityMaster))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var workbenchService = context.RequestServices.GetService<ISecurityMasterWorkbenchQueryService>();
            if (workbenchService is null)
            {
                return Results.Problem("Security Master workbench service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var snapshot = await workbenchService
                .GetTrustSnapshotAsync(securityId, fundProfileId, context.RequestAborted)
                .ConfigureAwait(false);

            return snapshot is null
                ? Results.NotFound()
                : Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationTrustSnapshot").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityMasterTrustSnapshotDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterInstrumentPassport), async (
            Guid securityId,
            string? fundProfileId,
            HttpContext context) =>
        {
            if (!EndpointAuthorization.HasAnyPermission(
                    context,
                    UserPermission.ViewSecurityMaster,
                    UserPermission.ModifySecurityMaster))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var workbenchService = context.RequestServices.GetService<ISecurityMasterWorkbenchQueryService>();
            if (workbenchService is null)
            {
                return Results.Problem("Security Master workbench service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var passport = await workbenchService
                .GetInstrumentPassportAsync(securityId, fundProfileId, context.RequestAborted)
                .ConfigureAwait(false);

            return passport is null
                ? Results.NotFound()
                : Results.Json(passport, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationInstrumentPassport").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<InstrumentPassportDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterBulkResolveConflicts), async (
            BulkResolveSecurityMasterConflictsRequest request,
            HttpContext context) =>
        {
            if (!HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var workbenchService = context.RequestServices.GetService<ISecurityMasterWorkbenchQueryService>();
            if (workbenchService is null)
            {
                return Results.Problem("Security Master workbench service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await workbenchService
                .BulkResolveConflictsAsync(request, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(result, jsonOptions);
        })
        .WithName("BulkResolveSecurityMasterWorkstationConflicts").RequirePermission(UserPermission.ModifySecurityMaster)
        .Accepts<BulkResolveSecurityMasterConflictsRequest>("application/json")
        .Produces<BulkResolveSecurityMasterConflictsResult>(200)
        .Produces(403)
        .Produces(501);

        // --- Multi-run comparison and diff ---

        group.MapPost(WorkstationSubroute(UiApiRoutes.RunsCompare), async (RunComparisonRequest request, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }
            var comparisonService = context.RequestServices.GetService<StrategyRunComparisonService>()
                ?? new StrategyRunComparisonService(readService);

            if (request.RunIds is not { Count: >= 2 })
            {
                return Results.BadRequest(new { error = "At least two run IDs are required for comparison." });
            }

            if (request.RunIds.Count > MaxRunComparisonRequestIds)
            {
                return Results.BadRequest(new { error = $"A maximum of {MaxRunComparisonRequestIds} run IDs can be compared per request." });
            }

            var scope = ResolveStrategyRunReadScope(context);
            if (!await AreStrategyRunsAccessibleAsync(
                    readService,
                    request.RunIds,
                    scope,
                    context.RequestAborted)
                .ConfigureAwait(false))
            {
                return Results.NotFound(new { error = "One or more run IDs were not found." });
            }

            var comparison = await comparisonService.CompareRunsAsync(
                    request,
                    scope,
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (request.Modes is { Count: > 0 })
            {
                var parsedModes = ParseModes(request.Modes);
                if (parsedModes is { Count: > 0 })
                {
                    var modeFilter = new HashSet<StrategyRunMode>(parsedModes);
                    comparison = comparison.Where(row => modeFilter.Contains(row.Mode)).ToArray();
                }
            }

            return Results.Json(comparison, jsonOptions);
        })
        .WithName("CompareRuns").DeclareNonMutating("Compares retained strategy runs and returns the comparison; the handler resolves the caller read scope, checks the runs are accessible, and calls StrategyRunComparisonService.CompareRunsAsync, which holds only StrategyRunReadService.").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyRunComparison>>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.RunsDiff), async (RunDiffRequest request, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }
            var comparisonService = context.RequestServices.GetService<StrategyRunComparisonService>()
                ?? new StrategyRunComparisonService(readService);
            var scope = ResolveStrategyRunReadScope(context);
            if (!await AreStrategyRunsAccessibleAsync(
                    readService,
                    [request.BaseRunId, request.TargetRunId],
                    scope,
                    context.RequestAborted)
                .ConfigureAwait(false))
            {
                return Results.NotFound(new { error = "One or both run IDs not found." });
            }

            var diff = await comparisonService.BuildDiffAsync(
                    request,
                    scope,
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (diff is null)
            {
                return Results.NotFound(new { error = "One or both run IDs not found." });
            }

            return Results.Json(diff, jsonOptions);
        })
        .WithName("DiffRuns").DeclareNonMutating("Diffs two retained strategy runs and returns the result; same read path as CompareRuns, through StrategyRunComparisonService.BuildDiffAsync.").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyRunDiff>(200)
        .Produces(404)
        .Produces(501);

        app.MapGet(UiApiRoutes.StrategyRunsByStrategy, async (string strategyId, string? type, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            RunType? runType = null;
            if (!string.IsNullOrWhiteSpace(type) &&
                Enum.TryParse<RunType>(type, ignoreCase: true, out var parsed))
            {
                runType = parsed;
            }

            var runs = await readService.GetRunsAsync(
                    strategyId,
                    runType,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("GetStrategyRuns")
        .WithTags("Strategies")
        .Produces<IReadOnlyList<StrategyRunSummary>>(200)
        .RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunHistory), async (
            string? mode,
            StrategyRunStatus? status,
            string? strategyId,
            int? limit,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var modes = ParseModes(mode);
            var runs = await readService.GetRunsAsync(
                    new StrategyRunHistoryQuery(
                        Modes: modes,
                        Status: status,
                        StrategyId: strategyId,
                        Limit: Math.Clamp(limit ?? 50, 1, 500)),
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("GetWorkstationRunHistory").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyRunSummary>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsTimeline), async (
            string? mode,
            StrategyRunStatus? status,
            string? strategyId,
            int? limit,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var modes = ParseModes(mode);
            var query = new StrategyRunHistoryQuery(
                Modes: modes,
                Status: status,
                StrategyId: strategyId,
                Limit: Math.Clamp(limit ?? 100, 1, 500));

            var timeline = await readService.GetMergedTimelineAsync(
                    query,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(timeline, jsonOptions);
        })
        .WithName("GetWorkstationMergedRunTimeline").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyRunTimelineEntry>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLineageTimeline), async (
            string? mode,
            StrategyRunStatus? status,
            string? strategyId,
            int? limit,
            HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var modes = ParseModes(mode);
            var query = new StrategyRunHistoryQuery(
                Modes: modes,
                Status: status,
                StrategyId: strategyId,
                Limit: Math.Clamp(limit ?? 100, 1, 500));

            var timeline = await readService.GetLineageTimelineAsync(
                    query,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(
                timeline.Where(static entry => entry.EventType != StrategyRunLineageEventType.ReplayVerified).ToArray(),
                jsonOptions);
        })
        .WithName("GetWorkstationRunLineageTimeline").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyRunLineageTimelineEntry>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsSweeps), async (int? limit, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var sweeps = await readService.GetSweepResultGroupsAsync(
                    limit ?? 25,
                    ResolveStrategyRunReadScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(sweeps, jsonOptions);
        })
        .WithName("GetWorkstationSweepResults").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategySweepResultGroup>>(200)
        .Produces(501);

        app.MapGet(UiApiRoutes.StrategyRunsCompare, async (string? ids, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (string.IsNullOrWhiteSpace(ids))
            {
                return Results.BadRequest(new { error = "At least two run IDs are required. Use ?ids=a,b" });
            }

            var runIds = ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            if (runIds.Length < 2)
            {
                return Results.BadRequest(new { error = "At least two run IDs are required for comparison." });
            }

            if (runIds.Length > MaxRunComparisonRequestIds)
            {
                return Results.BadRequest(new { error = $"A maximum of {MaxRunComparisonRequestIds} run IDs can be compared per request." });
            }

            var scope = ResolveStrategyRunReadScope(context);
            if (!await AreStrategyRunsAccessibleAsync(
                    readService,
                    runIds,
                    scope,
                    context.RequestAborted)
                .ConfigureAwait(false))
            {
                return Results.NotFound(new { error = "One or more run IDs were not found." });
            }

            var comparison = await readService.GetRunComparisonDtosAsync(
                    runIds,
                    scope,
                    ct: context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(comparison, jsonOptions);
        })
        .WithName("CompareStrategyRuns")
        .WithTags("Strategies")
        .Produces<IReadOnlyList<RunComparisonDto>>(200)
        .Produces(400)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies);

        // --- Portfolio cash-flow projections ---


        var portfolioGroup = app.MapGroup("/api/portfolio").WithTags("Portfolio");

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioCashFlows), async (
            string runId,
            DateTimeOffset? asOf,
            string? currency,
            int? bucketDays,
            HttpContext context) =>
        {
            var projectionService = context.RequestServices.GetService<CashFlowProjectionService>();
            if (projectionService is null)
            {
                return Results.Problem(
                    "Cash flow projection service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await projectionService
                .GetAsync(runId, asOf, currency, bucketDays, context.RequestAborted)
                .ConfigureAwait(false);

            return summary is null
                ? Results.NotFound()
                : Results.Json(summary, jsonOptions);
        })
        .WithName("GetPortfolioCashFlows")
        .Produces<RunCashFlowSummary>(200)
        .Produces(404)
        .Produces(501);

        MapCrossStrategyPortfolioRoutes(portfolioGroup, jsonOptions);
        app.MapGet("/workstation", (IWebHostEnvironment environment) => ServeWorkstationIndex(environment))
            .DeclareOpenRead("Browser workstation shell HTML; it carries no operator data, and the session middleware redirects an unauthenticated caller to /login before it is served. Every governed read the loaded shell then makes is declared on its own route.").ExcludeFromDescription();

        app.MapGet("/workstation/{*path}", (string? path, IWebHostEnvironment environment) =>
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.HasExtension(path))
                return ServeWorkstationIndex(environment);

            // Serve static assets (JS, CSS, etc.) directly from wwwroot/workstation/.
            // UseStaticFiles() middleware runs after routing in WebApplication, so the
            // catch-all route must serve these files explicitly.
            var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var workstationRoot = Path.GetFullPath(Path.Combine(root, "workstation"));
            var normalizedPath = path.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedPath))
                return Results.NotFound();

            var filePath = Path.GetFullPath(Path.Combine(workstationRoot, normalizedPath));
            var rootWithSeparator = workstationRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(rootWithSeparator, StringComparison.Ordinal) || !File.Exists(filePath))
                return Results.NotFound();

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".js" => "application/javascript",
                ".css" => "text/css",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                _ => "application/octet-stream"
            };
            return Results.File(filePath, contentType);
        }).DeclareOpenRead("Browser workstation shell fallback and its static assets from wwwroot/workstation; same shell and same reasoning as /workstation.").ExcludeFromDescription();
    }

    private static Task<TradingOperatorReadinessDto> GetTradingOperatorReadinessAsync(
        Guid? fundAccountId,
        HttpContext context)
    {
        var readinessService = context.RequestServices.GetService<TradingOperatorReadinessService>();
        if (readinessService is null)
        {
            var logger = context.RequestServices.GetService<Microsoft.Extensions.Logging.ILogger<TradingOperatorReadinessService>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingOperatorReadinessService>.Instance;
            readinessService = new TradingOperatorReadinessService(context.RequestServices, logger);
        }

        return readinessService.GetAsync(
            fundAccountId,
            ResolveStrategyRunReadScope(context),
            context.RequestAborted);
    }

    private static string NormalizeOperatorInboxToken(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var output = new StringBuilder(Math.Min(trimmed.Length, MaxOperatorInboxTokenLength));
        var previousWasSeparator = false;
        var producedCharacters = 0;

        foreach (var character in trimmed)
        {
            if (producedCharacters >= MaxOperatorInboxTokenLength)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                output.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                producedCharacters++;
                continue;
            }

            if (!previousWasSeparator && output.Length > 0)
            {
                output.Append('-');
                previousWasSeparator = true;
                producedCharacters++;
            }
        }

        if (output.Length > 0 && output[^1] == '-')
        {
            output.Length--;
        }

        return output.ToString();
    }

    private static string BuildTradingBrokerageNotes(
        StrategyRunSummary? run,
        bool hasLiveExecutionState,
        BrokerageConfiguration? brokerageConfiguration)
    {
        if (hasLiveExecutionState)
        {
            return "Live execution state from PaperTradingPortfolio and OrderManagementSystem.";
        }

        if (run?.Mode == StrategyRunMode.Paper && run.Promotion?.SuggestedNextMode == StrategyRunMode.Live)
        {
            var brokerageValidation = BrokerageValidationEvaluator.Evaluate(brokerageConfiguration);
            return brokerageValidation.HasBlockingGap
                ? $"Paper promotion is complete. Live promotion remains blocked. {brokerageValidation.Summary}"
                : $"Paper promotion is complete. {brokerageValidation.Summary}";
        }

        return "Paper gateway not active. Start a paper session to see live position and order data.";
    }

    // Returns null when neither the strategy run read service nor the configuration store is
    // registered so the route can respond 503 instead of serving fabricated provider/backfill data.
    private static async Task<WorkstationDataPayload?> BuildDataPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var configStore = context.RequestServices.GetService<Meridian.Application.UI.ConfigStore>();
        var kernelObservability = context.RequestServices.GetService<KernelObservabilityService>()?.GetSnapshot();
        var providerConnectionLifecycle = context.RequestServices.GetService<ProviderConnectionLifecycleService>();
        var routingConnectionService = context.RequestServices.GetService<ProviderConnectionService>();
        var routingBindingService = context.RequestServices.GetService<ProviderBindingService>();
        var routingTrustService = context.RequestServices.GetService<ProviderTrustScoringService>();

        if (readService is null && configStore is null)
        {
            return null;
        }

        var runs = readService is not null
            ? (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray()
            : [];
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        // --- Providers (real data from metrics store when available) ---
        var metricsStatus = configStore?.TryLoadProviderMetrics();
        var healthyProviderCount = metricsStatus?.HealthyProviders ?? 0;
        var canManageCredentials = HasPermission(context, UserPermission.ManageCredentials);
        var connectionRows = canManageCredentials && providerConnectionLifecycle is not null
            ? await providerConnectionLifecycle.GetConnectionsAsync(context.RequestAborted).ConfigureAwait(false)
            : [];
        var routingConnections = routingConnectionService is not null
            ? await routingConnectionService.GetConnectionsAsync(context.RequestAborted).ConfigureAwait(false)
            : [];
        var routingBindings = routingBindingService is not null
            ? await routingBindingService.GetBindingsAsync(context.RequestAborted).ConfigureAwait(false)
            : [];
        var trustSnapshots = routingTrustService is not null
            ? await routingTrustService.GetTrustSnapshotsAsync(context.RequestAborted).ConfigureAwait(false)
            : [];
        var providers = BuildWorkstationDataProviderRecords(
            metricsStatus,
            connectionRows,
            routingConnections,
            routingBindings,
            trustSnapshots,
            exposeConnectionSummaries: canManageCredentials);

        // --- Backfills (last known backfill result from status file) ---
        var lastBackfill = configStore?.TryLoadBackfillStatus();
        WorkstationDataBackfillRecord[] backfills;
        if (lastBackfill is not null)
        {
            var symbolSummary = lastBackfill.Symbols.Length > 0
                ? string.Join(", ", lastBackfill.Symbols.Take(3)) + (lastBackfill.Symbols.Length > 3 ? " …" : "")
                : "unknown";
            var days = (lastBackfill.To != null && lastBackfill.From != null)
                ? (lastBackfill.To.Value.DayNumber - lastBackfill.From.Value.DayNumber).ToString(CultureInfo.InvariantCulture) + "d"
                : "—";
            var age = DateTimeOffset.UtcNow - lastBackfill.CompletedUtc;
            var updatedAt = age.TotalMinutes < 60
                ? $"{(int)age.TotalMinutes}m ago"
                : $"{(int)age.TotalHours}h ago";
            backfills =
            [
                new WorkstationDataBackfillRecord(
                    JobId: $"BF-{Math.Abs(lastBackfill.GetHashCode()) % 10000:D4}",
                    Scope: $"{symbolSummary} / {days}",
                    Provider: lastBackfill.Provider,
                    Status: lastBackfill.Success ? "Completed" : "Failed",
                    Progress: lastBackfill.Success ? "100%" : "Error",
                    UpdatedAt: updatedAt)
            ];
        }
        else
        {
            backfills = [];
        }

        var metrics = new List<WorkstationMetricCard>
        {
            new("providers-healthy", "Providers Healthy", healthyProviderCount.ToString(CultureInfo.InvariantCulture), "0", healthyProviderCount > 0 ? "success" : "default"),
            new("backfills-running", "Backfills Running", activeRuns.ToString(CultureInfo.InvariantCulture), activeRuns == 0 ? "0" : $"+{activeRuns}", activeRuns > 0 ? "default" : "success"),
            new("exports-ready", "Exports Ready", "0", "0", "default"),
            new("ops-review", "Needs Review", reviewRuns.ToString(CultureInfo.InvariantCulture), reviewRuns == 0 ? "0" : $"+{reviewRuns}", reviewRuns == 0 ? "default" : "warning"),
            new("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), FormatKernelJumpAlertDelta(kernelObservability), GetKernelJumpAlertTone(kernelObservability))
        };

        var replacementCost = TryBuildDataReplacementCostEstimate(context);
        if (replacementCost is { ConservativeEstimateUsd: > 0m })
        {
            metrics.Add(new WorkstationMetricCard(
                "data-replacement-cost",
                "Est. Replacement Cost",
                FormatCompactUsd(replacementCost.ConservativeEstimateUsd),
                $"{replacementCost.TotalGigabytes.ToString("0.#", CultureInfo.InvariantCulture)} GB local",
                "success"));
        }

        return new WorkstationDataPayload(
            Metrics: metrics,
            Providers: providers,
            Backfills: backfills,
            Exports: [],
            UploadTemplates: BuildDataUploadTemplateCatalog(),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
    }

    /// <summary>
    /// Builds the replacement-cost estimate from the storage catalog, or returns
    /// <see langword="null"/> when the catalog service is unavailable or the meter is disabled.
    /// </summary>
    private static DataReplacementCostEstimate? TryBuildDataReplacementCostEstimate(HttpContext context)
    {
        var catalogService = context.RequestServices.GetService<IStorageCatalogService>();
        if (catalogService is null)
            return null;

        var options = context.RequestServices
            .GetService<IOptionsMonitor<DataReplacementCostOptions>>()?.CurrentValue
            ?? new DataReplacementCostOptions();
        if (!options.Enabled)
            return null;

        return DataReplacementCostEstimator.Estimate(catalogService.GetCatalog(), options);
    }

    private static string FormatCompactUsd(decimal usd) => usd switch
    {
        >= 1_000_000m => "$" + (usd / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        >= 1_000m => "$" + (usd / 1_000m).ToString("0.0", CultureInfo.InvariantCulture) + "k",
        _ => "$" + usd.ToString("0", CultureInfo.InvariantCulture)
    };

    private static bool MatchesLedgerDimensionFilter(
        LedgerDimensionSetDto? dimensions,
        string? fundId,
        string? entityId,
        string? sleeveId,
        string? strategyId,
        string? portfolioId,
        string? bookId,
        string? accountId,
        string? investorId,
        string? capitalAccountId,
        Guid? instrumentId,
        string? taxLotId,
        string? costCenterId,
        string? counterpartyId,
        string? organizationId,
        string? customerId,
        string? vendorId,
        string? projectId,
        IReadOnlyDictionary<string, string>? externalGlDimensions = null)
        => MatchesDimensionValue(fundId, dimensions?.FundId)
           && MatchesDimensionValue(entityId, dimensions?.EntityId)
           && MatchesDimensionValue(sleeveId, dimensions?.SleeveId)
           && MatchesDimensionValue(strategyId, dimensions?.StrategyId)
           && MatchesDimensionValue(portfolioId, dimensions?.PortfolioId)
           && MatchesDimensionValue(bookId, dimensions?.BookId)
           && MatchesDimensionValue(accountId, dimensions?.AccountId)
           && MatchesDimensionValue(investorId, dimensions?.InvestorId)
           && MatchesDimensionValue(capitalAccountId, dimensions?.CapitalAccountId)
           && MatchesDimensionValue(instrumentId?.ToString("D"), dimensions?.InstrumentId?.ToString("D"))
           && MatchesDimensionValue(taxLotId, dimensions?.TaxLotId)
           && MatchesDimensionValue(costCenterId, dimensions?.CostCenterId)
           && MatchesDimensionValue(counterpartyId, dimensions?.CounterpartyId)
           && MatchesDimensionValue(organizationId, dimensions?.OrganizationId)
           && MatchesDimensionValue(customerId, dimensions?.CustomerId)
           && MatchesDimensionValue(vendorId, dimensions?.VendorId)
           && MatchesDimensionValue(projectId, dimensions?.ProjectId)
           && MatchesExternalGlDimensions(externalGlDimensions, dimensions?.ExternalGlDimensions);

    private static bool MatchesDimensionValue(string? requested, string? actual)
        => string.IsNullOrWhiteSpace(requested) ||
           string.Equals(actual, requested.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> BuildExternalGlDimensionFilter(IQueryCollection query)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in query)
        {
            const string prefix = "externalGl.";
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                key.Length > prefix.Length)
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    dimensions[key[prefix.Length..].Trim()] = value.Trim();
                }
            }
        }

        var externalGlDimensionKey = NormalizeOptional(query["externalGlDimensionKey"].FirstOrDefault());
        var externalGlDimensionValue = NormalizeOptional(query["externalGlDimensionValue"].FirstOrDefault());
        if (externalGlDimensionKey is not null && externalGlDimensionValue is not null)
        {
            dimensions[externalGlDimensionKey] = externalGlDimensionValue;
        }

        return dimensions;
    }

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string>? requested,
        IReadOnlyDictionary<string, string>? actual)
    {
        if (requested is null || requested.Count == 0)
        {
            return true;
        }

        if (actual is null || actual.Count == 0)
        {
            return false;
        }

        foreach (var (key, expectedValue) in requested)
        {
            if (!actual.TryGetValue(key, out var actualValue) ||
                !string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationPortfolioPayload> BuildPortfolioPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var portfolio = context.RequestServices.GetService<IPortfolioState>();
        var oms = context.RequestServices.GetService<IOrderManager>();
        var brokerageConfiguration = context.RequestServices.GetService<BrokerageConfiguration>();
        var quoteCollector = context.RequestServices.GetService<QuoteCollector>();
        var tradeCollector = context.RequestServices.GetService<TradeDataCollector>();

        // Resolve all runs for the run-linked equity panel
        StrategyRunSummary[] allRuns = [];
        StrategyRunDetail?[] runDetailsForCashFlow = [];
        if (readService is not null)
        {
            allRuns = (await readService
                    .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 12), context.RequestAborted)
                    .ConfigureAwait(false))
                .ToArray();

            // Fetch details for the most recent runs to power the cash-flow summary.
            // Mirrors the Accounting workspace pattern; bounded to avoid amplifying load.
            var cashFlowRuns = allRuns.Take(6).ToArray();
            runDetailsForCashFlow = cashFlowRuns.Length == 0
                ? []
                : await Task.WhenAll(cashFlowRuns.Select(run =>
                        readService.GetRunDetailAsync(run.RunId, context.RequestAborted)))
                    .ConfigureAwait(false);
        }

        // --- Metrics ---
        var realisedPnl = portfolio?.RealisedPnl ?? allRuns.FirstOrDefault()?.NetPnl ?? 0m;
        var unrealisedPnl = portfolio?.UnrealisedPnl ?? 0m;
        var totalPnl = realisedPnl + unrealisedPnl;
        var pnlTone = totalPnl >= 0m ? "success" : "warning";

        var metrics = new WorkstationMetricCard[]
        {
            new("portfolio-net-pnl", "Net P&L", FormatCurrency(totalPnl), totalPnl >= 0m ? "+session" : "-session", pnlTone),
            new("portfolio-cash", "Cash", portfolio is not null ? FormatCurrency(portfolio.Cash) : "—", "0%", "default"),
            new("portfolio-value", "Portfolio Value", portfolio is not null ? FormatCurrency(portfolio.PortfolioValue) : "—", "0%", "default"),
            new("portfolio-runs", "Linked Runs", allRuns.Length.ToString(CultureInfo.InvariantCulture), "0%", "default")
        };

        // --- Positions (live mark drives MarkPrice / UnrealizedPnl / Exposure) ---
        WorkstationTradingPositionRow[] positions;
        if (portfolio is not null && portfolio.Positions.Count > 0)
        {
            positions = portfolio.Positions.Values.Select(pos =>
            {
                var mark = ResolveLiveMark(pos.Symbol, quoteCollector, tradeCollector);
                var hasMark = mark.HasValue && mark.Value > 0m;
                var effectiveMark = hasMark ? mark!.Value : pos.AverageCostBasis;
                var liveUnrealized = (effectiveMark - pos.AverageCostBasis) * pos.Quantity;
                var liveExposure = Math.Abs(pos.Quantity * effectiveMark);

                return new WorkstationTradingPositionRow(
                    PositionKey: pos.Symbol,
                    Symbol: pos.Symbol,
                    Side: pos.Quantity >= 0 ? "Long" : "Short",
                    Quantity: Math.Abs(pos.Quantity).ToString(CultureInfo.InvariantCulture),
                    AveragePrice: pos.AverageCostBasis.ToString("F2", CultureInfo.InvariantCulture),
                    MarkPrice: hasMark ? effectiveMark.ToString("F2", CultureInfo.InvariantCulture) : "—",
                    DayPnl: "—",
                    UnrealizedPnl: FormatCurrency(hasMark ? liveUnrealized : pos.UnrealizedPnl),
                    Exposure: hasMark ? FormatCurrency(liveExposure) : "—");
            }).ToArray();
        }
        else
        {
            positions = [];
        }

        // --- Risk state ---
        var grossExposure = 0m;
        var netExposureValue = 0m;
        var riskState = "Healthy";
        var riskSummary = "Portfolio exposure is within configured paper thresholds.";
        IReadOnlyList<string> activeGuardrails = [];
        IReadOnlyList<WorkstationRiskGuardrail> guardrails = [];

        if (portfolio is not null)
        {
            foreach (var pos in portfolio.Positions.Values)
            {
                var mark = ResolveLiveMark(pos.Symbol, quoteCollector, tradeCollector);
                var px = mark.HasValue && mark.Value > 0m ? mark.Value : pos.AverageCostBasis;
                grossExposure += Math.Abs(pos.Quantity * px);
                netExposureValue += pos.Quantity * px;
            }
            var drawdownPct = portfolio.PortfolioValue > 0m ? totalPnl / portfolio.PortfolioValue : 0m;
            if (drawdownPct < -0.05m)
            {
                riskState = "Constrained";
                riskSummary = "Portfolio has breached the 5% drawdown threshold.";
            }
            else if (drawdownPct < -0.02m)
            {
                riskState = "Observe";
                riskSummary = "Exposure nearing guardrail limits.";
            }
        }

        var runtimeRisk = await ResolveRuntimeRiskDescriptorAsync(context).ConfigureAwait(false);
        if (runtimeRisk is not null)
        {
            riskState = runtimeRisk.State;
            riskSummary = runtimeRisk.Summary;
            activeGuardrails = runtimeRisk.ActiveGuardrails;
            guardrails = runtimeRisk.Guardrails;
        }

        var risk = new WorkstationTradingRiskState(
            State: riskState,
            Summary: riskSummary,
            NetExposure: portfolio is not null ? FormatCurrency(netExposureValue) : "—",
            GrossExposure: portfolio is not null ? FormatCurrency(grossExposure) : "—",
            Var95: "—",
            MaxDrawdown: portfolio is not null && portfolio.PortfolioValue > 0m
                ? FormatPercent(totalPnl / portfolio.PortfolioValue)
                : "—",
            BuyingPowerUsed: portfolio is not null && portfolio.BuyingPower > 0m
                ? FormatPercent(grossExposure / portfolio.BuyingPower)
                : "—",
            ActiveGuardrails: activeGuardrails,
            Guardrails: guardrails);

        // --- Brokerage state ---
        var brokerageValidation = BrokerageValidationEvaluator.Evaluate(brokerageConfiguration);
        var latestRun = allRuns.FirstOrDefault(static r => r.Mode == StrategyRunMode.Paper) ?? allRuns.FirstOrDefault();
        var brokerage = new WorkstationTradingBrokerageState(
            Provider: brokerageValidation.GatewayDisplayName,
            Account: latestRun is not null && !string.IsNullOrWhiteSpace(latestRun.PortfolioId) ? latestRun.PortfolioId : "—",
            Environment: latestRun?.Mode == StrategyRunMode.Live ? "live" : "paper",
            Connection: portfolio is not null ? "Connected" : "Disconnected",
            LastHeartbeat: portfolio is not null ? "live" : "—",
            OrderIngress: oms is not null ? "healthy" : "—",
            FillFeed: portfolio is not null ? "healthy" : "—",
            Notes: [BuildTradingBrokerageNotes(latestRun, portfolio is not null, brokerageConfiguration)]);

        // --- Run-linked equity rows ---
        var runs = allRuns.Select(static run => new WorkstationPortfolioRunRow(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Engine: run.Engine.ToString(),
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            Pnl: run.NetPnl.HasValue ? FormatCurrency(run.NetPnl.Value) : "—",
            Sharpe: "—",
            Dataset: run.DatasetReference ?? "—",
            Window: run.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LastUpdated: run.LastUpdatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Notes: BuildRunNotes(run),
            PromotionState: run.Promotion?.State.ToString())).ToArray();

        return new WorkstationPortfolioPayload(
            Metrics: metrics,
            Positions: positions,
            Risk: risk,
            Brokerage: brokerage,
            Runs: runs,
            CashFlow: BuildAccountingWorkspaceCashFlowSummary(runDetailsForCashFlow));
    }

    private static async Task<WorkstationPortfolioSummaryPayload> BuildPortfolioSummaryPayloadAsync(HttpContext context, string? fundAccountId, string? strategyId, string? entity)
    {
        var started = DateTimeOffset.UtcNow;
        var basePayload = await BuildPortfolioPayloadAsync(context).ConfigureAwait(false);
        var cards = new List<WorkstationMetricCard>(basePayload.Metrics)
        {
            new("portfolio-exposure", "Gross Exposure", basePayload.Risk.GrossExposure, "live", "default"),
            new("portfolio-net-exposure", "Net Exposure", basePayload.Risk.NetExposure, "live", "default")
        };

        var serialized = JsonSerializer.Serialize(basePayload);
        var stale = string.Equals(basePayload.Brokerage.Connection, "Disconnected", StringComparison.OrdinalIgnoreCase);

        return new WorkstationPortfolioSummaryPayload(
            FundAccountId: string.IsNullOrWhiteSpace(fundAccountId) ? "all" : fundAccountId!,
            StrategyId: string.IsNullOrWhiteSpace(strategyId) ? "all" : strategyId!,
            Entity: string.IsNullOrWhiteSpace(entity) ? "portfolio" : entity!,
            ConsolidatedCards: cards,
            Positions: basePayload.Positions,
            Risk: basePayload.Risk,
            Telemetry: new WorkstationPortfolioSummaryTelemetry(
                RefreshLatencyMs: (long)Math.Max(0, (DateTimeOffset.UtcNow - started).TotalMilliseconds),
                PayloadSizeBytes: System.Text.Encoding.UTF8.GetByteCount(serialized),
                IsStale: stale,
                StaleReason: stale ? "brokerage-disconnected" : null,
                AsOfUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            DrillThroughRoutes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["positions"] = $"/portfolio/positions?fundAccountId={Uri.EscapeDataString(string.IsNullOrWhiteSpace(fundAccountId) ? "all" : fundAccountId!)}&strategyId={Uri.EscapeDataString(string.IsNullOrWhiteSpace(strategyId) ? "all" : strategyId!)}&entity={Uri.EscapeDataString(string.IsNullOrWhiteSpace(entity) ? "portfolio" : entity!)}",
                ["trades"] = $"/trading?fundAccountId={Uri.EscapeDataString(string.IsNullOrWhiteSpace(fundAccountId) ? "all" : fundAccountId!)}&strategyId={Uri.EscapeDataString(string.IsNullOrWhiteSpace(strategyId) ? "all" : strategyId!)}"
            });
    }

    private static async Task<MultiAssetCoverageSummaryDto> BuildMultiAssetCoveragePayloadAsync(
        HttpContext context,
        string? fundAccountId,
        string? entity,
        string? assetClass)
    {
        var service = context.RequestServices.GetService<IMultiAssetCoverageReadService>();
        if (service is null)
        {
            service = new MultiAssetCoverageReadService(new SecurityMasterOperationalReadinessService());
        }

        if (!TryResolveReconciliationBreakQueueScope(context, out var scope))
        {
            throw new InvalidOperationException(
                "A tenant- and company-scoped workstation request context is required.");
        }

        return await service
            .GetCoverageAsync(fundAccountId, entity, assetClass, scope, context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task<AssetOperationsDetailDto?> BuildAssetOperationsPayloadAsync(
        HttpContext context,
        Guid securityId)
    {
        var service = context.RequestServices.GetService<IAssetOperationsQueryService>()
            ?? new AssetOperationsReadService();
        return await service.GetOperationsAsync(securityId, context.RequestAborted).ConfigureAwait(false);
    }

    // Returns null when the strategy run read service is not registered so the route can
    // respond 503 instead of serving fabricated reconciliation/cash-flow data.
    private static async Task<WorkstationAccountingPayload?> BuildAccountingPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var breakQueueRepository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
        var kernelObservability = context.RequestServices.GetService<KernelObservabilityService>()?.GetSnapshot();
        var requestedLedgerBookId = ParseOptionalGuid(context.Request.Query["ledgerBookId"].FirstOrDefault());
        if (readService is null || breakQueueRepository is null)
        {
            return null;
        }

        if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
        {
            return null;
        }

        var manualJournalWorkbench = await BuildManualJournalWorkbenchPayloadAsync(context).ConfigureAwait(false);
        var breakQueueItems = await GetBreakQueueItemsAsync(
                breakQueueRepository,
                queueScope,
                status: null,
                fundAccountId: null,
                ledgerBookId: requestedLedgerBookId,
                ct: context.RequestAborted)
            .ConfigureAwait(false);
        var scopedOpenBreaks = breakQueueItems.Count(static item =>
            item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview);

        var allRuns = await GetAuthorizedAccountingRunsAsync(
                context,
                readService,
                queueScope,
                context.RequestAborted)
            .ConfigureAwait(false);
        if (allRuns is null)
        {
            return null;
        }

        var runs = allRuns.Take(6).ToArray();
        if (runs.Length == 0)
        {
            var reporting = BuildReportingPayload(context);
            // PR-03: return typed DTO
            return new WorkstationAccountingPayload(
                Metrics:
                [
                    new WorkstationMetricCard("open-breaks", "Open Breaks", scopedOpenBreaks.ToString(CultureInfo.InvariantCulture), "0%", scopedOpenBreaks == 0 ? "success" : "warning"),
                    new WorkstationMetricCard("timing-drift", "Timing Drift", "0", "0%", "default"),
                    new WorkstationMetricCard("security-gaps", "Security Gaps", "0", "0%", "success"),
                    new WorkstationMetricCard("audit-ready", "Audit Ready", "0", "0%", "default"),
                    new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
                ],
                ReconciliationQueue: Array.Empty<WorkstationAccountingRunRecord>(),
                BreakQueue: breakQueueItems,
                Workspace: new WorkstationAccountingWorkspaceSummary(0, 0, 0, scopedOpenBreaks, 0),
                CashFlow: BuildAccountingWorkspaceCashFlowSummary(Array.Empty<StrategyRunDetail?>()),
                Reporting: reporting,
                ControlCenter: BuildAccountingControlCenterPayload(breakQueueItems, reporting),
                KernelObservability: BuildKernelObservabilityPayload(kernelObservability),
                ManualJournalWorkbench: manualJournalWorkbench);
        }

        var reconciliationService = context.RequestServices.GetService<IReconciliationRunService>();
        var detailTasks = runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted));
        var reconciliationTasks = reconciliationService is null
            ? runs.Select(_ => Task.FromResult<ReconciliationRunDetail?>(null))
            : runs.Select(run => reconciliationService.GetLatestForRunAsync(run.RunId, context.RequestAborted));

        var details = await Task.WhenAll(detailTasks).ConfigureAwait(false);
        var reconciliations = await Task.WhenAll(reconciliationTasks).ConfigureAwait(false);

        var timingDriftRuns = reconciliations.Count(static detail => detail?.Summary.HasTimingDrift == true);
        var runsWithBreaks = reconciliations.Count(static detail => (detail?.Summary.BreakCount ?? 0) > 0);
        var runsWithSecurityIssues = details.Count(static detail =>
            (detail?.Portfolio?.SecurityMissingCount ?? 0) > 0 ||
            (detail?.Ledger?.SecurityMissingCount ?? 0) > 0);
        var auditReadyRuns = runs.Count(static run => !string.IsNullOrWhiteSpace(run.AuditReference)) - runsWithBreaks;
        var reportingPayload = BuildReportingPayload(context);

        // PR-03: return typed DTO
        return new WorkstationAccountingPayload(
            Metrics:
            [
                new WorkstationMetricCard("open-breaks", "Open Breaks", scopedOpenBreaks.ToString(CultureInfo.InvariantCulture), "0%", scopedOpenBreaks == 0 ? "success" : "warning"),
                new WorkstationMetricCard("timing-drift", "Timing Drift", timingDriftRuns.ToString(CultureInfo.InvariantCulture), "0%", timingDriftRuns == 0 ? "default" : "warning"),
                new WorkstationMetricCard("security-gaps", "Security Gaps", runsWithSecurityIssues.ToString(CultureInfo.InvariantCulture), "0%", runsWithSecurityIssues == 0 ? "success" : "warning"),
                new WorkstationMetricCard("audit-ready", "Audit Ready", Math.Max(0, auditReadyRuns).ToString(CultureInfo.InvariantCulture), "0%", auditReadyRuns > 0 ? "success" : "default"),
                new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
            ],
            ReconciliationQueue: runs
                .Zip(details, static (run, detail) => (run, detail))
                .Zip(reconciliations, (pair, reconciliation) => BuildAccountingRunCard(pair.run, pair.detail, reconciliation, kernelObservability))
                .ToArray(),
            BreakQueue: breakQueueItems,
            Workspace: new WorkstationAccountingWorkspaceSummary(
                TotalRuns: allRuns.Length,
                ReconciledRuns: reconciliations.Count(static detail => detail is not null),
                LedgerReadyRuns: runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                OpenBreaks: scopedOpenBreaks,
                SecurityIssues: runsWithSecurityIssues),
            CashFlow: BuildAccountingWorkspaceCashFlowSummary(details),
            Reporting: reportingPayload,
            ControlCenter: BuildAccountingControlCenterPayload(breakQueueItems, reportingPayload),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability),
            ManualJournalWorkbench: manualJournalWorkbench);
    }

    private static async Task<StrategyRunSummary[]?> GetAuthorizedAccountingRunsAsync(
        HttpContext context,
        StrategyRunReadService readService,
        ReconciliationBreakQueueScope scope,
        CancellationToken ct)
    {
        var tenancyRegistry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (tenancyRegistry is null)
        {
            return null;
        }

        try
        {
            var runs = await readService.GetRunsAsync(ct: ct).ConfigureAwait(false);
            var ownershipByFund = new Dictionary<string, FundProfileOwnership?>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var fundProfileId in runs
                         .Select(static run => run.FundProfileId)
                         .Where(static fundProfileId => !string.IsNullOrWhiteSpace(fundProfileId))
                         .Select(static fundProfileId => fundProfileId!.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ownershipByFund[fundProfileId] = await tenancyRegistry
                    .ResolveAsync(fundProfileId, ct)
                    .ConfigureAwait(false);
            }

            return runs
                .Where(run =>
                {
                    if (string.IsNullOrWhiteSpace(run.FundProfileId))
                    {
                        return false;
                    }

                    var fundProfileId = run.FundProfileId.Trim();
                    return ownershipByFund.TryGetValue(fundProfileId, out var ownership) &&
                           ownership is not null &&
                           ownership.IsHeldBy(scope.TenantId) &&
                           !string.IsNullOrWhiteSpace(ownership.CompanyId) &&
                           string.Equals(
                               ownership.CompanyId.Trim(),
                               scope.CompanyId.Trim(),
                               StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<ManualJournalEntryWorkbenchDto?> BuildManualJournalWorkbenchPayloadAsync(HttpContext context)
    {
        var service = context.RequestServices.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return null;
        }

        var query = context.Request.Query;
        var fundProfileId = query["fundProfileId"].FirstOrDefault();
        var ledgerBookId = ParseOptionalGuid(query["ledgerBookId"].FirstOrDefault());
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);

        return await service
            .GetWorkbenchAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
            .ConfigureAwait(false);
    }

    private static string ResolveModeVariant(StrategyRunMode? mode)
        => mode switch
        {
            StrategyRunMode.Paper => "paper",
            StrategyRunMode.Live => "live",
            _ => "research"
        };

    private static WorkstationAccountingRunRecord BuildAccountingRunCard(
        StrategyRunSummary run,
        StrategyRunDetail? detail,
        ReconciliationRunDetail? reconciliation,
        KernelObservabilitySnapshot? kernelObservability)
    {
        return new WorkstationAccountingRunRecord(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            LastUpdated: FormatRelativeTime(run.LastUpdatedAt),
            AuditReference: run.AuditReference,
            LedgerReference: run.LedgerReference,
            PortfolioId: run.PortfolioId,
            BreakCount: reconciliation?.Summary.BreakCount ?? 0,
            OpenBreakCount: reconciliation?.Summary.OpenBreakCount ?? 0,
            ReconciliationStatus: MapReconciliationStatus(reconciliation),
            Governance: new WorkstationAccountingRunGovernancePayload(
                HasAuditTrail: run.Governance?.HasAuditTrail ?? false,
                HasPortfolio: run.Governance?.HasPortfolio ?? false,
                HasLedger: run.Governance?.HasLedger ?? false,
                DatasetReference: run.Governance?.DatasetReference,
                FeedReference: run.Governance?.FeedReference),
            SecurityCoverage: BuildSecurityCoverage(detail),
            CashFlow: BuildAccountingRunCashFlowSummary(detail),
            LatestReconciliation: reconciliation is null
                ? null
                : new WorkstationAccountingRunReconciliationPayload(
                    ReconciliationRunId: reconciliation.Summary.ReconciliationRunId,
                    BreakCount: reconciliation.Summary.BreakCount,
                    OpenBreakCount: reconciliation.Summary.OpenBreakCount,
                    MatchCount: reconciliation.Summary.MatchCount,
                    HasTimingDrift: reconciliation.Summary.HasTimingDrift,
                    SecurityIssueCount: reconciliation.Summary.SecurityIssueCount,
                    HasSecurityCoverageIssues: reconciliation.Summary.HasSecurityCoverageIssues,
                    LastUpdated: FormatRelativeTime(reconciliation.Summary.CreatedAt),
                    Tone: reconciliation.Summary.BreakCount == 0 && !reconciliation.Summary.HasSecurityCoverageIssues ? "success" : "warning"),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
    }

    private static int GetKernelActiveAlertCount(KernelObservabilitySnapshot? snapshot)
        => snapshot?.ActiveAlertCount ?? 0;

    private static int GetKernelTotalAlertCount(KernelObservabilitySnapshot? snapshot)
        => snapshot?.AlertCount ?? 0;

    private static string GetKernelJumpAlertTone(KernelObservabilitySnapshot? snapshot)
        => GetKernelActiveAlertCount(snapshot) == 0 ? "success" : "warning";

    private static string FormatKernelJumpAlertDelta(KernelObservabilitySnapshot? snapshot)
        => $"{GetKernelTotalAlertCount(snapshot).ToString(CultureInfo.InvariantCulture)} total";

    private static WorkstationKernelObservabilityPayload BuildKernelObservabilityPayload(KernelObservabilitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new WorkstationKernelObservabilityPayload(
                UpdatedAtUtc: null,
                DeterminismChecksEnabled: false,
                ActiveAlerts: 0,
                TotalAlerts: 0,
                Alerts: 0,
                Domains: Array.Empty<WorkstationKernelDomainPayload>());
        }

        return new WorkstationKernelObservabilityPayload(
            UpdatedAtUtc: snapshot.UpdatedAtUtc,
            DeterminismChecksEnabled: snapshot.DeterminismChecksEnabled,
            ActiveAlerts: snapshot.ActiveAlertCount,
            TotalAlerts: snapshot.AlertCount,
            Alerts: snapshot.AlertCount,
            Domains: snapshot.Domains.Select(static domain => new WorkstationKernelDomainPayload(
                Domain: domain.Domain,
                Evaluations: domain.Evaluations,
                ThroughputPerMinute: domain.ThroughputPerMinute,
                LatencyMs: new WorkstationKernelLatencyPayload(
                    P50: domain.Latency.P50Ms,
                    P95: domain.Latency.P95Ms,
                    P99: domain.Latency.P99Ms),
                ReasonCoveragePercent: domain.ReasonCodeCoveragePercent,
                Drift: new WorkstationKernelDriftPayload(
                    Score: domain.ScoreDrift,
                    Severity: domain.SeverityDrift,
                    Methodology: "totalVariationDistance"),
                CriticalSeverityRate: new WorkstationKernelCriticalSeverityRatePayload(
                    ShortWindow: domain.CriticalRateShortWindow,
                    LongWindow: domain.CriticalRateLongWindow,
                    ShortWindowSamples: domain.CriticalRateShortWindowSamples,
                    LongWindowSamples: domain.CriticalRateLongWindowSamples,
                    JumpAlertActive: domain.CriticalJumpActive,
                    JumpAlertCount: domain.CriticalJumpAlertCount,
                    AlertThresholds: new WorkstationKernelAlertThresholdsPayload(
                        MinimumSampleCount: domain.CriticalJumpThresholds.MinimumSampleCount,
                        MinimumShortRate: domain.CriticalJumpThresholds.MinimumShortRate,
                        ZeroBaselineShortRate: domain.CriticalJumpThresholds.ZeroBaselineShortRate,
                        RelativeMultiplier: domain.CriticalJumpThresholds.RelativeMultiplier,
                        AbsoluteIncrease: domain.CriticalJumpThresholds.AbsoluteIncrease)),
                DeterminismMismatches: domain.DeterminismMismatches,
                LastUpdatedUtc: domain.LastUpdatedUtc)).ToArray());
    }

    private static string MapReconciliationStatus(ReconciliationRunDetail? reconciliation)
    {
        if (reconciliation is null)
        {
            return "NotStarted";
        }

        if (reconciliation.Summary.OpenBreakCount > 0)
        {
            return "BreaksOpen";
        }

        if (reconciliation.Summary.HasSecurityCoverageIssues)
        {
            return "SecurityCoverageOpen";
        }

        if (reconciliation.Summary.BreakCount > 0)
        {
            return "Resolved";
        }

        return "Balanced";
    }

    private static WorkstationReportingPayload BuildReportingPayload(HttpContext? context = null) =>
        context is null
            ? BuildUnavailableReportingPayload(BuildUnavailableReportingCapability(
                deployment: null,
                "The reporting workspace request context is unavailable."))
            : BuildEmbeddedReportingPayload(context);

    private static WorkstationReportingPayload BuildUnavailableReportingPayload(
        ReportingDeploymentCapabilityDto? deployment) =>
        new(
            ProfileCount: 0,
            RecommendedProfiles: [],
            Profiles: [],
            ReportPackDistributions: [],
            Summary: "Authoritative reporting is unavailable. Review the reporting deployment capability and readiness checks.",
            Templates: [],
            RecentRuns: [],
            DeploymentCapability: deployment);

    private static ReportAccessQueryContext BuildReportAccessQueryContext(HttpContext context)
    {
        var actor = EndpointAuthorization.TryResolveActor(context, out var resolvedActor)
            ? resolvedActor
            : null;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return new ReportAccessQueryContext(
            ActorPrincipalId: actor,
            GroupPrincipalIds: EndpointAuthorization.ResolveReportGroupPrincipalIds(context),
            CompanyId: EndpointAuthorization.ResolveCompanyId(context),
            HasGlobalOverride: EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance),
            TenantId: tenant.TenantId,
            RequireBoundScope: true);
    }

    private static WorkstationAccountingControlCenterPayload BuildAccountingControlCenterPayload(
        IReadOnlyList<ReconciliationBreakQueueItem> breakQueue,
        WorkstationReportingPayload reporting)
    {
        var criticalOpen = breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Critical && item.Status != ReconciliationBreakQueueStatus.Resolved && item.Status != ReconciliationBreakQueueStatus.Dismissed);
        var inReview = breakQueue.Count(item => item.Status == ReconciliationBreakQueueStatus.InReview);
        var unowned = breakQueue.Count(item => string.IsNullOrWhiteSpace(item.AssignedTo));
        var overdue = breakQueue.Count(item => item.Status != ReconciliationBreakQueueStatus.Resolved && item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-2));
        var breachCount = breakQueue.Count(item => item.Status != ReconciliationBreakQueueStatus.Resolved && item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-3));

        var alerts = new List<WorkstationAccountingAlertPayload>();
        if (criticalOpen > 0)
        {
            alerts.Add(new WorkstationAccountingAlertPayload("danger", $"{criticalOpen} critical reconciliation breaks remain unresolved."));
        }

        if (overdue > 0)
        {
            alerts.Add(new WorkstationAccountingAlertPayload("danger", $"{overdue} reconciliation breaks are overdue for resolution."));
        }

        if (reporting.ReportPackDistributions.Any(distribution => distribution.PendingItems > 0))
        {
            alerts.Add(new WorkstationAccountingAlertPayload("warning", "Report-pack distribution recipients have pending approval, publication, or delivery work."));
        }

        return new WorkstationAccountingControlCenterPayload(
            CloseReadiness: criticalOpen == 0 && overdue == 0 ? "ReadyWithAttention" : "Blocked",
            PortfolioFilterOptions: ["all-portfolios", "macro", "equity", "fixed-income"],
            AccountFilterOptions: breakQueue.Select(item => item.FundAccountId).Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct().Cast<string>().ToArray(),
            BlockerSeverityDistribution:
            [
                new WorkstationAccountingSeverityCountPayload("Critical", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Critical)),
                new WorkstationAccountingSeverityCountPayload("High", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.High)),
                new WorkstationAccountingSeverityCountPayload("Medium", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Medium)),
                new WorkstationAccountingSeverityCountPayload("Low", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Low))
            ],
            AgingCurves:
            [
                new WorkstationAccountingAgingBucketPayload("0-1d", breakQueue.Count(item => item.LastUpdatedAt >= DateTimeOffset.UtcNow.AddDays(-1))),
                new WorkstationAccountingAgingBucketPayload("2-3d", breakQueue.Count(item => item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-1) && item.LastUpdatedAt >= DateTimeOffset.UtcNow.AddDays(-3))),
                new WorkstationAccountingAgingBucketPayload("4d+", breakQueue.Count(item => item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-3)))
            ],
            OwnerWorkload: breakQueue.GroupBy(item => string.IsNullOrWhiteSpace(item.AssignedTo) ? "Unassigned" : item.AssignedTo!)
                .Select(group => new WorkstationAccountingOwnerWorkloadPayload(
                    Owner: group.Key,
                    OpenCount: group.Count(item => item.Status != ReconciliationBreakQueueStatus.Resolved && item.Status != ReconciliationBreakQueueStatus.Dismissed)))
                .OrderByDescending(item => item.OpenCount)
                .ToArray(),
            SlaBreachCount: breachCount,
            TrendSnapshots:
            [
                new WorkstationAccountingTrendSnapshotPayload("Open critical breaks", criticalOpen, criticalOpen > 0 ? "worsening" : "stable"),
                new WorkstationAccountingTrendSnapshotPayload("Breaks in review", inReview, inReview > 0 ? "improving" : "stable"),
                new WorkstationAccountingTrendSnapshotPayload("Unassigned breaks", unowned, unowned > 0 ? "worsening" : "stable"),
                new WorkstationAccountingTrendSnapshotPayload(
                    "Report distributions pending",
                    reporting.ReportPackDistributions.Count(distribution => distribution.PendingItems > 0),
                    "stable")
            ],
            DrillLinks:
            [
                new WorkstationAccountingDrillLinkPayload("Open close readiness", "/trading/readiness"),
                new WorkstationAccountingDrillLinkPayload("Open reconciliation queue", "/accounting/reconciliation"),
                new WorkstationAccountingDrillLinkPayload("Open report approvals", "/reporting/report-packs"),
                new WorkstationAccountingDrillLinkPayload("Open evidence completeness", "/reporting/evidence")
            ],
            Alerts: alerts);
    }

    private static string BuildRunNotes(StrategyRunSummary run)
    {
        if (run.Promotion?.RequiresReview == true)
        {
            return run.Promotion.State switch
            {
                StrategyRunPromotionState.CandidateForPaper => "Completed backtest awaiting paper review.",
                StrategyRunPromotionState.CandidateForLive => "Paper run pending live promotion review.",
                StrategyRunPromotionState.RequiresCompletion => "Run must complete before promotion review can proceed.",
                _ => "Run is flagged for Accounting review."
            };
        }

        if (!string.IsNullOrWhiteSpace(run.LedgerReference) && !string.IsNullOrWhiteSpace(run.PortfolioId))
        {
            return "Run has portfolio and ledger drill-in coverage.";
        }

        if (!string.IsNullOrWhiteSpace(run.LedgerReference))
        {
            return "Run includes ledger drill-in coverage.";
        }

        if (!string.IsNullOrWhiteSpace(run.PortfolioId))
        {
            return "Run includes portfolio drill-in coverage.";
        }

        return run.Status switch
        {
            StrategyRunStatus.Running => "Active run with live workspace telemetry.",
            StrategyRunStatus.Completed => "Completed run available for comparison and export.",
            StrategyRunStatus.Failed => "Run completed with errors requiring review.",
            _ => "Run is available for workstation review."
        };
    }

    private static string FormatWindow(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        var end = completedAt ?? DateTimeOffset.UtcNow;
        var span = end - startedAt;

        if (span.TotalDays >= 1)
        {
            return $"{(int)Math.Round(span.TotalDays)}d";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)Math.Round(span.TotalHours)}h";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)}m";
        }

        return "0m";
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var span = DateTimeOffset.UtcNow - timestamp;

        if (span.TotalMinutes < 1)
        {
            return "just now";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)}m ago";
        }

        if (span.TotalDays < 1)
        {
            return $"{(int)Math.Round(span.TotalHours)}h ago";
        }

        return $"{(int)Math.Round(span.TotalDays)}d ago";
    }

    private static string FormatReturn(decimal? totalReturn, decimal? netPnl)
    {
        if (totalReturn is not null)
        {
            return FormatPercent(totalReturn.Value);
        }

        if (netPnl is not null)
        {
            return FormatCurrency(netPnl.Value);
        }

        return "n/a";
    }

    private static string FormatSharpeProxy(StrategyRunSummary run)
    {
        if (run.TotalReturn is null && run.NetPnl is null)
        {
            return "n/a";
        }

        var proxy = (run.TotalReturn ?? 0m) * 12m;
        if (run.NetPnl is not null)
        {
            proxy += Math.Sign(run.NetPnl.Value) * 0.25m;
        }

        return proxy.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolves the freshest available mark price for a symbol so that workstation
    /// position rows can display live unrealized P&amp;L and exposure instead of placeholders.
    /// Priority: BBO mid (or far-touch when one side is missing) → most recent trade
    /// price → null (caller falls back to cost basis).
    /// </summary>
    internal static decimal? ResolveLiveMark(string symbol, QuoteCollector? quotes, TradeDataCollector? trades)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        if (quotes is not null && quotes.TryGet(symbol, out var bbo) && bbo is not null)
        {
            if (bbo.MidPrice is { } mid && mid > 0m)
            {
                return mid;
            }
            if (bbo.AskPrice > 0m)
            {
                return bbo.AskPrice;
            }
            if (bbo.BidPrice > 0m)
            {
                return bbo.BidPrice;
            }
        }

        if (trades is not null)
        {
            var recent = trades.GetRecentTrades(symbol, 1);
            if (recent.Count > 0 && recent[0].Price > 0m)
            {
                return recent[0].Price;
            }
        }

        return null;
    }

    private static string FormatPercent(decimal value)
        => $"{(value >= 0 ? "+" : string.Empty)}{(value * 100m).ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string FormatCurrency(decimal value)
    {
        var sign = value >= 0 ? "+" : "-";
        var absolute = Math.Abs(value);
        var scaled = absolute;
        var suffix = string.Empty;

        if (absolute >= 1_000_000m)
        {
            scaled = absolute / 1_000_000m;
            suffix = "M";
        }
        else if (absolute >= 1_000m)
        {
            scaled = absolute / 1_000m;
            suffix = "K";
        }

        return $"{sign}${scaled.ToString("0.##", CultureInfo.InvariantCulture)}{suffix}";
    }

    private static async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueItemsAsync(
        IServiceProvider services,
        ReconciliationBreakQueueScope scope,
        string? status,
        string? fundAccountId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>()
            ?? throw new InvalidOperationException(
                "Reconciliation break queue repository is not registered.");
        return await GetBreakQueueItemsAsync(repository, scope, status, fundAccountId, ledgerBookId, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueItemsAsync(
        IReconciliationBreakQueueRepository? repository,
        ReconciliationBreakQueueScope scope,
        string? status,
        string? fundAccountId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repository);

        ReconciliationBreakQueueStatus? parsed = null;
        if (Enum.TryParse<ReconciliationBreakQueueStatus>(status, ignoreCase: true, out var statusValue))
        {
            parsed = statusValue;
        }

        var items = await repository.GetAllAsync(scope, parsed, ct).ConfigureAwait(false);
        return items
            .Where(item => string.IsNullOrWhiteSpace(fundAccountId) ||
                           string.Equals(item.FundAccountId, fundAccountId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId.Value)
            .ToArray();
    }

    private static Guid? ParseOptionalGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static ReconciliationCalibrationSummaryDto BuildReconciliationCalibrationSummary(
        IReadOnlyList<ReconciliationBreakQueueItem> items,
        DateTimeOffset asOf)
    {
        var totalBreakCount = items.Count;
        var openBreakCount = items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open);
        var inReviewBreakCount = items.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview);
        var resolvedBreakCount = items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved);
        var dismissedBreakCount = items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed);
        var activeBreakCount = openBreakCount + inReviewBreakCount;
        var criticalOpenBreakCount = items.Count(static item =>
            (item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview) &&
            item.Severity == ReconciliationBreakSeverity.Critical);
        var pendingSignoffCount = items.Count(static item => RequiresCalibrationSignoff(item));
        var signedOffCount = items.Count(static item => IsSignedOff(item));
        var missingCalibrationMetadataCount = items.Count(static item => HasMissingCalibrationMetadata(item));

        var status = DetermineReconciliationCalibrationStatus(
            totalBreakCount,
            activeBreakCount,
            criticalOpenBreakCount,
            pendingSignoffCount,
            missingCalibrationMetadataCount);
        var profiles = items
            .GroupBy(static item => (
                Profile: NormalizeCalibrationValue(item.ToleranceProfileId, "unassigned-profile"),
                Route: NormalizeCalibrationValue(item.ExceptionRoute, "operations-triage")))
            .Select(BuildCalibrationProfileSummary)
            .OrderByDescending(static profile => profile.HighestSeverity)
            .ThenBy(static profile => profile.ToleranceProfileId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static profile => profile.ExceptionRoute, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ReconciliationCalibrationSummaryDto(
            AsOf: asOf,
            Status: status,
            Summary: BuildReconciliationCalibrationSummaryText(
                status,
                totalBreakCount,
                activeBreakCount,
                criticalOpenBreakCount,
                pendingSignoffCount,
                missingCalibrationMetadataCount,
                profiles.Length),
            TotalBreakCount: totalBreakCount,
            ActiveBreakCount: activeBreakCount,
            OpenBreakCount: openBreakCount,
            InReviewBreakCount: inReviewBreakCount,
            ResolvedBreakCount: resolvedBreakCount,
            DismissedBreakCount: dismissedBreakCount,
            CriticalOpenBreakCount: criticalOpenBreakCount,
            PendingSignoffCount: pendingSignoffCount,
            SignedOffCount: signedOffCount,
            MissingCalibrationMetadataCount: missingCalibrationMetadataCount,
            Profiles: profiles)
        {
            BreakCountTrend = activeBreakCount - resolvedBreakCount,
            AutoMatchRate = CalculateAutoMatchRate(totalBreakCount, activeBreakCount),
            T0ClosureRate = CalculateT0ClosureRate(totalBreakCount, resolvedBreakCount, dismissedBreakCount),
            BreakCountAlertThreshold = 25,
            AutoMatchRateAlertThreshold = 0.85m,
            T0ClosureRateAlertThreshold = 0.90m
        };
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ReviewBreakAsync(
        IServiceProvider services,
        ReconciliationBreakQueueScope scope,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        return await ReviewBreakAsync(repository, scope, request, ct).ConfigureAwait(false);
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ReviewBreakAsync(
        IReconciliationBreakQueueRepository? repository,
        ReconciliationBreakQueueScope scope,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct)
    {
        if (repository is null)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.NotFound,
                Item: null,
                Error: "Reconciliation break queue repository is not registered.");
        }

        return await repository.StartReviewAsync(scope, request, ct).ConfigureAwait(false);
    }

    private static string ResolveCurrentActor(HttpContext context)
    {
        if (context.Items[LoginSessionMiddleware.CurrentUserKey] is string currentUser && !string.IsNullOrWhiteSpace(currentUser))
        {
            return currentUser;
        }

        if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return context.User.Identity.Name!;
        }

        return "operator";
    }

    // Deliberately a superset of HasReconciliationMutationPermission: a profile that can act on
    // casework must be able to load the queue it acts on, and permission overrides need not match
    // the bundled roles, so ManageDirectLending cannot be assumed to arrive alongside ViewTrades.
    private static bool CanViewReconciliationBreakQueue(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
            UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance);

    private static bool CanMutateReconciliationBreakQueue(HttpContext context)
        => HasReconciliationMutationPermission(context);

    private static bool HasPermission(HttpContext context, UserPermission requiredPermission)
        => EndpointAuthorization.HasPermission(context, requiredPermission);

    private static bool HasReconciliationMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending,
            UserPermission.ModifySecurityMaster);

    private static bool HasOperationsContinuityReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewDirectLending,
            UserPermission.ViewSecurityMaster,
            UserPermission.ManageDirectLending,
            UserPermission.ModifySecurityMaster,
            UserPermission.AdminMaintenance);

    private static bool HasOperationsContinuityMutationPermission(HttpContext context)
        => HasReconciliationMutationPermission(context);

    private static bool HasFundAccountEvidenceMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending);

    private static bool HasGovernedWorkflowReopenPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);

    private static bool HasSecurityMasterOverrideApprovalPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ModifySecurityMaster);

    private static bool TryResolveCurrentUser(HttpContext context, out string currentUser)
        => EndpointAuthorization.TryResolveActor(context, out currentUser);

    private static StrategyRunStatus? ParseStrategyRunStatus(string? status)
        => Enum.TryParse<StrategyRunStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;

    private static OperationsWorkflowStatusDto? ParseOperationsWorkflowStatus(string? status)
        => Enum.TryParse<OperationsWorkflowStatusDto>(status, ignoreCase: true, out var parsed) ? parsed : null;

    private static IResult MissingOperationsPayload(string field, string message)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    private static IResult OperationsTransitionResult(OperationsTransitionResultDto result, JsonSerializerOptions jsonOptions)
    {
        if (result.Success)
        {
            return Results.Json(result, jsonOptions);
        }

        return result.ErrorCode switch
        {
            "NOT_FOUND" => Results.NotFound(result),
            "VERSION_MISMATCH" => Results.Conflict(result),
            "WORKFLOW_ALREADY_EXISTS" => Results.Conflict(result),
            "INVALID_STATE_TRANSITION" => Results.Conflict(result),
            _ => Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private static async Task<IResult> GetRunContinuityResultAsync(string runId, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        var continuityService = context.RequestServices.GetService<StrategyRunContinuityService>();
        if (continuityService is null)
        {
            return Results.Problem("Strategy run continuity service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
        }

        var detail = await continuityService.GetRunContinuityAsync(runId, context.RequestAborted).ConfigureAwait(false);
        return detail is null
            ? Results.NotFound()
            : Results.Json(new StrategyRunContinuityDto(
                detail.Run,
                detail.Lineage,
                detail.CashFlow,
                detail.Reconciliation,
                detail.ContinuityStatus), jsonOptions);
    }

    private static IResult ServeWorkstationIndex(IWebHostEnvironment environment)
    {
        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var indexPath = Path.Combine(root, "workstation", "index.html");

        return File.Exists(indexPath)
            ? Results.File(indexPath, "text/html")
            : Results.NotFound(new
            {
                error = "Workstation bundle not found.",
                message = "Build the canonical tree (src/Meridian.Ui/wwwroot/workstation) with 'npm --prefix src/Meridian.Ui/dashboard run build' before opening /workstation."
            });
    }

    private sealed record ProviderTrustRationalePayload(
        string Status,
        string TrustScore,
        string SignalSource,
        string ReasonCode,
        string RecommendedAction,
        string GateImpact);
}
