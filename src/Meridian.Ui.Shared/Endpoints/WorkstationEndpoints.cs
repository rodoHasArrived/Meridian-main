using System.Globalization;
using System.Security.Cryptography;
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
        .WithName("GetWorkstationSession");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationResearch), async (HttpContext context) =>
        {
            return await BuildStrategyPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationResearch");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategy), async (HttpContext context) =>
        {
            return await BuildStrategyPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationStrategy");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationResearchBriefing), async (HttpContext context) =>
        {
            var briefing = await BuildStrategyBriefingAsync(context).ConfigureAwait(false);
            return Results.Json(ToResearchBriefingDto(briefing), jsonOptions);
        })
        .WithName("GetWorkstationResearchBriefing")
        .Produces<ResearchBriefingDto>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyBriefing), async (HttpContext context) =>
        {
            var briefing = await BuildStrategyBriefingAsync(context).ConfigureAwait(false);
            return Results.Json(briefing, jsonOptions);
        })
        .WithName("GetWorkstationStrategyBriefing")
        .Produces<StrategyBriefingDto>(200);

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
        MapStatementConnectorEndpoints(group, jsonOptions);
        MapProviderIntegrationEndpoints(group, jsonOptions);

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
                    ct: context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetWorkstationWorkflowSummary")
        .Produces<OperatorWorkflowHomeSummary>(200)
        .Produces(403)
        .Produces(501)
        // No route-level read permission to defer to, so the ownership gate always evaluates the
        // fundProfileId query value (SEC-005 slice 3b).
        .RequireFundProfileTenantScope();

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
        .WithName("GetWorkstationWorkflowLibrary")
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

            var ingested = 0;
            foreach (var row in rows)
            {
                if (!buffer.TryIngest(row))
                {
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                }

                ingested++;
            }

            return Results.Accepted(value: new { ingested, buffered = true });
        })
        .WithName("IngestCollateralRows")
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
        .WithName("GetWorkstationWorkflowPresets")
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
        .WithName("SaveWorkstationWorkflowPreset")
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
        .WithName("UpdateWorkstationWorkflowPreset")
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
        .WithName("PinWorkstationWorkflowPreset")
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
        .WithName("MarkWorkstationWorkflowPresetUsed")
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
        .WithName("DeleteWorkstationWorkflowPreset")
        .Produces(204)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationTrading), async (Guid? fundAccountId, HttpContext context) =>
        {
            return await BuildTradingPayloadAsync(context, fundAccountId).ConfigureAwait(false);
        })
        .WithName("GetWorkstationTrading");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationTradingReadiness), async (Guid? fundAccountId, HttpContext context) =>
        {
            var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);
            return Results.Json(readiness, jsonOptions);
        })
        .WithName("GetWorkstationTradingReadiness")
        .Produces<TradingOperatorReadinessDto>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationCollateralExposure), (HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return Results.Forbid();
            }

            var service = context.RequestServices.GetRequiredService<CollateralExposureService>();
            var buffer = context.RequestServices.GetService<CollateralIngestionBuffer>();
            var rows = buffer?.DrainBatch(5_000) ?? [];
            return Results.Json(BuildCollateralExposureSnapshot(service, rows), jsonOptions);
        })
        .WithName("GetWorkstationCollateralExposure")
        .Produces<ExposureSnapshotDto>(200)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationOperatorInbox), async (Guid? fundAccountId, HttpContext context) =>
        {
            var inbox = await BuildOperatorInboxAsync(fundAccountId, context).ConfigureAwait(false);
            return Results.Json(inbox, jsonOptions);
        })
        .WithName("GetWorkstationOperatorInbox")
        .Produces<OperatorInboxDto>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationDataOperations), async (HttpContext context) =>
        {
            return await BuildDataPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationDataOperations");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationData), async (HttpContext context) =>
        {
            return await BuildDataPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationData");

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
        .WithName("PostWorkstationDataQuery")
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
        .WithName("GetWorkstationDataReplacementCost")
        .Produces<DataReplacementCostEstimate>(200)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationGovernance), async (HttpContext context) =>
        {
            return await BuildAccountingPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationGovernance");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAccounting), async (HttpContext context) =>
        {
            return await BuildAccountingPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationAccounting");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationReporting), async (HttpContext context) =>
        {
            return await BuildAccountingPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationReporting");

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationReportingStructuredExport), (
            string exportId,
            string? format,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<ReportPackRunReadService>()
                ?? new ReportPackRunReadService(new DefaultReportingTemplateCatalog());
            try
            {
                var payload = service.GetStructuredReportingExport(
                    exportId,
                    BuildReportAccessQueryContext(context));
                ApplyWorkstationStructuredExportAuditHeaders(context, payload);

                if (IsWorkstationStructuredCsvRequest(format))
                {
                    var fileName = $"{payload.Export.ExportId}-{payload.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.csv";
                    return Results.File(
                        BuildWorkstationStructuredExportCsv(payload),
                        "text/csv",
                        fileName);
                }

                if (IsWorkstationStructuredXlsxRequest(format))
                {
                    var fileName = $"{payload.Export.ExportId}-{payload.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.xlsx";
                    return Results.File(
                        BuildWorkstationStructuredExportXlsx(payload),
                        WorkstationStructuredXlsxContentType,
                        fileName);
                }

                if (IsWorkstationStructuredJsonRequest(format))
                {
                    var fileName = $"{payload.Export.ExportId}-{payload.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.json";
                    return Results.File(
                        JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions),
                        "application/json",
                        fileName);
                }

                return Results.Json(payload, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        })
        .WithName("GetWorkstationStructuredReportingExport")
        .Produces<StructuredReportingExportPayloadDto>(200)
        .Produces(200, contentType: "application/json")
        .Produces(200, contentType: "text/csv")
        .Produces(200, contentType: WorkstationStructuredXlsxContentType)
        .Produces(400)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolio), async (HttpContext context) =>
        {
            var payload = await BuildPortfolioPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolio")
        .Produces<WorkstationPortfolioPayload>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolioSummary), async (string? fundAccountId, string? strategyId, string? entity, HttpContext context) =>
        {
            var payload = await BuildPortfolioSummaryPayloadAsync(context, fundAccountId, strategyId, entity).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolioSummary")
        .Produces<WorkstationPortfolioSummaryPayload>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationPortfolioMultiAssetCoverage), async (string? fundAccountId, string? entity, string? assetClass, HttpContext context) =>
        {
            var payload = await BuildMultiAssetCoveragePayloadAsync(context, fundAccountId, entity, assetClass).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolioMultiAssetCoverage")
        .Produces<MultiAssetCoverageSummaryDto>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationAssetOperations), async (Guid securityId, HttpContext context) =>
        {
            var payload = await BuildAssetOperationsPayloadAsync(context, securityId).ConfigureAwait(false);
            return payload is null
                ? Results.NotFound()
                : Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationAssetOperations")
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
        .WithName("GetOperationsContinuitySummary");

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
        .WithName("GetOperationsContinuityApprovalPolicyMatrix")
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
        .WithName("UpsertOperationsContinuityApprovalPolicyRule")
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
        .WithName("GetOperationsContinuityCloseCalendar")
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

            var cockpit = await service
                .GetCockpitAsync(fundProfileId, ledgerBookId, fundAccountId, periodId, entityId, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(cockpit, jsonOptions);
        })
        .WithName("GetOperationsPrivateCapitalCloseCockpit")
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
        .WithName("UpsertOperationsContinuityCloseCalendarItem")
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
        .WithName("StartOperationsContinuityWorkflow")
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
        .WithName("GetOperationsContinuityDetail");

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
        .WithName("GetOperationsContinuityCloseReadiness")
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
        .WithName("GetOperationsContinuityTimeline");

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
        .WithName("ImportOperationsContinuityBrokerData")
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
        .WithName("NormalizeOperationsContinuityBrokerTransactions")
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
        .WithName("RefreshOperationsContinuityGatePosture");

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
        .WithName("ResolveOperationsContinuitySecurityMasterMappings");

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
        .WithName("ApproveOperationsContinuitySecurityMasterOverride")
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
        .WithName("BuildOperationsContinuityLedgerDraft");

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
        .WithName("ValidateOperationsContinuityLedgerDraft");

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
        .WithName("PostOperationsContinuityLedgerEntries")
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

            var service = context.RequestServices.GetService<IOperationsContinuityReconciliationBridge>();
            if (service is null)
            {
                return Results.Problem("Operations continuity reconciliation bridge is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.RunReconciliationAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("RunOperationsContinuityReconciliation");

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
        .WithName("AssignOperationsContinuityReconciliationBreak");

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

            var trustedRequest = request with { Actor = currentUser };
            var result = await service.ResolveBreakCaseAsync(workflowId, breakId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ResolveOperationsContinuityReconciliationBreak");

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
        .WithName("SubmitOperationsContinuityApproval");

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
        .WithName("ApproveOperationsContinuityWorkflow");

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
        .WithName("RejectOperationsContinuityWorkflow")
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
        .WithName("CloseOperationsContinuityWorkflow");

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
        .WithName("ReopenOperationsContinuityWorkflow")
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
        .WithName("GetOperationsContinuityBreaks");

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
        .WithName("GetOperationsContinuityLedgerPreview");

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
        .WithName("GetOperationsContinuityChecklist");

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
        .WithName("AcknowledgeOperationsContinuityChecklistTask");


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
            if (detail is not null)
            {
                await PublishProviderLedgerBreakQueueAsync(context.RequestServices, detail, context.RequestAborted).ConfigureAwait(false);
            }

            return detail is null
                ? Results.NotFound()
                : Results.Json(detail, jsonOptions);
        })
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .WithName("CreateReconciliationRun")
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
        .WithName("GetReconciliationRun")
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
        .WithName("GetLatestRunReconciliation")
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404);

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
        .WithName("GetRunReconciliationHistory")
        .Produces<IReadOnlyList<ReconciliationRunSummary>>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRuns), async (
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            return Results.Json(await service.ListStatementRunsAsync(context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("ListStatementRuns")
        .Produces<IReadOnlyList<StatementRunSummaryDto>>(200)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRuns), async (
            StatementRunCreateDto request,
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

            var trustedRequest = request with { ImportedBy = currentUser };
            var detail = await service.CreateStatementRunAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateStatementRun")
        .Produces<StatementRunDto>(201)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunById), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var detail = await service.GetStatementRunAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("GetStatementRun")
        .Produces<StatementRunDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunValidation), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var validation = await service.GetStatementRunValidationAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return validation is null ? Results.NotFound() : Results.Json(validation, jsonOptions);
        })
        .WithName("GetStatementRunValidation")
        .Produces<StatementRunValidationDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunBreaks), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationApiService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var breaks = await service.ListStatementRunBreaksAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return breaks is null ? Results.NotFound() : Results.Json(breaks, jsonOptions);
        })
        .WithName("ListStatementRunBreaks")
        .Produces<IReadOnlyList<StatementRunBreakDto>>(200)
        .Produces(404)
        .Produces(501);

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

            var trustedRequest = request with { Actor = currentUser };
            var detail = await service.ReconcileStatementRunAsync(runId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            if (detail is not null)
            {
                await PublishStatementBreakQueueAsync(context.RequestServices, service, context.RequestAborted).ConfigureAwait(false);
            }

            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("ReconcileStatementRun")
        .Produces<StatementRunDto>(200)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementExceptions), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var exceptions = await service.ListOpenExceptionsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(exceptions, jsonOptions);
        })
        .WithName("ListStatementExceptions")
        .Produces<IReadOnlyList<StatementRunExceptionDto>>(200)
        .Produces(501);
        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementBreaks), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var breaks = await service.ListOpenStatementBreaksAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(breaks, jsonOptions);
        })
        .WithName("ListOpenStatementBreaks")
        .Produces<IReadOnlyList<StatementBreakDto>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationOpenCases), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var cases = await service.ListOpenCasesAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(cases, jsonOptions);
        })
        .WithName("ListOpenReconciliationCases")
        .Produces<IReadOnlyList<ReconciliationCaseSummaryDto>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationQueueStatus), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IReconciliationApiService>();
            if (service is null)
            {
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var queueStatus = await service.ListQueueStatusAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(queueStatus, jsonOptions);
        })
        .WithName("ListReconciliationQueueStatus")
        .Produces<IReadOnlyList<ReconciliationQueueAccountStatusDto>>(200)
        .Produces(501);


        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueue), async (string? status, string? fundAccountId, Guid? ledgerBookId, HttpContext context) =>
        {
            if (!CanViewReconciliationBreakQueue(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var items = await GetBreakQueueItemsAsync(context.RequestServices, status, fundAccountId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(items, jsonOptions);
        })
        .WithName("GetReconciliationBreakQueue")
        .Produces<IReadOnlyList<ReconciliationBreakQueueItem>>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueueById), async (string breakId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.GetByIdAsync(breakId, context.RequestAborted).ConfigureAwait(false);
            return item is null ? Results.NotFound() : Results.Json(item, jsonOptions);
        })
        .WithName("GetReconciliationBreakQueueItem")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationCalibrationSummary), async (HttpContext context) =>
        {
            var asOf = DateTimeOffset.UtcNow;
            var ledgerBookId = ParseOptionalGuid(context.Request.Query["ledgerBookId"].FirstOrDefault());
            var items = await GetBreakQueueItemsAsync(context.RequestServices, status: null, fundAccountId: null, ledgerBookId: ledgerBookId, ct: context.RequestAborted).ConfigureAwait(false);
            var summary = BuildReconciliationCalibrationSummary(items, asOf);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetReconciliationCalibrationSummary")
        .Produces<ReconciliationCalibrationSummaryDto>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakAudit), async (string breakId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var history = await repository.GetAuditHistoryAsync(breakId, context.RequestAborted).ConfigureAwait(false);
            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetReconciliationBreakAudit")
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

            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var transition = await ReviewBreakAsync(context.RequestServices, request with
            {
                ReviewedBy = ResolveCurrentActor(context)
            }, context.RequestAborted).ConfigureAwait(false);
            return transition.Status switch
            {
                ReconciliationBreakQueueTransitionStatus.Success => Results.Json(transition.Item, jsonOptions),
                ReconciliationBreakQueueTransitionStatus.NotFound => Results.NotFound(),
                _ => Results.BadRequest(new { error = transition.Error ?? "Illegal transition." })
            };
        })
        .WithName("ReviewReconciliationBreak")
        .Produces<ReconciliationBreakQueueItem>(200)
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

            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var transition = await ResolveBreakAsync(context.RequestServices, request with
            {
                ResolvedBy = ResolveCurrentActor(context)
            }, context.RequestAborted).ConfigureAwait(false);
            return transition.Status switch
            {
                ReconciliationBreakQueueTransitionStatus.Success => Results.Json(transition.Item, jsonOptions),
                ReconciliationBreakQueueTransitionStatus.NotFound => Results.NotFound(),
                _ => Results.BadRequest(new { error = transition.Error ?? "Illegal transition." })
            };
        })
        .WithName("ResolveReconciliationBreak")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404);


        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakAssign), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.Assign }, context, jsonOptions).ConfigureAwait(false))
        .WithName("AssignReconciliationBreakCase")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakTransition), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.TransitionStatus }, context, jsonOptions).ConfigureAwait(false))
        .WithName("TransitionReconciliationBreakCase")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationCaseTaxonomy), () => Results.Json(FileReconciliationBreakQueueRepository.Taxonomy, jsonOptions))
        .WithName("GetReconciliationCaseTaxonomy")
        .Produces<ReconciliationTaxonomySnapshot>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComments), async (string breakId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.GetByIdAsync(breakId, context.RequestAborted).ConfigureAwait(false);
            return item is null ? Results.NotFound() : Results.Json(item.Comments ?? [], jsonOptions);
        })
        .WithName("GetReconciliationBreakComments")
        .Produces<IReadOnlyList<ReconciliationCaseComment>>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakRebuiltSnapshot), async (string breakId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.RebuildSnapshotFromAuditAsync(breakId, context.RequestAborted).ConfigureAwait(false);
            return item is null ? Results.NotFound() : Results.Json(item, jsonOptions);
        })
        .WithName("RebuildReconciliationBreakSnapshot")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComments), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.AddComment }, context, jsonOptions).ConfigureAwait(false))
        .WithName("AddReconciliationBreakComment")
        .Produces<ReconciliationBreakQueueItem>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComment), async (string breakId, string commentId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.EditComment, CommentId = commentId }, context, jsonOptions).ConfigureAwait(false))
        .WithName("EditReconciliationBreakComment")
        .Produces<ReconciliationBreakQueueItem>(200);

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
        .WithName("DeleteReconciliationBreakComment")
        .Produces<ReconciliationBreakQueueItem>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakRootCause), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.SetRootCause }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SetReconciliationBreakRootCause")
        .Produces<ReconciliationBreakQueueItem>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakResolution), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.SetResolution }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SetReconciliationBreakResolution")
        .Produces<ReconciliationBreakQueueItem>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakSignOff), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.SignOff }, context, jsonOptions).ConfigureAwait(false))
        .WithName("SignOffReconciliationBreakCase")
        .Produces<ReconciliationBreakQueueItem>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakReopen), async (string breakId, ReconciliationCaseworkCommand request, HttpContext context) =>
            await ApplyReconciliationCaseworkEndpointAsync(breakId, request with { Action = ReconciliationCaseworkAction.Reopen }, context, jsonOptions).ConfigureAwait(false))
        .WithName("ReopenReconciliationBreakCase")
        .Produces<ReconciliationBreakQueueItem>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkDryRun), async (ReconciliationBulkCaseworkRequest request, HttpContext context) =>
            await ApplyReconciliationBulkEndpointAsync(request with { DryRun = true }, context, jsonOptions).ConfigureAwait(false))
        .WithName("DryRunReconciliationBreakBulkAction")
        .Produces<ReconciliationBulkCaseworkResult>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkExecute), async (ReconciliationBulkCaseworkRequest request, HttpContext context) =>
            await ApplyReconciliationBulkEndpointAsync(request with { DryRun = false }, context, jsonOptions).ConfigureAwait(false))
        .WithName("ExecuteReconciliationBreakBulkAction")
        .Produces<ReconciliationBulkCaseworkResult>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkStatus), async (string bulkActionId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            var result = repository is null
                ? null
                : await repository.GetBulkCaseworkResultAsync(bulkActionId, context.RequestAborted).ConfigureAwait(false);

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
        .WithName("GetReconciliationBreakBulkActionStatus");

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkResult), async (string bulkActionId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await repository.GetBulkCaseworkResultAsync(bulkActionId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("GetReconciliationBreakBulkActionResult")
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

            var summary = await readService.GetLedgerSummaryAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return summary is null
                ? Results.NotFound()
                : Results.Json(summary, jsonOptions);
        })
        .WithName("GetRunLedger")
        .Produces<LedgerSummary>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsContinuity), async (string runId, HttpContext context) =>
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
        })
        .WithName("GetRunContinuity")
        .Produces<StrategyRunContinuityDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsReviewPacket), async (string runId, Guid? fundAccountId, HttpContext context) =>
        {
            var reviewPacketService = context.RequestServices.GetService<StrategyRunReviewPacketService>();
            if (reviewPacketService is null)
            {
                return Results.Problem("Strategy run review packet service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var packet = await reviewPacketService.GetAsync(runId, fundAccountId, context.RequestAborted).ConfigureAwait(false);
            return packet is null
                ? Results.NotFound()
                : Results.Json(packet, jsonOptions);
        })
        .WithName("GetRunReviewPacket")
        .Produces<StrategyRunReviewPacketDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsEquityCurve), async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var curve = await readService.GetEquityCurveAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return curve is null
                ? Results.NotFound()
                : Results.Json(curve, jsonOptions);
        })
        .WithName("GetRunEquityCurve")
        .Produces<EquityCurveSummary>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsFills), async (string runId, string? symbol, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var summary = await readService.GetFillsAsync(runId, context.RequestAborted).ConfigureAwait(false);
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
        .WithName("GetRunFills")
        .Produces<RunFillSummary>(200)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsAttribution), async (string runId, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var attribution = await readService.GetAttributionAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return attribution is null
                ? Results.NotFound()
                : Results.Json(attribution, jsonOptions);
        })
        .WithName("GetRunAttribution")
        .Produces<RunAttributionSummary>(200)
        .Produces(404)
        .Produces(501);

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

            var summary = await readService.GetLedgerSummaryAsync(runId, context.RequestAborted).ConfigureAwait(false);
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
                    NormalizeOptionalDimensionValue(ledgerBookId) ?? NormalizeOptionalDimensionValue(bookId),
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
        .WithName("GetRunLedgerTrialBalance")
        .Produces<IReadOnlyList<LedgerTrialBalanceLine>>(200)
        .Produces(404);

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

            var summary = await readService.GetLedgerSummaryAsync(runId, context.RequestAborted).ConfigureAwait(false);
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
                NormalizeOptionalDimensionValue(ledgerBookId) ?? NormalizeOptionalDimensionValue(bookId),
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
        .WithName("GetRunLedgerJournal")
        .Produces<IReadOnlyList<LedgerJournalLine>>(200)
        .Produces(404);

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
        .WithName("SearchSecurityMasterWorkstation")
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
        .WithName("GetSecurityMasterWorkstationSecurity")
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
        .WithName("GetSecurityMasterWorkstationSecurityHistory")
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
        .WithName("GetSecurityMasterWorkstationIdentityDrillIn")
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
        .WithName("GetSecurityMasterWorkstationEconomicDefinition")
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
        .WithName("GetSecurityMasterWorkstationTrustSnapshot")
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
        .WithName("GetSecurityMasterWorkstationInstrumentPassport")
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
        .WithName("BulkResolveSecurityMasterWorkstationConflicts")
        .Accepts<BulkResolveSecurityMasterConflictsRequest>("application/json")
        .Produces<BulkResolveSecurityMasterConflictsResult>(200)
        .Produces(403)
        .Produces(501);

        // --- Multi-run comparison and diff ---

        group.MapPost(WorkstationSubroute(UiApiRoutes.RunsCompare), async (RunComparisonRequest request, HttpContext context) =>
        {
            var comparisonService = context.RequestServices.GetService<StrategyRunComparisonService>();
            if (comparisonService is null)
            {
                var readService = context.RequestServices.GetService<StrategyRunReadService>();
                if (readService is null)
                    return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

                comparisonService = new StrategyRunComparisonService(readService);
            }

            if (request.RunIds is not { Count: >= 2 })
            {
                return Results.BadRequest(new { error = "At least two run IDs are required for comparison." });
            }

            if (request.RunIds.Count > MaxRunComparisonRequestIds)
            {
                return Results.BadRequest(new { error = $"A maximum of {MaxRunComparisonRequestIds} run IDs can be compared per request." });
            }

            var comparison = await comparisonService.CompareRunsAsync(request, context.RequestAborted).ConfigureAwait(false);
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
        .WithName("CompareRuns")
        .Produces<IReadOnlyList<StrategyRunComparison>>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.RunsDiff), async (RunDiffRequest request, HttpContext context) =>
        {
            var comparisonService = context.RequestServices.GetService<StrategyRunComparisonService>();
            if (comparisonService is null)
            {
                var readService = context.RequestServices.GetService<StrategyRunReadService>();
                if (readService is null)
                    return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

                comparisonService = new StrategyRunComparisonService(readService);
            }

            var diff = await comparisonService.BuildDiffAsync(request, context.RequestAborted).ConfigureAwait(false);
            if (diff is null)
            {
                return Results.NotFound(new { error = "One or both run IDs not found." });
            }

            return Results.Json(diff, jsonOptions);
        })
        .WithName("DiffRuns")
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

            var runs = await readService.GetRunsAsync(strategyId, runType, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("GetStrategyRuns")
        .WithTags("Strategies")
        .Produces<IReadOnlyList<StrategyRunSummary>>(200);

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
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("GetWorkstationRunHistory")
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

            var timeline = await readService.GetMergedTimelineAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(timeline, jsonOptions);
        })
        .WithName("GetWorkstationMergedRunTimeline")
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

            var timeline = await readService.GetLineageTimelineAsync(query, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(
                timeline.Where(static entry => entry.EventType != StrategyRunLineageEventType.ReplayVerified).ToArray(),
                jsonOptions);
        })
        .WithName("GetWorkstationRunLineageTimeline")
        .Produces<IReadOnlyList<StrategyRunLineageTimelineEntry>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsSweeps), async (int? limit, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var sweeps = await readService.GetSweepResultGroupsAsync(limit ?? 25, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(sweeps, jsonOptions);
        })
        .WithName("GetWorkstationSweepResults")
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

            var comparison = await readService.GetRunComparisonDtosAsync(runIds, ct: context.RequestAborted).ConfigureAwait(false);
            return Results.Json(comparison, jsonOptions);
        })
        .WithName("CompareStrategyRuns")
        .WithTags("Strategies")
        .Produces<IReadOnlyList<RunComparisonDto>>(200)
        .Produces(400)
        .Produces(501);

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

        // --- Cross-strategy aggregate portfolio ---

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioAggregate), (HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var positions = aggregator.GetAggregatedPositions();
            return Results.Json(positions, jsonOptions);
        })
        .WithName("GetPortfolioAggregate")
        .Produces<IReadOnlyList<AggregatedPosition>>(200)
        .Produces(503);

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioExposure), (HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var report = aggregator.GetCrossStrategyExposure();
            return Results.Json(report, jsonOptions);
        })
        .WithName("GetPortfolioExposure")
        .Produces<CrossStrategyExposureReport>(200)
        .Produces(503);

        portfolioGroup.MapGet(PortfolioSubroute(UiApiRoutes.PortfolioSymbolExposure), (string symbol, HttpContext context) =>
        {
            var aggregator = context.RequestServices.GetService<IAggregatePortfolioService>();
            if (aggregator is null)
                return Results.Problem("Aggregate portfolio service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var net = aggregator.GetNetPositionForSymbol(symbol);
            return Results.Json(net, jsonOptions);
        })
        .WithName("GetPortfolioSymbolExposure")
        .Produces<NetSymbolPosition>(200)
        .Produces(503);
        app.MapGet("/workstation", (IWebHostEnvironment environment) => ServeWorkstationIndex(environment))
            .ExcludeFromDescription();

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
        }).ExcludeFromDescription();
    }

    private static ExposureSnapshotDto BuildCollateralExposureSnapshot(
        CollateralExposureService service,
        IReadOnlyList<CollateralInputRow> rows)
    {
        var sourceRows = rows.Count == 0 ? BuildFallbackCollateralRows() : rows;
        var snapshots = service.BuildSnapshots(sourceRows);
        var breaches = service.EvaluateBreaches(snapshots);
        var breachDtos = breaches.Select(static breach => new ThresholdBreachDto(
            breach.Counterparty,
            breach.Severity.ToString(),
            breach.CoverageRatio,
            breach.Severity == ThresholdSeverity.HardBreach ? breach.HardBreachLevel : breach.EarlyWarningLevel,
            DateTimeOffset.UtcNow,
            breach.Message)).ToArray();

        var counterpartyDtos = snapshots.Select(static snapshot => new CounterpartyExposureDto(
            snapshot.Counterparty,
            snapshot.NetExposure,
            snapshot.GrossExposure,
            snapshot.CollateralBalance,
            snapshot.HaircutAdjustedCollateral,
            new MarginRequirementDto(0m, snapshot.RequiredCollateral, snapshot.RequiredCollateral),
            snapshot.HaircutAdjustedCollateral >= snapshot.RequiredCollateral,
            snapshot.ProductDecomposition.Select(static product => new ProductExposureDto(
                product.ProductType,
                product.NetExposure,
                product.GrossExposure)).ToArray())).ToArray();

        var calls = breaches
            .Where(static breach => breach.Severity == ThresholdSeverity.HardBreach)
            .Select(static breach => new CollateralCallDto(
                $"call-{NormalizeOperatorInboxToken(breach.Counterparty)}",
                breach.Counterparty,
                Math.Max(0m, breach.HardBreachLevel - breach.CoverageRatio),
                0m,
                DateTimeOffset.UtcNow.AddDays(1),
                "Open",
                breach.Message))
            .ToArray();

        var trend = Enumerable.Range(0, 12)
            .Select(index => new ExposureTrendPointDto(
                DateTimeOffset.UtcNow.AddHours(index - 11),
                snapshots.Sum(static snapshot => snapshot.NetExposure),
                snapshots.Count == 0 ? 0m : snapshots.Average(static snapshot => snapshot.CollateralCoverageRatio)))
            .ToArray();

        return new ExposureSnapshotDto(
            DateTimeOffset.UtcNow,
            rows.Count == 0 ? "micro-batch fixture" : "micro-batch buffer",
            counterpartyDtos,
            breachDtos,
            calls,
            trend);
    }

    private static IReadOnlyList<CollateralInputRow> BuildFallbackCollateralRows() =>
    [
        new(
            DateTimeOffset.UtcNow,
            "Prime-A",
            "Equity Swap",
            1_000_000m,
            -250_000m,
            50_000m,
            "cash",
            200_000m,
            100_000m)
    ];

    private static string WorkstationSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(WorkstationApiRoutePrefix, StringComparison.Ordinal)
            ? route[WorkstationApiRoutePrefix.Length..]
            : route;
    }

    private static string PortfolioSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(PortfolioApiRoutePrefix, StringComparison.Ordinal)
            ? route[PortfolioApiRoutePrefix.Length..]
            : route;
    }

    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationSessionPayload> BuildSessionPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return new WorkstationSessionPayload(
                DisplayName: "Meridian Operator",
                Role: "Strategy Lead",
                Environment: "paper",
                ActiveWorkspace: "strategy",
                CommandCount: 6,
                LatestRun: null,
                WorkspaceSummary: new WorkstationSessionWorkspaceSummary(0, 0, 0, 0, 0));
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var latest = runs.FirstOrDefault();
        var latestDetail = latest is null
            ? null
            : await readService.GetRunDetailAsync(latest.RunId, context.RequestAborted).ConfigureAwait(false);
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);

        return new WorkstationSessionPayload(
            DisplayName: BuildDisplayName(latest),
            Role: BuildRole(latest),
            Environment: MapEnvironment(latest),
            ActiveWorkspace: MapWorkspace(latest),
            CommandCount: Math.Max(6, runs.Length + activeRuns + reviewRuns),
            LatestRun: latest is null ? null : BuildRunDigest(latest, latestDetail),
            WorkspaceSummary: new WorkstationSessionWorkspaceSummary(
                TotalRuns: runs.Length,
                ActiveRuns: activeRuns,
                ReviewRuns: reviewRuns,
                LedgerCoverage: runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                PortfolioCoverage: runs.Count(static run => !string.IsNullOrWhiteSpace(run.PortfolioId))));
    }

    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationStrategyPayload> BuildStrategyPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return BuildStrategyFallbackPayload();
        }

        var runs = (await readService
                .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 6), context.RequestAborted)
                .ConfigureAwait(false))
            .ToArray();
        var runDetails = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted)))
            .ConfigureAwait(false);

        if (runs.Length == 0)
        {
            return new WorkstationStrategyPayload(
                Metrics:
                [
                    new WorkstationMetricCard("active-runs", "Active Runs", "0", "0%", "success"),
                    new WorkstationMetricCard("queued-runs", "Queued Promotions", "0", "0%", "default"),
                    new WorkstationMetricCard("review-runs", "Needs Review", "0", "0%", "warning"),
                    new WorkstationMetricCard("winning-runs", "Positive P&L", "0", "0%", "default")
                ],
                Runs: Array.Empty<WorkstationStrategyRunCard>(),
                Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
                Timeline: Array.Empty<WorkstationTimelineCard>(),
                Workspace: new WorkstationStrategyWorkspaceSummary(0, null, null, false, false, 0),
                PlotTool: BuildStrategyPlotToolPayload(Array.Empty<StrategyRunSummary>(), selectedRunIds: Array.Empty<string>()));
        }

        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var queuedPromotions = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        var winningRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs[0];

        return new WorkstationStrategyPayload(
            Metrics:
            [
                new WorkstationMetricCard("active-runs", "Active Runs", activeRuns.ToString(CultureInfo.InvariantCulture), activeRuns == 0 ? "0%" : $"+{activeRuns}", "success"),
                new WorkstationMetricCard("queued-runs", "Queued Promotions", queuedPromotions.ToString(CultureInfo.InvariantCulture), queuedPromotions == 0 ? "0%" : $"+{queuedPromotions}", "default"),
                new WorkstationMetricCard("review-runs", "Needs Review", reviewRuns.ToString(CultureInfo.InvariantCulture), reviewRuns == 0 ? "0%" : $"-{reviewRuns}", "warning"),
                new WorkstationMetricCard("winning-runs", "Positive P&L", winningRuns.ToString(CultureInfo.InvariantCulture), winningRuns == 0 ? "0%" : $"+{winningRuns}", "default")
            ],
            Runs: runs
                .Zip(runDetails, static (run, detail) => BuildStrategyRunCard(run, detail))
                .ToArray(),
            Comparisons: BuildModeComparisons(runs),
            Timeline: runs.Select(BuildTimelineCard).ToArray(),
            Workspace: new WorkstationStrategyWorkspaceSummary(
                TotalRuns: runs.Length,
                LatestRunId: latestRun.RunId,
                LatestStrategyName: latestRun.StrategyName,
                HasLedgerCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                HasPortfolioCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                PromotionCandidates: queuedPromotions),
            PlotTool: BuildStrategyPlotToolPayload(runs, selectedRunIds: Array.Empty<string>()));
    }

    private static async Task<StrategyBriefingDto> BuildStrategyBriefingAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return BuildStrategyBriefingFallback();
        }

        var runs = (await readService
                .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 10), context.RequestAborted)
                .ConfigureAwait(false))
            .ToArray();
        var details = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted)))
            .ConfigureAwait(false);

        return BuildStrategyBriefingFromRuns(runs, details);
    }

    private static StrategyBriefingDto BuildStrategyBriefingFromRuns(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details)
    {
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var promotionCandidates = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var positivePnlRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs.FirstOrDefault();
        var alertItems = BuildBriefingAlerts(runs, details);

        return new StrategyBriefingDto(
            Workspace: new StrategyBriefingWorkspaceSummary(
                TotalRuns: runs.Count,
                ActiveRuns: activeRuns,
                PromotionCandidates: promotionCandidates,
                PositivePnlRuns: positivePnlRuns,
                LatestRunId: latestRun?.RunId,
                LatestStrategyName: latestRun?.StrategyName,
                HasLedgerCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                HasPortfolioCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                Summary: latestRun is null
                    ? "Start a backtest or restore a saved run to populate the Market Briefing."
                    : $"{activeRuns} active Strategy session(s), {promotionCandidates} promotion candidate(s), and {alertItems.Count} alert(s) on the desk."),
            InsightFeed: BuildBriefingInsightFeed(runs, details, alertItems.Count),
            Watchlists: Array.Empty<WorkstationWatchlist>(),
            RecentRuns: runs
                .Zip(details, static (run, detail) => BuildBriefingRun(run, detail))
                .Take(6)
                .ToArray(),
            SavedComparisons: BuildSavedComparisons(runs),
            Alerts: alertItems,
            WhatChanged: BuildWhatChangedItems(runs));
    }

    private static StrategyBriefingDto BuildStrategyBriefingFallback()
    {
        var generatedAt = DateTimeOffset.UtcNow;
        return new StrategyBriefingDto(
            Workspace: new StrategyBriefingWorkspaceSummary(
                TotalRuns: 24,
                ActiveRuns: 6,
                PromotionCandidates: 3,
                PositivePnlRuns: 17,
                LatestRunId: "run-strategy-001",
                LatestStrategyName: "Mean Reversion FX",
                HasLedgerCoverage: true,
                HasPortfolioCoverage: true,
                Summary: "Strategy is organized around briefing context first, then run studio drill-ins."),
            InsightFeed: new InsightFeed(
                FeedId: "strategy-market-briefing",
                Title: "Pinned Insights",
                Summary: "A compact market briefing with pinned Strategy tiles, saved comparisons, and promotion posture.",
                GeneratedAt: generatedAt,
                Widgets:
                [
                    new InsightWidget(
                        WidgetId: "insight-meanrev-fx",
                        Title: "Mean Reversion FX",
                        Subtitle: "Paper run · Running",
                        Headline: "+4.2%",
                        Tone: "success",
                        Summary: "Primary paper candidate with steady fill quality and stable financing.",
                        RunId: "run-strategy-001",
                        DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, "run-strategy-001")),
                    new InsightWidget(
                        WidgetId: "insight-index-carry",
                        Title: "Index Carry Basket",
                        Subtitle: "Backtest · Completed",
                        Headline: "+2.8%",
                        Tone: "default",
                        Summary: "Pinned chart compares carry spread compression against basket returns.",
                        RunId: "run-strategy-014",
                        DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, "run-strategy-014")),
                    new InsightWidget(
                        WidgetId: "insight-vol-breakout",
                        Title: "Volatility Breakout",
                        Subtitle: "Backtest · Needs review",
                        Headline: "-0.9%",
                        Tone: "warning",
                        Summary: "Transaction-cost preview deteriorated after the most recent parameter sweep.",
                        RunId: "run-strategy-022",
                        DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, "run-strategy-022"))
                ]),
            Watchlists:
            [
                new WorkstationWatchlist(
                    WatchlistId: "wl-tech",
                    Name: "Tech Giants",
                    Symbols: ["AAPL", "MSFT", "NVDA", "AMZN", "META"],
                    SymbolCount: 5,
                    IsPinned: true,
                    SortOrder: 0,
                    AccentColor: "#4CAF50",
                    Summary: "Pinned for cross-run spread checks and financing sensitivity."),
                new WorkstationWatchlist(
                    WatchlistId: "wl-macro",
                    Name: "Macro FX",
                    Symbols: ["EURUSD", "USDJPY", "GBPUSD", "AUDUSD"],
                    SymbolCount: 4,
                    IsPinned: true,
                    SortOrder: 1,
                    AccentColor: "#2196F3",
                    Summary: "Monitored for carry baskets and mean-reversion entry timing.")
            ],
            RecentRuns:
            [
                new StrategyBriefingRun(
                    RunId: "run-strategy-001",
                    StrategyName: "Mean Reversion FX",
                    Mode: StrategyRunMode.Paper,
                    Status: StrategyRunStatus.Running,
                    Dataset: "FX Majors",
                    WindowLabel: "90d",
                    ReturnLabel: "+4.2%",
                    SharpeLabel: "1.41",
                    LastUpdatedLabel: "2m ago",
                    Notes: "Primary paper candidate with stable fill quality and healthy depth coverage.",
                    PromotionState: StrategyRunPromotionState.CandidateForLive,
                    NetPnl: 4200m,
                    TotalReturn: 0.042m,
                    FinalEquity: 104200m,
                    DrillIn: new StrategyRunDrillInLinks(
                        EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, "run-strategy-001"),
                        Fills: RunRoute(UiApiRoutes.RunsFills, "run-strategy-001"),
                        Attribution: RunRoute(UiApiRoutes.RunsAttribution, "run-strategy-001"),
                        Ledger: RunRoute(UiApiRoutes.RunsLedger, "run-strategy-001"),
                        CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, "run-strategy-001"),
                        Continuity: RunRoute(UiApiRoutes.RunsContinuity, "run-strategy-001")))
            ],
            SavedComparisons:
            [
                new StrategySavedComparison(
                    ComparisonId: "cmp-meanrev-fx",
                    StrategyName: "Mean Reversion FX",
                    ModeSummary: "Backtest -> Paper",
                    Summary: "Saved compare lane tracks readiness from completed backtest into paper execution.",
                    AnchorRunId: "run-strategy-001",
                    Modes:
                    [
                        new StrategySavedComparisonMode(
                            RunId: "run-strategy-001",
                            Mode: StrategyRunMode.Paper,
                            Status: StrategyRunStatus.Running,
                            NetPnl: 4200m,
                            TotalReturn: 0.042m,
                            DrillIn: new StrategyRunDrillInLinks(
                                EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, "run-strategy-001"),
                                Fills: RunRoute(UiApiRoutes.RunsFills, "run-strategy-001"),
                                Attribution: RunRoute(UiApiRoutes.RunsAttribution, "run-strategy-001"),
                                Ledger: RunRoute(UiApiRoutes.RunsLedger, "run-strategy-001"),
                                CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, "run-strategy-001"),
                                Continuity: RunRoute(UiApiRoutes.RunsContinuity, "run-strategy-001")))
                    ])
            ],
            Alerts:
            [
                new StrategyBriefingAlert(
                    AlertId: "alert-promotion-review",
                    Title: "Promotion review due",
                    Summary: "Mean Reversion FX is running in paper and is queued for live promotion review.",
                    Tone: "warning",
                    RunId: "run-strategy-001",
                    ActionLabel: "Review run"),
                new StrategyBriefingAlert(
                    AlertId: "alert-cost-preview",
                    Title: "Execution costs widened",
                    Summary: "Volatility Breakout now shows a weaker transaction-cost preview than the prior saved comparison.",
                    Tone: "default",
                    RunId: "run-strategy-022",
                    ActionLabel: "Open comparison")
            ],
            WhatChanged:
            [
                new StrategyWhatChangedItem(
                    ChangeId: "change-paper-ready",
                    Title: "Paper lane updated",
                    Summary: "Mean Reversion FX stayed profitable and kept full ledger continuity after the latest refresh.",
                    Category: "paper",
                    Timestamp: generatedAt.AddMinutes(-2),
                    RelativeTime: "2m ago",
                    RunId: "run-strategy-001"),
                new StrategyWhatChangedItem(
                    ChangeId: "change-backtest-failed",
                    Title: "Backtest needs review",
                    Summary: "Volatility Breakout completed with weaker returns and is now flagged for review.",
                    Category: "review",
                    Timestamp: generatedAt.AddMinutes(-18),
                    RelativeTime: "18m ago",
                    RunId: "run-strategy-022")
            ]);
    }

    private static ResearchBriefingDto ToResearchBriefingDto(StrategyBriefingDto briefing)
        => new(
            Workspace: new ResearchBriefingWorkspaceSummary(
                TotalRuns: briefing.Workspace.TotalRuns,
                ActiveRuns: briefing.Workspace.ActiveRuns,
                PromotionCandidates: briefing.Workspace.PromotionCandidates,
                PositivePnlRuns: briefing.Workspace.PositivePnlRuns,
                LatestRunId: briefing.Workspace.LatestRunId,
                LatestStrategyName: briefing.Workspace.LatestStrategyName,
                HasLedgerCoverage: briefing.Workspace.HasLedgerCoverage,
                HasPortfolioCoverage: briefing.Workspace.HasPortfolioCoverage,
                Summary: briefing.Workspace.Summary),
            InsightFeed: briefing.InsightFeed,
            Watchlists: briefing.Watchlists,
            RecentRuns: briefing.RecentRuns
                .Select(static run => new ResearchBriefingRun(
                    RunId: run.RunId,
                    StrategyName: run.StrategyName,
                    Mode: run.Mode,
                    Status: run.Status,
                    Dataset: run.Dataset,
                    WindowLabel: run.WindowLabel,
                    ReturnLabel: run.ReturnLabel,
                    SharpeLabel: run.SharpeLabel,
                    LastUpdatedLabel: run.LastUpdatedLabel,
                    Notes: run.Notes,
                    PromotionState: run.PromotionState,
                    NetPnl: run.NetPnl,
                    TotalReturn: run.TotalReturn,
                    FinalEquity: run.FinalEquity,
                    DrillIn: ToResearchDrillInLinks(run.DrillIn)))
                .ToArray(),
            SavedComparisons: briefing.SavedComparisons
                .Select(static comparison => new ResearchSavedComparison(
                    ComparisonId: comparison.ComparisonId,
                    StrategyName: comparison.StrategyName,
                    ModeSummary: comparison.ModeSummary,
                    Summary: comparison.Summary,
                    AnchorRunId: comparison.AnchorRunId,
                    Modes: comparison.Modes
                        .Select(static mode => new ResearchSavedComparisonMode(
                            RunId: mode.RunId,
                            Mode: mode.Mode,
                            Status: mode.Status,
                            NetPnl: mode.NetPnl,
                            TotalReturn: mode.TotalReturn,
                            DrillIn: ToResearchDrillInLinks(mode.DrillIn)))
                        .ToArray()))
                .ToArray(),
            Alerts: briefing.Alerts
                .Select(static alert => new ResearchBriefingAlert(
                    AlertId: alert.AlertId,
                    Title: alert.Title,
                    Summary: alert.Summary,
                    Tone: alert.Tone,
                    RunId: alert.RunId,
                    ActionLabel: alert.ActionLabel))
                .ToArray(),
            WhatChanged: briefing.WhatChanged
                .Select(static item => new ResearchWhatChangedItem(
                    ChangeId: item.ChangeId,
                    Title: item.Title,
                    Summary: item.Summary,
                    Category: item.Category,
                    Timestamp: item.Timestamp,
                    RelativeTime: item.RelativeTime,
                    RunId: item.RunId))
                .ToArray());

    private static ResearchRunDrillInLinks ToResearchDrillInLinks(StrategyRunDrillInLinks links)
        => new(
            EquityCurve: links.EquityCurve,
            Fills: links.Fills,
            Attribution: links.Attribution,
            Ledger: links.Ledger,
            CashFlows: links.CashFlows,
            Continuity: links.Continuity);

    // PR-03: returns typed DTO
    private static WorkstationStrategyPayload BuildStrategyFallbackPayload()
    {
        var fallbackCoverage = new WorkstationSecurityCoveragePayload(
            PortfolioResolved: 0,
            PortfolioMissing: 0,
            LedgerResolved: 0,
            LedgerMissing: 0,
            HasIssues: false,
            Tone: "default",
            Summary: "Security Master coverage not yet evaluated.",
            ResolvedReferences: Array.Empty<WorkstationSecurityCoverageReferencePayload>(),
            MissingReferences: Array.Empty<WorkstationSecurityCoverageGapPayload>());

        return new WorkstationStrategyPayload(
            Metrics:
            [
                new WorkstationMetricCard("active-runs", "Active Runs", "24", "+8%", "success"),
                new WorkstationMetricCard("queued-runs", "Queued Promotions", "3", "0%", "default"),
                new WorkstationMetricCard("review-runs", "Needs Review", "2", "-1%", "warning"),
                new WorkstationMetricCard("winning-runs", "Positive P&L", "17", "+4%", "default")
            ],
            Runs:
            [
                new WorkstationStrategyRunCard(
                    Id: "run-strategy-001",
                    StrategyName: "Mean Reversion FX",
                    Engine: "Meridian Native",
                    Mode: "paper",
                    Status: "Running",
                    Dataset: "FX Majors",
                    Window: "90d",
                    Pnl: "+4.2%",
                    Sharpe: "1.41",
                    LastUpdated: "2m ago",
                    Notes: "Primary paper candidate with stable fill quality and healthy depth coverage.",
                    PromotionState: null,
                    LedgerReference: null,
                    PortfolioId: null,
                    NetPnl: 4200m,
                    TotalReturn: 0.042m,
                    FinalEquity: 104200m,
                    SecurityCoverage: fallbackCoverage,
                    DrillIn: new WorkstationRunDrillInLinks(
                        EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, "run-strategy-001"),
                        Fills: RunRoute(UiApiRoutes.RunsFills, "run-strategy-001"),
                        Attribution: RunRoute(UiApiRoutes.RunsAttribution, "run-strategy-001"),
                        Ledger: null,
                        CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, "run-strategy-001"),
                        Continuity: RunRoute(UiApiRoutes.RunsContinuity, "run-strategy-001"),
                        Comparison: UiApiRoutes.RunsCompare))
            ],
            Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
            Timeline: Array.Empty<WorkstationTimelineCard>(),
            Workspace: new WorkstationStrategyWorkspaceSummary(1, "run-strategy-001", "Mean Reversion FX", false, false, 0),
            PlotTool: BuildStrategyFallbackPlotToolPayload());
    }

    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationTradingPayload> BuildTradingPayloadAsync(HttpContext context, Guid? fundAccountId = null)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var portfolio = context.RequestServices.GetService<IPortfolioState>();
        var oms = context.RequestServices.GetService<IOrderManager>();
        var brokerageConfiguration = context.RequestServices.GetService<BrokerageConfiguration>();
        var quoteCollector = context.RequestServices.GetService<QuoteCollector>();
        var tradeCollector = context.RequestServices.GetService<TradeDataCollector>();

        // When neither execution layer nor strategy run service is active, use fixture data
        if (portfolio is null && oms is null && readService is null)
        {
            return BuildTradingFallbackPayload();
        }

        // Resolve the most relevant paper run (for run-level metadata)
        StrategyRunSummary? run = null;
        if (readService is not null)
        {
            var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
            run = runs.FirstOrDefault(static candidate => candidate.Mode == StrategyRunMode.Paper) ?? runs.FirstOrDefault();
        }

        var brokerageValidation = BrokerageValidationEvaluator.Evaluate(brokerageConfiguration);

        // --- Metrics (prefer live data, fall back to run-level metrics) ---
        var realisedPnl = portfolio?.RealisedPnl ?? run?.NetPnl ?? 0m;
        var unrealisedPnl = portfolio?.UnrealisedPnl ?? 0m;
        var totalPnl = realisedPnl + unrealisedPnl;
        var openOrderCount = oms?.GetOpenOrders().Count ?? 0;
        var pnlTone = totalPnl >= 0m ? "success" : "warning";

        // --- Positions (live execution layer when available) — PR-03: typed rows ---
        // Live marks (BBO mid → last trade → cost basis) drive MarkPrice, UnrealizedPnl,
        // and Exposure so operators see real-time PnL as quotes update.
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
            // No live positions yet — show an informational placeholder row
            positions =
            [
                new WorkstationTradingPositionRow("—", "—", "—", "—", "—", "—", "—", "—", "No open positions")
            ];
        }

        // --- Open orders (live OMS when available) — PR-03: typed rows ---
        WorkstationTradingOrderRow[] openOrders;
        if (oms is not null)
        {
            openOrders = oms.GetOpenOrders().Select(static order => new WorkstationTradingOrderRow(
                OrderId: order.OrderId.ToString(),
                Symbol: order.Symbol,
                Side: order.Side.ToString(),
                Type: order.Type.ToString(),
                Quantity: order.Quantity.ToString(CultureInfo.InvariantCulture),
                LimitPrice: order.LimitPrice.HasValue ? order.LimitPrice.Value.ToString("F2", CultureInfo.InvariantCulture) : "—",
                Status: order.Status.ToString(),
                SubmittedAt: order.CreatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC")).ToArray();
        }
        else
        {
            openOrders = [];
        }

        // --- Risk state (derived from live portfolio when available) ---
        var riskState = "Healthy";
        var riskSummary = "Portfolio and order-book exposure are within configured paper thresholds.";
        IReadOnlyList<string> activeGuardrails =
        [
            "Single-name concentration cap set at 30% notional.",
            "Auto-throttle activates above 70% intraday buying power.",
            "Strategy promotion to live blocked while state is Observe or Constrained."
        ];
        var grossExposure = 0m;
        var netExposureValue = 0m;

        if (portfolio is not null)
        {
            foreach (var pos in portfolio.Positions.Values)
            {
                var mark = ResolveLiveMark(pos.Symbol, quoteCollector, tradeCollector);
                var px = mark.HasValue && mark.Value > 0m ? mark.Value : pos.AverageCostBasis;
                grossExposure += Math.Abs(pos.Quantity * px);
                netExposureValue += pos.Quantity * px;
            }
            var drawdownPct = portfolio.PortfolioValue > 0m
                ? totalPnl / portfolio.PortfolioValue
                : 0m;

            if (drawdownPct < -0.05m)
            {
                riskState = "Constrained";
                riskSummary = "Portfolio has breached the 5% drawdown threshold. Promotion to live is blocked.";
            }
            else if (drawdownPct < -0.02m)
            {
                riskState = "Observe";
                riskSummary = "Exposure nearing guardrail limits. Monitoring intraday drawdown closely.";
            }
        }
        else if (run is not null && run.NetPnl.HasValue && run.NetPnl < 0m)
        {
            riskState = "Observe";
            riskSummary = "Strategy is running at a loss. Monitoring active.";
        }

        var runtimeRisk = await ResolveRuntimeRiskDescriptorAsync(context).ConfigureAwait(false);
        if (runtimeRisk is not null)
        {
            riskState = runtimeRisk.State;
            riskSummary = runtimeRisk.Summary;
            activeGuardrails = runtimeRisk.ActiveGuardrails;
        }

        var maxDrawdownDisplay = portfolio is not null && portfolio.PortfolioValue > 0m
            ? FormatPercent(totalPnl / portfolio.PortfolioValue)
            : "—";

        var buyingPowerUsedDisplay = portfolio is not null && portfolio.BuyingPower > 0m
            ? FormatPercent(grossExposure / portfolio.BuyingPower)
            : "—";

        // --- Fills (completed orders from OMS) — PR-03: typed rows ---
        WorkstationTradingFillRow[] fills;
        if (oms is not null)
        {
            fills = oms.GetCompletedOrders(20).Select(static order => new WorkstationTradingFillRow(
                FillId: order.OrderId.ToString(),
                OrderId: order.OrderId.ToString(),
                Symbol: order.Symbol,
                Side: order.Side.ToString(),
                Quantity: order.FilledQuantity.ToString(CultureInfo.InvariantCulture),
                Price: order.AverageFillPrice.HasValue
                    ? order.AverageFillPrice.Value.ToString("F2", CultureInfo.InvariantCulture)
                    : "—",
                Venue: "Paper",
                Timestamp: (order.LastUpdatedAt ?? order.CreatedAt).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC")).ToArray();
        }
        else
        {
            fills = Array.Empty<WorkstationTradingFillRow>();
        }

        var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);

        // PR-03: return typed DTO
        return new WorkstationTradingPayload(
            Metrics:
            [
                new WorkstationMetricCard("trading-net-pnl", "Net P&L", FormatCurrency(totalPnl), totalPnl >= 0m ? "+session" : "-session", pnlTone),
                new WorkstationMetricCard("trading-open-orders", "Open Orders", openOrderCount.ToString(CultureInfo.InvariantCulture), openOrderCount == 0 ? "0" : $"+{openOrderCount}", "default"),
                new WorkstationMetricCard("trading-cash", "Cash", portfolio is not null ? FormatCurrency(portfolio.Cash) : "—", "0%", "default"),
                new WorkstationMetricCard("trading-portfolio-value", "Portfolio Value", portfolio is not null ? FormatCurrency(portfolio.PortfolioValue) : "—", "0%", "default")
            ],
            Positions: positions,
            OpenOrders: openOrders,
            Fills: fills,
            Risk: new WorkstationTradingRiskState(
                State: riskState,
                Summary: riskSummary,
                NetExposure: portfolio is not null ? FormatCurrency(netExposureValue) : "—",
                GrossExposure: portfolio is not null ? FormatCurrency(grossExposure) : "—",
                Var95: "—",
                MaxDrawdown: maxDrawdownDisplay,
                BuyingPowerUsed: buyingPowerUsedDisplay,
                ActiveGuardrails: activeGuardrails),
            Brokerage: new WorkstationTradingBrokerageState(
                Provider: brokerageValidation.GatewayDisplayName,
                Account: run is not null && !string.IsNullOrWhiteSpace(run.PortfolioId) ? run.PortfolioId : "—",
                Environment: run?.Mode == StrategyRunMode.Live ? "live" : "paper",
                Connection: portfolio is not null ? "Connected" : "Disconnected",
                LastHeartbeat: portfolio is not null ? "live" : "—",
                OrderIngress: oms is not null ? "healthy" : "—",
                FillFeed: portfolio is not null ? "healthy" : "—",
                Notes: [BuildTradingBrokerageNotes(run, portfolio is not null, brokerageConfiguration)]),
            Readiness: readiness,
            Comparisons: run is null ? Array.Empty<WorkstationModeComparisonGroup>() : BuildModeComparisons([run]),
            DrillIn: run is null ? null : BuildRunDrillInLinks(run));
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

        return readinessService.GetAsync(fundAccountId, context.RequestAborted);
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

    // PR-03: returns typed DTO
    private static WorkstationTradingPayload BuildTradingFallbackPayload()
    {
        return new WorkstationTradingPayload(
            Metrics:
            [
                new WorkstationMetricCard("trading-net-pnl", "Net P&L", "+$3,918", "+2.4%", "success"),
                new WorkstationMetricCard("trading-open-orders", "Open Orders", "5", "+1", "default"),
                new WorkstationMetricCard("trading-fills", "Fills Today", "27", "+7", "success"),
                new WorkstationMetricCard("trading-risk-state", "Risk State", "Healthy", "0%", "success")
            ],
            Positions:
            [
                new WorkstationTradingPositionRow("AAPL", "AAPL", "Long", "300", "188.22", "189.30", "+$324", "+$1,126", "$56,790"),
                new WorkstationTradingPositionRow("MSFT", "MSFT", "Long", "150", "416.10", "414.80", "-$195", "-$195", "$62,220")
            ],
            OpenOrders:
            [
                new WorkstationTradingOrderRow("PO-24812", "AMZN", "Buy", "Limit", "100", "184.00", "Working", "09:35:12 ET"),
                new WorkstationTradingOrderRow("PO-24814", "QQQ", "Sell", "Stop", "40", "442.30", "Pending Routing", "09:36:48 ET")
            ],
            Fills:
            [
                new WorkstationTradingFillRow("FL-90071", "PO-24810", "AAPL", "Buy", "50", "188.12", "NASDAQ", "09:33:04 ET"),
                new WorkstationTradingFillRow("FL-90077", "PO-24811", "MSFT", "Sell", "25", "414.88", "IEX", "09:34:26 ET")
            ],
            Risk: new WorkstationTradingRiskState(
                State: "Healthy",
                Summary: "Portfolio and order-book exposure are within configured paper thresholds.",
                NetExposure: "$119,010",
                GrossExposure: "$156,432",
                Var95: "$9,874",
                MaxDrawdown: "-0.9%",
                BuyingPowerUsed: "44%",
                ActiveGuardrails:
                [
                    "Daily loss guard set to -$12,000.",
                    "Max position notional guard set to $120,000.",
                    "Kill-switch can be engaged manually from Accounting review."
                ]),
            Brokerage: new WorkstationTradingBrokerageState(
                Provider: "Interactive Brokers",
                Account: "DU1009034",
                Environment: "paper",
                Connection: "Connected",
                LastHeartbeat: "1s ago",
                OrderIngress: "healthy (p50 19ms)",
                FillFeed: "healthy (p50 31ms)",
                Notes: ["Paper execution routing is synchronized with run-level reconciliation wiring."]),
            Readiness: BuildTradingFallbackReadiness(),
            Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
            DrillIn: null);
    }

    private static TradingOperatorReadinessDto BuildTradingFallbackReadiness()
    {
        var asOf = DateTimeOffset.UtcNow;
        return new TradingOperatorReadinessDto(
            AsOf: asOf,
            ActiveSession: null,
            Sessions: Array.Empty<TradingPaperSessionReadinessDto>(),
            Replay: null,
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 0,
                DefaultMaxPositionSize: null),
            Promotion: null,
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1-trust-gate",
                Status: "Blocked",
                ReadyForOperatorReview: false,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "missing",
                GeneratedAt: asOf,
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 0,
                ReadySampleCount: 0,
                ValidatedEvidenceDocumentCount: 0,
                RequiredOwners: Array.Empty<string>(),
                Blockers: ["Trading readiness service is unavailable in fallback mode."],
                Detail: "Trading readiness is unavailable in fallback mode."),
            BrokerageSync: null,
            WorkItems: Array.Empty<OperatorWorkItemDto>(),
            Warnings: ["Trading readiness service is unavailable in fallback mode."])
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Blocked,
            ReadyForPaperOperation = false,
            SnapshotMaterializedAt = asOf,
            SnapshotVersion = "fallback-unavailable"
        };
    }

    private static async Task<WorkstationDataPayload> BuildDataPayloadAsync(HttpContext context)
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
            return BuildDataFallbackPayload(kernelObservability);
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

    private static string? NormalizeOptionalDimensionValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        var externalGlDimensionKey = NormalizeOptionalDimensionValue(query["externalGlDimensionKey"].FirstOrDefault());
        var externalGlDimensionValue = NormalizeOptionalDimensionValue(query["externalGlDimensionValue"].FirstOrDefault());
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

    private static WorkstationDataPayload BuildDataFallbackPayload(KernelObservabilitySnapshot? kernelObservability = null)
    {
        var interactiveBrokers = BuildFallbackDataProviderRecord(
            providerId: "interactivebrokers",
            displayName: "Interactive Brokers",
            status: "Healthy",
            capability: "Execution + fills",
            latency: "21ms p50",
            note: "Paper adapter routing is available.",
            trustScore: "100%",
            signalSource: "Provider baseline health snapshot",
            reasonCode: "HEALTHY_BASELINE",
            recommendedAction: "Continue monitoring provider health; no DK1 action is required.",
            gateImpact: "Normal operation");
        var polygon = BuildFallbackDataProviderRecord(
            providerId: "polygon",
            displayName: "Polygon",
            status: "Healthy",
            capability: "Streaming equities",
            latency: "16ms p50",
            note: "Realtime subscriptions are steady.",
            trustScore: "100%",
            signalSource: "Provider baseline health snapshot",
            reasonCode: "HEALTHY_BASELINE",
            recommendedAction: "Continue monitoring provider health; no DK1 action is required.",
            gateImpact: "Normal operation");
        var databento = BuildFallbackDataProviderRecord(
            providerId: "databento",
            displayName: "Databento",
            status: "Warning",
            capability: "Historical replay",
            latency: "69ms p50",
            note: "Replay queue is elevated but within tolerance.",
            trustScore: "86%",
            signalSource: "Latency monitor",
            reasonCode: "LATENCY_REGRESSION",
            recommendedAction: "Delay operator promotion actions; review latency trend and compare against baseline window.",
            gateImpact: "Watch");

        return new WorkstationDataPayload(
            Metrics:
            [
                new WorkstationMetricCard("providers-healthy", "Providers Healthy", "4", "0", "success"),
                new WorkstationMetricCard("backfills-running", "Backfills Running", "2", "+1", "default"),
                new WorkstationMetricCard("exports-ready", "Exports Ready", "3", "+1", "success"),
                new WorkstationMetricCard("ops-review", "Needs Review", "1", "+1", "warning"),
                new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), FormatKernelJumpAlertDelta(kernelObservability), GetKernelJumpAlertTone(kernelObservability))
            ],
            Providers: [interactiveBrokers, polygon, databento],
            Backfills:
            [
                new WorkstationDataBackfillRecord("BF-1038", "US equities / 30d", "Databento", "Running", "58%", "3m ago"),
                new WorkstationDataBackfillRecord("BF-1040", "FX majors / 14d", "Polygon", "Queued", "0%", "6m ago")
            ],
            Exports:
            [
                new WorkstationDataExportRecord("EX-2196", "python-pandas", "strategy pack", "Ready", "118k", "7m ago"),
                new WorkstationDataExportRecord("EX-2198", "postgresql", "ops warehouse", "Attention", "42k", "9m ago")
            ],
            UploadTemplates: BuildDataUploadTemplateCatalog(),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
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
            ActiveGuardrails: activeGuardrails);

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

        return await service
            .GetCoverageAsync(fundAccountId, entity, assetClass, context.RequestAborted)
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

    private static async Task<WorkstationAccountingPayload> BuildAccountingPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var kernelObservability = context.RequestServices.GetService<KernelObservabilityService>()?.GetSnapshot();
        var requestedLedgerBookId = ParseOptionalGuid(context.Request.Query["ledgerBookId"].FirstOrDefault());
        var manualJournalWorkbench = await BuildManualJournalWorkbenchPayloadAsync(context).ConfigureAwait(false);
        if (readService is null)
        {
            return BuildAccountingFallbackPayload(kernelObservability, manualJournalWorkbench, requestedLedgerBookId);
        }

        var allRuns = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var runs = allRuns.Take(6).ToArray();
        if (runs.Length == 0)
        {
            var reporting = BuildReportingPayload(context);
            // PR-03: return typed DTO
            return new WorkstationAccountingPayload(
                Metrics:
                [
                    new WorkstationMetricCard("open-breaks", "Open Breaks", "0", "0%", "success"),
                    new WorkstationMetricCard("timing-drift", "Timing Drift", "0", "0%", "default"),
                    new WorkstationMetricCard("security-gaps", "Security Gaps", "0", "0%", "success"),
                    new WorkstationMetricCard("audit-ready", "Audit Ready", "0", "0%", "default"),
                    new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
                ],
                ReconciliationQueue: Array.Empty<WorkstationAccountingRunRecord>(),
                BreakQueue: Array.Empty<ReconciliationBreakQueueItem>(),
                Workspace: new WorkstationAccountingWorkspaceSummary(0, 0, 0, 0, 0),
                CashFlow: BuildAccountingWorkspaceCashFlowSummary(Array.Empty<StrategyRunDetail?>()),
                Reporting: reporting,
                ControlCenter: BuildAccountingControlCenterPayload(Array.Empty<ReconciliationBreakQueueItem>(), reporting),
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
        await SeedBreakQueueAsync(context.RequestServices, runs, reconciliations, context.RequestAborted).ConfigureAwait(false);

        var openBreaks = reconciliations.Sum(static detail => detail?.Summary.OpenBreakCount ?? 0);
        var timingDriftRuns = reconciliations.Count(static detail => detail?.Summary.HasTimingDrift == true);
        var runsWithBreaks = reconciliations.Count(static detail => (detail?.Summary.BreakCount ?? 0) > 0);
        var runsWithSecurityIssues = details.Count(static detail =>
            (detail?.Portfolio?.SecurityMissingCount ?? 0) > 0 ||
            (detail?.Ledger?.SecurityMissingCount ?? 0) > 0);
        var auditReadyRuns = runs.Count(static run => !string.IsNullOrWhiteSpace(run.AuditReference)) - runsWithBreaks;
        var breakQueueItems = await GetBreakQueueItemsAsync(context.RequestServices, status: null, fundAccountId: null, ledgerBookId: requestedLedgerBookId, ct: context.RequestAborted).ConfigureAwait(false);
        var reportingPayload = BuildReportingPayload(context);
        var scopedOpenBreaks = requestedLedgerBookId.HasValue
            ? breakQueueItems.Count(static item => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview)
            : openBreaks;

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

    // PR-03: returns typed DTO
    private static WorkstationAccountingPayload BuildAccountingFallbackPayload(
        KernelObservabilitySnapshot? kernelObservability = null,
        ManualJournalEntryWorkbenchDto? manualJournalWorkbench = null,
        Guid? requestedLedgerBookId = null)
    {
        if (requestedLedgerBookId.HasValue)
        {
            var scopedReporting = BuildReportingPayload();
            return new WorkstationAccountingPayload(
                Metrics:
                [
                    new WorkstationMetricCard("open-breaks", "Open Breaks", "0", "0%", "success"),
                    new WorkstationMetricCard("timing-drift", "Timing Drift", "0", "0%", "default"),
                    new WorkstationMetricCard("security-gaps", "Security Gaps", "0", "0%", "success"),
                    new WorkstationMetricCard("audit-ready", "Audit Ready", "0", "0%", "default"),
                    new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
                ],
                ReconciliationQueue: Array.Empty<WorkstationAccountingRunRecord>(),
                BreakQueue: Array.Empty<ReconciliationBreakQueueItem>(),
                Workspace: new WorkstationAccountingWorkspaceSummary(0, 0, 0, 0, 0),
                CashFlow: BuildAccountingWorkspaceCashFlowSummary(Array.Empty<StrategyRunDetail?>()),
                Reporting: scopedReporting,
                ControlCenter: BuildAccountingControlCenterPayload(Array.Empty<ReconciliationBreakQueueItem>(), scopedReporting),
                KernelObservability: BuildKernelObservabilityPayload(kernelObservability),
                ManualJournalWorkbench: manualJournalWorkbench);
        }

        var metricsCards = new WorkstationMetricCard[]
        {
            new("open-breaks", "Open Breaks", "4", "0%", "warning"),
            new("timing-drift", "Timing Drift", "1", "0%", "warning"),
            new("security-gaps", "Security Gaps", "2", "0%", "warning"),
            new("audit-ready", "Audit Ready", "9", "0%", "success"),
            new("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
        };
        var fallbackSecurityCoverage = new WorkstationSecurityCoveragePayload(
            PortfolioResolved: 14,
            PortfolioMissing: 1,
            LedgerResolved: 12,
            LedgerMissing: 1,
            HasIssues: true,
            Tone: "warning",
            Summary: "26 references mapped, 2 unresolved.",
            ResolvedReferences:
            [
                new WorkstationSecurityCoverageReferencePayload(
                    Source: "portfolio",
                    Symbol: "AAPL",
                    AccountName: null,
                    SecurityId: "security-aapl",
                    DisplayName: "Apple Inc.",
                    AssetClass: "Equity",
                    SubType: null,
                    Currency: "USD",
                    Status: "Active",
                    PrimaryIdentifier: "AAPL",
                    CoverageStatus: "Resolved",
                    CoverageReason: null,
                    MatchedIdentifierKind: "Ticker",
                    MatchedIdentifierValue: "AAPL",
                    MatchedProvider: null)
            ],
            MissingReferences:
            [
                new WorkstationSecurityCoverageGapPayload(
                    Source: "portfolio",
                    Symbol: "XYZ",
                    AccountName: null,
                    Reason: "Portfolio position is missing a Security Master match."),
                new WorkstationSecurityCoverageGapPayload(
                    Source: "ledger",
                    Symbol: "XYZ",
                    AccountName: "Securities",
                    Reason: "Ledger coverage is missing a Security Master match.")
            ]);
        var reconciliationQueue = new WorkstationAccountingRunRecord[]
        {
            new(
                RunId: "gov-run-001",
                StrategyName: "Global Macro Overlay",
                Mode: "paper",
                Status: "Completed",
                LastUpdated: "12m ago",
                AuditReference: "audit-gov-run-001",
                LedgerReference: null,
                PortfolioId: null,
                BreakCount: 2,
                OpenBreakCount: 2,
                ReconciliationStatus: "BreaksOpen",
                Governance: new WorkstationAccountingRunGovernancePayload(
                    HasAuditTrail: true,
                    HasPortfolio: true,
                    HasLedger: true,
                    DatasetReference: null,
                    FeedReference: null),
                SecurityCoverage: fallbackSecurityCoverage,
                CashFlow: new WorkstationAccountingRunCashFlowPayload(
                    CashBalance: 1_250_000m,
                    LedgerCashBalance: 1_247_500m,
                    CashVariance: -2_500m,
                    Financing: 12_500m,
                    RealizedPnl: 42_000m,
                    UnrealizedPnl: 18_000m,
                    JournalEntryCount: 24,
                    Tone: "warning",
                    Summary: "Cash and ledger balances diverge and should be reviewed."),
                LatestReconciliation: new WorkstationAccountingRunReconciliationPayload(
                    ReconciliationRunId: null,
                    BreakCount: 2,
                    OpenBreakCount: 2,
                    MatchCount: 0,
                    HasTimingDrift: false,
                    SecurityIssueCount: 2,
                    HasSecurityCoverageIssues: true,
                    LastUpdated: "15m ago",
                    Tone: "warning"),
                KernelObservability: BuildKernelObservabilityPayload(kernelObservability))
        };
        var breakQueue = new ReconciliationBreakQueueItem[]
        {
            new ReconciliationBreakQueueItem(
                BreakId: "BRK-gov-run-001-1",
                RunId: "gov-run-001",
                StrategyName: "Global Macro Overlay",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 2500m,
                Reason: "Cash variance exceeds configured tolerance.",
                AssignedTo: null,
                DetectedAt: DateTimeOffset.UtcNow.AddMinutes(-18),
                LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-18),
                Severity: ReconciliationBreakSeverity.Critical,
                ExceptionRoute: "accounting-variance-escalation",
                ToleranceProfileId: "critical-zero-tolerance",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Accounting sign-off",
                SignoffStatus: "pending-signoff"),
            new ReconciliationBreakQueueItem(
                BreakId: "BRK-gov-run-001-2",
                RunId: "gov-run-001",
                StrategyName: "Global Macro Overlay",
                Category: ReconciliationBreakCategory.ClassificationGap,
                Status: ReconciliationBreakQueueStatus.InReview,
                Variance: 0m,
                Reason: "Security Master coverage is missing for XYZ.",
                AssignedTo: "ops.gov",
                DetectedAt: DateTimeOffset.UtcNow.AddMinutes(-16),
                LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-8),
                ReviewedBy: "ops.gov",
                ReviewedAt: DateTimeOffset.UtcNow.AddMinutes(-8),
                ResolutionNote: "Investigating ticker reclassification.",
                Severity: ReconciliationBreakSeverity.Medium,
                ExceptionRoute: "security-master-accounting-review",
                ToleranceProfileId: "coverage-classification-review",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Accounting analyst",
                SignoffStatus: "in-review")
        };
        var reporting = BuildReportingPayload();
        return new WorkstationAccountingPayload(
            Metrics: metricsCards,
            ReconciliationQueue: reconciliationQueue,
            BreakQueue: breakQueue,
            Workspace: new WorkstationAccountingWorkspaceSummary(12, 9, 10, 4, 2),
            CashFlow: new WorkstationAccountingCashFlowSummaryPayload(
                TotalCash: 2_450_000m,
                TotalLedgerCash: 2_447_500m,
                NetVariance: -2_500m,
                TotalFinancing: 12_500m,
                RunsWithCashSignals: 9,
                RunsWithCashVariance: 1,
                Tone: "warning",
                Summary: "Cash-flow coverage is available for 9 runs; 1 run needs variance review."),
            Reporting: reporting,
            ControlCenter: BuildAccountingControlCenterPayload(breakQueue, reporting),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability),
            ManualJournalWorkbench: manualJournalWorkbench);
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

    private static WorkstationReportingPayload BuildReportingPayload(HttpContext? context = null)
        => context?.RequestServices.GetService<ReportPackRunReadService>()?.BuildPayload(BuildReportAccessQueryContext(context))
           ?? ReportPackRunReadService.BuildFallbackPayload();

    private static ReportAccessQueryContext BuildReportAccessQueryContext(HttpContext context)
    {
        var actor = EndpointAuthorization.TryResolveActor(context, out var resolvedActor)
            ? resolvedActor
            : null;
        return new ReportAccessQueryContext(
            ActorPrincipalId: actor,
            GroupPrincipalIds: EndpointAuthorization.ResolveReportGroupPrincipalIds(context),
            CompanyId: EndpointAuthorization.ResolveCompanyId(context),
            HasGlobalOverride: EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance));
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

    private static string BuildDisplayName(StrategyRunSummary? latest)
        => latest is null ? "Meridian Operator" : $"{latest.StrategyName} Desk";

    private static string BuildRole(StrategyRunSummary? latest)
        => latest is null
            ? "Strategy Lead"
            : latest.Mode == StrategyRunMode.Live
                ? "Live Operations"
                : "Strategy Lead";

    private static string MapEnvironment(StrategyRunSummary? latest)
        => latest?.Mode switch
        {
            StrategyRunMode.Live => "live",
            StrategyRunMode.Paper => "paper",
            StrategyRunMode.Backtest => "research",
            _ => "paper"
        };

    private static string MapWorkspace(StrategyRunSummary? latest)
        => latest?.Promotion?.State switch
        {
            StrategyRunPromotionState.LiveManaged => "accounting",
            StrategyRunPromotionState.CandidateForLive => "trading",
            StrategyRunPromotionState.CandidateForPaper => "strategy",
            _ => latest?.Mode == StrategyRunMode.Live ? "trading" : "strategy"
        };

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

    private static async Task SeedBreakQueueAsync(
        IServiceProvider services,
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<ReconciliationRunDetail?> reconciliations,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        await SeedBreakQueueAsync(repository, runs, reconciliations, ct).ConfigureAwait(false);
    }

    private static async Task SeedBreakQueueAsync(
        IReconciliationBreakQueueRepository? repository,
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<ReconciliationRunDetail?> reconciliations,
        CancellationToken ct)
    {
        if (repository is null)
        {
            return;
        }

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var reconciliation = i < reconciliations.Count ? reconciliations[i] : null;
            if (reconciliation is null)
            {
                continue;
            }

            foreach (var reconciliationBreak in reconciliation.Breaks)
            {
                var breakId = $"{run.RunId}:{reconciliationBreak.CheckId}";
                var now = DateTimeOffset.UtcNow;
                var routing = ResolveReconciliationExceptionRouting(
                    reconciliationBreak.Category,
                    reconciliationBreak.Severity,
                    Math.Abs(reconciliationBreak.Variance));
                await repository.CreateIfMissingAsync(
                    new ReconciliationBreakQueueItem(
                        BreakId: breakId,
                        RunId: run.RunId,
                        StrategyName: run.StrategyName,
                        Category: reconciliationBreak.Category,
                        Status: ReconciliationBreakQueueStatus.Open,
                        Variance: Math.Abs(reconciliationBreak.Variance),
                        Reason: reconciliationBreak.Reason,
                        AssignedTo: null,
                        DetectedAt: now,
                        LastUpdatedAt: now,
                        Severity: reconciliationBreak.Severity,
                        ExceptionRoute: routing.ExceptionRoute,
                        ToleranceProfileId: routing.ToleranceProfileId,
                        ToleranceBand: routing.ToleranceBand,
                        RequiredSignoffRole: routing.RequiredSignoffRole,
                        SignoffStatus: routing.SignoffStatus,
                        FundAccountId: run.FundProfileId,
                        ExplainabilitySummary: reconciliationBreak.Reason,
                        RoutingTarget: "/accounting/reconciliation",
                        RoutingDetail: $"Review reconciliation break {breakId} in accounting queue.",
                        RecommendedAction: "ReviewAndResolve",
                        SourceType: "provider-ledger",
                        SourceSystem: "provider-ledger-reconciliation",
                        SourceReference: reconciliationBreak.CheckId,
                        SourceBreakId: reconciliationBreak.CheckId,
                        SourceFingerprint: ComputeReconciliationSourceFingerprint("provider-ledger", run.RunId, reconciliationBreak.CheckId, reconciliationBreak.Category.ToString(), reconciliationBreak.Reason, reconciliationBreak.Variance.ToString(CultureInfo.InvariantCulture)),
                        LedgerBookId: null),
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task SeedStatementBreakQueueAsync(
        IReconciliationBreakQueueRepository repository,
        IReadOnlyList<StatementBreakDto> statementBreaks,
        CancellationToken ct)
    {
        foreach (var statementBreak in statementBreaks.Where(static item => IsOpenStatementBreak(item.Status)))
        {
            var item = MapStatementBreakToQueueItem(statementBreak);
            // Re-key any case seeded under the pre-migration fingerprint id onto the current id so a
            // fingerprint-input change does not duplicate cases or orphan their casework history.
            var previousBreakId = $"statement:{ComputeStatementBreakLegacyFingerprint(statementBreak)}";
            await repository.CreateOrMigrateAsync(item, previousBreakId, ct).ConfigureAwait(false);
        }
    }

    private static async Task PublishProviderLedgerBreakQueueAsync(
        IServiceProvider services,
        ReconciliationRunDetail detail,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        var readService = services.GetService<StrategyRunReadService>();
        if (repository is null || readService is null)
        {
            return;
        }

        var runs = await readService.GetRunsAsync(ct: ct).ConfigureAwait(false);
        var run = runs.FirstOrDefault(candidate =>
            string.Equals(candidate.RunId, detail.Summary.RunId, StringComparison.OrdinalIgnoreCase));
        if (run is null)
        {
            return;
        }

        await SeedBreakQueueAsync(repository, [run], [detail], ct).ConfigureAwait(false);
    }

    private static async Task PublishStatementBreakQueueAsync(
        IServiceProvider services,
        IReconciliationApiService statementService,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        if (repository is null)
        {
            return;
        }

        var statementBreaks = await statementService.ListOpenStatementBreaksAsync(ct).ConfigureAwait(false);
        await SeedStatementBreakQueueAsync(repository, statementBreaks, ct).ConfigureAwait(false);
    }

    private static async Task EnsureBreakQueueSeededAsync(IServiceProvider services, CancellationToken ct)
    {
        var readService = services.GetService<StrategyRunReadService>();
        var reconciliationService = services.GetService<IReconciliationRunService>();
        var statementService = services.GetService<IReconciliationApiService>();
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, ct).ConfigureAwait(false);
    }

    private static Task EnsureBreakQueueSeededAsync(
        StrategyRunReadService? readService,
        IReconciliationRunService? reconciliationService,
        IReconciliationBreakQueueRepository? repository,
        CancellationToken ct)
        => EnsureBreakQueueSeededAsync(readService, reconciliationService, null, repository, ct);

    private static async Task EnsureBreakQueueSeededAsync(
        StrategyRunReadService? readService,
        IReconciliationRunService? reconciliationService,
        IReconciliationApiService? statementService,
        IReconciliationBreakQueueRepository? repository,
        CancellationToken ct)
    {
        if (readService is not null && reconciliationService is not null)
        {
            var runs = await readService.GetRunsAsync(ct: ct).ConfigureAwait(false);
            if (runs.Count > 0)
            {
                var reconciliations = await Task.WhenAll(
                    runs.Select(run => reconciliationService.GetLatestForRunAsync(run.RunId, ct))).ConfigureAwait(false);
                await SeedBreakQueueAsync(repository, runs, reconciliations, ct).ConfigureAwait(false);
            }
        }

        if (repository is not null && statementService is not null)
        {
            var statementBreaks = await statementService.ListOpenStatementBreaksAsync(ct).ConfigureAwait(false);
            await SeedStatementBreakQueueAsync(repository, statementBreaks, ct).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueItemsAsync(
        IServiceProvider services,
        string? status,
        string? fundAccountId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        return await GetBreakQueueItemsAsync(repository, status, fundAccountId, ledgerBookId, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueItemsAsync(
        IReconciliationBreakQueueRepository? repository,
        string? status,
        string? fundAccountId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        if (repository is null)
        {
            return [];
        }

        ReconciliationBreakQueueStatus? parsed = null;
        if (Enum.TryParse<ReconciliationBreakQueueStatus>(status, ignoreCase: true, out var statusValue))
        {
            parsed = statusValue;
        }

        var items = await repository.GetAllAsync(parsed, ct).ConfigureAwait(false);
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

    private static decimal CalculateAutoMatchRate(int totalBreakCount, int activeBreakCount)
        => totalBreakCount <= 0 ? 1m : decimal.Round((decimal)Math.Max(0, totalBreakCount - activeBreakCount) / totalBreakCount, 4);

    private static decimal CalculateT0ClosureRate(int totalBreakCount, int resolvedBreakCount, int dismissedBreakCount)
        => totalBreakCount <= 0 ? 1m : decimal.Round((decimal)(resolvedBreakCount + dismissedBreakCount) / totalBreakCount, 4);

    private static ReconciliationCalibrationProfileSummaryDto BuildCalibrationProfileSummary(
        IGrouping<(string Profile, string Route), ReconciliationBreakQueueItem> group)
    {
        var items = group.ToArray();
        var toleranceBands = items
            .Where(static item => item.ToleranceBand.HasValue)
            .Select(static item => item.ToleranceBand!.Value)
            .ToArray();

        return new ReconciliationCalibrationProfileSummaryDto(
            ToleranceProfileId: group.Key.Profile,
            ExceptionRoute: group.Key.Route,
            HighestSeverity: items
                .OrderByDescending(static item => item.Severity)
                .First()
                .Severity,
            MaxToleranceBand: toleranceBands.Length == 0 ? null : toleranceBands.Max(),
            TotalBreakCount: items.Length,
            OpenBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open),
            InReviewBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview),
            ResolvedBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved),
            DismissedBreakCount: items.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed),
            PendingSignoffCount: items.Count(static item => RequiresCalibrationSignoff(item)),
            SignedOffCount: items.Count(static item => IsSignedOff(item)),
            LastUpdatedAt: items
                .OrderByDescending(static item => item.LastUpdatedAt)
                .First()
                .LastUpdatedAt);
    }

    private static ReconciliationCalibrationStatusDto DetermineReconciliationCalibrationStatus(
        int totalBreakCount,
        int activeBreakCount,
        int criticalOpenBreakCount,
        int pendingSignoffCount,
        int missingCalibrationMetadataCount)
    {
        if (totalBreakCount == 0)
        {
            return ReconciliationCalibrationStatusDto.Ready;
        }

        if (criticalOpenBreakCount > 0 || missingCalibrationMetadataCount > 0)
        {
            return ReconciliationCalibrationStatusDto.Blocked;
        }

        return activeBreakCount > 0 || pendingSignoffCount > 0
            ? ReconciliationCalibrationStatusDto.ReviewRequired
            : ReconciliationCalibrationStatusDto.Ready;
    }

    private static string BuildReconciliationCalibrationSummaryText(
        ReconciliationCalibrationStatusDto status,
        int totalBreakCount,
        int activeBreakCount,
        int criticalOpenBreakCount,
        int pendingSignoffCount,
        int missingCalibrationMetadataCount,
        int profileCount)
    {
        if (totalBreakCount == 0)
        {
            return "No reconciliation breaks require calibration.";
        }

        if (missingCalibrationMetadataCount > 0)
        {
            return $"{missingCalibrationMetadataCount} reconciliation break(s) are missing tolerance or sign-off metadata.";
        }

        if (criticalOpenBreakCount > 0)
        {
            return $"{criticalOpenBreakCount} critical reconciliation break(s) block calibration sign-off.";
        }

        if (status == ReconciliationCalibrationStatusDto.ReviewRequired)
        {
            return $"{activeBreakCount} reconciliation break(s) need review across {profileCount} tolerance profile(s); {pendingSignoffCount} sign-off item(s) remain open.";
        }

        return "All reconciliation breaks are resolved or dismissed; calibration is ready for accounting sign-off.";
    }

    private static bool HasMissingCalibrationMetadata(ReconciliationBreakQueueItem item)
        => (item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview) &&
           (string.IsNullOrWhiteSpace(item.ExceptionRoute) ||
            string.IsNullOrWhiteSpace(item.ToleranceProfileId) ||
            !item.ToleranceBand.HasValue ||
            string.IsNullOrWhiteSpace(item.RequiredSignoffRole) ||
            string.IsNullOrWhiteSpace(item.SignoffStatus));

    private static bool RequiresCalibrationSignoff(ReconciliationBreakQueueItem item)
        => (item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview) &&
           !IsTerminalCalibrationSignoff(item.SignoffStatus);

    private static bool IsTerminalCalibrationSignoff(string? signoffStatus)
        => string.Equals(signoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(signoffStatus, "dismissed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(signoffStatus, "monitor", StringComparison.OrdinalIgnoreCase);

    private static bool IsSignedOff(ReconciliationBreakQueueItem item)
        => string.Equals(item.SignoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase) ||
           item.SignoffHistory?.Count > 0;

    private static string NormalizeCalibrationValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;


    private static async Task<IResult> ApplyReconciliationCaseworkEndpointAsync(
        string breakId,
        ReconciliationCaseworkCommand request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (!CanMutateReconciliationBreakQueue(context))
        {
            return EndpointHelpers.Forbidden();
        }

        if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
        }

        if (!TryResolveCurrentUser(context, out var currentUser))
        {
            return Results.Unauthorized();
        }

        var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
        if (repository is null)
        {
            return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
        }

        var trusted = request with { Actor = currentUser, Source = string.IsNullOrWhiteSpace(request.Source) ? "workstation-api" : request.Source };
        var transition = await repository.ApplyCaseworkCommandAsync(trusted, context.RequestAborted).ConfigureAwait(false);
        return transition.Status switch
        {
            ReconciliationBreakQueueTransitionStatus.Success => Results.Json(transition.Item, jsonOptions),
            ReconciliationBreakQueueTransitionStatus.NotFound => Results.NotFound(),
            ReconciliationBreakQueueTransitionStatus.Conflict => Results.Conflict(new { error = transition.Error ?? "Version conflict.", currentState = transition.Item?.LifecycleState.ToString(), currentVersion = transition.Item?.Version }),
            _ => Results.BadRequest(new { error = transition.Error ?? "Invalid reconciliation casework command.", code = transition.ErrorCode.ToString(), validation = transition.Validation })
        };
    }

    private static async Task<IResult> ApplyReconciliationBulkEndpointAsync(
        ReconciliationBulkCaseworkRequest request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (!CanMutateReconciliationBreakQueue(context))
        {
            return EndpointHelpers.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "Idempotency key is required." });
        }

        if (request.BreakIds.Count > request.MaxCaseCount)
        {
            return Results.BadRequest(new { error = $"Bulk action exceeds max case count {request.MaxCaseCount}." });
        }

        if (!TryResolveCurrentUser(context, out var currentUser))
        {
            return Results.Unauthorized();
        }

        var repository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
        if (repository is null)
        {
            return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
        }

        var trusted = request with { Actor = currentUser, Source = string.IsNullOrWhiteSpace(request.Source) ? "workstation-api" : request.Source };
        var result = await repository.ApplyBulkCaseworkAsync(trusted, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result, jsonOptions);
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ReviewBreakAsync(
        IServiceProvider services,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        return await ReviewBreakAsync(repository, request, ct).ConfigureAwait(false);
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ReviewBreakAsync(
        IReconciliationBreakQueueRepository? repository,
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

        return await repository.StartReviewAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ResolveBreakAsync(
        IServiceProvider services,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
        return await ResolveBreakAsync(repository, request, ct).ConfigureAwait(false);
    }

    private static async Task<ReconciliationBreakQueueTransitionResult> ResolveBreakAsync(
        IReconciliationBreakQueueRepository? repository,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct)
    {
        if (repository is null)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.NotFound,
                Item: null,
                Error: "Reconciliation break queue repository is not registered.");
        }

        return await repository.ResolveAsync(request, ct).ConfigureAwait(false);
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

    private static bool CanViewReconciliationBreakQueue(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewTrades,
            UserPermission.ViewSecurityMaster,
            UserPermission.ModifySecurityMaster,
            UserPermission.AdminMaintenance);

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
                message = "Build src/Meridian.Ui/dashboard before opening /workstation."
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
